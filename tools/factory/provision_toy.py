#!/usr/bin/env python3
"""Factory station: mint a toy's identity and burn it, one unit at a time.

WHAT THIS REPLACES. Every toy used to advertise the SAME BLE pairing code
("areg-pair") and the device id/key were hand-copied from a curl response
into config.h for a one-shot AREG_PROVISION_IDENTITY_ONCE bench burn — fine
for a bench of one, not a production line. This script does the whole run:

  1. Register the toy against the backend (POST /api/devices/register,
     provisioning-secret gated) -> {deviceId, apiKey, claimCode, pop}.
  2. Build an NVS partition image carrying id / key / pop, using Espressif's
     OWN nvs_partition_gen (see VENDORING below) at the exact CSV format
     device_creds.cpp reads (namespace "aregdev", keys "devid"/"apikey"/"pop"
     -- see esp32/AregVoiceMvp/device_creds_rules.h, the single source of
     truth for those three strings).
  3. Write that image with esptool to the NVS partition's OWN offset, read
     from esp32/AregVoiceMvp/partitions.csv rather than hard-coded here --
     the partition table has moved once already (B.2, huge_app -> custom)
     and a hard-coded offset would silently mis-flash after the next move.
  4. If a serial port was given: reset the toy and watch its own serial log
     for a successful heartbeat, so "provisioned" means "the backend has
     heard from this exact unit", not merely "esptool exited 0".
  5. Render the printable label: a QR (the same {deviceId, claim, pop} JSON
     the toy's own QR encodes) and a one-page PDF with deviceId, claim code
     and PoP in large print.

WHAT NEVER APPEARS ON SCREEN OR IN A LOG LINE: the device API key. It is
held in memory for exactly as long as it takes to build the NVS image and is
never printed, never logged, and never written into the label. The NVS image
itself (nvs.bin, in --out-dir) DOES contain it in plaintext, by necessity --
that file is exactly as sensitive as a filled-in config.h and the runbook
(docs/factory-provisioning-runbook.md) says so: never commit it, never
upload it anywhere, delete the batch's out-dir when the run is done.

VENDORING (per CLAUDE.md: "implement the generator standalone or vendor it
with its licence"). Reimplementing esp-idf's NVS binary format from scratch
was rejected on purpose -- a subtly wrong from-scratch encoder is the kind of
bug that only shows up as a toy that silently keeps its old identity or,
worse, a corrupt NVS page, and there would be no hardware here to catch it
on. Instead this vendors Espressif's OWN generator, published by Espressif
as the `esp-idf-nvs-partition-gen` PyPI package (Apache License 2.0, same
source as esp-idf's `components/nvs_flash/nvs_partition_generator/
nvs_partition_gen.py`) -- see requirements.txt. It is invoked as a
subprocess (`python3 -m esp_idf_nvs_partition_gen generate ...`) rather than
imported, so this script has no dependency on that package's internal API
surface, only its documented CLI.

USAGE
    # Real unit, on the bench, with a serial port attached:
    export AREG_PROVISIONING_SECRET=...
    python3 tools/factory/provision_toy.py \\
        --backend-url http://192.168.1.50:5000 \\
        --mac AA:BB:CC:DD:EE:FF \\
        --port /dev/ttyUSB0

    # Register only, flash by hand later (no port given):
    python3 tools/factory/provision_toy.py \\
        --backend-url http://192.168.1.50:5000 --mac AA:BB:CC:DD:EE:FF

    # Dry run -- label only, from a saved registration response, no network,
    # no esptool, no serial port. Useful for testing the label layout.
    python3 tools/factory/provision_toy.py --dry-run sample_response.json

Exit code 0 = provisioned (or, in --dry-run, label rendered). Non-zero = do
not ship this unit; read the printed reason.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
import time
from pathlib import Path

try:
    import requests
except ImportError:
    requests = None  # only needed for the (non-dry-run) register step

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_PARTITIONS_CSV = REPO_ROOT / "esp32" / "AregVoiceMvp" / "partitions.csv"

# Must match device_creds_rules.h EXACTLY -- these three strings are the
# whole contract between this script and the firmware that reads NVS back.
NVS_NAMESPACE = "aregdev"
NVS_KEY_ID = "devid"
NVS_KEY_APIKEY = "apikey"
NVS_KEY_POP = "pop"

# The exact line voice_client.cpp prints on a successful heartbeat POST
# (voice_client.cpp: `Serial.printf("[heartbeat] status=%d ...")`) -- what
# --port verification watches for. A non-200 status prints too (e.g. 401,
# which would mean the burned key does not match what the backend just
# minted -- a real, reportable failure, not a flake).
HEARTBEAT_OK_RE = re.compile(r"\[heartbeat\]\s+status=(\d+)")

PARTITION_ROW_RE = re.compile(
    r"^\s*([A-Za-z0-9_]+)\s*,\s*([A-Za-z0-9_]*)\s*,\s*([A-Za-z0-9_]*)\s*,"
    r"\s*([^,]+?)\s*,\s*([^,]+?)\s*,?\s*(?:#.*)?$"
)


class ProvisionError(RuntimeError):
    """A reportable failure -- printed and turned into a non-zero exit,
    never a raw traceback (this runs on a factory bench, not a dev machine)."""


def parse_partition_offset(partitions_csv: Path, name: str) -> int:
    """Reads ONE partition's offset out of an esp-idf partitions.csv, so the
    NVS write offset always matches the table actually shipping -- see the
    module docstring on why this must never be a hard-coded constant."""
    if not partitions_csv.is_file():
        raise ProvisionError(f"partitions.csv not found: {partitions_csv}")
    for line in partitions_csv.read_text().splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        m = PARTITION_ROW_RE.match(line)
        if not m:
            continue
        row_name, _type, _subtype, offset_str, _size = m.groups()[:5]
        if row_name.strip() == name:
            offset_str = offset_str.strip()
            return int(offset_str, 16) if offset_str.lower().startswith("0x") else int(offset_str)
    raise ProvisionError(f"no '{name}' row in {partitions_csv}")


def register_device(backend_url: str, mac: str, secret: str) -> dict:
    if requests is None:
        raise ProvisionError("the 'requests' package is required for --backend-url/--mac "
                              "(pip install -r tools/factory/requirements.txt); "
                              "use --dry-run to render a label without it")
    url = backend_url.rstrip("/") + "/api/devices/register"
    resp = requests.post(
        url,
        json={"macAddress": mac},
        headers={"X-Provisioning-Secret": secret} if secret else {},
        timeout=15,
    )
    if resp.status_code != 201:
        # Deliberately do not echo the response body -- a 409 (already
        # registered) or 401 body is not secret, but there is no reason to
        # widen what this script prints beyond what it has to.
        raise ProvisionError(
            f"registration failed: HTTP {resp.status_code} "
            f"(already registered? wrong/missing AREG_PROVISIONING_SECRET?)")
    data = resp.json()
    for field in ("deviceId", "apiKey", "claimCode", "pop", "qrPayload"):
        if not data.get(field):
            raise ProvisionError(
                f"registration response is missing '{field}' -- this MAC may already be "
                f"registered (re-registration mints no claim code / pop; factory "
                f"provisioning needs a brand-new device). Response had keys: "
                f"{sorted(data.keys())}")
    return data


def build_nvs_image(out_dir: Path, device_id: str, api_key: str, pop: str, size_bytes: int) -> Path:
    """Builds the NVS partition image via Espressif's own generator (see the
    VENDORING note at the top of this file). CSV format:
    https://docs.espressif.com/projects/esp-idf/en/stable/esp32/api-reference/storage/nvs_partition_gen.html
    """
    with tempfile.TemporaryDirectory() as tmp:
        csv_path = Path(tmp) / "creds.csv"
        # NOTE: api_key touches disk here only as part of this short-lived
        # CSV, immediately consumed by the generate call below and deleted
        # with the temp dir. It is never printed to stdout/stderr anywhere
        # in this script.
        csv_path.write_text(
            "key,type,encoding,value\n"
            f"{NVS_NAMESPACE},namespace,,\n"
            f"{NVS_KEY_ID},data,string,{device_id}\n"
            f"{NVS_KEY_APIKEY},data,string,{api_key}\n"
            f"{NVS_KEY_POP},data,string,{pop}\n"
        )
        out_dir.mkdir(parents=True, exist_ok=True)
        nvs_bin = out_dir / "nvs.bin"
        result = subprocess.run(
            [sys.executable, "-m", "esp_idf_nvs_partition_gen", "generate",
             str(csv_path), str(nvs_bin), hex(size_bytes)],
            capture_output=True, text=True,
        )
        if result.returncode != 0 or not nvs_bin.is_file():
            # Safe to print stdout/stderr here -- the generator's own output
            # never echoes the CSV values back, only file paths and sizes.
            raise ProvisionError(
                f"nvs_partition_gen failed (exit {result.returncode}):\n"
                f"{result.stdout}\n{result.stderr}")
        return nvs_bin


def flash_nvs_image(port: str, baud: int, offset: int, nvs_bin: Path) -> None:
    result = subprocess.run(
        [sys.executable, "-m", "esptool", "--chip", "esp32s3",
         "--port", port, "--baud", str(baud),
         "write-flash", hex(offset), str(nvs_bin)],
        capture_output=True, text=True,
    )
    print(result.stdout)
    if result.returncode != 0:
        raise ProvisionError(f"esptool write-flash failed (exit {result.returncode}):\n{result.stderr}")


def verify_heartbeat(port: str, baud: int, timeout_s: float) -> None:
    """Watches the toy's OWN serial log after the NVS write (esptool resets
    the chip after write-flash by default) for its first heartbeat POST.
    Proves the exact unit that was just flashed authenticated with the
    identity just burned -- not merely that the write succeeded."""
    try:
        import serial  # esptool's own dependency; see requirements.txt
    except ImportError:
        raise ProvisionError("pyserial is required for heartbeat verification "
                              "(pip install -r tools/factory/requirements.txt)")

    print(f"[verify] watching {port} for a heartbeat (up to {timeout_s:.0f}s)...")
    deadline = time.monotonic() + timeout_s
    buf = ""
    with serial.Serial(port, baudrate=baud, timeout=1) as ser:
        while time.monotonic() < deadline:
            chunk = ser.read(4096)
            if not chunk:
                continue
            buf += chunk.decode("utf-8", errors="replace")
            while "\n" in buf:
                line, buf = buf.split("\n", 1)
                m = HEARTBEAT_OK_RE.search(line)
                if m:
                    status = int(m.group(1))
                    if status == 200:
                        print(f"[verify] heartbeat OK ({line.strip()})")
                        return
                    raise ProvisionError(
                        f"toy heartbeat came back non-200 ({line.strip()}) -- the "
                        f"burned identity does not match what the backend minted; "
                        f"do not ship this unit")
    raise ProvisionError(
        f"no heartbeat seen on {port} within {timeout_s:.0f}s -- check Wi-Fi "
        f"credentials are provisioned (BLE or config.h fallback) and the backend "
        f"is reachable from the toy's network")


def render_label(out_dir: Path, device_id: str, claim_code: str, pop: str, qr_payload: str) -> None:
    import qrcode
    from reportlab.lib.pagesizes import A6
    from reportlab.lib.units import mm
    from reportlab.pdfgen import canvas

    out_dir.mkdir(parents=True, exist_ok=True)
    qr_path = out_dir / "qr.png"
    qrcode.make(qr_payload).save(qr_path)

    pdf_path = out_dir / "label.pdf"
    width, height = A6
    c = canvas.Canvas(str(pdf_path), pagesize=A6)

    qr_size = 45 * mm
    c.drawImage(str(qr_path), (width - qr_size) / 2, height - qr_size - 10 * mm,
                width=qr_size, height=qr_size)

    def centered(y_mm: float, text: str, font_size: int, bold: bool = False) -> None:
        c.setFont("Helvetica-Bold" if bold else "Helvetica", font_size)
        c.drawCentredString(width / 2, y_mm * mm, text)

    centered(60, "Areg", 18, bold=True)
    centered(50, "Device ID", 9)
    centered(45, device_id, 11, bold=True)
    centered(35, "Pairing code (Bluetooth setup)", 9)
    centered(29, pop, 22, bold=True)
    centered(18, "Claim code (app)", 9)
    centered(12, claim_code, 13, bold=True)

    c.showPage()
    c.save()
    print(f"[label] wrote {qr_path}")
    print(f"[label] wrote {pdf_path}")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--backend-url", help="e.g. http://192.168.1.50:5000")
    ap.add_argument("--mac", help="the toy's MAC address to register")
    ap.add_argument("--port", help="serial port; if given, flash the NVS image and verify a heartbeat")
    ap.add_argument("--baud", type=int, default=115200)
    ap.add_argument("--heartbeat-timeout", type=float, default=45.0,
                     help="seconds to wait for [heartbeat] status=200 after flashing (default 45)")
    ap.add_argument("--out-dir", type=Path, default=None,
                     help="default: tools/factory/out/<deviceId>/ -- CONTAINS THE DEVICE KEY "
                          "in plaintext (nvs.bin); never commit, never upload, delete after the batch")
    ap.add_argument("--partitions-csv", type=Path, default=DEFAULT_PARTITIONS_CSV)
    ap.add_argument("--dry-run", type=Path, metavar="RESPONSE_JSON",
                     help="skip registration AND hardware; render the label from a saved "
                          "POST /api/devices/register response (must still carry deviceId/"
                          "claimCode/pop/qrPayload -- apiKey is ignored either way)")
    args = ap.parse_args()

    try:
        if args.dry_run:
            data = json.loads(args.dry_run.read_text())
            missing = [f for f in ("deviceId", "claimCode", "pop", "qrPayload") if not data.get(f)]
            if missing:
                raise ProvisionError(f"{args.dry_run} is missing field(s): {missing}")
        else:
            if not args.backend_url or not args.mac:
                raise ProvisionError("--backend-url and --mac are required (or pass --dry-run)")
            secret = os.environ.get("AREG_PROVISIONING_SECRET", "")
            if not secret:
                print("[warn] AREG_PROVISIONING_SECRET is not set -- this only works against a "
                      "backend with Devices:AllowOpenRegistration (dev/bench only, never production)",
                      file=sys.stderr)
            data = register_device(args.backend_url, args.mac, secret)

        device_id = data["deviceId"]
        claim_code = data["claimCode"]
        pop = data["pop"]
        qr_payload = data["qrPayload"]
        # api_key is read once, used once (build_nvs_image), and this name
        # goes out of scope with main() -- it is never assigned anywhere
        # else in this file.
        api_key = data.get("apiKey")

        out_dir = args.out_dir or (Path(__file__).resolve().parent / "out" / device_id)

        print(f"[device] id={device_id}")  # deviceId is not secret (it is IN the QR)

        if not args.dry_run:
            if api_key is None:
                raise ProvisionError("registration response had no apiKey -- cannot burn NVS")
            nvs_offset = parse_partition_offset(args.partitions_csv, "nvs")
            nvs_size = None
            for line in args.partitions_csv.read_text().splitlines():
                m = PARTITION_ROW_RE.match(line.strip())
                if m and m.group(1).strip() == "nvs":
                    size_str = m.group(5).strip()
                    nvs_size = int(size_str, 16) if size_str.lower().startswith("0x") else int(size_str)
            if nvs_size is None:
                raise ProvisionError(f"could not read the nvs partition size from {args.partitions_csv}")

            nvs_bin = build_nvs_image(out_dir, device_id, api_key, pop, nvs_size)
            print(f"[nvs] built {nvs_bin} ({nvs_size} B, offset {hex(nvs_offset)})")

            if args.port:
                flash_nvs_image(args.port, args.baud, nvs_offset, nvs_bin)
                verify_heartbeat(args.port, args.baud, args.heartbeat_timeout)
            else:
                print("[nvs] --port not given -- flash by hand later:")
                print(f"      python3 -m esptool --chip esp32s3 --port <PORT> "
                      f"write-flash {hex(nvs_offset)} {nvs_bin}")

        render_label(out_dir, device_id, claim_code, pop, qr_payload)
        print(f"[done] {out_dir}")
        if not args.dry_run:
            print("[done] out-dir also contains the device's plaintext key (nvs.bin) -- "
                  "treat like a filled-in config.h: never commit, delete after the batch.")
        return 0
    except ProvisionError as e:
        print(f"FAIL - {e}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())

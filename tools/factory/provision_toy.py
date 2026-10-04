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

    # Rotate an ALREADY-REGISTERED toy's key (a leaked key, a repaired unit):
    export AREG_PROVISIONING_SECRET=...
    python3 tools/factory/provision_toy.py --rotate-existing \\
        --backend-url https://<host> --mac <MAC exactly as the console shows it>

ROTATING AN EXISTING TOY (--rotate-existing). Keeps the toy's device id, its
Device row and its parent link; only the key changes. It sends
`X-Force-Rotate: true` TWICE on purpose: on a row still holding a legacy
plaintext key, DeviceService's first forced re-registration returns that SAME
key (and only then hashes it), so one call could burn the leaked key straight
back in. The second call always mints a fresh one, and that is the key burned.
A forced re-registration mints no claim code and no PoP, so the NVS image
carries devid/apikey only (the toy then advertises the bench fallback PoP,
`areg-pair`) and no label is printed -- the claim code on the box is
unchanged. Writing nvs.bin replaces the WHOLE nvs partition, which also wipes
the toy's stored Wi-Fi: re-provision it over BLE afterwards. Not combinable
with --port: no heartbeat can arrive until that BLE step is done, so the
image is flashed by hand with the printed command. A rotation does not clear
a console revocation -- restore the toy there first if it is revoked. Unless
--out-dir is given, the rotated nvs.bin goes to a fresh private temp
directory OUTSIDE the repo (the path is printed): this is the key that
replaces one that leaked through git, so it must never sit where a routine
`git add -A` would pick it up.

Exit code 0 = provisioned (or, in --dry-run, label rendered). Non-zero = do
not ship this unit; read the printed reason.
"""
from __future__ import annotations

import argparse
import importlib.util
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


def nvs_partition_geometry(partitions_csv: Path) -> tuple[int, int]:
    """(offset, size) of the 'nvs' row -- both read from the table actually
    shipping, never hard-coded (see parse_partition_offset)."""
    offset = parse_partition_offset(partitions_csv, "nvs")
    for line in partitions_csv.read_text().splitlines():
        m = PARTITION_ROW_RE.match(line.strip())
        if m and m.group(1).strip() == "nvs":
            size_str = m.group(5).strip()
            return offset, int(size_str, 16) if size_str.lower().startswith("0x") else int(size_str)
    raise ProvisionError(f"could not read the nvs partition size from {partitions_csv}")


def _post_register(backend_url: str, mac: str, secret: str, force_rotate: bool = False):
    """One POST /api/devices/register. Returns the raw response; callers check
    the status. Never prints anything itself."""
    if requests is None:
        raise ProvisionError("the 'requests' package is required for --backend-url/--mac "
                              "(pip install -r tools/factory/requirements.txt); "
                              "use --dry-run to render a label without it")
    headers = {"X-Provisioning-Secret": secret} if secret else {}
    if force_rotate:
        headers["X-Force-Rotate"] = "true"
    try:
        return requests.post(
            backend_url.rstrip("/") + "/api/devices/register",
            json={"macAddress": mac},
            headers=headers,
            timeout=15,
        )
    except requests.RequestException as e:
        # The exception text names the URL, never the headers.
        raise ProvisionError(f"could not reach the backend: {type(e).__name__}: {e}")


def register_device(backend_url: str, mac: str, secret: str) -> dict:
    resp = _post_register(backend_url, mac, secret)
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


def _rotation_step(backend_url: str, mac: str, secret: str, step: int) -> dict:
    resp = _post_register(backend_url, mac, secret, force_rotate=True)
    if resp.status_code != 201:
        # Same posture as register_device: the status only, never the body.
        raise ProvisionError(
            f"rotation call {step}/2 failed: HTTP {resp.status_code} "
            f"(401 = wrong/missing AREG_PROVISIONING_SECRET; 429 = wait a minute). "
            f"Re-run the whole command -- the toy's old key may already be dead, "
            f"which is expected: it is being replaced")
    data = resp.json()
    for field in ("deviceId", "apiKey"):
        if not data.get(field):
            raise ProvisionError(f"rotation call {step}/2 returned no '{field}'")
    return data


def rotate_existing_device(backend_url: str, mac: str, secret: str) -> dict:
    """Rotates an already-registered toy's key in place: two forced
    re-registrations, the second one's key is the one to burn (see the module
    docstring for why one is not enough). Returns {deviceId, apiKey}. Every
    comparison below is in memory; no key is ever put into a message."""
    first = _rotation_step(backend_url, mac, secret, 1)
    if first.get("claimCode") or first.get("pop"):
        # A forced call on an UNKNOWN MAC registers a brand-new device (that
        # is the only response that carries a claim code / PoP).
        raise ProvisionError(
            f"this MAC was not registered, so the backend just created a NEW device "
            f"(id={first['deviceId']}) instead of rotating one. Check --mac against the "
            f"console Devices tab (the MAC under the toy's name, exact spelling), and "
            f"revoke device {first['deviceId']} there. Nothing was built or burned")
    second = _rotation_step(backend_url, mac, secret, 2)
    if second["deviceId"] != first["deviceId"]:
        raise ProvisionError(
            f"the two rotation calls returned different device ids ({first['deviceId']} vs "
            f"{second['deviceId']}) -- stop and investigate. Nothing was built or burned")
    if second["apiKey"] == first["apiKey"]:
        raise ProvisionError(
            "the second rotation call returned the same key as the first -- the backend "
            "did not rotate. Nothing was built or burned")
    return {"deviceId": second["deviceId"], "apiKey": second["apiKey"]}


def nvs_generator_available() -> bool:
    """True when Espressif's generator (run below as `python -m`) is
    installed for THIS interpreter."""
    return importlib.util.find_spec("esp_idf_nvs_partition_gen") is not None


def build_nvs_image(out_dir: Path, device_id: str, api_key: str, pop: str | None, size_bytes: int) -> Path:
    """Builds the NVS partition image via Espressif's own generator (see the
    VENDORING note at the top of this file). CSV format:
    https://docs.espressif.com/projects/esp-idf/en/stable/esp32/api-reference/storage/nvs_partition_gen.html

    pop=None (a --rotate-existing image) leaves the pop row out entirely; the
    firmware then falls back to its compiled bench PoP (ble_provisioning.cpp).
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
            + (f"{NVS_KEY_POP},data,string,{pop}\n" if pop else "")
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


def default_rotate_out_dir(device_id: str) -> Path:
    """A fresh 0700 temp directory outside the repo for a rotated nvs.bin.
    Only the id's first characters go in the name, filtered so a malformed id
    can never steer the path."""
    tag = re.sub(r"[^0-9A-Za-z-]", "", device_id)[:8]
    return Path(tempfile.mkdtemp(prefix=f"areg-rotate-{tag}-"))


def rotate_main(args: argparse.Namespace) -> int:
    """--rotate-existing: rotate, build a devid/apikey-only nvs.bin, print the
    flash command and the follow-up checks. No label, no key on screen."""
    if args.dry_run or args.port:
        raise ProvisionError(
            "--rotate-existing cannot be combined with --dry-run or --port (flash the "
            "printed command by hand, then re-provision Wi-Fi over BLE; see the docstring)")
    if not args.backend_url or not args.mac:
        raise ProvisionError("--rotate-existing needs --backend-url and --mac")
    secret = os.environ.get("AREG_PROVISIONING_SECRET", "")
    if not secret:
        print("[warn] AREG_PROVISIONING_SECRET is not set -- this only works against a "
              "backend with Devices:AllowOpenRegistration (dev/bench only, never production)",
              file=sys.stderr)
    # Check everything the image build needs BEFORE touching the backend: a
    # rotation that cannot then be built leaves the toy with a dead key.
    nvs_offset, nvs_size = nvs_partition_geometry(args.partitions_csv)
    if not nvs_generator_available():
        raise ProvisionError(
            "esp-idf-nvs-partition-gen is not installed for this Python "
            "(pip install -r tools/factory/requirements.txt) -- checked before rotating, "
            "so nothing changed on the backend")

    rotated = rotate_existing_device(args.backend_url, args.mac, secret)
    device_id = rotated["deviceId"]
    out_dir = args.out_dir or default_rotate_out_dir(device_id)
    print(f"[device] id={device_id}")  # not secret (it is in the QR)
    print("[rotate] key rotated in place (2 forced re-registrations; the second key is "
          "the one in the image). Parent link and claim code are unchanged.")

    nvs_bin = build_nvs_image(out_dir, device_id, rotated["apiKey"], None, nvs_size)
    print(f"[nvs] built {nvs_bin} ({nvs_size} B, offset {hex(nvs_offset)}; devid + apikey only, no pop)")
    print("[nvs] flash it with the toy on USB:")
    print(f"      python3 -m esptool --chip esp32s3 --port <PORT> write-flash {hex(nvs_offset)} {nvs_bin}")
    print("[next] the write wipes the toy's Wi-Fi: re-provision it over BLE (PoP: the bench "
          "fallback, areg-pair; if the toy does not open provisioning by itself, hold the "
          "button 2 s at power-on). Serial must show [heartbeat] status=200.")
    print("[next] the OLD key must now get 401 on POST /api/devices/heartbeat. If the console "
          "shows this toy as revoked, restore it there -- a rotation does not clear a revocation.")
    print(f"[done] {out_dir} contains the device's plaintext key (nvs.bin) -- delete it once "
          f"the toy shows status=200.")
    return 0


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--backend-url", help="e.g. http://192.168.1.50:5000")
    ap.add_argument("--mac", help="the toy's MAC address to register")
    ap.add_argument("--port", help="serial port; if given, flash the NVS image and verify a heartbeat")
    ap.add_argument("--baud", type=int, default=115200)
    ap.add_argument("--heartbeat-timeout", type=float, default=45.0,
                     help="seconds to wait for [heartbeat] status=200 after flashing (default 45)")
    ap.add_argument("--out-dir", type=Path, default=None,
                     help="default: tools/factory/out/<deviceId>/ (with --rotate-existing: a fresh "
                          "temp directory outside the repo, printed) -- CONTAINS THE DEVICE KEY "
                          "in plaintext (nvs.bin); never commit, never upload, delete after the batch")
    ap.add_argument("--partitions-csv", type=Path, default=DEFAULT_PARTITIONS_CSV)
    ap.add_argument("--dry-run", type=Path, metavar="RESPONSE_JSON",
                     help="skip registration AND hardware; render the label from a saved "
                          "POST /api/devices/register response (must still carry deviceId/"
                          "claimCode/pop/qrPayload -- apiKey is ignored either way)")
    ap.add_argument("--rotate-existing", action="store_true",
                     help="rotate the key of a toy that is ALREADY registered (same device id); "
                          "builds nvs.bin with devid/apikey only and prints no label -- see "
                          "'ROTATING AN EXISTING TOY' above")
    args = ap.parse_args(argv)

    try:
        if args.rotate_existing:
            return rotate_main(args)
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
            nvs_offset, nvs_size = nvs_partition_geometry(args.partitions_csv)

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

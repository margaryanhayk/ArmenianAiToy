#!/usr/bin/env python3
"""Factory station, RELEASE profile: lock one toy (Secure Boot V2 + flash
encryption + NVS encryption + JTAG off + ROM download mode off).

This is the ONE manufacturing flow of docs/firmware-security.md s9 ("Enable
Flash Encryption and Secure Boot v2 Externally"): every key is generated on
this station, every image is encrypted HERE, written to a BLANK chip, and
only then are the eFuses burned -- no first-boot in-place encryption.

STEPS (each one checked before the next; a failure stops the run):
   0  preflight   bundle.json shas; app gate (--require-sbv2 --profile
                  release: incl. >= 3 TLS trust anchors); bootloader gate
                  (exactly 2 blocks, both keys, AND a RELEASED.md build whose
                  Status is APPROVED -- PILOT with --pilot-board, any
                  recorded row in a rehearsal); partition-table.bin == the
                  table RELEASED.md records with that bootloader AND parses
                  to esp32/AregVoiceMvp/partitions.csv (nvs_sec where step 4
                  builds it); boot_app0.bin == arduino-esp32 3.3.8's; the two
                  PUBLIC keys' digests == esp32/security/sb_trusted_digests.txt; refuses
                  TEST keys / TEST bundles; provision/repair ALSO refuse a
                  bundle without hardware-pilot evidence (bundle.json
                  "pilot" -> a tools/quality-evidence/ file naming this
                  signed bootloader's sha256 and "OTA release+1 on locked
                  board: PASS"), unless --pilot-board (a sacrificial pilot
                  board: recorded as such, NEVER shipped)
   1  virgin chip esptool read-mac / flash-id (ESP32-S3, 8 MB);
                  espefuse summary: CRYPT_CNT 0b000, SECURE_BOOT_EN off,
                  every KEY_PURPOSE USER, download mode open -- else STOP
   2  register    POST /api/devices/register (provision_toy.py's code)
   3  keys        espsecure generate-flash-encryption-key --keylen 512;
                  nvs_partition_gen generate-key --key_protect_hmac
   4  images      espsecure encrypt-flash-data (bootloader 0x0, table
                  0x8000, boot_app0 0xe000, app 0x10000) + round-trip check;
                  identity CSV -> nvs_partition_gen encrypt -> nvs_sec.bin;
                  CSV deleted
   5  flash       esptool erase-flash; write-flash (chip still blank)
      ONE confirmation (`BURN <mac>`) covers steps 6 AND 7: no human wait
      may sit between them (see the window below)
   6  BURN        FE key (BLOCK_KEY0/1), HMAC key (BLOCK_KEY4), CRYPT_CNT=7,
                  download-mode cache/encrypt off, JTAG off, DIS_DIRECT_BOOT,
                  write-protect DIS_ICACHE                     [IRREVERSIBLE]
   7  BURN        SB digests (BLOCK_KEY2 primary, BLOCK_KEY3 backup) from the
                  PUBLIC keys, revoke slot 2, SECURE_BOOT_EN, write-protect
                  RD_DIS + KEY_PURPOSE_5                        [IRREVERSIBLE]
   8  verify      reset; console must show the [sec] posture (sb=1
                  fe=release dl=enabled nvs=encrypted store=encrypted), the
                  provisioned identity, key_fp == SHA-256(apiKey)[:4], and
                  the "[sec] factory not finished" line (the image keeps a
                  toy with download mode open OFFLINE and refuses BLE setup)
   9  BURN        DIS_DOWNLOAD_MODE; reset; dl=disabled sb_rel=1 fe_rel=1,
                  no key_fp line                         [IRREVERSIBLE, LAST]
  10  shred       every file in the workdir (tmpfs), then the directory
  11  record      <out-root>/<deviceId>/factory-record.json (no secrets) +
                  label; --out-root defaults to ~/areg-factory-out (mode 700,
                  OUTSIDE the repository)

The operator types `BURN <mac>` twice: once for steps 6+7 together, once
for step 9 (or passes --confirm-mac <mac> for a batch). Right before EVERY
irreversible espefuse call the station re-reads the chip's MAC from its
eFuses and stops if it is not the toy identified at step 1 (a swapped or
replugged board).

THE STEP 6 -> 7 WINDOW. After step 6 flash encryption is on and Secure Boot
is still off. If the chip BOOTED NORMALLY then, the release bootloader
(CONFIG_SECURE_BOOT + CONFIG_SECURE_DISABLE_ROM_DL_MODE) would enable Secure
Boot by itself and burn DIS_DOWNLOAD_MODE -- the toy locked before step 8
could check anything (ESP-IDF v5.5.4 secure_boot_secure_features.c). So
every esptool call here passes `--after no-reset` (esptool's default
hard-reset would boot it), espefuse only ever resets INTO download mode,
`repair` reads the eFuses before anything else and, while CRYPT_CNT=0b111
and SECURE_BOOT_EN=0, refuses any normal boot until step 7 is verified.

https only: provision / --rotate-existing refuse a non-https backend (the
provisioning secret goes out and a fresh device key comes back).

The device API key, the FE key and the
HMAC key are never printed and never logged; they exist only in
/dev/shm/areg-factory/<mac>/ (mode 700) for about a minute and are shredded
after step 9. On a failure at steps 6-8 the workdir is KEPT for
`secure_provision.py repair` -- never re-run the full flow on that chip.

ANY failure after registration writes <out-root>/<deviceId>/INCOMPLETE.json
(device id, MAC, last completed step, whether eFuses were burned, time; no
secrets) and prints DO NOT SHIP with the revocation call
(POST /api/internal/devices/<id>/revoke) for a toy that will not be
finished. A successful finish removes it.

USAGE
  # production (one toy on PORT):
  export AREG_PROVISIONING_SECRET=...
  python3 tools/factory/secure_provision.py provision --bundle bundle-1.4.0 \\
      --backend-url https://<host> --port /dev/ttyACM0

  # convert an already-registered toy (keeps id + parent link, new key,
  # locally minted PoP, PoP-only sticker):
  python3 tools/factory/secure_provision.py provision --rotate-existing --mac <MAC> ...

  # repair a toy that stopped at step 6/7/8 (workdir still there):
  python3 tools/factory/secure_provision.py repair --bundle ... --port ... [--reflash]

  # rehearsals -- nothing touches a chip:
  python3 tools/factory/secure_provision.py dry-run --bundle B --response resp.json
      (steps 3-4 for real in a temp dir, prints 5-9)
  python3 tools/factory/secure_provision.py virt --bundle B --response resp.json
      (also EXECUTES 6, 7, 9 against `espefuse --virt --path-efuse-file`
       and asserts every summary)

Tools: esptool / espsecure / espefuse 5.2.0 and esp-idf-nvs-partition-gen
0.3.0 (tools/factory/requirements.txt), run as `python -m esptool`,
`python -m espsecure`, `python -m espefuse`, `python -m
esp_idf_nvs_partition_gen` with THIS interpreter.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import secrets
import shutil
import subprocess
import sys
import tempfile
import time
from dataclasses import dataclass, field
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(REPO / "tools" / "firmware"))
import check_release_image as gate_lib  # noqa: E402  (RELEASED.md pins, partition-table parsing)
import provision_toy as pt  # noqa: E402  (registration, rotation, label, partition parsing)
from sign_release import PILOT_PASS_LINE, pilot_evidence_problems  # noqa: E402  (one rule, both ends)

# Where the pilot evidence lives (the repo; the tests point it elsewhere).
PILOT_REPO_ROOT = REPO

GATE = REPO / "tools" / "firmware" / "check_release_image.py"
SECURITY_DIR = REPO / "esp32" / "security"
DEFAULT_TRUSTED = SECURITY_DIR / "sb_trusted_digests.txt"
DEFAULT_PRIMARY_PUB = SECURITY_DIR / "sb_primary.pub.pem"
DEFAULT_BACKUP_PUB = SECURITY_DIR / "sb_backup.pub.pem"
TEST_TRUSTED = REPO / "tools" / "firmware" / "testdata" / "TEST_sb_trusted_digests.txt"
WORKDIR_ROOT = Path("/dev/shm/areg-factory")
DEFAULT_OUT_ROOT = pt.DEFAULT_FACTORY_OUT  # ~/areg-factory-out: never inside the repository
# esptool's default --after is hard-reset, i.e. a NORMAL boot. Between steps 6
# and 7 that boot would let the release bootloader lock the toy by itself, so
# no esptool call made by this station ever resets the chip.
ESPTOOL_AFTER = ["--after", "no-reset"]
STATION_VERSION = "secure-provision/1"

CHIP = "esp32s3"
FLASH_SIZE = "8MB"
POP_ALPHABET = "ABCDEFGHJKMNPQRSTVWXYZ23456789"  # == DeviceService.cs / device_creds_rules.h
POP_LENGTH = 8
NVS_SYS_NS, NVS_LAYOUT_KEY = "aregsys", "layout"  # == secure_store_rules.h

# --- the eFuse plan (docs/firmware-security.md s4.1) --------------------------
# (field, burn value, expected summary value)
STEP6_EFUSES = [
    ("DIS_DOWNLOAD_MANUAL_ENCRYPT", "1", True),
    ("DIS_DOWNLOAD_ICACHE", "1", True),
    ("DIS_DOWNLOAD_DCACHE", "1", True),
    ("DIS_PAD_JTAG", "1", True),
    ("DIS_USB_JTAG", "1", True),
    ("DIS_DIRECT_BOOT", "1", True),
    ("SOFT_DIS_JTAG", "7", 7),
]
STEP7_EFUSES = [
    ("SECURE_BOOT_KEY_REVOKE2", "1", True),
    ("SECURE_BOOT_EN", "1", True),
]
FINAL_PURPOSES = {
    "KEY_PURPOSE_0": "XTS_AES_256_KEY_1",
    "KEY_PURPOSE_1": "XTS_AES_256_KEY_2",
    "KEY_PURPOSE_2": "SECURE_BOOT_DIGEST0",
    "KEY_PURPOSE_3": "SECURE_BOOT_DIGEST1",
    "KEY_PURPOSE_4": "HMAC_UP",
    "KEY_PURPOSE_5": "USER",
}
# Deliberately NOT burned (kept writable for a future signed-OTA revocation,
# or pointless): verified to stay at these values after step 7.
MUST_STAY = {
    "SECURE_BOOT_KEY_REVOKE0": False,
    "SECURE_BOOT_KEY_REVOKE1": False,
    "SECURE_BOOT_AGGRESSIVE_REVOKE": False,
    "ENABLE_SECURITY_DOWNLOAD": False,
    "DIS_USB_SERIAL_JTAG": False,
}
RD_DIS_FINAL = 0b10011  # blocks KEY0, KEY1, KEY4 read-protected = 19
BURN_MARKER = "burn-started"  # in the workdir: from here on only `repair` may touch this toy

POSTURE_RE = re.compile(
    r"\[sec\] profile=(?P<profile>\S+) sb=(?P<sb>\d) fe=(?P<fe>\S+) dl=(?P<dl>\S+) jtag=(?P<jtag>\S+) "
    r"nvs=(?P<nvs>\S+) hmac=(?P<hmac>\S+) sbslots=(?P<sbslots>0x[0-9a-f]+) revoked=(?P<revoked>0x[0-9a-f]+) "
    r"sb_rel=(?P<sb_rel>\d) fe_rel=(?P<fe_rel>\d) store=(?P<store>\S+)")
KEY_FP_RE = re.compile(r"\[sec\] key_fp=([0-9a-f]{8})")
FACTORY_WINDOW_LINE = "[sec] factory not finished (download mode open) - offline, BLE setup disabled"
IDENTITY_RE = re.compile(r"\[device\] using provisioned identity \(id=([^)\s]+)\)")
CHIP_RE = re.compile(r"(?:Chip type:|Chip is)\s*(ESP32-S3)\b[^\n]*?revision v(\d+)\.(\d+)")
MAC_RE = re.compile(r"^MAC:\s*([0-9a-fA-F]{2}(?::[0-9a-fA-F]{2}){5})", re.M)
FLASH_RE = re.compile(r"Detected flash size:\s*(\S+)")
CRYPT_CNT_RE = re.compile(r"SPI_BOOT_CRYPT_CNT\b.*\(0b([01]{3})\)")
EFUSE_MAC_RE = re.compile(r"([0-9a-fA-F]{2}(?::[0-9a-fA-F]{2}){5})")


class StationError(RuntimeError):
    pass


# --------------------------------------------------------------------------
# Process / console / prompt seams (replaced by fakes in the tests)
# --------------------------------------------------------------------------
def _default_run(argv: list[str], cwd: Path | None = None) -> subprocess.CompletedProcess:
    return subprocess.run(argv, cwd=cwd, capture_output=True, text=True)


def _default_console(port: str, seconds: float) -> str:
    """Hard-resets the toy (RTS pulse, as esptool does) and returns what its
    console prints within `seconds`."""
    import serial  # pyserial, an esptool dependency
    buf = []
    with serial.Serial(port, 115200, timeout=0.5) as s:
        s.dtr = False
        s.rts = True
        time.sleep(0.15)
        s.rts = False
        deadline = time.monotonic() + seconds
        while time.monotonic() < deadline:
            chunk = s.read(4096)
            if chunk:
                buf.append(chunk.decode("utf-8", errors="replace"))
    return "".join(buf)


@dataclass
class Tools:
    python: str = sys.executable

    def esptool(self) -> list[str]:
        return [self.python, "-m", "esptool"]

    def espefuse(self) -> list[str]:
        return [self.python, "-m", "espefuse"]

    def espsecure(self) -> list[str]:
        return [self.python, "-m", "espsecure"]

    def nvsgen(self) -> list[str]:
        return [self.python, "-m", "esp_idf_nvs_partition_gen"]


@dataclass
class Station:
    run: callable = _default_run
    console: callable = _default_console
    ask: callable = input
    say: callable = print
    tools: Tools = field(default_factory=Tools)
    log: list = field(default_factory=list)
    echo: bool = False  # dry-run: print every command as it runs

    def ok(self, argv: list[str], what: str, cwd: Path | None = None) -> str:
        self.log.append(list(argv))
        if self.echo:
            self.say("  $ " + (f"(cd {cwd} && " if cwd else "") + " ".join(str(a) for a in argv) + (")" if cwd else ""))
        r = self.run(argv, cwd=cwd)
        if r.returncode != 0:
            raise StationError(f"{what} failed (exit {r.returncode}):\n{(r.stdout or '')[-2000:]}{(r.stderr or '')[-2000:]}")
        return (r.stdout or "") + (r.stderr or "")


# --------------------------------------------------------------------------
# Small pure helpers (unit-tested directly)
# --------------------------------------------------------------------------
def mint_pop() -> str:
    return "".join(secrets.choice(POP_ALPHABET) for _ in range(POP_LENGTH))


def key_fingerprint(api_key: str) -> str:
    return hashlib.sha256(api_key.encode()).digest()[:4].hex()


def mac_tag(mac: str) -> str:
    tag = re.sub(r"[^0-9a-fA-F]", "", mac).lower()
    if len(tag) != 12:
        raise StationError(f"not a MAC address: {mac!r}")
    return tag


def identity_csv(device_id: str, api_key: str, pop: str) -> str:
    for v in (device_id, api_key, pop):
        if "," in v or "\n" in v:
            raise StationError("identity values must not contain commas or newlines")
    return ("key,type,encoding,value\n"
            f"{pt.NVS_NAMESPACE},namespace,,\n"
            f"{pt.NVS_KEY_ID},data,string,{device_id}\n"
            f"{pt.NVS_KEY_APIKEY},data,string,{api_key}\n"
            f"{pt.NVS_KEY_POP},data,string,{pop}\n"
            f"{NVS_SYS_NS},namespace,,\n"
            f"{NVS_LAYOUT_KEY},data,u8,1\n")


def partition_geometry(csv_path: Path, name: str) -> tuple[int, int]:
    offset = pt.parse_partition_offset(csv_path, name)
    for line in csv_path.read_text().splitlines():
        m = pt.PARTITION_ROW_RE.match(line.strip())
        if m and m.group(1).strip() == name:
            return offset, int(m.group(5).strip(), 0)
    raise StationError(f"no '{name}' row in {csv_path}")


def efuse_value(summary: dict, name: str):
    if name not in summary:
        raise StationError(f"eFuse summary has no {name}")
    return summary[name]["value"]


def block_hex(summary: dict, name: str) -> str:
    return str(efuse_value(summary, name)).replace(" ", "").lower()


def efuse_mac(summary: dict) -> str:
    """The factory MAC from `espefuse summary --format json` ("aa:bb:.. (OK)")."""
    m = EFUSE_MAC_RE.search(str(efuse_value(summary, "MAC")))
    if not m:
        raise StationError("the eFuse summary has no readable MAC")
    return m.group(1).lower()


def half_burned(summary: dict, crypt_bits: str) -> bool:
    """Flash encryption ON, Secure Boot OFF: a normal boot now would let the
    release bootloader enable Secure Boot and disable download mode by itself."""
    return crypt_bits == "111" and not efuse_value(summary, "SECURE_BOOT_EN")


def bootloader_min_status(mode: str, pilot_board: bool) -> str:
    """The RELEASED.md Status a bundle's bootloader needs for this run."""
    if mode in ("dry-run", "virt"):
        return "CANDIDATE"   # a rehearsal (TEST bundles, nothing touches a chip)
    return "PILOT" if pilot_board else "APPROVED"


def crypt_cnt_bits(text_summary: str) -> str:
    m = CRYPT_CNT_RE.search(text_summary)
    if not m:
        raise StationError("could not read SPI_BOOT_CRYPT_CNT bits from the eFuse summary")
    return m.group(1)


def check_virgin(summary: dict, crypt_bits: str) -> list[str]:
    bad = []
    if crypt_bits != "000":
        bad.append(f"SPI_BOOT_CRYPT_CNT=0b{crypt_bits} (flash encryption was enabled before)")
    if efuse_value(summary, "SECURE_BOOT_EN"):
        bad.append("SECURE_BOOT_EN already set")
    for i in range(6):
        v = efuse_value(summary, f"KEY_PURPOSE_{i}")
        if v != "USER":
            bad.append(f"KEY_PURPOSE_{i}={v} (a key block is already used)")
    if efuse_value(summary, "DIS_DOWNLOAD_MODE"):
        bad.append("DIS_DOWNLOAD_MODE already set")
    if efuse_value(summary, "RD_DIS"):
        bad.append(f"RD_DIS={efuse_value(summary, 'RD_DIS')} (a block is already read-protected)")
    return bad


def plan_step6(summary: dict, crypt_bits: str, fe_key: Path, hmac_key: Path) -> list[str]:
    """espefuse arguments that bring a chip to the post-step-6 state from
    whatever it is now -- the full command on a virgin chip, only the
    missing parts on a half-burned one (repair). Empty = already done."""
    args: list[str] = []
    if efuse_value(summary, "KEY_PURPOSE_0") != "XTS_AES_256_KEY_1":
        args += ["burn-key", "BLOCK_KEY0", str(fe_key), "XTS_AES_256_KEY"]
    if efuse_value(summary, "KEY_PURPOSE_4") != "HMAC_UP":
        args += ["burn-key", "BLOCK_KEY4", str(hmac_key), "HMAC_UP"]
    burns: list[str] = []
    if crypt_bits != "111":
        if crypt_bits != "000":
            raise StationError(f"SPI_BOOT_CRYPT_CNT is 0b{crypt_bits} -- not a state this station creates; stop")
        burns += ["SPI_BOOT_CRYPT_CNT", "7"]
    for name, value, want in STEP6_EFUSES:
        if efuse_value(summary, name) != want:
            burns += [name, value]
    if burns:
        args += ["burn-efuse", *burns]
    if summary["DIS_ICACHE"]["writeable"]:
        args += ["write-protect-efuse", "DIS_ICACHE"]
    return args


def plan_step7(summary: dict, primary_pub: Path, backup_pub: Path) -> list[str]:
    args: list[str] = []
    if efuse_value(summary, "KEY_PURPOSE_2") != "SECURE_BOOT_DIGEST0":
        args += ["burn-key-digest", "BLOCK_KEY2", str(primary_pub), "SECURE_BOOT_DIGEST0"]
    if efuse_value(summary, "KEY_PURPOSE_3") != "SECURE_BOOT_DIGEST1":
        args += ["burn-key-digest", "BLOCK_KEY3", str(backup_pub), "SECURE_BOOT_DIGEST1"]
    burns: list[str] = []
    for name, value, want in STEP7_EFUSES:
        if efuse_value(summary, name) != want:
            burns += [name, value]
    if burns:
        args += ["burn-efuse", *burns]
    wp = [n for n in ("RD_DIS", "KEY_PURPOSE_5") if summary[n]["writeable"]]
    if wp:
        args += ["write-protect-efuse", *wp]
    return args


def verify_after_step6(summary: dict, crypt_bits: str) -> list[str]:
    bad = []
    if crypt_bits != "111":
        bad.append(f"SPI_BOOT_CRYPT_CNT=0b{crypt_bits}, expected 0b111")
    for k in ("KEY_PURPOSE_0", "KEY_PURPOSE_1", "KEY_PURPOSE_4"):
        if efuse_value(summary, k) != FINAL_PURPOSES[k]:
            bad.append(f"{k}={efuse_value(summary, k)}, expected {FINAL_PURPOSES[k]}")
    for name, _, want in STEP6_EFUSES:
        if efuse_value(summary, name) != want:
            bad.append(f"{name}={efuse_value(summary, name)}, expected {want}")
    rd = efuse_value(summary, "RD_DIS")
    if rd & RD_DIS_FINAL != RD_DIS_FINAL:
        bad.append(f"RD_DIS={rd}: blocks KEY0/KEY1/KEY4 must be read-protected")
    for b in ("BLOCK_KEY0", "BLOCK_KEY1", "BLOCK_KEY4"):
        if summary[b]["readable"]:
            bad.append(f"{b} is still readable")
    if summary["DIS_ICACHE"]["writeable"]:
        bad.append("DIS_ICACHE is not write-protected")
    return bad


def verify_after_step7(summary: dict, crypt_bits: str, primary_digest: str, backup_digest: str) -> list[str]:
    bad = verify_after_step6(summary, crypt_bits)
    for k, want in FINAL_PURPOSES.items():
        if efuse_value(summary, k) != want and k not in ("KEY_PURPOSE_0", "KEY_PURPOSE_1", "KEY_PURPOSE_4"):
            bad.append(f"{k}={efuse_value(summary, k)}, expected {want}")
    for name, _, want in STEP7_EFUSES:
        if efuse_value(summary, name) != want:
            bad.append(f"{name}={efuse_value(summary, name)}, expected {want}")
    for name, want in MUST_STAY.items():
        if efuse_value(summary, name) != want:
            bad.append(f"{name}={efuse_value(summary, name)} -- must stay {want}")
    if efuse_value(summary, "RD_DIS") != RD_DIS_FINAL:
        bad.append(f"RD_DIS={efuse_value(summary, 'RD_DIS')}, expected {RD_DIS_FINAL}")
    for n in ("RD_DIS", "KEY_PURPOSE_5"):
        if summary[n]["writeable"]:
            bad.append(f"{n} is not write-protected")
    if block_hex(summary, "BLOCK_KEY2") != primary_digest:
        bad.append("BLOCK_KEY2 != the PRIMARY public-key digest")
    if block_hex(summary, "BLOCK_KEY3") != backup_digest:
        bad.append("BLOCK_KEY3 != the BACKUP public-key digest")
    if efuse_value(summary, "DIS_DOWNLOAD_MODE"):
        bad.append("DIS_DOWNLOAD_MODE is already set (step 9 must come last)")
    return bad


def parse_posture(console: str) -> dict | None:
    m = None
    for m in POSTURE_RE.finditer(console):
        pass
    return m.groupdict() if m else None


def check_step8_console(console: str, device_id: str, api_key: str) -> list[str]:
    bad = []
    p = parse_posture(console)
    want = {"profile": "release", "sb": "1", "fe": "release", "dl": "enabled", "jtag": "off",
            "nvs": "encrypted", "hmac": "key4", "sbslots": "0x3", "revoked": "0x4", "store": "encrypted"}
    if p is None:
        return ["no [sec] posture line on the console (wrong image? not booting? check `secure boot verification` lines)"]
    for k, v in want.items():
        if p[k] != v:
            bad.append(f"posture {k}={p[k]}, expected {v}")
    ids = IDENTITY_RE.findall(console)
    if device_id not in ids:
        bad.append("console did not show `[device] using provisioned identity` with this device id")
    fps = KEY_FP_RE.findall(console)
    if not fps:
        bad.append("no [sec] key_fp line (the identity in nvs_sec is unreadable?)")
    elif fps[-1] != key_fingerprint(api_key):
        bad.append("key_fp does not match the key this station minted -- wrong nvs_sec image")
    if FACTORY_WINDOW_LINE not in console:
        bad.append("console did not show the factory-window line (the image must keep a toy with download "
                   "mode open offline -- an image older than that fix?)")
    return bad


def check_step9_console(console: str) -> list[str]:
    bad = []
    p = parse_posture(console)
    if p is None:
        return ["no [sec] posture line after locking"]
    for k, v in {"dl": "disabled", "sb_rel": "1", "fe_rel": "1", "sb": "1", "fe": "release",
                 "store": "encrypted"}.items():
        if p[k] != v:
            bad.append(f"posture {k}={p[k]}, expected {v}")
    if KEY_FP_RE.search(console):
        bad.append("a key_fp line is still printed with download mode disabled")
    if FACTORY_WINDOW_LINE in console:
        bad.append("the toy still reports the factory window (offline) after download mode was disabled")
    return bad


# --------------------------------------------------------------------------
# Flow
# --------------------------------------------------------------------------
@dataclass
class Bundle:
    root: Path
    meta: dict
    files: dict  # name -> Path

    @staticmethod
    def load(root: Path) -> "Bundle":
        meta_path = root / "bundle.json"
        if not meta_path.is_file():
            raise StationError(f"{root} has no bundle.json (made by tools/firmware/sign_release.py)")
        meta = json.loads(meta_path.read_text())
        files = {}
        for name in ("bootloader", "partition_table", "boot_app0", "app"):
            art = meta.get("artifacts", {}).get(name)
            if not art:
                raise StationError(f"bundle.json lists no '{name}'")
            p = root / art["file"]
            if not p.is_file():
                raise StationError(f"bundle file missing: {p.name}")
            got = hashlib.sha256(p.read_bytes()).hexdigest()
            if got != art["sha256"]:
                raise StationError(f"{p.name}: sha256 {got[:16]}... != bundle.json {art['sha256'][:16]}...")
            files[name] = p
        return Bundle(root, meta, files)


@dataclass
class Options:
    mode: str                       # "provision" | "repair" | "dry-run" | "virt"
    bundle: Path
    port: str | None = None
    mac: str | None = None
    backend_url: str | None = None
    response: Path | None = None    # dry-run / virt registration response
    rotate_existing: bool = False
    reflash: bool = False
    confirm_mac: str | None = None
    trusted_digests: Path = DEFAULT_TRUSTED
    primary_pub: Path = DEFAULT_PRIMARY_PUB
    backup_pub: Path = DEFAULT_BACKUP_PUB
    partitions_csv: Path = pt.DEFAULT_PARTITIONS_CSV
    out_root: Path = DEFAULT_OUT_ROOT
    released_md: Path = gate_lib.RELEASED_MD
    workdir_root: Path = WORKDIR_ROOT
    efuse_file: Path | None = None  # virt
    console_seconds: float = 30.0
    pilot_board: bool = False       # a sacrificial hardware-pilot board (docs s12): no evidence needed, never shipped


class Flow:
    def __init__(self, o: Options, st: Station):
        self.o, self.st = o, st
        self.rehearsal = o.mode in ("dry-run", "virt")
        self.reg: dict | None = None      # set once the toy is registered
        self.chip: dict | None = None
        self.wd: Path | None = None
        self.last_step = "0"              # last COMPLETED step
        self.expected_mac: str | None = None  # the chip identified at step 1; re-read before every burn
        # True while flash encryption is on and Secure Boot is not (yet)
        # verified: nothing may boot the chip normally (see THE STEP 6 -> 7 WINDOW).
        self.normal_boot_forbidden = False

    # ---- eFuse access --------------------------------------------------
    def efuse_base(self) -> list[str]:
        if self.o.mode == "virt":
            return [*self.st.tools.espefuse(), "--chip", CHIP, "--virt", "--path-efuse-file", str(self.o.efuse_file)]
        return [*self.st.tools.espefuse(), "--chip", CHIP, "-p", self.o.port or "<PORT>"]

    def summary(self, wd: Path, name: str) -> tuple[dict, str]:
        out = wd / f"{name}.json"
        self.st.ok([*self.efuse_base(), "summary", "--format", "json", "--file", str(out)], "espefuse summary (json)")
        text = self.st.ok([*self.efuse_base(), "summary"], "espefuse summary")
        return json.loads(out.read_text()), crypt_cnt_bits(text)

    def confirm(self, step: str, mac: str) -> None:
        if self.o.mode == "virt":
            return
        token = f"BURN {mac.lower()}"
        if self.o.confirm_mac and self.o.confirm_mac.lower() == mac.lower():
            self.st.say(f"[confirm] {step}: pre-confirmed by --confirm-mac")
            return
        got = self.st.ask(f"\n{step} is IRREVERSIBLE on toy {mac}. Type exactly `{token}` to continue: ")
        if got.strip().lower() != token.lower():
            raise StationError(f"{step}: not confirmed -- nothing burned. The workdir is kept for `repair`.")

    def esptool_base(self) -> list[str]:
        """Every esptool call: this chip, this port, and NEVER a reset after."""
        return [*self.st.tools.esptool(), "--chip", CHIP, "-p", self.o.port or "<PORT>", *ESPTOOL_AFTER]

    def assert_same_chip(self, wd: Path, step: str) -> None:
        """Right before an irreversible espefuse call: the chip on the port is
        still the one identified at step 1 (a swapped / replugged board would
        get FE/HMAC keys for flash it does not hold, or be locked unverified)."""
        if self.o.mode == "dry-run" or not self.expected_mac:
            return
        out = wd / f"mac-check-{len(self.st.log)}.json"
        self.st.ok([*self.efuse_base(), "summary", "--format", "json", "--file", str(out)],
                   "espefuse summary (MAC check)")
        got = efuse_mac(json.loads(out.read_text()))
        if mac_tag(got) != mac_tag(self.expected_mac):
            burned = (wd / BURN_MARKER).exists() or self.o.mode == "repair"
            nxt = ("put the right toy back and run `repair` (never a fresh `provision`: it is half-burned)" if burned
                   else "put the right toy back and re-run `provision` (nothing was burned on it yet; the "
                        "registration is reused)")
            raise StationError(f"{step}: the chip on {self.o.port or 'the port'} is {got}, not {self.expected_mac} "
                               f"-- the toy was swapped or replugged. NOTHING was burned by this step; {nxt}.")

    def console(self) -> str:
        """Hard-resets the toy into a NORMAL boot and reads its console."""
        if self.normal_boot_forbidden:
            raise StationError("refusing to boot the toy normally: flash encryption is on and Secure Boot is not "
                               "verified yet -- the release bootloader would lock it by itself. Finish step 7 "
                               "first (`repair`).")
        return self.st.console(self.o.port, self.o.console_seconds)

    # ---- step 0 ----------------------------------------------------------
    def preflight(self) -> tuple[Bundle, str, str]:
        b = Bundle.load(self.o.bundle)
        test_ok = self.rehearsal
        if b.meta.get("test_signed") and not test_ok:
            raise StationError("this bundle is TEST-signed -- refusing to lock a real toy with it")
        trusted = [ln.split("#", 1)[0].strip().lower() for ln in self.o.trusted_digests.read_text().splitlines()]
        trusted = [d for d in trusted if d]
        if "TEST ONLY" in self.o.trusted_digests.read_text() and not test_ok:
            raise StationError(f"{self.o.trusted_digests} is a TEST ONLY digests file -- refusing a real run")
        if len(trusted) != 2:
            raise StationError(f"{self.o.trusted_digests} must list exactly 2 digests (primary, backup)")
        digests = []
        with tempfile.TemporaryDirectory() as tmp:
            for pub in (self.o.primary_pub, self.o.backup_pub):
                if not pub.is_file():
                    raise StationError(f"public key {pub} not found (the station holds PUBLIC keys only)")
                if b"PRIVATE KEY" in pub.read_bytes():
                    raise StationError(f"{pub.name} is a PRIVATE key -- private keys never come to the factory")
                d = Path(tmp) / "d.bin"
                self.st.ok([*self.st.tools.espsecure(), "digest-sbv2-public-key", "--keyfile", str(pub),
                            "--output", str(d)], "espsecure digest-sbv2-public-key")
                digests.append(d.read_bytes().hex())
        if digests != trusted:
            raise StationError("the public keys' digests do not equal sb_trusted_digests.txt (primary, backup) "
                               "-- wrong key files, or the digests file is stale")
        if TEST_TRUSTED.is_file() and not test_ok:
            test = {ln.strip().lower() for ln in TEST_TRUSTED.read_text().splitlines() if re.fullmatch(r"[0-9a-fA-F]{64}", ln.strip())}
            if set(digests) & test:
                raise StationError("a TEST signing key is configured -- refusing a real run")
        if self.o.mode in ("provision", "repair"):
            self.check_pilot(b)
        version = b.meta.get("version", "")
        min_status = bootloader_min_status(self.o.mode, self.o.pilot_board)
        self.st.ok([sys.executable, str(GATE), str(b.files["app"]), "--expect-version", version, "--profile", "release",
                    "--require-sbv2", "--trusted-digests", str(self.o.trusted_digests)], "release gate (app)")
        self.st.ok([sys.executable, str(GATE), str(b.files["bootloader"]), "--bootloader",
                    "--trusted-digests", str(self.o.trusted_digests), "--released-md", str(self.o.released_md),
                    "--min-status", min_status], "release gate (bootloader)")
        self.check_frozen_pieces(b, min_status)
        self.st.say(f"[0] bundle {version} verified (shas, signatures, both keys, RELEASED.md >= {min_status}, "
                    f"partition table == partitions.csv, boot_app0) -- digests {digests[0][:12]} / {digests[1][:12]}")
        return b, digests[0], digests[1]

    def check_frozen_pieces(self, b: Bundle, min_status: str) -> None:
        """What step 5 writes below the app can never change on a locked toy:
        the bootloader must be a RELEASED.md build approved for this run, the
        table the one recorded WITH it (and == partitions.csv, which step 4
        uses for nvs_sec), boot_app0 Arduino 3.3.8's. Checked here, in-process,
        as well as by the gate."""
        bl = b.files["bootloader"].read_bytes()
        bl_sha = hashlib.sha256(bl[:-gate_lib.SB_SECTOR]).hexdigest()
        bad = gate_lib.approved_bootloader_problems(bl_sha, min_status, self.o.released_md)
        row = gate_lib.release_row(bl_sha, self.o.released_md) if not bad else None
        pt_bytes = b.files["partition_table"].read_bytes()
        pt_sha = hashlib.sha256(pt_bytes).hexdigest()
        if row and row["partition_table_sha256"] != pt_sha:
            bad.append(f"partition-table.bin {pt_sha[:16]}... is not the table RELEASED.md records with this "
                       f"bootloader ({row['partition_table_sha256'][:16]}...)")
        bad += gate_lib.partition_table_problems(pt_bytes, self.o.partitions_csv)
        ba_sha = hashlib.sha256(b.files["boot_app0"].read_bytes()).hexdigest()
        if ba_sha != gate_lib.BOOT_APP0_SHA256:
            bad.append(f"boot_app0.bin {ba_sha[:16]}... is not arduino-esp32 3.3.8's "
                       f"({gate_lib.BOOT_APP0_SHA256[:16]}...)")
        if bad:
            raise StationError("the bundle's frozen pieces are refused (nothing touched the chip):\n  "
                               + "\n  ".join(bad))

    def check_pilot(self, b: Bundle) -> None:
        """A real toy is locked only with a bootloader + OTA path proven on
        silicon (docs/firmware-security.md s12). Rehearsals never get here."""
        if self.o.pilot_board:
            self.st.say("[0] --pilot-board: no pilot evidence required. This board is a SACRIFICIAL PILOT BOARD "
                        "-- it is recorded as such and must NEVER be shipped.")
            return
        bl = b.meta.get("artifacts", {}).get("bootloader", {}).get("sha256", "")
        pilot = b.meta.get("pilot") or {}
        if not pilot.get("evidence"):
            raise StationError("this bundle has no hardware-pilot evidence (bundle.json \"pilot\") -- run the pilot "
                               "on 2 sacrificial boards first (docs/firmware-security.md s12; `provision "
                               "--pilot-board` for those), record it with `sign_release.py add --pilot-evidence`")
        if pilot.get("bootloader_sha256", "").lower() != bl.lower():
            raise StationError("bundle.json's pilot entry names a different bootloader than the bundle carries")
        bad = pilot_evidence_problems(pilot["evidence"], bl, repo=PILOT_REPO_ROOT)
        if bad:
            raise StationError("hardware-pilot evidence refused:\n  " + "\n  ".join(bad))
        self.st.say(f"[0] pilot evidence {pilot['evidence']}: this bootloader passed '{PILOT_PASS_LINE}'")

    # ---- step 1 ----------------------------------------------------------
    def identify_chip(self, wd_hint: Path | None = None) -> dict:
        out = self.st.ok([*self.esptool_base(), "read-mac"], "esptool read-mac")
        m, c = MAC_RE.search(out), CHIP_RE.search(out)
        if not m or not c:
            raise StationError("could not read the chip type / MAC -- is this an ESP32-S3 in download mode?")
        mac = m.group(1).lower()
        if self.o.mac and mac_tag(self.o.mac) != mac_tag(mac):
            raise StationError(f"chip MAC {mac} != --mac {self.o.mac}")
        flash = FLASH_RE.search(self.st.ok([*self.esptool_base(), "flash-id"], "esptool flash-id") or "")
        if not flash or flash.group(1) != FLASH_SIZE:
            raise StationError(f"flash size is {flash.group(1) if flash else 'unknown'}, expected {FLASH_SIZE} (N8R8)")
        return {"mac": mac, "chip": c.group(1), "revision": f"v{c.group(2)}.{c.group(3)}", "flash": FLASH_SIZE}

    # ---- steps 3-4 -------------------------------------------------------
    def make_keys_and_images(self, wd: Path, b: Bundle, reg: dict) -> dict:
        fe, keys = wd / "fe.bin", wd / "keys"
        self.st.ok([*self.st.tools.espsecure(), "generate-flash-encryption-key", "--keylen", "512", str(fe)],
                   "espsecure generate-flash-encryption-key")
        self.st.ok([*self.st.tools.nvsgen(), "generate-key", "--key_protect_hmac", "--kp_hmac_keygen",
                    "--kp_hmac_keyfile", "hmac.bin", "--keyfile", "nvs_keys.bin", "--outdir", "."],
                   "nvs_partition_gen generate-key", cwd=wd)
        hmac, nvs_keys = keys / "hmac.bin", keys / "nvs_keys.bin"
        if fe.stat().st_size != 64 or hmac.stat().st_size != 32 or not nvs_keys.is_file():
            raise StationError("key generation produced unexpected sizes (FE key must be 64 B, HMAC key 32 B)")
        self.st.say("[3] flash-encryption key (512 bit) and NVS HMAC key generated in the workdir")
        enc = {}
        for name, addr, outname in (("bootloader", "0x0", "bl.enc"), ("partition_table", "0x8000", "pt.enc"),
                                    ("boot_app0", "0xe000", "ota.enc"), ("app", "0x10000", "app.enc")):
            self.st.ok([*self.st.tools.espsecure(), "encrypt-flash-data", "--aes-xts", "--keyfile", str(fe),
                        "--address", addr, "--output", str(wd / outname), str(b.files[name])],
                       f"espsecure encrypt-flash-data ({name})")
            enc[addr] = wd / outname
        self.st.ok([*self.st.tools.espsecure(), "decrypt-flash-data", "--aes-xts", "--keyfile", str(fe),
                    "--address", "0x10000", "--output", str(wd / "app.dec"), str(enc["0x10000"])],
                   "espsecure decrypt-flash-data (round trip)")
        if (wd / "app.dec").read_bytes() != b.files["app"].read_bytes():
            raise StationError("encrypt/decrypt round trip of the app does not match -- stop")
        (wd / "app.dec").unlink()
        off, size = partition_geometry(self.o.partitions_csv, "nvs_sec")
        csv = wd / "id.csv"
        csv.write_text(identity_csv(reg["deviceId"], reg["apiKey"], reg["pop"]))
        os.chmod(csv, 0o600)
        try:
            self.st.ok([*self.st.tools.nvsgen(), "encrypt", "id.csv", "nvs_sec.bin", hex(size),
                        "--inputkey", "keys/nvs_keys.bin"], "nvs_partition_gen encrypt", cwd=wd)
        finally:
            shred_file(self.st, csv)
        nvs_bin = wd / "nvs_sec.bin"
        if not nvs_bin.is_file() or nvs_bin.stat().st_size != size:
            raise StationError("nvs_sec.bin missing or wrong size")
        self.st.say(f"[4] images encrypted for this chip; nvs_sec.bin built ({size:#x} B @ {off:#x}); identity CSV shredded")
        return {"fe": fe, "hmac": hmac, "enc": enc, "nvs_sec": nvs_bin, "nvs_off": off}

    def write_args(self, art: dict, force: bool = False) -> list[str]:
        base = self.esptool_base()
        if force:
            base += ["--no-stub"]
        args = [*base, "write-flash"]
        if force:
            args += ["--force"]
        else:
            args += ["--flash-mode", "keep", "--flash-freq", "keep", "--flash-size", "keep"]
        for addr in ("0x0", "0x8000", "0xe000", "0x10000"):
            args += [addr, str(art["enc"][addr])]
        args += [hex(art["nvs_off"]).upper().replace("X", "x"), str(art["nvs_sec"])]
        return args

    # ---- console checks -------------------------------------------------
    def step8(self, reg: dict) -> str:
        text = self.console()
        bad = check_step8_console(text, reg["deviceId"], reg["apiKey"])
        if bad:
            raise StationError("step 8 verification FAILED -- do NOT lock this toy; workdir kept for `repair`:\n  "
                               + "\n  ".join(bad))
        self.st.say("[8] toy booted: Secure Boot + flash encryption + encrypted store, identity and key match")
        return text

    # ---- the whole flow -------------------------------------------------
    def run(self) -> int:
        o, st = self.o, self.st
        b, prim_d, back_d = self.preflight()
        chip = {"mac": o.mac or "00:00:00:00:00:00", "chip": "ESP32-S3", "revision": "n/a (rehearsal)", "flash": FLASH_SIZE}
        if o.mode == "repair":
            self.repair_precheck()   # eFuses FIRST: a half-burned chip must never boot normally
        if o.mode in ("provision", "repair"):
            chip = self.identify_chip()
            self.expected_mac = chip["mac"]
        self.chip = chip
        mac = chip["mac"]
        wd = self.workdir(mac)
        self.wd = wd
        try:
            if o.mode == "repair":
                return self.repair(b, chip, wd, prim_d, back_d)
            if self.rehearsal:
                # A rehearsal never keeps anything: TEST keys or not, the temp
                # workdir is shredded whatever happens.
                try:
                    return self.flow(b, chip, wd, prim_d, back_d)
                finally:
                    if wd.exists():
                        self.cleanup(wd)
            return self.flow(b, chip, wd, prim_d, back_d)
        except Exception:
            self.mark_incomplete()
            raise

    def mark_incomplete(self) -> None:
        """A run that stopped after registration leaves a record (no secrets)
        and a loud DO NOT SHIP: a toy that stopped after step 6/7 but before
        step 9 is HALF-secured (download mode open -- RAM code over the cable
        could use its HMAC_UP key to decrypt nvs_sec)."""
        if not self.reg or self.o.mode == "dry-run":
            return
        dev = self.reg["deviceId"]
        burned = bool(self.wd and (self.wd / BURN_MARKER).exists()) or self.o.mode == "repair"
        out = self.o.out_root / dev
        try:
            private_dir(self.o.out_root)
            out.mkdir(parents=True, exist_ok=True)
            (out / "INCOMPLETE.json").write_text(json.dumps({
                "station": STATION_VERSION, "mode": self.o.mode, "deviceId": dev,
                "mac": (self.chip or {}).get("mac"), "last_completed_step": self.last_step,
                "efuses_burned": burned, "pilot_board": self.o.pilot_board,
                "stopped_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            }, indent=2, sort_keys=True) + "\n")
        except OSError as e:
            self.st.say(f"(could not write INCOMPLETE.json: {e})")
        revoke = f"POST /api/internal/devices/{dev}/revoke"
        if self.last_step == "9-burned":
            self.st.say(f"DO NOT SHIP -- toy {dev}: download mode is burned but the locked-state check failed. "
                        f"No cable can repair it: set it aside for the owner, and revoke device {dev}: {revoke} "
                        f"unless it proves itself. Record: {out / 'INCOMPLETE.json'}")
        elif burned:
            self.st.say(f"DO NOT SHIP -- toy {dev} stopped after step {self.last_step} with eFuses burned (HALF-secured). "
                        f"Run `secure_provision.py repair` on THIS station now; if it cannot be finished, revoke "
                        f"device {dev}: {revoke} and set the toy aside. Record: {out / 'INCOMPLETE.json'}")
        else:
            self.st.say(f"DO NOT SHIP -- toy {dev} stopped after step {self.last_step} (nothing burned): re-run "
                        f"`provision` (it reuses this registration); if it will not be finished, revoke device "
                        f"{dev}: {revoke}. Record: {out / 'INCOMPLETE.json'}")

    def flow(self, b: Bundle, chip: dict, wd: Path, prim_d: str, back_d: str) -> int:
        o, st = self.o, self.st
        mac = chip["mac"]
        state_path = wd / "state.json"

        pre, bits = None, None
        if o.mode in ("provision", "virt"):
            pre, bits = self.summary(wd, "pre")
            if o.mode == "virt":
                self.expected_mac = efuse_mac(pre)   # the virtual chip's own MAC
            elif mac_tag(efuse_mac(pre)) != mac_tag(chip["mac"]):
                raise StationError(f"eFuse MAC {efuse_mac(pre)} != esptool read-mac {chip['mac']} -- stop")
            bad = check_virgin(pre, bits)
            if bad:
                raise StationError("chip is NOT virgin -- STOP (use `repair` only for a toy this station "
                                   "started):\n  " + "\n  ".join(bad))
            st.say(f"[1] chip {chip['chip']} {chip['revision']} {chip['flash']} mac {mac}: virgin eFuses")
            self.last_step = "1"

        if o.mode == "provision" and state_path.is_file():
            reg = json.loads(state_path.read_text())["reg"]
        else:
            reg = self.registration(mac)
            state_path.write_text(json.dumps({"reg": reg, "chip": chip, "bundle": str(o.bundle)}))
            os.chmod(state_path, 0o600)
        self.reg = reg
        st.say(f"[2] device id {reg['deviceId']}")  # the id is not secret (it is in the QR)
        self.last_step = "2"
        art = self.make_keys_and_images(wd, b, reg)
        self.last_step = "4"

        flash_cmds = [[*self.esptool_base(), "erase-flash"], self.write_args(art)]
        step6 = [*self.efuse_base(), "--do-not-confirm",
                 *plan_step6(pre or virgin_summary_stub(), bits or "000", art["fe"], art["hmac"])]
        if o.mode == "dry-run":
            st.say("[dry-run] steps 5-9 would run:")
            for cmd in flash_cmds:
                st.say("  " + " ".join(cmd))
            st.say("  " + " ".join(step6))
            st.say("  " + " ".join([*self.efuse_base(), "--do-not-confirm",
                                     *plan_step7(stub_after_step6(), o.primary_pub, o.backup_pub)]))
            st.say("  <reset; check the [sec] posture line, identity and key_fp>")
            st.say("  " + " ".join([*self.efuse_base(), "--do-not-confirm", "burn-efuse", "DIS_DOWNLOAD_MODE", "1"]))
            self.cleanup(wd)
            return 0

        if o.mode == "provision":
            for cmd in flash_cmds:
                st.ok(cmd, "esptool flash")
            st.say("[5] encrypted images + nvs_sec written to the still-blank chip")
        else:
            st.say("[5] (virt) flash write skipped -- no chip")
        self.last_step = "5"

        st.say("[!] steps 6 and 7 run back to back under ONE confirmation: do NOT unplug or reset the toy. A normal "
               "boot after step 6 but before step 7 would let the release bootloader enable Secure Boot by itself "
               "and disable download mode before step 8 can verify anything.")
        self.confirm("steps 6 AND 7 (flash encryption + HMAC key + JTAG off, then Secure Boot -- back to back, "
                     "no further prompt)", mac)
        self.assert_same_chip(wd, "step 6")
        (wd / BURN_MARKER).write_text("step6\n")
        self.normal_boot_forbidden = True
        st.ok(step6, "espefuse step 6")
        s6, bits6 = self.summary(wd, "after6")
        bad = verify_after_step6(s6, bits6)
        if bad:
            raise StationError("step 6 summary check FAILED (workdir kept; run `repair`):\n  " + "\n  ".join(bad))
        st.say("[6] eFuses: XTS-AES-256 key, HMAC_UP key, CRYPT_CNT=0b111, JTAG off -- verified")
        self.last_step = "6"

        self.assert_same_chip(wd, "step 7")
        st.ok([*self.efuse_base(), "--do-not-confirm", *plan_step7(s6, o.primary_pub, o.backup_pub)], "espefuse step 7")
        s7, bits7 = self.summary(wd, "after7")
        bad = verify_after_step7(s7, bits7, prim_d, back_d)
        if bad:
            raise StationError("step 7 summary check FAILED (workdir kept; run `repair`):\n  " + "\n  ".join(bad))
        self.normal_boot_forbidden = False   # Secure Boot verified: a normal boot is safe again
        st.say("[7] eFuses: both Secure Boot digests, slot 2 revoked, SECURE_BOOT_EN, RD_DIS write-protected -- verified")
        self.last_step = "7"

        if o.mode == "provision":
            self.step8(reg)
        else:
            st.say("[8] (virt) boot verification skipped -- no chip")
        self.last_step = "8"
        return self.lock_and_finish(b, chip, wd, reg, s7)

    def lock_and_finish(self, b: Bundle, chip: dict, wd: Path, reg: dict, s7: dict) -> int:
        o, st = self.o, self.st
        mac = chip["mac"]
        self.confirm("step 9 (DISABLE ROM DOWNLOAD MODE -- the toy can never be cable-flashed again)", mac)
        self.assert_same_chip(wd, "step 9")
        st.ok([*self.efuse_base(), "--do-not-confirm", "burn-efuse", "DIS_DOWNLOAD_MODE", "1"], "espefuse step 9")
        self.last_step = "9-burned"  # download mode is off for good; only the check is left
        if o.mode == "virt":
            s9, _ = self.summary(wd, "after9")
            if not efuse_value(s9, "DIS_DOWNLOAD_MODE"):
                raise StationError("(virt) DIS_DOWNLOAD_MODE not set after step 9")
            st.say("[9] (virt) DIS_DOWNLOAD_MODE verified in the virtual eFuses")
        else:
            text = self.console()
            bad = check_step9_console(text)
            if bad:
                raise StationError("step 9 verification FAILED (download mode is burned; the toy is locked -- "
                                   "set it aside for the owner):\n  " + "\n  ".join(bad))
            st.say("[9] locked: dl=disabled sb_rel=1 fe_rel=1, no key fingerprint on the console")
        self.last_step = "9"
        self.cleanup(wd)
        st.say("[10] workdir shredded (keys, images, identity)")
        self.record(b, chip, reg, s7)
        return 0

    def repair_precheck(self) -> None:
        """Before ANYTHING else in `repair` (even read-mac): the eFuses. While
        CRYPT_CNT=0b111 and SECURE_BOOT_EN=0 the toy must not boot normally
        until step 7 is verified -- the release bootloader would lock it."""
        with tempfile.TemporaryDirectory() as tmp:
            s, bits = self.summary(Path(tmp), "repair-pre")
        if half_burned(s, bits):
            self.normal_boot_forbidden = True
            self.st.say("[repair] flash encryption ON, Secure Boot OFF: no normal boot until step 7 is verified "
                        "(esptool --after no-reset only; step 7 runs before anything else)")

    def repair(self, b: Bundle, chip: dict, wd: Path, prim_d: str, back_d: str) -> int:
        o, st = self.o, self.st
        state = wd / "state.json"
        if not state.is_file() or not (wd / "fe.bin").is_file() or not (wd / "keys" / "hmac.bin").is_file():
            raise StationError(f"no kept workdir for {chip['mac']} ({wd}) -- repair is only possible on the station "
                               f"that started this toy, before step 9, with its workdir intact")
        reg = json.loads(state.read_text())["reg"]
        self.reg = reg
        self.last_step = "repair-start"
        s, bits = self.summary(wd, "repair")
        if efuse_value(s, "DIS_DOWNLOAD_MODE"):
            raise StationError("download mode is already disabled -- nothing a cable can repair")
        if bits not in ("000", "111"):
            raise StationError(f"SPI_BOOT_CRYPT_CNT=0b{bits} -- not a state this station creates; set the toy aside")
        args6 = plan_step6(s, bits, wd / "fe.bin", wd / "keys" / "hmac.bin")
        args7_now = plan_step7(s, o.primary_pub, o.backup_pub)
        if args6 or args7_now:
            # One confirmation for whatever of 6 and 7 is left: no human wait in the window.
            self.confirm("repair: finish " + " and ".join(x for x, a in (("step 6", args6), ("step 7", args7_now)) if a),
                         chip["mac"])
        if args6:
            self.assert_same_chip(wd, "repair step 6")
            self.normal_boot_forbidden = True
            st.ok([*self.efuse_base(), "--do-not-confirm", *args6], "espefuse step 6 (repair)")
            s, bits = self.summary(wd, "repair6")
        bad = verify_after_step6(s, bits)
        if bad:
            raise StationError("after repair, step 6 still incomplete:\n  " + "\n  ".join(bad))
        args7 = plan_step7(s, o.primary_pub, o.backup_pub)
        if args7:
            self.assert_same_chip(wd, "repair step 7")
            st.ok([*self.efuse_base(), "--do-not-confirm", *args7], "espefuse step 7 (repair)")
            s, bits = self.summary(wd, "repair7")
        bad = verify_after_step7(s, bits, prim_d, back_d)
        if bad:
            raise StationError("after repair, step 7 still incomplete:\n  " + "\n  ".join(bad))
        self.normal_boot_forbidden = False
        if o.reflash:
            art = {"enc": {a: wd / n for a, n in (("0x0", "bl.enc"), ("0x8000", "pt.enc"), ("0xe000", "ota.enc"),
                                                    ("0x10000", "app.enc"))},
                   "nvs_sec": wd / "nvs_sec.bin", "nvs_off": partition_geometry(o.partitions_csv, "nvs_sec")[0]}
            # esptool refuses a write to a flash-encrypted chip without --force
            # (verified); the images are already encrypted for THIS chip.
            st.ok(self.write_args(art, force=True), "esptool write-flash --force (repair)")
            st.say("[repair] encrypted images rewritten")
        self.step8(reg)
        return self.lock_and_finish(b, chip, wd, reg, s)

    # ---- pieces ------------------------------------------------------------
    def workdir(self, mac: str) -> Path:
        if self.rehearsal:
            root = Path(tempfile.mkdtemp(prefix="areg-factory-rehearsal-", dir="/dev/shm" if Path("/dev/shm").is_dir() else None))
            return root
        if not self.o.workdir_root.parent.is_dir():
            raise StationError(f"{self.o.workdir_root.parent} does not exist -- keys must live on tmpfs (/dev/shm)")
        wd = self.o.workdir_root / mac_tag(mac)
        if self.o.mode == "repair":
            if not wd.is_dir():
                raise StationError(f"no kept workdir for {mac} ({wd}) -- repair is only possible on the station "
                                   f"that started this toy, before step 9, with its workdir intact")
            return wd
        if self.o.mode == "provision" and wd.exists() and any(wd.iterdir()):
            if (wd / BURN_MARKER).exists():
                raise StationError(f"{wd}: an earlier run on this toy already burned eFuses -- use `repair`, "
                                   f"never a fresh `provision`")
            # Interrupted before any eFuse burn (network, cable, gate...): nothing
            # irreversible happened. Keep only the registration and start over.
            for f in sorted(wd.rglob("*"), reverse=True):
                if f.is_file() and f.name != "state.json":
                    shred_file(self.st, f)
            self.st.say(f"[resume] an earlier run stopped before any burn; reusing its registration")
        wd.mkdir(parents=True, exist_ok=True)
        os.chmod(self.o.workdir_root, 0o700)
        os.chmod(wd, 0o700)
        return wd

    def registration(self, mac: str) -> dict:
        o = self.o
        if self.rehearsal:
            data = json.loads(o.response.read_text())
            data.setdefault("apiKey", "dtk_" + "0" * 32)  # rehearsal placeholder, never a real key
            if o.rotate_existing:
                data["pop"] = mint_pop()
            for f in ("deviceId", "pop"):
                if not data.get(f):
                    raise StationError(f"{o.response} lacks '{f}'")
            return data
        secret = os.environ.get("AREG_PROVISIONING_SECRET", "")
        if not o.backend_url:
            raise StationError("--backend-url is required")
        if not o.backend_url.startswith("https://"):
            raise StationError(f"--backend-url must be https:// (got {o.backend_url.split('://')[0]}://...) -- the "
                               "provisioning secret goes out and the toy's new device key comes back over it")
        if o.rotate_existing:
            rot = pt.rotate_existing_device(o.backend_url, mac, secret)
            return {"deviceId": rot["deviceId"], "apiKey": rot["apiKey"], "pop": mint_pop(), "rotated": True}
        return pt.register_device(o.backend_url, mac, secret)

    def cleanup(self, wd: Path) -> None:
        for p in sorted(wd.rglob("*"), reverse=True):
            if p.is_file():
                shred_file(self.st, p)
        shutil.rmtree(wd, ignore_errors=True)

    def record(self, b: Bundle, chip: dict, reg: dict, s7: dict) -> None:
        out = self.o.out_root / reg["deviceId"]
        private_dir(self.o.out_root)
        out.mkdir(parents=True, exist_ok=True)
        efuses = {k: v.get("value") for k, v in s7.items() if k.startswith(("KEY_PURPOSE", "BLOCK_KEY", "SECURE_BOOT",
                                                                             "DIS_", "SPI_BOOT", "RD_DIS", "SOFT_DIS",
                                                                             "ENABLE_SECURITY"))}
        record = {
            "station": STATION_VERSION, "mode": self.o.mode, "deviceId": reg["deviceId"],
            "chip": chip, "rotated": bool(reg.get("rotated")), "pilot_board": self.o.pilot_board,
            "pilot_evidence": (b.meta.get("pilot") or {}).get("evidence"),
            "bundle": {"version": b.meta.get("version"), "test_signed": bool(b.meta.get("test_signed")),
                       "artifacts": {k: v["sha256"] for k, v in b.meta["artifacts"].items()},
                       "idf_commit": b.meta.get("idf_commit"), "arduino_core": b.meta.get("arduino_core")},
            "efuses_after_step7": efuses,
            "finished_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        }
        (out / "factory-record.json").write_text(json.dumps(record, indent=2, sort_keys=True) + "\n")
        (out / "INCOMPLETE.json").unlink(missing_ok=True)
        self.st.say(f"[11] {out / 'factory-record.json'} (no secrets)")
        if self.o.pilot_board:
            self.st.say("[11] PILOT BOARD -- NEVER SHIP. Record the pilot results in tools/quality-evidence/ "
                        "(docs/firmware-security.md s12).")
        try:
            if reg.get("rotated") or not reg.get("claimCode"):
                render_pop_sticker(out, reg["deviceId"], reg["pop"])
            else:
                pt.render_label(out, reg["deviceId"], reg["claimCode"], reg["pop"], reg.get("qrPayload", "{}"))
        except ImportError as e:
            if not self.rehearsal:
                raise StationError(f"label rendering needs qrcode/reportlab ({e}); pip install -r tools/factory/requirements.txt")
            self.st.say(f"[11] label skipped in a rehearsal ({e})")


def private_dir(path: Path) -> None:
    """The factory output root: labels (claim code + PoP), records. Mode 700."""
    path.mkdir(parents=True, exist_ok=True)
    try:
        os.chmod(path, 0o700)
    except OSError:
        pass


def shred_file(st: Station, path: Path) -> None:
    r = st.run(["shred", "-u", str(path)])
    if path.exists():
        try:
            n = path.stat().st_size
            with open(path, "r+b") as f:
                f.write(secrets.token_bytes(n))
            path.unlink()
        except OSError:
            pass
    del r


def render_pop_sticker(out_dir: Path, device_id: str, pop: str) -> None:
    from reportlab.lib.pagesizes import A7
    from reportlab.pdfgen import canvas
    out_dir.mkdir(parents=True, exist_ok=True)
    c = canvas.Canvas(str(out_dir / "pop-sticker.pdf"), pagesize=A7)
    w, h = A7
    c.setFont("Helvetica", 8)
    c.drawCentredString(w / 2, h - 30, f"Areg {device_id[:8]}")
    c.drawCentredString(w / 2, h - 50, "Pairing code (Bluetooth setup)")
    c.setFont("Helvetica-Bold", 22)
    c.drawCentredString(w / 2, h - 85, pop)
    c.showPage()
    c.save()


def virgin_summary_stub() -> dict:
    """What a virgin chip's summary says -- used only to PRINT the step-6
    command in a dry run (no chip to read)."""
    s = {f"KEY_PURPOSE_{i}": {"value": "USER", "readable": True, "writeable": True} for i in range(6)}
    for name, _, _ in STEP6_EFUSES + STEP7_EFUSES:
        s[name] = {"value": False if name != "SOFT_DIS_JTAG" else 0, "readable": True, "writeable": True}
    for n in ("DIS_ICACHE", "RD_DIS"):
        s[n] = {"value": False if n == "DIS_ICACHE" else 0, "readable": True, "writeable": True}
    return s


def stub_after_step6() -> dict:
    s = virgin_summary_stub()
    for k in ("KEY_PURPOSE_0", "KEY_PURPOSE_1", "KEY_PURPOSE_4"):
        s[k]["value"] = FINAL_PURPOSES[k]
    return s


def main(argv=None, station: Station | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("mode", choices=["provision", "repair", "dry-run", "virt"])
    ap.add_argument("--bundle", type=Path, required=True, help="signed release bundle (bundle.json + images)")
    ap.add_argument("--port")
    ap.add_argument("--mac", help="expected MAC (provision: checked against the chip; rotate: the registered MAC)")
    ap.add_argument("--backend-url")
    ap.add_argument("--response", type=Path, help="dry-run/virt: a saved POST /api/devices/register response")
    ap.add_argument("--rotate-existing", action="store_true",
                    help="convert an already-registered toy: same device id, new key, locally minted PoP")
    ap.add_argument("--reflash", action="store_true", help="repair: rewrite the encrypted images (esptool --force)")
    ap.add_argument("--confirm-mac", help="pre-confirm the irreversible steps for exactly this MAC")
    ap.add_argument("--trusted-digests", type=Path, default=DEFAULT_TRUSTED)
    ap.add_argument("--sb-primary-pub", type=Path, default=DEFAULT_PRIMARY_PUB)
    ap.add_argument("--sb-backup-pub", type=Path, default=DEFAULT_BACKUP_PUB)
    ap.add_argument("--partitions-csv", type=Path, default=pt.DEFAULT_PARTITIONS_CSV)
    ap.add_argument("--out-root", type=Path, default=DEFAULT_OUT_ROOT,
                    help="labels + factory records (default ~/areg-factory-out, mode 700 -- outside the repository)")
    ap.add_argument("--released-md", type=Path, default=gate_lib.RELEASED_MD,
                    help="esp32/bootloader-release/RELEASED.md (which bootloader + table builds may be frozen in)")
    ap.add_argument("--efuse-file", type=Path, help="virt: the virtual eFuse file (created virgin if absent)")
    ap.add_argument("--console-seconds", type=float, default=30.0)
    ap.add_argument("--workdir-root", type=Path, default=WORKDIR_ROOT,
                    help="where per-toy key workdirs live (default /dev/shm/areg-factory -- keep it on tmpfs)")
    ap.add_argument("--pilot-board", action="store_true",
                    help="provision/repair a SACRIFICIAL hardware-pilot board without pilot evidence (never shipped)")
    a = ap.parse_args(argv)
    o = Options(mode=a.mode, bundle=a.bundle, port=a.port, mac=a.mac, backend_url=a.backend_url,
                response=a.response, rotate_existing=a.rotate_existing, reflash=a.reflash,
                confirm_mac=a.confirm_mac, trusted_digests=a.trusted_digests, primary_pub=a.sb_primary_pub,
                backup_pub=a.sb_backup_pub, partitions_csv=a.partitions_csv, out_root=a.out_root,
                efuse_file=a.efuse_file, console_seconds=a.console_seconds, workdir_root=a.workdir_root,
                pilot_board=a.pilot_board, released_md=a.released_md)
    st = station or Station()
    st.echo = st.echo or o.mode == "dry-run"
    try:
        if o.mode in ("provision", "repair") and not o.port:
            raise StationError("--port is required")
        if o.mode in ("dry-run", "virt") and not o.response:
            raise StationError("--response RESPONSE_JSON is required for a rehearsal")
        if o.mode == "virt" and not o.efuse_file:
            o.efuse_file = Path(tempfile.mkdtemp(prefix="areg-virt-efuse-")) / "efuse.bin"
            st.say(f"[virt] virtual eFuses: {o.efuse_file} (virgin)")
        if not o.trusted_digests.is_file():
            raise StationError(f"{o.trusted_digests} not found -- create esp32/security/ (see its README)")
        return Flow(o, st).run()
    except (StationError, pt.ProvisionError, OSError, json.JSONDecodeError) as e:
        st.say(f"FAIL - {e}")
        return 1


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Refuse a firmware image that must not be released.

WHY THIS EXISTS. On 2026-08-13 a 1.2.1 image was built on the release machine
and pushed to the repo carrying the owner's REAL device id and API key
(`dtk_<32 hex>`, the exact format DeviceService mints). The rule against that
was already written down — the 1.1.x work established that OTA images ship
with PLACEHOLDER credentials, "because an OTA image reaches every toy, and one
toy's secret must never ride inside it" — and it was written down in prose, in
a runbook, as a step a human performs. It was skipped on the first release
after it was written.

Had it shipped: `device_creds` reads NVS first and falls back to the compiled
values, so every factory-fresh or re-flashed toy that installed the image would
have authenticated to the backend as that one toy.

So the check is a program now, not a paragraph. Dependency-free — no ffmpeg, no
dotnet, no Arduino toolchain — because a check that needs a toolchain is the
check that gets skipped on the day it matters. Same reasoning, and the same
shape, as tools/story-audio/check_story_audio.py.

It never prints a secret it finds. It reports the KIND and the COUNT, because
the whole point is that the value must not travel any further.

USAGE
    python3 tools/firmware/check_release_image.py <image.bin> [--expect-version 1.2.1]
                                                 [--forbid-version 1.2.0]
    # chip-security release (2026-10-08, docs/firmware-security.md s10):
    python3 tools/firmware/check_release_image.py app-unsigned.bin \
        --expect-version 1.4.0 --profile release
    python3 tools/firmware/check_release_image.py app-signed.bin \
        --expect-version 1.4.0 --profile release \
        --require-sbv2 --trusted-digests esp32/security/sb_trusted_digests.txt
    python3 tools/firmware/check_release_image.py bootloader-signed.bin \
        --bootloader --trusted-digests esp32/security/sb_trusted_digests.txt

--profile release   the image must be what a LOCKED toy runs: ESP32-S3 app
                    header (magic 0xE9, chip id 9, dio/80m/8MB), exactly one
                    signed in-image marker AREGFWV1:<version>:<board-sb>:release,
                    none of the bench/fallback markers (shared PoP
                    "areg-pair", TLS-insecure banner, compile-time fallback
                    identity/Wi-Fi, bench banners), and at least
                    MIN_TLS_ANCHORS PEM root certificates (a locked toy's only
                    update path is TLS to the backend: one pinned root would
                    strand the whole locked fleet on a CA move).
--require-sbv2      Secure Boot V2 (RSA-3072-PSS) signature: signed-app shape
                    (4 KB multiple, body a whole number of 64 KB pages,
                    <= 3 MB), at least one block that VERIFIES with a key in
                    --trusted-digests, and no block that does not (an
                    untrusted key, a bad CRC, a stale digest). Pure stdlib
                    RSA -- this tool still needs no toolchain and no pip.
--bootloader        the signed RELEASE bootloader: <= 0x8000, ESP32-S3
                    header, EXACTLY two verifying blocks covering BOTH
                    trusted digests (primary + backup -- a bootloader signed
                    by one key bricks every toy once that key is revoked),
                    AND its unsigned body (the image minus the 4 KB signature
                    sector) is a build recorded in
                    esp32/bootloader-release/RELEASED.md whose Status is at
                    least --min-status (default APPROVED). A signature only
                    proves WHO signed; this proves WHAT is frozen into a toy
                    for life -- e.g. the app-config bootloader build_idf.sh
                    also produces (no CONFIG_SECURE_BOOT: it never verifies
                    the app) would carry two good signatures too.
--released-md FILE  the RELEASED.md to read (default: this repository's)
--min-status S      CANDIDATE < PILOT < APPROVED. APPROVED = what real toys
                    get (the owner sets it after the hardware pilot and the
                    anti-rollback decision); PILOT = the two sacrificial
                    pilot boards; CANDIDATE = TEST rehearsals only.

Exit code 0 = safe to stage. Non-zero = do not release.
"""
import argparse
import hashlib
import re
import struct
import sys
import zlib
from pathlib import Path

# Printable ASCII runs, the same thing `strings` extracts.
STRING_RE = re.compile(rb"[\x20-\x7e]{4,}")

# A device API key. DeviceService.cs mints `dtk_{Guid:N}` — 32 hex chars.
DEVICE_KEY_RE = re.compile(r"^dtk_[0-9a-fA-F]{32}$")

# A device id. Also matches BLE service UUIDs, which is why a hit is reported
# rather than silently ignored: an operator looks once and decides, instead of
# the tool guessing and being wrong in the unsafe direction.
GUID_RE = re.compile(
    r"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")

# What a correctly-built OTA image carries instead of real credentials.
PLACEHOLDERS = ("YOUR_DEVICE_GUID", "YOUR_DEVICE_API_KEY")

# A BLE provisioning PoP (factory pairing, 2026-09-11). DeviceService.cs mints
# 8 chars from this exact alphabet -- excludes I, L, O, U, 0, 1 because the
# code is printed on the box and read aloud. A hit here means AREG_BLE_POP
# was left defined with a real value in a build that became an OTA image
# (config.h.example's own placeholder "YOURBLEPOP" is 10 chars and contains
# excluded letters, so it can never match this by construction).
POP_ALPHABET = "ABCDEFGHJKMNPQRSTVWXYZ23456789"
POP_RE = re.compile(rf"^[{POP_ALPHABET}]{{8}}$")

# The bench-only shared fallback (AREG_PROV_POP default, ble_provisioning.cpp)
# is fine to ship -- it carries no per-device secret. It never matches POP_RE
# anyway (lowercase, contains a hyphen), listed for readability only.
#
# The other four are coincidental library artifacts, NOT related to any
# build flag: a plain esp32:esp32@3.3.8 image with nothing PoP-related
# defined still contains them (confirmed 2026-09-11 against a real compiled
# AregVoiceMvp.ino.bin -- see tools/firmware/test_check_release_image.py's
# note). "ESPHTTPD"/"EXCVADDR" are literal identifiers from the ESP-IDF core
# (an HTTP server name, a panic-handler register name); "BBB6BHHB"/
# "B8BH8B4B" are fixed-width binary struct-format descriptors from a core
# library, not ASCII text at all -- they only decode as 8 printable bytes by
# chance. Same "exact-value only, never widen the pattern" discipline as
# KNOWN_LIBRARY_GUIDS above: if the next core bump introduces a NEW
# coincidental hit, read the FAIL line, confirm by hand it is not a real PoP
# (it will not be 8 chars of a printf'd credential near AREG_BLE_POP in the
# source), and add the exact string here -- never loosen POP_RE itself.
KNOWN_SAFE_POP_STRINGS = {"areg-pair", "ESPHTTPD", "EXCVADDR", "BBB6BHHB", "B8BH8B4B"}

# The 8 MB whole-flash artifact. Serving it over OTA writes a bootloader and a
# partition table into a 3 MB app slot; the runbook says it would produce an
# unbootable toy. Size alone catches it long before anything else does.
OTA_SLOT_BYTES = 3 * 1024 * 1024

# ota_apply.cpp skips manifest HMAC verification whenever AREG_MANIFEST_HMAC_KEY
# is "" (the config.h.example default) and logs this exact marker instead of
# failing loudly -- a deliberate Stage-A bench allowance
# (tools/firmware/README.md history: signature checking arrived before the
# backend had a key to sign with). The release gate never checked for it, so
# an image built with no key -- the default, unless a release machine sets
# one -- could ship over OTA to every toy with signature verification
# silently off. The string is a compile-time literal Serial.println() always
# embeds in .rodata when the branch is reachable, so scanning the image bytes
# finds it exactly like every other marker in this file, no toolchain needed.
# Keep this in sync with ota_apply.cpp's log line -- never reword one without
# the other.
OTA_SIG_CHECK_DISABLED_MARKER = "OTA_SIG_CHECK_DISABLED"


def extract_strings(data: bytes) -> list[str]:
    return [m.group().decode("ascii") for m in STRING_RE.finditer(data)]


# ---------------------------------------------------------------------------
# Chip security (2026-10-08). Secure Boot V2 signature block, ESP32-S3:
# 4096-byte sector at the end, up to 3 blocks of 1216 bytes -- magic 0xE7,
# version 2, SHA-256 of the body at 4, RSA n (LE) at 36, e (LE u32) at 420,
# R/M' up to 812, RSA-PSS signature (LE) at 812, CRC32 at 1196. Key digest =
# SHA-256(block[36:812]) -- the 32 bytes espefuse burns. Mirrors
# esp32/AregVoiceMvp/sbv2_rules.h; the RSA verify is RFC 8017 RSASSA-PSS
# (MGF1-SHA256, salt 32), ported from the design's reference that agreed with
# the ROM bootloader on every QEMU vector.
# ---------------------------------------------------------------------------
SB_SECTOR, SB_BLOCK, SB_MAX_BLOCKS = 4096, 1216, 3
MMU_PAGE = 0x10000
BOOTLOADER_MAX = 0x8000          # partition table at 0x8000
CHIP_ID_ESP32S3 = 9
FLASH_MODE_DIO = 2
FLASH_BYTE_8MB_80M = 0x3F        # size 8 MB (3) << 4 | 80 MHz (0xF)
MARKER_MAGIC = b"AREGFWV1:"
# A whole PEM certificate block (header, base64 body, footer). The bare header
# alone is NOT counted: mbedTLS's own x509 parser carries the literal
# "-----BEGIN CERTIFICATE-----\0" in every image, anchors or not.
PEM_CERT_RE = re.compile(rb"-----BEGIN CERTIFICATE-----\n(?:[A-Za-z0-9+/=]{1,76}\n){2,}-----END CERTIFICATE-----")
MIN_TLS_ANCHORS = 3   # == security_profile.h's AREG_CA_ANCHOR_COUNT floor
MARKER_RE = re.compile(rb"AREGFWV1:([0-9.]+):([\x21-\x39\x3b-\x7e]+):([a-z]+)\x00")
RELEASE_FORBIDDEN = (
    # (raw bytes, why)
    (b"areg-pair", "the shared fallback BLE PoP (C150) -- a release toy has only its own"),
    (b"TLS INSECURE", "the TLS-insecure banner (AREG_TLS_INSECURE)"),
    (b"using compile-time fallback creds", "compiled-in Wi-Fi fallback"),
    (b"using compile-time identity", "compiled-in device identity fallback"),
    (b"using the bench fallback", "bench PoP fallback path"),
    (b"[sd-bench]", "AREG_SD_BENCH_TEST banner"),
    (b"[sd-diag] bench fw built", "AREG_SD_DIAG_BENCH banner"),
    (b"[sd-playback] bench fw built", "AREG_SD_PLAYBACK_BENCH banner"),
    (b"[fallback-test] bench fw built", "AREG_STORY_SD_FALLBACK_TEST_BENCH banner"),
    (b"[cs-test]", "AREG_CONTENT_SYNC_TEST_BENCH banner"),
    (b"[sel-test]", "AREG_STORY_SELECT_TEST_BENCH banner"),
    (b"bench I2S conflict isolation", "AREG_DISABLE_MP3_PLAYBACK bench build"),
    # ESP8266Audio's AudioFileSourceHTTPStream (and its ICY subclass): a plain
    # NetworkClient -- HTTPClient::begin(client, "https://...") only sets port
    # 443, it never adds TLS. Linked into a release image it would fetch story
    # audio (token in the URL) in cleartext and play whatever came back.
    (b"Can't open HTTP request", "ESP8266Audio's plain-TCP HTTP stream (no TLS) -- audio injection"),
    (b"AudioFileSourceHTTPStream::", "ESP8266Audio's plain-TCP HTTP stream (no TLS) -- audio injection"),
)


# ---------------------------------------------------------------------------
# What may be frozen into a locked toy (review round 3). The bootloader, the
# partition table and otadata can never change after the factory; a valid
# SIGNATURE does not say which BUILD was signed. The approved builds are rows
# of esp32/bootloader-release/RELEASED.md (unsigned bootloader sha256 +
# partition-table sha256 + Status); boot_app0.bin is Arduino's fixed file.
# ---------------------------------------------------------------------------
REPO_ROOT = Path(__file__).resolve().parents[2]
RELEASED_MD = REPO_ROOT / "esp32" / "bootloader-release" / "RELEASED.md"
# arduino-esp32 3.3.8 tools/partitions/boot_app0.bin (the otadata the factory
# writes at 0xe000; identical in the core package and the git tag 3.3.8).
BOOT_APP0_SHA256 = "f94c5d786a7a8fab06ac5d10e33bf37711a6697636dc037559ea19cc410a17f0"
BOOTLOADER_STATUSES = ("CANDIDATE", "PILOT", "APPROVED")   # ascending
SHA256_RE = re.compile(r"\b[0-9a-f]{64}\b")


def released_rows(path: Path = RELEASED_MD) -> list[dict]:
    """The build table of RELEASED.md: one dict per row with
    bootloader_unsigned_sha256, partition_table_sha256 and status (the first
    upper-case word of the Status cell -- anything not in
    BOOTLOADER_STATUSES, e.g. SUPERSEDED, is never approved)."""
    rows, cols = [], None
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line.lstrip().startswith("|"):
            cols = None if rows else cols
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if cols is None:
            low = [c.lower() for c in cells]
            bl = next((i for i, c in enumerate(low) if "bootloader-unsigned.bin" in c), None)
            pt = next((i for i, c in enumerate(low) if "partition-table.bin" in c), None)
            st = next((i for i, c in enumerate(low) if c == "status"), None)
            if None not in (bl, pt, st):
                cols = (bl, pt, st)
            continue
        if set("".join(cells)) <= set("-: "):
            continue  # the |---|---| separator
        if len(cells) <= max(cols):
            continue
        bl_sha = SHA256_RE.search(cells[cols[0]].lower())
        pt_sha = SHA256_RE.search(cells[cols[1]].lower())
        status = re.match(r"[A-Z]+", cells[cols[2]].lstrip("*_` "))
        if bl_sha and pt_sha:
            rows.append({"bootloader_unsigned_sha256": bl_sha.group(0),
                         "partition_table_sha256": pt_sha.group(0),
                         "status": status.group(0) if status else ""})
    return rows


def status_at_least(status: str, minimum: str) -> bool:
    if status not in BOOTLOADER_STATUSES or minimum not in BOOTLOADER_STATUSES:
        return False
    return BOOTLOADER_STATUSES.index(status) >= BOOTLOADER_STATUSES.index(minimum)


def release_row(bootloader_unsigned_sha256: str, path: Path = RELEASED_MD) -> dict | None:
    """The LAST row recording this unsigned bootloader (a later row may
    re-pin its partition table), or None."""
    hits = [r for r in released_rows(path) if r["bootloader_unsigned_sha256"] == bootloader_unsigned_sha256.lower()]
    return hits[-1] if hits else None


def approved_bootloader_problems(unsigned_sha256: str, minimum: str, path: Path = RELEASED_MD) -> list[str]:
    try:
        row = release_row(unsigned_sha256, path)
    except OSError as e:
        return [f"cannot read {path}: {e}"]
    if row is None:
        return [f"unsigned bootloader {unsigned_sha256[:16]}... is not a build recorded in {path.name} -- "
                "only a recorded esp32/bootloader-release build may be frozen into a toy (e.g. NOT the "
                "app-config bootloader build_idf.sh also produces: no CONFIG_SECURE_BOOT, it never verifies the app)"]
    if not status_at_least(row["status"], minimum):
        return [f"unsigned bootloader {unsigned_sha256[:16]}... is '{row['status'] or '?'}' in {path.name}; "
                f"this use needs at least {minimum} (the owner sets APPROVED after the hardware pilot and the "
                "anti-rollback decision -- docs/firmware-security.md s11)"]
    return []


# The partition table binary (ESP-IDF gen_esp32part.py format): 32-byte
# entries <2sBBLL16sL> (magic AA 50, type, subtype, offset, size, name,
# flags), then an MD5 entry (EB EB + 14 x FF + md5 of the entries), padded
# with FF to 0xC00. The factory station parses the bundle's table to prove
# it is the repository's partitions.csv before anything is written.
PART_MAGIC, PART_MD5_MAGIC, PART_TABLE_LEN = b"\xaa\x50", b"\xeb\xeb", 0xC00
PART_TYPES = {"app": 0x00, "data": 0x01}
PART_SUBTYPES = {
    "app": {"factory": 0x00, "test": 0x20, **{f"ota_{i}": 0x10 + i for i in range(16)}},
    "data": {"ota": 0x00, "phy": 0x01, "nvs": 0x02, "coredump": 0x03, "nvs_keys": 0x04, "efuse": 0x05,
             "undefined": 0x06, "esphttpd": 0x80, "fat": 0x81, "spiffs": 0x82, "littlefs": 0x83},
}
PART_FLAGS = {"encrypted": 1 << 0, "readonly": 1 << 1}


def partitions_from_csv(path: Path) -> list[dict]:
    """The rows of a partitions.csv with explicit offsets (this repo's style)."""
    rows = []
    for line in path.read_text().splitlines():
        line = line.split("#", 1)[0].strip()
        if not line:
            continue
        cells = [c.strip() for c in line.split(",")] + [""] * 6
        name, typ, sub, off, size, flags = cells[:6]
        flag_bits = 0
        for f in (x.strip() for x in flags.split(":") if x.strip()):
            flag_bits |= PART_FLAGS[f]
        rows.append({"name": name, "type": PART_TYPES[typ], "subtype": PART_SUBTYPES[typ][sub],
                     "offset": int(off, 0), "size": int(size, 0), "flags": flag_bits})
    return rows


def encode_partition_table(rows: list[dict]) -> bytes:
    """Byte-for-byte what IDF's gen_esp32part.py writes (MD5 entry on)."""
    out = b"".join(struct.pack("<2sBBLL16sL", PART_MAGIC, r["type"], r["subtype"], r["offset"], r["size"],
                               r["name"].encode(), r["flags"]) for r in rows)
    out += PART_MD5_MAGIC + b"\xff" * 14 + hashlib.md5(out).digest()
    return out + b"\xff" * (PART_TABLE_LEN - len(out))


def parse_partition_table(data: bytes) -> list[dict]:
    """Inverse of encode_partition_table; raises ValueError on anything else
    (no MD5 entry, a bad MD5, a stray entry)."""
    rows, i = [], 0
    while i + 32 <= len(data):
        e = data[i:i + 32]
        if e[:2] == PART_MAGIC:
            _, typ, sub, off, size, name, flags = struct.unpack("<2sBBLL16sL", e)
            rows.append({"name": name.rstrip(b"\x00").decode("ascii", "replace"), "type": typ, "subtype": sub,
                         "offset": off, "size": size, "flags": flags})
        elif e[:2] == PART_MD5_MAGIC:
            if hashlib.md5(data[:i]).digest() != e[16:32]:
                raise ValueError("partition table MD5 does not match its entries")
            if set(data[i + 32:]) - {0xFF}:
                raise ValueError("bytes after the partition table's MD5 entry")
            return rows
        else:
            raise ValueError(f"partition table entry {i // 32} has no AA 50 / EB EB magic")
        i += 32
    raise ValueError("partition table has no MD5 entry")


def partition_table_problems(data: bytes, csv_path: Path) -> list[str]:
    """The bundle's binary table must BE the repository's partitions.csv --
    the station writes nvs_sec at the CSV's offset, so a different table is
    only noticed after the irreversible burns (or never, if it still boots)."""
    try:
        got = parse_partition_table(data)
    except ValueError as e:
        return [f"partition-table.bin: {e}"]
    want = partitions_from_csv(csv_path)
    if got == want:
        return []
    def fmt(r: dict | None) -> str:
        return "absent" if r is None else (f"type {r['type']}/{r['subtype']:#x} @ {r['offset']:#x} size {r['size']:#x} "
                                           f"flags {r['flags']}")
    g, w = {r["name"]: r for r in got}, {r["name"]: r for r in want}
    bad = [f"partition-table.bin '{n}': {fmt(g.get(n))}; {csv_path.name}: {fmt(w.get(n))}"
           for n in sorted(set(g) | set(w), key=lambda n: (n != "nvs_sec", n)) if g.get(n) != w.get(n)]
    return bad or [f"partition-table.bin differs from {csv_path.name} (row order)"]


def _mgf1(seed: bytes, length: int) -> bytes:
    out, c = b"", 0
    while len(out) < length:
        out += hashlib.sha256(seed + struct.pack(">I", c)).digest()
        c += 1
    return out[:length]


def pss_verify(n: int, e: int, m_hash: bytes, sig: bytes, s_len: int = 32) -> bool:
    """RFC 8017 s8.1.2 + s9.1.2 with SHA-256 / MGF1-SHA256."""
    mod_bits = n.bit_length()
    k = (mod_bits + 7) // 8
    if len(sig) != k:
        return False
    s = int.from_bytes(sig, "big")
    if s >= n:
        return False
    em_bits = mod_bits - 1
    em_len = (em_bits + 7) // 8
    em = pow(s, e, n).to_bytes(k, "big")[k - em_len:]
    h_len = 32
    if em_len < h_len + s_len + 2 or em[-1] != 0xBC:
        return False
    masked_db, h = em[:em_len - h_len - 1], em[em_len - h_len - 1:-1]
    unused = 8 * em_len - em_bits
    if unused and masked_db[0] >> (8 - unused):
        return False
    db = bytes(a ^ b for a, b in zip(masked_db, _mgf1(h, len(masked_db))))
    if unused:
        db = bytes([db[0] & (0xFF >> unused)]) + db[1:]
    ps_len = em_len - h_len - s_len - 2
    if any(db[:ps_len]) or db[ps_len] != 0x01:
        return False
    salt = db[-s_len:]
    return hashlib.sha256(b"\x00" * 8 + m_hash + salt).digest() == h


def read_trusted_digests(path: Path) -> list[str]:
    """One 64-hex-char SHA-256 public-key digest per line; '#' comments."""
    out = []
    for line in path.read_text().splitlines():
        line = line.split("#", 1)[0].strip().lower()
        if not line:
            continue
        if not re.fullmatch(r"[0-9a-f]{64}", line):
            raise ValueError(f"not a 32-byte hex digest: {line[:20]}...")
        out.append(line)
    return out


def sbv2_blocks(img: bytes, trusted: set[str]):
    """Walk the signature sector. Returns (verified_digests, problems).
    A problem is any block that is present but does NOT verify."""
    verified, problems = [], []
    if len(img) < 2 * SB_SECTOR or len(img) % SB_SECTOR:
        return verified, ["length is not a positive multiple of 4096 -- not a signed image"]
    body, sector = img[:-SB_SECTOR], img[-SB_SECTOR:]
    digest = hashlib.sha256(body).digest()
    for i in range(SB_MAX_BLOCKS):
        b = sector[i * SB_BLOCK:(i + 1) * SB_BLOCK]
        if b[0] != 0xE7 or b[1] != 0x02:
            break
        if zlib.crc32(b[:1196]) & 0xFFFFFFFF != struct.unpack_from("<I", b, 1196)[0]:
            problems.append(f"signature block {i}: bad CRC (corrupted)")
            break
        kd = hashlib.sha256(b[36:812]).hexdigest()
        if b[4:36] != digest:
            problems.append(f"signature block {i}: image digest mismatch (image modified after signing)")
            continue
        if kd not in trusted:
            problems.append(f"signature block {i}: signed by an UNTRUSTED key ({kd[:16]}...)")
            continue
        n = int.from_bytes(b[36:420], "little")
        e = struct.unpack_from("<I", b, 420)[0]
        if pss_verify(n, e, digest, b[812:1196][::-1]):
            verified.append(kd)
        else:
            problems.append(f"signature block {i}: RSA-PSS verification FAILED")
    return verified, problems


def check_s3_header(data: bytes, failures: list[str], notes: list[str], app: bool) -> None:
    if len(data) < 24 or data[0] != 0xE9:
        failures.append("not an ESP image (first byte is not 0xE9)")
        return
    chip = struct.unpack_from("<H", data, 12)[0]
    if chip != CHIP_ID_ESP32S3:
        failures.append(f"image header chip id is {chip}, not 9 (ESP32-S3)")
    if app and (data[2] != FLASH_MODE_DIO or data[3] != FLASH_BYTE_8MB_80M):
        failures.append(f"image header flash byte(s) {data[2]:#04x}/{data[3]:#04x} are not "
                        f"dio / 8MB@80m (0x02/0x3f) -- built with the wrong flash settings")
    if not failures:
        notes.append("ESP32-S3 image header ok" + (" (dio, 8MB, 80m)" if app else ""))


def check_marker(data: bytes, expect_version: str | None, failures: list[str], notes: list[str]) -> None:
    count = data.count(MARKER_MAGIC)
    found = MARKER_RE.findall(data)
    if count == 0:
        failures.append("no AREGFWV1 version marker -- not built from this tree's fw_version_marker.cpp")
        return
    if len(found) != count or len(set(found)) != 1:
        failures.append(f"version marker is malformed or inconsistent ({count} magic, "
                        f"{len(set(found))} distinct well-formed) -- the toy would refuse it")
        return
    ver, board, profile = (x.decode() for x in found[0])
    if profile != "release":
        failures.append(f"marker profile is '{profile}', not 'release' -- a DEV image must never be signed or staged for locked toys")
    if not board.endswith("-sb"):
        failures.append(f"marker board '{board}' does not end in -sb (the secured fleet's board model)")
    if expect_version and ver != expect_version:
        failures.append(f"marker version {ver} != expected {expect_version}")
    if not failures:
        notes.append(f"marker AREGFWV1:{ver}:{board}:{profile} (x{count})")


def check_bootloader(path: Path, data: bytes, trusted: list[str],
                     released_md: Path = RELEASED_MD, min_status: str = "APPROVED") -> int:
    failures: list[str] = []
    notes: list[str] = []
    print(f"bootloader {path}")
    print(f"size       {len(data):,} B (limit {BOOTLOADER_MAX:#x}: the partition table sits at 0x8000)")
    if len(data) > BOOTLOADER_MAX:
        failures.append(f"{len(data):#x} bytes -- would overwrite the partition table at 0x8000")
    check_s3_header(data, failures, notes, app=False)
    verified, problems = sbv2_blocks(data, set(trusted))
    failures.extend(problems)
    if len(verified) != 2 or set(verified) != set(trusted[:2]) or len(trusted) < 2:
        failures.append(
            f"{len(verified)} verifying signature block(s) covering {len(set(verified))} trusted key(s); "
            f"the release bootloader needs EXACTLY two -- primary AND backup. Signed by one key only, "
            f"revoking that key later bricks every toy (QEMU case L).")
    else:
        notes.append("signed by both trusted keys (primary + backup)")
    if len(data) > SB_SECTOR:
        body_sha = hashlib.sha256(data[:-SB_SECTOR]).hexdigest()
        bad = approved_bootloader_problems(body_sha, min_status, released_md)
        if bad:
            failures.extend(bad)
        else:
            notes.append(f"unsigned body {body_sha[:16]}... is a {released_md.name} build "
                         f"({release_row(body_sha, released_md)['status']}, needed >= {min_status})")
    for n in notes:
        print(f"  ok    {n}")
    for f in failures:
        print(f"  FAIL  {f}")
    print()
    if failures:
        print(f"FAIL - {len(failures)} reason(s). Do NOT flash this bootloader.")
        return 1
    print("PASS - release bootloader is dual-signed and fits.")
    return 0


def main(argv=None) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("image", type=Path)
    ap.add_argument("--expect-version",
                    help="version string that MUST be present (e.g. 1.2.1)")
    ap.add_argument("--forbid-version",
                    help="version string that must be ABSENT (e.g. the one "
                         "being replaced) — catches the config.h override trap "
                         "where a -D flag is silently ignored and the old "
                         "version ships under a new name")
    ap.add_argument("--profile", choices=["release"],
                    help="require a chip-security RELEASE image (header, marker, no bench strings)")
    ap.add_argument("--require-sbv2", action="store_true",
                    help="require a Secure Boot V2 signature by a key in --trusted-digests")
    ap.add_argument("--bootloader", action="store_true",
                    help="check a signed release BOOTLOADER instead of an app")
    ap.add_argument("--trusted-digests", type=Path,
                    help="file of SHA-256 public-key digests (hex, one per line)")
    ap.add_argument("--released-md", type=Path, default=RELEASED_MD,
                    help="--bootloader: the RELEASED.md whose recorded builds may be frozen into a toy")
    ap.add_argument("--min-status", choices=BOOTLOADER_STATUSES, default="APPROVED",
                    help="--bootloader: the RELEASED.md Status the build needs (default APPROVED)")
    args = ap.parse_args(argv)

    if not args.image.is_file():
        print(f"FAIL - no such file: {args.image}")
        return 2

    data = args.image.read_bytes()
    failures: list[str] = []
    notes: list[str] = []

    trusted: list[str] = []
    if args.require_sbv2 or args.bootloader:
        if args.trusted_digests is None or not args.trusted_digests.is_file():
            print("FAIL - --require-sbv2/--bootloader need --trusted-digests FILE "
                  "(e.g. esp32/security/sb_trusted_digests.txt)")
            return 2
        try:
            trusted = read_trusted_digests(args.trusted_digests)
        except ValueError as e:
            print(f"FAIL - {args.trusted_digests}: {e}")
            return 2
        if not trusted:
            print(f"FAIL - {args.trusted_digests} holds no digest")
            return 2

    if args.bootloader:
        return check_bootloader(args.image, data, trusted, args.released_md, args.min_status)

    strings = extract_strings(data)
    size = len(data)

    print(f"image   {args.image}")
    print(f"size    {size:,} B ({size / OTA_SLOT_BYTES * 100:.1f}% of the "
          f"{OTA_SLOT_BYTES:,} B OTA slot)")

    if size > OTA_SLOT_BYTES:
        failures.append(
            f"larger than the OTA slot ({size:,} > {OTA_SLOT_BYTES:,}). This is "
            f"probably AregVoiceMvp.ino.merged.bin — the app-only image is "
            f"AregVoiceMvp.ino.bin.")

    keys = [s for s in strings if DEVICE_KEY_RE.match(s)]
    if keys:
        failures.append(
            f"{len(keys)} device API key(s) compiled in (dtk_...). An OTA image "
            f"reaches every toy; one toy's credential must never ride inside "
            f"it. Set AREG_DEVICE_API_KEY back to the config.h.example "
            f"placeholder and rebuild. Treat the key as COMPROMISED and revoke "
            f"it — the value is not printed here, look in your own config.h.")

    # Well-known LIBRARY constants that are GUID-shaped by nature. These are
    # identical in every firmware in the world that uses the library, carry no
    # secret, and would otherwise fail every BLE-enabled release forever.
    # Keep this list EXACT-VALUE only -- never a pattern, never a flag an
    # operator could wave a real device id through. First entry added for
    # release 1.3.4 (2026-09-02), the first gated release with BLE
    # provisioning compiled in.
    KNOWN_LIBRARY_GUIDS = {
        # Espressif WiFiProv BLE provisioning service UUID (esp_prov).
        "258eafa5-e914-47da-95ca-c5ab0dc85b11",
    }
    guids = [s for s in strings
             if GUID_RE.match(s) and s.lower() not in KNOWN_LIBRARY_GUIDS]
    if guids:
        failures.append(
            f"{len(guids)} GUID-shaped string(s) compiled in. If any is the "
            f"toy's device id, the same rule applies as for the key. BLE "
            f"service UUIDs are also GUID-shaped — check which this is before "
            f"overriding, and if it is legitimate say so in the release notes.")

    # Wi-Fi credentials. Same rule as the device key and it was missed for the
    # same reason: config.h compiles AREG_WIFI_SSID / AREG_WIFI_PASSWORD in as
    # a fallback, so every image built on a bench that has a working toy
    # carries a HOME network's name and password in plaintext -- and those
    # images are committed to a git repo and served to every toy that polls.
    # Found 2026-08-14: the staged image AND the shipped field image both had
    # one. A factory-fresh toy installing that image would also try to join
    # someone else's house.
    #
    # The gate cannot know the password, so it checks the shape instead: an
    # image built from config.h.example has EMPTY Wi-Fi strings, and the SSID
    # of a real home router is recognisable. This catches the common vendor
    # prefixes; it is a net, not a proof, which is why the release still ends
    # with a human reading the diff.
    wifi_markers = [s for s in strings
                    if re.match(r"^(OVIO|TP-Link|Ucom|Rostelecom|Beeline|MTS|"
                                r"KTV|Team|VivaCell|Telecom)[-_ ]?[A-Za-z0-9_-]{2,}$", s)]
    if wifi_markers:
        failures.append(
            f"{len(wifi_markers)} probable Wi-Fi SSID(s) compiled in. An OTA "
            f"image reaches every toy, so a home network's name and password "
            f"must never ride inside it — a factory-fresh toy would try to "
            f"join that house. Set AREG_WIFI_SSID / AREG_WIFI_PASSWORD back to "
            f"the config.h.example placeholders and provision Wi-Fi onto the "
            f"toy instead. Values are not printed here.")

    pops = [s for s in strings
            if POP_RE.match(s) and s not in KNOWN_SAFE_POP_STRINGS]
    if pops:
        failures.append(
            f"{len(pops)} BLE provisioning PoP-shaped string(s) compiled in "
            f"(8 chars, the DeviceService.cs PoP alphabet). An OTA image "
            f"reaches every toy; one toy's per-device pairing code must never "
            f"ride inside it. If you set AREG_BLE_POP for a single-unit bench "
            f"burn, unset it and rebuild -- the factory station "
            f"(tools/factory/provision_toy.py) writes the PoP straight to NVS "
            f"and never needs this macro. The value is not printed here.")

    # Raw byte search, not the extracted-strings list: the marker sits inside
    # a longer human-readable log sentence (ota_apply.cpp), and that sentence
    # contains an em-dash -- a non-ASCII byte that STRING_RE's printable-ASCII
    # match splits the sentence around, so the marker never appears as its
    # own complete entry in `strings`. Confirmed against a real compiled
    # image (2026-09-12): `strings` cut it as "...OTA_SIG_CHECK_DISABLED "
    # (trailing space, no more) — an exact `in strings` check missed it
    # silently. A raw substring search over `data` cannot be fooled by where
    # the surrounding sentence happens to break.
    if OTA_SIG_CHECK_DISABLED_MARKER.encode("ascii") in data:
        failures.append(
            f"manifest HMAC signature verification is compiled OFF "
            f"(AREG_MANIFEST_HMAC_KEY is empty — the '{OTA_SIG_CHECK_DISABLED_MARKER}' "
            f"marker is in the image). An OTA image reaches every toy with no "
            f"human checking each one; shipping this means any manifest, "
            f"forged or not, would be applied unverified. Stage-A bench-only — "
            f"set AREG_MANIFEST_HMAC_KEY to the real signing key and rebuild "
            f"before release.")

    present_placeholders = [p for p in PLACEHOLDERS if p in strings]
    if present_placeholders:
        notes.append(f"placeholders present: {', '.join(present_placeholders)}")

    if args.expect_version:
        if args.expect_version in strings:
            notes.append(f"version {args.expect_version} present")
        else:
            failures.append(
                f"expected version {args.expect_version} is NOT in the image. "
                f"config.h uses a plain #define and is included first, so a -D "
                f"build flag is silently overridden — edit config.h.")

    if args.forbid_version:
        if args.forbid_version in strings:
            failures.append(
                f"the OLD version {args.forbid_version} is still in the image. "
                f"The build did not pick up the new version; staging this would "
                f"serve the old firmware under the new manifest.")
        else:
            notes.append(f"old version {args.forbid_version} absent")

    if args.profile == "release":
        check_s3_header(data, failures, notes, app=True)
        check_marker(data, args.expect_version, failures, notes)
        hits = [why for needle, why in RELEASE_FORBIDDEN if needle in data]
        if hits:
            failures.append("release image carries bench/fallback code: " + "; ".join(hits)
                            + " -- build with the RELEASE profile (tools/firmware/build_idf.sh release)")
        else:
            notes.append("no bench / fallback / shared-PoP markers")
        anchors = len(PEM_CERT_RE.findall(data))
        if anchors < MIN_TLS_ANCHORS:
            failures.append(f"only {anchors} TLS trust anchor(s) in the image, need >= {MIN_TLS_ANCHORS} "
                            "(esp32/AregVoiceMvp/tls_trust_anchors.cpp) -- a locked toy can only be updated "
                            "over TLS; one CA move would strand it forever")
        else:
            notes.append(f"{anchors} TLS trust anchors (>= {MIN_TLS_ANCHORS})")

    if args.require_sbv2:
        if len(data) % SB_SECTOR or (len(data) - SB_SECTOR) % MMU_PAGE or len(data) < MMU_PAGE + SB_SECTOR:
            failures.append(
                f"{len(data):,} B is not a signed-app shape (4 KB multiple whose body is a whole "
                f"number of 64 KB pages) -- build with --secure-pad-v2 and sign with espsecure")
        verified, problems = sbv2_blocks(data, set(trusted))
        failures.extend(problems)
        if not verified:
            failures.append("NOT signed by a trusted Secure Boot key -- a locked toy's bootloader "
                            "would refuse to boot it (and the toy's OTA pre-check refuses to install it)")
        else:
            notes.append(f"Secure Boot V2 signature verified by {len(verified)} trusted key(s) "
                         f"({', '.join(d[:12] for d in verified)})")

    for n in notes:
        print(f"  ok    {n}")
    for f in failures:
        print(f"  FAIL  {f}")

    print()
    if failures:
        print(f"FAIL - {len(failures)} reason(s). DO NOT release this image.")
        return 1
    print("PASS - no credentials found; safe to stage.")
    print("This checks the image only. The human OTA gates still apply: build "
          "on the release machine, roll out to one toy first, watch the "
          "check-in.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

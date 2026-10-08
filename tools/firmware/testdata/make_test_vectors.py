#!/usr/bin/env python3
"""Regenerate the committed Secure Boot V2 TEST vectors. NOT run by CI.

Everything this writes is signed with TEST keys that exist only on the
machine that ran it (AREG_TEST_KEYS_DIR, never the repo). Only signed
images, the TEST public-key digests and the expected hashes are committed --
never a private key. A TEST digest is trusted by nothing but the tests:
production trust lives in esp32/security/sb_trusted_digests.txt (the owner's
keys), which no TEST-signed image can satisfy.

Outputs (deterministic for a given key pair, because RSA-PSS salts are
random the SIGNATURE bytes differ per run -- rerun the tests after
regenerating):
  tools/firmware/testdata/TEST_app_signed.bin         0x11000 B synthetic app,
      ESP32-S3 header (dio/80m/8MB, chip id 9), marker
      AREGFWV1:9.9.9:areg-s3-n8-sb:release, three PEM-shaped dummy trust
      anchors (+ the bare mbedTLS header literal), signed by TEST primary
  tools/firmware/testdata/TEST_app_dev_signed.bin     same shape, profile "dev"
  tools/firmware/testdata/TEST_bootloader_signed.bin  synthetic bootloader,
      signed by TEST primary THEN TEST backup (--append-signatures)
  tools/firmware/testdata/TEST_sb_trusted_digests.txt the two TEST digests
  tools/firmware/testdata/TEST_sb_{primary,backup}.pub.pem  the TEST PUBLIC keys
      (the factory station's --virt rehearsal burns their digests)
  esp32/AregVoiceMvp/host_tests/vectors/sbv2_sector.bin   last 4 KB of
      TEST_app_signed.bin
  esp32/AregVoiceMvp/host_tests/vectors/sbv2_sector_expected.h

USAGE
  AREG_TEST_KEYS_DIR=/path/to/keys-test ESPSECURE=espsecure \\
      python3 tools/firmware/testdata/make_test_vectors.py
  (keys: TEST_sb_primary.pem, TEST_sb_backup.pem, and their .pub.pem)
"""
import hashlib
import os
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
OUT = REPO / "tools" / "firmware" / "testdata"
VEC = REPO / "esp32" / "AregVoiceMvp" / "host_tests" / "vectors"


def synthetic_app(version: str, profile: str, body_len: int = 0x10000) -> bytes:
    hdr = bytearray(24)
    hdr[0] = 0xE9                      # ESP_IMAGE_HEADER_MAGIC
    hdr[1] = 1                         # one segment
    hdr[2] = 2                         # SPI mode DIO (what Arduino/IDF write for a QIO board)
    hdr[3] = (3 << 4) | 0xF            # 8 MB | 80 MHz
    struct.pack_into("<I", hdr, 4, 0x40370000)
    struct.pack_into("<H", hdr, 12, 9)  # chip id: ESP32-S3
    marker = f"AREGFWV1:{version}:areg-s3-n8-sb:{profile}".encode() + b"\0"
    # Three PEM-shaped trust anchors with a dummy base64 body (not real
    # certificates): the minimum the release gate accepts (MIN_TLS_ANCHORS;
    # tests break one to prove the refusal). Plus the bare header literal
    # mbedTLS carries in every real image, which the gate must NOT count.
    body = b"VEVTVCBBTkNIT1IgLS0gTk9UIEEgQ0VSVElGSUNBVEUgLS0gVEVTVCBPTkxZIC0t\n" * 2
    anchors = b"".join(b"-----BEGIN CERTIFICATE-----\n" + body + b"-----END CERTIFICATE-----\n"
                       for _ in range(3)) + b"\0-----BEGIN CERTIFICATE-----\0"
    payload = bytes(hdr) + b"\0" * 8 + marker + b"\0" + version.encode() + b"\0" + anchors
    # Non-printable filler: no accidental 4+ byte printable runs for the
    # gate's string scan to trip over.
    filler = bytes(i % 31 for i in range(body_len - len(payload)))
    return payload + filler


def synthetic_bootloader(body_len: int = 0x2000) -> bytes:
    hdr = bytearray(24)
    hdr[0], hdr[1], hdr[2], hdr[3] = 0xE9, 1, 2, (3 << 4) | 0xF
    struct.pack_into("<H", hdr, 12, 9)
    return bytes(hdr) + bytes(i % 29 for i in range(body_len - 24))


def run(*args: str) -> None:
    subprocess.run(list(args), check=True, capture_output=True)


def main() -> int:
    keys = Path(os.environ.get("AREG_TEST_KEYS_DIR", ""))
    esps = os.environ.get("ESPSECURE", "espsecure")
    prim, back = keys / "TEST_sb_primary.pem", keys / "TEST_sb_backup.pem"
    if not prim.is_file() or not back.is_file():
        print("set AREG_TEST_KEYS_DIR to the directory holding the TEST_*.pem keys", file=sys.stderr)
        return 2
    with tempfile.TemporaryDirectory() as tmp:
        t = Path(tmp)
        digests = []
        for name in ("TEST_sb_primary.pub.pem", "TEST_sb_backup.pub.pem"):
            run(esps, "digest-sbv2-public-key", "--keyfile", str(keys / name), "--output", str(t / "d.bin"))
            digests.append((t / "d.bin").read_bytes().hex())
        for name in ("TEST_sb_primary.pub.pem", "TEST_sb_backup.pub.pem"):
            data = (keys / name).read_bytes()
            assert b"PRIVATE" not in data
            (OUT / name).write_bytes(data)
        (OUT / "TEST_sb_trusted_digests.txt").write_text(
            "# TEST ONLY -- digests of the TEST signing keys used by the gate's unit tests.\n"
            "# Never trusted by a real toy or by a real release.\n" + "\n".join(digests) + "\n")
        for profile, name in (("release", "TEST_app_signed.bin"), ("dev", "TEST_app_dev_signed.bin")):
            (t / "app.bin").write_bytes(synthetic_app("9.9.9", profile))
            run(esps, "sign-data", "--version", "2", "--keyfile", str(prim), "--output", str(OUT / name), str(t / "app.bin"))
        (t / "bl.bin").write_bytes(synthetic_bootloader())
        run(esps, "sign-data", "--version", "2", "--keyfile", str(prim), "--output", str(t / "bl1.bin"), str(t / "bl.bin"))
        run(esps, "sign-data", "--version", "2", "--keyfile", str(back), "--append-signatures",
            "--output", str(OUT / "TEST_bootloader_signed.bin"), str(t / "bl1.bin"))
    signed = (OUT / "TEST_app_signed.bin").read_bytes()
    body, sector = signed[:-4096], signed[-4096:]
    VEC.mkdir(parents=True, exist_ok=True)
    (VEC / "sbv2_sector.bin").write_bytes(sector)
    img = hashlib.sha256(body).digest()
    key = hashlib.sha256(sector[36:812]).digest()
    assert key.hex() == digests[0], "block 0 must carry the TEST primary key"
    arr = lambda b: ", ".join(f"0x{x:02x}" for x in b)
    (VEC / "sbv2_sector_expected.h").write_text(
        "#pragma once\n"
        "// GENERATED by tools/firmware/testdata/make_test_vectors.py -- TEST vector.\n"
        "// sbv2_sector.bin is the signature sector of tools/firmware/testdata/\n"
        "// TEST_app_signed.bin (0x11000 B, signed by a TEST key that never leaves\n"
        "// the machine that generated it).\n"
        "#include <stdint.h>\n"
        f"static const uint8_t kTestImageDigest[32] = {{{arr(img)}}};\n"
        f"static const uint8_t kTestKeyDigest[32] = {{{arr(key)}}};\n"
        f"static const uint32_t kTestSignedLen = 0x{len(signed):x};\n")
    print(f"wrote vectors; TEST primary digest {digests[0][:16]}..., backup {digests[1][:16]}...")
    return 0


if __name__ == "__main__":
    sys.exit(main())

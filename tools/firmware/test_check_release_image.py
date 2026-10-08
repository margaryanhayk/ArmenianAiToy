#!/usr/bin/env python3
"""Proves check_release_image.py's PoP gate does what it claims.

Dependency-free (stdlib unittest only), same discipline as the checker
itself: no toolchain, so this never gets skipped. Builds synthetic byte
blobs standing in for a firmware image rather than compiling real firmware —
the checker works on extracted strings, so a blob containing the right bytes
IS a valid input to it.

USAGE
    python3 tools/firmware/test_check_release_image.py
"""
import unittest
from pathlib import Path

from check_release_image import main as check_main
import check_release_image as gate
import sys
import io
import contextlib


REAL_POP = "AREGX234"  # 8 chars, every one in the DeviceService.cs alphabet
PLACEHOLDER_POP = "YOURBLEPOP"  # config.h.example's own placeholder — 10 chars, has O/U/L


def run_check(image_bytes: bytes, tmp_path: Path) -> int:
    image_path = tmp_path / "image.bin"
    image_path.write_bytes(image_bytes)
    argv_backup = sys.argv
    sys.argv = ["check_release_image.py", str(image_path)]
    try:
        with contextlib.redirect_stdout(io.StringIO()) as out:
            code = check_main()
        return code, out.getvalue()
    finally:
        sys.argv = argv_backup


class CheckReleaseImagePopGateTests(unittest.TestCase):
    def setUp(self):
        import tempfile
        self._tmpdir = tempfile.TemporaryDirectory()
        self.tmp_path = Path(self._tmpdir.name)

    def tearDown(self):
        self._tmpdir.cleanup()

    def test_bad_image_with_real_looking_pop_fails(self):
        # A real PoP embedded as a plain C string literal, the way
        # device_creds_save(AREG_DEVICE_ID, AREG_DEVICE_API_KEY, AREG_BLE_POP)
        # would compile it into .rodata if AREG_BLE_POP were left defined.
        blob = b"junk header bytes\x00" + REAL_POP.encode("ascii") + b"\x00more junk"
        code, out = run_check(blob, self.tmp_path)
        self.assertNotEqual(code, 0, "a real-looking PoP must fail the release gate")
        self.assertIn("PoP-shaped", out)
        # The value itself must never be printed.
        self.assertNotIn(REAL_POP, out)

    def test_good_image_without_a_real_pop_passes(self):
        # A correctly-built image: only the bench placeholder, the
        # config.h.example placeholder, and the four KNOWN coincidental
        # library-string collisions are present. The four are not synthetic
        # — a real `arduino-cli compile` of AregVoiceMvp.ino at
        # esp32:esp32@3.3.8, with NO PoP-related build flag set at all,
        # contains exactly these as printable 8-char runs (confirmed
        # 2026-09-11; see the KNOWN_SAFE_POP_STRINGS comment in
        # check_release_image.py for what each one actually is). The first
        # version of this test used a blob without them and PASSED while the
        # real compiled binary FAILED — this blob is what closes that gap.
        blob = (
            b"junk header bytes\x00"
            + b"areg-pair\x00"
            + PLACEHOLDER_POP.encode("ascii")
            + b"\x00YOUR_DEVICE_GUID\x00YOUR_DEVICE_API_KEY\x00"
            + b"ESPHTTPD\x00EXCVADDR\x00BBB6BHHB\x00B8BH8B4B\x00more junk"
        )
        code, out = run_check(blob, self.tmp_path)
        self.assertEqual(code, 0, f"a clean image must pass; got:\n{out}")
        self.assertIn("PASS", out)

    def test_known_safe_pop_string_does_not_fail(self):
        # The shared bench fallback itself, alone, must never trip the gate —
        # it is 9 chars, lowercase, and hyphenated, so POP_RE cannot match it,
        # but pin the behavior directly rather than only via the regex shape.
        blob = b"junk\x00areg-pair\x00junk"
        code, out = run_check(blob, self.tmp_path)
        self.assertEqual(code, 0)

    def test_allowlisting_one_string_does_not_blind_the_gate_to_others(self):
        # The allowlist must stay a set of EXACT, individually-approved
        # strings, never a loophole a real leaked PoP could hide behind.
        # A known-safe string alongside an unrelated real-looking one must
        # still fail on the real one.
        blob = b"junk\x00ESPHTTPD\x00" + REAL_POP.encode("ascii") + b"\x00junk"
        code, out = run_check(blob, self.tmp_path)
        self.assertNotEqual(code, 0)
        self.assertIn("PoP-shaped", out)


class CheckReleaseImageOtaSigGateTests(unittest.TestCase):
    """ota_apply.cpp skips manifest HMAC verification when
    AREG_MANIFEST_HMAC_KEY is empty and logs the OTA_SIG_CHECK_DISABLED
    marker instead of failing loudly. The release gate must refuse any
    image carrying that marker and pass one that does not."""

    def setUp(self):
        import tempfile
        self._tmpdir = tempfile.TemporaryDirectory()
        self.tmp_path = Path(self._tmpdir.name)

    def tearDown(self):
        self._tmpdir.cleanup()

    def test_image_with_sig_check_disabled_marker_fails(self):
        blob = b"junk header bytes\x00OTA_SIG_CHECK_DISABLED\x00more junk"
        code, out = run_check(blob, self.tmp_path)
        self.assertNotEqual(code, 0,
                            "an image with signature verification compiled "
                            "off must fail the release gate")
        self.assertIn("OTA_SIG_CHECK_DISABLED", out)

    def test_marker_split_by_a_surrounding_em_dash_still_fails(self):
        # Regression: ota_apply.cpp's real log sentence wraps the marker in
        # human-readable text with an em-dash right after it. `strings`
        # extraction (STRING_RE, printable-ASCII-only) breaks the sentence
        # at that non-ASCII byte, so the marker never appears as its own
        # complete entry in the extracted-strings list — an `in strings`
        # check missed this against a real compiled image (2026-09-12,
        # confirmed with `strings` directly). The gate must search raw
        # bytes, not the strings list, so this must still fail.
        em_dash = "—".encode("utf-8")
        sentence = (b"[ota] WARNING: OTA_SIG_CHECK_DISABLED " + em_dash
                    + b" manifest signature check SKIPPED")
        blob = b"junk\x00" + sentence + b"\x00junk"
        code, out = run_check(blob, self.tmp_path)
        self.assertNotEqual(code, 0,
                            "a marker split by a non-ASCII byte must still "
                            "be caught")
        self.assertIn("OTA_SIG_CHECK_DISABLED", out)

    def test_image_without_the_marker_passes(self):
        blob = (
            b"junk header bytes\x00"
            + b"areg-pair\x00"
            + PLACEHOLDER_POP.encode("ascii")
            + b"\x00YOUR_DEVICE_GUID\x00YOUR_DEVICE_API_KEY\x00"
            + b"ESPHTTPD\x00EXCVADDR\x00BBB6BHHB\x00B8BH8B4B\x00more junk"
        )
        code, out = run_check(blob, self.tmp_path)
        self.assertEqual(code, 0, f"a clean image must pass; got:\n{out}")
        self.assertIn("PASS", out)


# ---------------------------------------------------------------------------
# Chip security (2026-10-08): Secure Boot V2 signature, release profile,
# release bootloader. Vectors are COMMITTED, TEST-signed (testdata/,
# regenerated by testdata/make_test_vectors.py with TEST keys that never
# enter the repo); only the TEST public-key digests are trusted here.
# ---------------------------------------------------------------------------
import hashlib  # noqa: E402
import struct  # noqa: E402
import tempfile  # noqa: E402
import zlib  # noqa: E402

TESTDATA = Path(__file__).resolve().parent / "testdata"
SIGNED = TESTDATA / "TEST_app_signed.bin"
SIGNED_DEV = TESTDATA / "TEST_app_dev_signed.bin"
BOOTLOADER = TESTDATA / "TEST_bootloader_signed.bin"
TRUSTED = TESTDATA / "TEST_sb_trusted_digests.txt"


def run_args(image_bytes: bytes, tmp: Path, *extra: str):
    image_path = tmp / "image.bin"
    image_path.write_bytes(image_bytes)
    with contextlib.redirect_stdout(io.StringIO()) as out:
        code = check_main([str(image_path), *extra])
    return code, out.getvalue()


def fix_crc(img: bytearray, block: int = 0) -> None:
    off = len(img) - 4096 + block * 1216
    struct.pack_into("<I", img, off + 1196, zlib.crc32(bytes(img[off:off + 1196])) & 0xFFFFFFFF)


class ChipSecurityGateTests(unittest.TestCase):
    REL = ("--expect-version", "9.9.9", "--profile", "release")

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.signed = SIGNED.read_bytes()
        self.sig = ("--require-sbv2", "--trusted-digests", str(TRUSTED))
        self.bl_body_sha = hashlib.sha256(BOOTLOADER.read_bytes()[:-4096]).hexdigest()
        self.released = self.released_md("APPROVED")

    def released_md(self, status: str, sha: str | None = None) -> Path:
        p = self.tmp / f"RELEASED-{status}.md"
        p.write_text(
            "# Release bootloader -- recorded builds (TEST fixture)\n\n"
            "| Date | Built by | IDF commit | Toolchain | IDF esptool / repo | Size | sha256 (bootloader-unsigned.bin) "
            "| sha256 (partition-table.bin) | Status |\n|---|---|---|---|---|---|---|---|---|\n"
            f"| 2026-10-08 | test | x | x | x | 0x2000 | `{sha or self.bl_body_sha}` | `{'cd' * 32}` | {status} -- fixture |\n")
        return p

    def bl_args(self, released: Path | None = None, *extra: str):
        return ("--bootloader", "--trusted-digests", str(TRUSTED), "--released-md", str(released or self.released),
                *extra)

    def tearDown(self):
        self._tmp.cleanup()

    def test_vectors_are_test_only_and_shaped(self):
        self.assertIn("TEST ONLY", TRUSTED.read_text())
        self.assertGreaterEqual(len(self.signed), 0x11000)
        self.assertEqual((len(self.signed) - 4096) % 0x10000, 0)
        for f in TESTDATA.iterdir():  # never a private key in the repo
            self.assertNotIn(b"PRIVATE KEY", f.read_bytes(), f.name)

    def test_signed_release_vector_passes(self):
        code, out = run_args(self.signed, self.tmp, *self.REL, *self.sig)
        self.assertEqual(code, 0, out)
        self.assertIn("Secure Boot V2 signature verified", out)
        self.assertIn("AREGFWV1:9.9.9:areg-s3-n8-sb:release", out)

    def test_release_needs_three_tls_trust_anchors(self):
        code, out = run_args(self.signed, self.tmp, *self.REL)
        self.assertEqual(code, 0, out)
        self.assertIn("3 TLS trust anchors", out)
        self.assertEqual(self.signed.count(b"-----BEGIN CERTIFICATE-----"), 4,
                         "3 anchors + mbedTLS's bare header literal, which must not count")
        one_less = self.signed.replace(b"-----BEGIN CERTIFICATE-----\n", b"-----BEGIN XXXXXXXXXXX-----\n", 1)
        code, out = run_args(one_less, self.tmp, *self.REL)
        self.assertNotEqual(code, 0, "an image with 2 trust anchors must fail the release gate")
        self.assertIn("only 2 TLS trust anchor(s)", out)

    def test_flipped_body_byte_fails(self):
        img = bytearray(self.signed)
        img[0x8000] ^= 0x01
        code, out = run_args(bytes(img), self.tmp, *self.REL, *self.sig)
        self.assertNotEqual(code, 0)
        self.assertIn("image digest mismatch", out)

    def test_flipped_signature_byte_with_valid_crc_fails_rsa(self):
        img = bytearray(self.signed)
        img[len(img) - 4096 + 812 + 100] ^= 0x01
        fix_crc(img)
        code, out = run_args(bytes(img), self.tmp, *self.REL, *self.sig)
        self.assertNotEqual(code, 0)
        self.assertIn("RSA-PSS verification FAILED", out)

    def test_flipped_crc_fails(self):
        img = bytearray(self.signed)
        img[len(img) - 4096 + 1196] ^= 0xFF
        code, out = run_args(bytes(img), self.tmp, *self.REL, *self.sig)
        self.assertNotEqual(code, 0)
        self.assertIn("bad CRC", out)

    def test_untrusted_key_fails(self):
        only_backup = self.tmp / "backup_only.txt"
        only_backup.write_text(TRUSTED.read_text().splitlines()[-1] + "\n")
        code, out = run_args(self.signed, self.tmp, *self.REL, "--require-sbv2", "--trusted-digests", str(only_backup))
        self.assertNotEqual(code, 0)
        self.assertIn("UNTRUSTED key", out)

    def test_unsigned_image_fails(self):
        body = self.signed[:-4096]
        code, out = run_args(body, self.tmp, *self.REL, *self.sig)
        self.assertNotEqual(code, 0)
        self.assertIn("not a signed-app shape", out)
        code, out = run_args(body + b"\xff" * 4096, self.tmp, *self.REL, *self.sig)
        self.assertNotEqual(code, 0)
        self.assertIn("NOT signed by a trusted Secure Boot key", out)

    def test_unsigned_release_image_passes_without_require_sbv2(self):
        code, out = run_args(self.signed[:-4096], self.tmp, *self.REL)
        self.assertEqual(code, 0, out)

    def test_require_sbv2_without_digests_is_a_usage_error(self):
        code, out = run_args(self.signed, self.tmp, "--require-sbv2")
        self.assertEqual(code, 2)

    def test_dev_profile_marker_is_refused_as_release(self):
        code, out = run_args(SIGNED_DEV.read_bytes(), self.tmp, *self.REL, *self.sig)
        self.assertNotEqual(code, 0)
        self.assertIn("marker profile is 'dev'", out)

    def test_wrong_expected_version_fails(self):
        code, out = run_args(self.signed, self.tmp, "--expect-version", "9.9.8", "--profile", "release")
        self.assertNotEqual(code, 0)

    def test_bench_and_fallback_strings_are_refused(self):
        for needle in (b"areg-pair", b"[net] *** TLS INSECURE BUILD", b"[wifi] using compile-time fallback creds",
                       b"[sd-bench] starting", b"[cs-test] x", b"Can't open HTTP request",
                       b"ERROR! AudioFileSourceHTTPStream::read passed NULL data"):
            img = self.signed[:-4096] + b"\x00" + needle + b"\x00"
            code, out = run_args(img, self.tmp, *self.REL)
            self.assertNotEqual(code, 0, needle)
            self.assertIn("bench/fallback", out)

    def test_wrong_chip_or_flash_header_fails(self):
        img = bytearray(self.signed[:-4096])
        struct.pack_into("<H", img, 12, 0)  # ESP32 (not S3)
        code, out = run_args(bytes(img), self.tmp, *self.REL)
        self.assertNotEqual(code, 0)
        self.assertIn("chip id", out)
        img = bytearray(self.signed[:-4096])
        img[2] = 0  # QIO in the header
        code, out = run_args(bytes(img), self.tmp, *self.REL)
        self.assertNotEqual(code, 0)
        self.assertIn("flash byte", out)

    def test_duplicate_inconsistent_markers_fail(self):
        img = self.signed[:-4096] + b"AREGFWV1:9.9.8:areg-s3-n8-sb:release\x00"
        code, out = run_args(img, self.tmp, *self.REL)
        self.assertNotEqual(code, 0)
        self.assertIn("inconsistent", out)

    def test_old_style_call_still_works_on_the_signed_vector(self):
        code, out = run_args(self.signed, self.tmp, "--expect-version", "9.9.9")
        self.assertEqual(code, 0, out)

    # ---- release bootloader ----
    def test_dual_signed_bootloader_passes(self):
        code, out = run_args(BOOTLOADER.read_bytes(), self.tmp, *self.bl_args())
        self.assertEqual(code, 0, out)
        self.assertIn("both trusted keys", out)
        self.assertIn("APPROVED, needed >= APPROVED", out)

    def test_bootloader_signed_by_one_key_fails(self):
        bl = bytearray(BOOTLOADER.read_bytes())
        off = len(bl) - 4096 + 1216
        bl[off:off + 1216] = b"\xff" * 1216  # drop the backup block
        code, out = run_args(bytes(bl), self.tmp, *self.bl_args())
        self.assertNotEqual(code, 0)
        self.assertIn("EXACTLY two", out)

    def test_app_image_is_not_a_bootloader(self):
        code, out = run_args(self.signed, self.tmp, *self.bl_args())
        self.assertNotEqual(code, 0)
        self.assertIn("partition table", out)

    def test_bootloader_needs_both_digests_trusted(self):
        one = self.tmp / "one.txt"
        one.write_text(TRUSTED.read_text().splitlines()[-2] + "\n")
        code, out = run_args(BOOTLOADER.read_bytes(), self.tmp, "--bootloader", "--trusted-digests", str(one),
                             "--released-md", str(self.released))
        self.assertNotEqual(code, 0)

    # ---- review round 3: WHICH bootloader is frozen, not only who signed it ----
    def test_dual_signed_but_unrecorded_bootloader_fails(self):
        # e.g. build_idf.sh's app-config bootloader (no CONFIG_SECURE_BOOT):
        # two perfectly good signatures, never verifies the app.
        other = self.released_md("APPROVED", sha="ab" * 32)
        code, out = run_args(BOOTLOADER.read_bytes(), self.tmp, *self.bl_args(other))
        self.assertNotEqual(code, 0)
        self.assertIn("not a build recorded", out)

    def test_bootloader_status_must_reach_the_minimum(self):
        for status, minimum, ok in (("CANDIDATE", "APPROVED", False), ("PILOT", "APPROVED", False),
                                    ("CANDIDATE", "CANDIDATE", True), ("PILOT", "PILOT", True),
                                    ("APPROVED", "PILOT", True), ("CANDIDATE", "PILOT", False),
                                    ("SUPERSEDED", "CANDIDATE", False)):
            code, out = run_args(BOOTLOADER.read_bytes(), self.tmp,
                                 *self.bl_args(self.released_md(status), "--min-status", minimum))
            self.assertEqual(code == 0, ok, f"{status} vs >= {minimum}:\n{out}")

    def test_default_minimum_is_approved(self):
        code, out = run_args(BOOTLOADER.read_bytes(), self.tmp, *self.bl_args(self.released_md("PILOT")))
        self.assertNotEqual(code, 0)
        self.assertIn("needs at least APPROVED", out)

    def test_repository_released_md_parses(self):
        rows = gate.released_rows()
        self.assertTrue(rows, "esp32/bootloader-release/RELEASED.md lost its build table")
        for r in rows:
            self.assertRegex(r["bootloader_unsigned_sha256"], r"^[0-9a-f]{64}$")
            self.assertRegex(r["partition_table_sha256"], r"^[0-9a-f]{64}$")
        self.assertIn(rows[-1]["status"], gate.BOOTLOADER_STATUSES)

    def test_stdlib_pss_matches_the_vector_digest(self):
        # Belt and braces: the digest the gate hashes is the one the block carries.
        body, sector = self.signed[:-4096], self.signed[-4096:]
        self.assertEqual(sector[4:36], hashlib.sha256(body).digest())


if __name__ == "__main__":
    unittest.main()

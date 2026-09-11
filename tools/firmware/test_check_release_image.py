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


if __name__ == "__main__":
    unittest.main()

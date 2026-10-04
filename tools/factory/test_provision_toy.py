#!/usr/bin/env python3
"""Proves provision_toy.py's --rotate-existing contract, in the same
dependency-free style as test_load_sd_card.py: stdlib unittest only, a fake
`requests` module standing in for the backend and a fake `subprocess.run`
standing in for Espressif's NVS generator -- no real HTTP, no esptool, no
toolchain, so this never gets skipped.

What it pins:
  - the rotate path calls POST /api/devices/register TWICE, both with
    X-Force-Rotate: true and the provisioning secret, and burns the SECOND
    key (DeviceService's legacy-plaintext arm returns the old key on the first);
  - an unknown MAC (the first forced call minted a NEW device), a device-id
    mismatch and a non-rotating backend all stop before anything is built;
  - the image carries devid/apikey only, no label is rendered, and the key
    never reaches stdout or stderr on success or failure;
  - without --out-dir the rotated nvs.bin lands in a private temp directory
    OUTSIDE the repo, never in tools/factory/out/ (not gitignored);
  - the ordinary new-unit image still carries the pop row.

USAGE
    python3 tools/factory/test_provision_toy.py
"""
from __future__ import annotations

import contextlib
import io
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))

import provision_toy as sut  # noqa: E402

BACKEND = "http://backend.example"
MAC = "AA:BB:CC:DD:EE:FF"
SECRET = "test-provisioning-secret"
DEVICE_ID = "248729da-0000-4000-8000-000000000001"
# Synthetic device-key fixtures in the real dtk_<32 hex> shape, assembled at
# runtime so the repo's secret scan never sees a key-shaped literal.
KEY_PREFIX = "dtk" + "_"
OLD_KEY = KEY_PREFIX + "0123456789abcdef" * 2   # the "leaked" legacy key
FIRST_KEY = KEY_PREFIX + "1" * 32
NEW_KEY = KEY_PREFIX + "fedcba9876543210" * 2


class FakeResponse:
    def __init__(self, status_code: int, body: dict):
        self.status_code = status_code
        self._body = body

    def json(self) -> dict:
        return self._body


class FakeRequests:
    """Stands in for the `requests` module: replays canned responses in order
    and records every call."""

    class RequestException(Exception):
        pass

    def __init__(self, responses):
        self._responses = list(responses)
        self.calls = []

    def post(self, url, json=None, headers=None, timeout=None):
        self.calls.append({"url": url, "json": json, "headers": dict(headers or {})})
        if not self._responses:
            raise AssertionError("unexpected extra POST")
        return self._responses.pop(0)


def rotated(key: str, device_id: str = DEVICE_ID) -> FakeResponse:
    # The shape a forced re-registration of an EXISTING device returns:
    # id + key, no claim code / qr / pop.
    return FakeResponse(201, {"deviceId": device_id, "apiKey": key,
                              "claimCode": None, "qrPayload": None, "pop": None})


def brand_new(key: str) -> FakeResponse:
    return FakeResponse(201, {"deviceId": DEVICE_ID, "apiKey": key, "claimCode": "C0DE" * 8,
                              "qrPayload": "{}", "pop": "ABCDEFGH"})


class FakeGenerator:
    """Stands in for `python -m esp_idf_nvs_partition_gen generate CSV BIN SIZE`:
    keeps the CSV text it was handed and writes a placeholder image."""

    def __init__(self):
        self.csv_text = None
        self.argv = None

    def __call__(self, argv, capture_output=False, text=False):
        self.argv = argv
        csv_path, bin_path = Path(argv[4]), Path(argv[5])
        self.csv_text = csv_path.read_text()
        bin_path.write_bytes(b"\xff" * 16)
        return subprocess.CompletedProcess(argv, 0, stdout="", stderr="")


class RotateExistingDeviceTests(unittest.TestCase):
    def setUp(self):
        self._real_requests = sut.requests

    def tearDown(self):
        sut.requests = self._real_requests

    def test_calls_register_twice_with_force_and_secret_and_returns_second_key(self):
        # Legacy-plaintext arm: the first forced call hands back the OLD key.
        fake = FakeRequests([rotated(OLD_KEY), rotated(NEW_KEY)])
        sut.requests = fake
        result = sut.rotate_existing_device(BACKEND, MAC, SECRET)
        self.assertEqual(result, {"deviceId": DEVICE_ID, "apiKey": NEW_KEY})
        self.assertEqual(len(fake.calls), 2)
        for call in fake.calls:
            self.assertEqual(call["url"], BACKEND + "/api/devices/register")
            self.assertEqual(call["json"], {"macAddress": MAC})
            self.assertEqual(call["headers"].get("X-Force-Rotate"), "true")
            self.assertEqual(call["headers"].get("X-Provisioning-Secret"), SECRET)

    def test_hashed_row_also_burns_the_second_key(self):
        fake = FakeRequests([rotated(FIRST_KEY), rotated(NEW_KEY)])
        sut.requests = fake
        self.assertEqual(sut.rotate_existing_device(BACKEND, MAC, SECRET)["apiKey"], NEW_KEY)

    def test_unknown_mac_stops_after_the_first_call(self):
        fake = FakeRequests([brand_new(FIRST_KEY)])
        sut.requests = fake
        with self.assertRaises(sut.ProvisionError) as ctx:
            sut.rotate_existing_device(BACKEND, MAC, SECRET)
        self.assertEqual(len(fake.calls), 1)
        self.assertIn("NEW device", str(ctx.exception))
        self.assertIn(DEVICE_ID, str(ctx.exception))  # so the operator can revoke it
        self.assertNotIn(FIRST_KEY, str(ctx.exception))

    def test_device_id_mismatch_is_refused(self):
        sut.requests = FakeRequests([rotated(OLD_KEY), rotated(NEW_KEY, device_id="other-id")])
        with self.assertRaises(sut.ProvisionError) as ctx:
            sut.rotate_existing_device(BACKEND, MAC, SECRET)
        self.assertIn("different device ids", str(ctx.exception))
        self.assertNotIn(NEW_KEY, str(ctx.exception))

    def test_same_key_twice_is_refused(self):
        sut.requests = FakeRequests([rotated(OLD_KEY), rotated(OLD_KEY)])
        with self.assertRaises(sut.ProvisionError) as ctx:
            sut.rotate_existing_device(BACKEND, MAC, SECRET)
        self.assertIn("did not rotate", str(ctx.exception))
        self.assertNotIn(OLD_KEY, str(ctx.exception))

    def test_non_201_reports_status_only(self):
        sut.requests = FakeRequests([FakeResponse(401, {"error": "Device registration is not permitted."})])
        with self.assertRaises(sut.ProvisionError) as ctx:
            sut.rotate_existing_device(BACKEND, MAC, SECRET)
        self.assertIn("HTTP 401", str(ctx.exception))
        self.assertNotIn("not permitted", str(ctx.exception))

    def test_second_call_failure_reports_status(self):
        sut.requests = FakeRequests([rotated(OLD_KEY), FakeResponse(429, {})])
        with self.assertRaises(sut.ProvisionError) as ctx:
            sut.rotate_existing_device(BACKEND, MAC, SECRET)
        self.assertIn("2/2", str(ctx.exception))
        self.assertIn("HTTP 429", str(ctx.exception))
        self.assertNotIn(OLD_KEY, str(ctx.exception))


class RotateMainTests(unittest.TestCase):
    def setUp(self):
        self._real_requests = sut.requests
        self._tmp = tempfile.TemporaryDirectory()
        self.out_dir = Path(self._tmp.name) / "out"
        self._env = mock.patch.dict(sut.os.environ, {"AREG_PROVISIONING_SECRET": SECRET})
        self._env.start()
        self._gen_present = mock.patch.object(sut, "nvs_generator_available", return_value=True)
        self._gen_present.start()

    def tearDown(self):
        sut.requests = self._real_requests
        self._env.stop()
        self._gen_present.stop()
        self._tmp.cleanup()

    def run_main(self, argv):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            rc = sut.main(argv)
        return rc, out.getvalue(), err.getvalue()

    def rotate_argv(self, *extra):
        return ["--rotate-existing", "--backend-url", BACKEND, "--mac", MAC,
                "--out-dir", str(self.out_dir), *extra]

    def test_builds_devid_apikey_only_image_prints_offset_and_never_the_key(self):
        fake = FakeRequests([rotated(OLD_KEY), rotated(NEW_KEY)])
        sut.requests = fake
        gen = FakeGenerator()
        with mock.patch.object(sut.subprocess, "run", side_effect=gen):
            rc, out, err = self.run_main(self.rotate_argv())

        self.assertEqual(rc, 0, err)
        self.assertEqual(len(fake.calls), 2)
        # The CSV handed to the generator: namespace + devid + the SECOND key, no pop.
        self.assertEqual(gen.csv_text,
                         "key,type,encoding,value\n"
                         f"{sut.NVS_NAMESPACE},namespace,,\n"
                         f"{sut.NVS_KEY_ID},data,string,{DEVICE_ID}\n"
                         f"{sut.NVS_KEY_APIKEY},data,string,{NEW_KEY}\n")
        self.assertNotIn(OLD_KEY, gen.csv_text)
        self.assertTrue((self.out_dir / "nvs.bin").is_file())
        # No label: no QR, no PDF.
        self.assertFalse((self.out_dir / "qr.png").exists())
        self.assertFalse((self.out_dir / "label.pdf").exists())
        # The offset printed is the one in the shipping partitions.csv.
        offset, size = sut.nvs_partition_geometry(sut.DEFAULT_PARTITIONS_CSV)
        self.assertIn(f"write-flash {hex(offset)}", out)
        self.assertIn(f"({size} B, offset {hex(offset)}", out)
        self.assertEqual(gen.argv[-1], hex(size))
        # Never the key, either one, anywhere.
        for key in (OLD_KEY, NEW_KEY):
            self.assertNotIn(key, out)
            self.assertNotIn(key, err)
        self.assertIn(DEVICE_ID, out)

    def test_default_out_dir_is_a_private_temp_dir_outside_the_repo(self):
        out_dir = sut.default_rotate_out_dir(DEVICE_ID)
        try:
            self.assertTrue(out_dir.is_dir())
            self.assertFalse(out_dir.resolve().is_relative_to(sut.REPO_ROOT.resolve()))
            self.assertTrue(out_dir.name.startswith(f"areg-rotate-{DEVICE_ID[:8]}-"))
            if os.name == "posix":
                self.assertEqual(out_dir.stat().st_mode & 0o077, 0)
        finally:
            shutil.rmtree(out_dir, ignore_errors=True)
        # A malformed id cannot steer the path.
        hostile = sut.default_rotate_out_dir("../../x/y")
        try:
            self.assertEqual(hostile.parent, Path(tempfile.gettempdir()))
        finally:
            shutil.rmtree(hostile, ignore_errors=True)

    def test_rotate_without_out_dir_writes_outside_the_repo_and_prints_the_path(self):
        sut.requests = FakeRequests([rotated(OLD_KEY), rotated(NEW_KEY)])
        gen = FakeGenerator()
        argv = ["--rotate-existing", "--backend-url", BACKEND, "--mac", MAC]
        with mock.patch.object(sut.subprocess, "run", side_effect=gen):
            rc, out, err = self.run_main(argv)
        nvs_bin = Path(gen.argv[5])
        try:
            self.assertEqual(rc, 0, err)
            self.assertTrue(nvs_bin.is_file())
            self.assertFalse(nvs_bin.resolve().is_relative_to(sut.REPO_ROOT.resolve()))
            self.assertFalse((sut.REPO_ROOT / "tools" / "factory" / "out" / DEVICE_ID).exists())
            self.assertIn(f"[done] {nvs_bin.parent} contains", out)
            self.assertIn("delete it", out)
            self.assertNotIn(NEW_KEY, out + err)
        finally:
            shutil.rmtree(nvs_bin.parent, ignore_errors=True)

    def test_failure_paths_never_print_the_key(self):
        sut.requests = FakeRequests([rotated(OLD_KEY), rotated(OLD_KEY)])
        with mock.patch.object(sut.subprocess, "run", side_effect=AssertionError("must not build")):
            rc, out, err = self.run_main(self.rotate_argv())
        self.assertEqual(rc, 1)
        self.assertIn("FAIL", err)
        self.assertNotIn(OLD_KEY, out + err)
        self.assertFalse((self.out_dir / "nvs.bin").exists())

    def test_refuses_port_and_dry_run_before_any_network_call(self):
        fake = FakeRequests([])
        sut.requests = fake
        for extra in (["--port", "/dev/ttyUSB0"], ["--dry-run", "x.json"]):
            rc, _out, err = self.run_main(self.rotate_argv(*extra))
            self.assertEqual(rc, 1)
            self.assertIn("cannot be combined", err)
        self.assertEqual(fake.calls, [])

    def test_missing_generator_stops_before_any_network_call(self):
        fake = FakeRequests([])
        sut.requests = fake
        with mock.patch.object(sut, "nvs_generator_available", return_value=False):
            rc, _out, err = self.run_main(self.rotate_argv())
        self.assertEqual(rc, 1)
        self.assertIn("nothing changed on the backend", err)
        self.assertEqual(fake.calls, [])


class NewUnitImageTests(unittest.TestCase):
    def test_new_unit_image_still_carries_the_pop(self):
        gen = FakeGenerator()
        with tempfile.TemporaryDirectory() as tmp, \
                mock.patch.object(sut.subprocess, "run", side_effect=gen):
            sut.build_nvs_image(Path(tmp), DEVICE_ID, NEW_KEY, "ABCDEFGH", 0x5000)
        self.assertTrue(gen.csv_text.endswith(f"{sut.NVS_KEY_POP},data,string,ABCDEFGH\n"))

    def test_new_unit_main_path_is_unchanged(self):
        # One plain (unforced) registration, pop in the image, label rendered.
        real_requests = sut.requests
        fake = FakeRequests([brand_new(NEW_KEY)])
        sut.requests = fake
        gen = FakeGenerator()
        out, err = io.StringIO(), io.StringIO()
        try:
            with tempfile.TemporaryDirectory() as tmp, \
                    mock.patch.dict(sut.os.environ, {"AREG_PROVISIONING_SECRET": SECRET}), \
                    mock.patch.object(sut.subprocess, "run", side_effect=gen), \
                    mock.patch.object(sut, "render_label") as label, \
                    contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
                rc = sut.main(["--backend-url", BACKEND, "--mac", MAC, "--out-dir", tmp])
        finally:
            sut.requests = real_requests
        self.assertEqual(rc, 0, err.getvalue())
        self.assertEqual(len(fake.calls), 1)
        self.assertNotIn("X-Force-Rotate", fake.calls[0]["headers"])
        self.assertIn(f"{sut.NVS_KEY_POP},data,string,ABCDEFGH\n", gen.csv_text)
        label.assert_called_once()
        self.assertNotIn(NEW_KEY, out.getvalue() + err.getvalue())


if __name__ == "__main__":
    unittest.main()

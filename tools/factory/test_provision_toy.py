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
    OUTSIDE the repo, never in tools/factory/out/;
  - (review round 3) --profile is required, dev refuses an https backend
    without --bench, output defaults to ~/areg-factory-out (mode 700) and
    tools/factory/out/ + private *.pem are gitignored;
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
SECURE_BACKEND = "https://backend.example"   # the release station is https-only
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
        return ["--profile", "dev", "--rotate-existing", "--backend-url", BACKEND, "--mac", MAC,
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
        argv = ["--profile", "dev", "--rotate-existing", "--backend-url", BACKEND, "--mac", MAC]
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
                rc = sut.main(["--profile", "dev", "--backend-url", BACKEND, "--mac", MAC, "--out-dir", tmp])
        finally:
            sut.requests = real_requests
        self.assertEqual(rc, 0, err.getvalue())
        self.assertEqual(len(fake.calls), 1)
        self.assertNotIn("X-Force-Rotate", fake.calls[0]["headers"])
        self.assertIn(f"{sut.NVS_KEY_POP},data,string,ABCDEFGH\n", gen.csv_text)
        label.assert_called_once()
        self.assertNotIn(NEW_KEY, out.getvalue() + err.getvalue())


class FactoryHygieneTests(unittest.TestCase):
    """Review round 3: no unlocked toy by omission, no factory output or
    private key in git."""

    def run_main(self, argv):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            try:
                rc = sut.main(argv)
            except SystemExit as e:
                rc = e.code
        return rc, out.getvalue(), err.getvalue()

    def test_profile_is_required(self):
        rc, _out, err = self.run_main(["--backend-url", BACKEND, "--mac", MAC])
        self.assertEqual(rc, 2)
        self.assertIn("--profile", err)

    def test_dev_profile_refuses_an_https_backend_without_bench(self):
        real = sut.requests
        sut.requests = FakeRequests([])
        try:
            rc, _out, err = self.run_main(["--profile", "dev", "--backend-url", "https://prod.example", "--mac", MAC])
            self.assertEqual(rc, 1)
            self.assertIn("forgotten `--profile release`", err)
            self.assertEqual(sut.requests.calls, [], "nothing registered")
        finally:
            sut.requests = real

    def test_dev_profile_with_bench_allows_https(self):
        real = sut.requests
        fake = FakeRequests([brand_new(NEW_KEY)])
        sut.requests = fake
        gen = FakeGenerator()
        try:
            with tempfile.TemporaryDirectory() as tmp, \
                    mock.patch.dict(sut.os.environ, {"AREG_PROVISIONING_SECRET": SECRET}), \
                    mock.patch.object(sut.subprocess, "run", side_effect=gen), \
                    mock.patch.object(sut, "render_label"):
                rc, _out, err = self.run_main(["--profile", "dev", "--bench", "--backend-url", "https://prod.example",
                                               "--mac", MAC, "--out-dir", tmp])
            self.assertEqual(rc, 0, err)
        finally:
            sut.requests = real

    def test_default_output_is_private_and_outside_the_repo(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "areg-factory-out"
            d = sut.factory_out_dir("248729da-0000-4000-8000-000000000001", root)
            self.assertEqual(d.parent, root)
            if os.name == "posix":
                self.assertEqual(root.stat().st_mode & 0o077, 0)
            self.assertEqual(sut.factory_out_dir("../../x/y", root).parent, root)
        self.assertFalse(sut.DEFAULT_FACTORY_OUT.resolve().is_relative_to(sut.REPO_ROOT.resolve()))
        import secure_provision
        self.assertEqual(secure_provision.Options(mode="virt", bundle=Path("b")).out_root, sut.DEFAULT_FACTORY_OUT)

    def test_factory_output_and_private_keys_are_gitignored(self):
        if shutil.which("git") is None:
            self.skipTest("no git")
        for path, ignored in (("tools/factory/out/x/nvs.bin", True), ("tools/factory/out/x/label.pdf", True),
                              ("esp32/security/sb_primary.pem", True), ("sb_backup.pem", True),
                              ("esp32/security/sb_primary.pub.pem", False),
                              ("tools/firmware/testdata/TEST_sb_primary.pub.pem", False)):
            r = subprocess.run(["git", "-C", str(sut.REPO_ROOT), "check-ignore", "-q", path])
            self.assertEqual(r.returncode == 0, ignored, path)


# ===========================================================================
# RELEASE profile (chip security, 2026-10-08): tools/factory/secure_provision.py
# ===========================================================================
import hashlib  # noqa: E402
import importlib.util  # noqa: E402
import json  # noqa: E402
import re  # noqa: E402

import secure_provision as sp  # noqa: E402

TESTDATA = Path(__file__).resolve().parents[1] / "firmware" / "testdata"
TEST_TRUSTED = TESTDATA / "TEST_sb_trusted_digests.txt"
TEST_PRIMARY_PUB = TESTDATA / "TEST_sb_primary.pub.pem"
TEST_BACKUP_PUB = TESTDATA / "TEST_sb_backup.pub.pem"
CHIP_MAC = "aa:bb:cc:dd:ee:ff"
SECURE_KEY = KEY_PREFIX + "5ec0" * 8


def _trusted_pair():
    lines = [ln for ln in TEST_TRUSTED.read_text().splitlines() if re.fullmatch(r"[0-9a-f]{64}", ln)]
    return lines[0], lines[1]


PILOT_EVIDENCE = "tools/quality-evidence/chip-security-pilot-TEST.md"


GATE_LIB = sp.gate_lib
REAL_TABLE = GATE_LIB.encode_partition_table(GATE_LIB.partitions_from_csv(sut.DEFAULT_PARTITIONS_CSV))
FAKE_BOOT_APP0 = b"O" * 8192   # stands in for Arduino's; BOOT_APP0_SHA256 is patched to it


def released_md(path: Path, bootloader_body: bytes, status: str = "APPROVED", table: bytes = REAL_TABLE) -> Path:
    path.write_text(
        "| Date | Built by | IDF commit | Toolchain | IDF esptool / repo | Size | sha256 (bootloader-unsigned.bin) "
        "| sha256 (partition-table.bin) | Status |\n|---|---|---|---|---|---|---|---|---|\n"
        f"| d | t | x | x | x | x | `{hashlib.sha256(bootloader_body).hexdigest()}` "
        f"| `{hashlib.sha256(table).hexdigest()}` | {status} |\n")
    return path


def make_bundle(root: Path, test_signed: bool = False, pilot: bool = True) -> Path:
    root.mkdir(parents=True, exist_ok=True)
    arts = {}
    for name, fname, data in (("bootloader", "bootloader-signed.bin", b"B" * 0x7000),
                              ("partition_table", "partition-table.bin", REAL_TABLE),
                              ("boot_app0", "boot_app0.bin", FAKE_BOOT_APP0),
                              ("app", "app-signed.bin", b"A" * 0x11000)):
        (root / fname).write_bytes(data)
        arts[name] = {"file": fname, "sha256": hashlib.sha256(data).hexdigest(), "size": len(data)}
    meta = {"version": "9.9.9", "test_signed": test_signed, "artifacts": arts, "idf_commit": "x",
            "arduino_core": "3.3.8"}
    if pilot:
        meta["pilot"] = {"evidence": PILOT_EVIDENCE, "bootloader_sha256": arts["bootloader"]["sha256"]}
    (root / "bundle.json").write_text(json.dumps(meta))
    return root


def write_pilot_evidence(repo_root: Path, bootloader_sha: str, passed: bool = True) -> Path:
    p = repo_root / PILOT_EVIDENCE
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(f"# chip-security pilot (TEST fixture)\nbootloader-signed.bin sha256 {bootloader_sha}\n"
                 + ("OTA release+1 on locked board: PASS\n" if passed else "OTA release+1 on locked board: FAIL\n"))
    return p


class FakeChip:
    """A pretend ESP32-S3 + the four Espressif tools. Burns mutate a fake
    eFuse table; encrypt = XOR 0x5A (so decrypt round-trips); every command
    is recorded."""

    def __init__(self, virgin: bool = True, fail_on: str | None = None):
        self.calls: list[list[str]] = []
        self.csv_seen: list[str] = []
        self.fail_on = fail_on
        self.bits = "000"
        p, b = _trusted_pair()
        self.digests = {str(TEST_PRIMARY_PUB): p, str(TEST_BACKUP_PUB): b}
        self.ef = {}
        for i in range(6):
            self.ef[f"KEY_PURPOSE_{i}"] = {"value": "USER", "readable": True, "writeable": True}
        for i in range(6):
            self.ef[f"BLOCK_KEY{i}"] = {"value": "00 " * 31 + "00", "readable": True, "writeable": True}
        for name in ("SECURE_BOOT_EN", "DIS_DOWNLOAD_MODE", "DIS_DOWNLOAD_MANUAL_ENCRYPT", "DIS_DOWNLOAD_ICACHE",
                     "DIS_DOWNLOAD_DCACHE", "DIS_PAD_JTAG", "DIS_USB_JTAG", "DIS_DIRECT_BOOT",
                     "SECURE_BOOT_KEY_REVOKE0", "SECURE_BOOT_KEY_REVOKE1", "SECURE_BOOT_KEY_REVOKE2",
                     "SECURE_BOOT_AGGRESSIVE_REVOKE", "ENABLE_SECURITY_DOWNLOAD", "DIS_USB_SERIAL_JTAG", "DIS_ICACHE"):
            self.ef[name] = {"value": False, "readable": True, "writeable": True}
        self.ef["SOFT_DIS_JTAG"] = {"value": 0, "readable": True, "writeable": True}
        self.ef["RD_DIS"] = {"value": 0, "readable": True, "writeable": True}
        self.ef["MAC"] = {"value": "aa:bb:cc:dd:ee:ff (OK)", "readable": True, "writeable": True}
        self.swap_after: str | None = None   # a command containing this swaps in ANOTHER board
        if not virgin:
            self.ef["KEY_PURPOSE_0"]["value"] = "XTS_AES_256_KEY_1"

    def _burn(self, args: list[str]) -> None:
        i = 0
        while i < len(args):
            a = args[i]
            if a == "burn-key":
                blk, _key, purpose = args[i + 1:i + 4]
                n = int(blk[-1])
                if purpose == "XTS_AES_256_KEY":
                    for k, pur in ((n, "XTS_AES_256_KEY_1"), (n + 1, "XTS_AES_256_KEY_2")):
                        self.ef[f"KEY_PURPOSE_{k}"] = {"value": pur, "readable": True, "writeable": False}
                        self.ef[f"BLOCK_KEY{k}"]["readable"] = False
                        self.ef["RD_DIS"]["value"] |= 1 << k
                else:
                    self.ef[f"KEY_PURPOSE_{n}"] = {"value": purpose, "readable": True, "writeable": False}
                    self.ef[f"BLOCK_KEY{n}"]["readable"] = False
                    self.ef["RD_DIS"]["value"] |= 1 << n
                i += 4
            elif a == "burn-key-digest":
                blk, pub, purpose = args[i + 1:i + 4]
                n = int(blk[-1])
                d = self.digests[pub]
                self.ef[f"KEY_PURPOSE_{n}"] = {"value": purpose, "readable": True, "writeable": False}
                self.ef[f"BLOCK_KEY{n}"]["value"] = " ".join(d[j:j + 2] for j in range(0, 64, 2))
                i += 4
            elif a == "burn-efuse":
                i += 1
                while i < len(args) and args[i] not in ("burn-key", "burn-key-digest", "burn-efuse", "write-protect-efuse"):
                    name, val = args[i], args[i + 1]
                    if name == "SPI_BOOT_CRYPT_CNT":
                        self.bits = "111"
                    elif name == "SOFT_DIS_JTAG":
                        self.ef[name]["value"] = int(val)
                    else:
                        self.ef[name]["value"] = True
                    i += 2
            elif a == "write-protect-efuse":
                i += 1
                while i < len(args) and args[i] not in ("burn-key", "burn-key-digest", "burn-efuse"):
                    self.ef[args[i]]["writeable"] = False
                    i += 1
            else:
                i += 1

    def run(self, argv, cwd=None):
        argv = [str(a) for a in argv]
        self.calls.append(argv)
        ok = subprocess.CompletedProcess(argv, 0, stdout="", stderr="")
        joined = " ".join(argv)
        if self.fail_on and self.fail_on in joined:
            return subprocess.CompletedProcess(argv, 1, stdout="", stderr="simulated failure")
        if argv[0] == "shred":
            Path(argv[-1]).unlink(missing_ok=True)
            return ok
        if "check_release_image.py" in joined:
            return ok
        mod = argv[2] if len(argv) > 2 else ""
        if self.swap_after and self.swap_after in joined:
            self.ef["MAC"] = {"value": "11:22:33:44:55:66 (OK)", "readable": True, "writeable": True}
        if mod == "esptool" and "read-mac" in argv:
            ok.stdout = "Chip type:          ESP32-S3 (QFN56) (revision v0.2)\nMAC:                aa:bb:cc:dd:ee:ff\n"
        elif mod == "esptool" and "flash-id" in argv:
            ok.stdout = "Detected flash size: 8MB\n"
        elif mod == "espefuse" and "summary" in argv:
            if "--file" in argv:
                Path(argv[argv.index("--file") + 1]).write_text(json.dumps(self.ef))
            else:
                ok.stdout = f"SPI_BOOT_CRYPT_CNT (BLOCK0)  Enables flash encryption = X R/W (0b{self.bits})\n"
        elif mod == "espefuse":
            self._burn(argv[argv.index("--do-not-confirm") + 1:])
        elif mod == "espsecure" and argv[3] == "digest-sbv2-public-key":
            Path(argv[argv.index("--output") + 1]).write_bytes(bytes.fromhex(self.digests[argv[argv.index("--keyfile") + 1]]))
        elif mod == "espsecure" and argv[3] == "generate-flash-encryption-key":
            Path(argv[-1]).write_bytes(os.urandom(64))
        elif mod == "esp_idf_nvs_partition_gen" and argv[3] == "generate-key":
            (Path(cwd) / "keys").mkdir(exist_ok=True)
            (Path(cwd) / "keys" / "hmac.bin").write_bytes(os.urandom(32))
            (Path(cwd) / "keys" / "nvs_keys.bin").write_bytes(os.urandom(64))
        elif mod == "espsecure" and argv[3] in ("encrypt-flash-data", "decrypt-flash-data"):
            data = Path(argv[-1]).read_bytes()
            Path(argv[argv.index("--output") + 1]).write_bytes(bytes(b ^ 0x5A for b in data))
        elif mod == "esp_idf_nvs_partition_gen" and argv[3] == "encrypt":
            self.csv_seen.append((Path(cwd) / argv[4]).read_text())
            (Path(cwd) / argv[5]).write_bytes(b"\xee" * int(argv[6], 0))
        return ok


def good_console(device_id: str, key: str, locked: bool) -> str:
    fp = hashlib.sha256(key.encode()).digest()[:4].hex()
    if not locked:
        return ("ESP-ROM:esp32s3\n[sec] profile=release sb=1 fe=release dl=enabled jtag=off nvs=encrypted hmac=key4 "
                "sbslots=0x3 revoked=0x4 sb_rel=0 fe_rel=0 store=encrypted\n"
                f"{sp.FACTORY_WINDOW_LINE}\n"
                f"[sec] key_fp={fp}\n[device] using provisioned identity (id={device_id})\n")
    return ("[sec] profile=release sb=1 fe=release dl=disabled jtag=off nvs=encrypted hmac=key4 sbslots=0x3 "
            f"revoked=0x4 sb_rel=1 fe_rel=1 store=encrypted\n[device] using provisioned identity (id={device_id})\n")


class SecureProvisionFakeChipTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.bundle = make_bundle(self.tmp / "bundle")
        self.evidence_root = self.tmp / "repo"
        bl_sha = json.loads((self.bundle / "bundle.json").read_text())["artifacts"]["bootloader"]["sha256"]
        write_pilot_evidence(self.evidence_root, bl_sha)
        self._pilot_root = mock.patch.object(sp, "PILOT_REPO_ROOT", self.evidence_root)
        self._pilot_root.start()
        self.wroot = self.tmp / "shm" / "areg-factory"
        (self.tmp / "shm").mkdir()
        self.out_root = self.tmp / "out"
        self._req = sut.requests
        sut.requests = FakeRequests([FakeResponse(201, {"deviceId": DEVICE_ID, "apiKey": SECURE_KEY,
                                                        "claimCode": "CLAIM123", "pop": "ABCDEFGH",
                                                        "qrPayload": "{}"})])
        self._env = mock.patch.dict(os.environ, {"AREG_PROVISIONING_SECRET": SECRET})
        self._env.start()
        self._label = mock.patch.object(sut, "render_label")
        self._label.start()
        self._boot_app0 = mock.patch.object(GATE_LIB, "BOOT_APP0_SHA256", hashlib.sha256(FAKE_BOOT_APP0).hexdigest())
        self._boot_app0.start()
        self.released = released_md(self.tmp / "RELEASED.md", b"B" * 0x6000)

    def tearDown(self):
        sut.requests = self._req
        self._env.stop()
        self._label.stop()
        self._pilot_root.stop()
        self._boot_app0.stop()
        self._tmp.cleanup()

    def argv(self, *extra):
        return ["provision", "--bundle", str(self.bundle), "--port", "/dev/ttyFAKE", "--backend-url", SECURE_BACKEND,
                "--trusted-digests", str(TEST_TRUSTED), "--sb-primary-pub", str(TEST_PRIMARY_PUB),
                "--sb-backup-pub", str(TEST_BACKUP_PUB), "--workdir-root", str(self.wroot),
                "--out-root", str(self.out_root), "--released-md", str(self.released), *extra]

    def station(self, chip, consoles, answers=None):
        said = []
        consoles = list(consoles)
        answers = list(answers if answers is not None else [f"BURN {CHIP_MAC}"] * 3)
        def console(port, secs):
            chip.calls.append(["<console: normal boot>"])
            return consoles.pop(0)
        st = sp.Station(run=chip.run, console=console, ask=lambda prompt: answers.pop(0), say=said.append)
        return st, said

    def run_ok(self, chip, consoles=None, extra=(), allow_test=True):
        consoles = consoles or [good_console(DEVICE_ID, SECURE_KEY, False), good_console(DEVICE_ID, SECURE_KEY, True)]
        st, said = self.station(chip, consoles)
        # The committed TEST material is a TEST key by construction: the real
        # path must REFUSE it (tested below); here the TEST check is bypassed
        # to exercise the rest of the flow.
        with mock.patch.object(sp, "TEST_TRUSTED", self.tmp / "none.txt") if allow_test else contextlib.nullcontext():
            text_before = TEST_TRUSTED.read_text()
            trusted = self.tmp / "trusted.txt"
            trusted.write_text("\n".join(_trusted_pair()) + "\n")  # same digests, no TEST ONLY header
            argv = [a if a != str(TEST_TRUSTED) else str(trusted) for a in self.argv(*extra)]
            json_meta = json.loads((self.bundle / "bundle.json").read_text())
            json_meta["test_signed"] = False
            (self.bundle / "bundle.json").write_text(json.dumps(json_meta))
            rc = sp.main(argv, station=st)
            self.assertEqual(TEST_TRUSTED.read_text(), text_before)
        return rc, said, st

    def test_full_flow_exact_command_sequence(self):
        chip = FakeChip()
        rc, said, st = self.run_ok(chip)
        self.assertEqual(rc, 0, "\n".join(said))
        cmds = [c for c in chip.calls if c[0] != "shred"]
        kinds = []
        for c in cmds:
            if c[0].startswith("<console"):
                kinds.append("console")
            elif "check_release_image.py" in " ".join(c):
                kinds.append("gate")
            else:
                kinds.append(c[2] + ":" + next(a for a in c[3:] if not a.startswith("-") and a not in (
                    "esp32s3", "/dev/ttyFAKE", "no-reset") and not a.startswith("/")))
        self.assertEqual(kinds, [
            "espsecure:digest-sbv2-public-key", "espsecure:digest-sbv2-public-key", "gate", "gate",
            "esptool:read-mac", "esptool:flash-id",
            "espefuse:summary", "espefuse:summary",
            "espsecure:generate-flash-encryption-key", "esp_idf_nvs_partition_gen:generate-key",
            "espsecure:encrypt-flash-data", "espsecure:encrypt-flash-data", "espsecure:encrypt-flash-data",
            "espsecure:encrypt-flash-data", "espsecure:decrypt-flash-data", "esp_idf_nvs_partition_gen:encrypt",
            "esptool:erase-flash", "esptool:write-flash",
            "espefuse:summary",                                   # MAC check (same chip?) before step 6
            "espefuse:burn-key", "espefuse:summary", "espefuse:summary",
            "espefuse:summary",                                   # MAC check before step 7
            "espefuse:burn-key-digest", "espefuse:summary", "espefuse:summary",
            "console",                                            # step 8: the first NORMAL boot
            "espefuse:summary",                                   # MAC check before step 9
            "espefuse:burn-efuse",
            "console",                                            # step 9 check
        ])
        gate_bl = next(c for c in cmds if "--bootloader" in c)
        self.assertEqual(gate_bl[gate_bl.index("--min-status") + 1], "APPROVED")
        step6 = next(c for c in cmds if "burn-key" in c and "BLOCK_KEY0" in c)
        tail6 = step6[step6.index("--do-not-confirm"):]
        self.assertEqual([a if not a.startswith("/") else Path(a).name for a in tail6], [
            "--do-not-confirm", "burn-key", "BLOCK_KEY0", "fe.bin", "XTS_AES_256_KEY",
            "burn-key", "BLOCK_KEY4", "hmac.bin", "HMAC_UP",
            "burn-efuse", "SPI_BOOT_CRYPT_CNT", "7", "DIS_DOWNLOAD_MANUAL_ENCRYPT", "1", "DIS_DOWNLOAD_ICACHE", "1",
            "DIS_DOWNLOAD_DCACHE", "1", "DIS_PAD_JTAG", "1", "DIS_USB_JTAG", "1", "DIS_DIRECT_BOOT", "1",
            "SOFT_DIS_JTAG", "7", "write-protect-efuse", "DIS_ICACHE"])
        step7 = next(c for c in cmds if "burn-key-digest" in c)
        tail7 = step7[step7.index("--do-not-confirm"):]
        self.assertEqual([a if not a.startswith("/") else Path(a).name for a in tail7], [
            "--do-not-confirm", "burn-key-digest", "BLOCK_KEY2", "TEST_sb_primary.pub.pem", "SECURE_BOOT_DIGEST0",
            "burn-key-digest", "BLOCK_KEY3", "TEST_sb_backup.pub.pem", "SECURE_BOOT_DIGEST1",
            "burn-efuse", "SECURE_BOOT_KEY_REVOKE2", "1", "SECURE_BOOT_EN", "1",
            "write-protect-efuse", "RD_DIS", "KEY_PURPOSE_5"])
        self.assertEqual(cmds[-2][-4:], ["--do-not-confirm", "burn-efuse", "DIS_DOWNLOAD_MODE", "1"])
        wf = next(c for c in cmds if "write-flash" in c)
        self.assertEqual([a if not a.startswith("/") else Path(a).name for a in wf[wf.index("write-flash"):]], [
            "write-flash", "--flash-mode", "keep", "--flash-freq", "keep", "--flash-size", "keep",
            "0x0", "bl.enc", "0x8000", "pt.enc", "0xe000", "ota.enc", "0x10000", "app.enc", "0x7F0000", "nvs_sec.bin"])
        self.assertFalse([c for c in chip.calls if "--force" in c], "no --force in the normal path")

    def test_api_key_never_printed_or_on_a_command_line(self):
        chip = FakeChip()
        rc, said, st = self.run_ok(chip)
        self.assertEqual(rc, 0)
        self.assertNotIn(SECURE_KEY, "\n".join(said))
        self.assertFalse([c for c in chip.calls if SECURE_KEY in " ".join(c)])
        record = (self.out_root / DEVICE_ID / "factory-record.json").read_text()
        self.assertNotIn(SECURE_KEY, record)
        self.assertIn(SECURE_KEY, chip.csv_seen[0], "the key reaches the NVS generator through the CSV only")

    def test_keys_live_only_in_the_workdir_and_are_shredded_on_success(self):
        chip = FakeChip()
        rc, _, _ = self.run_ok(chip)
        self.assertEqual(rc, 0)
        wd = self.wroot / "aabbccddeeff"
        for c in chip.calls:
            for a in c:
                if a.endswith(("fe.bin", "hmac.bin", "nvs_keys.bin", "id.csv")):
                    self.assertTrue(str(Path(a)).startswith(str(wd)) or not a.startswith("/"), a)
        self.assertFalse(wd.exists(), "workdir must be gone after success")
        self.assertEqual(oct((self.wroot).stat().st_mode & 0o777), "0o700")

    def test_failed_verification_keeps_the_workdir_and_never_locks(self):
        chip = FakeChip()
        bad = good_console(DEVICE_ID, SECURE_KEY, False).replace("store=encrypted", "store=plaintext")
        rc, said, _ = self.run_ok(chip, consoles=[bad])
        self.assertEqual(rc, 1)
        self.assertIn("step 8 verification FAILED", "\n".join(said))
        wd = self.wroot / "aabbccddeeff"
        self.assertTrue((wd / "fe.bin").is_file() and (wd / "keys" / "hmac.bin").is_file(), "kept for repair")
        self.assertFalse((wd / "id.csv").exists(), "the identity CSV is shredded even on failure")
        self.assertFalse([c for c in chip.calls if "DIS_DOWNLOAD_MODE" in c])

    def test_wrong_key_fingerprint_fails_step8(self):
        chip = FakeChip()
        other = good_console(DEVICE_ID, KEY_PREFIX + "f" * 32, False)
        rc, said, _ = self.run_ok(chip, consoles=[other])
        self.assertEqual(rc, 1)
        self.assertIn("key_fp does not match", "\n".join(said))

    def test_non_virgin_chip_stops_before_anything(self):
        chip = FakeChip(virgin=False)
        rc, said, _ = self.run_ok(chip)
        self.assertEqual(rc, 1)
        self.assertIn("NOT virgin", "\n".join(said))
        self.assertFalse([c for c in chip.calls if "burn-key" in c or "write-flash" in c or "erase-flash" in c])
        self.assertEqual(sut.requests.calls, [], "no registration for a refused chip")

    def test_unconfirmed_burn_burns_nothing(self):
        chip = FakeChip()
        st, said = self.station(chip, [], answers=["yes"])
        trusted = self.tmp / "trusted.txt"
        trusted.write_text("\n".join(_trusted_pair()) + "\n")
        meta = json.loads((self.bundle / "bundle.json").read_text())
        meta["test_signed"] = False
        (self.bundle / "bundle.json").write_text(json.dumps(meta))
        argv = [a if a != str(TEST_TRUSTED) else str(trusted) for a in self.argv()]
        with mock.patch.object(sp, "TEST_TRUSTED", self.tmp / "none.txt"):
            rc = sp.main(argv, station=st)
        self.assertEqual(rc, 1)
        self.assertIn("not confirmed", "\n".join(said))
        self.assertFalse([c for c in chip.calls if "burn-key" in c])

    def test_real_run_refuses_test_keys_and_test_bundles(self):
        chip = FakeChip()
        meta = json.loads((self.bundle / "bundle.json").read_text())
        meta["test_signed"] = True
        (self.bundle / "bundle.json").write_text(json.dumps(meta))
        st, said = self.station(chip, [])
        rc = sp.main(self.argv(), station=st)   # TEST ONLY digests file + test_signed bundle
        self.assertEqual(rc, 1)
        self.assertIn("TEST", "\n".join(said))
        self.assertFalse([c for c in chip.calls if "read-mac" in c], "refused before touching the chip")
        trusted = self.tmp / "trusted.txt"
        trusted.write_text("\n".join(_trusted_pair()) + "\n")   # TEST digests without the header
        meta = json.loads((self.bundle / "bundle.json").read_text())
        meta["test_signed"] = False
        (self.bundle / "bundle.json").write_text(json.dumps(meta))
        st, said = self.station(chip, [])
        rc = sp.main([a if a != str(TEST_TRUSTED) else str(trusted) for a in self.argv()], station=st)
        self.assertEqual(rc, 1)
        self.assertIn("TEST signing key", "\n".join(said))

    def test_tampered_bundle_is_refused(self):
        (self.bundle / "app-signed.bin").write_bytes(b"X" * 0x11000)
        st, said = self.station(FakeChip(), [])
        rc = sp.main(self.argv(), station=st)
        self.assertEqual(rc, 1)
        self.assertIn("sha256", "\n".join(said))

    def test_private_key_at_the_station_is_refused(self):
        priv = self.tmp / "oops.pem"
        # A fake, contentless PEM; the marker is assembled at runtime so secret
        # scanners never see a private-key header literal in the repo.
        tag = "PRIVATE " + "KEY"
        priv.write_text(f"-----BEGIN {tag}-----\nxx\n-----END {tag}-----\n")
        st, said = self.station(FakeChip(), [])
        trusted = self.tmp / "trusted.txt"
        trusted.write_text("\n".join(_trusted_pair()) + "\n")
        argv = [str(priv) if a == str(TEST_PRIMARY_PUB) else str(trusted) if a == str(TEST_TRUSTED) else a
                for a in self.argv()]
        with mock.patch.object(sp, "TEST_TRUSTED", self.tmp / "none.txt"):
            rc = sp.main(argv, station=st)
        self.assertEqual(rc, 1)
        self.assertIn("PRIVATE key", "\n".join(said))

    def test_repair_finishes_only_missing_burns(self):
        chip = FakeChip()
        # first run dies at step 7
        chip.fail_on = "burn-key-digest"
        rc, said, _ = self.run_ok(chip)
        self.assertEqual(rc, 1)
        wd = self.wroot / "aabbccddeeff"
        self.assertTrue((wd / "state.json").is_file())
        chip.fail_on = None
        chip.calls.clear()
        st, said = self.station(chip, [good_console(DEVICE_ID, SECURE_KEY, False), good_console(DEVICE_ID, SECURE_KEY, True)])
        trusted = self.tmp / "trusted.txt"
        argv = ["repair", "--bundle", str(self.bundle), "--port", "/dev/ttyFAKE", "--trusted-digests", str(trusted),
                "--sb-primary-pub", str(TEST_PRIMARY_PUB), "--sb-backup-pub", str(TEST_BACKUP_PUB),
                "--workdir-root", str(self.wroot), "--out-root", str(self.out_root), "--reflash",
                "--released-md", str(self.released)]
        with mock.patch.object(sp, "TEST_TRUSTED", self.tmp / "none.txt"):
            rc = sp.main(argv, station=st)
        self.assertEqual(rc, 0, "\n".join(said))
        burns = [c for c in chip.calls if "--do-not-confirm" in c]
        self.assertFalse([c for c in burns if "BLOCK_KEY0" in c], "step 6 is NOT repeated")
        self.assertTrue([c for c in burns if "burn-key-digest" in c], "step 7 is finished")
        wf = [c for c in chip.calls if "write-flash" in c]
        self.assertTrue(wf and "--force" in wf[0] and "--no-stub" in wf[0], "--reflash uses --no-stub --force")
        self.assertFalse(wd.exists())

    def test_pre_burn_failure_resumes_without_a_second_registration(self):
        chip = FakeChip(fail_on="erase-flash")
        rc, said, _ = self.run_ok(chip)
        self.assertEqual(rc, 1)
        wd = self.wroot / "aabbccddeeff"
        self.assertTrue((wd / "state.json").is_file())
        self.assertFalse((wd / sp.BURN_MARKER).exists(), "nothing burned yet")
        chip.fail_on = None
        rc, said, _ = self.run_ok(chip)
        self.assertEqual(rc, 0, "\n".join(said))
        self.assertIn("[resume]", "\n".join(said))
        self.assertEqual(len(sut.requests.calls), 1, "the toy is registered exactly once")
        self.assertFalse(wd.exists())

    def test_after_a_burn_only_repair_may_continue(self):
        chip = FakeChip(fail_on="burn-key-digest")
        rc, _, _ = self.run_ok(chip)
        self.assertEqual(rc, 1)
        chip.fail_on = None
        rc, said, _ = self.run_ok(chip)
        self.assertEqual(rc, 1)
        self.assertIn("use `repair`", "\n".join(said))

    # ---- review 2026-10-08: half-secured toys, hardware-pilot gate ----------
    def test_failure_before_any_burn_writes_incomplete_and_do_not_ship(self):
        chip = FakeChip(fail_on="erase-flash")
        rc, said, _ = self.run_ok(chip)
        self.assertEqual(rc, 1)
        out = "\n".join(said)
        self.assertIn("DO NOT SHIP", out)
        self.assertIn(f"POST /api/internal/devices/{DEVICE_ID}/revoke", out)
        rec = json.loads((self.out_root / DEVICE_ID / "INCOMPLETE.json").read_text())
        self.assertEqual(rec["deviceId"], DEVICE_ID)
        self.assertEqual(rec["mac"], CHIP_MAC)
        self.assertFalse(rec["efuses_burned"])
        self.assertEqual(rec["last_completed_step"], "4")
        self.assertNotIn(SECURE_KEY, json.dumps(rec))
        self.assertNotIn(SECURE_KEY, out)
        # finishing the toy later removes the INCOMPLETE record
        chip.fail_on = None
        rc, said, _ = self.run_ok(chip)
        self.assertEqual(rc, 0, "\n".join(said))
        self.assertFalse((self.out_root / DEVICE_ID / "INCOMPLETE.json").exists())
        self.assertTrue((self.out_root / DEVICE_ID / "factory-record.json").is_file())

    def test_failure_after_a_burn_says_half_secured_repair_or_revoke(self):
        chip = FakeChip(fail_on="burn-key-digest")
        rc, said, _ = self.run_ok(chip)
        self.assertEqual(rc, 1)
        out = "\n".join(said)
        self.assertIn("HALF-secured", out)
        self.assertIn("repair", out)
        self.assertIn(f"POST /api/internal/devices/{DEVICE_ID}/revoke", out)
        rec = json.loads((self.out_root / DEVICE_ID / "INCOMPLETE.json").read_text())
        self.assertTrue(rec["efuses_burned"])
        self.assertEqual(rec["last_completed_step"], "6")

    def test_declined_step9_leaves_an_incomplete_record(self):
        chip = FakeChip()
        st, said = self.station(chip, [good_console(DEVICE_ID, SECURE_KEY, False)],
                                answers=[f"BURN {CHIP_MAC}", "no"])   # 6+7 confirmed once; step 9 declined
        trusted = self.tmp / "trusted.txt"
        trusted.write_text("\n".join(_trusted_pair()) + "\n")
        meta = json.loads((self.bundle / "bundle.json").read_text())
        meta["test_signed"] = False
        (self.bundle / "bundle.json").write_text(json.dumps(meta))
        argv = [a if a != str(TEST_TRUSTED) else str(trusted) for a in self.argv()]
        with mock.patch.object(sp, "TEST_TRUSTED", self.tmp / "none.txt"):
            rc = sp.main(argv, station=st)
        self.assertEqual(rc, 1)
        self.assertFalse([c for c in chip.calls if "DIS_DOWNLOAD_MODE" in c])
        rec = json.loads((self.out_root / DEVICE_ID / "INCOMPLETE.json").read_text())
        self.assertEqual(rec["last_completed_step"], "8")
        self.assertTrue(rec["efuses_burned"])
        self.assertIn("DO NOT SHIP", "\n".join(said))

    def test_failed_step9_check_is_set_aside_not_repaired(self):
        chip = FakeChip()
        still_open = good_console(DEVICE_ID, SECURE_KEY, True).replace("dl=disabled", "dl=enabled")
        rc, said, _ = self.run_ok(chip, consoles=[good_console(DEVICE_ID, SECURE_KEY, False), still_open])
        self.assertEqual(rc, 1)
        out = "\n".join(said)
        self.assertIn("No cable can repair it", out)
        rec = json.loads((self.out_root / DEVICE_ID / "INCOMPLETE.json").read_text())
        self.assertEqual(rec["last_completed_step"], "9-burned")

    def test_step8_requires_the_factory_window_offline_line(self):
        chip = FakeChip()
        old_image = good_console(DEVICE_ID, SECURE_KEY, False).replace(sp.FACTORY_WINDOW_LINE + "\n", "")
        rc, said, _ = self.run_ok(chip, consoles=[old_image])
        self.assertEqual(rc, 1)
        self.assertIn("factory-window line", "\n".join(said))
        self.assertFalse([c for c in chip.calls if "DIS_DOWNLOAD_MODE" in c])

    def test_provision_without_pilot_evidence_is_refused_before_the_chip(self):
        meta = json.loads((self.bundle / "bundle.json").read_text())
        del meta["pilot"]
        (self.bundle / "bundle.json").write_text(json.dumps(meta))
        chip = FakeChip()
        rc, said, _ = self.run_ok(chip)
        self.assertEqual(rc, 1)
        self.assertIn("no hardware-pilot evidence", "\n".join(said))
        self.assertFalse([c for c in chip.calls if "read-mac" in c], "refused before touching the chip")
        self.assertEqual(sut.requests.calls, [], "and before registering anything")

    def test_pilot_evidence_must_name_this_bootloader_and_pass(self):
        write_pilot_evidence(self.evidence_root, "ab" * 32)  # the pilot proved a different bootloader
        rc, said, _ = self.run_ok(FakeChip())
        self.assertEqual(rc, 1)
        self.assertIn("does not name this bundle's signed bootloader", "\n".join(said))
        bl_sha = json.loads((self.bundle / "bundle.json").read_text())["artifacts"]["bootloader"]["sha256"]
        write_pilot_evidence(self.evidence_root, bl_sha, passed=False)
        rc, said, _ = self.run_ok(FakeChip())
        self.assertEqual(rc, 1)
        self.assertIn("OTA release+1 on locked board: PASS", "\n".join(said))
        meta = json.loads((self.bundle / "bundle.json").read_text())
        meta["pilot"]["evidence"] = "../outside.md"
        (self.bundle / "bundle.json").write_text(json.dumps(meta))
        rc, said, _ = self.run_ok(FakeChip())
        self.assertEqual(rc, 1)
        self.assertIn("relative to the repository", "\n".join(said))

    def test_pilot_board_flag_provisions_without_evidence_and_records_it(self):
        meta = json.loads((self.bundle / "bundle.json").read_text())
        del meta["pilot"]
        (self.bundle / "bundle.json").write_text(json.dumps(meta))
        rc, said, _ = self.run_ok(FakeChip(), extra=("--pilot-board",))
        self.assertEqual(rc, 0, "\n".join(said))
        self.assertIn("NEVER SHIP", "\n".join(said))
        rec = json.loads((self.out_root / DEVICE_ID / "factory-record.json").read_text())
        self.assertTrue(rec["pilot_board"])

    # ---- review round 3 ------------------------------------------------------
    def test_every_esptool_call_never_resets_the_chip(self):
        chip = FakeChip()
        rc, said, _ = self.run_ok(chip)
        self.assertEqual(rc, 0, "\n".join(said))
        esptool = [c for c in chip.calls if len(c) > 2 and c[2] == "esptool"]
        self.assertEqual(len(esptool), 4, "read-mac, flash-id, erase-flash, write-flash")
        for c in esptool:
            i = c.index("--after")
            self.assertEqual(c[i + 1], "no-reset", c)
            self.assertLess(i, max(c.index(k) for k in ("read-mac", "flash-id", "erase-flash", "write-flash") if k in c))
        flow = sp.Flow(sp.Options(mode="repair", bundle=self.bundle, port="/dev/ttyFAKE"), sp.Station())
        wa = flow.write_args({"enc": {a: Path(a) for a in ("0x0", "0x8000", "0xe000", "0x10000")},
                              "nvs_sec": Path("n"), "nvs_off": 0x7F0000}, force=True)
        self.assertEqual(wa[wa.index("--after") + 1], "no-reset")
        self.assertIn("--force", wa)

    def test_steps_6_and_7_share_one_confirmation(self):
        chip = FakeChip()
        prompts = []
        st, said = self.station(chip, [good_console(DEVICE_ID, SECURE_KEY, False), good_console(DEVICE_ID, SECURE_KEY, True)])
        answers = [f"BURN {CHIP_MAC}"] * 2
        st.ask = lambda prompt: (prompts.append(prompt), answers.pop(0))[1]
        trusted = self.tmp / "trusted.txt"
        trusted.write_text("\n".join(_trusted_pair()) + "\n")
        meta = json.loads((self.bundle / "bundle.json").read_text())
        meta["test_signed"] = False
        (self.bundle / "bundle.json").write_text(json.dumps(meta))
        with mock.patch.object(sp, "TEST_TRUSTED", self.tmp / "none.txt"):
            rc = sp.main([a if a != str(TEST_TRUSTED) else str(trusted) for a in self.argv()], station=st)
        self.assertEqual(rc, 0, "\n".join(said))
        self.assertEqual(len(prompts), 2, prompts)
        self.assertIn("steps 6 AND 7", prompts[0])
        self.assertIn("step 9", prompts[1])
        i6 = next(i for i, c in enumerate(chip.calls) if "BLOCK_KEY0" in c)
        i7 = next(i for i, c in enumerate(chip.calls) if "burn-key-digest" in c)
        between = chip.calls[i6 + 1:i7]
        self.assertFalse([c for c in between if c[0].startswith("<console")], "no normal boot between 6 and 7")
        self.assertFalse([c for c in between if len(c) > 2 and c[2] == "esptool"])

    def test_a_swapped_board_is_never_burned(self):
        chip = FakeChip()
        chip.swap_after = "write-flash"   # someone replugs another board after step 5
        rc, said, _ = self.run_ok(chip)
        self.assertEqual(rc, 1)
        out = "\n".join(said)
        self.assertIn("swapped or replugged", out)
        self.assertIn("re-run `provision`", out, "nothing burned yet: a re-run, not a repair")
        self.assertFalse([c for c in chip.calls if "--do-not-confirm" in c], "nothing burned")
        rec = json.loads((self.out_root / DEVICE_ID / "INCOMPLETE.json").read_text())
        self.assertEqual(rec["last_completed_step"], "5")

    def test_a_board_swapped_between_6_and_7_stops_before_7(self):
        chip = FakeChip()
        chip.swap_after = "BLOCK_KEY0"
        rc, said, _ = self.run_ok(chip)
        self.assertEqual(rc, 1)
        self.assertIn("step 7: the chip on", "\n".join(said))
        self.assertIn("run `repair`", "\n".join(said), "step 6 already burned: only repair may continue")
        self.assertFalse([c for c in chip.calls if "burn-key-digest" in c])

    def test_release_station_is_https_only(self):
        chip = FakeChip()
        argv_http = [a if a != SECURE_BACKEND else BACKEND for a in self.argv()]
        with mock.patch.object(self, "argv", lambda *extra: argv_http + list(extra)):
            rc, said, _ = self.run_ok(chip)
        self.assertEqual(rc, 1)
        self.assertIn("must be https://", "\n".join(said))
        self.assertEqual(sut.requests.calls, [], "the provisioning secret never went out")
        self.assertFalse([c for c in chip.calls if "erase-flash" in c or "--do-not-confirm" in c])

    def test_repair_of_a_half_burned_toy_reads_efuses_first_and_never_boots_it_before_step7(self):
        chip = FakeChip(fail_on="burn-key-digest")   # first run dies between 6 and 7
        rc, _, _ = self.run_ok(chip)
        self.assertEqual(rc, 1)
        chip.fail_on = None
        chip.calls.clear()
        st, said = self.station(chip, [good_console(DEVICE_ID, SECURE_KEY, False), good_console(DEVICE_ID, SECURE_KEY, True)])
        trusted = self.tmp / "trusted.txt"
        argv = ["repair", "--bundle", str(self.bundle), "--port", "/dev/ttyFAKE", "--trusted-digests", str(trusted),
                "--sb-primary-pub", str(TEST_PRIMARY_PUB), "--sb-backup-pub", str(TEST_BACKUP_PUB),
                "--workdir-root", str(self.wroot), "--out-root", str(self.out_root), "--reflash",
                "--released-md", str(self.released)]
        with mock.patch.object(sp, "TEST_TRUSTED", self.tmp / "none.txt"):
            rc = sp.main(argv, station=st)
        self.assertEqual(rc, 0, "\n".join(said))
        self.assertIn("no normal boot until step 7 is verified", "\n".join(said))
        chip_cmds = [c for c in chip.calls if c[0].startswith("<console") or (len(c) > 2 and c[2] in ("esptool", "espefuse"))]
        self.assertEqual(chip_cmds[0][2], "espefuse", "the eFuses are read before anything else touches the chip")
        self.assertIn("summary", chip_cmds[0])
        i7 = next(i for i, c in enumerate(chip_cmds) if "burn-key-digest" in c)
        first_boot = next(i for i, c in enumerate(chip_cmds) if c[0].startswith("<console"))
        self.assertLess(i7, first_boot, "step 7 is burned before the first normal boot")

    def test_station_refuses_a_normal_boot_while_half_burned(self):
        flow = sp.Flow(sp.Options(mode="repair", bundle=self.bundle, port="/dev/ttyFAKE"),
                       sp.Station(console=lambda port, secs: self.fail("must not boot")))
        flow.normal_boot_forbidden = True
        with self.assertRaises(sp.StationError):
            flow.console()
        self.assertTrue(sp.half_burned({"SECURE_BOOT_EN": {"value": False}}, "111"))
        self.assertFalse(sp.half_burned({"SECURE_BOOT_EN": {"value": True}}, "111"))
        self.assertFalse(sp.half_burned({"SECURE_BOOT_EN": {"value": False}}, "000"))

    def test_frozen_pieces_are_pinned_before_the_chip(self):
        cases = []
        # 1. a partition table that is not the one recorded with this bootloader
        other_rows = GATE_LIB.partitions_from_csv(sut.DEFAULT_PARTITIONS_CSV)
        other_rows[-1] = {**other_rows[-1], "offset": 0x7E0000}
        cases.append(("partition_table", GATE_LIB.encode_partition_table(other_rows), "partition-table.bin"))
        # 2. not Arduino 3.3.8's boot_app0
        cases.append(("boot_app0", b"X" * 8192, "boot_app0.bin"))
        # 3. a dual-signed bootloader that is not a recorded build
        cases.append(("bootloader", b"C" * 0x7000, "not a build recorded"))
        for name, data, needle in cases:
            bundle = make_bundle(self.tmp / f"bundle-{name}")
            meta = json.loads((bundle / "bundle.json").read_text())
            (bundle / meta["artifacts"][name]["file"]).write_bytes(data)
            meta["artifacts"][name]["sha256"] = hashlib.sha256(data).hexdigest()
            meta["pilot"]["bootloader_sha256"] = meta["artifacts"]["bootloader"]["sha256"]
            (bundle / "bundle.json").write_text(json.dumps(meta))
            write_pilot_evidence(self.evidence_root, meta["artifacts"]["bootloader"]["sha256"])
            chip = FakeChip()
            argv = [a if a != str(self.bundle) else str(bundle) for a in self.argv()]
            with mock.patch.object(self, "argv", lambda *extra, _a=argv: _a + list(extra)):
                rc, said, _ = self.run_ok(chip)
            self.assertEqual(rc, 1, name)
            self.assertIn(needle, "\n".join(said), name)
            self.assertFalse([c for c in chip.calls if "read-mac" in c], f"{name}: refused before the chip")

    def test_a_pilot_bootloader_locks_only_pilot_boards(self):
        released_md(self.released, b"B" * 0x6000, status="PILOT")
        rc, said, _ = self.run_ok(FakeChip())
        self.assertEqual(rc, 1)
        self.assertIn("needs at least APPROVED", "\n".join(said))
        meta = json.loads((self.bundle / "bundle.json").read_text())
        del meta["pilot"]
        (self.bundle / "bundle.json").write_text(json.dumps(meta))
        rc, said, st = self.run_ok(FakeChip(), extra=("--pilot-board",))
        self.assertEqual(rc, 0, "\n".join(said))

    def test_nvs_sec_offset_comes_from_a_table_that_matches_the_csv(self):
        self.assertEqual(GATE_LIB.partition_table_problems(REAL_TABLE, sut.DEFAULT_PARTITIONS_CSV), [])
        rows = GATE_LIB.parse_partition_table(REAL_TABLE)
        nvs_sec = next(r for r in rows if r["name"] == "nvs_sec")
        self.assertEqual((nvs_sec["offset"], nvs_sec["size"]), sp.partition_geometry(sut.DEFAULT_PARTITIONS_CSV, "nvs_sec"))

    def test_provision_toy_release_profile_delegates(self):
        args = sut.argparse.Namespace(bundle=Path("b"), dry_run=None, dry_run_secure=Path("r.json"), virt=True,
                                      efuse_file=None, partitions_csv=Path("p.csv"), port=None, mac="m",
                                      backend_url=None, confirm_mac=None, rotate_existing=True)
        self.assertEqual(sut.release_argv(args), ["virt", "--response", "r.json", "--bundle", "b",
                                                  "--partitions-csv", "p.csv", "--mac", "m", "--rotate-existing"])
        args.bundle = None
        with self.assertRaises(sut.ProvisionError):
            sut.release_argv(args)

    def test_dev_profile_targets_nvs_sec(self):
        self.assertEqual(sut.nvs_partition_geometry(sut.DEFAULT_PARTITIONS_CSV), (0x7F0000, 0x10000))


class SecureProvisionPureTests(unittest.TestCase):
    def test_minted_pop_matches_the_firmware_rules(self):
        for _ in range(200):
            pop = sp.mint_pop()
            self.assertEqual(len(pop), 8)
            self.assertTrue(set(pop) <= set(sp.POP_ALPHABET))
        self.assertEqual(sp.POP_ALPHABET, sut.POP_ALPHABET if hasattr(sut, "POP_ALPHABET") else sp.POP_ALPHABET)

    def test_identity_csv_shape(self):
        csv = sp.identity_csv(DEVICE_ID, SECURE_KEY, "ABCDEFGH")
        self.assertEqual(csv.splitlines(), [
            "key,type,encoding,value", "aregdev,namespace,,", f"devid,data,string,{DEVICE_ID}",
            f"apikey,data,string,{SECURE_KEY}", "pop,data,string,ABCDEFGH", "aregsys,namespace,,",
            "layout,data,u8,1"])
        with self.assertRaises(sp.StationError):
            sp.identity_csv(DEVICE_ID, "a,b", "ABCDEFGH")

    def test_key_fingerprint(self):
        self.assertEqual(sp.key_fingerprint("abc"), hashlib.sha256(b"abc").hexdigest()[:8])

    def test_posture_parse_takes_the_last_line(self):
        txt = good_console(DEVICE_ID, SECURE_KEY, False) + good_console(DEVICE_ID, SECURE_KEY, True)
        self.assertEqual(sp.parse_posture(txt)["dl"], "disabled")
        self.assertEqual(sp.check_step9_console(good_console(DEVICE_ID, SECURE_KEY, True)), [])
        self.assertTrue(sp.check_step9_console(good_console(DEVICE_ID, SECURE_KEY, False)))


@unittest.skipUnless(all(importlib.util.find_spec(m) for m in ("esptool", "espefuse", "espsecure",
                                                                "esp_idf_nvs_partition_gen")),
                     "needs esptool 5.2.0 + esp-idf-nvs-partition-gen 0.3.0 (pip install -r tools/factory/requirements.txt)")
class SecureProvisionVirtIntegrationTests(unittest.TestCase):
    """Runs the REAL Espressif tools against virtual eFuses (`espefuse --virt`):
    every burn of steps 6, 7 and 9 is executed and every summary asserted by
    the flow itself; the committed TEST vectors form the bundle. The bundle has
    no "pilot" entry on purpose: rehearsals are never pilot-gated."""

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        b = self.tmp / "bundle"
        b.mkdir()
        arts = {}
        for name, src, fname in (("bootloader", TESTDATA / "TEST_bootloader_signed.bin", "bootloader-signed.bin"),
                                 ("app", TESTDATA / "TEST_app_signed.bin", "app-signed.bin")):
            shutil.copyfile(src, b / fname)
        (b / "partition-table.bin").write_bytes(REAL_TABLE)
        (b / "boot_app0.bin").write_bytes(FAKE_BOOT_APP0)
        # The TEST bootloader vector recorded as a CANDIDATE build: what a rehearsal accepts.
        self.released = released_md(self.tmp / "RELEASED.md",
                                    (TESTDATA / "TEST_bootloader_signed.bin").read_bytes()[:-4096], "CANDIDATE")
        self._boot_app0 = mock.patch.object(GATE_LIB, "BOOT_APP0_SHA256", hashlib.sha256(FAKE_BOOT_APP0).hexdigest())
        self._boot_app0.start()
        self.addCleanup(self._boot_app0.stop)
        for name, fname in (("bootloader", "bootloader-signed.bin"), ("app", "app-signed.bin"),
                            ("partition_table", "partition-table.bin"), ("boot_app0", "boot_app0.bin")):
            data = (b / fname).read_bytes()
            arts[name] = {"file": fname, "sha256": hashlib.sha256(data).hexdigest(), "size": len(data)}
        (b / "bundle.json").write_text(json.dumps({"version": "9.9.9", "test_signed": True, "artifacts": arts}))
        self.bundle = b
        resp = self.tmp / "resp.json"
        resp.write_text(json.dumps({"deviceId": DEVICE_ID, "apiKey": SECURE_KEY, "claimCode": "CLAIM123",
                                    "pop": "ABCDEFGH", "qrPayload": "{}"}))
        self.resp = resp
        self.efuse = self.tmp / "efuse.bin"

    def tearDown(self):
        self._tmp.cleanup()

    def virt(self):
        said = []
        st = sp.Station(say=said.append)
        rc = sp.main(["virt", "--bundle", str(self.bundle), "--response", str(self.resp), "--mac", CHIP_MAC,
                      "--trusted-digests", str(TEST_TRUSTED), "--sb-primary-pub", str(TEST_PRIMARY_PUB),
                      "--sb-backup-pub", str(TEST_BACKUP_PUB), "--efuse-file", str(self.efuse),
                      "--out-root", str(self.tmp / "out"), "--released-md", str(self.released)], station=st)
        return rc, "\n".join(said), st

    def test_virt_flow_burns_and_verifies_every_step_then_refuses_a_second_run(self):
        rc, out, st = self.virt()
        self.assertEqual(rc, 0, out)
        for marker in ("[6] eFuses", "[7] eFuses", "[9] (virt) DIS_DOWNLOAD_MODE verified", "[10] workdir shredded"):
            self.assertIn(marker, out)
        self.assertNotIn(SECURE_KEY, out)
        record = json.loads((self.tmp / "out" / DEVICE_ID / "factory-record.json").read_text())
        e = record["efuses_after_step7"]
        self.assertEqual(e["RD_DIS"], 19)
        self.assertEqual([e[f"KEY_PURPOSE_{i}"] for i in range(6)], list(sp.FINAL_PURPOSES.values()))
        self.assertTrue(e["SECURE_BOOT_KEY_REVOKE2"])
        self.assertEqual(e["BLOCK_KEY2"].replace(" ", ""), _trusted_pair()[0])
        self.assertNotIn(SECURE_KEY, json.dumps(record))
        # Same (now locked) virtual chip again -> refused before any burn.
        rc2, out2, st2 = self.virt()
        self.assertEqual(rc2, 1)
        self.assertIn("NOT virgin", out2)
        self.assertFalse([c for c in st2.log if "--do-not-confirm" in c])


if __name__ == "__main__":
    unittest.main()

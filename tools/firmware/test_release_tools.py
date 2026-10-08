#!/usr/bin/env python3
"""Unit tests for build_release.py (arduino-cli path), sign_release.py
(offline signing), release_checks.py (provenance) and build_idf.sh's signing
policy. Fake runners and throwaway git repos only -- no arduino-cli, no
espsecure, no ESP-IDF, no key.

    python3 tools/firmware/test_release_tools.py -v
"""
import contextlib
import hashlib
import io
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import build_release  # noqa: E402
import release_checks  # noqa: E402
import sign_release  # noqa: E402

REPO = HERE.parents[1]

TESTDATA = HERE / "testdata"
DIGESTS = [ln for ln in (TESTDATA / "TEST_sb_trusted_digests.txt").read_text().splitlines()
           if len(ln) == 64 and not ln.startswith("#")]


def done(argv, rc=0, out=""):
    return subprocess.CompletedProcess(argv, rc, stdout=out, stderr="")


def git_repo(root: Path) -> Path:
    root.mkdir(parents=True, exist_ok=True)
    env = {**os.environ, "GIT_AUTHOR_NAME": "t", "GIT_AUTHOR_EMAIL": "t@t", "GIT_COMMITTER_NAME": "t",
           "GIT_COMMITTER_EMAIL": "t@t"}
    for args in (["init", "-q"], ["config", "commit.gpgsign", "false"]):
        subprocess.run(["git", *args], cwd=root, check=True, env=env)
    return root


def git_commit(root: Path, msg: str = "c") -> None:
    env = {**os.environ, "GIT_AUTHOR_NAME": "t", "GIT_AUTHOR_EMAIL": "t@t", "GIT_COMMITTER_NAME": "t",
           "GIT_COMMITTER_EMAIL": "t@t"}
    subprocess.run(["git", "add", "-A"], cwd=root, check=True, env=env)
    subprocess.run(["git", "commit", "-q", "-m", msg], cwd=root, check=True, env=env)


class BuildReleaseTests(unittest.TestCase):
    """build_release.py with a fake runner: preflight (clean tree, pinned
    libraries, fresh HMAC key) then compile / elf2image / pad / gate."""

    def setUp(self):
        self._t = tempfile.TemporaryDirectory()
        tmp = Path(self._t.name)
        self.out = tmp / "out"
        self.out.mkdir()
        self.calls = []
        # three installed libraries + a lock pinning exactly them
        self.libs = {}
        lock = []
        for name, key, ver in (("ArduinoJson", "ARDUINOJSON", "7.4.3"), ("ESP8266Audio", "ESP8266AUDIO", "2.4.1"),
                               ("Adafruit NeoPixel", "NEOPIXEL", "1.15.5")):
            d = tmp / "libs" / name.replace(" ", "_")
            d.mkdir(parents=True)
            (d / "lib.cpp").write_text(f"// {name}\n")
            self.libs[name] = {"name": name, "version": ver, "source_dir": str(d)}
            lock += [f"{key}_TAG={ver}", f"{key}_LIBRARY_VERSION={ver}",
                     f"{key}_SRC_SHA256={release_checks.sources_sha256(d)}"]
        self.lock = tmp / "deps.lock"
        self.lock.write_text("\n".join(lock) + "\n")
        self.sketch = tmp / "sketch"
        self.sketch.mkdir()
        (self.sketch / "config.h").write_text('#define AREG_MANIFEST_HMAC_KEY "TEST-fresh-manifest-key-0123456789"\n')

    def tearDown(self):
        self._t.cleanup()

    def argv(self, *extra):
        return ["--version", "1.4.0", "--out-dir", str(self.out), "--lock", str(self.lock),
                "--sketch", str(self.sketch), *extra]

    def runner(self, mismatch=False, gate_rc=0, dirty="", libs=None):
        def run(argv):
            self.calls.append(argv)
            if argv[:1] == ["git"] and "status" in argv:
                return done(argv, 0, dirty)
            if "lib" in argv and "list" in argv:
                rows = [{"library": v} for v in (libs if libs is not None else self.libs).values()]
                return done(argv, 0, json.dumps({"installed_libraries": rows}))
            if "compile" in argv:
                (self.out / "AregVoiceMvp.ino.elf").write_bytes(b"ELF")
                (self.out / "AregVoiceMvp.ino.bin").write_bytes(b"BIN")
            elif "elf2image" in argv:
                o = Path(argv[argv.index("-o") + 1])
                o.write_bytes(b"PADDED" if "--secure-pad-v2" in argv else (b"OTHER" if mismatch else b"BIN"))
            elif "check_release_image.py" in " ".join(argv):
                return done(argv, gate_rc, "gate output")
            return done(argv)
        return run

    def test_happy_path_argv(self):
        with contextlib.redirect_stdout(io.StringIO()):
            rc = build_release.main(self.argv("--forbid-version", "1.3.4", "--esptool", "esptool"), run=self.runner())
        self.assertEqual(rc, 0)
        self.assertEqual(self.calls[0][:3], ["git", "-C", str(build_release.REPO)])
        self.assertIn("status", self.calls[0])
        self.assertEqual(self.calls[1][-4:], ["lib", "list", "--format", "json"])
        compile_argv = self.calls[2]
        self.assertIn("compiler.cpp.extra_flags=-DAREG_SECURITY_PROFILE_RELEASE -DAREG_CONTENT_SYNC_BENCH", compile_argv)
        self.assertIn(build_release.FQBN, compile_argv)
        e2i = self.calls[3]
        self.assertEqual(e2i[:12], ["esptool", "--chip", "esp32s3", "elf2image", "--flash-mode", "dio",
                                    "--flash-freq", "80m", "--flash-size", "8MB", "--elf-sha256-offset", "0xb0"])
        self.assertNotIn("--secure-pad-v2", e2i)
        self.assertIn("--secure-pad-v2", self.calls[4])
        gate = self.calls[5]
        self.assertEqual(gate[-6:], ["--expect-version", "1.4.0", "--profile", "release", "--forbid-version", "1.3.4"])
        self.assertEqual((self.out / "app-unsigned.bin").read_bytes(), b"PADDED")

    def test_dirty_tree_is_refused_before_compiling(self):
        with contextlib.redirect_stderr(io.StringIO()) as err:
            rc = build_release.main(self.argv(), run=self.runner(dirty=" M esp32/AregVoiceMvp/net_transport.cpp\n"))
        self.assertEqual(rc, 1)
        self.assertIn("dirty tree", err.getvalue())
        self.assertFalse([c for c in self.calls if "compile" in c], "nothing is compiled from a dirty tree")

    def test_unpinned_library_is_refused_before_compiling(self):
        libs = dict(self.libs)
        libs["ESP8266Audio"] = {**libs["ESP8266Audio"]}
        (Path(libs["ESP8266Audio"]["source_dir"]) / "lib.cpp").write_text("// registry 2.4.1, not tag 2.4.2\n")
        libs["ArduinoJson"] = {**libs["ArduinoJson"], "version": "7.4.2"}
        with contextlib.redirect_stderr(io.StringIO()) as err:
            rc = build_release.main(self.argv(), run=self.runner(libs=libs))
        self.assertEqual(rc, 1)
        self.assertIn("ArduinoJson 7.4.2 installed", err.getvalue())
        self.assertIn("ESP8266Audio: sources", err.getvalue())
        self.assertFalse([c for c in self.calls if "compile" in c])
        del libs["Adafruit NeoPixel"]
        with contextlib.redirect_stderr(io.StringIO()) as err:
            rc = build_release.main(self.argv(), run=self.runner(libs=libs))
        self.assertIn("Adafruit NeoPixel is not installed", err.getvalue())

    def test_missing_hmac_key_is_refused(self):
        (self.sketch / "config.h").write_text('#define AREG_MANIFEST_HMAC_KEY ""\n')
        with contextlib.redirect_stderr(io.StringIO()) as err:
            rc = build_release.main(self.argv(), run=self.runner())
        self.assertEqual(rc, 1)
        self.assertIn("HMAC key is missing", err.getvalue())
        self.assertFalse([c for c in self.calls if "compile" in c])

    def test_elf2image_drift_stops_before_padding(self):
        with contextlib.redirect_stderr(io.StringIO()) as err:
            rc = build_release.main(self.argv(), run=self.runner(mismatch=True))
        self.assertEqual(rc, 1)
        self.assertIn("did not reproduce", err.getvalue())
        self.assertFalse([c for c in self.calls if "--secure-pad-v2" in c])

    def test_gate_failure_fails_the_build(self):
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            rc = build_release.main(self.argv(), run=self.runner(gate_rc=1))
        self.assertEqual(rc, 1)


class ReleaseChecksTests(unittest.TestCase):
    """release_checks.py against throwaway git repos."""

    def setUp(self):
        self._t = tempfile.TemporaryDirectory()
        self.tmp = Path(self._t.name)

    def tearDown(self):
        self._t.cleanup()

    def test_clean_tree_sees_modified_and_untracked_files(self):
        r = git_repo(self.tmp / "r")
        (r / "esp32").mkdir()
        (r / "esp32" / "a.cpp").write_text("int a;\n")
        git_commit(r)
        release_checks.require_clean(r, ("esp32",))
        (r / "esp32" / "b.cpp").write_text("int b;\n")       # untracked: it WOULD be compiled
        with self.assertRaises(release_checks.CheckError):
            release_checks.require_clean(r, ("esp32",))
        (r / "esp32" / "b.cpp").unlink()
        (r / "esp32" / "a.cpp").write_text("int a2;\n")      # modified
        with self.assertRaises(release_checks.CheckError) as cm:
            release_checks.require_clean(r, ("esp32",))
        self.assertIn("dirty tree", str(cm.exception))
        (r / "docs.md").write_text("x")                      # outside the release paths: irrelevant
        self.assertEqual([ln for ln in release_checks.dirty_entries(r, ("other",))], [])

    def test_hmac_key_found_in_a_committed_image_is_refused(self):
        r = git_repo(self.tmp / "r")
        key = "TEST-old-manifest-key-0123456789"
        (r / "fw").mkdir()
        (r / "fw" / "areg-current.bin").write_bytes(b"\xe9\x00junk" + key.encode() + b"\x00more")
        git_commit(r)
        (r / "fw" / "areg-current.bin").write_bytes(b"\xe9\x00new image without it")
        git_commit(r, "replace")                              # the key stays in HISTORY
        with self.assertRaises(release_checks.CheckError) as cm:
            release_checks.require_fresh_hmac_key(r, key)
        self.assertIn("fw/areg-current.bin", str(cm.exception))
        self.assertNotIn(key, str(cm.exception), "the key itself is never printed")
        self.assertEqual(release_checks.require_fresh_hmac_key(r, "TEST-brand-new-key-abcdefghijkl"), 2)
        with self.assertRaises(release_checks.CheckError):
            release_checks.require_fresh_hmac_key(r, "short")

    def test_hmac_key_read_from_config_h(self):
        c = self.tmp / "config.h"
        c.write_text('#ifndef AREG_MANIFEST_HMAC_KEY\n  #define AREG_MANIFEST_HMAC_KEY "abc"\n#endif\n')
        self.assertEqual(release_checks.hmac_key_from_config(c), "abc")

    def test_idf_deps_allow_only_the_repo_wrapper(self):
        deps = self.tmp / "deps"
        project = self.tmp / "proj"
        heads = {}
        for name, key, wrapped in release_checks.GIT_DEPS:
            d = git_repo(deps / "components" / name)
            (d / "src.c").write_text(f"/* {name} */\n")
            git_commit(d)
            heads[key] = subprocess.run(["git", "rev-parse", "HEAD"], cwd=d, capture_output=True,
                                        text=True).stdout.strip()
            if wrapped:
                w = project / "component-wrappers" / name / "CMakeLists.txt"
                w.parent.mkdir(parents=True)
                w.write_text(f"# wrapper {name}\n")
                shutil.copyfile(w, d / "CMakeLists.txt")
        npd = deps / "components" / release_checks.NETPROV_DIR
        npd.mkdir(parents=True)
        (npd / "x.c").write_text("x")
        (npd / release_checks.NETPROV_MARKER).write_text("commit\n")
        sdk = deps / "arduino-libs" / "sdkconfig"
        sdk.parent.mkdir(parents=True)
        sdk.write_text("CONFIG_X=y\n")
        pins = {**heads, "NETWORK_PROVISIONING_TREE_SHA256": release_checks.tree_sha256(npd),
                "ARDUINO_LIBS_SDKCONFIG_SHA256": hashlib.sha256(sdk.read_bytes()).hexdigest()}
        self.assertEqual(release_checks.idf_deps_problems(deps, pins, project), [])
        # a hand edit at the right commit, an extra file, a foreign wrapper, a netprov edit, a sdkconfig edit
        (deps / "components" / "arduino" / "src.c").write_text("/* edited */\n")
        (deps / "components" / "ArduinoJson" / "extra.h").write_text("#define X\n")
        (deps / "components" / "ESP8266Audio" / "CMakeLists.txt").write_text("# not the wrapper\n")
        (npd / "x.c").write_text("y")
        sdk.write_text("CONFIG_X=n\n")
        bad = "\n".join(release_checks.idf_deps_problems(deps, pins, project))
        for needle in ("arduino: 'M src.c'", "ArduinoJson: '?? extra.h'", "ESP8266Audio/CMakeLists.txt is not",
                       "tree sha256", "arduino-libs/sdkconfig sha256"):
            self.assertIn(needle, bad)

    def test_sources_hash_ignores_examples_and_non_sources(self):
        d = self.tmp / "lib"
        (d / "src").mkdir(parents=True)
        (d / "src" / "a.cpp").write_text("a")
        h = release_checks.sources_sha256(d)
        (d / "examples").mkdir()
        (d / "examples" / "e.cpp").write_text("e")
        (d / "library.properties").write_text("version=1")
        self.assertEqual(release_checks.sources_sha256(d), h)
        (d / "src" / "a.cpp").write_text("b")
        self.assertNotEqual(release_checks.sources_sha256(d), h)

    def test_deps_lock_pins_the_new_provenance_keys(self):
        pins = release_checks.read_lock()
        for k in ("TOOLCHAIN", "IDF_ESPTOOL", "NETWORK_PROVISIONING_TREE_SHA256", "ARDUINOJSON_SRC_SHA256",
                  "ESP8266AUDIO_SRC_SHA256", "NEOPIXEL_SRC_SHA256", "ARDUINOJSON_LIBRARY_VERSION",
                  "ESP8266AUDIO_LIBRARY_VERSION", "NEOPIXEL_LIBRARY_VERSION"):
            self.assertTrue(pins.get(k), k)
        self.assertEqual(pins["TOOLCHAIN"], "esp-14.2.0_20260121")
        self.assertEqual(pins["IDF_ESPTOOL"], "4.12.0")


class BuildIdfSigningPolicyTests(unittest.TestCase):
    """build_idf.sh's release signing policy runs before any toolchain is
    needed, so it is tested by running the real script."""

    def setUp(self):
        self._t = tempfile.TemporaryDirectory()
        self.tmp = Path(self._t.name)
        self.key = self.tmp / "sb_primary.pem"
        self.key.write_text("not a key\n")
        self.real = self.tmp / "real_digests.txt"
        self.real.write_text("aa" * 32 + "\n" + "bb" * 32 + "\n")
        self.test = self.tmp / "test_digests.txt"
        self.test.write_text("# TEST ONLY\n" + "aa" * 32 + "\n" + "bb" * 32 + "\n")

    def tearDown(self):
        self._t.cleanup()

    def run_script(self, **env):
        e = {k: v for k, v in os.environ.items() if not k.startswith(("AREG_", "ESPSECURE", "IDF_PATH"))}
        e.update(env)
        return subprocess.run(["bash", str(HERE / "build_idf.sh"), "release", "--version", "1.4.0"],
                              capture_output=True, text=True, env=e)

    def test_real_digests_plus_a_signing_key_is_refused(self):
        r = self.run_script(AREG_SB_SIGNING_KEY=str(self.key), AREG_SB_TRUSTED_DIGESTS=str(self.real))
        self.assertEqual(r.returncode, 1)
        self.assertIn("never sits on the build machine", r.stderr)
        self.assertIn("sign_release.py app", r.stderr)

    def test_test_signing_needs_esptool_5_2_espsecure(self):
        fake = self.tmp / "espsecure.py"
        fake.write_text("#!/bin/sh\necho 'usage: espsecure [-h] {sign_data,verify_signature}'\n")
        fake.chmod(0o755)
        r = self.run_script(AREG_SB_SIGNING_KEY=str(self.key), AREG_SB_TRUSTED_DIGESTS=str(self.test),
                            ESPSECURE=str(fake))
        self.assertEqual(r.returncode, 1)
        self.assertIn("not esptool 5.2.0's espsecure", r.stderr)

    def test_default_is_unsigned_so_a_real_build_never_asks_for_the_key(self):
        # No key in the environment: the signing policy passes and the script
        # moves on (here it then stops at the next prerequisite -- a clean
        # tree or the ESP-IDF environment -- never at the signing key).
        r = self.run_script(AREG_SB_TRUSTED_DIGESTS=str(self.real))
        self.assertNotIn("AREG_SB_SIGNING_KEY", r.stderr)
        self.assertNotIn("never sits on the build machine", r.stderr)


class BuildScriptHardeningTests(unittest.TestCase):
    """Review round 3: build_idf.sh / build_release_bootloader.sh need ESP-IDF
    to run, so the rules are pinned on the script text."""

    @classmethod
    def setUpClass(cls):
        cls.idf = (HERE / "build_idf.sh").read_text()
        cls.bl = (HERE / "build_release_bootloader.sh").read_text()

    def test_component_manager_is_off_before_any_idf_py(self):
        for name, text in (("build_idf.sh", self.idf), ("build_release_bootloader.sh", self.bl)):
            self.assertIn("\nexport IDF_COMPONENT_MANAGER=0\n", text, name)
            first_idf_py = min(i for i in (text.find("idf.py -B"), text.find("idf.py -B build")) if i >= 0)
            self.assertLess(text.index("export IDF_COMPONENT_MANAGER=0"), first_idf_py, name)
            self.assertIn('"$PROJ/dependencies.lock"', text, name)
            self.assertIn('"$PROJ/managed_components"', text, name)
        gi = (REPO / "esp32" / "AregVoiceIdf" / ".gitignore").read_text()
        self.assertNotIn("dependencies.lock", gi, "never gitignored: the clean-tree check must see it")
        self.assertNotIn("managed_components", gi)

    def test_idf_checkout_must_be_clean_including_submodules(self):
        for name, text in (("build_idf.sh", self.idf), ("build_release_bootloader.sh", self.bl)):
            self.assertIn("status --porcelain --ignore-submodules=none", text, name)
            self.assertIn("submodule status --recursive", text, name)
            self.assertIn("grep -q '^[-+U]' <<<", text, name)  # no SIGPIPE-vs-pipefail trap

    def test_anti_rollback_follows_the_bootloader_defaults(self):
        self.assertNotIn("anti-rollback must stay OFF", self.bl)
        self.assertIn("grep -q '^CONFIG_BOOTLOADER_APP_ANTI_ROLLBACK=y' sdkconfig.defaults", self.bl)
        self.assertIn('die "anti-rollback on without sdkconfig.defaults saying so"', self.bl)
        self.assertIn("CONFIG_BOOTLOADER_APP_SECURE_VERSION", self.idf)
        self.assertIn("esp32/bootloader-release/sdkconfig.defaults", self.idf)
        defaults = (REPO / "esp32" / "bootloader-release" / "sdkconfig.defaults").read_text()
        self.assertIn("# CONFIG_BOOTLOADER_APP_ANTI_ROLLBACK is not set", defaults,
                      "anti-rollback stays an OWNER decision (docs/firmware-security.md s11 rule 8)")
        self.assertFalse(build_release.anti_rollback_enforced())

    def test_arduino_release_is_refused_once_anti_rollback_is_on(self):
        with tempfile.TemporaryDirectory() as d:
            repo = Path(d)
            cfg = repo / "esp32" / "bootloader-release" / "sdkconfig.defaults"
            cfg.parent.mkdir(parents=True)
            cfg.write_text("CONFIG_BOOTLOADER_APP_ANTI_ROLLBACK=y\n")
            self.assertTrue(build_release.anti_rollback_enforced(repo))
            with self.assertRaises(build_release.BuildError) as cm:
                build_release.preflight(["arduino-cli"], repo, repo / "deps.lock",
                                        run=lambda argv: done(argv), repo=repo)
            self.assertIn("build_idf.sh release", str(cm.exception))


class FakeSigner(sign_release.Signer):
    """digest-sbv2-public-key returns the digest mapped to the key path;
    sign-data copies the input and appends a marker; the gate is recorded."""

    def __init__(self, digests: dict, gate_rc: int = 0, banner: str = "espsecure v5.2.0 - ESP32 Secure Boot"):
        self.calls = []
        self.digests = digests
        self.gate_rc = gate_rc
        self.banner = banner
        super().__init__(runner=self._run, espsecure="espsecure")

    def _run(self, argv):
        self.calls.append(argv)
        if "--help" in argv:
            return done(argv, 0, self.banner)
        if "digest-sbv2-public-key" in argv:
            Path(argv[argv.index("--output") + 1]).write_bytes(bytes.fromhex(self.digests[argv[argv.index("--keyfile") + 1]]))
        elif "sign-data" in argv:
            src = Path(argv[-1])
            Path(argv[argv.index("--output") + 1]).write_bytes(src.read_bytes() + b"SIG")
        elif "check_release_image.py" in " ".join(argv):
            return done(argv, self.gate_rc, "gate")
        return done(argv)


class SignReleaseTests(unittest.TestCase):
    def setUp(self):
        self._t = tempfile.TemporaryDirectory()
        self.tmp = Path(self._t.name)
        self.prim, self.back, self.rogue = (self.tmp / n for n in ("p.pem", "b.pem", "r.pem"))
        for k in (self.prim, self.back, self.rogue):
            k.write_text("key")
        self.app = self.tmp / "app-unsigned.bin"
        self.app.write_bytes(b"A" * 0x10000)
        self.bl = self.tmp / "bootloader-unsigned.bin"
        self.bl.write_bytes(b"B" * 0x6000)
        self.trusted = self.tmp / "trusted.txt"
        self.trusted.write_text("\n".join(DIGESTS) + "\n")   # same digests, no TEST header
        self.out = self.tmp / "bundle"
        self.dig = {str(self.prim): DIGESTS[0], str(self.back): DIGESTS[1], str(self.rogue): "ab" * 32}
        # Review round 3: the bootloader must be a RELEASED.md build. TEST
        # keys accept any recorded row (CANDIDATE); real keys need APPROVED.
        self.table = self.tmp / "partition-table.bin"
        self.table.write_bytes(b"P" * 3072)
        self.released = self.released_md("CANDIDATE")

    def released_md(self, status: str, bl_sha: str | None = None, pt_sha: str | None = None) -> Path:
        p = self.tmp / f"RELEASED-{status}.md"
        p.write_text(
            "| Date | Built by | IDF commit | Toolchain | IDF esptool / repo | Size | sha256 (bootloader-unsigned.bin) "
            "| sha256 (partition-table.bin) | Status |\n|---|---|---|---|---|---|---|---|---|\n"
            f"| d | t | x | x | x | 0x6000 | `{bl_sha or hashlib.sha256(self.bl.read_bytes()).hexdigest()}` "
            f"| `{pt_sha or hashlib.sha256(self.table.read_bytes()).hexdigest()}` | {status} |\n")
        return p

    def bl_argv(self, *extra, released=None):
        return ["bootloader", "--keyfile", str(self.prim), "--backup-keyfile", str(self.back), "--input", str(self.bl),
                "--out-dir", str(self.out), "--trusted-digests", str(self.trusted),
                "--released-md", str(released or self.released), *extra]

    def tearDown(self):
        self._t.cleanup()

    def main(self, argv, signer):
        with contextlib.redirect_stdout(io.StringIO()) as out, contextlib.redirect_stderr(io.StringIO()) as err:
            rc = sign_release.main(argv, signer=signer)
        return rc, out.getvalue() + err.getvalue()

    def app_argv(self, key, *extra):
        return ["app", "--keyfile", str(key), "--input", str(self.app), "--expect-version", "1.4.0",
                "--out-dir", str(self.out), "--trusted-digests", str(self.trusted), *extra]

    def test_refuses_a_test_key_without_the_flag(self):
        s = FakeSigner(self.dig)
        rc, out = self.main(self.app_argv(self.prim), s)
        self.assertEqual(rc, 1)
        self.assertIn("TEST key", out)
        self.assertFalse([c for c in s.calls if "sign-data" in c])

    def test_refuses_a_test_only_digests_file(self):
        s = FakeSigner(self.dig)
        argv = self.app_argv(self.prim)
        argv[argv.index(str(self.trusted))] = str(TESTDATA / "TEST_sb_trusted_digests.txt")
        rc, out = self.main(argv, s)
        self.assertEqual(rc, 1)
        self.assertIn("TEST ONLY", out)

    def test_refuses_an_untrusted_key(self):
        s = FakeSigner(self.dig)
        rc, out = self.main(self.app_argv(self.rogue, "--allow-test-key"), s)
        self.assertEqual(rc, 1)
        self.assertIn("not in the trusted digests", out)

    def test_refuses_when_the_unsigned_gate_fails(self):
        s = FakeSigner(self.dig, gate_rc=1)
        rc, out = self.main(self.app_argv(self.prim, "--allow-test-key"), s)
        self.assertEqual(rc, 1)
        self.assertFalse([c for c in s.calls if "sign-data" in c], "never sign an image the gate refused")

    def test_signs_app_then_gates_signed_and_writes_bundle(self):
        s = FakeSigner(self.dig)
        rc, out = self.main(self.app_argv(self.prim, "--allow-test-key"), s)
        self.assertEqual(rc, 0, out)
        gates = [c for c in s.calls if "check_release_image.py" in " ".join(c)]
        self.assertEqual(len(gates), 2)
        self.assertIn("--profile", gates[0])
        self.assertNotIn("--require-sbv2", gates[0])
        self.assertIn("--require-sbv2", gates[1])
        signed = self.out / "app-signed-TEST.bin"
        self.assertTrue(signed.is_file())
        bundle = json.loads((self.out / "bundle.json").read_text())
        self.assertTrue(bundle["test_signed"])
        self.assertEqual(bundle["artifacts"]["app"]["file"], "app-signed-TEST.bin")
        self.assertEqual(bundle["idf_commit"], sign_release.IDF_COMMIT)

    def test_bootloader_needs_both_distinct_keys_and_appends(self):
        s = FakeSigner(self.dig)
        base = ["bootloader", "--keyfile", str(self.prim), "--input", str(self.bl), "--out-dir", str(self.out),
                "--trusted-digests", str(self.trusted), "--allow-test-key", "--released-md", str(self.released)]
        rc, out = self.main(base, s)
        self.assertEqual(rc, 1)
        self.assertIn("BOTH keys", out)
        rc, out = self.main(base + ["--backup-keyfile", str(self.prim)], s)
        self.assertEqual(rc, 1)
        self.assertIn("same key", out)
        s = FakeSigner(self.dig)
        rc, out = self.main(base + ["--backup-keyfile", str(self.back)], s)
        self.assertEqual(rc, 0, out)
        signs = [c for c in s.calls if "sign-data" in c]
        self.assertEqual(len(signs), 2)
        self.assertNotIn("--append-signatures", signs[0])
        self.assertIn("--append-signatures", signs[1])
        self.assertIn(str(self.back), signs[1])
        gate = [c for c in s.calls if "check_release_image.py" in " ".join(c)][-1]
        self.assertIn("--bootloader", gate)

    def test_refuses_an_espsecure_that_is_not_5_2_0(self):
        s = FakeSigner(self.dig, banner="usage: espsecure [-h] {sign_data,verify_signature}")
        rc, out = self.main(self.app_argv(self.prim, "--allow-test-key"), s)
        self.assertEqual(rc, 1)
        self.assertIn("not esptool 5.2.0's espsecure", out)
        self.assertFalse([c for c in s.calls if "sign-data" in c])

    def test_out_dir_inside_the_repo_must_be_gitignored(self):
        s = FakeSigner(self.dig)
        argv = self.app_argv(self.prim, "--allow-test-key")
        argv[argv.index("--out-dir") + 1] = str(REPO / "docs" / "bundle-TEST")
        rc, out = self.main(argv, s)
        self.assertEqual(rc, 1)
        self.assertIn("never be committed", out)
        self.assertFalse((REPO / "docs" / "bundle-TEST").exists())

    def test_pilot_evidence_is_checked_then_recorded(self):
        s = FakeSigner(self.dig)
        base = self.bl_argv("--allow-test-key")
        self.assertEqual(self.main(base, s)[0], 0)
        bl_sha = json.loads((self.out / "bundle.json").read_text())["artifacts"]["bootloader"]["sha256"]
        fake_repo = self.tmp / "repo"
        ev = fake_repo / "tools" / "quality-evidence" / "chip-security-pilot-TEST.md"
        ev.parent.mkdir(parents=True)
        add = ["add", "--out-dir", str(self.out), "--pilot-evidence", "tools/quality-evidence/chip-security-pilot-TEST.md"]
        with mock.patch.object(sign_release, "REPO", fake_repo):
            ev.write_text(f"bootloader {bl_sha}\nOTA release+1 on locked board: FAIL\n")
            rc, out = self.main(add, s)
            self.assertEqual(rc, 1)
            self.assertIn("OTA release+1 on locked board: PASS", out)
            ev.write_text(f"bootloader {'ab' * 32}\nOTA release+1 on locked board: PASS\n")
            rc, out = self.main(add, s)
            self.assertEqual(rc, 1)
            self.assertIn("different bootloader", out)
            ev.write_text(f"bootloader {bl_sha}\nOTA release+1 on locked board: PASS\n")
            rc, out = self.main(add, s)
            self.assertEqual(rc, 0, out)
        pilot = json.loads((self.out / "bundle.json").read_text())["pilot"]
        self.assertEqual(pilot, {"evidence": "tools/quality-evidence/chip-security-pilot-TEST.md",
                                 "bootloader_sha256": bl_sha})
        self.assertTrue(sign_release.pilot_evidence_problems("../x.md", bl_sha))
        self.assertTrue(sign_release.pilot_evidence_problems("docs/x.md", bl_sha))

    def test_oversized_bootloader_is_refused(self):
        self.bl.write_bytes(b"B" * 0x7001)
        s = FakeSigner(self.dig)
        rc, out = self.main(self.bl_argv("--allow-test-key"), s)
        self.assertEqual(rc, 1)
        self.assertIn("0x7000", out)

    # ---- review round 3: WHICH bootloader / table / boot_app0 get frozen -----
    def real_keys(self):
        """A trusted pair that is NOT the committed TEST pair (no --allow-test-key)."""
        real = ["c1" * 32, "c2" * 32]
        self.trusted.write_text("\n".join(real) + "\n")
        return {str(self.prim): real[0], str(self.back): real[1]}

    def test_unrecorded_bootloader_is_refused_before_any_key_is_used(self):
        s = FakeSigner(self.dig)
        rc, out = self.main(self.bl_argv("--allow-test-key", released=self.released_md("APPROVED", bl_sha="ab" * 32)), s)
        self.assertEqual(rc, 1)
        self.assertIn("not a build recorded", out)
        self.assertFalse([c for c in s.calls if "sign-data" in c])

    def test_real_keys_need_an_approved_row_or_pilot_for_a_pilot_row(self):
        for status, extra, ok in (("CANDIDATE", (), False), ("CANDIDATE", ("--pilot",), False),
                                  ("PILOT", (), False), ("PILOT", ("--pilot",), True),
                                  ("APPROVED", (), True), ("SUPERSEDED", ("--pilot",), False)):
            shutil.rmtree(self.out, ignore_errors=True)
            s = FakeSigner(self.real_keys())
            rc, out = self.main(self.bl_argv(*extra, released=self.released_md(status)), s)
            self.assertEqual(rc == 0, ok, f"{status} {extra}:\n{out}")
            if ok:
                bl = json.loads((self.out / "bundle.json").read_text())["artifacts"]["bootloader"]
                self.assertEqual(bl["released_status"], status)
                gate = [c for c in s.calls if "check_release_image.py" in " ".join(c)][-1]
                self.assertEqual(gate[gate.index("--min-status") + 1], "PILOT" if extra else "APPROVED")
            else:
                self.assertFalse([c for c in s.calls if "sign-data" in c], f"{status} {extra}")

    def test_add_pins_the_partition_table_to_the_bootloader_row_and_boot_app0(self):
        s = FakeSigner(self.dig)
        boot = self.tmp / "boot_app0.bin"
        boot.write_bytes(b"O" * 8192)
        add = ["add", "--out-dir", str(self.out), "--released-md", str(self.released),
               "--partition-table", str(self.table)]
        rc, out = self.main(add, s)
        self.assertEqual(rc, 1)
        self.assertIn("sign the bootloader into this bundle first", out)
        self.assertEqual(self.main(self.bl_argv("--allow-test-key"), s)[0], 0)
        rc, out = self.main(add, s)
        self.assertEqual(rc, 0, out)
        other = self.tmp / "other-table.bin"
        other.write_bytes(b"Q" * 3072)
        rc, out = self.main(["add", "--out-dir", str(self.out), "--released-md", str(self.released),
                             "--partition-table", str(other)], s)
        self.assertEqual(rc, 1)
        self.assertIn("frozen into every toy", out)
        rc, out = self.main(["add", "--out-dir", str(self.out), "--boot-app0", str(boot)], s)
        self.assertEqual(rc, 1)
        self.assertIn("arduino-esp32 3.3.8", out)
        with mock.patch.object(sign_release.gate_lib, "BOOT_APP0_SHA256", hashlib.sha256(b"O" * 8192).hexdigest()):
            rc, out = self.main(["add", "--out-dir", str(self.out), "--boot-app0", str(boot)], s)
        self.assertEqual(rc, 0, out)

    def test_bootloader_must_be_a_whole_number_of_sectors(self):
        self.bl.write_bytes(b"B" * 0x5800)
        s = FakeSigner(self.dig)
        rc, out = self.main(self.bl_argv("--allow-test-key"), s)
        self.assertEqual(rc, 1)
        self.assertIn("4 KB multiple", out)


if __name__ == "__main__":
    unittest.main()

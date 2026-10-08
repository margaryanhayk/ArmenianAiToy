#!/usr/bin/env python3
"""Fetch the pinned dependencies of the ESP-IDF firmware build.

Reads esp32/AregVoiceIdf/deps.lock and fills $AREG_IDF_DEPS (default
~/.cache/areg-idf-deps, ALWAYS outside the repo) with:

  components/arduino                         arduino-esp32 @ ARDUINO_ESP32_COMMIT
  components/espressif__network_provisioning idf-extra-components @ IEC_COMMIT
                                             (tree sha256 == NETWORK_PROVISIONING_TREE_SHA256)
  components/ArduinoJson                     @ ARDUINOJSON_COMMIT
  components/ESP8266Audio                    @ ESP8266AUDIO_COMMIT (+ IDF wrapper)
  components/Adafruit_NeoPixel               @ NEOPIXEL_COMMIT (+ IDF wrapper)
  arduino-libs/sdkconfig                     sha256 == ARDUINO_LIBS_SDKCONFIG_SHA256

Every clone is checked against its pinned COMMIT, the sdkconfig against its
sha256; anything else is refused (exit 1). Idempotent: a dependency already
at its pinned commit is left alone. Stdlib only (plus the `git` binary).

    python3 tools/firmware/idf_fetch_deps.py [--deps DIR] [--mirror-dir DIR]
                                             [--libs-sdkconfig FILE]

--mirror-dir DIR    clone from DIR/<name> (a local git clone) instead of the
                    network; names: arduino-esp32, idf-extra-components,
                    ArduinoJson, ESP8266Audio, Adafruit_NeoPixel. The commit
                    check still applies.
--libs-sdkconfig F  use a local copy of the Arduino libs' esp32s3/sdkconfig
                    (still sha256-checked) instead of the HTTP range fetch.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import os
import shutil
import subprocess
import sys
import tarfile
import urllib.request
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from release_checks import tree_sha256  # noqa: E402  (network_provisioning content pin)

REPO = Path(__file__).resolve().parents[2]
PROJECT = REPO / "esp32" / "AregVoiceIdf"
LOCK = PROJECT / "deps.lock"


class FetchError(RuntimeError):
    pass


def read_lock(path: Path = LOCK) -> dict[str, str]:
    pins = {}
    for line in path.read_text().splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        k, _, v = line.partition("=")
        pins[k.strip()] = v.strip()
    return pins


def default_deps_dir() -> Path:
    env = os.environ.get("AREG_IDF_DEPS")
    if env:
        return Path(env)
    base = Path(os.environ.get("XDG_CACHE_HOME", Path.home() / ".cache"))
    return base / "areg-idf-deps"


def git(*args: str, cwd: Path | None = None) -> str:
    r = subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True)
    if r.returncode != 0:
        raise FetchError(f"git {' '.join(args)} failed: {r.stderr.strip()}")
    return r.stdout.strip()


def head_of(path: Path) -> str | None:
    if not (path / ".git").exists():
        return None
    try:
        return git("rev-parse", "HEAD", cwd=path)
    except FetchError:
        return None


def clone_pinned(name: str, url: str, tag: str, commit: str, dest: Path, mirror: Path | None) -> None:
    if head_of(dest) == commit:
        print(f"  ok    {name} @ {commit[:10]} (already present)")
        return
    if dest.exists():
        shutil.rmtree(dest)
    src = str(mirror / name) if mirror and (mirror / name).exists() else url
    git("-c", "advice.detachedHead=false", "clone", "-q", "--depth", "1", "--branch", tag, src, str(dest))
    got = head_of(dest)
    if got != commit:
        shutil.rmtree(dest, ignore_errors=True)
        raise FetchError(f"{name}: tag {tag} resolved to {got}, pinned {commit} -- refusing (tag moved?)")
    print(f"  ok    {name} @ {commit[:10]} ({tag})")


def fetch_network_provisioning(pins: dict, dest: Path, work: Path, mirror: Path | None) -> None:
    marker = dest / ".areg-pinned-commit"
    want_tree = pins["NETWORK_PROVISIONING_TREE_SHA256"]
    if (marker.is_file() and marker.read_text().strip() == pins["IEC_COMMIT"]
            and tree_sha256(dest) == want_tree):
        print(f"  ok    network_provisioning @ {pins['IEC_COMMIT'][:10]} (already present, tree hash ok)")
        return
    iec = work / "idf-extra-components"
    src = str(mirror / "idf-extra-components") if mirror and (mirror / "idf-extra-components").exists() else pins["IEC_URL"]
    if not (iec / ".git").exists():
        git("clone", "-q", "--filter=blob:none", "--no-checkout", src, str(iec))
    try:
        git("cat-file", "-e", pins["IEC_COMMIT"] + "^{commit}", cwd=iec)
    except FetchError:
        git("fetch", "-q", "origin", pins["IEC_COMMIT"], cwd=iec)
    blob = subprocess.run(["git", "archive", pins["IEC_COMMIT"], "network_provisioning"],
                          cwd=iec, capture_output=True)
    if blob.returncode != 0:
        raise FetchError(f"git archive network_provisioning failed: {blob.stderr.decode(errors='replace')}")
    if dest.exists():
        shutil.rmtree(dest)
    dest.mkdir(parents=True)
    with tarfile.open(fileobj=io.BytesIO(blob.stdout)) as tf:
        for m in tf.getmembers():
            parts = Path(m.name).parts
            if len(parts) < 2 or ".." in parts or m.issym() or m.islnk():
                continue
            m.name = str(Path(*parts[1:]))
            tf.extract(m, dest)
    got_tree = tree_sha256(dest)
    if got_tree != want_tree:
        shutil.rmtree(dest, ignore_errors=True)
        raise FetchError(f"network_provisioning tree sha256 {got_tree} != pinned {want_tree} -- refusing")
    marker.write_text(pins["IEC_COMMIT"] + "\n")
    print(f"  ok    network_provisioning @ {pins['IEC_COMMIT'][:10]} (tree hash ok)")


class _RangeFile(io.RawIOBase):
    """Read a remote file with HTTP Range requests (the libs zip is ~520 MB;
    we need one 128 KB member)."""

    def __init__(self, url: str):
        self.url, self.pos = url, 0
        with urllib.request.urlopen(urllib.request.Request(url, method="HEAD"), timeout=60) as r:
            self.size = int(r.headers["Content-Length"])

    def seekable(self):
        return True

    def readable(self):
        return True

    def tell(self):
        return self.pos

    def seek(self, off, whence=0):
        self.pos = {0: off, 1: self.pos + off, 2: self.size + off}[whence]
        return self.pos

    def readinto(self, b):
        n = len(b)
        if n == 0 or self.pos >= self.size:
            return 0
        end = min(self.pos + n, self.size) - 1
        req = urllib.request.Request(self.url, headers={"Range": f"bytes={self.pos}-{end}"})
        with urllib.request.urlopen(req, timeout=120) as r:
            data = r.read()
        b[:len(data)] = data
        self.pos += len(data)
        return len(data)


def fetch_libs_sdkconfig(pins: dict, dest: Path, local: Path | None) -> None:
    want = pins["ARDUINO_LIBS_SDKCONFIG_SHA256"]
    if dest.is_file() and hashlib.sha256(dest.read_bytes()).hexdigest() == want:
        print(f"  ok    arduino-libs sdkconfig sha256 {want[:12]} (already present)")
        return
    if local is not None:
        data = local.read_bytes()
    else:
        zf = zipfile.ZipFile(io.BufferedReader(_RangeFile(pins["ARDUINO_LIBS_ZIP_URL"]), buffer_size=1 << 20))
        data = zf.read(pins["ARDUINO_LIBS_ZIP_MEMBER"])
    got = hashlib.sha256(data).hexdigest()
    if got != want:
        raise FetchError(f"arduino-libs sdkconfig sha256 {got} != pinned {want}")
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_bytes(data)
    print(f"  ok    arduino-libs sdkconfig sha256 {want[:12]}")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--deps", type=Path, default=None)
    ap.add_argument("--mirror-dir", type=Path, default=None)
    ap.add_argument("--libs-sdkconfig", type=Path, default=None)
    a = ap.parse_args(argv)
    deps = (a.deps or default_deps_dir()).resolve()
    try:
        if deps == REPO or REPO in deps.parents:
            raise FetchError(f"{deps} is inside the repo -- dependencies never live in git")
        pins = read_lock()
        comps = deps / "components"
        comps.mkdir(parents=True, exist_ok=True)
        print(f"deps -> {deps}")
        clone_pinned("arduino-esp32", pins["ARDUINO_ESP32_URL"], pins["ARDUINO_ESP32_TAG"],
                     pins["ARDUINO_ESP32_COMMIT"], comps / "arduino", a.mirror_dir)
        fetch_network_provisioning(pins, comps / "espressif__network_provisioning", deps / "work", a.mirror_dir)
        clone_pinned("ArduinoJson", pins["ARDUINOJSON_URL"], pins["ARDUINOJSON_TAG"],
                     pins["ARDUINOJSON_COMMIT"], comps / "ArduinoJson", a.mirror_dir)
        for name, key in (("ESP8266Audio", "ESP8266AUDIO"), ("Adafruit_NeoPixel", "NEOPIXEL")):
            clone_pinned(name, pins[f"{key}_URL"], pins[f"{key}_TAG"], pins[f"{key}_COMMIT"],
                         comps / name, a.mirror_dir)
            shutil.copyfile(PROJECT / "component-wrappers" / name / "CMakeLists.txt",
                            comps / name / "CMakeLists.txt")
        fetch_libs_sdkconfig(pins, deps / "arduino-libs" / "sdkconfig", a.libs_sdkconfig)
        print(f"export AREG_IDF_DEPS={deps}")
        return 0
    except (FetchError, KeyError, OSError) as e:
        print(f"FAIL - {e}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())

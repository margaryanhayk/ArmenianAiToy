#!/usr/bin/env python3
"""Release-provenance checks shared by the firmware build scripts.

Stdlib + the `git` binary only. Every subcommand exits 0 (ok) or 1 (FAIL with
the reason); none prints a secret.

    python3 tools/firmware/release_checks.py clean-tree PATH...
        Refuse uncommitted OR untracked changes under the given repo paths. A
        release is stamped with HEAD's commit (AREG_FW_BUILD, release.json
        source_commit); built from a dirty tree that stamp would be a lie and
        "rebuild from the tag and compare" would fail without warning.

    python3 tools/firmware/release_checks.py idf-deps --deps DIR
        The fetched ESP-IDF build dependencies are EXACTLY their pins in
        esp32/AregVoiceIdf/deps.lock: each git clone at its commit with no
        modified or untracked file (the only allowed difference is the IDF
        wrapper CMakeLists.txt, which must equal
        esp32/AregVoiceIdf/component-wrappers/<name>/CMakeLists.txt), the
        network_provisioning tree hash, the Arduino libs' sdkconfig sha256.
        Commit equality alone would pass a hand-edited file at the right commit.

    python3 tools/firmware/release_checks.py hmac-fresh [--config-h FILE]
        The manifest HMAC key the release is built with (env
        AREG_MANIFEST_HMAC_KEY, or the #define in FILE) appears in NO firmware
        image ever committed to this repository. The repository was public:
        every committed image -- and the key inside it -- must be treated as
        published, so the first secured release needs a NEW key.

    python3 tools/firmware/release_checks.py arduino-libs [--arduino-cli CMD]
        The libraries arduino-cli would compile (`arduino-cli lib list
        --format json`) are the pinned ones: library.properties version AND a
        hash of their compiled sources equal deps.lock (ESP8266Audio's tag
        2.4.2 still says "2.4.1" in library.properties, so the version string
        alone cannot tell it from the registry's 2.4.1 -- the source hash can).

    python3 tools/firmware/release_checks.py tree-sha256 DIR [--sources]
        Print a directory's content hash (to record a new pin in deps.lock).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shlex
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
PROJECT = REPO / "esp32" / "AregVoiceIdf"
LOCK = PROJECT / "deps.lock"

# Release-relevant repo paths: everything that decides the bytes of an image.
RELEASE_PATHS = ("esp32/AregVoiceMvp", "esp32/AregVoiceIdf", "esp32/bootloader-release", "tools/firmware")

# (component dir under $AREG_IDF_DEPS/components, deps.lock commit key, has an IDF wrapper)
GIT_DEPS = (
    ("arduino", "ARDUINO_ESP32_COMMIT", False),
    ("ArduinoJson", "ARDUINOJSON_COMMIT", False),
    ("ESP8266Audio", "ESP8266AUDIO_COMMIT", True),
    ("Adafruit_NeoPixel", "NEOPIXEL_COMMIT", True),
)
NETPROV_DIR = "espressif__network_provisioning"
NETPROV_MARKER = ".areg-pinned-commit"

# (arduino-cli library name, deps.lock key prefix)
ARDUINO_LIBS = (("ArduinoJson", "ARDUINOJSON"), ("ESP8266Audio", "ESP8266AUDIO"), ("Adafruit NeoPixel", "NEOPIXEL"))
SOURCE_EXT = {".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp", ".hxx", ".inc", ".ipp", ".tpp", ".s", ".S"}
SOURCE_SKIP_TOP = {"examples", "extras", "test", "tests", "docs", ".git", ".github"}

# Where committed firmware images could ever have lived (any path in history).
IMAGE_SUFFIXES = (".bin", ".elf", ".hex")


class CheckError(RuntimeError):
    pass


def read_lock(path: Path = LOCK) -> dict[str, str]:
    pins = {}
    for line in path.read_text().splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            k, _, v = line.partition("=")
            pins[k.strip()] = v.strip()
    return pins


def _git(args: list[str], cwd: Path) -> subprocess.CompletedProcess:
    return subprocess.run(["git", *args], cwd=cwd, capture_output=True)


# ---- content hashes ----------------------------------------------------------
def tree_sha256(root: Path, exclude: tuple[str, ...] = (".git", NETPROV_MARKER)) -> str:
    """sha256 over (posix relative path, sha256(file bytes)) of every file,
    sorted by path; any path component in `exclude` is skipped."""
    h = hashlib.sha256()
    files = []
    for p in root.rglob("*"):
        rel = p.relative_to(root)
        if any(part in exclude for part in rel.parts) or not p.is_file():
            continue
        files.append((rel.as_posix(), p))
    for rel, p in sorted(files):
        h.update(rel.encode() + b"\0" + hashlib.sha256(p.read_bytes()).digest())
    return h.hexdigest()


def sources_sha256(root: Path) -> str:
    """tree_sha256 restricted to compilable sources (what arduino-cli builds),
    so a registry zip and a git clone of the same release hash equal."""
    h = hashlib.sha256()
    files = []
    for p in root.rglob("*"):
        rel = p.relative_to(root)
        if rel.parts and rel.parts[0] in SOURCE_SKIP_TOP:
            continue
        if ".git" in rel.parts or not p.is_file() or p.suffix not in SOURCE_EXT:
            continue
        files.append((rel.as_posix(), p))
    for rel, p in sorted(files):
        h.update(rel.encode() + b"\0" + hashlib.sha256(p.read_bytes()).digest())
    return h.hexdigest()


# ---- clean tree --------------------------------------------------------------
def dirty_entries(repo: Path, paths: tuple[str, ...] | list[str]) -> list[str]:
    r = _git(["status", "--porcelain", "--untracked-files=all", "--", *paths], repo)
    if r.returncode != 0:
        raise CheckError(f"git status failed in {repo}: {r.stderr.decode(errors='replace').strip()}")
    return [ln for ln in r.stdout.decode(errors="replace").splitlines() if ln.strip()]


def require_clean(repo: Path, paths=RELEASE_PATHS) -> None:
    bad = dirty_entries(repo, paths)
    if bad:
        shown = "\n  ".join(bad[:20]) + ("\n  ..." if len(bad) > 20 else "")
        raise CheckError("release from a dirty tree -- commit first (the image and release.json are stamped "
                         f"with HEAD's commit):\n  {shown}")


# ---- IDF deps ----------------------------------------------------------------
def idf_deps_problems(deps: Path, pins: dict[str, str], project: Path = PROJECT) -> list[str]:
    bad = []
    comps = deps / "components"
    for name, key, wrapped in GIT_DEPS:
        d = comps / name
        r = _git(["rev-parse", "HEAD"], d)
        head = r.stdout.decode().strip() if r.returncode == 0 else ""
        if head != pins.get(key):
            bad.append(f"{name}: HEAD {head or 'missing'} != pinned {pins.get(key)} -- rerun idf_fetch_deps.py")
            continue
        try:
            entries = dirty_entries(d, ["."])
        except CheckError as e:
            bad.append(f"{name}: {e}")
            continue
        for ln in entries:
            path = ln[3:].strip()
            if wrapped and path == "CMakeLists.txt":
                continue
            bad.append(f"{name}: '{ln.strip()}' differs from the pinned commit (hand edit?)")
        if wrapped:
            wrapper = project / "component-wrappers" / name / "CMakeLists.txt"
            got = d / "CMakeLists.txt"
            if not got.is_file() or got.read_bytes() != wrapper.read_bytes():
                bad.append(f"{name}/CMakeLists.txt is not the repo wrapper "
                           f"(component-wrappers/{name}/CMakeLists.txt)")
    np_dir = comps / NETPROV_DIR
    want = pins.get("NETWORK_PROVISIONING_TREE_SHA256")
    if not np_dir.is_dir():
        bad.append(f"{NETPROV_DIR} missing -- rerun idf_fetch_deps.py")
    elif not want:
        bad.append("deps.lock has no NETWORK_PROVISIONING_TREE_SHA256")
    else:
        got = tree_sha256(np_dir)
        if got != want:
            bad.append(f"{NETPROV_DIR} tree sha256 {got[:16]}... != pinned {want[:16]}... (hand edit?)")
    sdk = deps / "arduino-libs" / "sdkconfig"
    want = pins.get("ARDUINO_LIBS_SDKCONFIG_SHA256")
    if not sdk.is_file():
        bad.append("arduino-libs/sdkconfig missing -- rerun idf_fetch_deps.py")
    elif hashlib.sha256(sdk.read_bytes()).hexdigest() != want:
        bad.append("arduino-libs/sdkconfig sha256 != ARDUINO_LIBS_SDKCONFIG_SHA256 (hand edit?)")
    return bad


# ---- HMAC key freshness -------------------------------------------------------
HMAC_DEFINE_RE = re.compile(r'^\s*#\s*define\s+AREG_MANIFEST_HMAC_KEY\s+"([^"]*)"', re.M)


def hmac_key_from_config(path: Path) -> str:
    m = HMAC_DEFINE_RE.search(path.read_text(errors="replace"))
    return m.group(1) if m else ""


def committed_images(repo: Path) -> list[tuple[str, str]]:
    """(blob sha, path) for every firmware-image blob reachable from any ref."""
    r = _git(["rev-list", "--all", "--objects"], repo)
    if r.returncode != 0:
        raise CheckError(f"git rev-list failed: {r.stderr.decode(errors='replace').strip()}")
    out = []
    for ln in r.stdout.decode(errors="replace").splitlines():
        sha, _, path = ln.partition(" ")
        if path.endswith(IMAGE_SUFFIXES):
            out.append((sha, path))
    return out


def images_containing(repo: Path, needle: bytes) -> list[str]:
    hits = []
    for sha, path in committed_images(repo):
        r = _git(["cat-file", "blob", sha], repo)
        if r.returncode == 0 and needle in r.stdout:
            hits.append(f"{path} (blob {sha[:12]})")
    return hits


def require_fresh_hmac_key(repo: Path, key: str) -> int:
    if len(key) < 16:
        raise CheckError("the manifest HMAC key is missing or shorter than 16 characters")
    hits = images_containing(repo, key.encode())
    if hits:
        raise CheckError("the manifest HMAC key is inside firmware image(s) committed to this repository -- "
                         "treat it as published and generate a NEW key (backend and toys together): "
                         + "; ".join(hits))
    return len(committed_images(repo))


# ---- Arduino libraries ---------------------------------------------------------
def installed_arduino_libs(cli: list[str], run=subprocess.run) -> dict[str, dict]:
    r = run([*cli, "lib", "list", "--format", "json"], capture_output=True, text=True)
    if r.returncode != 0:
        raise CheckError(f"`arduino-cli lib list` failed: {r.stderr.strip()}")
    data = json.loads(r.stdout or "{}")
    rows = data.get("installed_libraries", []) if isinstance(data, dict) else data
    libs = {}
    for row in rows or []:
        lib = row.get("library", row)
        libs.setdefault(lib.get("name", ""), lib)
    return libs


def arduino_lib_problems(libs: dict[str, dict], pins: dict[str, str]) -> list[str]:
    bad = []
    for name, key in ARDUINO_LIBS:
        lib = libs.get(name)
        want_v = pins.get(f"{key}_LIBRARY_VERSION")
        want_h = pins.get(f"{key}_SRC_SHA256")
        if lib is None:
            bad.append(f"{name} is not installed (pinned {pins.get(f'{key}_TAG')}; see esp32/AregVoiceMvp/README.md)")
            continue
        if lib.get("version") != want_v:
            bad.append(f"{name} {lib.get('version')} installed, pinned library.properties version {want_v}")
        src = Path(lib.get("source_dir") or lib.get("install_dir") or "")
        if not src.is_dir():
            bad.append(f"{name}: source dir {src} not found")
            continue
        got = sources_sha256(src)
        if got != want_h:
            bad.append(f"{name}: sources at {src} hash {got[:16]}..., pinned {str(want_h)[:16]}... "
                       f"(not tag {pins.get(f'{key}_TAG')}, or edited)")
    return bad


# ---- CLI ------------------------------------------------------------------------
def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--repo", type=Path, default=REPO)
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("clean-tree")
    p.add_argument("paths", nargs="*", default=list(RELEASE_PATHS))
    p = sub.add_parser("idf-deps")
    p.add_argument("--deps", type=Path, default=Path(os.environ.get("AREG_IDF_DEPS", "")))
    p.add_argument("--lock", type=Path, default=LOCK)
    p = sub.add_parser("hmac-fresh")
    p.add_argument("--config-h", type=Path, help="read the key from this config.h instead of the environment")
    p = sub.add_parser("arduino-libs")
    p.add_argument("--arduino-cli", default="arduino-cli")
    p.add_argument("--lock", type=Path, default=LOCK)
    p = sub.add_parser("tree-sha256")
    p.add_argument("dir", type=Path)
    p.add_argument("--sources", action="store_true", help="hash only compilable sources (arduino-libs pins)")
    a = ap.parse_args(argv)
    try:
        if a.cmd == "clean-tree":
            require_clean(a.repo, tuple(a.paths))
            print(f"  ok    clean tree ({', '.join(a.paths)})")
        elif a.cmd == "idf-deps":
            bad = idf_deps_problems(a.deps, read_lock(a.lock))
            if bad:
                raise CheckError("pinned IDF dependencies do not match deps.lock:\n  " + "\n  ".join(bad))
            print(f"  ok    IDF dependencies in {a.deps} match deps.lock (commits, no edits, tree/sdkconfig hashes)")
        elif a.cmd == "hmac-fresh":
            key = hmac_key_from_config(a.config_h) if a.config_h else os.environ.get("AREG_MANIFEST_HMAC_KEY", "")
            n = require_fresh_hmac_key(a.repo, key)
            print(f"  ok    manifest HMAC key appears in none of the {n} firmware image(s) in git history")
        elif a.cmd == "arduino-libs":
            bad = arduino_lib_problems(installed_arduino_libs(shlex.split(a.arduino_cli)), read_lock(a.lock))
            if bad:
                raise CheckError("installed Arduino libraries differ from esp32/AregVoiceIdf/deps.lock:\n  "
                                 + "\n  ".join(bad))
            print("  ok    Arduino libraries match deps.lock (versions + source hashes)")
        elif a.cmd == "tree-sha256":
            print(sources_sha256(a.dir) if a.sources else tree_sha256(a.dir))
        return 0
    except (CheckError, OSError, ValueError) as e:
        print(f"FAIL - {e}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())

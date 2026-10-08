#!/usr/bin/env python3
"""Build an UNSIGNED release app with arduino-cli (the Arduino path).

The CANONICAL release build is the ESP-IDF one (tools/firmware/build_idf.sh
release): pinned libraries, byte-reproducible output. This script is the
supported fallback for the Windows release machine that already has
arduino-cli + esp32:esp32@3.3.8 -- same sources, same RELEASE profile, same
gate; but library versions are whatever that machine has installed, so its
bytes are not reproducible elsewhere. docs/firmware-security.md s5.1.

    python tools/firmware/build_release.py --version 1.4.0 [--forbid-version 1.3.4]
        [--out-dir esp32/AregVoiceMvp/release/1.4.0] [--arduino-cli arduino-cli]

Steps (each must pass or the script stops):
  0. provenance: no uncommitted or untracked change under esp32/AregVoiceMvp,
     esp32/AregVoiceIdf, esp32/bootloader-release, tools/firmware (the image
     is stamped with HEAD's commit); the installed Arduino libraries
     (`arduino-cli lib list --format json`) are the deps.lock pins --
     library.properties version AND a hash of their sources; the manifest
     HMAC key in the sketch's config.h is in no firmware image ever committed
     to this repository (tools/firmware/release_checks.py);
  1. arduino-cli compile with -DAREG_SECURITY_PROFILE_RELEASE and
     -DAREG_CONTENT_SYNC_BENCH (the release values -- AREG_FW_VERSION,
     AREG_BACKEND_BASE_URL https://..., AREG_MANIFEST_HMAC_KEY -- come from
     the sketch's local config.h, exactly as the OTA runbook says; the
     release profile then REFUSES to compile with any bench flag, compiled-in
     credential, shared PoP, http:// URL or empty HMAC key).
  2. re-run `esptool elf2image` with the board's own arguments
     (platform.txt: dio / 80m / 8MB / --elf-sha256-offset 0xb0) and require a
     byte-equal match with arduino-cli's .bin (proves the arguments);
  3. the same plus --secure-pad-v2 -> app-unsigned.bin;
  4. tools/firmware/check_release_image.py app-unsigned.bin
     --expect-version X --profile release [--forbid-version Y].
Then carry app-unsigned.bin to the OFFLINE machine:
  python tools/firmware/sign_release.py app --keyfile sb_primary.pem ...
"""
from __future__ import annotations

import argparse
import hashlib
import json
import shlex
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import release_checks  # noqa: E402

REPO = Path(__file__).resolve().parents[2]
SKETCH = REPO / "esp32" / "AregVoiceMvp"
GATE = REPO / "tools" / "firmware" / "check_release_image.py"
FQBN = "esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc"
RELEASE_DEFINES = "-DAREG_SECURITY_PROFILE_RELEASE -DAREG_CONTENT_SYNC_BENCH"
ELF2IMAGE = ["--chip", "esp32s3", "elf2image", "--flash-mode", "dio", "--flash-freq", "80m",
             "--flash-size", "8MB", "--elf-sha256-offset", "0xb0"]


class BuildError(RuntimeError):
    pass


def _run(argv: list[str]) -> subprocess.CompletedProcess:
    return subprocess.run(argv, capture_output=True, text=True)


def compile_argv(cli: list[str], out_dir: Path, sketch: Path = SKETCH) -> list[str]:
    return [*cli, "compile", "--fqbn", FQBN,
            "--build-property", f"compiler.cpp.extra_flags={RELEASE_DEFINES}",
            "--build-property", f"compiler.c.extra_flags={RELEASE_DEFINES}",
            "--output-dir", str(out_dir), str(sketch)]


def anti_rollback_enforced(repo: Path = REPO) -> bool:
    """True once the owner turned anti-rollback on in the frozen release
    bootloader (docs/firmware-security.md s11 rule 8A)."""
    defaults = repo / "esp32" / "bootloader-release" / "sdkconfig.defaults"
    return defaults.is_file() and any(ln.strip() == "CONFIG_BOOTLOADER_APP_ANTI_ROLLBACK=y"
                                      for ln in defaults.read_text().splitlines())


def preflight(cli: list[str], sketch: Path, lock: Path, run=_run, repo: Path = REPO) -> None:
    """Step 0 -- provenance. Runs before anything is compiled."""
    if anti_rollback_enforced(repo):
        raise BuildError("the release bootloader enforces anti-rollback: the precompiled Arduino core cannot "
                         "give an app a secure version, so arduino-cli makes DEV builds only -- use "
                         "tools/firmware/build_idf.sh release")
    r = run(["git", "-C", str(repo), "status", "--porcelain", "--untracked-files=all", "--",
             *release_checks.RELEASE_PATHS])
    if r.returncode != 0:
        raise BuildError(f"git status failed: {r.stderr}")
    dirty = [ln for ln in (r.stdout or "").splitlines() if ln.strip()]
    if dirty:
        raise BuildError("release from a dirty tree -- commit first (the image is stamped with HEAD's commit):\n  "
                         + "\n  ".join(dirty[:20]))
    r = run([*cli, "lib", "list", "--format", "json"])
    if r.returncode != 0:
        raise BuildError(f"`arduino-cli lib list` failed: {r.stderr}")
    try:
        data = json.loads(r.stdout or "{}")
    except ValueError as e:
        raise BuildError(f"`arduino-cli lib list --format json` printed no JSON ({e})") from None
    rows = data.get("installed_libraries", []) if isinstance(data, dict) else data
    libs = {}
    for row in rows or []:
        lib = row.get("library", row)
        libs.setdefault(lib.get("name", ""), lib)
    bad = release_checks.arduino_lib_problems(libs, release_checks.read_lock(lock))
    if bad:
        raise BuildError("installed Arduino libraries differ from esp32/AregVoiceIdf/deps.lock "
                         "(esp32/AregVoiceMvp/README.md has the install lines):\n  " + "\n  ".join(bad))
    config_h = sketch / "config.h"
    key = release_checks.hmac_key_from_config(config_h) if config_h.is_file() else ""
    try:
        release_checks.require_fresh_hmac_key(repo, key)
    except release_checks.CheckError as e:
        raise BuildError(f"{e} (key read from {config_h})") from None


def build(version: str, forbid: str | None, out_dir: Path, cli: list[str], esptool: list[str], run=_run,
          sketch: Path = SKETCH, lock: Path = release_checks.LOCK) -> Path:
    preflight(cli, sketch, lock, run)
    out_dir.mkdir(parents=True, exist_ok=True)
    r = run(compile_argv(cli, out_dir, sketch))
    if r.returncode != 0:
        raise BuildError(f"arduino-cli compile failed (exit {r.returncode}) -- a RELEASE profile #error "
                         f"names what to fix in config.h:\n{r.stdout[-4000:]}{r.stderr[-4000:]}")
    elf = out_dir / "AregVoiceMvp.ino.elf"
    arduino_bin = out_dir / "AregVoiceMvp.ino.bin"
    for f in (elf, arduino_bin):
        if not f.is_file():
            raise BuildError(f"arduino-cli produced no {f.name}")
    check = out_dir / "check.bin"
    r = run([*esptool, *ELF2IMAGE, "-o", str(check), str(elf)])
    if r.returncode != 0:
        raise BuildError(f"esptool elf2image failed: {r.stderr}")
    if check.read_bytes() != arduino_bin.read_bytes():
        raise BuildError("re-running elf2image did not reproduce arduino-cli's .bin byte for byte -- "
                         "the board's elf2image arguments changed; fix ELF2IMAGE before padding")
    check.unlink()
    unsigned = out_dir / "app-unsigned.bin"
    r = run([*esptool, *ELF2IMAGE, "--secure-pad-v2", "-o", str(unsigned), str(elf)])
    if r.returncode != 0:
        raise BuildError(f"esptool elf2image --secure-pad-v2 failed: {r.stderr}")
    gate = [sys.executable, str(GATE), str(unsigned), "--expect-version", version, "--profile", "release"]
    if forbid:
        gate += ["--forbid-version", forbid]
    r = run(gate)
    print(r.stdout)
    if r.returncode != 0:
        raise BuildError("release gate refused app-unsigned.bin")
    return unsigned


def main(argv=None, run=_run) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--version", required=True)
    ap.add_argument("--forbid-version")
    ap.add_argument("--out-dir", type=Path)
    ap.add_argument("--arduino-cli", default="arduino-cli")
    ap.add_argument("--esptool", default=f"{sys.executable} -m esptool")
    ap.add_argument("--sketch", type=Path, default=SKETCH,
                    help="sketch directory (default esp32/AregVoiceMvp, whose local config.h holds the release values)")
    ap.add_argument("--lock", type=Path, default=release_checks.LOCK,
                    help="pins for the Arduino libraries (default esp32/AregVoiceIdf/deps.lock)")
    a = ap.parse_args(argv)
    out_dir = a.out_dir or (SKETCH / "release" / a.version)
    try:
        unsigned = build(a.version, a.forbid_version, out_dir, shlex.split(a.arduino_cli), shlex.split(a.esptool), run,
                         a.sketch, a.lock)
    except BuildError as e:
        print(f"FAIL - {e}", file=sys.stderr)
        return 1
    print(f"[build] {unsigned} sha256 {hashlib.sha256(unsigned.read_bytes()).hexdigest()}")
    print("[next] sign it OFFLINE: python tools/firmware/sign_release.py app --keyfile sb_primary.pem "
          f"--input {unsigned.name} --expect-version {a.version} --out-dir <bundle>")
    return 0


if __name__ == "__main__":
    sys.exit(main())

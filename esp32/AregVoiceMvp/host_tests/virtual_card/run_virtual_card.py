#!/usr/bin/env python3
"""Run the firmware's logic on a PC: the virtual card, and the plain host tests.

    python3 esp32/AregVoiceMvp/host_tests/virtual_card/run_virtual_card.py [--only NAME] [--keep]
    python3 esp32/AregVoiceMvp/host_tests/virtual_card/run_virtual_card.py --host-tests

Default mode builds three programs from the REAL sketch sources and runs them:

    story_select_selftest   the on-device story-selection suite (story_select_test.cpp)
    content_sync_selftest   the on-device content-sync suite (content_sync_test.cpp)
    virtual_card            content_index_io + story_select + content_report on a fake SD

--host-tests builds and runs every host_tests/*_test.cpp (pure-logic headers, no
ArduinoJson needed) with whatever compiler is found. preflight.sh uses it when
there is no g++ on the PATH (a Windows PC with only the Visual Studio C++ tools).

Exit code: 0 all passed, 1 something failed, 77 skipped (no C++ compiler, or no
ArduinoJson 7 for the default mode). preflight.sh treats 77 as SKIP.
Compilers: g++ / clang++, or on Windows the Visual Studio C++ Build Tools.
ArduinoJson 7: $ARDUINOJSON_DIR (the folder holding ArduinoJson.h) or the usual
Arduino libraries folder.

The real config.h is NEVER read: the sketch is copied to a scratch folder and
config.h.example is used in its place, so no Wi-Fi password is compiled in.
"""
from __future__ import annotations

import argparse
import glob
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
HOST_TESTS = HERE.parent
SKETCH = HOST_TESTS.parent            # esp32/AregVoiceMvp
SKIP = 77

# name -> (main/test sources, extra sketch sources, -D flags)
PROGRAMS = {
    "story_select_selftest": (["main_story_selftest.cpp", "story_select_test.cpp"], [],
                              ["AREG_STORY_SELECT_TEST_BENCH"]),
    "content_sync_selftest": (["main_cs_selftest.cpp", "content_sync_test.cpp"],
                              ["content_sync_model.cpp"], ["AREG_CONTENT_SYNC_TEST_BENCH"]),
    "virtual_card": (["virtual_card_test.cpp"],
                     ["content_index_io.cpp", "story_select.cpp", "content_report.cpp",
                      "content_sync_model.cpp", "json_psram.cpp"], []),
}


def find_arduinojson() -> Path | None:
    cands = []
    if os.environ.get("ARDUINOJSON_DIR"):
        cands.append(Path(os.environ["ARDUINOJSON_DIR"]))
    home = Path.home()
    for base in (home / "Documents" / "Arduino" / "libraries" / "ArduinoJson",
                 home / "Arduino" / "libraries" / "ArduinoJson"):
        cands += [base / "src", base]
    for c in cands:
        ver = c / "ArduinoJson" / "version.hpp"
        if (c / "ArduinoJson.h").is_file() and ver.is_file():
            if "ARDUINOJSON_VERSION_MAJOR 7" in ver.read_text(errors="replace"):
                return c
    return None


def find_compiler():
    for name in ("g++", "clang++"):
        path = shutil.which(name)
        if path:
            return ("gnu", path)
    if os.name == "nt":
        for root in (os.environ.get("ProgramFiles(x86)"), os.environ.get("ProgramFiles")):
            if not root:
                continue
            hits = sorted(glob.glob(str(Path(root) / "Microsoft Visual Studio" / "*" / "*" /
                                        "VC" / "Auxiliary" / "Build" / "vcvars64.bat")))
            if hits:
                return ("msvc", hits[-1])
    return None


def stage(build: Path) -> None:
    """Copy the sketch (minus the real config.h) and lay the stand-ins over it."""
    for src in list(SKETCH.glob("*.h")) + list(SKETCH.glob("*.cpp")):
        if src.name != "config.h":
            shutil.copy2(src, build / src.name)
    shutil.copy2(SKETCH / "config.h.example", build / "config.h")
    for src in (HERE / "overlay").glob("*"):
        shutil.copy2(src, build / src.name)
    for src in HERE.glob("main_*.cpp"):
        shutil.copy2(src, build / src.name)
    shutil.copy2(HERE / "virtual_card_test.cpp", build / "virtual_card_test.cpp")


def compile_program(name, comp, sources, includes, defines, exe: Path, build: Path):
    """Returns (built_ok, compiler_output)."""
    if comp[0] == "gnu":
        cmd = [comp[1], "-std=c++17", "-O0", "-w"] + [f"-I{i}" for i in includes]
        cmd += [f"-D{d}" for d in defines] + [str(s) for s in sources] + ["-o", str(exe)]
        r = subprocess.run(cmd, capture_output=True, text=True, errors="replace")
        return r.returncode == 0, r.stdout + r.stderr
    # MSVC: /Fo must be a directory ending in a backslash; quoting it would let
    # the backslash escape the closing quote, so use a relative name.
    bat = build / f"build_{name}.bat"
    log_file = build / f"{name}.build.txt"
    incs = " ".join(f'/I"{i}"' for i in includes)
    flags = " ".join(f"/D{d}" for d in defines)
    srcs = " ".join(f'"{s}"' for s in sources)
    bat.write_text(
        "@echo off\r\n"
        f'call "{comp[1]}" >nul 2>nul\r\n'
        f'cd /d "{build}"\r\n'
        f'if not exist obj_{name} mkdir obj_{name}\r\n'
        f'cl /nologo /std:c++17 /EHsc /W1 /utf-8 {incs} {flags} {srcs} '
        f'/Fe:"{exe}" /Foobj_{name}\\ > "{log_file}" 2>&1\r\n'
        "exit /b %ERRORLEVEL%\r\n")
    subprocess.run(["cmd", "/c", str(bat)], capture_output=True, text=True, errors="replace")
    log = log_file.read_text(errors="replace") if log_file.exists() else ""
    return exe.exists() and "error C" not in log and "fatal error" not in log, log


def run_exe(name: str, exe: Path) -> bool:
    run = subprocess.run([str(exe)], capture_output=True, text=True, encoding="utf-8", errors="replace")
    lines = run.stdout.splitlines()
    last = [ln for ln in lines if ln.strip()][-1:] or [""]
    if run.returncode != 0:
        print(f"[{name}] FAILED (exit {run.returncode})")
        print("\n".join(ln for ln in lines if "FAIL" in ln or not ln.startswith("  ok"))[-4000:])
        return False
    checks = sum(1 for ln in lines if "PASS" in ln or ln.startswith("  ok"))
    print(f"[{name}] PASS ({checks} checks)  last line: {last[0][:90]}")
    return True


def exe_path(build: Path, name: str) -> Path:
    return build / (name + (".exe" if os.name == "nt" else ""))


def run_virtual(comp, aj: Path, build: Path, only) -> bool:
    stage(build)
    ok = True
    for name in ([only] if only else list(PROGRAMS)):
        mains, extra, defines = PROGRAMS[name]
        sources = [build / s for s in mains + extra]
        built, log = compile_program(name, comp, sources, [HERE / "stubs", build, aj], defines,
                                     exe_path(build, name), build)
        if not built:
            print(f"[{name}] BUILD FAILED\n{log[-3000:]}")
            ok = False
            continue
        ok = run_exe(name, exe_path(build, name)) and ok
    return ok


def run_host_tests(comp, build: Path) -> bool:
    tests = sorted(HOST_TESTS.glob("*_test.cpp"))
    if not tests:
        print("HOST TESTS: no *_test.cpp found")
        return False
    ok = True
    for t in tests:
        name = t.stem
        built, log = compile_program(name, comp, [t], [SKETCH], [], exe_path(build, name), build)
        if not built:
            print(f"[{name}] BUILD FAILED\n{log[-3000:]}")
            ok = False
            continue
        ok = run_exe(name, exe_path(build, name)) and ok
    print(f"HOST TESTS: {len(tests)} suites " + ("PASS" if ok else "FAIL"))
    return ok


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--only", choices=sorted(PROGRAMS), help="run one virtual-card program")
    ap.add_argument("--host-tests", action="store_true",
                    help="build and run host_tests/*_test.cpp instead (no ArduinoJson needed)")
    ap.add_argument("--keep", action="store_true", help="keep the scratch build folder and print it")
    args = ap.parse_args(argv)

    comp = find_compiler()
    if comp is None:
        print("SKIP: no C++ compiler (g++, clang++, or the Visual Studio C++ Build Tools)")
        return SKIP
    aj = None
    if not args.host_tests:
        aj = find_arduinojson()
        if aj is None:
            print("SKIP: ArduinoJson 7 not found (set ARDUINOJSON_DIR to the folder holding ArduinoJson.h)")
            return SKIP

    build = Path(tempfile.mkdtemp(prefix="areg-virtual-card-"))
    try:
        ok = run_host_tests(comp, build) if args.host_tests else run_virtual(comp, aj, build, args.only)
        if not args.host_tests:
            print("VIRTUAL CARD: " + ("PASS" if ok else "FAIL"))
        return 0 if ok else 1
    finally:
        if args.keep:
            print(f"build folder kept: {build}")
        else:
            shutil.rmtree(build, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())

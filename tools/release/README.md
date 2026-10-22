# Release preflight

One command that runs every automated release gate the repo has:

```bash
tools/release/preflight.sh                 # everything (~80 s, most of it dotnet test)
tools/release/preflight.sh --quick         # build only, skip dotnet test
tools/release/preflight.sh --no-dotnet     # skip build, test and the CLAUDE.md count
tools/release/preflight.sh --image esp32/AregVoiceMvp/build/AregVoiceMvp.ino.bin
```

Works in Git Bash on Windows. Needs `python3` (or `python`), `dotnet` 10 and a
C++ compiler (`g++`) on PATH; a missing compiler is a SKIP, a missing dotnet
or python is a FAIL. Output ends in `PREFLIGHT: GREEN` (exit 0) or
`PREFLIGHT: RED` (exit 1). Every gate's full output is in the log directory
printed at the top and bottom. Gates keep running after a failure.

| Gate | What it runs |
|---|---|
| dotnet-build | restore + Release build of `backend/ArmenianAiToy.slnx` (as CI) |
| dotnet-test | `dotnet test`; a failed OR skipped test is a FAIL |
| claude-md-count | the `dotnet test # N tests` line in CLAUDE.md must equal what ran; on a mismatch it tells you the number to write |
| firmware-host-tests | every `esp32/AregVoiceMvp/host_tests/*_test.cpp` with `g++ -std=c++17 -I..` (as CI) |
| python-tests | the unittest suites CI runs, plus `test_preflight_checks.py` |
| story-audio / music-audio | `check_story_audio.py`, `check_music_audio.py` |
| speaker-maps / ambience-anchors | `check_speaker_map.py`, `check_ambience_anchors.py` |
| secret-scan | key-shaped strings and credential files in the working tree (tracked + untracked, not gitignored); prints file, line and rule, never the value |
| release-image | `check_release_image.py <bin>`; SKIP unless `--image` is given |
| listen-test | `tools/listen/listen_test.py status`; a non-zero exit is a WARN, never a FAIL (unheard audio is a human gate) |

## What GREEN does not mean

- The firmware was not compiled (needs `arduino-cli` and the ESP32 core; run
  the build by hand, then pass the `.ino.bin` with `--image`).
- Nobody listened to anything. `listen-test` shows what is still unheard.
- Nothing was flashed, and no live provider (OpenAI, ElevenLabs, Railway) was called.

## Secret-scan false positives

A match is ignored when its text contains an obvious fixture word (`test`,
`example`, `placeholder`, `dummy`, `fake`, `abcdefgh`, ...), or when its line
carries the marker `preflight:allow-secret`. Anything else fails the gate; fix
the file rather than the pattern. Patterns and their tests live in
`preflight_checks.py` / `test_preflight_checks.py`.

## Tests and tips

```bash
python3 tools/release/test_preflight_checks.py -v
```

If Git Bash prints `$'\r': command not found`, the checkout converted line
endings: `git config core.autocrlf input` and re-checkout. `PREFLIGHT_LISTEN_SCRIPT`
overrides the listen-test script path (used to test the WARN path).

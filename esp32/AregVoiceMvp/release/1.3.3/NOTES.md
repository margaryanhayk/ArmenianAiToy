# Firmware 1.3.3 — hold-to-menu

- **Version**: `AREG_FW_VERSION` = `1.3.3`
- **Build tag**: `AREG_FW_BUILD` = `hold-to-menu`
- **Date**: 2026-08-15
- **Sketch size**: 1,328,736 B = **42.2%** of the 3 MB OTA app slot
  (1.3.2 was 1,328,688 B)

## CABLE-FLASHED ONLY — DO NOT STAGE FOR OTA

This build was compiled from a bench `config.h` carrying the owner's real
device id, real API key, and real Wi-Fi password. It must never be copied to
`FirmwareUpdate:ImagePath` or offered over the air as-is.

Before this version may become a real OTA release:

1. Rebuild from a `config.h` holding only placeholder credentials.
2. Run `tools/firmware/check_release_image.py` against the rebuilt binary
   and confirm it PASSES.

`FirmwareUpdate:LatestVersion` in `backend/src/ArmenianAiToy.Api/appsettings.json`
is deliberately left at `1.3.2` — this release was never staged, so the
offer gate has nothing to advertise for it. See
`docs/ota-release-runbook.md` § Field log → "1.3.3" for the full release
note.

## What changed

Fixes two defects in `handle_welcome_flow()` (the "what shall we do?" menu —
the only door to Game/Riddle/Curiosity):

1. It was reachable only once, at the tail of `setup()` — a power cycle was
   the only way back to it. Fixed with a hold-to-menu gesture: holding the
   button `AREG_MENU_HOLD_MS` (2000 ms) in IDLE re-opens it; a quick press
   still starts/resumes a story.
2. On child silence it returned immediately with no retry and no fallback —
   the toy asked a question and went dead silent. Fixed via a new
   `child_present` parameter: `true` after a hold now falls through to a
   story on silence instead of closing. Boot-time silence
   (`child_present=false`) still closes quietly, unchanged — nothing at
   power-on proves a child is in the room.

Full change list, the `transition_to(ST_IDLE)` trap, the bedtime carve-out,
and the content-sync arm-gate fix are documented in `CLAUDE.md` §
"Firmware 1.3.3 — hold-to-menu".

## Hardware evidence (real toy, COM7, 2026-08-15)

- Pre-flash boot log confirmed the defect live: `[welcome] ask ask-sgrc` →
  `[welcome] listening (mode)` → `[welcome] no answer — closing quietly` →
  `[state] 3 -> 0`, then silence.
- Post-flash: `[heartbeat] status=200 (fw=1.3.3 bedtime=0 paused=0)`,
  pre-arm waiting-line count **1** in 95 s (was ~19), no panic, heap
  123,724 B (unchanged from 1.3.2).
- NVS was not in esptool's erase list — device credentials, Wi-Fi, story
  cursor and heard-set survived the flash.
- Full log: `tools/quality-evidence/hold-to-menu-bench-20260815.log`.

## NOT verified

Nobody has yet pressed the button on 1.3.3. Quick-press-still-plays-a-story,
hold-opens-the-menu, held-button-not-eaten-as-the-answer,
silence-after-a-hold-plays-a-story, and toy-still-responds-after-the-menu
are all UNVERIFIED and await the owner's hands on the toy.

(No binaries in this folder are committed — `*.bin`/`*.elf`/`*.map` are
gitignored at `esp32/AregVoiceMvp/.gitignore`.)

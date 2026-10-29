# Bench notes 2026-10-29, fw 1.3.5-bench (build bench-20261029)

Written by the bench coach. Raw serial log: `bench-raw-20261029.log` (repo
root, NOT committed: it holds story stream tokens and the Wi-Fi name).
Verdicts are PASS / FAIL / NOT HEARD. NOT HEARD means the log shows it but
nobody listened, or it was not exercised.

## Setup

- Flashed earlier tonight with streaming OFF. The card was not re-loaded from
  the PC; it already held 16 stories (`[sd-selftest]` read a real story file
  twice with identical bytes, and content sync said "already current").
- After the flash the toy sent nothing on COM7 for about 8 hours. A watcher
  open, a 15 s raw read and one software reset pulse all got 0 bytes. Output
  only came back after the owner unplugged and replugged the USB cable.
- The watcher missed the first seconds of that boot, so the first
  `[boot] starting` line is not in the log. The second boot (below) has it.

## Steps on the real toy

| Step | Verdict | Evidence |
|---|---|---|
| R1 power on | PASS (partial) | `[ota] running fw=1.3.5-bench`, `[heartbeat] status=200`. First boot banner missed. |
| R2 quick press | FAIL (no input) | Owner pressed, "nothing changed". Zero `[button]` edge lines in the whole log. The firmware prints every raw edge before debounce and the boot line says `pin=18 resting=UP`, so the press never reached the pin. |
| R3 to R7 | NOT HEARD | Need a working button. |
| R8 volume knob | NOT HEARD | Log only. See finding 4. |
| R9 content sync | PARTIAL | Manifest 200, 20 offered, 16 accepted, index written, `[content-sync] PASS`. But `index parse failed: IncompleteInput` 7 times in the first 30 s of the boot (finding 2). |
| R10 button after flows | NOT HEARD | Cannot test without a press. |
| Idle stability | FAIL (1 reset) | One TASK_WDT reset after 136 min idle (finding 5). |

## Findings from the real toy

1. Serial silent after the flash until a physical USB replug (cause unknown).
2. Seven `IncompleteInput` index parse failures (1 content-report, 6
   story-select) in the first 30 s after boot. The same file parsed fine in
   content-sync at 3 min. Result in that window: welcome clip "not on card",
   "no cached stories", Wi-Fi stream fallback, stream answered non-200 twice.
3. The button never produced an edge. Pin 18 reads UP on every sample.
4. Volume input noisy at rest: about 850 to 1150 mV, with 19 readings of 0 mV
   out of 383. The volume code already has a deadband and needs two matching
   reads, so a 0 mV result is the pin really going to ground. Wiring.
5. At about 13:15 (ms 8176677) `task_wdt: Task watchdog got triggered`,
   `loopTask`, CPU 1 in IDLE (so the loop was blocked, not spinning), then
   `reset_reason=6/TASK_WDT`. The last `[alive]` was 49 s earlier, at the point
   the next 60 s heartbeat was due. The backtrace decodes to the idle task
   only, so the blocked call is inferred (see virtual bug 1), not proven.
   After the reboot the first 3 heartbeats failed with status -1, then 125
   succeeded (network was bad for about 3 min).
6. After that reset the SD card has not mounted: 250 `SD.begin failed` lines
   and no success since, while it worked after the USB power cycle before.
   Check at the bench: replug USB. If the card mounts again, the card gets
   stuck after a CPU-only reset. If not, it is wiring (see the 5 V note in
   `voice_client.cpp`).

## Virtual tests (no toy needed), run on the bench PC

| Test | Result |
|---|---|
| `dotnet test` (backend) | 3346 passed, 0 failed, 0 skipped |
| `tools/release/preflight.sh` | Real gates green: build, python 186 tests, story audio 20/20, music 4/4, speaker maps, ambience anchors, CLAUDE.md size. Reported RED for two false reasons (virtual bugs 6 and 7). |
| Firmware host tests, built with MSVC | 7 of 7 pass |
| Firmware on-device self-tests run on the PC with stand-ins | story selection 102 of 102, content sync 245 of 245 |
| Real story-picking code on a virtual SD card, 43 KB index (16 stories x 7 clips, 42 voice, 104 games, 4 music) | 16 stories eligible. Rotation (SHIP A6): no back-to-back repeat in 60 simulated power cycles for 2, 3, 4 and 5 stories, spread of 0. A story whose file is missing is never chosen. A story that never started is offered again. |
| Index size as a cause of `IncompleteInput` | Ruled out: the realistic 43 KB index parses. |
| Damaged index (cut at 10, 50, 99 percent, and a short read) | 0 stories, no crash, same message the toy printed. |
| Virtual toy against a local backend, 26 checks | 23 pass, 3 fail. Register, heartbeat, commands, manifest (57,677 bytes), all 20 story files and 70 clips and 42 voice and 104 game clips and 4 music tracks match size and sha256, Range resume gives 206, unknown story 404, traversal refused, longest content report accepted. |
| Static read of the button-dies rule (R10) | The story session has no early return and ends in IDLE; welcome flow is followed by a forced IDLE. No violation found. |

## Bugs found by the virtual tests, and what was done about each

Findings 1, 4, 6 and 7 are fixed on branch `fix/virtual-bench-findings-20261029`
(compiled, not flashed). 2 and 3 wait for an owner decision and are written up
in `docs/v2-backlog.md`. 5 is a bench check. The table above is the state BEFORE
the fixes; the results after them are in the next section.

1. **Network stall reboots an idle toy.** The TLS handshake timeout of core
   3.3.6 is 120 s and the sketch never sets one (`net_transport.cpp`), so a
   stalled heartbeat can block the loop longer than the watchdog. This toy's
   local config sets the watchdog to 30 s; the tracked default and example
   are 60 s ("larger than any legitimate blocking operation"), and the HTTP
   read timeout alone is 30 s. **Fixed in code:** handshake bounded to 10 s,
   the idle calls wait 8 s instead of 30 s, and setup() raises a watchdog below
   53 s and logs it (`net_budget_rules.h`, host test). Your `config.h` can drop
   its `AREG_WDT_TIMEOUT_S` line.
2. **The Wi-Fi story stream cannot serve a story from a fresh server.** By
   design it is a second, OpenAI-voice render of each story, made live on the
   first request and cached under `StoryAudio:CacheRoot`
   (`/data/story-audio-cache` in the Dockerfile). So on a fresh deploy, or
   while the `/data` volume is not mounted, the first request has to run
   moderation plus 4 TTS chunks before sending a byte. Locally, with a dummy
   key, a valid token gave 404 "Unknown story" once that render failed.
   Production was not tested. Effect: with no usable SD story the toy is
   silent on a press, and says nothing. **Not fixed, owner decision:** warm
   the cache after each deploy, remove the fallback, or play a calm clip (the
   failure clip is still a stub and needs copy and a listen test).
3. **The toy can hold 16 stories but the server offers 20.** The four dropped
   are the alternate endings of sutasan, sutlik-orskan, three-piglets and
   ulik, so only 6 of the 10 variant endings can ever be on a toy. 8 titles
   also exceed the 64-byte title buffer (Armenian letters take 2 bytes) and
   are cut; harmless unless a title is spoken. **Not fixed, owner decision:** a
   story entry is 1,120 bytes and five tables of them use about 90 KB of the
   toy's internal RAM; 20 stories would add about 22 KB to a heap that is
   about 90 KB free and needs about 45 KB for a TLS handshake. The fix is to
   move the tables to PSRAM and read the heap lines on the bench.
4. **A damaged index is never repaired and never retried.** A truncated
   `/content_index.json` with an intact `.prev` gives 0 stories from
   story-select, because the backup is restored only when the primary is
   missing. One short read at the wrong moment silences the toy until the next
   sync. **Fixed in code:** one shared reader (`content_index_io.cpp`) tries
   three times, then reads the previous copy, with a 5 s memo so a dying card
   is not hammered; content sync repairs the primary from the backup. A
   parent's "intro off" now survives a bad read too.
5. **Toy keeps SD-less after a CPU reset** (finding 6). Not reproducible
   virtually. The core's SD init already sends 160 clocks with CS high and a
   reset command at 400 kHz, so software has nothing more to add; this is a
   bench check: replug USB, and if the card mounts again, re-seat the wires.
6. **preflight.sh was falsely RED on Windows.** Python prints a carriage
   return, so "0" was compared with "0\r" and the test gate failed although all
   3346 tests passed. It also skipped the CLAUDE.md count check.
   **Fixed:** the helper prints bare LF (regression test included).
7. **secret-scan was falsely RED** on `tools/story-audio/clip-listen-test.html`
   (untracked, generated, one 7 MB line of embedded audio). Random audio bytes
   matched the Google key pattern. **Fixed:** the pattern needs a delimited
   token. Still do not commit that file.

Hardware picture: a button that never reads, a volume input that goes to
0 mV, an SD card that comes and goes and a serial port that goes silent are
all consistent with one loose ground or 5 V wire on the bench rig.

## After the fixes (same PC, same night, nothing flashed)

| Check | Result |
|---|---|
| `tools/release/preflight.sh` | **GREEN** (first time on this PC): dotnet test 3346, CLAUDE.md count and size, firmware host tests 8 suites (now built with the Visual Studio C++ tools), firmware virtual card 394 checks, python 191 tests, audio gates, secret scan 0 findings. Not a pass, by design: release-image (no image given) and listen-test (human gate). |
| Virtual card, 45 checks | The cases that failed before now pass: a short read is retried (recovers on attempt 3), a damaged primary reads from the previous copy, a missing primary reads through, a dying card is read once and not hammered, content sync repairs the primary, a parent's "intro off" survives, rotation still never repeats. |
| Firmware compile | arduino-cli, core 3.3.6, same FQBN: compiles, exit 0 (warnings were not enabled); +112 bytes of code and +8 bytes of internal RAM against the build flashed this morning (1,649,359 bytes of 16 MB; 61 percent of RAM variables, 127,312 free). |
| Not verified | Any of it on a toy; the handshake bound against a really stalled link; free heap while running. |

## Idle report 15:21

- One reset since the log started (the TASK_WDT one above).
- Uptime since it: about 126 min, heap flat (90812), RSSI -57 dBm.
- Still no `[button]` edge.

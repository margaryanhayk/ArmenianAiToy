# Bench test session — one evening, the hardware evidence SHIP.md lacks

**Written 2026-09-16, reworked 2026-10-22.** One evening, one toy, one cable.
The goal is to turn "built but never run" into "seen working" or "known
broken", and to leave behind a log and an evidence file that say so without
anyone having to remember the evening.

You press buttons and talk. `tools/firmware/bench_watch.py` reads the serial
port, scores what it can prove, and writes the evidence file when you press
Ctrl+C. Nothing in this file has been compiled, and **nothing can be flashed
from the cloud** — the flash step is yours and only yours.

The long per-feature checklists live in `esp32/AregVoiceMvp/README.md`. This
file is the running order: what to change, what to build, what to press, and
which SHIP.md item each step produces evidence for. Order matters: the
verified behaviour (regression) goes **first**, so a bad evening fails fast
on something known, not at midnight on something new.

| Part | What | Time | Produces |
|---|---|---|---|
| 0 | Pre-flight (PC side) | ~20 min | a build whose logs are distinguishable |
| 1 | Regression R1–R10 | ~25 min | "main still does what 1.3.3 did" |
| 2 | SHIP evidence | ~60 min | A6, A4, C1, C3, C4, A1 (+ D3's denominator) |
| 3 | Never-run features N1–N6 | optional, ~75 min | findings, not SHIP items |
| 4 | Listening, no toy | 1 h | the 14 unheard audio files |

---

## Part 0 — pre-flight (PC side)

Your real `config.h` is gitignored and lives only on your machine. Do **not**
replace it. `tools/firmware/bench_session.sh` edits it only where you ask
(`--label`, `--stream`) and writes a timestamped backup first.

### 0.1 ESP32 core 3.3.6 — not 3.3.7, not 3.3.8

`arduino-cli core list` must show `esp32:esp32 3.3.6`. With BLE provisioning on
(the default, `config.h.example:527`), Arduino-ESP32 3.3.7 and 3.3.8 crash the
S3 inside `btdm_controller_init` in a boot loop (`config.h.example:529-533`,
arduino-esp32 issues #12436 / #12357); the release machine builds with 3.3.6
(`docs/ota-release-runbook.md`). A toy that boot-loops tonight is the core,
not the firmware. CLAUDE.md names 3.3.8: that is what the cloud container
compiled with. `bench_session.sh` stops if it sees 3.3.7/3.3.8 with BLE on.

```
arduino-cli core install esp32:esp32@3.3.6
```

### 0.2 config.h — a NEW label, and nothing else

- **Set a new `AREG_FW_VERSION` / `AREG_FW_BUILD`** (for example `1.3.5-bench`
  and `bench-20261022`). Unless `config.h` sets them the toy reports
  `fw=1.0.0 build=dev` (`ota_foundation.h` default); every hardware log from
  2026-08-15 to 2026-09-02 says `fw=1.3.3`, so leaving that label makes tonight
  indistinguishable from them. The device reads `1.3.5-bench` as `1.3.5`
  (tolerant parse, `ota_apply.cpp`), so the suffix is safe, and it sorts above
  the staged 1.3.4 image. `bench_session.sh --label 1.3.5-bench` rewrites the
  two lines for you.
- **Leave `AREG_QA_STREAM_PLAYBACK` commented out for the FIRST flash.** It is
  an opt-in (`bench_session.sh --stream`) for the optional second flash (N5).
  The pre-negotiation streaming reader ran once on hardware
  (`button-jumper3-20260819.log`); the chunked de-framer never has, and a click
  during the regression run would not tell you whether streaming or the rest
  caused it. Earlier versions of the script turned it on unconditionally: if
  your `config.h` still has it on, comment it out.
- **No `*_BENCH`, `*_ONCE` or `*_FORCE` flag** except `AREG_CONTENT_SYNC_BENCH`,
  which is load-bearing (without it nothing ever downloads) and must stay ON.
  A bench flag left in `config.h` once ate the button for two evenings. The
  script lists strays; `[games] armed (pick=…)` in the log means
  `AREG_OFFLINE_GAMES_BENCH` is compiled in.
- **The variant-endings and story-pauses gates stay undefined** for Parts 1–2;
  see N6 for what they need.
- **Do not set `AREG_WDT_TIMEOUT_S` below 60.** An old override of 30 (the same
  as the HTTP read timeout) rebooted a toy on a stalled heartbeat on 2026-10-29.
  The firmware now raises a value below its floor and says so
  (`[boot] task watchdog raised 30s -> 53s ...`); if you see that line, delete
  the `AREG_WDT_TIMEOUT_S` line from `config.h`.

### 0.3 Compile first

```
arduino-cli compile --fqbn "esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc" ./esp32/AregVoiceMvp
```

- **Expect roughly 1.64 MB, about 52% of the 3 MB slot** (BLE provisioning on;
  the 1.3.4 image is 1,638,720 B = 52.1%, `release/1.3.4/NOTES.md`). The old
  "1.26 MB / 40%" figure is stale. **96–97% still means the wrong partition
  scheme** (`PartitionScheme=default` measures against a 1.25 MB slot) — stop
  and fix that before flashing.
- **Update 2026-10-27:** HEAD was compiled in the cloud on core 3.3.6 with the example
  `config.h`: exit 0, 0 warnings, 1,632,992 B = 51.9 % of the slot
  (`tools/quality-evidence/firmware-compile-20261027.md`). A compile error on your PC
  therefore points at your `config.h` or your library versions (ArduinoJson 7.4.2,
  ESP8266Audio 2.4.1, Adafruit NeoPixel 1.15.5 built), not at the sketch.
- The newest firmware commit, `5cf65b6` (2026-09-14), said in its own message
  *"NOT compile-verified: arduino-cli is not available in this environment"*,
  and CI runs host tests only. **A compile error here is a finding, not a bench
  failure** — keep the error text and stop; do not debug it at the bench.

```
arduino-cli upload -p COM7 --fqbn "esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc" ./esp32/AregVoiceMvp
```

`bench_session.sh --port COM7 --label 1.3.5-bench` does steps 0.1–0.3 and the
upload (and `--sd <mount>` loads the card).

### 0.4 Dashboard and cost cap

For this toy in the parent dashboard: **not paused**, outside the bedtime
window, all modes on, **after-story questions ON**. A paused toy ignores the
button (`[button] ignored`) and a toy in its bedtime window stays silent
(`[welcome] … staying silent`); the watcher lists both as problems, but
finding that out at 21:00 wastes the evening.

The repo default daily cost cap is **$0.25 per toy per day** (about 30
in-story questions, `appsettings.json` `OpenAI:DailyCostCap:Default`). R6, the A4
presses and the ten C1 chains come close. If the toy answers with the daily-limit
clip, that is the cap, not a bug — raise it on Railway before the evening.

### 0.5 Start the watcher, then touch nothing until it is listening

In a second terminal at the repo root:

```
python3 tools/firmware/bench_watch.py --port COM7 --log tools/quality-evidence/bench-<date>.log
```

(`pip install pyserial` once.) Close the Arduino serial monitor first — only
one program can hold the port. It opens the port with **DTR and RTS held low**
(on the S3's native USB-CDC those lines are the reset/download-mode gesture;
it cost an evening on 2026-08-17). It prints events as they happen
(`BOOT`, `CHAIN #n … run n/10`, `CUT`, `INTENT`, `HOLD`, `TROUBLE`) and keeps
the raw log flushed line by line. **A power-cycle is fine**: when the USB port
vanishes it waits and reconnects. Leave it running until the very end, then
press Ctrl+C: it prints the summary and writes
`tools/quality-evidence/bench-<YYYYMMDD-HHMM>.md` (never overwriting an
existing file). `--no-evidence` skips that file; `--replay <log>` scores an
old log with no toy attached.

---

## Part 1 — regression: the behaviour already seen on 1.3.2 / 1.3.3

Stop at the first failure; write down what you did. After **every** flow the
main button must still work (R10) — if it does not, that is the
`s_state != ST_IDLE` bug and the most important thing you can find tonight
(the watcher also flags a toy that keeps printing `[alive]` while its last
`[state]` is not IDLE).

| # | Press / say | Expect | Log line to look for | Prior evidence |
|---|---|---|---|---|
| R1 | Power on | your **new** `fw=` / `build=` in the banner (it prints once Wi-Fi is up), heartbeat OK | `[boot] AregVoiceMvp starting`, `[ota] boot poll (fw=… build=…`, `[heartbeat] status=200` | every 1.3.3 log |
| R2 | Quick press from idle | a story plays to the end, no tone / "aaaa" | `[story] selected <id> (N eligible)`, `[story] finished` | `full-story-verify-20260816.log`, `sd-bus-fix-listen-test-20260902.md` |
| R3 | Quick press mid-story, press again | it pauses; the second press resumes the **same** story | `[story] resuming <id> at byte N` | CLAUDE.md "browse/pause/resume" |
| R4 | GREEN / RED during a story | next / previous story starts from the top; the story named in the log is the one you hear | `[browse] a -> b (next\|prev)`, `[welcome] playing chosen story b` | `menu-first-test-20260826.log` |
| R5 | Let a story end | summary clip → **one** question → your answer → reaction → conclusion | `[post] summary/conclusion`, `[post] question N`, `[post] reply N played` | `reflection-answer-wdt-fix-20260816.log` |
| R6 | Press **and hold** mid-story, ask, let go | the answer plays, then the story resumes | `[story] SD barge-in`, `[audio] record_begin`, `[qa] answer played (buffered POST response)` | `button-jumper3-20260819.log` |
| R7 | Hold 2 s in idle, press GREEN | the menu opens, a chosen story plays | `[button] hold -- opening the menu`, `[welcome] playing chosen story <id>` | `sd-recheck-20260826.log:91`, `menu-first-test-20260826.log` |
| R8 | Turn the knob mid-story | volume follows | `[volume] gain …` | `volume-median-verify-20260816.log` |
| R9 | Wait ~3 min after boot | a sync runs, the index is written, **no** `index parse failed` (an `index read recovered on attempt N` or `using the previous copy` line means the card gave a bad read and the firmware worked around it: write it down, it points at wiring or the card); dashboard shows the content report | `[content-sync] manifest status=200`, `[content-sync] index written` | `content-sync-fix-verified-20260814.log`, `card-heal-20260902.log` |
| R10 | After every row above | the main button still responds | no `[alive]` while `[state]` ≠ 0 | the "never" rule |

R4 and R7 are worth a close look. The code comment near the browse path
(`AregVoiceMvp.ino`, "BLOCKED — DO NOT FLASH AND EXPECT THIS TO WORK YET")
describes a bug where the rotation's pick played instead of the chosen story;
the wipe it describes is now guarded (`if (!s_story_preselected)`), so the
comment is stale — but the guarded path is the code you are about to hear for
the first time. If the story you hear is not the one in the `[browse]` or
`[welcome] playing chosen story` line, write that down.

Streaming is **off** in this build, so R6 prints
`[latency] qa_release->play_begin_ms=…` (the buffered path) and the answer takes
about 5.7 s to start; that is the baseline, not a failure.

---

## Part 2 — SHIP evidence

Each step names the SHIP.md item it feeds. The watcher prints a block per item
and the evidence file repeats them; **PASS means the log proves the hardware
half, NOT YET means keep going, FAIL means the log shows a miss.** SHIP.md is
edited only by you.

### A6 — three cached stories, no back-to-back repeat, across a power-cycle

> SHIP A6: *the toy can choose from at least three cached Armenian stories on
> SD, using `/content_index.json`, without repeating the same story back-to-back.*

Make **five story starts from idle, one quick press each**. A press *during* a
story is a pause or a question, not a new story, so between starts either let
the story finish or — once you **hear** it begin — unplug and replug the toy.
Do at least one power-cycle (the rotation cursor `aregstory` lives in NVS; it
advances only after audio actually started). Do not use GREEN/RED: browsing is
not a rotation selection and is not scored.

Watcher: `story <id>` per start, then
`SHIP A6  N selections, adjacent repeats: 0, power-cycles between selections: K
(needs ≥3 selections + ≥1 power-cycle, 0 repeats)`. A reboot counts as a
power-cycle only if its `reset_reason` is POWERON, EXT or USB (11); a crash or a
software reset is reported separately and does not count. Hardware evidence on
record today (`tools/quality-evidence/*.log`): eight logs with one rotation
selection each, never three in one log, never across a power-cycle.

### A4 — interrupting mid-speech

> SHIP A4: *interrupting mid-speech stops the audio within one second and the
> toy responds to what the child said.*

Five presses in the **middle** of a story, each held while you ask a question.
**The serial log cannot time the stop**: `[button] pressed` prints only on the
idle path, and the barge-in line (`[story] SD barge-in: …`) is printed at the
same instant the decoder stops. So the "within one second" half is yours:
**film the toy with a phone (or use a stopwatch) and write the seconds from
press to silence for each of the five.** Put them in a note next to the
evidence file (for example `tools/quality-evidence/bench-<date>-owner-notes.md`).

Watcher: `CUT` per barge-in, then
`SHIP A4  N barge-ins, N answered; the <1 s stop is attested by ear or phone
video, not by this log` — i.e. each cut was followed by `[audio] record_begin`
and then `[qa] answer played`. That is the "responds to what the child said"
half; the verdict stays NOT YET until you add the seconds.

### C1 — ten voice chains in a row without a manual reset

> SHIP C1: *completes the full chain: wake → listen → backend → TTS →
> speaker, ten times in a row without a manual reset.*

Ten chains, alternating **an in-story question** (press and hold during a
story, ask, let go) **and the after-story answer** (when a story ends, answer
the one question it asks). Any mix works; the watcher prints
`CHAIN #n … (run n/10)` per completed chain (`[qa] answer played`,
`[post] reply N played`, `[chat] turn N played`). A reset, panic, task watchdog
**or a failed chain** ends the run — "ten in a row" means in a row. Do not
touch the reset button or unplug the toy during this step.

Watcher: `SHIP C1  longest run of voice chains without a reset: N (needs 10)`.
This step also gives **D3 its denominator**: the evidence file records
"N online turns in M minutes" (first to last line, host clock). Note the start
and end time and read the OpenAI / ElevenLabs / Gemini usage for that window
afterwards; D3 is the bill divided by that, and the bill is not in the log.

### C3 — a Wi-Fi drop recovers without an adult

> SHIP C3: *Wi-Fi drop mid-session recovers without the child needing an adult.*

With the toy idle, **switch the router off for 30 seconds, then on.** Watch for
`WIFI … event=got_ip`, then press, start a story, press again and ask something:
the next turn must complete with **no reboot**. Do not leave the router off for
five minutes: past `AREG_PROV_FALLBACK_AFTER_MS` (300000 ms,
`config.h.example:558`) the toy opens BLE provisioning
(`[prov] long Wi-Fi outage — opening BLE provisioning`), which needs an adult
and is reported as a WARN, not a recovery.

Watcher: `SHIP C3  Wi-Fi drop recovered without a reboot: 1 of 1 drop(s)` and
the outage length. Disconnects during boot are not counted (only a drop of a
link that was up), and an outage shorter than 20 s is reported as too short.

### C4 — loud enough and clear enough in a normal room

> SHIP C4: *measured, not guessed.*

Phone SPL app, **1 m** from the speaker, a story playing, at **two knob
settings**. Write down for each: the app, the distance, the room, and the
dB(A). A serial log cannot measure loudness; the evidence file lists C4 as NOT
YET until you add the note.

### A1 — a full story and its after-story, start to finish

> SHIP A1: *a full story in Armenian, start to finish, no crash, no English
> leaking in.*

This is R2 and R5 again with your ears on. Write **three lines**: (1) the story
was Armenian only, no English; (2) the after-story flow (summary, question,
reaction, conclusion) was heard; (3) no crash or reset. The watcher proves the
no-reset half (`SHIP A1  stories played start to finish with no reset: N`); the
language half is yours.

---

## Part 3 — never run on hardware (optional)

Findings, not SHIP items. Do these only if Parts 1–2 passed and you have time.
Each is in `esp32/AregVoiceMvp/README.md` with the long checklist.

- **N1 — content retirement and the index backup (15 min).** After a sync,
  `/content_index.json.prev` exists on the card. Power off, delete
  `/content_index.json` but keep `.prev`, boot: the log shows
  `[content-sync] primary index missing — restored from backup` and nothing is
  re-downloaded. A heartbeat, a welcome answer and an in-story question still work.
- **N2 — voice menu to a game (20 min).** Hold 2 s, say «խաղանք». **First check
  the log line `[welcome] intent=…` is not `unknown`.** `unknown` is the only
  intent ever logged on hardware (`owner-live-test-20260815.log`,
  `sd-selftest-20260830.log`): GREEN/RED reach only story/suggest/yes/no, so
  Game, Riddle, Curiosity and Calm are voice-only doors, and if STT keeps
  returning `unknown` they are unreachable on the real toy whatever the loop code
  does. The watcher prints `INTENT unknown` as a warning. With game clips on the
  card an offline game runs (needs the answer buttons, which the bench toy has);
  they round-robin; Simon is expected to exit after its intro (`tone-green` /
  `tone-red` were never rendered); each finished game appears on the dashboard.
- **N3 — the online loop (30 min).** Hold, say «հանելուկ»: riddle loop. Stay
  silent for two windows: `[chat] two silent windows — session over`, and **the
  button still works**. Say a stop word: goodbye plays and the session ends.
  Switch the router off mid-session: canned failure clip, clean return to idle.
- **N4 — Calm outside the bedtime window (5 min).** The phrase is in
  `.claude/MODES.md`.
- **N5 — streaming (second flash, 10 min).** `bench_session.sh --port COM7
  --skip-pull --stream --label 1.3.5-stream`. Ask five questions mid-story, count
  the seconds to the first sound (expect about 2.7 s, down from 5.7 s;
  `docs/latency-plan.md` Part 5), and listen for a click, a gap or a chopped
  first word. The watcher scores `[latency] qa_release->first_byte_ms` (the
  streaming key — a different quantity from the buffered `play_begin_ms`, which
  is why the line says which one it is). If it sounds broken, set
  `StoryQa__StreamAnswerAudio=false` on Railway: it turns streaming off for every
  toy, no reflash.
- **N6 — variant endings and story pauses (flag edit, re-flash).** Neither
  works from the dashboard toggle alone. Both have a firmware gate that defaults
  to **0** (`story_select.cpp:339-341` for endings, `AregVoiceMvp.ino:1749-1751`
  for pauses) and neither appears in `config.h.example`. Add to your `config.h`:

  ```c
  #define AREG_VARIANT_ENDINGS_ENABLED 1
  #define AREG_STORY_PAUSES_ENABLED 1
  ```

  then re-flash. Endings also need the parent toggle ON, the alt file on the
  card, and a story that **finished once** and is then selected again (the heard
  set is written only at a natural end). Pauses need the pause clips on the card,
  a long enough story and no bedtime window. The README section "Story feature
  toggles" says variant endings "ARE live"; the code gate says otherwise, and
  the code wins.

Skip BLE pairing (the per-toy PoP needs a factory-provisioned unit).

---

## Part 4 — listen to the new audio (1 hour, no toy needed)

Do this even if the bench session goes badly. It needs only your ears.

- [ ] The 10 variant story endings in
      `backend/src/ArmenianAiToy.Api/story-audio/*-alt.mp3`.
- [ ] The 4 bedtime tracks in `.../story-audio/music/`.

Nobody has ever heard any of these 14 files. No tool can hear a seam, a wrong
voice, or a bad join. Listen to the bedtime tracks at bedtime volume on the
toy's actual speaker — the −23 LUFS target has never been confirmed by ear.

---

## What to hand over

Ctrl+C the watcher, then keep: the raw `.log`, the generated `.md` (both in
`tools/quality-evidence/`), and your notes — the five A4 seconds, the C4
readings, the A1 three lines, the D3 start/end times. For each failure: what you
pressed, what you heard, what the log said, and the `build=` value. That is
enough for a fix session to start from evidence instead of memory.

---

## Honest status of this document

- **Measured, from the repo:** the `AREG_QA_STREAM_PLAYBACK` effect
  (5.7 s → 2.7 s, `docs/latency-plan.md` Part 5); the 1.3.4 image size
  (1,638,720 B = 52.1% of the slot, `release/1.3.4/NOTES.md`, same size as the
  tracked `areg-current.bin`).
- **Read from the code, not run:** the backend streaming default of true
  (`StoryQaController.cs:655-659`); the 3.3.6 core requirement
  (`config.h.example:529-533`); the two feature gates' defaults; every serial
  line named here (grepped from `esp32/AregVoiceMvp/*.ino|cpp` and compared with
  the real logs in `tools/quality-evidence/`); the `reset_reason` numbers.
- **Not verified by anyone:** every item in Parts 2 and 3 on hardware; the newest
  firmware commit had never been compiled (`5cf65b6`; compiled clean 2026-10-27, never flashed); whether the guarded browse
  and menu-choice path plays the chosen story.
- **`bench_watch.py`** is covered by 80 offline unit tests (`tools/firmware/
  test_bench_watch.py`, run in CI) over synthetic logs in the firmware's real line
  shapes and replays of the saved logs, and was run once end to end against real
  pyserial 3.5 over a Linux pseudo-terminal (open, readline, a vanished port,
  Ctrl+C, evidence written). It has **never been run against the toy or a
  USB-CDC port**: whether DTR/RTS held low really prevents the S3 reset on a
  Windows COM port, and whether the reconnect catches a real power-cycle, are
  unverified. The `reset_reason` classification and the stuck-state detector
  rest on the firmware's own comments and on 58 saved logs showing no false
  positive, not on a bench run.
- This file was written in a container with no arduino-cli and no toy. It has not
  been compiled or run.

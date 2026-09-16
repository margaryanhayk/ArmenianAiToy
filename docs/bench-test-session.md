# Bench test session — flashing and testing everything unverified

**Written 2026-09-16.** One evening, one toy, one cable. The goal is to turn
"built but never run" into "seen working" or "known broken".

Nothing in this file has been compiled. There is no arduino-cli in the cloud
container these instructions were written in, and **nothing can be flashed
from the cloud** — the flash step is yours and only yours.

The long, exhaustive per-feature checklists already live in
`esp32/AregVoiceMvp/README.md`. This file does not repeat them. It is the
running order: what to change, what to build, and what to press first so a
bad evening fails fast instead of at midnight.

---

## Part 1 — config.h changes

Your real `config.h` is gitignored and lives only on your machine. Do **not**
replace it — it holds your Wi-Fi, your backend URL and your bench
credentials. Change only the lines below.

### 1.1 The one that makes the toy fast

Find this line (it is commented out in `config.h.example` around line 477)
and **uncomment it**:

```c
#define AREG_QA_STREAM_PLAYBACK 1
```

This is the single biggest change of the evening. Measured effect, from
`docs/latency-plan.md`: time until the child hears the first sound drops from
**5.7 s to 2.7 s**.

The backend side is already done and already live. `StoryQa:StreamAnswerAudio`
defaults to **true** (`StoryQaController.cs:641-645`), and the server only
streams to a toy that asks by sending `X-Areg-Accept-Stream: 1`. A toy on
flag-off firmware never asks and is never handed a streamed body, so turning
this on cannot break any other unit.

### 1.2 Leave these alone

Everything else new is already compiled into every build and needs no flag:
the online Game/Riddle/Curiosity/Calm voice loop, the three offline games,
content retirement, per-namespace index writes, the per-toy BLE pairing code,
and the 2026-09-14 code-review fixes.

Do **not** enable the `*_BENCH` flags for this session. They change startup
behaviour (auto-starting games, forced syncs) and will get in the way of
testing the real flow. One thing at a time.

### 1.3 The SD orphan sweep is a server setting, not a config.h flag

It is off by default and is switched on per-device from the backend
(`orphanSweepEnabled` in the content manifest). Leave it off for this first
session — it deletes files, and you want a boring card while you are testing
everything else.

---

## Part 2 — build and flash

Use the canonical FQBN. Using `PartitionScheme=default` is the known cause of
the false "96–97% of program storage" alarm, because it measures against a
1.25 MB slot instead of the real 3 MB one.

```
arduino-cli compile --fqbn "esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc" ./esp32/AregVoiceMvp
```

Expect roughly 1.26 MB, about 40% of the slot. If it reports 96%, you used
the wrong partition scheme — stop and fix that before flashing.

```
arduino-cli upload -p COM7 --fqbn "esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc" ./esp32/AregVoiceMvp
```

**Before you blame the hardware for anything tonight, check `AREG_FW_BUILD`
in the serial log.** A bench build flag left in `config.h` once ate the
button for two evenings.

---

## Part 3 — test order

Ordered so the cheapest and most fundamental things fail first. Stop and
write down anything that fails; do not push on to the next section hoping it
gets better.

### Step 0 — it still boots and the button still works (2 minutes)

- [ ] Power on. Serial log shows the expected `AREG_FW_VERSION` and
      `AREG_FW_BUILD`.
- [ ] Quick press in idle → a story starts.
- [ ] Press again mid-story → it pauses. Press again → it resumes.
- [ ] After the story ends, the button still responds.

If the button dies after any flow tonight, that is the `s_state != ST_IDLE`
failure and it is the most important bug you can find. Note exactly what you
did just before.

### Step 1 — the speed change (10 minutes)

This is why you are here.

- [ ] Start a story, press during it to ask a question, and **count the
      seconds** until you hear the first sound of the answer.
- [ ] Expected: about 2.7 s, down from about 5.7 s.
- [ ] Do it five times. One fast answer is luck.
- [ ] Listen for a click, a gap, or a chopped first word. Streaming changes
      how the audio is de-framed, and a de-framing defect is exactly what the
      backend kill switch exists for.

If it sounds broken: set `StoryQa__StreamAnswerAudio=false` on Railway. That
turns it off for every toy immediately, with no reflash.

### Step 2 — my code-review fixes (15 minutes)

These went to main on 2026-09-16 and have never run on hardware.

- [ ] Normal content sync still works end to end: manifest, download, index
      written, files playable.
- [ ] After a sync, confirm `/content_index.json.prev` now exists on the card.
- [ ] Power the toy off, delete `/content_index.json` but leave `.prev`, boot
      again → it should restore from the backup instead of re-downloading the
      whole library.
- [ ] A heartbeat, a welcome-flow voice answer, and an in-story question all
      still work (these paths got the response-size caps).

The `millis()` rollover fix cannot be tested on a bench. It needs 49 days of
uptime. Accept it on the code reading.

### Step 3 — the voice menu and the online loop (30 minutes)

Never run on hardware at all.

- [ ] Hold 2 s in idle → the welcome menu greets you and asks what to do.
- [ ] Say «խաղանք» with no game clips on the card → the online Game loop
      starts.
- [ ] Say «հանելուկ» → riddle loop.
- [ ] Stay silent for two windows in a row → the session ends cleanly and
      **the button still works**.
- [ ] Say a stop word → goodbye plays, clean return to idle.
- [ ] Turn the router off mid-session → canned failure clip, clean return to
      idle, button still alive.

### Step 4 — offline games (20 minutes)

Compiled into every build since 2026-08-19. Never run.

- [ ] With game clips on the card, say «խաղանք» → a real offline game runs,
      not the online loop.
- [ ] Play each of the three at least once. They round-robin.
- [ ] Simon will not work yet: the `tone-green` and `tone-red` clips have
      never been rendered. Expect it to be skipped.
- [ ] Each finished game appears on the parent dashboard.

### Step 5 — listen to the new audio (1 hour, no toy needed)

Do this even if the bench session goes badly. It needs only your ears.

- [ ] The 10 variant story endings in
      `backend/src/ArmenianAiToy.Api/story-audio/*-alt.mp3`.
- [ ] The 4 bedtime tracks in `.../story-audio/music/`.

Nobody has ever heard any of these 14 files. No tool can hear a seam, a
wrong voice, or a bad join. Listen to the bedtime tracks at bedtime volume on
the toy's actual speaker — the −23 LUFS target has never been confirmed by
ear.

---

## Part 4 — what to record

For each failure: what you pressed, what you heard, what the serial log said,
and the `AREG_FW_BUILD` value. That is enough for a fix session to start from
evidence instead of guesswork.

Put the results in `tools/quality-evidence/` with today's date, the way every
other bench result in this repo is recorded.

---

## Honest status of this document

- The `AREG_QA_STREAM_PLAYBACK` effect (5.7 s → 2.7 s) is **measured**, from
  `docs/latency-plan.md` Part 5. Not estimated.
- The backend streaming default of true is **read from the code**
  (`StoryQaController.cs:644-645`), not assumed.
- The build size and FQBN warning are **from the firmware README**, measured
  on a real build.
- Everything in Part 3 is **untested by anyone**. That is the entire point of
  the session.
- This file was written in a container with no arduino-cli and no toy. It has
  not been compiled or run.

# Three shipped stories play about a third of their text

**Found 2026-08-10** during a full-project review, on branch
`claude/story-audio-project-review-dfpiq4` at `64a6957`. Measured, not inferred.

---

## The finding

Three of the eight stories a child can hear today stop partway through and end
mid-tale. **«Խոսող ձուկը» plays for 1 minute 21 seconds of a story that needs
about 5 minutes 17** — roughly a quarter of it.

| Story | Text needs | Actual audio | Share | Verdict |
|---|---|---|---|---|
| `khosogh-dzuk` | 5:17 | **1:21** | **26%** | truncated |
| `anban-huri` | 3:39 | **1:27** | **40%** | truncated |
| `pochat-aghves` | 3:35 | **1:25** | **40%** | truncated |
| `ulik` | 1:48 | 1:18 | 72% | passes, barely |
| `sutlik-orskan` | 2:05 | 1:40 | 80% | passes |
| `princess-and-pea` | 1:04 | 1:02 | 96% | passes |
| `sutasan` | 1:12 | 1:09 | 96% | passes |
| `three-piglets` | 1:21 | 1:23 | 103% | passes |

Reproduce with, from the repo root:

```
python3 tools/story-audio/check_story_audio.py
```

which exits non-zero and prints the table above. It needs nothing installed.

## Why the numbers are trustworthy

"Text needs" is `characters / 15`, the same rule
`tools/story-audio/Ship-StoryAudio.ps1` uses (`CharsPerSecond = 15.0`), and the
70% floor is that script's `ShortFloor`. The rule is well calibrated against
this library: the three stories that are demonstrably complete narrate at
**14.8, 15.7 and 15.7 characters per second**.

The three failing ones would have to narrate at **37.9, 37.9 and 58.8
characters per second** to contain their whole text. That is not a brisk
delivery; it is not physically possible. The audio genuinely stops early.

Durations come from summing every MPEG frame, not from a header estimate — the
same header-only shortcut is what let this pass unnoticed before.

## Why it was not caught

The library was rendered outside the repo. `Ship-StoryAudio.ps1` exists exactly
to catch this — its header names "too short for the text" as one of the five
defects it was written for — but **it was never run on these files**. The proof
is in the encoding: that script emits **192 kbps**, and all eight shipped files
are **128 kbps**. They never passed through it.

CLAUDE.md § "Story narration pipeline" describes this class of defect as found
and mitigated on 2026-08-04, and it is worth being precise about what was and
was not done:

- The mitigation was real — `tools/ElevenLabsRender` gained chunking and a
  length check, and refuses a render under 70% of expected.
- The five stories then at `Version: 1` were re-rendered to `Version: 6`.
- But **all five of those re-renders are still short** (26%, 40%, 40%, 72%,
  80%), while the three stories never re-rendered (`Version: 2`) are the three
  that are complete. The re-render did not fix the problem it was for, and
  nothing compared the result against the text afterwards.

## The second consequence: in-story questions are answered about the wrong scene

Truncation does not only cut the story short — it also misleads the part of the
backend that decides *where in the story the child is*.

When a child interrupts to ask a question, the toy sends the byte offset it
paused at. `StoryQaController.OffsetToSegment` prefers an exact per-segment byte
map (`{story}.segments.json`) and falls back to
`offset × segmentCount ÷ fileSize` when there is none. **There is none: zero
`.segments.json` files exist in this repo**, because the library was rendered
outside it. So every shipped story uses the proportional guess.

That guess is fine when the audio matches the text. On the truncated stories it
is badly wrong, because `fileSize` covers only a fraction of the segments:

- `khosogh-dzuk`'s file holds ~26% of the text. A child who interrupts near the
  end of the *file* has heard roughly segment 2 of 9 — the proportional guess
  scores him at segment 8.
- The prompt is then grounded in a scene he has not heard, and the re-anchor
  recap line describes events that have not happened yet.

The whole-story summary in the prompt still answers most questions correctly, so
this degrades quality rather than breaking it — but it is a real, invisible
second cost of the truncation and it disappears with the same fix.

**The durable fix is in the recording, not the code:** ask the narrator for one
WAV per story segment. Concatenating them produces the exact byte map as a
by-product, which retires the proportional fallback for good. Recorded in
`docs/voice-narrator-brief.md` §3, because it has to be asked for *before* the
studio session, not after.

## Two related observations

- **`ulik.mp3` does NOT have a second ID3 tag.** An early pass reported one; it
  was a false positive — the bytes `ID3` appear at offset 427715 inside ordinary
  audio data, with an impossible version byte (0x53) and a 124 MB declared size.
  `check_story_audio.py` validates the version, revision and syncsafe size bytes
  so it cannot make that mistake. Recorded here because the wrong version of this
  finding was nearly published.
- **The firmware assumes the wrong bitrate.** `AREG_STORY_PAUSE_BYTES_PER_SEC`
  is `24000` (192 kbps); the shipped files are 128 kbps = 16000 B/s, a 1.5×
  error in the mid-story pause planner's position maths. Harmless today because
  story pauses are gated off (`AREG_STORY_PAUSES_ENABLED 0`), and it becomes
  correct by itself if the re-render ships at 192 kbps through the PowerShell
  script. **Check it against the bitrate actually shipped before turning pauses
  on.**

## What was deliberately not done

No story was re-rendered. `tools/ElevenLabsRender` states the reason itself:
*"SAMPLE BEFORE YOU BATCH: the narrator voice is still interim, so a full render
is thrown away when it changes."* The narrator is an open owner decision, so the
fix belongs in that one re-render pass, together with the voice change and the
ambience cues in `backend/content/story-ambience/`.

Loudness was not measured — this container has no ffmpeg. The -16.4 LUFS check
stays with `Ship-StoryAudio.ps1`, which must be run on a machine that has it
before anything is installed.

## The gate, going forward

`tools/story-audio/check_story_audio.py` is the same two checks with no
toolchain to install, so it can run in CI, in a container, or on any machine on
the day it matters. The PowerShell script remains the shipper — it owns repair,
levelling, install and the `Version` bump, and it is still the thing to run
before anything reaches a card.

Neither replaces the human listen test.

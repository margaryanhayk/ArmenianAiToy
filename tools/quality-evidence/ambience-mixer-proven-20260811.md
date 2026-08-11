# The ambience mixer, run for the first time

**2026-08-11.** `tools/story-audio/mix_ambience.py` was written on 2026-08-10
without ever being executed — the container had no ffmpeg, and the file said so
honestly. ffmpeg is installed now (see `docs/container-toolchain.md`), so this
records what happened when it finally ran, because "written but never run" is
exactly the state that let three truncated stories reach a child.

## What was run

There are no sound files yet — all 18 are `licence: TBD` and nothing has been
chosen or bought. So the inputs were synthesised:

- **Narration**: nine WAVs of speech-shaped noise, one per segment of
  «Անբան Հուռին», each sized from that segment's real character count at the
  library's 15 chars/second. Total **3:39.4** — the length the real story needs.
- **Sounds**: one 8-second WAV per id in the cue sheet, so nothing resolves to
  a missing file for the wrong reason.

```
python3 tools/story-audio/mix_ambience.py --story anban-huri \
  --segments-dir <segs> --sounds-dir <sounds> --out <out> --render
```

## It works, and here is the proof rather than the claim

**No drift.** Output duration `219.400000` s against 219.4 s of narration in.
The concatenation and the five cue layers do not lengthen or shorten the story.

**The narration is untouched.** Measured over a ten-second window with no cue
in it, before and after the mix:

| | mean volume |
|---|---|
| narration alone | **-26.1 dB** |
| after mixing | **-26.1 dB** |

That is the `amix ... normalize=0` contract the tool documents holding in
practice. With ffmpeg's default, every input would be scaled by 1/N and the
voice would have been quietly pulled down as cues were added — the failure the
tool was written to avoid, now demonstrated rather than asserted.

**The cues land exactly where the sheet says.** Subtracting the narration back
out of the mix leaves only what the mixer added:

| window | expected | isolated ambience |
|---|---|---|
| 0:00–0:04 | village-morning | **-45.8 dB** |
| 0:20–0:30 | *nothing* | **-91.0 dB** |
| 0:57–1:02 | river-shallow | **-41.4 dB** |
| 1:25–1:28 | river-shallow (seg 4) | **-43.8 dB** |
| 2:00–2:10 | *nothing* | **-91.0 dB** |
| 2:35–2:38 | hall-murmur | **-47.8 dB** |

-91 dB is digital silence. The mixer adds sound at the five cue times and
**absolutely nothing** anywhere else.

**The segment map appears.** `anban-huri.segments.json` was written with nine
starts and nine durations. This repo has never had a single `.segments.json`,
which is why `StoryQaController.OffsetToSegment` guesses a child's position from
`offset / fileSize` — a guess that is badly wrong on the truncated stories. The
mixer produces the map as a by-product of doing its real job.

## All 29 anchors still hold

`tools/story-audio/check_ambience_anchors.py` (added today, needs nothing
installed) re-reads every cue against the actual story text:

```
checked 29 cues across 8 stories against 18 sound ids
every cue lands in the segment it names, quoting a line that is really there
```

Worth having in CI: cues are anchored to a quoted line rather than a timestamp,
so an edit to a story's text is exactly what silently breaks them.

## What is still blocked, and it is not the tool

**The 18 sounds.** Every one reads `licence: TBD`; nothing has been chosen or
bought. That is a cost decision, not an engineering one, and it is deliberately
the one thing that cannot happen by accident — no file can reach a toy without
someone answering the licensing question first.

The ambience also rides the **next render**, not this one: the per-segment WAVs
this tool needs only exist once the narrator records that way
(`docs/voice-narrator-brief.md` §3), and the Charlotte pass is a single MP3 per
story.

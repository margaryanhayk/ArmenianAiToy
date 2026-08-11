# The truncation had a measurable cause, and it was the request size

**2026-08-11.** The 2026-08-10 review established *that* three shipped stories
play a quarter to a third of their text
(`story-audio-truncation-20260810.md`). This establishes *why*, closes it, and
records the fix that was shipped the same day.

## The measurement

Character counts come from the story JSONs
(`backend/src/ArmenianAiToy.Application/Stories/Content/*.story.json`);
durations from `tools/story-audio/check_story_audio.py`, which walks the MP3
frames. "Rendered chars" is the delivered duration converted back at the
library's measured 15 characters/second.

| story | chars in text | audio | share | chars rendered |
|---|---:|---:|---:|---:|
| princess-and-pea | 967 | 1:02 | 97% | ~930 |
| sutasan | 1,080 | 1:09 | 96% | ~1,035 |
| three-piglets | 1,222 | 1:23 | 102% | ~1,245 |
| ulik | 1,616 | 1:18 | 72% | ~1,163 |
| sutlik-orskan | 1,875 | 1:40 | 80% | ~1,500 |
| pochat-aghves | 3,220 | 1:25 | 40% | ~1,288 |
| anban-huri | 3,290 | 1:27 | 40% | ~1,316 |
| khosogh-dzuk | 4,753 | 1:21 | 26% | ~1,236 |

Read the last column, not the share column. **Every story that came back short
delivered between 1,163 and 1,500 characters of audio, whatever it was asked
for** — a 4,753-character story and a 1,616-character story produced almost the
same amount of speech. Every story that fits under that ceiling is complete.

That is not a story-by-story quality problem. It is one number.

## Whose ceiling it is

It belongs to **the clone**, not to the model or the tool. On 2026-08-10 the
same tool, same `eleven_v3` model and same 4,753-character text produced:

| voice | length | share |
|---|---:|---:|
| the owner's clone (`areg-storyteller`) | 1:21 | 26% |
| Charlotte (ElevenLabs Default) | 6:03 | 114% |
| Daniel (ElevenLabs Default) | 7:04 | 134% |

Consistent with ElevenLabs' own note that Professional Voice Clones are not
fully optimised for Eleven v3.

## Why it recurred after being fixed once

`CLAUDE.md` § Story narration pipeline has said since 2026-08-04 that v3
curtails at ~1,200–1,400 characters and that "long stories need ~800-character
chunks". The tool was built with `--max-chunk 700` accordingly.

The default was later raised to **4,000** with this comment:

> (The old 700 came from a truncation that was really a wrong-model problem.)

Both halves of that sentence are true separately — v3 *is* the only model on the
account that speaks Armenian — but the conclusion does not follow, and at 4,000
most stories go as a single request again. Five of the eight stories were
re-rendered under that default and all five came back short. **The three stories
that are complete today are the three that were never re-rendered.**

This is the same shape as the original finding: a documented gate, and a change
made on a plausible-sounding root cause that nothing re-measured.

## The fix

`--per-segment` on `tools/ElevenLabsRender`: one request per story segment
rather than per story. The longest single segment in the library is **835
characters**, so no request can approach the ceiling. Truncation stops being
unlikely and becomes arithmetically impossible.

Three by-products, none of them incidental:

- Seams fall on paragraph breaks. v3 rejects `previous_text`/`next_text`, so
  every seam is blind either way; better to put them where a narrator pauses.
- A fluffed line costs one request instead of a story.
- **The segment map.** Per-segment durations give `<storyId>.segments.json`,
  which this repo has never had — see below.

## A real bug the new self-test caught

The first version of the map summed the durations of the API responses. Those
are wrong by one frame each: every response opens with a Xing/"Info" header
frame that `Mp3Stitch` correctly drops but `Mp3Duration` counts. Four synthetic
pieces showed the discrepancy immediately:

```
sum of pieces   27.037s
joined file     26.932s   delta 0.104s      <- 4 pieces x 26ms
```

26 ms per segment, accumulating down the story. Small enough to look right,
which is exactly the failure mode this whole thread of work has been about. The
map now measures each piece **as it appears in the finished file**
(`Mp3Stitch.Join([piece])`), and the same test reports `delta 0.000s`.

Re-runnable with no API key and nothing sent:

```
dotnet run --project tools/ElevenLabsRender -- --self-test --output <dir of mp3s>
```

## Still true, still open

- **Nothing has been re-rendered.** This records the cause and ships the fix;
  the paid render is the owner's to run
  (`docs/story-audio-rerender-runbook.md`). Until he does, three stories still
  stop mid-tale on real toys.
- `check_story_audio.py` still exits non-zero on the shipped library, by design.
  It goes into CI the day it is green — a check that is red for a known reason
  is a check people learn to ignore.

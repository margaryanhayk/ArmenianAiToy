# The renderer read its own stage directions aloud

**Date:** 2026-08-12. **Found by:** the owner, by listening.
**Status:** fixed, with two guards so it cannot recur.

## What happened

Character voices were requested by putting the direction in the text sent to
ElevenLabs:

```
[deep thick growling wolf voice, badly pretending to be sweet] Սևուկ ուլիկ, …
```

`eleven_v3` treats a bracketed English phrase as an audio tag **sometimes**.
On other requests it reads it out. Measured across the 20 spans of the riddle
duel in «Խոսող ձուկը»:

| span | Armenian | direction | expected | actual |
|---|---|---|---|---|
| «- Ո՞վ է։» | 8 ch | 88 ch | 0.5 s | **4.3 s** |
| «- Հրեշը։ Եկել եմ…» | 47 ch | 76 ch | 3.1 s | 3.3 s |

The long spans absorbed the tag. The eight-character one spoke eighty-eight
characters of English first. A child would have heard *"a poor, gentle,
wondering man"* in the middle of a Tumanyan tale.

**The shape of this bug is the lesson.** It worked on most spans, which is why
it survived a demo; it failed on the shortest, which is the one too small to
hide it. An intermittent defect that correlates with brevity is invisible to
spot-checking.

It also invalidates an earlier claim of mine. When the owner said the first
wolf test's difference "was not significant", I attributed that to the model
under-acting. It was more likely reading the instruction instead of following
it.

## The rule now

**Only the story's own words are ever sent to TTS.** There is no safe way to
put anything else there, because whether it is spoken is decided per request.

Expression comes from two places that cannot be spoken:

- **`voiceSettings`** — `stability` / `style` / `similarity_boost`, API fields
  per speaker.
- **pitch and formant shift**, applied after the render with
  `asetrate`+`atempo`. Deterministic, measurable, repeatable. This is the half
  that was always working; the wolf at 0.88 was accepted on that basis.

The English descriptions were not deleted — they were renamed to
`directionForHumanNarrator` and kept as notes for a real storyteller, who can
read "sly old woman, honeyed and straight-faced" and act on it. The rename is
the point: nothing can now feed that field to a model by accident.

## The two guards

In `tools/story-voices/render_story.py`:

1. **Refuse before sending.** Any text containing `[`, `]`, or a run of three
   Latin letters aborts the render. Verified: both shapes are refused, real
   Armenian passes.
2. **Check the duration after.** A span more than 2.5× the length its Armenian
   implies (at 15 chars/second) fails the whole render rather than shipping.
   This is exactly the measurement that exposed the bug, so it now runs every
   time instead of only when someone happens to listen.

## Proof

Same 20 spans, re-rendered with the guards:

- «- Ո՞վ է։» — **0.7 s**, was 4.3 s.
- Every span within range of its Armenian.
- Whole segment 50.5 s, was 56.4 s. The six seconds were the English.
- `check_speaker_map.py` still passes: the story text was never touched.

## Why the check belongs in the pipeline

This is the second defect in this library that was invisible to every
structural check and obvious to a listener — the first was the truncation
found on 2026-08-10. Both were caught by comparing audio length against text
length. That comparison is now applied at two scales: per story
(`check_story_audio.py`) and per span (the renderer). Neither replaces the
human listen test, which is what found this one.

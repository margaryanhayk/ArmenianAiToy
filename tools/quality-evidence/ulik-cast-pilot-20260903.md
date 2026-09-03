# Ուլիկը cast pilot — one story, four voices (2026-09-03)

Owner decision, same day: characters get real voices instead of a
pitch-shifted Areg. Rules: the narrator is always Areg; Katrin and Vardan
take a part in every story; library (English) voices only for villains and
animals, because they speak Armenian with an accent.

## What was rendered

`tools/story-voices/render_story.py ulik` with the cast in
`backend/content/story-voices/ulik.voices.json` — all four on
`eleven_v3_conversational`, the model the owner preferred for both clones:

| Speaker | Voice | How chosen |
|---|---|---|
| narrator | areg-storyteller | rule |
| mother | katrin-v3 | owner's pick from all 34 account voices |
| ulik | vardan-v2, pitch 1.10 | owner's pick; lifted to sound small |
| wolf | **areg-wolf** `5WzpQqbTMzWBCHk0Mol1` | see below |

15 spans, every span inside the renderer's length and tail gates. Forest
beds and both door knocks mixed with `mix_ambience.py` from the per-segment
WAVs and a fresh forced alignment; levelled to -16.4 LUFS with ffmpeg
`loudnorm` for the listening copy. 2:23. Handed to the owner as
`ulik-pilot-final.mp3`. **Not shipped**: `story-audio/ulik.mp3` and its
marker are untouched.

## The wolf took four rounds

1. Callum (library, "husky trickster"): the owner called it compatible but
   too dramatic; three faster, flatter renders of the same voice — rejected.
2. Seven other library voices (Brian, Adam, Bill, Charlie, Harry, Roger,
   Terry): all rejected. The common fault is the English accent in Armenian,
   which no voice_settings change removes.
3. ElevenLabs' shared voice library has **zero** voices tagged `hy`.
4. **Voice Design** (`POST /v1/text-to-voice/design`, `eleven_ttv_v3`) from a
   description — "deep husky wolf pretending to be a gentle mother goat,
   speaks Armenian natively" — with the wolf's own Armenian line as the
   preview text. Three previews; the owner picked the first; saved to the
   account as `areg-wolf` (`POST /v1/text-to-voice`). No source person, so
   nothing to license and no accent inherited from one. This is the route
   for every future villain and animal.

Also offered, not taken: Areg and Vardan pitched down 15% (native, but
still recognisably them), and the owner recording the wolf himself.

## Known soft spot

The second knock («դուռը ծեծում», segment 4) anchored at 2.97 s into the
segment; the narrator's line ends at 2.48 s and the mother starts at 2.88 s,
so the cut for the inserted knock falls ~90 ms into her first word. The
forced alignment drifted badly over this render (-29.6 s by the end,
against -0.6 s per segment on the single-voice library), most likely the
designed wolf and the pitch-lifted Ulik confusing the aligner. The cast
renderer already writes an exact per-span map (`ulik.spans.json`); the
mixer should prefer it over alignment when a `landOn` phrase is the tail of
a span. Not fixed in this pilot — the owner listens first.

## Cost

Roughly 1,600 characters per full render, three full renders plus ~20
wolf auditions ≈ 7,000 characters. One forced alignment per render.

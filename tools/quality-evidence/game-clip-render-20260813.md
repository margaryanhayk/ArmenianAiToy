# The expressive re-render — 109 clips, and the games shipped

**Verdict: ACCEPTED by the owner, 2026-08-13.**

He heard the sample his own render note demands, then the rewritten greetings
and a further handful, and accepted. Recorded as a human verdict, not a tool's.

## What was rendered

| what | clips | voice |
|---|---|---|
| offline game clips | 90 | areg-storyteller, katrin-v3, vardan-v2 |
| alternate story endings | 10 | areg-storyteller |
| Ծիվիկ serial | 9 (6 episodes + intro, refrain, closing) | areg-storyteller |
| rewritten welcome greetings | 2 | areg-storyteller |

Areg keeps the same voice as the story narration and the welcome clips, so the
toy is one character across everything it says. The children are `katrin-v3`
and `vardan-v2` — invented characters, so there was nobody to ask.

Expression comes only from `voice_settings`: Areg a notch brisker in a game
than in a story, the children looser still because they are playing. **Never
from words in the text.** That rule is absolute and was bought on 2026-08-12,
when `eleven_v3` read an English stage direction aloud inside a Tumanyan tale.

## The sample came first, because his own render note says so

> render ONE «Քո կենդանին …» question, ONE guess clip, ONE sound-detective
> round and the buzzer win pair BEFORE batch-rendering — one bad pattern would
> poison a whole family.

It is not caution for its own sake: 16 guess clips share a sentence, three win
clips share a sentence, ten rounds share a shape, and «Ծիվի՛կ» opens all six
episodes. `--all` refuses to run without `--i-listened-to-the-sample`, which is
a promise a human makes and not a flag a script sets.

## Two defects the batch found

**The overrun guard was wrong on short clips, and it stopped the batch at 62 of
109.** «Մու-մու։» is eight characters, so `chars/15` expects half a second and
2.5× is 1.3 s — and the file is 1.4 s, because a child stretches an animal
sound. The render note says exactly that: *"the kid performers stretch them
naturally."* An overrun must now be **both** proportionally and absolutely
large (>2.5× **and** >3 s over), which still catches the case the guard exists
for: an 8-character line that came back **4.3 s** long when the model spoke a
stage direction.

**There was no resume.** A rendered clip is money already spent, and the retry
would have re-bought all 62. Fixed before resuming.

## Shipped

`tools/story-audio/apply_game_clips.py`, new — the games could not reuse the
welcome-clip shipper because **their identity is a pair**. Four of the five
games ship a clip called `intro`, so a `ClipId`-keyed patcher would quietly
write the mind-reader's intro over the buzzer's. Pinned by a self-test that
patches one of two identically-named clips and asserts the other is untouched.

- 90 speech clips installed, `Version` 1 → 2 on each, so field toys re-download.
- Every sha256 and size re-computed **from the installed bytes**, then verified
  against the file on disk afterwards: 90 of 90 match.
- `button-simon/tone-green` and `tone-red` left untouched — they are pure
  tones, not speech, never in the text file, and re-rendering them would have
  been a category error.
- It refuses a partial job by default: half a set means the toy plays the new
  performance on one clip and the old one on the next, inside a single game.

`dotnet test` 2554 green.

## What is NOT done

- **The endings and the serial have no manifest entries at all.** They are
  rendered and sitting in the render directory; getting them onto a toy needs
  ContentSync work that does not exist yet (alternate endings hang off
  `AltOf`, the serial off `SeriesId`).
- **Nobody has heard these in sequence.** The games have never been bench-run —
  they need the two push buttons. A clip that is right on its own can still be
  wrong after the one before it.
- **The twelve new Katrin and Vardan lines are not rendered**, by design. They
  are marked `"new"` and the renderer's self-test asserts no pending line can
  enter a render.

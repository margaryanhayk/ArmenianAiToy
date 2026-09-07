# Cast pilots v1 — every story (2026-09-07)

Owner: "Do it for all stories, and just send all" after «Անբան Հուռին» v1. One listening
copy per story, sent in this order; nothing shipped. Each is: cast render
(`render_story.py`), per-span forced alignment, ambience mix where cues exist,
two-pass loudnorm to −16.4 LUFS at 192 kbps, the story's summary clip appended.
The maps in `backend/content/story-voices/` carry the cast and a `castNote` per role.

| # | story | narration | LUFS | cast | cues |
|---|---|---|---|---|---|
| 1 | anban-huri | 4:10 | -16.7 | huri=katrin-v3, frogs=vardan-v2 @0.9, husband=areg-storyteller @0.96, mother_in_law=katrin-v3 (shift @0.92 | 5 |
| 2 | princess-and-pea | 1:08 | -16.8 | queen=katrin-v3 @0.92 | 4 |
| 3 | little-cloud | 0:21 | -16.7 | flower=katrin-v3 @1.08 | 0 |
| 4 | sutlik-orskan | 2:33 | -16.6 | boaster=areg-storyteller, companion=vardan-v2 @0.97 | 3 |
| 5 | sutasan | 1:30 | -16.7 | king=areg-storyteller @0.95, shepherd=vardan-v2 @0.95, tailor=areg-storyteller @1.02, peasant=vardan-v2 | 2 |
| 6 | pochat-aghves | 4:25 | -16.6 | fox=vardan-v2, old_woman=katrin-v3 @0.92, cow=areg-storyteller @0.88, field=areg-storyteller @0.95, spring=katrin-v3 @1.1, girl=katrin-v3, pedlar=areg-storyteller, hen=katrin-v3 @1.12 | 3 |
| 7 | khosogh-dzuk | 6:29 | -16.6 | poor_man=areg-storyteller, fish=katrin-v3, fisherman=areg-storyteller @0.93, monster=areg-wolf, guest=vardan-v2 | 6 |
| 8 | three-piglets | 1:24 | -16.8 | narration only | 5 |
| 9 | hedgehog-apple | 0:24 | -16.7 | narration only | 0 |

Casting rules applied: Areg narrates on `eleven_v3`; characters on `eleven_v3_conversational`;
Katrin and Vardan wherever a role fits a girl or a boy; Areg on the dialogue model, shifted,
for grown men; Katrin at 0.92 for every old woman (the mother-in-law, the old queen, the
պառավ) so one verdict covers all three; `areg-wolf` for the monster in «Խոսող ձուկը» (villain).
Vardan is never lifted (rejected 2026-09-04). Every take floor −43..−66 dBFS; no clone noise.

## What the render guard learned on this batch (all in `render_story.py`)

- The transcriber respells dialect and names one letter off per word, runs short words together
  («Զենետալի») and splits long ones («մոր քուրի»); it also returned the first four words of a
  21 s take five times in nine. Each of these refused a correct take up to three times and
  would have failed the story. Now: a respelled word is the same word; a letters-only tie-break
  with a small length slack (an added word adds letters, so «shshsh»/«hmm» still fail); a
  truncated transcription is re-asked; the refusal line prints the full numbers.
- The pitch guard's reference is the speaker's FIRST take; on «Խոսող ձուկը» that take was
  the outlier three times over. When the retries agree with each other and not with the
  reference, the reference is re-based.
- A resumed run times its kept segments from disk and writes the span map; an empty TTS
  response is asked for once more; NOISY is not flagged on takes under 2.5 s.

## Not done

- The owner has heard none of these. Nothing ships until he has.
- «Փոքրիկ ամպիկը» and «Ոզնիկն ու խնձորը» have no ambience cues yet.
- The OpenAI-TTS stream caches are untouched.

# Who says what — speaker maps

One file per story marking **every word** with who speaks it: the narrator, the
wolf, the frogs, the mother. Nothing at runtime reads this folder yet; it is
the instruction sheet for rendering narration, and it is deliberately
narrator-independent so it survives a change of voice.

## Why it exists

«Ուլիկը» turns on the wolf saying the mother's exact words in a different
voice. The text says so three times — «իր հաստ ձայնով», «Քո ձայնը կոշտ է ու
կոպիտ», «Էնպե՜ս հաստ ձայն ունե՜ր» — and the 2026-08-11 narration says all of
it in one voice. A child is told a difference exists and cannot hear it.

That is not one bug in one story. «Խոսող ձուկը» ends in a riddle duel where the
monster and the guest alternate **with no attribution at all** — a renderer has
no way to tell them apart without being told. «Պոչատ աղվեսը» asks eight
different characters the same long question. Marking it once, properly, means
the next render gets it right the first time instead of rediscovering it.

## The files

| story | spans | voices | characters |
|---|---|---|---|
| Անբան Հուռին | 31 | 5 | huri, frogs, husband, mother_in_law |
| Ոզնիկն ու խնձորը | 3 | 1 | — narration only |
| Խոսող ձուկը | 71 | 6 | poor_man, fish, fisherman, monster, guest |
| Փոքրիկ ամպիկը | 5 | 2 | flower |
| Պոչատ աղվեսը | 30 | 9 | fox, old_woman, cow, field, spring, girl, pedlar, hen |
| Արքայադուստրը և սիսեռահատիկը | 7 | 2 | queen |
| Սուտասանը | 25 | 5 | king, shepherd, tailor, peasant |
| Սուտլիկ որսկանը | 18 | 3 | boaster, companion |
| Երեք խոզուկները | 6 | 1 | — narration only |
| Ուլիկը | 15 | 4 | mother, wolf, ulik |

**211 spans across 56 segments and 18,738 characters of Armenian.**

## The rules used

**Only DIRECT speech gets a character.** Reported speech — «Նա ասաց, որ իսկական
արքայադուստր է» — stays narrator, because that is the narrator speaking about
her, not her speaking. Three stories turn out to have no direct speech at all,
and that is recorded rather than left blank, so a later pass knows it was
checked and not skipped.

**A story's teller can be a character.** «Սուտլիկ որսկանը» is a tall tale in
the first person: its "narrator" is the boaster, and he must never wink.

**Repetition can share one voice on purpose.** The four companions in the same
story answer as one flat chorus; distinguishing them would slow the run of
gags. In «Պոչատ աղվեսը» the opposite is true — eight separate colours, because
the fox's words barely change and the answerer is the only thing that does.

## The one hard invariant

`tools/story-voices/check_speaker_map.py` joins each segment's spans and
requires the result to equal the story text **exactly**. The story texts are
adapted Tumanyan and are approved; a map that silently dropped a comma would
put unreviewed text in a child's ear. The checker needs no ffmpeg, no dotnet
and no network, so it runs on the day it matters.

## Status

**DRAFT.** The attribution is mechanical and verifiable; the *directions* and
pitches are my proposals and want the owner's ear. Nothing has been rendered
from this file yet.

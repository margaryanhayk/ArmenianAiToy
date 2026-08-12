# Ambience across the library — eight of ten stories

**Date:** 2026-08-12
**Prompted by:** the owner accepting «Ուլիկը» — "acceptable for now"

## What shipped

Eight of the ten stories now carry ambience. `hedgehog-apple` and
`little-cloud` deliberately do not — the owner's decision, 25 and 21 seconds
long, too short to dress.

| story | cues mixed | held | Version |
|---|---|---|---|
| anban-huri | 4 scene | — | 9 |
| khosogh-dzuk | 3 scene | 2 | 9 |
| pochat-aghves | 3 scene | — | 9 |
| princess-and-pea | 2 scene | 1 | 5 |
| sutasan | 1 scene | 1 | 5 |
| sutlik-orskan | 3 scene | — | 9 |
| three-piglets | 4 scene | 1 | 5 |
| ulik | 2 scene + 2 inserted knocks | — | 11 |

**Three are complete and final**: «Անբան Հուռին», «Պոչատ աղվեսը» and
«Սուտլիկ որսկանը» carry only scene cues, and a scene cue anchored to a segment
start has never been the thing that went wrong.

## 26 sounds, all generated, none shared between stories

`tools/story-ambience/generate_sounds.py` makes one file per **(story, sound)**
pair. 18 sound ids used by 29 cues resolve to 26 pairs, so each story gets its
own forest, its own wind, its own river — the repetition the owner objected to
is structurally impossible. Verified: **zero duplicate files across the 26.**

Within one story a repeated sound stays one file, which three cue notes
require: «Ուլիկը»'s two knocks are the same door, «Անբան Հուռին» returns to the
same river, «Սուտլիկ որսկանը» walks one road twice.

Beds are 10 s so a `holdUnder` loop is inaudible; one-shots 4 s. Prompts are
reviewed English in the cue sheet with per-cue `avoid` clauses, recorded beside
the audio in `prompts.json`. The audio is committed: generation is paid and
non-deterministic and would not come back the same.

## Five cues are HELD, and that is a statement, not an omission

| story | cue | what its note demands |
|---|---|---|
| khosogh-dzuk | water-splash | "must land exactly on «գցում գետը» and nowhere else" |
| khosogh-dzuk | door-knock | "the tensest moment in the library" |
| princess-and-pea | door-knock | "on «թակեցին», with the rain still under it" |
| three-piglets | wind-gust | "on the first blow only, not on all three" |
| sutasan | thunder-distant | thunder inside a lie — a joke, not a storm |

Each needs to know when a particular word is spoken. **The ElevenLabs key is
missing the `forced_alignment` permission** (and `speech_to_text`), so it cannot
be measured, and it is not going to be estimated a third time:

- attempt 1 anchored to the segment start — **5.6 s early**, the owner heard it;
- attempt 2 estimated the span boundary and snapped to the nearest pause —
  **2.4 s late**, the owner heard that too;
- attempt 3, written and discarded before this pass: fit a per-story timing
  model (chars, commas, sentence marks) by least squares against the exact
  segment map. It returned **negative pause coefficients** — a comma making
  speech shorter — with residuals up to 1.9 s, and still missed the owner's
  correction by 1.8 s.

So a `held` cue is skipped by the mixer, printed on every run, and recorded in
the story's `.ambience.json` marker. Mixing it at the segment start instead
would be exactly the placement already rejected twice.

`tools/story-audio/align_story.py` is written and self-tested — ten checks,
including that **all 29 cueLines in the library are findable in their story
text**, so nothing can silently fall back to estimating. One permission toggle
runs it.

## Verification

- Every self-test PASS: `mix_ambience.py`, `generate_sounds.py`,
  `align_story.py`, `check_speaker_map.py`.
- Dry run per story before spending: **no collision warnings** anywhere.
- `check_story_audio.py` — 10 of 10.
- Loudness `-16.6` to `-16.7` LUFS against the library's -16.4 contract,
  192 kbps, one ID3 tag each.
- Every one of the seven **changed sha256** — checked against `git show HEAD:`,
  because the file SIZES are identical (CBR MP3, unchanged duration, no
  insertions in this pass) and a silent no-op would otherwise look like success.
- Eight byte maps regenerated after the re-encode; ascending, starting at 45,
  ending inside their file. Manifest sizes match the bytes on disk.
- `.ambience.json` marker present on all eight; a second mix is refused.
- `dotnet test` 2554 — no C# and no story text touched.

## What is NOT proven

Nobody has heard any of these seven. The one thing that matters — whether a
river under «Խոսող ձուկը» makes it warmer or just noisier — is an ear question,
and the last three rounds are why no tool's verdict is offered here.

Worth listening for specifically:
- **«Սուտասանը» now has one cue in the whole story** (a hall murmur), because
  its thunder is held. It may read as under-dressed rather than sparse.
- **«Խոսող ձուկը» is 5:35 with three cues**, and its two most important sounds
  are the held ones. The river establishes and then nothing happens for four
  minutes.
- **«Երեք խոզուկները» marks the seasons** — summer, autumn, first rain,
  spring — which is the most cue-dense story in the library and the most likely
  to feel busy.

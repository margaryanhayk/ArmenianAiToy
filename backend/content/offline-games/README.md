# Offline games — clip texts (owner-reviewed)

Armenian TEXT source for five new offline SD-card games. **Nothing at
runtime reads this folder** — same convention as `../quiz-questions/` and
`../voice-clips/`: the Armenian lives here so it is reviewable in one place
and a diff shows honestly what changed. The firmware engine for
these games shipped 2026-08-07 (`esp32/AregVoiceMvp/offline_games.{h,cpp}`,
bench-flagged, not yet hardware-run); text came first so the owner reviewed
words, not renders.

**Status: OWNER-REVIEWED 2026-08-07.** armenian-story-master authored
2026-08-06; the owner then rewrote 34 lines himself (frog «կռկռ-կռկռ» over the
Russian calque, goose «ղա-ղա» reassigned, one mind-reader branch re-cut with
its leaves swapped) and approved the 12 buzzer variety lines. Remaining gates:
the sample-first listen test (see `_renderNote` in the JSON), then the render.

## The five games and their id schemes

| game key | ids | notes |
|---|---|---|
| `mind-reader` | `intro`, `q-<path>`, `g-<4 bits>`, `win`, `lose`, `replay` | Child thinks of one of 16 animals; the toy walks a 4-deep yes/no tree and guesses. **GREEN/yes appends `1`, RED/no appends `0`**; a question node's id is the answer path so far (`q-root`, `q-0`, `q-1`, `q-00` … `q-111`), a guess leaf's id is the full 4-bit path (`g-0000` … `g-1111`). 15 questions + 16 guesses. |
| `who-first` | `intro`, `go` + `go-2..4`, `win-green` + `win-green-2..3`, `win-red` + `win-red-2..3`, `between-1..5`, `end-both`, `close` | Two-player buzzer. Round lines name only the winning COLOR; no clip names a loser, ever. The go / win-green / win-red families rotate per the vardan-katrin `feedbackVariants` convention (the UNNUMBERED clip is variant 1 of its family; the firmware cursor walks the family so replays never repeat). `between-N` are optional between-round energy lines and deliberately never contain the trigger word «հիմա» — only go-family clips may say it. All go clips end in the byte-identical trigger «Հիմա՛։»; all win variants of a color end in the byte-identical «Ապրե՛ս, <color>։» — render once, splice. |
| `sound-detective` | `intro`, `rNN-sound`, `rNN-ask` | Katrin/Vardan alternate making an animal sound with the mouth; Areg asks who it was and speaks the green/red mapping. `rNN-ask` carries `answer: green|red` → renders to the quiz-style `-y`/`-n` filename suffix, verified by the existing quiz engine. Feedback **reuses the quiz clips verbatim** (`win`/`wrong`/`done`) — no new feedback audio. |
| `button-simon` | `intro`, `your-turn`, `level-up-1..3`, `miss`, `best`, `done` | Tone-sequence memory game; every claim in the clips is derived from measured button presses. |
| `story-pauses` | `shout-1..4`, `resume-1..4` | Generic mid-story shout inserts + resumes. The mic is NOT listening during these — no resume may claim to have heard or understood anything. |

## The answer lives in the filename (where there is one)

`sound-detective` follows the quiz contract exactly: `r02-ask` with
`answer: red` renders to `r02-n.mp3`. Editing an `answer` after render means
the suffix must change too — don't edit answers; retire the round id and add
a new one. `mind-reader` guesses need no answer field: GREEN after a guess
routes to `win`, RED to `lose`, by flow.

Note `r02-ask` and `r10-ask` are deliberately the SAME text («գայլը՝ կանաչ,
թե՞ շունը՝ կարմիր») with OPPOSITE answers — bark vs howl. One render, two
filenames.

## TTS / product rules these texts already obey

All from owner listen tests — violating any means a re-render:

1. **Full «-ը» article before vowel-initial next words** (eleven_v3
   swallows the euphonic «ն»): «Կանաչը առաջինը», «Խաղը ավարտվեց» are
   deliberate, not typos. Junctions where grammar forces «ն» (ի-stem
   «կենդանին», pre-«է» positions) are in `_watchWords` — listen for the ն
   at sample time.
2. **Onomatopoeia = bare hyphenated pairs** («հաֆ-հաֆ», «մու-մու»), no
   stress marks inside; the kid performers stretch them naturally.
3. **Repeated lines are word-for-word identical** — render once, trim the
   tail once, splice. The full identical-line list is in `_renderNote`.
   Three lines are byte-identical to already-shipped audio (quiz
   `win`/`wrong`/`done`, the library resume line «Ուրեմն, շարունակում ենք
   հեքիաթը։») — reuse those renders, do not re-record.
4. **Colors, never names; never a loser.** The toy addresses «կանա՛չ» and
   «կարմի՛ր», and no clip announces who lost.
5. **Honesty.** The toy claims only what buttons measured («Ես հաշվեցի ձեր
   սեղմումները»). These games are offline: the mic is off, so nothing may
   claim to have heard the child — the story-pause resumes are theatrical
   affirmations that work identically over an answer or silence.

## Open flags for the owner

- `mind-reader` has no spoken title — the working name «Ո՞վ եմ ես» no
  longer matches the final mechanic (the toy guesses the CHILD's animal).
  Menu name is a free decision, zero re-render cost.
- `sound-detective` r05 (ոչխար «բե-բե» vs այծ «մե-մե»): confirm the բ/մ
  split matches how the kids actually perform the sounds BEFORE the answer
  suffix is fixed.
- `story-pauses` timing (length of the silent shout beat) is a firmware
  knob, not a text question.

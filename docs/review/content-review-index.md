# Content waiting for the owner's read

Companion to `docs/review/content-review.html` (open that on a phone).
Generated from the repo as it stands. **No text was edited** — this is packaging only.

**158 items in 4 batches, ~20 minutes of reading.**
Answer format: reply with the item numbers, e.g. `1-26 yes, 41 no`.
There is no form and nothing is saved by the page — the numbers come back in a message.

## Batches open for review

| # | Batch | Items | Numbers | Read | Source file |
|---|-------|-------|---------|------|-------------|
| 1 | Վարդանն ու Կատրինը — the judging game | 26 | 1-26 | ~2 min | `backend/content/vardan-katrin-games/rounds.json` |
| 2 | The offline quiz — 50 questions | 53 | 27-79 | ~2 min | `backend/content/quiz-questions/quiz-questions.json` |
| 3 | The lessons and after-story questions on the 10 shipped stories | 70 | 80-149 | ~4 min | `backend/src/ArmenianAiToy.Application/Stories/Content/*.story.json` |
| 4 | Ծիվիկի մեծ ճանապարհը — the six-episode serial | 9 | 150-158 | ~12 min | `backend/content/serial-hero/tsivik-series.json` |

## What a yes unlocks

### 1. Վարդանն ու Կատրինը — the judging game (items 1-26)

Vardan claims something, Katrin contradicts him, Areg asks the child to judge with the buttons. Nothing is rendered yet - these are the words only. Balance is 5 rounds Vardan-right, 5 Katrin-right, so green is not always the wrong button.

**Yes unlocks:** A yes lets us render one sample round in the kids' voices for your listen test, then the whole set.

### 2. The offline quiz — 50 questions (items 27-79)

The three-button game the toy can play with no internet at all. The answer lives in the rendered file name, so the toy really checks the press - no cloud, no AI. q01-q20 are true/false; q21-q50 each speak their own green/red meaning, which is how the same two buttons also become counting, first-sound and comparison games.

**Yes unlocks:** A yes lets us render one sample question plus the three feedback lines for your listen test, then the other 49.

### 3. The lessons and after-story questions on the 10 shipped stories (items 80-149)

The stories themselves are approved and already on the toy - none of that text is here. What is here is the newer layer around them: the one-line lesson, the goal shown to parents, and the two extra questions (plus all three takeaways) the toy uses for the after-story talk. The original first question of each story is already approved and is shown greyed, for context only.

**Yes unlocks:** A yes lets these be rendered as the per-story summary and question clips, which is what makes the after-story talk work offline.

### 4. Ծիվիկի մեծ ճանապարհը — the six-episode serial (items 150-158)

An original hero, not folklore and not Katrin or Vardan. Tsivik is a small swallow who wants to see the sea with his own eyes; each episode is one day of the journey and stands on its own. This is the longest read on the page - about a story and a half of text. The chant is what a child will repeat, so it is worth reading that one twice.

**Yes unlocks:** A yes lets us render the chant plus episode one as a sample for your listen test, then the other five.

## Already answered — not on the page as work

- **The 10 alternate story endings** (`backend/content/variant-endings/`) — You reviewed these on 7 August - you rewrote 7 of them yourself, kept anban-huri as drafted, and picked option A for sutasan. Nothing left to read. What is left is the audio render and the listen test.
- **The 90 offline-game clips (mind-reader, buzzer, sound game, Simon, story pauses)** (`backend/content/offline-games/`) — You made your own edit pass on these on 7 August - 34 lines in your wording - and they are already rendered and on the toy for bench testing. The remaining gate is the expressive re-render before launch, not the words. One question inside them is still open, and it is written down under “Problems found” in docs/review/content-review-index.md.
- **The 43 welcome greetings and menu lines** (`backend/content/voice-clips/`) — These are your own words - your set of greetings, cut down to 39 plus the menu lines. They are rendered and shipped. Only the listen test is open.
- **The 10 story texts themselves** (`backend/src/.../Stories/Content/`) — All approved and on the toy. No story text is on this page.

## Problems found (not fixed — a human decides)

1. **`backend/content/offline-games/game-clips.json` — the file's own status line is stale.**
   Its `_comment` still reads *"DRAFTS pending owner review + listen test"*, but the owner's
   own edit pass landed in commit `86ae80d` (34 lines in his wording) and the clips are rendered
   and synced to the toy. The 90 clip texts are therefore NOT in the reading pile above. If the
   status line is right and the review never happened, this whole batch has to be added back.

2. **An open owner question is buried inside an already-rendered clip.**
   `offline-games/game-clips.json`, clip `r05-ask` of *sound-detective*, carries the note:
   *"Flag for owner: confirm the բե/մե split matches how the kids actually perform it BEFORE
   fixing the answer suffix."* This is a question about which button is correct (sheep vs goat),
   and it is sitting in a batch nobody is reading any more. If it is wrong, the offline game
   marks a right answer as wrong.

3. **`backend/content/vardan-katrin-games/rounds.json` carries two overlapping reaction sets.**
   `feedback` (6 lines) and `feedbackVariants` (15 lines) describe the same moments. Four lines
   are byte-identical across the two blocks; two differ only by the `-ն`/`-ը` article fix from
   the linguistic review (`Կատրինն էր ճիշտ` vs `Կատրինը ճիշտ էր`), i.e. the older block was not
   updated. Only `done-areg` exists in `feedback` alone. The page reads the 15 variants + that one
   closing line and skips the older six, but the file should eventually lose the dead block, or
   the render will pick the un-corrected wording.

4. **The mind-reader game has no spoken name.**
   The same file records that the working title «Ո՞վ եմ ես» is now backwards (the child thinks of
   the animal, the toy guesses), no clip speaks a title, and so the menu name is an open owner
   decision with no re-render cost. Not a text to approve — a name to pick.

5. **Nothing broken was found in the four batches on the page.**
   No mojibake, no truncated line, no accidental duplicate. The one repeated line inside the
   Vardan/Katrin rounds (Areg's judging sentence, identical in all 10) and the repeated ask line
   in the sound game (`r02-ask` / `r10-ask`) are both deliberate and documented as render-once-
   and-splice.

## Method / honesty notes

- Items were counted as **one thing the owner can say yes or no to**, not one JSON field:
  a Vardan/Katrin round is one item because it renders as one MP3; a story's goal, lesson,
  two new questions and three takeaways are seven items because each is an independent line.
- `backend/content/story-drafts/` is **empty** (README only) — there is no unpromoted story draft.
- The 10 story texts themselves, the 43 welcome clips, the 10 variant endings and the 90
  offline-game clips are **not** in the reading pile; the reason for each is listed above.
- Reading times assume 150 Armenian words a minute and are rounded up.
- Reflection question 1 of every story is already approved and is shown greyed for context;
  it is not numbered and is not up for review.

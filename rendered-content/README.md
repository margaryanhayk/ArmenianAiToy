# Rendered SD-card content (durable home)

The rendered MP3 sets for the toy's SD card. Rescued out of a session
temp directory 2026-08-06 (weak-point audit Tier 1: a Windows temp sweep
would have destroyed the only copies).

> **CORRECTION 2026-08-10 — this header used to say "owner-approved,
> listen-tested". For `quiz/` and `vk-games/` that was NOT TRUE.** Both
> source files still read `DRAFTS` in their own `_comment`
> (`backend/content/quiz-questions/quiz-questions.json`,
> `backend/content/vardan-katrin-games/rounds.json`), both carry a
> `_renderNote` requiring ONE sample to be listen-tested *before* batch
> rendering — the batch was rendered anyway — and
> `docs/review/content-review-index.md` still lists both as open for the
> owner's read (items 1–26 and 27–79). `voice/` and `offline-games/` did
> get an owner pass; those two did not.
>
> This is the same failure pattern as the story-audio truncation found the
> same day: a document asserting a quality gate that was never run. See
> `tools/quality-evidence/story-audio-truncation-20260810.md`. The
> "NOT the launch set" note further down was always correct and is the
> line to trust.

| Folder | Files | SD destination | Contract |
|---|---|---|---|
| `quiz/` | 53 | `/quiz/` | qNN-**y**/-**n** suffix IS the answer key (GREEN/RED button). Never rename; answer changes = new id. |
| `voice/` | 43 | `/voice/` | Welcome-flow clips; ids per `backend/content/voice-clips/`. |
| `vk-games/` | 16 | `/quiz/` | Vardan-vs-Katrin rounds (vkNN-y/-n) + reaction clips. |
| `render-meta/` | — | — | Judge-question variant texts + bake-off lines (provenance). |

Render rules baked into these files (owner's ear, 2026-08-05/06):
full `-ը` article before vowels (TTS swallows euphonic ն); every take
tail-trimmed 0.45s (inhale removal); onomatopoeia as bare hyphenated
pairs; repeated lines rendered once and spliced.

Source texts: `backend/content/{quiz-questions,voice-clips,vardan-katrin-games}/`.
Re-render pipeline: eleven_v3, voices areg-storyteller / katrin-v3 /
vardan-v2 (ids in the project memory).

## Offline-game renders (2026-08-07)

| Folder | Files | SD destination | Note |
|---|---|---|---|
| `offline-games/<game>/` | 90 | `/games/<game>/` | Subfoldered because four games each define a clip called `intro` — the firmware resolves `/games/<game-key>/<id>.mp3`. |
| `vk-games/` (+15 new) | 31 | `/quiz/` | Vardan-vs-Katrin reaction VARIANTS (`win-vardan-1..3` etc.) — the firmware rotates them so a reaction never repeats twice running. |

Rendered from the owner-reviewed texts in
`backend/content/{offline-games,vardan-katrin-games}/`, eleven_v3,
voices areg-storyteller / katrin-v3 / vardan-v2. All 105 verified as
real MP3s (ID3/frame header + size floor); zero failures.

> **NOT the launch set (owner decision 2026-08-07).** These are
> accepted as a working library so the toy can be bench-tested. Before
> the first families get toys, every child-facing clip gets an
> EXPRESSIVE re-render — emotional delivery, not merely correct
> Armenian — followed by a full listen test. Do not treat these files
> as final.

Two clips the firmware wants that are NOT here yet: Simon's two tone
sounds (`tone-green` / `tone-red`) — non-verbal, no Armenian to review.

## KNOWN DEFECT — none of the 31 `vk-games/` clips can ever play (2026-08-10)

`offline_quiz.cpp:find_question()` probes `/quiz/q%02d-{y,n}.mp3` **first**,
then `/quiz/vk%02d-{y,n}.mp3`, using **one shared index space**. The rendered
set is `q01`–`q50` (every slot filled) and `vk01`–`vk10`, so for every n in
1..10 the `q` probe matches and returns before the `vk` probe is reached.
`is_vk` can therefore never become true, and `play_vk_feedback()` is dead code.

Putting these files on a card changes nothing — the Vardan-vs-Katrin game has
never been reachable. Fixing it means giving `vk` its own index range or its
own scan loop, and that belongs in the slice that actually revives the game and
bench-tests it, not in a blind edit to code behind a build flag
(`AREG_OFFLINE_QUIZ_BENCH`) that is **defined nowhere in this repo**.

Two more things that slice will need to know: the quiz is not on a button at
all — it auto-fires 30 s after boot from the IDLE loop — and `kMaxQuestions` is
60, so 50 quiz + 10 VK rounds sit exactly at the ceiling.

Also unused: `vk-games/done-areg.mp3` has no consumer; the firmware plays
`/quiz/done.mp3` from the quiz set.

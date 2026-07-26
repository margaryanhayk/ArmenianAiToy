# anban-huri — production-voice TTS listen test — 2026-07-27

**Human verdict: PASS.**

This is the completed record of the human listen-test gate for
`backend/content/story-drafts/anban-huri.story.json` («Անբան Հուռին»).
It is derived from the blank checklist
`anban-huri-listen-test-TEMPLATE.md`, which remains in place for reuse.

## Provenance of this record — read before citing it

The owner supplied **one** thing: the overall verdict `PASS`, dated
`2026-07-27`.

No per-clip verdicts, timestamps, quotations, ratings, playback-hardware
details or wording corrections were supplied, and **none have been
invented here**. Every line below is one of exactly three kinds, and
they are labelled:

- **[HUMAN]** — supplied by the owner.
- **[REPO]** — verified mechanically from the repository by an agent.
- **[NOT SUPPLIED]** — the template asked for it; the owner did not
  provide it. It is recorded as absent, not filled in.

## Verdict

| Item | Value | Source |
|---|---|---|
| Review date | 2026-07-27 | [HUMAN] |
| Overall production-voice listen test | **PASS** | [HUMAN] |
| Overall child-ready listen result | **PASS** | [HUMAN] |
| Listening defects reported | **None** | [HUMAN] |
| Re-render requested on the basis of this test | **No** | [HUMAN] |
| Reviewer identity | [NOT SUPPLIED] | — |
| Playback path (headphones vs toy speaker) | [NOT SUPPLIED] | — |
| Per-clip verdicts / timestamps / quotations | [NOT SUPPLIED] | — |

## Audio in scope

The template presented these four files as the mandatory review
material, and this record treats them as the scope the overall PASS
covers. **[REPO]** — all four already existed; nothing was re-rendered
for this test, so no OpenAI spend was incurred.

| File | Covers | Size |
|---|---|---|
| `backend/src/ArmenianAiToy.Api/story-audio-cache/anban-huri.mp3` | all 9 segments, one continuous narration (~3:52, 160 kbps) | 4,654,560 B |
| `…/story-audio-cache/clips/anban-huri--announce.mp3` | title «Անբան Հուռին» | 10,080 B |
| `…/story-audio-cache/clips/anban-huri--conclusion.mp3` | `reflectionText` | 135,360 B |
| `…/story-audio-cache/clips/anban-huri--question-0.mp3` | `reflectionQuestions[0]` | 40,800 B |

**[REPO] Render currency.** Produced 2026-06-18/19 by the production
`OpenAITtsSynthesisService` (model `tts-1`, voice **Nova**, MP3). The
draft's last commit is `ce9318b` (2026-06-12) and its working tree was
clean at review time, so the audio post-dates the frozen text and is the
current rendering of it.

## Watchpoints in scope

The template listed these as the mandatory review points, taken from the
draft's own `review.notes`. They were therefore **within the scope of the
overall PASS**. This record does **not** claim any of them received an
individually-recorded human verdict — none was supplied.

| Watchpoint | Where it occurs | Clip | Human detail |
|---|---|---|---|
| «Հուռու» | `reflectionText` only | `conclusion.mp3` | No defect reported |
| «զվարճալի» | `reflectionText` only | `conclusion.mp3` | No defect reported |
| «մանեց» (dialect stress) | segment 3, ~1:01 | full narration | No defect reported |
| «Հուռի» | segments 0, 1, 3, 4 + question | narration, `question-0.mp3` | No defect reported |
| Prosody: «՛», «՞», «և», ն-liaison, «՝», comma beat, «։» | throughout | all clips | No defect reported |

## Evidence note

> The human owner reported the overall production-voice listen test as
> PASS. No individual defect, timestamp, or required wording correction
> was reported. This record does not fabricate additional observations
> beyond that verdict.

## What this PASS closes, and what it does not

**[REPO] Closes:** pipeline step 4 in
`backend/content/story-drafts/README.md` — "TTS listen test on the
production voice". This document is that step's evidence.

**[REPO] `review.listenTestAt` is deliberately still `null`.** Stamping
it now was attempted and then reverted, because the repository owns that
field at *promotion* time, not at listen-test time:

- `README.md` pipeline step 5: "Human approval → move to
  `Stories/Content/` → status `approved` → `linguisticReviewAt` /
  `listenTestAt` stamped → commit." The stamp is part of the promotion
  act, bundled with the move and the status flip.
- The draft's own `review.notes` say the same: "then human promotion to
  Stories/Content with status approved **and stamped dates**".
- `LibraryStoryQuestionTests.AnbanHuri_IsAProductStoryCandidate_NotSourceOnly`
  asserts BOTH dates are null while the file sits in `story-drafts`,
  commenting: "dates must never be pre-stamped on an unapproved draft".
  A trial stamp failed that test (1 failed / 2053 passed); reverting
  restored 2054/2054 and the draft file to its exact committed hash
  `761b9e2a…`.

So the audio gate is **closed by this record**, and the date field will
be stamped by whoever performs the promotion. Weakening or editing that
test to allow an early stamp was rejected — it is an approval invariant,
not an inconvenience.

**[REPO] Does NOT close, and was deliberately left untouched:**

- `review.status` — still `"draft"`. Promotion is a human act
  (`README.md`: "Approval means MOVING the file to Stories/Content/, not
  flipping status in place").
- `review.linguisticReviewAt` — still `null`. That is pipeline step 2
  (armenian-story-master), a separate gate; a listen test does not own it.
- `verificationStatus: pending` — edition/orthography source verification
  for the Tumanyan retelling. Note that this is **prose inside
  `review.notes`, not a schema field**, so nothing was or could be "set"
  for it. It is an open owner decision and is untouched.
- The runtime library is unchanged: the story is still absent from
  `backend/src/ArmenianAiToy.Application/Stories/Content/`, so it remains
  unservable to children by construction.

**[REPO] Asset status:** the full narration and all three clips exist
(above). The story is **not** wired into `ContentSync` in shipped config
(`appsettings.json` ships `ContentSync:Enabled=false`), so nothing is
served to a device from this record either.

**[REPO] SHIP A6 unaffected.** A6 additionally requires a third approved
story with an SD-wired MP3, a real three-item sync, and a hardware run
showing selection, no back-to-back repeats, reboot persistence, and
pause/resume staying on one story. None of that is closed here.

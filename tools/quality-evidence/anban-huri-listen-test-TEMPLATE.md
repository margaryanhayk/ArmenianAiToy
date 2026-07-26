# anban-huri — production-voice TTS listen test

**Status: NOT YET PERFORMED.** This file is a blank template. The
listening is the acceptance gate and it is a human act — an agent must
not fill in the verdicts, and must not stamp `listenTestAt` /
`linguisticReviewAt` in the draft. Rename this file to
`anban-huri-listen-test-<YYYYMMDD>.md` when you record a real pass.

## What is under test

`backend/content/story-drafts/anban-huri.story.json` — «Անբան Հուռին»,
9 segments, `review.status: "draft"`, `bedtimeSafe: false`.

Story segments are **not to be edited during a listen test**. A word the
voice mangles is fixed by re-rendering or by changing spoken *metadata*,
never by editing the tale text. If the text itself turns out to be
unspeakable, that is an owner decision, not a fix.

> **Note on `anban-huri` specifically** (corrected 2026-07-27): its text
> is **adapted, not byte-frozen**. Source verification found mixed
> dialect/standard forms against Թումանյան, *Երկերի լիակատար ժողովածու*
> հ.5, pp. 226–228; the owner accepted them as the v1 product text. Do
> not repeat the "byte-frozen" description for this story. See
> `anban-huri-source-verification-20260727.md`.

## Audio (already rendered — no new OpenAI spend needed)

Rendered 2026-06-18/19 via the production `OpenAITtsSynthesisService`
(model `tts-1`, voice **Nova**, MP3), from the draft as it stands today.
The draft's last commit is `ce9318b` (2026-06-12) and its working tree is
clean, so the audio is **current** for the frozen text.

| File | Covers | Size |
|---|---|---|
| `backend/src/ArmenianAiToy.Api/story-audio-cache/anban-huri.mp3` | all 9 segments, one continuous narration (~3:52, 160 kbps) | 4,654,560 B |
| `…/story-audio-cache/clips/anban-huri--announce.mp3` | title «Անբան Հուռին» | 10,080 B |
| `…/story-audio-cache/clips/anban-huri--conclusion.mp3` | `reflectionText` | 135,360 B |
| `…/story-audio-cache/clips/anban-huri--question-0.mp3` | `reflectionQuestions[0]` | 40,800 B |

Segment start times in the full narration (byte offsets ÷ 20 000 B/s):

| Seg | Start | Watchpoints in this segment |
|---|---|---|
| 0 | 0:00 | «Հուռի» |
| 1 | 0:19 | «Հուռի» |
| 2 | 0:34 | — |
| 3 | 1:01 | **«մանեց»** (dialect stress), «Հուռի» |
| 4 | 1:30 | «Հուռի» |
| 5 | 2:21 | — |
| 6 | 2:43 | — |
| 7 | 3:11 | — |
| 8 | 3:42 | — |

## Watchpoints called out by the draft's own review notes

The draft names these explicitly as the things to listen for:

- **«Հուռու»** — genitive of the name, in `reflectionText` only →
  `anban-huri--conclusion.mp3`.
- **«զվարճալի»** — in `reflectionText` only → `conclusion.mp3`.
- **dialect forms like «մանեցե՛ք» / «մանեց»** — in segment 3, ~1:01 in
  the full narration.

Plus the standard prosody watchpoints from `tools/TtsListenTest/README.md`:
mid-word «՛», «՞» digraph placement, «և» mid-word, ն-article liaison,
«՝» pause, comma beat, «։» finality.

## Verdicts — fill these in while listening

| # | Clip / position | What to check | Verdict (PASS / FAIL) | Note |
|---|---|---|---|---|
| 1 | `announce.mp3` | title read naturally, name «Հուռին» intact | | |
| 2 | `conclusion.mp3` | **«Հուռու»** not mangled | | |
| 3 | `conclusion.mp3` | **«զվարճալի»** not mangled | | |
| 4 | `conclusion.mp3` | «՝» pause, comma beats, «։» finality | | |
| 5 | `question-0.mp3` | «Ի՞նչ» rising question intonation, «։» close | | |
| 6 | narration 0:00–0:34 | name «Հուռի» across segments 0–1 | | |
| 7 | narration ~1:01 | **«մանեց»** dialect stress | | |
| 8 | narration 1:30–2:21 | «Հուռի» in segment 4; general flow | | |
| 9 | narration 2:21–3:52 | segments 5–8: no truncation, clean ending | | |
| 10 | whole narration | no English/Latin leakage, no dropped words | | |

## Overall

- **Listened by:** _(name — must be a human; agent review does not count)_
- **Date (UTC):** _____
- **Playback path:** _(headphones / the actual MAX98357A + speaker — note which; the toy's speaker is the honest test)_
- **Overall verdict:** _(PASS / PASS-WITH-NOTES / FAIL)_
- **Summary:** _____

## If it passes — the promotion is still a separate human act

Per `backend/content/story-drafts/README.md` and CLAUDE.md, promotion is
a human act and must not be automated:

1. Move the file to
   `backend/src/ArmenianAiToy.Application/Stories/Content/`.
2. Set `review.status` to `"approved"`.
3. Stamp `listenTestAt` (and `linguisticReviewAt` if that pass is also
   real) with the true dates — **do not fake them**.
4. Note that `verificationStatus: pending` is still open in the draft —
   edition/orthography source verification for the Tumanyan retelling is
   a *separate* gate from this listen test and is not closed by it.

## What this does NOT close

Passing this test does not make SHIP A6 done. A6 additionally needs a
third approved story with an SD-wired MP3, a real three-item sync, and a
hardware run showing selection, no back-to-back repeats, reboot
persistence, and pause/resume staying on one story.

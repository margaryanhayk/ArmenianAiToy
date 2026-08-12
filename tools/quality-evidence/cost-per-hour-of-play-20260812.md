# Cost per hour of play — SHIP.md D3

**Date:** 2026-08-12
**Answer: roughly $0.08–$0.39 per hour, ~$0.20 typical.** Method and inputs
below, so a price change is a re-run rather than a rewrite.

## Why the number is small

Most of an hour of play costs nothing. Stories play from the SD card with no
network call at all; the welcome flow's greeting, the offer clips, the offline
games and the story-pause lines are all pre-rendered MP3s. Even the welcome
flow's *listening* — `POST /api/devices/voice-intent` — pays only for
speech-to-text, because the intent is classified by the deterministic keyword
matcher rather than by a model.

So spend is driven by one thing: how often the child **interrupts to ask a
question**, or answers a reflection question after a story.

## One in-story question, priced

Rates are the repo's own constants in
`Application/Helpers/OpenAICostEstimator.cs` — chat $2.50/$10.00 per 1M input
/ output tokens, Whisper $0.006/minute, TTS $15.00 per 1M characters, and the
repo's 4-characters-per-token approximation.

| component | | share |
|---|---|---|
| STT, 5 s of child speech | $0.00050 | 6% |
| **chat input, 2,086 tokens** | **$0.00522** | **67%** |
| chat output, ~120 chars | $0.00030 | 4% |
| TTS, ~120 chars | $0.00180 | 23% |
| **total** | **$0.0078** | |

The 2,086 tokens is **measured, not assumed**: 8,343 characters is the mean
assembled prompt across all nine guided stories at every segment, obtained by
calling `LibraryStoryQuestionPromptBuilder.Build` directly and summing
`SystemPrompt` + `UserMessage`. Two thirds of the cost of answering a child is
the grounding we send with the question, and that is the correct trade — the
prompt is what keeps the answer inside the story.

## Per hour

| a child who… | real |
|---|---|
| listens mostly, asks ~10 times | $0.08 |
| asks and answers ~25 times | $0.20 |
| interrupts constantly, ~50 times | $0.39 |

Twenty-five model turns in an hour is already a talkative child; the rest of
the hour is narration off the card.

## A finding: the daily cap counts about a third of what it spends

`StoryQaController` records cost as
`EstimateChatCostUsd(question, answerText)` — the child's **question**, not the
prompt that was actually sent. A question is around 21 characters; the prompt
is 8,343. So the meter sees ~6 input tokens where the API billed ~2,086.

- Recorded per turn: **$0.0026**. Real: **$0.0078**. The meter sees **33%**.
- The shipped cap is `OpenAI:DailyCostCap:Default = $0.50` per device per day.
  It therefore fires after ~191 turns, by which point real spend is about
  **$1.49 — three times the cap.**

**FIXED the same day.** The owner's decision was one honest limit for
everybody rather than tiers, so:

- `EstimateChatCostUsdFromPrompt(promptChars, response)` prices against what
  was actually sent. `LibraryStoryQuestionService` and
  `ReflectionDialogueService` now report their real prompt size, summed across
  the repair retry — a second billed call the meter never saw at all.
- The reflection path recorded **speech-to-text only**; its AI reaction was a
  billed model call counted as free. Now counted, including when the reaction
  is discarded by output moderation, because it was billed either way.
- The cap moved from `$0.50` to **`$0.25`**, derived as 30 questions × $0.0078
  rather than picked as a dollar figure — about $7.50/month per toy at the
  ceiling. A test pins the dollar value against the intended question count so
  the two cannot drift.

The online chat path (`ChatController`, `AudioChatController`) still uses the
old overload and still under-counts. Its prompt is assembled inside
`ChatService`, which is a HIGH-risk file, and **no shipped firmware uses that
path** — the online chat flow is complete but has never been flashed. Recorded
rather than forced.

**INTERIM.** The owner's instruction was explicit: this must change before
production. One flat limit is right while no real child has used the toy;
tiers need a free-tier number that can only be learned by watching one. See
`docs/usage-tiers-brainstorm.md`.

## Reproducing

The prompt measurement was taken with a throwaway xunit probe that walked
`InMemoryCuratedStoryLibrary.ListAvailable()`, resolved
`StoryQuestionGuides.TryGet`, and built a prompt at every segment index. It was
deleted after the reading — it asserted nothing and would have been noise in a
suite of 2,549 real tests. Re-create it in ten lines if the prompt grows.

Everything else is arithmetic over the constants named above.

## Not included

- ElevenLabs narration. It is a one-off render cost, not per hour of play —
  the ten stories cost 18,692 characters, once, and every play afterwards is
  free.
- Hosting. Railway's bill is not per hour of play.
- The online chat path (`/api/chat`, `/api/chat/audio`). Its prompt is built by
  `ChatService` and is a different size; the same under-count applies to it via
  `ChatController` and `AudioChatController`.

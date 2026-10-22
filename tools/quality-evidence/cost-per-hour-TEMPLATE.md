# Measured cost per hour of play — SHIP.md D3

**Status: NOT YET MEASURED.** This file is a blank template. The numbers below
are read from three billing surfaces by a human after real play; an agent must
not fill them in. Rename this file to `cost-per-hour-measured-<YYYYMMDD>.md`
when you record a real hour.

SHIP.md D3: *provider cost per hour of play is measured and written down.*

The **estimate** already exists: `tools/quality-evidence/cost-per-hour-of-play-20260812.md`
(about $0.08 to $0.39 per hour, ~$0.20 typical, from the repo's own cost
constants). This template is for the **measured** number that replaces or
confirms it.

## The hour

Play with one toy, one child (or you playing a child), for one continuous hour.
Ask questions mid-story and answer a reflection question or two, so the model
paths are exercised; an hour of pure SD-card playback costs nothing and proves
nothing.

| | |
|---|---|
| Date (UTC) | |
| Window start / end (UTC, with timezone) | |
| Toy (name, not the device id) | |
| Questions asked / reflection answers (counted by hand) | |
| Chat provider and model in use that day | |

## The three numbers

Read each one **before** the hour and **after** it, with the same date window,
and write the difference. Do not use a daily total that includes other traffic
(other toys, the bench, red-team runs, benchmarks).

| # | Where to read it | Before | After | Delta (this hour) |
|---|---|---|---|---|
| 1 | `admin.html` → System → "Top toys by spend (30 days)", this toy's row (the console's own **estimate**) | | | |
| 2 | OpenAI usage page (platform.openai.com → Usage), filtered to the same UTC window | | | |
| 3 | Google AI Studio billing (aistudio.google.com → Billing / usage), same window | | | |

Notes for reading them:

- Number 1 is the repo's estimate (`DeviceUsageDay`, summed client-side by the
  System tab), not a bill. Numbers 2 and 3 are what the providers charge, and
  they can lag by minutes to hours; read them again later if the delta looks
  too small.
- OpenAI covers speech-to-text, any OpenAI TTS and the moderation calls; Google
  covers the chat model when `AI:ChatProvider` is `gemini`. ElevenLabs narration
  is a one-off render cost, not per hour of play, and is excluded.
- Hosting (Railway) is not per hour of play and is excluded.

## Result

```
Provider cost for the hour (2 + 3):   $
Console estimate for the hour (1):    $
Estimate / provider ratio:
Cost per hour of play:                $
Cost per question:                    $
```

If the console estimate and the providers disagree by more than about 30%, say
which way and look at the estimator before trusting the per-device cap.

## Where the estimator lives

`backend/src/ArmenianAiToy.Application/Helpers/OpenAICostEstimator.cs`: pure
static constants (chat input/output per million tokens, Whisper per minute, TTS
per million characters, 4 characters per token). It feeds `OpenAICostMeter`
(the in-memory daily cap) and the durable `DeviceUsageDay` rows the console
reads. The cap itself is `OpenAI:DailyCostCap:Default` in
`backend/src/ArmenianAiToy.Api/appsettings.json`; see
`docs/openai-daily-cost-cap.md`.

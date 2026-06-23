# Per-device daily OpenAI cost cap

Production-safety v1 gate that bounds OpenAI spend per device per
UTC day. Ranked as P0 in the
`docs/areg-current-readiness-evaluation.md` "Top 20 next tasks"
section; this doc records the v1 implementation, its
limitations, how to configure it, and how to verify it.

## What it does

For every `POST /api/chat` and `POST /api/chat/audio` request:

1. After the existing paused / bedtime / mode-disabled gates run.
2. BEFORE any upstream OpenAI call (chat, Whisper, TTS).
3. The controller consults
   `OpenAICostMeter.IsOverCap(deviceId, cap, DateTime.UtcNow)`.
4. **Under cap:** request proceeds. After a successful completion,
   the controller estimates the cost (via
   `OpenAICostEstimator`) and records it on the meter.
5. **Over cap:** request short-circuits with a child-safe canned
   response. ChatService / STT / TTS are NOT called.

## What it does NOT do

- Does NOT expose money / cost detail to the child. The canned
  response is shape-compatible with the paused-device response.
- Does NOT persist counters across process restarts (v1 is
  in-memory only — `ConcurrentDictionary` + lock). Accepted
  limitation; worst case is one extra cap-worth of spend during
  a restart, never unbounded.
- Does NOT use a real tokenizer. Cost estimates are based on
  character length × conservative `chars-per-token` factor.
  Designed to over-estimate slightly, so the cap fires earlier
  rather than later.
- Does NOT mutate moderation, auth, rate-limiting, or provider /
  model config. The gate is purely additive.

## Configuration

Configuration section: `OpenAI:DailyCostCap` (bound by
`OpenAIDailyCostCapOptions`).

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Enabled` | bool | `true` | Master switch. False = gate never trips, no recording. |
| `Default` | decimal | `0.50` | Per-device daily cap in USD. |
| `Global` | decimal | `0` | **#022** fleet-wide daily ceiling (USD) across ALL devices. `0` = disabled (opt-in). When the day's fleet total reaches it, every paid path fails closed until the next UTC day. |
| `PerDeviceOverride` | dict<string, decimal> | empty | Optional `{ "<deviceGuid>": <capUsd> }` overrides. |

`appsettings.json` example (already shipped with safe defaults):

```jsonc
{
  "OpenAI": {
    "DailyCostCap": {
      "Enabled": true,
      "Default": 0.50,
      "PerDeviceOverride": {
        // "11111111-2222-3333-4444-555555555555": 2.00
      }
    }
  }
}
```

If the entire section is missing, defaults apply (`Enabled=true`,
`Default=0.50`, `Global=0`).

## Global / fleet ceiling (#022)

The per-device cap can't stop a fleet-wide spike (many devices each under
their own cap). `Global` adds a hard daily ceiling on the SUM of all devices'
estimated cost on the current UTC day. It is the runaway-spend kill-switch.

- **Opt-in**: default `0` = disabled, so shipped behavior is unchanged.
- Checked **first** at all four paid gate sites (`/api/chat`,
  `/api/chat/audio`, and both `/api/story-qa` paths), inside the same
  `Enabled` block, before any OpenAI call. Over-ceiling fails closed with the
  SAME soft-off response as the per-device cap (Clean `ChatResponse` /
  `CannedVoiceClips.PausedKey`).
- Backed by a fleet-wide accumulator on `OpenAICostMeter`
  (`IsGlobalOverCap` / `GetGlobalTotal` / `ShouldLogGlobalCapTrip`), rolling
  on the UTC day boundary like the per-device buckets.
- The `aat_openai_cost_cap_trip_total` metric reuses the existing per-path
  `kind` value (no new tag values); the global trip is distinguished in a
  once-per-day `LogWarning` (`OpenAI GLOBAL daily cost ceiling reached ...`).
- **Same in-process / per-instance / resets-on-restart caveat** as the
  per-device cap: with N instances the effective ceiling is N×, and a restart
  resets it. The shared-store fix is the same future work as the per-device
  meter. Still a real backstop — one instance's runaway loop self-limits.

## Cost estimation constants

Defined in `OpenAICostEstimator.cs` (Application layer). All
labelled as conservative local estimates — not authoritative
OpenAI pricing.

| Constant | Value | Source |
|---|---|---|
| `ChatInputUsdPerMillionTokens` | `2.50` | approximate gpt-4o input |
| `ChatOutputUsdPerMillionTokens` | `10.00` | approximate gpt-4o output |
| `WhisperUsdPerMinute` | `0.006` | approximate whisper-1 |
| `TtsUsdPerMillionChars` | `15.00` | approximate tts-1 |
| `CharsPerTokenEstimate` | `4` | rough |

**If you change a model** (in `OpenAI:ChatModel`,
`OpenAI:TranscriptionModel`, `OpenAI:TtsModel`), update the
matching constant here AND this doc. Otherwise the cap will
under- or over-fire by the pricing-delta ratio.

## Metric and log

Metric:
```
aat_openai_cost_cap_trip_total{kind=chat|audio}
```
Counter incremented once per request that hit the cap. Operators
can alert on rate.

Structured warning log (flood-controlled via
`OpenAICostMeter.ShouldLogCapTrip` — at most ONE line per device
per UTC day):

```
OpenAI daily cost cap reached.
  DeviceId=<guid>
  Kind=chat|audio
  CurrentEstimatedUsd=<decimal>
  CapUsd=<decimal>
  UtcDate=<yyyy-MM-dd>
```

No child message text, no API responses, no secrets in the log.

## Audio path specifics

The audio gate fires at the SAME position as the existing
paused / bedtime / mode-disabled checks — before Whisper STT.
This means a cap-trip on the audio path skips STT + chat + TTS,
which is the entire cost surface.

The audio cap-trip response reuses the existing
`CannedVoiceClips.PausedKey` clip. A future slice can add a
distinct cost-cap audio clip if operators want device-side
distinguishability (LED state, etc.); v1 deliberately keeps the
voice-side cost surface minimal.

## Manual verification

### Chat path (text)

```bash
# Override cap for one device to 0.01 USD so a single turn trips:
#   appsettings.Development.json:
#     "OpenAI": { "DailyCostCap": {
#       "Default": 0.01,
#       "PerDeviceOverride": { "<deviceGuid>": 0.01 }
#     }}
#
# Start backend, hit /api/chat twice as that device.
# Expected:
#   Turn 1: real Armenian reply (under cap, but recorded crosses 0.01)
#   Turn 2: «Հիմա մի փոքր դադար տանք։ Քիչ հետո նորից կշարունակենք։»
#           SafetyFlag.Clean. ChatService NOT invoked.
```

### Audio path (voice)

```bash
# Same override as above. Send any WAV via POST /api/chat/audio.
# Expected:
#   Turn 1: TTS'd reply, real audio.
#   Turn 2: canned paused-style clip; Whisper not called.
```

### Metric

```bash
curl -s http://localhost:5000/metrics | grep aat_openai_cost_cap_trip_total
# Two non-zero series (chat / audio) after the manual verifications above.
```

### Log

Grep stdout for `OpenAI daily cost cap reached`. Should appear
exactly once per device per UTC day even under sustained cap-hit
traffic.

## How it's tested

Deterministic unit tests, no network, no OpenAI calls:

- `OpenAICostMeterTests` — under-cap, over-cap, day-boundary
  reset, per-device isolation, flood-controlled log gate, and the
  **global ceiling** (fleet accrual, day-reset, once/day global log,
  per-device counters undisturbed).
- `OpenAICostEstimatorTests` — non-negativity, ordering, edge
  cases, constants are documented and positive.
- `ChatControllerCostCapTests` — end-to-end controller behavior:
  under-cap proceeds, over-cap blocks before ChatService,
  cost-cap-disabled config skips the gate, per-device isolation,
  per-device override, and the **global ceiling** (trips while the
  device is under its own cap; `Global=0` disables it).

Run:

```
cd backend
dotnet test tests/ArmenianAiToy.Application.Tests/ArmenianAiToy.Application.Tests.csproj --no-build --nologo --filter "CostCap"
dotnet test tests/ArmenianAiToy.Application.Tests/ArmenianAiToy.Application.Tests.csproj --no-build --nologo --filter "CostMeter"
dotnet test tests/ArmenianAiToy.Application.Tests/ArmenianAiToy.Application.Tests.csproj --no-build --nologo --filter "CostEstimator"
```

## Limitations / future work

- **Process restart resets counters** — a v2 could persist the
  per-device daily bucket to SQLite (one row per device per UTC
  day with TTL). Skipped in v1 to avoid a new migration.
- **Rough cost estimate** — a v2 could use the real OpenAI usage
  fields when the SDK exposes them on the response, replacing
  the character-length proxy.
- **No automatic alert** — operators must wire their own
  Prometheus alert on `aat_openai_cost_cap_trip_total`.
- **No parent-facing visibility** — parents do not see cap state
  in `parent.html`. A v2 could surface today's spend per device
  in the Today panel.
- **Audio cost-cap clip is shape-shared with paused** — see the
  "Audio path specifics" section above. Distinguishable clip
  is a v2 concern.

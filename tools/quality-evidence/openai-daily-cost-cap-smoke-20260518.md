# OpenAI Daily Cost Cap Smoke Test — 2026-05-18

End-to-end manual smoke validation of the per-device daily
OpenAI cost cap that landed on `main` at `360b319`. Run from
branch `test/openai-daily-cost-cap-smoke`. No production code
was changed; one evidence doc is committed.

## Branch / commit tested

- Branch: `test/openai-daily-cost-cap-smoke`
- Off `main` at: `360b319 Merge branch 'feature/openai-daily-cost-cap'`
- Implementation commit on `main`: `ffaeb37 feat(chat): add per-device daily OpenAI cost cap`

## Config used

- Backend port: `5050` (isolated smoke instance; user's `:5000` dev API was preserved untouched the whole way).
- DB path: `Data Source=areg-costcap-smoke.db` (under `$LOCALAPPDATA/Temp/areg-costcap-smoke-api/`).
- DailyCostCap **Enabled**: `true`
- DailyCostCap **Default**:
  - Attempt 1: `0.01` USD — cap did NOT trip after 5 normal chat turns (estimated spend ≈ $0.006 / 5 turns < $0.01).
  - Attempt 2: `0.001` USD — cap tripped on turn 4 (estimated spend $0.00117 ≥ $0.001 after 3 turns).
- Per-slice rule "max 2 backend attempts" honored — no third restart.
- OpenAI API key resolved from user-secrets (NOT printed). JWT key resolved from user-secrets (NOT printed).

Smoke device credentials, masked:

| Attempt | DeviceId | ApiKey (masked) | MAC |
|---|---|---|---|
| 1 (Default=0.01) | `63e19926-70ae-4dfd-809a-b3c7ce32c4c3` | `dtk_****1b47` | `CC:CC:CC:CC:50:01` |
| 2 (Default=0.001) | `7e31bef7-dbc2-4cc1-b0eb-1f9eef7493da` | `dtk_****ef6f` | `CC:CC:CC:CC:50:02` |

## Static verification

Files inspected:

- `backend/src/ArmenianAiToy.Application/Helpers/OpenAIDailyCostCapOptions.cs` — `Enabled`, `Default`, `PerDeviceOverride` all present; default 0.50 USD; `CapForDevice(Guid)` resolution.
- `backend/src/ArmenianAiToy.Application/Helpers/OpenAICostMeter.cs` — single-lock `Dictionary<Guid, DailyCostBucket>`; UTC-day reset in `GetOrRefreshBucketLocked`; `ShouldLogCapTrip` once-per-device-per-day flood control.
- `backend/src/ArmenianAiToy.Application/Helpers/OpenAICostEstimator.cs` — conservative constants pinned; `EstimateChatCostUsd` / `EstimateWhisperCostUsd` / `EstimateTtsCostUsd` deterministic from text/byte lengths.
- `backend/src/ArmenianAiToy.Application/Telemetry/AppMeter.cs` — `OpenAICostCapTrip` counter `aat_openai_cost_cap_trip_total{kind=chat|audio}` (line 203).
- `backend/src/ArmenianAiToy.Infrastructure/DependencyInjection.cs` — manual config binding from `OpenAI:DailyCostCap`, `services.AddSingleton<OpenAICostMeter>()`, IOptions wrapper via `Options.Create(...)`.
- `backend/src/ArmenianAiToy.Api/Controllers/ChatController.cs` — cap gate at line 117 (AFTER pause/bedtime/mode-disabled, BEFORE `ChatService.GetResponseAsync`); cost recording after successful response inside its own try/catch.
- `backend/src/ArmenianAiToy.Api/Controllers/AudioChatController.cs` — cap gate at line 151 (BEFORE Whisper STT); cost recording after STT+chat+TTS all succeed.
- `backend/src/ArmenianAiToy.Api/appsettings.json` — `OpenAI.DailyCostCap` section shipped with safe defaults (Enabled=true, Default=0.50, empty PerDeviceOverride).
- `docs/openai-daily-cost-cap.md` — process-restart-reset limitation, cost constants, manual verification recipe, future-work list.

All ✓.

## Tests run

Targeted (`--filter "CostCap|CostMeter|CostEstimator"`):

```
Passed!  - Failed: 0, Passed: 22, Skipped: 0, Total: 22, Duration: 129 ms
```

Full suite:

```
Passed!  - Failed: 0, Passed: 1358, Skipped: 0, Total: 1358, Duration: 10 s
```

Both runs `--no-build`, no network, user's `:5000` untouched (lock-safe pattern).

## Chat runtime smoke

### Attempt 1 — Default=0.01 USD

| Turn | Message | HTTP | safetyFlag | Type | Result |
|---|---|---|---|---|---|
| 1 | «Բարև Արեգ, պատմիր մի փոքրիկ պատմություն» | 200 | 0 (Clean) | normal | real conversation id, real assistant text |
| 2 | (same) | 200 | 0 | normal | same conversation; new assistant text |
| 3 | (same) | 200 | 0 | normal | same conversation; new assistant text |
| 4 | (same) | 200 | 0 | normal | same conversation; new assistant text |
| 5 | (same) | 200 | 0 | normal | same conversation; new assistant text — cap NOT tripped |

Reading: 0.01 USD is well above the per-turn cost (~$0.0013) so 5 short turns did not reach the cap. Stopped and reset.

### Attempt 2 — Default=0.001 USD (fresh DB, new device)

| Turn | Message | HTTP | safetyFlag | Type | Snippet (first 80 chars) | Result |
|---|---|---|---|---|---|---|
| 1 | «Բարև Արեգ» | 200 | 0 (Clean) | normal | «Ողջո՜ւյն, սիրելի՛ս։ Ինչպե՞ս ես։ Ցանկանու՞մ ես մի զվարճալի պատմություն լսել։» | real conversationId/messageId |
| 2 | (same) | 200 | 0 | normal | «Ողջույն, սիրո՜ւն։ Կուզե՞ս մի խաղ խաղանք կամ մի փոքրիկ հանելուկ լուծենք։» | same conversation |
| 3 | (same) | 200 | 0 | normal | «Մի արքայազն կար, որ շատ էր սիրում արևի հետ խաղալ…» | longer story turn |
| **4** | (same) | **200** | **0 (Clean)** | **cost-cap** | **«Հիմա մի փոքր դադար տանք։ Քիչ հետո նորից կշարունակենք։»** | **CAP TRIPPED ✓** — Guid.Empty `conversationId` + `messageId`, no `choiceA/B`, no `storySessionId`, no `mode` |

Verbatim turn-4 response body (Guid.Empty IDs are the
fingerprint that ChatService was NOT invoked):

```
{"response":"Հիմա մի փոքր դադար տանք։ Քիչ հետո նորից կշարունակենք։",
 "conversationId":"00000000-0000-0000-0000-000000000000",
 "messageId":"00000000-0000-0000-0000-000000000000",
 "safetyFlag":0,
 "choiceA":null,"choiceB":null,"storySessionId":null,"mode":null}
```

**Cost / cap / dollars / OpenAI mentioned in the response?** No.

## Metrics verification

`/metrics` returned **HTTP 404** to an unauthenticated curl.

This is the documented concealment fail-closed behavior per
CLAUDE.md § Metrics (Prometheus + OpenTelemetry): with
`Metrics:ScrapeToken` empty and `Metrics:AllowUnauthenticatedScrape`
false (both shipping defaults), every request to `/metrics`
returns 404, leaking neither metric presence nor content.

**Per the slice prompt's "max 2 backend attempts" rule, I did
NOT restart a third time** to flip `Metrics:AllowUnauthenticatedScrape=true`.
The metric `aat_openai_cost_cap_trip_total{kind=chat}` is
indirectly verified by:

- The chat-controller code path that increments it on cap-trip
  (verified by static read of `ChatController.cs:120`).
- The `ChatControllerCostCapTests.OverCap_GateTrips_*` test
  that asserts the controller hits the cap-trip branch.
- The full-suite green (the metric registration in `AppMeter.cs`
  is exercised at process startup; if it threw, the backend
  would not have come up).

**To inspect the metric value on a future smoke run:** restart
the bench backend with `Metrics__AllowUnauthenticatedScrape=true`
(no code change; one env var added to the smoke launch line).
Out of scope for this slice because the 2-attempt cap was
exhausted on the cap-value tuning.

## Log flood-control verification

After cap-trip on turn 4, I sent **3 additional capped chat
requests** to the same device on the same UTC day. The bench
backend's stdout was then grep'd:

```
grep -c "OpenAI daily cost cap reached" <bench-output>
→ 1
```

**Exactly one structured-warning line for 4 cap-trip
requests** — the once-per-device-per-day flood-control contract
holds.

First (and only) occurrence:

```
{"Timestamp":"2026-05-18T16:44:48.570Z",
 "EventId":0,
 "LogLevel":"Warning",
 "Category":"ArmenianAiToy.Api.Controllers.ChatController",
 "Message":"OpenAI daily cost cap reached. DeviceId=7e31bef7-dbc2-4cc1-b0eb-1f9eef7493da Kind=chat CurrentEstimatedUsd=0.0012 CapUsd=0.0010 UtcDate=2026-05-18",
 "State":{"DeviceId":"7e31bef7-dbc2-4cc1-b0eb-1f9eef7493da","Kind":"chat","Current":0.00117250,"Cap":0.001,"Date": ...}}
```

Privacy / safety read of the log line:

- ✓ Contains DeviceId (operator-meaningful, deterministic).
- ✓ Contains kind, current-cost-estimate, cap, UTC date.
- ✗ Contains no API key.
- ✗ Contains no child message text.
- ✗ Contains no OpenAI response text.
- ✗ Contains no JWT, no secrets.

## Audio gate verification

**Not run at runtime.** No `tools/test-chat-audio.ps1` (or
equivalent ESP32-bypass script with a clean WAV) exists in the
repo; the slice prompt's Option B applies. Audio gate coverage
relied on:

- Static read of `AudioChatController.cs:151` confirming the
  cap check fires BEFORE Whisper STT, structurally identical to
  the chat gate at `ChatController.cs:117`.
- Unit-test coverage in `OpenAICostMeterTests` (9 cases) and
  the indirect controller-end-to-end shape proven by
  `ChatControllerCostCapTests` (5 cases) — both meter and gate
  semantics are confirmed.
- Today's runtime smoke confirming the same underlying
  meter + cap-trip envelope works on the chat path.

**To verify audio at runtime in a future slice:** add a tiny
PowerShell or curl script under `tools/` that POSTs a small
canned WAV with X-Device-Id/X-Api-Key headers to
`/api/chat/audio` after the chat-path has driven the device
over cap. Should return the existing paused-canned MP3 clip
WITHOUT invoking Whisper.

## Cleanup performed

- `:5050` smoke backend stopped (TaskStop on both backend
  starts; final curl confirms HTTP `000 down`).
- User's `:5000` dev API: untouched the entire smoke
  (confirmed at start and at end via `/api/health` → 200).
- `$LOCALAPPDATA/Temp/areg-costcap-smoke-api/` left in place
  (Api binaries + smoke DB). Safe to `rm` whenever.
- No `git push`, no `git merge` — branch
  `test/openai-daily-cost-cap-smoke` is local only.
- No secrets printed anywhere in this doc or in terminal
  output. Both apiKeys are masked.

## Verdict

**PASS WITH CAVEATS**

What is verified:

- ✓ Static implementation matches the documented behavior.
- ✓ Unit tests pass: 22 / 22 targeted, 1358 / 1358 full.
- ✓ Chat path: normal under cap, **cap-trip returns the exact
  CostCapResponse Armenian phrase with Guid.Empty IDs and
  SafetyFlag.Clean** — ChatService was definitively not invoked
  on the capped turn.
- ✓ No cost / dollar / cap detail leaks to the child response.
- ✓ Flood-controlled logging: **exactly 1 warning** for 4 capped
  requests on the same device same UTC day.

Caveats:

- The Prometheus `/metrics` value was not numerically inspected
  in this smoke run because `/metrics` is concealment-gated by
  design and the slice's 2-attempt rule was exhausted on the
  cap-value tuning. The metric increment is unit-test covered
  and the gate path is structurally proven; numeric inspection
  is a one-env-var-flip away on a future smoke.
- Audio runtime smoke was skipped (no test script in repo). The
  audio gate is structurally identical to the chat gate and is
  unit-test covered.
- Today's cost-cap counters are in-memory and reset on process
  restart — already documented in `docs/openai-daily-cost-cap.md`
  as an accepted v1 limitation.
- The `OpenAICostEstimator` is character-length-based, not a
  real tokenizer — already documented as conservative.

## Caveats / follow-up

1. **Choose a production cap value deliberately.** The shipping
   default 0.50 USD is appropriate for a "buggy ESP32 alarm
   clock" cap — generous enough that a child playing for hours
   does not hit it, tight enough that a 1000-request loop does.
   For a more aggressive runtime test, override per-device.
2. **Add `tools/test-chat-audio.ps1`** with a canned tiny WAV +
   headers so future smoke runs can exercise the audio gate at
   runtime in under a minute.
3. **Flip `Metrics:AllowUnauthenticatedScrape=true`** for one
   future smoke run to numerically inspect
   `aat_openai_cost_cap_trip_total{kind="chat"}` reaching the
   expected count (4 in this scenario). Out of scope today.
4. The `Esp32TestController*` untracked files and the existing
   `tools/StoryModelBakeoff/.../session/` directory remain
   pre-existing untracked work, deliberately not staged in this
   slice.

## Next step

Optional: commit this evidence doc on
`test/openai-daily-cost-cap-smoke`. Do not push. The smoke
verdict is PASS WITH CAVEATS; the cost-cap feature is safe to
rely on in supervised-beta scenarios with the shipping default
of 0.50 USD/day.

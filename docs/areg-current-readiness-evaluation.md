# Areg Current Readiness Evaluation

**Branch:** `evaluation/areg-current-readiness-20260518` (off `main` at `a3c930f`)
**Date:** 2026-05-18 (current-main evaluation, post-moderation-doc-merge)
**Supersedes:** the prior 2026-05-18 evaluation on `evaluation/areg-current-readiness` (one entry retracted — see below).

## Executive summary

Areg is a substantial mid-stage prototype with **mature backend
plumbing**, a **credibly-instrumented quality-hardening
discipline**, and **child-safety contracts that are now
demonstrably offline-testable**. The text chat path is
**bench-ready**, the voice MVP is **bench-ready on one
ESP32-S3 board**, the parent dashboard is **heavily wired**
(audit events, data export, retention, bedtime windows, mode
flags, per-child overrides, password reset, email verification,
Google sign-in), and **dual-layer moderation** with a fail-closed
sentinel contract is in place — and verified by **32 offline
tests** that pass in ~1 second without network.

The honest gap is between **bench-ready** and
**production / 24-7-unattended ready**. The voice path is
Story-only; the legacy text-only ESP32 sketch is broken against
the current backend; the bench runs HTTP-only on a dynamic LAN
IP; OpenAI cost is uncapped beyond a per-device rate-limiter
token bucket; live-benchmark evidence is one-sample-per-day on a
noise floor of ~1–3 weak cases per 90 turns; and no production
deployment / monitoring / OTA / enclosure story exists yet. A
controlled-supervision child beta is plausible on this state; an
unsupervised always-on toy is not.

| Layer | Status |
|---|---|
| Demo-ready (controlled session, operator present) | **YES** ✓ |
| Bench-ready (reproducible from clone, tests green) | **YES** ✓ |
| Child-safe supervised beta (parent in room, dev backend) | **PLAUSIBLE** (with monitoring) |
| Production-ready (multi-user, hardened, monitored) | **NO** |
| 24/7 unattended ready (physical toy, no operator) | **NO** |

### What changed since the prior 2026-05-18 evaluation

- `main` advanced from `7b066ba` → `84e66ff` → `a3c930f` via two
  merges: the follow-up quality hardening (Story/Riddle/Curiosity
  fixes, +15 deterministic tests, three targeted live benchmarks
  clean) and a doc-only moderation-test mocking-strategy
  clarification.
- The prior eval listed "Mock OpenAI SDK in moderation tests" as
  the #1 P0/S task. **That item is retracted.** Inspection of
  `OpenAIModerationAdapter.cs` and `ModerationFailClosedTests.cs`
  showed the seam (`protected virtual ClassifyOnceAsync`) had
  already been added at some prior date and tests already mocked
  via a `StubAdapter` subclass. The dummy `ModerationClient` the
  `StubAdapter` passes to its base ctor is constructed but never
  invoked. Empirical verification:
  `dotnet test --filter "Moderation"` → **32 / 32 passed in 1
  second** with no network access. The mocking-strategy
  clarification commit (`b203d67`) made this visible to future
  readers in the test file's class-level summary.

## Score table

| Surface | Score / 100 | Reason |
|---|---|---|
| **Overall project readiness** | **73** (+1) | Same posture as prior eval, +1 for the resolved moderation-test concern (testing surface is one layer cleaner). |
| Backend architecture | 78 | Clean Architecture observed (Api / Application / Domain / Infrastructure), 11 entities, 9 migrations, 23 helpers, mature DI. ChatService at 2347 lines is the monolithic choke-point — testability good, maintainability risk grows. |
| Armenian language quality | 80 | All five mode prompts share an abstract-worded formal-plural ban with `Assert.DoesNotContain` pins. Cross-mode register consistent. Live runtime sampled at 147 turns total over four runs — small. |
| Story mode | 80 | 2026-05-18 targeted live run 0/29 weak. ANCHOR-ON-NAMED-ENTITY rule fixed the T10 regression. Choice template-verb variety still mediocre qualitatively (~50% of turns open with «Մոտենանք / Նայենք»). |
| Game mode | 78 | Cold-start fix verified on 2 live runs (0/20 weak both times). One-sample-per-day evidence. PLURAL-IMPERATIVE OPENERS ban lives in Game only — other modes rely on pronoun ban. |
| Riddle mode | 84 | 2026-05-18 targeted live run 0/15 weak. DIRECTIVE-IS-BINDING rule + defensive multi-word triggers in `RiddleIntent`. «նորից»-dispatch capability live-verified. |
| Curiosity mode | 86 | Eliminated a metric that lived in the committed baseline (`length_growing` 1 → 0). Exemption for «ավելի պատմիր» is rule-only, not benchmark-triggered. |
| Calm mode | 80 | Anti-companion + grounding + wind-down arc are mature. Formal-plural ban added. NOT live-retested on 2026-05-18 (was 0/13 on prior runs). |
| ESP32 browser prototype | 65 | `wwwroot/index.html` + `story.html` + `parent.html` work from a phone on LAN. Legacy `esp32/ArmenianAiToy/ArmenianAiToy.ino` sketch is broken against current backend (no device-auth headers, documented as stale). Dynamic LAN IP requires re-provisioning. |
| ESP32 voice/TTS readiness | 62 | `AregVoiceMvp.ino` works on one ESP32-S3-DevKitC-1 + INMP441 + MAX98357A. Buffered playback only, no retry, no barge-in, HTTP-only, no enclosure. Audio endpoint is **Story-only** per CLAUDE.md C1 spec — Game/Riddle/Calm/Curiosity do not flow through voice. |
| Parent dashboard / monitoring | 78 | `wwwroot/parent.html` is read-only with login + linked devices + summaries + flagged messages + conversation detail + audit feed + Today panel (server-aggregated, timezone-aware). Email verification, Google sign-in, password reset, account delete, per-child mode overrides, bedtime window. No UX testing evidence. |
| Safety / moderation | **84** (+2) | Dual moderation (input + output), fail-closed sentinel pinned by 32 offline-mocked tests, `OpenAIReliabilityGate` retry/circuit, `ChatGateEvaluator` pause/bedtime/mode gating, `DangerousInputFilter` helper. The mocking-strategy is now discoverable from the test file header. |
| Privacy / data handling | 78 | `AuditEvent` append-only with null-actor system events, `ParentDataExported` audit row, `RetentionPurgeService` background, `LocalDiskAudioBlobStore` cleanup cascade C2.2a+b. No external COPPA/GDPR review on record. |
| Testing | **82** (+2) | 1336 deterministic tests; 91 test files. Moderation tests now provably offline. Prompt-content presence tests, mode-detection priority tests, dispatcher tests, choice parser tests, parent-service tests. Gap: behavior-level live coverage at scale. |
| Benchmark reliability | 70 | 9 benchmark tools cover Story / Game / Riddle / Calm / Curiosity + BenchmarkAll. Noise floor empirically ~1-3 weak cases per 90-turn BenchmarkAll. One-sample-per-day evidence per slice. Each live run costs real OpenAI tokens. |
| Production readiness | 50 | HTTP-only bench, dynamic IP, OpenAI cost uncapped beyond rate-limiter, no SLO dashboards or alerting, no OTA, no enclosure, no deployment runbook, no incident response, no on-call. CLAUDE.md is a thorough spec but is not an operations manual. |

(Scores marked **(+N)** moved up vs the prior 2026-05-18
evaluation.)

## Evidence used

**Tests run fresh today on `evaluation/areg-current-readiness-20260518`:**

```
cd backend
dotnet test tests/ArmenianAiToy.Application.Tests/ArmenianAiToy.Application.Tests.csproj --no-build --nologo
→ Passed!  - Failed: 0, Passed: 1336, Skipped: 0, Total: 1336, Duration: 9 s

dotnet test ... --filter "Moderation"
→ Passed!  - Failed: 0, Passed: 32, Skipped: 0, Total: 32, Duration: 1 s
```

**Benchmark docs read:**

- `tools/quality-evidence/areg-game-riddle-quality-20260517.md`
- `tools/quality-evidence/areg-live-quality-validation-20260517.md` (3 BenchmarkAll runs + Game-targeted post-fix)
- `tools/quality-evidence/areg-followup-live-validation-20260518.md` (Story / Riddle / Curiosity targeted, 0 weak)
- `docs/day-quality-hardening-report.md`
- `docs/followup-quality-hardening-report.md`
- `docs/esp32-chain.md`

**Latest live results (from prior sessions, NOT re-run today — paid run skipped):**

| Run | Story | Game | Riddle | Calm | Curiosity |
|---|---|---|---|---|---|
| 2026-05-17 12:53 BenchmarkAll #1 | 0/29 | 1/20 | 0/15 | 0/13 | 0/13 |
| 2026-05-17 18:03 Game-targeted | — | 0/20 | — | — | — |
| 2026-05-17 19:10 BenchmarkAll #3 | 1/29 | 0/20 | 1/15 | 0/13 | 1/13 |
| 2026-05-18 00:18 targeted | **0/29** | — | **0/15** | — | **0/13** |

**Files inspected:** ChatService.cs (2347 lines), all 24 Application helpers, all 8 controllers (Chat, AudioChat, Audit, Child, Conversation, Device, Esp32Test, Parent), DeviceAuthMiddleware, AppDbContext + 11 migrations, all 6 OpenAI infrastructure files (chat / moderation / Whisper / TTS / reliability gate / failure classifier), CLAUDE.md (1738 lines, 28 documented invariant sections), 91 test files (counted, sampled). ModerationFailClosedTests.cs re-inspected after b203d67 — header comment now documents the mocking seam.

**Live runs deliberately NOT executed today:** A fifth BenchmarkAll on this evaluation branch would consume OpenAI tokens for re-validation that would not change the report's scoring. The four prior runs are sufficient single-sample evidence per slice.

## Feature-by-feature assessment

### `/api/chat` text path

- **Current status:** bench-ready, child-safe-supervised plausible.
- **Works:** ChatService orchestrates label consumption → moderation (input) → normalization → mode detection → prompt assembly → OpenAI GPT-4o via `OpenAIReliabilityGate` → tail-block parse → moderation (output) → response. Pause/bedtime/mode-disabled gates short-circuit before any OpenAI call.
- **Weak:** ChatService at 2347 lines is the architectural choke-point; any change has wide blast radius. Choice template-verb variety (qualitative).
- **Evidence:** 1336 unit tests + 4 live runs + `ChatControllerPath5Tests` Path-5 sanitization.
- **Score:** 80
- **Next action:** consider extracting per-mode dispatcher / directive-builder out of ChatService to reduce monolith risk.

### Story mode

- **Current status:** bench-ready.
- **Works:** opening variety enforced, child-narration banned, rhetorical-question ban with «արդյոք» literal pinned, choice differentiation (TWO axes), choice grounding (entities already named in body), no-recap rule, verbatim anchor, anti-folklore-by-default, ANCHOR-ON-NAMED-ENTITY rule. `StoryChoiceCoherenceGate` (613 lines) is a runtime guard.
- **Weak:** «Մոտենանք / Նայենք» first-verb pair still dominant; no first-verb-rotation rule.
- **Evidence:** 38 prompt-content tests + StoryChoiceCoherenceGate tests + live 0/29 weak on last run.
- **Score:** 80
- **Next action:** add a first-verb-rotation rule + benchmark check across consecutive turns.

### Game mode

- **Current status:** bench-ready.
- **Works:** four turn kinds (`new_game` / `continue` / `switch_game` / `stop_game`) with runtime directive injection, seven game types with subtypes, round progression, variety policy, celebration rotation, magic phrasing, STRICT NON-NEGOTIABLES (cold-start one-type rule, body_part+clap_along ban, PLURAL-IMPERATIVE OPENERS ban, EXAMPLES-SHOW-MULTI-TURN-RHYTHM disclaimer).
- **Weak:** PLURAL-IMPERATIVE ban is mode-local. One-sample-per-day live evidence.
- **Evidence:** 107 prompt-content tests + 9 GameLoopIntegrationTests + live 0/20 weak on 2 post-fix runs.
- **Score:** 78
- **Next action:** lift PLURAL-IMPERATIVE OPENERS rule cross-mode; second live BenchmarkAll for two-sample evidence.

### Riddle mode

- **Current status:** bench-ready.
- **Works:** four turn kinds, runtime `RiddleIntent` dispatch with 26 explicit Armenian/English start-new triggers + 14 give-up triggers, RIDDLE_TURN_KIND DIRECTIVE IS BINDING rule, second-wrong-guess-needs-new-clue rule, cold-rejection ban.
- **Weak:** Model variance on cold-starts — fix is prompt-side strengthening, no runtime classification of post-classification turn-kind.
- **Evidence:** 94 prompt-content + intent tests + 8 RiddleLoopIntegrationTests + live 0/15 weak on most recent run.
- **Score:** 84
- **Next action:** add explicit-ask runtime verifier — if model output lacks Armenian question mark on a `new_riddle` directive, retry once.

### Curiosity mode

- **Current status:** bench-ready.
- **Works:** two-layer answer (direct answer + optional analogy/fun-fact), anti-encyclopedia, anti-praise-opener, story-return shape gated on PREVIOUS_MODE directive, FOLLOW-UP CONCISION rule.
- **Weak:** «ավելի պատմիր» exemption is rule-only — no benchmark scenario exercises it.
- **Evidence:** 69 prompt-content tests + live 0/13 weak (baseline metric eliminated 1 → 0).
- **Score:** 86
- **Next action:** add a CuriosityBenchmark scenario that triggers «ավելի պատմիր» to verify the exemption path.

### Calm mode

- **Current status:** bench-ready.
- **Works:** soft-tone TONE rule, no-questions, no-exclamations, no-companion-language, grounding anchor pool, bedtime-distress shape, wind-down arc (turn 1 / turn 2 / turn 3+), closing-phrase shape, fall-back response with anchor.
- **Weak:** F2 (Turn-2 distress-vs-arc cardinality coexistence) documented but unresolved — two cardinality surfaces with incompatible turn-2 semantics, pinned by a single test.
- **Evidence:** 19 prompt-content tests + `CalmFallbackResponse` shape pin + live 0/13 weak on run 1 and run 3 (not re-tested on 2026-05-18).
- **Score:** 80
- **Next action:** resolve F2 by either tightening BEDTIME-DISTRESS SHAPE or adding an explicit arc-precedence line.

### Device registration / auth

- **Current status:** bench-ready.
- **Works:** `DeviceAuthMiddleware` matches `X-Device-Id` GUID + `X-Api-Key`, sets `HttpContext.Items["DeviceId"]`, updates last-seen fire-and-forget. Three protected paths (`/api/chat`, `/api/audio`, `/api/devices/heartbeat`). Anti-enumeration on register (silent collision no-op, BCrypt timing normalization).
- **Weak:** API key is in plaintext at rest in the DB row (no HMAC + hash); no key rotation flow; no per-device revocation UI.
- **Evidence:** `DeviceServiceTests`, `DeviceAuthMiddleware` happy/sad path tests.
- **Score:** 75
- **Next action:** hash device API keys (BCrypt or HMAC-SHA256), expose parent-side key-rotate.

### Parent dashboard

- **Current status:** bench-ready, UX-unvalidated.
- **Works:** `wwwroot/parent.html` covers login → linked devices → summaries → flagged messages → conversation detail → audit feed → Today panel (server-aggregated, timezone-aware). Email verification flow, password reset flow, Google sign-in, account delete, per-child mode overrides, bedtime window, mode flags.
- **Weak:** No user testing recorded. Mobile responsiveness unverified. Single static HTML file — gets large.
- **Evidence:** ParentController tests, ConversationController tests, all parent-service test files (~10 files).
- **Score:** 78
- **Next action:** real parent UX session on a phone with 10+ flagged messages, 3+ devices, audit feed paginated.

### Audio / voice endpoint

- **Current status:** bench-ready, Story-only.
- **Works:** `POST /api/chat/audio` accepts WAV, runs Whisper STT, dispatches to ChatService, runs OpenAI TTS, returns MP3. Same pause/bedtime/mode gates as text path (pre-STT, no upstream cost). Blob persistence (`LocalDiskAudioBlobStore`), retention cascade C2.2a+b on parent / dormancy paths, parent-side `▶ Listen` for assistant audio with MIME whitelist.
- **Weak:** Story-only — Game / Riddle / Calm / Curiosity do not flow through voice. No streaming (buffered MP3). No barge-in. No retry on STT failure. Orphan blob sweeper deferred (C2.3).
- **Evidence:** `AudioChatControllerTests`, `LocalDiskAudioBlobStoreTests`, `ParentServiceAudioCascadeTests`, `RetentionPurgeServiceAudioCascadeTests`, `ParentMessageAudioTests`.
- **Score:** 65
- **Next action:** route at least Riddle through voice path; or document Story-only constraint in parent-facing UI.

### ESP32 browser prototype

- **Current status:** demo-ready on a controlled LAN.
- **Works:** Phone → wwwroot/index.html → device-register → `/api/chat` with X-Device-Id/X-Api-Key. Story page with choice handoff via `storySessionId` + `selectedChoice`. Parent dashboard separately.
- **Weak:** Legacy `esp32/ArmenianAiToy/ArmenianAiToy.ino` text sketch sends no auth headers → rejected 401 by middleware. Dynamic LAN IP requires WiFiManager portal re-entry on every IP change. No TLS.
- **Evidence:** `docs/esp32-chain.md`.
- **Score:** 65
- **Next action:** retrofit legacy text sketch OR mark it as deprecated and remove from repo.

### ESP32 voice MVP

- **Current status:** bench-ready prototype.
- **Works:** `AregVoiceMvp.ino` (button → INMP441 mic → WAV upload → backend → MP3 → minimp3 → MAX98357A → speaker). Five LED states, one canned failure clip, single per-turn latency log.
- **Weak:** Buffered playback only, no retry, no barge-in, no battery, no enclosure, HTTP only, BOOT button doubles as press-to-talk (fragile), config.h carries plaintext credentials in working tree (tracked file, with operator's `skip-worktree` guidance).
- **Evidence:** `esp32/AregVoiceMvp/README.md`, in-tree bench README.
- **Score:** 60
- **Next action:** wrap config.h secrets in untracked `secrets.h` include + add a CI check that grep'd-empty config.h is committed.

### TTS / speaker path

- **Current status:** working on bench voice MVP.
- **Works:** `OpenAITtsSynthesisService` with voice "Nova", MP3 output, used by `/api/chat/audio` and by the canned-failure-clip generator.
- **Weak:** Cost uncapped per turn (TTS for every voice response). No caching of common phrases. No voice identity test (model could swap voices mid-conversation if config changes).
- **Evidence:** Audio infrastructure tests; voice MVP README documents end-to-end.
- **Score:** 65
- **Next action:** add a TTS cost-cap counter + cache canned phrases (pause / bedtime / mode-disabled / fallback).

### Microphone path

- **Current status:** working on bench voice MVP.
- **Works:** INMP441 I²S RX at 16 kHz mono, 480 KB PSRAM capture buffer for 15s cap, debounced button.
- **Weak:** L/R pin must be tied to GND (silent failure mode otherwise — documented). 2.4 GHz only. Single capture session per turn.
- **Evidence:** `esp32/AregVoiceMvp/README.md`.
- **Score:** 65
- **Next action:** when adding a second board, validate the L/R-grounding instruction on a fresh assembly.

### Benchmark tools

- **Current status:** bench-ready instrument set.
- **Works:** 9 benchmark projects (Story / Game / Riddle / Calm / Curiosity individually + BenchmarkAll orchestrator + Mode / ChatApi / ParentHistory / ParentDemoSeed). Each writes `run_<ts>.md` + `.json` + `summary.json` artifacts. BenchmarkAll aggregates regression verdicts. `prompts.json` is SHA-pinned in `summary.json` so prompt edits cannot silently invalidate the verdict.
- **Weak:** Each run consumes real OpenAI tokens. Noise floor ~1-3 weak cases per 90-turn BenchmarkAll, not characterized over multi-day samples.
- **Evidence:** `tools/BenchmarkAll/Program.cs` (orchestrator), per-tool result MDs from 2026-04-30, 2026-05-17, 2026-05-18.
- **Score:** 75
- **Next action:** add a "noise-floor characterization" run set — 5 BenchmarkAll runs on a frozen branch, compute baseline noise distribution.

### Safety / moderation

- **Current status:** bench-ready with offline-tested fail-closed contract.
- **Works:** Dual moderation (input + output), `OpenAIModerationAdapter` fail-closes to `(IsSafe=false, ["moderation_unavailable"])` sentinel pinned by **32 offline-mocked tests** that pass in 1 second; `OpenAIReliabilityGate` retries 429/Timeout/5xx with backoff + circuit breaker; pause / bedtime / mode-disabled / Calm-bedtime gates before any model call; `DangerousInputFilter` heuristic. Violence false-positive override (0.40 / 0.50 ceilings) with story-request widening.
- **Weak:** No red-team corpus on record. Single-category violence override is the only false-positive escape — other categories could theoretically over-block child inputs.
- **Evidence:** `ModerationFailClosedTests` (32 tests, all offline since b203d67's clarification commit; the seam has existed for ~36 days), `OpenAIReliabilityGateTests`, `ChatGateEvaluatorTests`, `OpenAIFailureClassifierTests`.
- **Score:** 84
- **Next action:** assemble a small red-team corpus (10 unsafe Armenian prompts) and confirm 100% block rate.

### Persistence / database

- **Current status:** bench-ready, SQLite.
- **Works:** EF Core 9 migrations (11 of them now), `Migrate()` on startup, baseline-adoption documented for legacy DBs, `AppDbContextFactory` design-time, in-memory provider for tests.
- **Weak:** SQLite only — not validated against PostgreSQL or another production engine. No connection-pool sizing guidance.
- **Evidence:** Tests use `Microsoft.EntityFrameworkCore.InMemory`; migration files inspected.
- **Score:** 72
- **Next action:** run the test suite against PostgreSQL in CI (or document SQLite-only intent).

### Logging / audit

- **Current status:** bench-ready.
- **Works:** Structured JSON console logging (`JsonConsoleFormatter`, scopes, UTC timestamps). 14 audit event types (parent-account-deleted, parent-child-deleted, parent-device-unlinked, parent-password-changed, parent-device-pause-state-changed, parent-bedtime-window-set, parent-device-mode-flags-set, parent-data-exported, parent-conversation-deleted, parent-password-reset-requested/completed, parent-email-verified, parent-google-sign-in, child-mode-overrides-set, plus system-actor conversations-purged-by-retention / device-dormancy-deleted / parent-dormancy-anonymized). No foreign keys from audit to entities so rows outlive deletes.
- **Weak:** Logs go to stdout only — no file sink, no rotation, no retention policy. Host owns log retention.
- **Evidence:** `AuditController`, `AuditControllerTests`, ~10 ParentService test files exercising audit emission.
- **Score:** 80
- **Next action:** document a stdout-to-rotating-file expectation for any deploy target.

### Retention / cleanup

- **Current status:** bench-ready.
- **Works:** `RetentionPurgeService` background hosted service runs at configured interval; conversation purge by max-age; password-reset-token cleanup; email-verification-token cleanup; dormant device delete; audio blob cascade on parent / dormancy paths (C2.2a + C2.2b). Each system-actor pass writes a counts-only audit row.
- **Weak:** Orphan blob sweeper (C2.3) explicitly deferred. Audio path's dangling-reference disclaimer applies for any pre-C2 blobs left in `audio-blobs/`.
- **Evidence:** `RetentionPurgeServiceTests`, `RetentionPurgeServiceAudioCascadeTests`, audit-row inclusion test.
- **Score:** 78
- **Next action:** when the orphan-sweeper trigger conditions hit (audio-blobs > 5 GB, sustained nonzero failures, prod beta), land C2.3.

### Deployment readiness

- **Current status:** NOT ready.
- **Works:** `dotnet run --project src/ArmenianAiToy.Api` from a clone with a configured OpenAI key.
- **Weak:** No Dockerfile, no Helm chart, no docker-compose, no CI/CD pipeline, no SLO dashboards, no alerting, no health-check ladder beyond `/api/health`, no OpenAI cost cap, no incident playbook, no on-call.
- **Evidence:** `.github/` inspected (CI presence not visible from repo browsing). No Dockerfile in tree.
- **Score:** 40
- **Next action:** decide deploy target (single VPS / Fly.io / Render / etc.), write Dockerfile + basic deploy doc.

## Top 15 risks (ranked by severity)

1. **[P0 / production / child-safety]** No production deployment story. Anyone running Areg today is running it on a dev laptop. Any "real child use" needs a runbook for keeping the backend up.
2. **[P0 / privacy]** No external COPPA / GDPR review on record. Audit + retention + export are wired, but compliance posture is self-attested. Legal counsel before any beta with non-operator children.
3. **[P0 / reliability]** OpenAI cost is uncapped beyond a per-device rate limiter. A single buggy ESP32 hammering `/api/chat/audio` could rack up significant TTS+Whisper+chat costs.
4. **[P1 / quality]** Live-benchmark noise floor is ~1-3 weak cases per 90 turns and is **not characterized over multiple days** on the same branch. Push posture conflates "single-good-day evidence" with "noise floor dropped." Need a 5-run-on-frozen-branch characterization.
5. **[P1 / maintainability]** ChatService at 2347 lines is the architectural choke-point. Any future per-mode change has wide blast radius. Splitting per-mode dispatch / directive-building helpers out would localize change.
6. **[P1 / privacy]** Device API keys are plaintext at rest in `Device.ApiKey`. The export deliberately excludes them, but anyone with DB-row access has them. Should be hashed (BCrypt or HMAC).
7. **[P1 / production]** ESP32 voice MVP runs HTTP-only on the bench LAN. No TLS. A toy in a real home with shared Wi-Fi is exposed.
8. **[P1 / production]** Voice path is Story-only. A parent who buys "Areg the storyteller" and asks for a Riddle gets the text-only path silently. Either document this clearly in product UI or extend voice.
9. **[P1 / child-safety]** No red-team corpus on record. The fail-closed contract is solid; the actual block coverage on adversarial Armenian inputs is unmeasured.
10. **[P2 / reliability]** No circuit-breaker / retry policy on Whisper or TTS adapters. ChatService is wrapped by `OpenAIReliabilityGate`; audio adapters are not.
11. **[P2 / quality]** Calm F2 (Turn-2 distress-vs-arc cardinality coexistence) is documented-but-unresolved. Two prompt rules disagree on Turn-2 length and the prompt does not name precedence. Low-frequency failure mode.
12. **[P2 / quality]** Story choice template-verb monoculture (`Մոտենանք / Նայենք` opens ~50% of turns). Not benchmark-failing but qualitatively flat.
13. **[P2 / maintainability]** `StoryChoiceCoherenceGate` at 613 lines is dense; future story-choice changes will be expensive to reason about.
14. **[P2 / production]** Bench backend on `:5050` rebuild requires `dotnet build --output` to a temp dir to avoid the user's `:5000` API holding bin/ locks. This whole workflow assumes one operator with full machine access — fragile for any multi-developer setup.
15. **[P3 / maintainability]** CLAUDE.md claims 1250 tests; actual is 1336. Documentation drift is starting to show up at the count level.

## Highest-impact next tasks (top 20)

For each: P0/P1/P2 = priority; S/M/L = difficulty.

1. **[P0 / M]** **Per-device daily OpenAI cost cap.** Add a counter for chat + STT + TTS tokens per `DeviceId` per UTC day; refuse with the same canned envelope when cap is hit. *Affected:* `ChatGateEvaluator`, `AudioChatController`, new `OpenAICostMeter` helper. *Validation:* unit test triggers cap; integration test sees the canned reply.
2. **[P0 / M]** **Hash device API keys.** Store `ApiKeyHash` (BCrypt) in DB, validate via constant-time compare. *Affected:* `Device` entity, `DeviceService`, one migration. *Validation:* existing `DeviceAuthMiddleware` tests pass; new test confirms plaintext key not in DB row.
3. **[P0 / L]** **Decide deploy target + write Dockerfile + minimal deploy doc.** *Affected:* new `Dockerfile`, `docker-compose.yml`, `docs/deploy.md`. *Validation:* container builds, runs, hits `/api/health` from outside.
4. **[P0 / M]** **Red-team corpus + automated block-rate test.** 10 unsafe Armenian + 10 unsafe English prompts; assert 100% block by `OpenAIModerationAdapter`. *Affected:* new `tools/RedTeamCorpus/`, new test file. *Validation:* live run hits 20/20 blocks.
5. **[P1 / S]** **Add a CuriosityBenchmark scenario triggering «ավելի պատմիր».** Verifies the exemption path in FOLLOW-UP CONCISION. *Affected:* `tools/CuriosityBenchmark/prompts.json`. *Validation:* one new live run.
6. **[P1 / S]** **Resolve Calm F2.** Tighten BEDTIME-DISTRESS SHAPE to fit inside WIND-DOWN ARC cardinalities, or add an explicit arc-precedence line. *Affected:* `CalmModeInstruction` + `CalmPromptContentTests`. *Validation:* full suite + targeted Calm bench.
7. **[P1 / S]** **Lift PLURAL-IMPERATIVE OPENERS cross-mode.** Currently Game-only; add the same abstract ban to Story / Riddle / Calm / Curiosity. *Affected:* `ChatService.cs` (4 constants), 4 test files. *Validation:* `Assert.DoesNotContain("Եկեք", Prompt)` on each.
8. **[P1 / M]** **Wrap `OpenAIWhisperTranscriptionService` and `OpenAITtsSynthesisService` with the reliability gate.** *Affected:* `Infrastructure/Audio/*` + `OpenAIReliabilityGate`. *Validation:* unit tests for retry + circuit behavior on simulated 429/5xx.
9. **[P1 / S]** **Story first-verb-rotation rule.** Disallow CHOICE_A's first verb being the same as the previous CHOICE_A's first verb. Either prompt-side or coherence-gate-side. *Affected:* `StoryChoiceCoherenceGate.cs` or `StoryChoiceInstruction`. *Validation:* targeted Story benchmark.
10. **[P1 / S]** **Update CLAUDE.md test count to 1336** (and document the gap to `Esp32TestController*` untracked work).
11. **[P1 / M]** **Run a 5-sample noise-floor characterization** on the current `main` (consumes ~$10-15 OpenAI). Document the actual noise distribution per mode. *Affected:* `tools/quality-evidence/areg-noise-floor-characterization-YYYYMMDD.md`. *Validation:* report committed.
12. **[P1 / M]** **Voice path: extend at least Riddle.** Currently Story-only. Adds backend dispatch + AudioChatController coverage. *Affected:* `AudioChatController`, `ChatService.GetResponseAsync` voice path. *Validation:* `tools/test-chat-audio.ps1` extended.
13. **[P2 / S]** **Retrofit or remove legacy `esp32/ArmenianAiToy/ArmenianAiToy.ino`.** Either add device-auth headers or move to `esp32/_deprecated/`. *Affected:* one sketch file. *Validation:* docs/esp32-chain.md re-read.
14. **[P2 / M]** **Split ChatService by mode dispatcher.** Extract `BuildXxxTurnDirective` family into a `ModeDirectiveBuilder` class. *Affected:* `ChatService.cs` (large refactor). *Validation:* full test suite + targeted live re-run.
15. **[P2 / S]** **TTS canned-phrase cache.** Pre-render and cache the four canned replies (pause, bedtime, mode-disabled, safety-fallback). *Affected:* `CannedVoiceClips` or new `TtsCacheService`. *Validation:* gated-path turn skips OpenAI TTS.
16. **[P2 / S]** **PostgreSQL CI matrix.** Run the test suite against Postgres in CI to catch SQLite-only assumptions. *Affected:* `.github/workflows/`. *Validation:* CI green on Postgres.
17. **[P2 / M]** **Parent UX session.** Real phone, 10+ flagged messages, 3+ devices. Take notes on rough edges. *Affected:* `wwwroot/parent.html`. *Validation:* QA notes doc + fix prioritization.
18. **[P2 / S]** **Mode-detector audit on voice path.** Verify that Story-only-voice doesn't silently route a Game / Riddle request to Story prompt assembly via voice. *Affected:* `AudioChatController` + `ChatService` voice branch. *Validation:* unit test for the dispatch outcome.
19. **[P2 / S]** **ESP32 secrets hardening.** Wrap config.h secrets in untracked `secrets.h` include + add a CI check that grep'd-empty config.h is committed. *Affected:* `esp32/AregVoiceMvp/config.h` shape. *Validation:* fresh clone + `secrets.h.example` + reproducible bring-up.
20. **[P3 / L]** **COPPA / GDPR external review** before any non-operator child beta. *Affected:* legal counsel. *Validation:* signed review.

## Manual phone / ESP32 checklist

Run against a backend started from `main` HEAD at `a3c930f`, with the OpenAI key configured and the phone on the same Wi-Fi.

### Backend health

- [ ] `curl http://<laptop>:5000/api/health` → 200 `{"status":"ok",...}`
- [ ] `/metrics` returns 404 when no scrape token (concealment fail-closed).
- [ ] Login as parent at `/parent.html` → linked devices visible.

### Story mode (text)

- [ ] Open `http://<laptop>:5000/story.html`, tap "սկսել".
- [ ] First story turn: 3-5 sentences, no «Մի անգամ» / «Մի գեղեցիկ» opener.
- [ ] Two choice buttons visible. Both name a concrete entity from the body (not «ընկեր», not «ճանապարհ»).
- [ ] Tap CHOICE_A: continuation's first sentence visibly anchors on a ≥4-char stem from the choice label.
- [ ] No recap of the previous turn's setup in the continuation.

### Game mode (text)

- [ ] Open `http://<laptop>:5000/`, type `let's play` (or «խաղանք»).
- [ ] First reply: ONE clear Armenian instruction (one game type). Not a «what do you want to play» meta-question. Not «Եկեք խաղանք».
- [ ] Reply with the child action.
- [ ] Second reply: one short celebration + next round in the SAME game type, varied subtype. Not paired questions.
- [ ] Type «բավ է» (enough). Reply: warm one-line goodbye, no plead for more.

### Riddle mode (text)

- [ ] Type «տուր ինձ հանելուկ».
- [ ] First reply: clear riddle ending in «Ի՞նչ է։», no choice buttons, no answer.
- [ ] Reply with a wrong guess. Reply: gentle hint with a NEW physical clue. Not «ճիշտ չէ». Not the answer.
- [ ] Reply «նորից» mid-round. Reply: a FRESH riddle pose with «Ի՞նչ է։». Not a hint.
- [ ] Reply «չգիտեմ». Reply: gentle answer reveal + offer of next riddle.

### Curiosity mode (text)

- [ ] Type «Ինչու է երկինքը կապույտ».
- [ ] First reply: 1-3 short Armenian sentences. No «Հիանալի հարց» praise opener. No «Այս երևույթը» encyclopedia opener.
- [ ] Follow-up: «ինչու». Reply: SAME length or shorter than the previous. No second paragraph. No second example.
- [ ] Follow-up: «ավելի պատմիր». Reply: NOW allowed to be longer (this is the exemption path).

### Calm mode (text)

- [ ] Type «քնեմ» or «բարի գիշեր».
- [ ] First reply: soft tone, no exclamation, no question, one grounding anchor («Բարձիկը փափուկ է», «Շնչիր դանդաղ»).
- [ ] Reply «մթից վախենում եմ». Reply: 1-3 short sentences, NO echo of «վախ», one grounding anchor.

### ESP32 voice MVP (separate hardware bring-up)

- [ ] BOOT button press → LED red → speak Armenian sentence → release → LED yellow.
- [ ] LED green → speaker plays Armenian reply.
- [ ] Serial: `[latency] release->play_begin_ms=N` < 7000 ms.
- [ ] Three turns succeed back-to-back.
- [ ] Wi-Fi drop mid-upload → orange LED → canned failure clip plays.

### Parent dashboard

- [ ] Login.
- [ ] Linked devices list non-empty.
- [ ] Click a device → conversation summaries paginated.
- [ ] Flagged-messages tab → non-Clean rows ordered newest-first.
- [ ] Click a conversation → message timeline + assistant `▶ Listen` works.
- [ ] Audit feed → today's actions visible.
- [ ] Today panel → counts match summaries.

## Merge / push status

- **Current main:** `a3c930f Merge branch 'fix/moderation-tests-mock-sdk'` — clean, no staged files.
- **Evaluation branch (this session):** `evaluation/areg-current-readiness-20260518` — created locally on top of main; one docs commit will land here.
- **Prior evaluation branch:** `evaluation/areg-current-readiness` — still exists locally with the prior (now-retracted-on-#1) eval; NOT touched in this slice.
- **Pushed:** nothing in this evaluation session.
- **Merged:** nothing in this evaluation session.
- **Local noise:** unchanged from session start; the 6 documented untracked / locally-modified items remain unstaged.

# Child Runtime

## When to use

Invoke when the requested task changes how a child-facing request is accepted, orchestrated, moderated, or answered — anything between the device's HTTP call on `/api/chat` and the Armenian text that comes back. This is the runtime path that the child directly experiences.

Apply the moment any of these words appear in a task: `/api/chat`, ChatController, ChatService, runtime, request flow, orchestration, fallback, child-facing response, response shape, 502, sanitized body, retry, moderation routing, Path-1/2/3/4/5, assistant fallback, safety fallback, runtime regression.

## Tasks that belong here

- `ChatController.Chat` entry-point behavior: input guards, exception handling, sanitized wire shape.
- `ChatService.GetResponseAsync` orchestration between the existing runtime paths (moderation, normalization, prompt building, story-intent detection, AI call, tail-block handling, fallback branches).
- Child-facing response-shape discipline: `ChatResponse` fields, `SafetyFlag` propagation, choice block emission.
- Runtime error and safe-fallback handling on child-facing surfaces (Armenian fallback text, retry-fallback persistence, input-moderation fallback persistence).
- Routing between existing runtime paths when a fix is narrow and local (not a reshape of orchestration).
- Runtime regression fixes — a concrete, observable misbehavior on the `/api/chat` path that a narrow diff can close.
- Child-facing deterministic runtime tests: controller-level shape tests, service-level orchestration tests that use existing seams (NSubstitute, `StubAdapter`, `InMemory` DB).
- ESP32 / device firmware is the HTTP client of this runtime. If the device's contract is affected, state it explicitly and keep changes backward-compatible unless firmware work is coordinated.

## Tasks that do NOT belong here

- Mode-specific prompt tuning, tone changes, or benchmark baseline refreshes — see `mode-workstreams`.
- Parent dashboard / account / self-service / monitoring — see `parent-control`.
- JWT / auth / password / secret / sanitized-parent-path work unless the runtime task is directly inseparable from it — see `auth-security`.
- ESP32 / hardware / audio pipeline / on-device firmware.
- Broad infrastructure refactors, DI reshuffling, new middleware.
- Benchmark architecture work, per-tool baseline refreshes, tolerance tuning.
- Child profile CRUD on the parent side (that's `ChildController` + parent JWT → `parent-control`).

If a task mixes child-runtime work with any of the above, split it.

## Child-runtime guardrails

- **Preserve child safety first.** Dual moderation (input + output), fail-closed posture, Armenian safe-fallback text — never soften these. Tightening is fine; loosening needs explicit approval.
- **Do not weaken moderation or fail-closed behavior.** `OpenAIModerationAdapter`'s fail-closed contract and the retry-on-429-once rule are invariants. Tests in `ModerationFailClosedTests` lock them in.
- **Do not leak internal errors to the child-facing surface.** The `/api/chat` 502 path returns a constant sanitized body (`"AI service unavailable. Please try again."`). No exception messages, request-ids, URLs, or stack markers. The Path-5 sanitization is pinned — do not regress it.
- **Prefer narrow runtime fixes over orchestration redesign.** `ChatService.GetResponseAsync` is 1905 lines by design; walking it end-to-end is expected. Find the exact branch the fix needs, and edit only there.
- **Do not silently change mode behavior while claiming a runtime-only change.** If the fix alters what a mode produces (tone, length, choice shape), it's a mode task — hand off to `mode-workstreams`.
- **Do not widen response contracts casually.** Adding a field to `ChatResponse` changes what every device parses. Removing or renaming a field is a breaking change. Either requires a plan and approval.
- **Do not bundle unrelated controller/service/UI work.** A runtime fix is not the right commit to also touch ConversationController, parent.html, or ChildController.
- **Do not reopen benchmark-split cleanup.** If a runtime fix surfaces a benchmark regression, hand the refresh to `mode-workstreams`; don't collapse or restructure benchmarks.
- **Preserve existing design decisions.** Last 20 messages as context, 30-minute conversation inactivity expiry, in-memory `ConcurrentDictionary` label handoff with 30-minute expiry, 30-second OpenAI call timeout — all of these are deliberate and pinned by tests.
- **Armenian is the output language.** Any hardcoded fallback string added to the runtime path must be Armenian. Log messages and internal error strings stay English.

## Change classification guide

Pick exactly one before editing.

### 1. Test-only runtime regression pin
- Add a test that locks in an existing runtime-sensitive behavior (sanitized 502 body, fail-closed moderation, SafetyFlag persistence, tail-block stripping before storage).
- No production edit.
- Risk: LOW. Precedents: `5e2b5d3`, `d598f61`.

### 2. Runtime bug fix
- A concrete, reproducible misbehavior on `/api/chat` with a narrow fix site. Examples: a single catch branch, a single sanitization, a single null-guard.
- One controller/service file + targeted tests.
- Risk: LOW-to-MEDIUM. Precedents: `f71b16d` (Path-5 sanitization), `9333e73` (input-moderation fallback as Flagged), `438c3e8` (retry-fallback as Flagged).

### 3. Child-facing response-shape change
- Adds, tightens, or sanitizes a field on `ChatResponse` or an error body. Backward-compatibility for the device client is a first-class concern.
- MEDIUM risk. If additive (new optional field), proceed with a plan. If breaking, STOP and request approval.

### 4. Runtime fallback/sanitization change
- Changes how the runtime behaves when something upstream fails (moderation unavailable, chat completion exception, malformed tail block).
- Must pair with a regression test and — if the fallback text is child-facing — an Armenian-correctness check.
- Risk: MEDIUM.

### 5. Routing / orchestration change
- Reorders steps inside `ChatService.GetResponseAsync`, changes which path a given input takes, or introduces a new branch point.
- HIGH risk — reorderings can reintroduce safety holes or mode drift. STOP and request approval with a plan naming the exact lines.

### 6. High-risk runtime redesign requiring approval
- Touching central `ChatService` shape, introducing a new engine / state machine, changing the moderation pipeline, modifying the system prompt, or touching `ModeDetector` priority rules.
- STOP and request approval. Produce a plan with exact files and lines.

## Default working style

- **Inspect first, edit later.** Read `ChatController.Chat`, the relevant slice of `ChatService.GetResponseAsync`, the adjacent helpers (`TailBlockParser`, `ChoiceNormalizer`, `ModeDetector`, `ResponseCleaner`, `ResponseQualityGate`), and the tests that already cover the path.
- **Identify child-facing blast radius.** Every runtime edit can change what the child sees. Before editing, state in one sentence what a child will hear differently, and how you'll test it.
- **Classify before editing** using the change-classification guide above. Name the classification in the report.
- **Prefer the smallest safe diff.** Runtime bugs are one-catch-block or one-predicate fixes most of the time. Refactors "while you're in there" are the exact pattern the guardrails exist to block.
- **Reuse existing runtime patterns.** `catch (Exception) → StatusCode(502, controlled-body)` for sanitized errors; `SafetyFlag.Flagged` for fallback persistence; the existing in-file seams (`ClassifyOnceAsync`, `StubAdapter`, pending-label `ConcurrentDictionary`) for testability.
- **Keep layers aligned only when they must change together.** A controller-body change needs a controller test; a service-branch change needs a service test. Don't touch both unless both must move.
- **Stop and ask for approval when touching central orchestration broadly.** Classifications 5 and 6 are hard stops. If the diff starts crossing multiple branches in `ChatService`, re-plan.
- **Report clearly and explicitly.** Use the report format below. Validation Results must include a targeted test filter + build + full suite.

## Approval required before editing

Stop and request explicit approval when the task would:

- Change central `ChatService` architecture broadly (the orchestration method's shape, the top-level try/catch envelope, the ordering of moderation → normalization → prompt build → AI call → tail block).
- Change moderation or fail-closed behavior broadly (beyond a targeted branch inside the existing contract).
- Change response schema in a breaking way (remove/rename a `ChatResponse` field, change a status code, change the sanitized-body wording in a contract-breaking way).
- Change routing across multiple child-facing paths at once (Path-1 + Path-5, or three separate catch branches).
- Mix runtime changes with mode prompt redesign (split the commit; hand the prompt half to `mode-workstreams`).
- Add new runtime abstractions (a state machine, a router class, a new pipeline stage) that widen scope beyond the fix at hand.

For approval cases produce a plan with exact files / lines / classification, and wait for explicit "approved — proceed" before touching code.

## Testing guidance

- **Controller test when** the `ChatController` response shape, status code, or sanitized body contract is load-bearing. Use `Substitute.For<IChatService>()` and `DefaultHttpContext` with `HttpContext.Items["DeviceId"] = Guid.NewGuid()` per the precedent in `ChatControllerPath5Tests`.
- **Service test when** a specific orchestration branch, fallback path, or persistence side-effect is load-bearing. Prefer narrowly-targeted tests over full-integration ones — the orchestration is long; isolated branch coverage is what catches regressions.
- **Both when** the runtime contract spans layers: the service emits a signal (e.g. a specific `SafetyFlag` value) and the controller projects it to the wire.
- **How to pin sanitized child-facing error behavior.** Stub the service to throw an exception whose message carries every leak marker the sanitization exists to suppress (`request-id`, `https://`, `OpenAI`, a unique sentinel). Assert status 502, exact sanitized body string via reflection on the anonymous payload, AND `JsonSerializer.Serialize(obj.Value)` contains none of the leak markers. Precedent: `5e2b5d3` (`Chat_WhenChatServiceThrows_Returns502WithSanitizedBody`).
- **How to keep runtime tests deterministic.** Never hit the live OpenAI SDK. Never depend on wall-clock. Never depend on the network. Use NSubstitute, the existing `StubAdapter` / `ClassifyOnceAsync` seam in `OpenAIModerationAdapter`, and EF Core InMemory for data.
- **How to avoid live-SDK/network dependency.** `ClassifyOnceAsync` is a `protected virtual` seam already in the moderation adapter — subclass it in tests, override with a scripted queue. For chat, always mock `IChatService` rather than wiring the real service.
- **Use existing seams and patterns** wherever they exist: the in-file `StubAdapter` pattern in `ModerationFailClosedTests`, the `Substitute.For<IChatService>()` pattern in `ChatControllerPath5Tests`, the `TestDbContext` + InMemory pattern for persistence assertions. Don't invent a second way to do what these already do.
- **Include anti-tautology guards** on every negative-assertion cluster. A test that asserts "returns 502 with sanitized body on exception" needs a paired "returns non-502 on success" so a future regression that makes the endpoint always-502 fails distinctly. Precedent: `Chat_WhenChatServiceSucceeds_DoesNotReturn502` in `ChatControllerPath5Tests`.

## Output expectations

For any non-trivial child-runtime task, return in this exact structure:

1. Current State
2. Change Decision
3. Files Changed
4. Diff Summary
5. Validation Results
6. Risks / Tradeoffs
7. Exact Commit Message Suggestion

Under **Change Decision**, state the classification (1–6 above) and one sentence explaining why.

Under **Validation Results**, always include:
- Targeted test filter result.
- `dotnet build` result.
- Full `dotnet test` result.
- `git status --short` showing that `.claude/settings.local.json` stayed unstaged and no unintended files were modified.

## Repo-aware examples

### Good use: pinning ChatController sanitized 502 behavior
- Classification: (1) test-only runtime regression pin.
- Stub `IChatService` to throw an `InvalidOperationException` carrying `request-id=…`, `https://api.openai.com/…`, and a unique `LEAK-SENTINEL-…`. Assert 502 + exact sanitized body + no leak markers in the serialized body. Add the anti-tautology guard.
- Precedent: `5e2b5d3`.

### Good use: a narrow child-facing fallback fix
- Classification: (2) runtime bug fix.
- Example: persisting an input-moderation assistant fallback as `SafetyFlag.Flagged` rather than `Clean`. One branch edit in `ChatService`, one targeted test that inspects the saved message's `SafetyFlag`.
- Precedent: `9333e73` (input-moderation fallback), `438c3e8` (retry-fallback).

### Good use: a test-only runtime regression pin (de-flaking)
- Classification: (1) test-only runtime regression pin.
- Remove a live-SDK dependency from a moderation test by reusing the existing `ClassifyOnceAsync` seam. Two flaky tests become deterministic; no production change.
- Precedent: `d598f61`.

### Good refusal: refusing to treat a mode prompt rewrite as a runtime-only task
- Task: "While you're fixing the Path-5 502 body, also rewrite the Calm mode prompt for warmth."
- Correct response: refuse to bundle. The Path-5 fix is child-runtime; the Calm prompt rewrite is `mode-workstreams`. Ship the runtime fix here, hand the prompt work to the mode skill in a separate commit. Mixing churns two risk domains through one review.

### Good refusal: refusing a broad ChatService redesign in a narrow bugfix task
- Task: "The orchestration is getting long — while fixing this one catch branch, extract a StoryFlowEngine class and a ModerationPipeline class."
- Correct response: refuse. Classification (6). Orchestration redesign is a multi-commit arc with safety implications; the narrow bug fix ships as-is. Produce a plan for the refactor separately if it's a real priority.

## What this skill composes with

- `change-decision` — always run first to confirm Minimal code change / Test-only / Review-only before touching anything.
- `pre-commit-check` — final gate before committing; runs the standard build + test validation.
- `mode-workstreams` — invoke together when a runtime task genuinely crosses into mode behavior (a transition fix, a mode-specific fallback). The mode skill owns the prompt/benchmark angle; `child-runtime` owns the orchestration/response-shape angle.
- `auth-security` — invoke together when a runtime issue is also trust/security-sensitive (sanitization, leak markers, fail-closed). `auth-security` owns the secret/config/sanitization classification; `child-runtime` owns the /api/chat path itself.
- `parent-control` — invoke only when a task genuinely crosses into parent-facing territory (e.g. a runtime change that also requires updating how a parent sees a conversation's safety flag). The parent surface is out of scope by default.
- `minimal-csharp-change` — honor its smallest-safe-diff discipline on every runtime edit.
- `story-flow-review` — use when the task touches the story-choice pipeline end-to-end (tail-block emission, parsing, normalization, prompt injection, unclear handling, expiry).
- `repo-workflow` — inspection-first pass discipline applies especially to runtime work where orchestration is long.

## Constraints

- Do NOT weaken moderation, fail-closed posture, or Armenian safe-fallback behavior.
- Do NOT leak internal exception detail into any child-facing response body.
- Do NOT reshape `ChatService` orchestration as a side effect of a bug fix.
- Do NOT silently change mode behavior under the banner of a runtime fix.
- Do NOT change `ChatResponse` fields or status codes in a breaking way without approval.
- Do NOT introduce new runtime abstractions (engines, routers, pipelines) to solve a one-branch bug.
- Do NOT bundle runtime fixes with unrelated controller, UI, parent, or benchmark work.
- Do NOT reopen the per-mode benchmark split in a runtime commit.
- Do NOT skip the classification step from the change guide.
- Do NOT use live OpenAI SDK, network, or wall-clock in runtime tests — use existing seams.

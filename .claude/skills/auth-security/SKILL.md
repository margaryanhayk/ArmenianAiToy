# Auth Security

## When to use

Invoke when the requested task changes anything in the repo's authentication, authorization, secret handling, or security-sensitive response shape. This includes JWT signing and validation, parent login/register/password flows, ownership checks at the controller or service layer, idempotent no-existence-leak responses, sanitization of sensitive error paths, and any security-focused test.

Apply the moment any of these words appear in a task: JWT, signing key, secret, password, hash, BCrypt, login, register, change password, ownership, authorize, authentication, 401, 403, 404 for not-yours, token, API key, fail-closed, sanitized body, existence leak, secret default.

## Tasks that belong here

- JWT configuration: `Jwt:Key`, `Jwt:Issuer`, `Jwt:Audience`, signing-key validation, `TokenValidationParameters`.
- Login / register / password-policy / change-password flows.
- Password storage and verification (BCrypt), password-strength rules.
- Ownership authorization checks at controller or service boundary (claim-based parentId, linked-device sets).
- Idempotent no-existence-leak response shapes on security-sensitive endpoints.
- Sanitizing internal error leaks into wire responses (the ChatController Path-5 pattern).
- Secret / config fail-fast behavior at startup.
- Security-sensitive controller / service changes that fix, tighten, or pin the above.
- Security-focused tests: negative tests for missing keys, tests that assert a service is NOT called on an unauthorized path, tests that assert body shape does not leak internals.

## Tasks that do NOT belong here

- Benchmark tooling, per-mode baselines, or any child-facing mode prompt/style work — see `mode-workstreams`.
- Hardware, audio, ESP32, or on-device firmware work.
- Broad `ChatService` / orchestration / system-prompt changes that are not specifically about a security behavior (hard-stop territory).
- Device bootstrap / registration (`POST /api/devices/register`) — not an auth change, device-side not parent-side.
- Generic repo cleanup, rename passes, DI reshuffling, or CSS/JS polish.
- Product UX work that isn't trust/security-sensitive — see `parent-control` for dashboard/polish.

If a task mixes security hardening with any of the above, split it.

## Auth / security guardrails

- **Prefer minimal hardening over broad redesign.** A 3-line fail-fast check beats a 300-line options binding. A single guard beats a new framework.
- **No secret defaults in shipped config.** `appsettings.json` must not ship with real or placeholder secrets that "just work." Fail fast at startup if a secret is unset (precedent: `8d8e120`).
- **Never leak internal errors into wire responses.** Exception messages may contain request-ids, URLs, stack markers. Return a controlled sanitized body; log the detail server-side (precedent: `5e2b5d3`).
- **Never leak existence across ownership boundaries.** "Not yours" and "doesn't exist" must look identical on the wire — same status, same body shape (precedent: `055792e`).
- **Keep ownership checks explicit.** Read the caller identity from the JWT `NameIdentifier` claim; never trust parentId from URL/body/header. Enforce at both service (data predicate) and controller (response shape) where both matter.
- **Require extra care for permission changes.** Any widening of who can do what (parent → admin, one parent → another family's data, read → write) demands a plan and approval before editing.
- **Do not weaken security for convenience.** Tightening is acceptable by default; loosening a guard, a check, a tolerance, or a response shape must be justified in the commit message and explicitly approved.
- **Do not silently change token/auth behavior broadly.** A narrow hardening fix does not renegotiate token lifetime, claim shape, or validation parameters on the side.
- **Cryptographic routines stay in tested libraries.** Don't roll BCrypt, HMAC, or JWT signing code — use the existing packages.
- **Constant-time comparison for any hash/secret compare** once you touch that code. Inherit the library's behavior; don't substitute `==` on secret material.
- **Log email for operational trace, never passwords/tokens/hashes.** No `{Password}`, `{Token}`, `{ApiKey}`, `PasswordHash` structured log fields anywhere — confirmed across `backend/src` today.

## Change classification guide

Pick exactly one before editing.

### 1. Test-only security regression pin
- Add a test that locks in an existing security-sensitive behavior (no-existence-leak, no-internal-leak, fail-closed on moderation, auth short-circuit).
- No production edit.
- Risk: LOW. Precedents: `055792e`, `5e2b5d3`, `d65f8b7`, `d598f61`.

### 2. Minimal hardening fix
- A surgical guard on a specific input, response, or config path (e.g. password min-length, sanitized 502 body, fail-fast on missing secret).
- Often one controller or service file + targeted tests; maybe one config line.
- Risk: LOW-to-MEDIUM depending on the path. Precedents: `0082848`, `5e2b5d3`, `f71b16d`.

### 3. Parent-account security improvement
- New security-focused parent action or guard (password strength, change-password, session lifetime hint, future rate-limit on login).
- Controller + service + possibly small UI + targeted tests. Coordinate with `parent-control` for UX.
- Risk: MEDIUM. New endpoints require a plan + approval.

### 4. Ownership / privacy behavior change
- Tightens or pins who can see/act on what (parent→linked-device scope, no-existence-leak on GetById, claim-based parentId on new action).
- Controller-level tests mandatory; service-level test mandatory when ownership predicate lives in the service.
- Risk: LOW-to-MEDIUM when tightening an existing pattern; HIGH when changing who is allowed.

### 5. Config / secret handling change
- Removes an insecure default, adds a fail-fast, rotates how a secret is supplied (user-secrets, env var, key vault later).
- Must validate that all existing tests still green AND that the app fails fast at startup on misconfiguration.
- Risk: MEDIUM. Precedent: `8d8e120`.

### 6. High-risk auth redesign requiring approval
- Anything touching the auth architecture: JwtBearer wiring, claim shape, token lifetime, password storage scheme, new middleware, schema/migration for security data, multi-layer refactor beyond a narrow fix.
- STOP and request approval. Produce a plan with exact files and lines.

## Default working style

- **Inspect first, edit later.** Before any edit, read every site that touches the value you're changing (grep for `Jwt:Key`, `Password`, `ApiKey`, `ex.Message`, etc.). Confirm the blast radius.
- **Identify blast radius.** Name every consumer of the touched code and say, in one sentence, how the change affects each. If a consumer would need to change, split it out or stop and report.
- **Classify before editing.** Say the classification (1–6 above) explicitly in the report.
- **Prefer the smallest safe diff.** Auth fixes especially want surgical edits — a single guard line, a single `throw`, a single `TryParseExact`. Don't refactor callers "while you're in there."
- **Reuse existing patterns.** `IsNullOrWhiteSpace` + length-guard for password policy; `DateOnly.TryParseExact` for DOB; `!linkedDevices.Contains(deviceId) → Forbid/NotFound` for ownership; inline `throw new InvalidOperationException(...)` at startup for missing secrets; `catch (Exception) → StatusCode(502, controlled-body)` for sanitized 502s. All of these are already in the codebase — extend them, don't replace them.
- **Keep layers aligned only when they must change together.** A controller contract change implies test + UI updates in the same commit. A service-only tightening does not need a controller touch.
- **Stop and ask for approval when changes broaden.** If a "minimal fix" starts touching DI, JwtBearer options, `ModerationClient` configuration, or more than one controller, that's a different classification — stop and re-plan.
- **Report clearly and explicitly.** Use the report format below. Validation Results must include a manual startup check when a config-or-secret path changed (see `8d8e120`).
- **Armenian quality is NOT a concern here.** Error messages on security paths are for operators; match the repo's existing English conventions.

## Approval required before editing

Stop and request explicit approval when the task would:

- Change auth architecture (JwtBearer options, token validation parameters, authentication scheme, authorization policies).
- Change token format, claim shape, or lifetime semantics broadly.
- Change permissions (who can do what) — not just tightening an existing check, but broadening or redistributing capability.
- Add new auth middleware behavior (a rate-limit pipeline, a CSRF layer, a revocation list).
- Change the password-storage approach (BCrypt → Argon2, salt pepper, credential rotation).
- Change the schema or introduce a migration for security-relevant data (API-key hashing at rest, session table, revocation list).
- Touch multiple layers — controller + service + middleware + DB — in a way that exceeds a narrow fix.

For approval cases produce a plan with exact files / lines / classification, and wait for explicit "approved — proceed" before touching code.

## Testing guidance

- **Controller test when** the response shape, status code, ownership short-circuit, claim-based argument flow, or sanitized-body contract is load-bearing. Use NSubstitute for the service plus `DefaultHttpContext` + `ClaimsPrincipal` per the precedent in `ChatControllerPath5Tests`, `ConversationControllerOwnershipTests`, `ChildControllerOwnershipTests`, `ParentControllerRegisterTests`, and `ParentControllerUnlinkDeviceTests`.
- **Service test when** data-access logic, ownership predicates, SaveChanges behavior, or fail-fast logic sits in the service. Use the `TestDbContext` + `CreateService(jwtKey?)` helper pattern in `ParentServiceAuthTests`.
- **Both when** the feature has a real end-to-end security contract. Examples: unlink (controller shape + service predicate isolation), JWT-key hardening (service throws + controller doesn't swallow).
- **How to pin no-existence-leak behavior.** Add two tests with the SAME expected result shape: one where the resource doesn't exist, one where it exists but is not owned. Assert both produce the same `Assert.IsType<...>` AND the same body. Add negative assertions: `Assert.IsNotType<ForbidResult>(result)`, `Assert.IsNotType<OkObjectResult>(result)` — so a future drift into a distinct "not yours" code fails distinctly. Precedent: `055792e` (`GetById_WhenConversationExistsButDeviceNotLinked_ReturnsNotFound`).
- **How to validate secrets/config fail-fast behavior.** Two complementary tests: one with the value missing (`null` / empty / whitespace), one with the known-bad legacy literal. Both must throw the same `InvalidOperationException`. For startup paths that can't be unit-tested directly (Program.cs), do a manual run (`dotnet run --project src/ArmenianAiToy.Api`) and confirm the process exits with the expected message — include the captured output in Validation Results. Precedent: `8d8e120` (`LoginAsync_WhenJwtKeyMissing_Throws` + `LoginAsync_WhenJwtKeyIsLegacyDefault_Throws` + manual startup output).
- **How to validate service short-circuit behavior.** Any guard that short-circuits before a service call must be pinned with `await service.DidNotReceiveWithAnyArgs().<Method>(default, default!, ...)`. A correct response from an endpoint that still reached the DB is still wrong (ownership, weak-password, malformed-input, etc.). Precedents: the `_AndDoesNotCallService` suffix on `CreateChild_WhenDeviceNotLinked_…`, `Register_WhenPasswordTooShort_…`, `UnlinkDevice_…`.
- **Keep tests narrow and deterministic.** Do NOT reach the live OpenAI SDK, do NOT depend on wall-clock, do NOT depend on network. Use the existing `StubAdapter`/`ClassifyOnceAsync` seam for moderation; use `Substitute.For<IChatService>()` for chat; use `TestDbContext` + InMemory for data. Precedent for de-flaking: `d598f61`.
- **Anti-tautology guards are mandatory** on every negative-assertion cluster: at least one positive-path test that would fail if the whole action inverted. Without it, a regression that makes the endpoint always-401 or always-NotFound silently passes.

## Output expectations

For any non-trivial auth/security task, return in this exact structure:

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
- For any config/secret path change, a manual startup check: `dotnet run --project src/ArmenianAiToy.Api` result captured.
- `git status --short` showing that `.claude/settings.local.json` stayed unstaged and no unintended files were modified.

## Repo-aware examples

### Good use: requiring `Jwt:Key` to be explicitly configured
- Classification: (5) config/secret handling change.
- `Program.cs:26` + `ParentService.GenerateJwt` both fail fast on null / empty / whitespace OR the legacy literal. `appsettings.json` no longer ships the default. Two service-level negative tests. Manual startup verified.
- Precedent: `8d8e120`.

### Good use: minimum parent password length guard
- Classification: (2) minimal hardening fix.
- One controller-side guard after the existing `IsNullOrWhiteSpace` check. No DTO, no service mirror, no NuGet. Three controller tests: too-short, valid, missing.
- Precedent: `0082848`.

### Good use: pinning no-existence-leak contract on `GetById`
- Classification: (1) test-only security regression pin.
- Two tests with identical expected shape — "doesn't exist" and "exists but not yours" — plus negative `Assert.IsNotType` guards and an anti-tautology happy-path test.
- Precedent: `055792e`.

### Good use: sanitizing ChatController Path-5 502 body
- Classification: (2) minimal hardening fix.
- Catch-all branch returns a constant sanitized string; internal detail stays in server logs. Controller test uses an `InvalidOperationException` carrying leak markers (`request-id`, `https://`, `OpenAI`, unique sentinel) and asserts none appear in the serialized body.
- Precedent: `5e2b5d3` (pin) / `f71b16d` (production fix).

### Good refusal: refusing a broad Device.ApiKey storage redesign in a narrow task
- Task: "While you're in there, please hash Device.ApiKey at rest."
- Correct response: refuse to bundle. API-key-at-rest hashing is a schema change + migration + every consumer of `.FirstOrDefaultAsync(d => d.ApiKey == apiKey)` needing a constant-time hashed compare + a rotation plan for existing devices. Classification (6) — stop, produce a plan, request approval.

### Good refusal: refusing to soften a no-existence-leak invariant
- Task: "Return 403 on 'not yours' instead of 404 — it's more REST-correct."
- Correct response: refuse. The shared 404 shape is the no-existence-leak contract (documented in code + CLAUDE.md + pinned in `055792e`). A 403/404 split re-introduces the enumeration channel. If the reporter wants a behavioral change here, it requires product + security review — classification (6).

## What this skill composes with

- `change-decision` — always run it first to confirm Minimal code change / Test-only / Review-only before touching anything.
- `pre-commit-check` — final gate before committing; runs the standard build + test validation.
- `parent-control` — use together when the security fix sits on a parent-facing action (login, register, unlink, password change). `parent-control` carries the UX/ownership/dashboard angle; `auth-security` carries the secret/config/sanitization/classification angle.
- `mode-workstreams` — invoked ONLY when a task genuinely crosses boundaries (e.g. a safety-tightening in a mode prompt that is also the fix path for a security concern). If the task is purely about a mode's tone or benchmark, it is a mode task, not an auth task.
- `minimal-csharp-change` — honor its smallest-safe-diff discipline on every auth edit.
- `repo-workflow` — inspection-first pass discipline applies especially to auth/secret changes where blast radius matters.

## Constraints

- Do NOT ship secret defaults in `appsettings.json` or any committed config.
- Do NOT concatenate `ex.Message` into any response body on security-sensitive or parent-facing paths.
- Do NOT use distinct status codes to differentiate "not yours" from "doesn't exist" on the same action.
- Do NOT read `parentId` / `deviceId` from URL or body when the authoritative source is the JWT claim.
- Do NOT introduce new auth middleware, new claim types, or new token-validation parameters without an approval cycle.
- Do NOT remove or soften an ownership test, a no-existence-leak assertion, a fail-fast secret check, or an anti-tautology guard without explicit approval.
- Do NOT roll custom cryptography (hash, HMAC, JWT sign) — inherit the library.
- Do NOT log passwords, tokens, hashes, or API keys.
- Do NOT bundle an auth hardening commit with unrelated UI polish, mode prompt work, or benchmark refresh.
- Do NOT skip the classification step from the change guide.

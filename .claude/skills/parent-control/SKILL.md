# Parent Control

## When to use

Invoke when the requested task changes anything a parent sees, does, or authenticates against: the parent dashboard, the `ParentController`, `ParentService`, `ConversationController`, `ChildController` (parent-authorized actions), `parent.html`, or parent-specific tests. Device linking/unlinking lives in `ParentController` (not `DeviceController`) and IS in scope; device bootstrap / registration (`POST /api/devices/register`) is NOT.

Apply the moment any of these words appear in a task: parent, parent dashboard, login, register, JWT, linked devices, unlink, link device, monitoring, flagged messages, conversation summary, child profile (from the parent side), password, parent-facing UI, parent-facing endpoint.

## Tasks that belong here

- `ParentController` changes (register, login, link, unlink, devices list/details, any new parent action)
- `ParentService` behavior used by parent-authorized flows
- `ConversationController` work (parent monitoring) — ownership, pagination, no-existence-leak, shape of summary/flagged/detail responses
- `ChildController` work when the caller is the parent (CreateChild, GetChildren, DOB validation, ownership Forbid)
- `parent.html` features, empty states, dashboard polish, device-row actions, confirmation flows
- Parent self-service endpoints (change password, unlink device, future logout-all, future email change)
- Read-only monitoring surfaces (summary rows, flagged lists, conversation detail)
- Controller/service/UI tests scoped to parent-authorized behavior
- Auth/security changes that specifically protect the parent surface (JWT signing-key hygiene, minimum password length, login guards)

## Tasks that do NOT belong here

- Child-facing runtime and mode behavior (Story/Game/Riddle/Curiosity/Calm) — see `mode-workstreams`
- Benchmark architecture and per-mode baselines — see `mode-workstreams` / `benchmark-run`
- Hardware, audio, ESP32, on-device work
- Speech/audio pipeline
- Device bootstrap / registration (`POST /api/devices/register`, `DeviceController`, ESP32 provisioning) — device-side, not parent-side
- `ChatService` orchestration and the global system prompt (hard-stop territory)
- Moderation/safety core (`OpenAIModerationAdapter` override logic, fail-closed paths)
- Infrastructure refactors that aren't specifically about a parent feature (DI rework, generic middleware redesign)

If a task mixes parent-facing work with any of the above, split it.

## Parent-trust guardrails

- **Preserve parent trust as the product's core promise.** Parents bought a thing that their child talks to. Every parent-facing action must be clear, explicit, and conservative — never surprising.
- **Prefer explicit over clever.** Confirm destructive actions. Label clearly. Use plain messages.
- **Never leak internal errors to the wire.** Controlled sanitized bodies, never `ex.Message` concatenated into responses (this is how the ChatController Path-5 sanitization got pinned). If a service throws unexpectedly, return a controlled message and log the detail server-side.
- **Never leak existence across ownership boundaries.** "Not yours" and "doesn't exist" must be indistinguishable from the wire — same status, same body shape (see `ConversationController.GetById` precedent).
- **Preserve the read-only boundary over child data.** The parent dashboard is observational. Do not casually add mutation of children, conversations, or messages. Parent self-service acts on *their own* account and link state, not on the child's generated content.
- **Parent self-service is allowed;** child-data mutation is not — unless the task explicitly requests it, a plan is produced, and approval is obtained.
- **Auth and security-sensitive changes require extra care.** State the security rationale, keep diffs surgical, add tests that pin the invariant, and never silently broaden scope.
- **Do not widen parent features into admin-style control.** A parent managing their own account is in scope; a parent acting on another family's data or on the device from a position above it (remote device wipe, remote mute, etc.) is a new feature class that requires explicit product approval.
- **Tokens are bearer-only today.** Do not introduce cookie-based auth or cross-site-credential flows without an explicit security review.

## Change classification guide

Pick exactly one before editing.

### 1. UI-only parent polish
- Changes to `parent.html` (empty states, confirmation text, layout, disabled states).
- No backend edit. No contract change.
- Risk: LOW.

### 2. Controller/service behavior change
- Adjusts an existing parent-authorized action's shape, status code, or error contract.
- Usually paired with controller-level tests; may need a service test too.
- Risk: LOW-MEDIUM.

### 3. Auth/account hardening
- Password policy, JWT validation, rate limits on auth endpoints, credential storage.
- HIGH risk per CLAUDE.md when it touches signing keys or auth plumbing. Produce a plan, pause for approval.
- Precedents: `8d8e120` (Jwt:Key fail-fast), `0082848` (min-password-length).

### 4. Parent self-service feature
- New parent-account or device-management action (unlink, change password, etc.).
- MEDIUM risk (new endpoint) — plan + approval. Typically one service method + one controller action + a small UI block + targeted tests.
- Precedent: `3cc31b4` (unlink device).

### 5. Test-only change
- Pinning existing controller/service/UI behavior without changing production code.
- Risk: LOW.

### 6. Too-broad / needs approval
- Changes that touch multiple parent-facing layers unnecessarily.
- Changes that affect permissions or the read-only boundary over child data.
- Changes that introduce or remove endpoints beyond what the task described.
- STOP and request approval.

## Default working style

- **Inspect first, edit later.** Before any edit, read the touched controller action, its service method, the relevant DTO, the UI section that calls it, and the existing tests for that endpoint.
- **Classify the task before editing** using the change-classification guide above. Say the classification explicitly in the report.
- **Identify which sensitivity applies:** user-visible (UX), trust-sensitive (dashboard contract, ownership), or security-sensitive (auth, credentials). Some tasks are multiple.
- **Prefer narrow end-to-end slices.** For a new parent self-service feature: interface signature + service method + controller action + minimal UI block + targeted tests — all in one small commit when they're tightly coupled. For a UX polish: only the UI.
- **Keep layers aligned when they must change together.** If the controller contract shifts, update the UI and the tests in the same commit. Don't leave the dashboard fetching a shape the controller no longer returns.
- **Do not mix parent work with unrelated child/runtime work.** A parent dashboard polish commit does not also touch `ChatService`, mode prompts, or benchmark baselines. If the work mixes, split it.
- **Keep reports structured.** Use the 7-section format below every time.

## Ownership, privacy, no-existence-leak guidance

- **A parent can act only on their own linked resources.** The parent's JWT `NameIdentifier` claim is the source of truth for parentId; never trust a parentId from URL, body, or header.
- **The service enforces ownership, the controller enforces both ownership and response shape.** They are complementary — don't remove either side.
- **Unauthorized access returns no extra information.** The canonical shape: same `NotFound` (or same `Ok` with the same body) for "doesn't exist" as for "not yours". Pinned precedent: `ConversationController.GetById` (055792e).
- **Idempotent responses are appropriate for destructive self-service.** Unlink/remove flows should return the same body whether the target existed or not — the caller learns nothing about the internal state (precedent: `3cc31b4`).
- **Tests must pin ownership-sensitive behavior.** For every parent endpoint that scopes to a deviceId, add:
    - a "device not linked → Forbid / idempotent Ok / same shape as not-yours" test, AND
    - a `DidNotReceiveWithAnyArgs()` assertion that the service method was not called on the unauthorized path, AND
    - an anti-tautology happy-path test so the negative tests cannot silently degrade.
- **Do not use status codes as a channel.** Returning 403 for one case and 404 for another on the same action re-introduces the existence-leak — collapse them to a single shape.

## UI guidance for parent.html

- **Prefer minimal inline changes over refactors.** `parent.html` is a self-contained static page (no framework, no build step, ~986 lines). Respect that shape.
- **Keep controls obvious.** A destructive self-service button should look different enough from a navigation row button that a parent won't click it by accident.
- **Confirm destructive actions** with `window.confirm(...)`. No silent destructive operations.
- **Refresh the affected section after success.** Unlink → re-fetch the device list. Change password → clear the form and show success. Never leave the UI in an inconsistent state.
- **Avoid CSS/JS cleanup unless asked.** Inline styles on a new button are acceptable (precedent: unlink button in `3cc31b4`). Don't rewrite the page's CSS block for the sake of a new feature.
- **Preserve existing page patterns:** `show(view)`, `setStatus(id, text, class)`, `authedFetch(path)`, `getToken()`, `clearToken()`, `shortId(guid)`, `fmtTime(ts)`. Reuse these helpers — don't invent parallel ones.
- **Inline fetch with bearer headers is fine** for non-GET requests; the existing `submitLinkDevice` and `unlinkDevice` functions are the canonical shape — mirror it.
- **Handle `401`** by clearing the token and returning the parent to the login view with a "Session expired" status. Every authenticated UI path does this.
- **Keep accessibility intact.** No button nested inside another button. Give buttons explicit `type="button"` when outside a form.

## Testing guidance

- **Controller test when:** the action's response shape, status code, ownership short-circuit, or claim-based argument flow is load-bearing. Use NSubstitute for the service and `DefaultHttpContext` + `ClaimsPrincipal` per the precedent in `ConversationControllerPaginationTests` / `ChatControllerPath5Tests` / `ChildControllerOwnershipTests` / `ParentControllerRegisterTests` / `ParentControllerUnlinkDeviceTests`.
- **Service test when:** data access logic, ownership predicates, SaveChanges behavior, or cross-entity isolation is load-bearing. Use the `TestDbContext` + `CreateService(jwtKey?)` helper pattern in `ParentServiceAuthTests`.
- **Both when:** the feature has a real end-to-end contract (e.g. unlink — controller shape + service DB predicate both matter). Precedent: `3cc31b4` added three service tests and two controller tests.
- **Keep parent-feature tests narrow.** Don't retest framework behavior (authorization attribute enforcement, JSON serialization). Test the action's code.
- **Validate "no unintended data deletion."** For any delete/unlink test, assert the row it targeted is gone AND at least one unrelated row (different parent, different device, or different child) was untouched.
- **Validate ownership boundaries explicitly.** Assert both the "right answer" (Forbid/NotFound/idempotent Ok) AND the service method was not called (`DidNotReceiveWithAnyArgs`). A correct response from an endpoint that still hit the DB is still wrong.
- **Anti-tautology guards are mandatory** on every negative-assertion cluster. Without them, a future regression that inverts the whole action silently passes.

## Output expectations

For any non-trivial parent-facing task, return in this exact structure:

1. Current State
2. Change Decision
3. Files Changed
4. Diff Summary
5. Validation Results
6. Risks / Tradeoffs
7. Exact Commit Message Suggestion

Under **Change Decision**, state the classification (1–6 above) and one sentence explaining why.

Under **Validation Results**, always include:
- Targeted test filter result
- `dotnet build` result
- Full `dotnet test` result
- `git status --short` showing that `.claude/settings.local.json` stayed unstaged

## Repo-aware examples

### Good use: adding an unlink-device feature
- Classification: (4) parent self-service.
- One interface method + one service method + one controller action + one small UI block + 3 service tests + 2 controller tests. Single commit. Precedent: `3cc31b4`.
- Response is idempotent: `Ok(new { unlinked = true })` whether or not a row existed. No existence leak.

### Good use: adding a minimum password length guard
- Classification: (3) auth/account hardening, LOW-to-MEDIUM risk, approval obtained per CLAUDE.md guidance.
- Controller-side guard after the existing `IsNullOrWhiteSpace` check. No DTO change, no service mirror. Three controller-level tests: too-short, valid, missing.
- Precedent: `0082848`.

### Good use: pinning no-existence-leak behavior
- Classification: (5) test-only change.
- Add controller-level tests that assert "exists but not yours" returns the same shape as "doesn't exist" (status AND type AND body shape). Precedent: `055792e` (ConversationController).

### Good use: a read-only parent dashboard improvement
- Classification: (1) UI-only parent polish.
- Example: show a flagged-message count next to each device in the list.
- Only edit `parent.html`. If the data isn't already on the summary row, classify it as (2) instead and do backend+UI together.

### Good refusal: mixing parent work with child-mode prompt changes
- Task: "Add the change-password feature AND tighten the Calm prompt."
- Correct response: refuse to bundle. Offer to ship change-password first under this skill, and hand the Calm prompt tightening to `mode-workstreams` in a separate commit. Explain that mixing churns two different benchmark/risk domains through one review.

### Good refusal: widening into admin control
- Task: "Let the parent force-delete all conversation history."
- Correct response: stop and request product approval. This crosses the read-only boundary over child data; it's a new feature class, not a polish slice.

## When to stop and ask for approval

Stop and request explicit approval before editing when the task:

- Introduces a new parent-facing endpoint (any HTTP verb/route not already in `ParentController` / `ConversationController` / `ChildController`).
- Changes auth or security plumbing: JWT config, token shape, password storage, login/register flow.
- Changes permissions (who can do what), the read-only boundary, or the ownership model.
- Would delete or mutate child-linked data (conversations, messages, child profiles) — even indirectly via cascade.
- Touches multiple layers (controller + service + UI + tests) for what appears to be a polish task — reclassify or split.
- Would alter the shape of an existing action's response in a way a live UI already depends on — requires an aligned UI update and a clear migration note.

In these cases, produce a plan with exact files/lines and the classification, and wait for "approved — proceed" before touching code.

## Constraints

- Do NOT leak internal exception detail into parent-facing response bodies.
- Do NOT introduce existence-leak channels (distinct status codes for "not yours" vs "doesn't exist").
- Do NOT mix parent-facing work with child-runtime, mode-prompt, or benchmark work in the same commit.
- Do NOT skip the classification step from the change guide.
- Do NOT edit CSS/JS outside the feature's minimal surface in `parent.html`.
- Do NOT add mutation of child-generated data (conversations, messages) as a side effect of a parent self-service change.
- Do NOT remove or soften an ownership test, a no-existence-leak assertion, or an anti-tautology guard without an explicit product reason.

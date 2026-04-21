# CLAUDE.md

## Project

Armenian AI Toy ("Areg") — a physical children's toy (ages 4-7) with an Armenian-speaking AI companion. ESP32 hardware connects to a .NET backend that orchestrates OpenAI GPT-4o for child-safe conversations.

Areg is a **play leader and storyteller**, not an AI friend or chatbot.

## Product Constraints

- **Armenian-first.** All child-facing output is in Armenian.
- **Safety-first.** Dual moderation (input + output). Never bypass safety checks.
- **Parent-trust-first.** No emotional companion behavior. No open-ended chat.
- **Bounded conversation.** Five modes only — Story, Game, Riddle, Curiosity Window, Calm/Bedtime. Never free-form AI chat. Full spec in `.claude/MODES.md`.
- **Tone rules (summary — full rules in `.claude/MODES.md`):**
  - Story mode: warm, slightly unhurried, quiet sense of magic. 3–5 sentences + choice block.
  - Game mode: clear, direct, a notch more energetic. Short sentences, brisk reaction.
  - Riddle mode: playful and slightly knowing, warm hints, no choice block.
  - Curiosity Window: brief, genuinely interested, one real answer, then return to play.
  - Calm/bedtime mode: soft, slow, close. No choices, no questions, no cliffhangers.
  - Humor is okay in moderation.
  - Must NOT sound like: a chatbot, teacher, anxious assistant, baby voice, or emotional companion.
- **Identity stays the same across modes.**
- **Hardware/audio is out of scope** for current work.
- **Armenian folklore integration is postponed** — do NOT add it.

## Build & Test

```bash
# Backend (from backend/ directory)
dotnet build                                    # Build all projects
dotnet test                                     # Run all tests (773 tests)
dotnet run --project src/ArmenianAiToy.Api      # Run API on http://0.0.0.0:5000

# API key (one-time setup)
dotnet user-secrets set "OpenAI:ApiKey" "sk-..." --project src/ArmenianAiToy.Api
```

Database (SQLite) auto-applies EF Core migrations on first run via
`db.Database.Migrate()`. See **Database migrations** below.

## Database migrations

The schema is owned by EF Core migrations (not `EnsureCreated()`).
Migration sources live in
`backend/src/ArmenianAiToy.Infrastructure/Data/Migrations/`, and
`dotnet-ef` is pinned to 9.0.3 via `.config/dotnet-tools.json` at the
repo root.

### First-time setup (fresh clone)

```bash
dotnet tool restore                              # install pinned dotnet-ef
```

### Running the API

No action needed — `Program.cs` calls `Migrate()` at startup.

### Adding a new migration

```bash
# From backend/
dotnet ef migrations add <Name> \
  --project src/ArmenianAiToy.Infrastructure \
  --output-dir Data/Migrations
```

A design-time `AppDbContextFactory` at
`Infrastructure/Data/AppDbContextFactory.cs` lets `dotnet ef` build
contexts without booting `Program.cs`, so generating migrations does
not require `Jwt:Key` or `OpenAI:ApiKey` to be provisioned.

### Updating a dev DB after pulling new migrations

Just run the API — `Migrate()` applies anything unapplied. Alternatively:

```bash
dotnet ef database update --project src/ArmenianAiToy.Infrastructure
```

### Cut-over policy for pre-migrations DBs

This repo **switched from `EnsureCreated()` to `Migrate()`** in the A4
commit. DBs created before that commit have no `__EFMigrationsHistory`
table, so `Migrate()` will try to re-create all tables and fail.

Two resolution paths:

1. **delete-and-recreate** (the policy for this commit, and for any
   local dev DB at `backend/src/ArmenianAiToy.Api/armenian_ai_toy.db*`):
   simply delete the three SQLite files (`.db`, `.db-shm`, `.db-wal`)
   before starting the API. `Migrate()` will create a fresh schema.
   Only safe when the DB contents are disposable (dev laptops).

2. **baseline-adoption** (recommended for future staging / production
   DBs that carry real data): mark the existing schema as-if the
   `Initial` migration was already applied, then let `Migrate()`
   apply any later migrations normally.
   ```bash
   # One-time adoption script (run against the legacy DB):
   sqlite3 armenian_ai_toy.db <<'SQL'
   CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (
     MigrationId TEXT NOT NULL CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY,
     ProductVersion TEXT NOT NULL
   );
   INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion)
     VALUES ('20260420201336_Initial', '9.0.3');
   SQL
   ```
   After this, `Migrate()` sees `Initial` as applied and only runs
   migrations added after it.

### Rule of thumb

- **Never call `EnsureCreated()` on a real DB again.** It bypasses the
  migrations history and corrupts the adoption contract.
- Tests using `UseInMemoryDatabase(...)` are unaffected — the
  in-memory provider does not participate in migrations. They may
  continue to use the existing pattern.

## Architecture

**Backend — Clean Architecture (.NET 10, 4 projects):**

- **Api** — Controllers, DeviceAuthMiddleware, static web UI in `wwwroot/`
- **Application** — Services, DTOs, Helpers. Core logic in `ChatService` (multi-step orchestration flow including: label consumption, moderation, normalization, prompt building, story intent detection, AI call, and tail-block handling)
- **Domain** — Entities and Enums
- **Infrastructure** — EF Core (SQLite), OpenAI SDK adapters

**Key files:**
- `ChatService.cs` — main orchestration (story choices, normalization, prompt injection)
- `ChoiceNormalizer.cs` — heuristic child input → option_a/option_b/unknown
- `TailBlockParser.cs` — extracts/strips `---\nCHOICE_A:...\nCHOICE_B:...` from AI responses
- `ModeDetector.cs` — 5-mode detection (Story/Game/Riddle/Curiosity/Calm) with priority rules
- `ModeDetectorTests.cs`, `ModeDetectorIntegrationTests.cs` — mode detection and ChatService integration tests
- `ChoiceNormalizerTests.cs`, `ChoiceHandoffTests.cs` — story choice pipeline tests

**ESP32 Firmware** — Thin client. Proxies to .NET backend. No AI on device.

## Parent-Facing Read-Only Monitoring Surface

A read-only dashboard for parents to review device activity. Strictly observational —
no editing, no deletion, no child-facing features.

**UI**
- `wwwroot/parent.html` — single self-contained static page (HTML + inline CSS + vanilla JS, no framework, no build step).
- Discoverable via a small link inside the Parent Monitoring panel of `wwwroot/index.html`.
- Views: login → linked devices → conversation summaries / flagged messages tabs → conversation detail.

**Backend endpoints** (all parent-JWT authenticated, ownership-checked against linked devices)
- `POST /api/parents/login` — issues JWT
- `DELETE /api/parents/devices/{deviceId}/link` — unlink a device from the authenticated parent account (idempotent; no existence leak). If this removes the last remaining parent link, the device and its cascade subtree (children, conversations, messages) are deleted.
- `GET  /api/parents/devices` — list linked device ids
- `GET  /api/conversations?deviceId=&limit=&offset=` — full conversation history
- `GET  /api/conversations/summary?deviceId=&limit=&offset=` — lightweight summary rows with snippets
- `GET  /api/conversations/flagged?deviceId=&limit=&offset=` — flat newest-first list of non-Clean messages
- `GET  /api/conversations/{conversationId}` — full conversation detail (404 on not-yours, no existence leak)

**Pagination guard**: list endpoints reject `offset < 0` and `limit < 1` with 400, and clamp `limit > 100` to 100. Lives as a private static helper inside `ConversationController`.

**Manual QA checklist**
1. `dotnet run --project src/ArmenianAiToy.Api` → open `http://localhost:5000/` → click "Open the Parent Dashboard →".
2. Log in → devices list loads (or "No devices linked to this account yet." if none).
3. Click a device → Conversations tab active, summaries load. Click Flagged tab → flagged list loads (or "No safety-flagged messages on this device. ✓").
4. Click a row → detail view opens with messages; Blocked (red) and Flagged (amber) borders distinct. ← Back returns to the originating tab.
5. Pagination: ← Newer disabled on page 1, Older → disabled on last page, "Page N" label visible.
6. Bad inputs: `?offset=-1` → 400; `?limit=0` → 400; `?limit=500` → 200 with at most 100 rows.
7. Log out → returns to login view, token cleared from sessionStorage.

## Audit events

An append-only `AuditEvents` table records sensitive parent actions.
Write-only surface for now — no API, no UI, no query path. Rows are
written inside the same `SaveChangesAsync` as the action they describe.

**Events captured in slice 1** (`AuditEventType`):
- `ParentAccountDeleted` — emitted in `ParentService.DeleteAccountAsync`.
- `ParentChildDeleted` — emitted in `ParentService.DeleteChildAsync`.
- `ParentDeviceUnlinked` — emitted in `ParentService.UnlinkDeviceAsync`
  (both the still-linked path and the orphan-cascade path). Metadata
  carries `orphan_cascaded: bool`.
- `ParentPasswordChanged` — emitted in `ParentService.ChangePasswordAsync`
  on success only; wrong-password failures are not audited in slice 1.

Register / login / device pause-resume / chat / moderation / rate-limit
events are deliberately out of scope for this slice.

**Invariants** (do not regress):
- **No foreign keys** from `AuditEvent` to `Parent` / `Device` / `Child`.
  Audit rows must outlive the entities they describe — a cascade that
  took the audit trail with it would destroy the record of the action
  at the same moment it is meant to document.
- **No PII in `Metadata`.** Only counts, booleans, and identifiers
  already carried in the dedicated `ActorParentId` / `TargetDeviceId` /
  `TargetChildId` columns. Keeps audit durable without becoming a
  second copy of data the parent just asked to have erased.
- Audit rows **survive** parent / device / child deletion cascades
  (C1 / C2 / C3 / unlink cascade).
- The existing `ILogger.LogInformation` lines stay — audit is additive,
  not a replacement.

## Bedtime window (B4)

Parent-configured daily quiet hours on a device. Scheduled analogue of
`Device.IsPaused`. Fires at the HTTP boundary in `ChatController`:
while inside the window, `POST /api/chat` returns the same canned reply
a paused device returns, before any OpenAI call or conversation write.

- **Hard-block semantics.** Not force-Calm — chat is refused during the
  window, identical to the pause path. This keeps B4 off `ChatService`
  and `ModeDetector` entirely.
- **Per-device scope.** Stored on `Device` (`BedtimeStart`, `BedtimeEnd`,
  `TimeZone`). Siblings sharing one device share one window. Per-child
  windows require child identification in the chat flow and are a
  later slice.
- **Disabled state.** Both `BedtimeStart` and `BedtimeEnd` null → window
  is off. Half-null is normalized to disabled server-side — the write
  endpoint is idempotent for "clear the window" and accepts half-null
  without a 400.
- **Pause wins.** The chat gate is
  `IsDevicePausedAsync || IsDeviceInBedtimeWindowAsync`. If a parent has
  explicitly paused, the bedtime window is moot; pause is the stronger
  signal.
- **Timezone handling.** Each device carries an IANA `TimeZone` string,
  default `"Asia/Yerevan"`. Evaluated with
  `TimeZoneInfo.FindSystemTimeZoneById` at gate time. If the id fails to
  resolve on the host, the evaluator logs a warning and falls back to
  UTC — the window is still enforced, never silently disabled.
- **Midnight-crossing windows** (e.g. 22:00–07:00) are explicitly
  supported: start is inclusive, end is exclusive, wrap-around handled
  in the evaluator.
- **Log-only in slice 1.** Setting/clearing the window emits a
  `LogInformation` line but does **not** write an `AuditEvent`. Audit
  scope for parental-control config changes (pause, bedtime) is a
  separate later decision taken together.

Endpoint: `PUT /api/parents/devices/{deviceId}/bedtime-window` with body
`{ "start": "HH:mm:ss" | null, "end": "HH:mm:ss" | null }`. Parent-JWT
authenticated, ownership-checked against linked devices, silent 404 on
miss (same shape as pause/resume).

## Engineering Guardrails

- **No architecture redesign.** Work within existing structure.
- **Minimal changes only.** Small diffs. Preserve existing behavior.
- **No new engines or abstractions.** No state machines. No speculative features.
- **Always explain what changed and why.**
- **Always show full updated file contents** after changes.
- **Prefer tests** for logic changes and edge cases.
- **Do not expand scope** beyond what was asked.
- **Do not add folklore, audio, or hardware work.**
- **System prompt is in English** — GPT-4o follows English instructions more reliably.

## Key Design Decisions

- Devices auth via `X-Device-Id`/`X-Api-Key` headers. Parents use JWT.
- `ChildService.BuildChildContext()` appends name/gender/age to system prompt. Gender matters for Armenian grammar.
- Conversations auto-expire after 30 min inactivity. Last 20 messages as context.
- Story choice labels handed off across requests via in-memory `ConcurrentDictionary` with 30-min expiry.
- `previous_story_choice: option_a|option_b|unclear` injected into prompt only during active story flow.
- Choice normalization happens only after input moderation passes.
- Story memory (character/place/mood) extracted from AI responses and re-injected into system prompt for continuity.
- OpenAI chat calls have a 30-second timeout via CancellationToken.

## Autonomous Workflow

Claude CLI operates on this project using a multi-agent pipeline. The agents and skills are defined in `backend/.claude/agents/` and `.claude/skills/`.

**Before every task:**
1. Classify: workstream (story-core / safety / parent-surface / tests / hardening / tooling), mode (review-only / minimal-code-change / test-only / no-change-needed), risk (low / medium / high).
2. HIGH risk (ChatService, system prompt, domain entities, safety, auth) → produce plan, stop for approval.
3. MEDIUM risk (new endpoint, helper, DTO) → produce plan, pause for approval.
4. LOW risk (test, doc, UI polish) → brief plan, proceed.

**Available agents** (`backend/.claude/agents/`):
- `repo-scout` — read-only reconnaissance (first step of every session)
- `plan-proposer` — generates implementation plans with exact files/lines
- `backend-implementer` — executes approved plans, writes code and tests
- `test-runner` — runs `dotnet test`, diagnoses failures
- `doc-sync` — keeps CLAUDE.md and Swagger docs accurate
- `areg-story-evaluator` — story output quality scoring (7-dimension rubric)
- `armenian-linguistic-reviewer` — Armenian text naturalness review
- `prompt-reviewer` — pre-implementation scope/risk/safety review

**Available skills** (`.claude/skills/`):
- `/change-decision` — classify work mode before touching code
- `/minimal-csharp-change` — enforce smallest-safe-diff discipline
- `/phase-b-guardrails` — scope enforcement, product boundary check
- `/story-flow-review` — correctness check for story choice pipeline
- `/pre-commit-check` — final validation gate before commits
- `/benchmark-run` — run StoryBenchmark and compare to baseline
- `/task-brief` — standardized task intake and classification

**Hard stops (must get human approval):**
ChatService changes, system prompt changes, domain entity changes, new endpoints, safety/moderation changes, new NuGet dependencies, git push, persistent test failures, benchmark regressions.

**Self-validation before completing any task:**
All tests pass, no secrets staged, CLAUDE.md test count matches, new endpoints documented, diff is minimal, story-affecting changes benchmarked.

**Work session pattern:** accept task → classify → plan → approve if needed → implement → test → doc-sync → pre-commit-check → commit → report.

**Operating model docs** (`.claude/`):
- `AUTONOMY.md` — top-level operating model index (agents, skills, hard stops, session flow)
- `MODES.md` — canonical 5-mode product specification
- `ROADMAP.md` — phased mode-system implementation plan
- `COMMIT-CONVENTION.md` — commit message style guide

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
  **Single recorded exception (owner decision 2026-06-12):** the
  owner-designated exact classic title `anban-huri` («Անբան Հուռին»)
  is a real product story draft in `backend/content/story-drafts/`.
  Its story segments are byte-frozen — review, TTS listen test, and
  promotion affect spoken-reflection metadata and approval state only,
  never the story text. It is NOT runtime-served until the TTS listen
  test passes and a human promotes it to approved `Stories/Content/`.
  No other folklore titles may be added without a new owner decision.

## Build & Test

```bash
# Backend (from backend/ directory)
dotnet build                                    # Build all projects
dotnet test                                     # Run all tests (2054 tests)
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

**Connection-string discipline (#071).** `Database:ConnectionString` ships
EMPTY in `appsettings.json`; `appsettings.Development.json` carries the dev
file name (`Data Source=armenian_ai_toy.db`). At startup `Program.cs` calls
`DatabaseConnectionString.Resolve` (in `Api/Security/`) before
`AddInfrastructure`: Development falls back to the dev default, but any
**non-Development** environment **fails fast** when the string is unset or is
the dev default (a tell-tale copy-paste). So prod must set
`Database__ConnectionString` explicitly and can never silently run on the
dev-named SQLite file. The guard throws only outside Development, so the local
bench is unaffected. Pinned by `DatabaseConnectionStringTests`.

**Concurrent-`Migrate()` guard (#025).** `Program.cs` runs the startup
`db.Database.Migrate()` through `StartupMigrationLock.RunGuarded`
(`Api/Security/`) — an exclusive OS file lock (`db-migration.lock`, gitignored)
so concurrent instances don't race the migration. The first booter migrates;
others block, then their own `Migrate()` is a no-op. File lock, not a named
mutex (those aren't cross-process on Unix in .NET); the OS frees it on crash.
If it can't be acquired in 60s (or on an IO/permission error) it warns and
proceeds unguarded — never blocks boot. Single-process boots acquire instantly
(unchanged). This is the in-process form; a one-shot migration job /
init-container is still the recommended deploy pattern once an orchestrator is
in play. Pinned by `StartupMigrationLockTests`.

**SQLite concurrency PRAGMAs (#019).** `SqlitePragmaInterceptor`
(a `DbConnectionInterceptor` wired in `AddInfrastructure` via
`AddInterceptors`) runs `journal_mode=WAL`, `busy_timeout=5000`, and
`synchronous=NORMAL` on every opened connection. Without these the
shipped SQLite defaults fail a concurrent writer immediately with
`SQLITE_BUSY` (→ 500s). This is a **stopgap** for the single-file
deployment; moving off SQLite (a future slice) is the real fix.
`journal_mode` is a persistent file setting (idempotent on re-issue);
`busy_timeout`/`synchronous` are per-connection. In-memory test DBs
ignore WAL harmlessly. The WAL sidecars (`*.db-wal` / `*.db-shm`) are
already `.gitignore`d.

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
- Views: login → linked devices → conversation summaries / flagged messages tabs → conversation detail. A separate **Your activity** view, reached from the "View your activity →" link in the linked-devices header, renders the per-actor audit feed (see § Audit events). The activity view is deliberately *not* nested under a device because the feed is per actor parent, not per device.

**Backend endpoints** (all parent-JWT authenticated, ownership-checked against linked devices)
- `POST /api/parents/login` — issues JWT
- `DELETE /api/parents/devices/{deviceId}/link` — unlink a device from the authenticated parent account (idempotent; no existence leak). If this removes the last remaining parent link, the device and its cascade subtree (children, conversations, messages) are deleted.
- `GET  /api/parents/devices` — list linked device ids
- `GET  /api/conversations?deviceId=&limit=&offset=` — full conversation history
- `GET  /api/conversations/summary?deviceId=&limit=&offset=` — lightweight summary rows with snippets
- `GET  /api/conversations/flagged?deviceId=&limit=&offset=` — flat newest-first list of non-Clean messages
- `GET  /api/conversations/{conversationId}` — full conversation detail (404 on not-yours, no existence leak)
- `DELETE /api/conversations/{conversationId}` — hard-delete a single conversation the parent owns. Messages cascade via the existing schema FK. 404 on not-yours or unknown id (same silent-404 phrasing as `DeleteChild`; no existence leak). Writes exactly one `ParentConversationDeleted` audit row on success; failure paths write nothing.
- `POST /api/parents/password/reset-request` — begin a password-reset flow. Anti-enumeration: returns 202 with identical body `{ resetRequested: true }` for known and unknown emails, with BCrypt timing normalization on both paths. See § Password reset.
- `POST /api/parents/password/reset` — complete a reset with a previously-issued token + new password. 200 with `{ reset: true }` on success; uniform 400 "Reset link is invalid or expired." for any failure (unknown / expired / already consumed / too-short password). Does NOT re-issue a JWT — the parent logs in separately.
- `GET  /api/parents/audit?limit=&offset=` — per-actor audit history; see § Audit events for the response shape.
- `GET  /api/parents/export` — single-JSON full export of the parent's own data; see § Data export.

**Pagination guard**: list endpoints reject `offset < 0` and `limit < 1` with 400, and clamp `limit > 100` to 100. Lives as a private static helper inside `ConversationController`.

## Today summary panel (E1.1)

A small read-only "Today" panel at the top of the per-device
Conversations view summarizes the day's activity at a glance. It
sits below the device-context line and above the paginated
Conversations / Flagged list, and is independent of the existing
tabs.

- **Frontend-only.** No new backend endpoint. The panel reuses
  `GET /api/conversations/summary?deviceId=&limit=100&offset=0` (the
  same endpoint that drives the Conversations tab's paginated list)
  with the existing parent-JWT auth and ownership filter. No new
  privacy surface; a parent can never reach the panel for a device
  they don't own.
- **UTC boundary.** "Today" is computed in UTC: rows whose
  `startedAt >= Date.UTC(year, month, day, 0, 0, 0)` are counted.
  The panel header is labeled `Today (UTC)` so a far-Western-
  timezone parent isn't surprised.
- **Limit cap.** The panel pulls up to **100 newest summary rows**
  via the existing endpoint and filters them client-side. A device
  with > 100 conversations on a single UTC day reports the top 100
  (footnote on the panel: *"Showing up to 100 newest conversations
  for today (UTC)."*).
- **Counts shown:** conversations today, messages today (sum of
  `messageCount` across today's rows — the existing summary returns
  the WHOLE conversation's message count, so a conversation that
  spans midnight UTC contributes its full count to today's total;
  documented limitation), flagged messages today (sum of
  `flaggedMessageCount` with the older `hasFlaggedContent` fallback),
  the newest 3 conversations of the day, and up to 3 flagged
  conversations of the day.
- **Failure mode.** If the Today fetch fails (e.g. transient backend
  error), the panel renders a quiet *"Today summary unavailable."*
  line and the existing Conversations / Flagged tabs continue to
  work normally. The Today fetch and the paginated list fetch are
  independent.

**Deferred to a future E1.2 slice (NOT in E1.1):**
- Modes used today (requires per-message `Mode` field aggregation;
  currently absent from the `/summary` shape).
- Assistant-audio availability count (requires per-message
  `audioAvailable`; absent from `/summary`).
- Accurate per-message daily aggregation (the current sums count
  whole conversations spanning midnight UTC).
- Server-side aggregation endpoint
  (`GET /api/conversations/today-summary?deviceId=`) to remove the
  100-row cap and add the modes / audio counts.
- Multi-device "all my devices today" view.
- Browser-local timezone handling.

### E1.2.ui addendum (panel rewired to server-aggregated endpoint)

As of E1.2.ui, the Today panel consumes
`GET /api/conversations/today-summary?deviceId=...` instead of the
client-side aggregation over `/api/conversations/summary?limit=100`.

- **Counts are EXACT per-message** (no longer over-counts when a
  conversation spans midnight UTC).
- **The 100-row cap is gone.** Server pre-aggregates the full day; the
  panel renders the DTO as-is.
- **New "🔊 N replayable" badge** when `assistantMessagesWithAudio > 0`.
  The badge is **count-only** — actual audio playback stays in the
  conversation detail view via the existing C2.1 ▶ Listen affordance.
  The panel does not surface any path or id, only a daily count.
- **"[N today]" tail** on each newest / flagged conversation link
  shows `messageCountToday` per the E1.2 DTO. For a conversation that
  started yesterday but had a turn today, this tail reflects ONLY the
  today messages, not the lifetime count.
- **Footnote** is now `Today (UTC) — server-aggregated.` (the
  limit-100 caveat from E1.1 is dropped).
- **Privacy unchanged.** The DTO does NOT expose `childId` or
  `audioBlobPath`; the panel cannot. The C2.1 contract that audio
  replay happens in the detail view (with the assistant-only role
  gate) is preserved.

## Today summary endpoint (E1.2)

Server-side daily aggregation that supersedes the E1.1 frontend-only
panel's approximate sums. Lives under the existing
`ConversationController` family and reuses the same parent-JWT +
linked-device ownership gate.

**Endpoint**:
`GET /api/conversations/today-summary?deviceId={Guid}[&asOfUtc={ISO8601}]`

- **Authorization**: parent JWT in `Authorization` header
  (`[Authorize]` on the action). The auth pipeline rejects anonymous
  callers before the action runs.
- **Ownership**: 403 `Forbid` when the queried `deviceId` is not in
  the caller's `GetLinkedDeviceIdsAsync` set — same convention as
  `/summary`, `/flagged`, and `/history`. (Silent 404 is reserved for
  the conversation-by-id family, where existence-leak across families
  matters.)
- **`asOfUtc`**: optional. Bound as `DateTimeOffset?` so any valid
  ISO8601 is accepted (with or without offset). Normalized to UTC via
  `DateTimeOffset.UtcDateTime` before reaching the service. The
  ASP.NET Core model binder returns 400 for unparseable values.
- **Day boundary**: UTC. `DayStartUtc = asOfUtc.Date` with
  `DateTimeKind.Utc`. Matches E1.1 panel semantics.
- **Counts are EXACT per-message** (vs E1.1's whole-conversation
  over-counting):
  - `MessagesCount` — count of `Message` rows where
    `Timestamp >= DayStartUtc` on this device.
  - `FlaggedMessagesCount` — same scope, with
    `SafetyFlag != Clean`.
  - `AssistantMessagesWithAudio` — same scope, with
    `Role == Assistant AND AudioBlobPath != null`. **Role gate is
    structural** — child WAV uploads with `AudioBlobPath` set CANNOT
    contribute to the count.
- **Conversation links**:
  - `Newest`: top 3 conversations on this device with any today
    activity, ordered by `StartedAt` desc.
  - `Flagged`: top 3 conversations with at least one flagged
    today-message, ordered by latest flagged-today timestamp desc.
    Today-clean conversations are excluded.
- Each `TodaySummaryConversationLink` carries
  `MessageCountToday` / `FlaggedMessageCountToday` (today-only sums)
  alongside the conversation's `StartedAt` and a snippet trimmed by
  the same `MakeSnippet()` helper used by `/summary`.

**Wire-shape invariants** (do not regress):

- `TodaySummaryDto` and `TodaySummaryConversationLink` deliberately
  do NOT carry `ChildId` — per-child filtering is a separate concern
  that would need an explicit per-child authorization step. Pinned by
  `TodaySummaryDto_DoesNotExposeChildIdOrAudioBlobPath`.
- Neither DTO carries `AudioBlobPath`. Audio paths are server-internal;
  only the COUNT is exposed. Pinned by the same test.
- `AssistantMessagesWithAudio` is role-gated. Pinned by
  `GetTodaySummary_AssistantAudio_OnlyAssistantWithAudioBlobPath`.

**`parent.html` consumption — LIVE as of E1.2.ui.** The Today panel
calls this endpoint and renders the DTO directly. The legacy E1.1
client-side aggregation over `/api/conversations/summary?limit=100`
is gone; the limit-100 cap and whole-conversation over-counting are
no longer in the panel. The `assistantMessagesWithAudio` field is
displayed as a small `🔊 N replayable` badge (count only — playback
stays in the detail view per C2.1). See the "Today summary panel
(E1.1)" → "E1.2.ui addendum" subsection above for the full UI
contract and manual QA additions.

**Modes-used-today — DEFERRED to E1.3.** `DetectedMode` is not
persisted in the schema today (it lives only in
`ChatService.ActiveModes`, an in-memory `ConcurrentDictionary` that
clears on backend restart). Re-running `ModeDetector` against
historical messages would diverge from runtime resolution because the
pure-function detector has no access to the runtime active-story
session or history-priority state. A future E1.3 slice would add a
persistent `Message.Mode` (or equivalent) column and migration
before exposing this aggregate.

### E2.1 addendum — timezone-aware day boundary

By default the Today endpoint now computes its day boundary in the
**device's local time zone** (`Device.TimeZone`, IANA, default
`Asia/Yerevan`) instead of UTC. The wire shape gains three additive
fields and stays backwards-compatible.

- **Resolution chain** (`ConversationService.ResolveTodayTimezoneAsync`):
  explicit `?tz=<IANA>` query param → `Device.TimeZone` → `"UTC"`.
  Only the first non-empty entry is consulted — the `tz` query
  param is the override seam, not the default.
- **Fail-soft to UTC.** Unresolvable ids (`TimeZoneNotFoundException`,
  `InvalidTimeZoneException`) collapse to `TimeZoneInfo.Utc`,
  log one warning, and stamp `TimeZoneResolved=false` on the DTO.
  `TimeZoneId` echoes the *attempted* id so the dashboard can
  honestly label "UTC fallback". Same contract as
  `BedtimeWindowEvaluator`. Hard 400 was rejected — it would break
  parents on hosts whose IANA db lacks the requested zone.
- **DTO additive fields**:
  - `DayStartLocal: DateTime` (Kind=Unspecified) — midnight local in
    the resolved zone.
  - `TimeZoneId: string` — echoed id (or literal `"UTC"` when no id
    was attempted).
  - `TimeZoneResolved: bool` — false when fell back to UTC.
- **`DayStartUtc`** stays the EF filter anchor and is now derived
  via `TimeZoneInfo.ConvertTimeToUtc(DayStartLocal, resolvedTz)`.
  EF queries are unchanged: still `Timestamp >= DayStartUtc`.
- **Privacy invariants unchanged.** Still no `ChildId`, still no
  `AudioBlobPath` on `TodaySummaryDto` or
  `TodaySummaryConversationLink`. Pinned by the existing wire-shape
  test, extended to also require the three new fields are present.
- **Frontend.** `parent.html` does NOT pass `?tz=` — the backend
  infers from the device row. The Today-panel footnote now reads:
  - `Today (Asia/Yerevan) — server-aggregated.` (resolved=true)
  - `Today (UTC fallback) — server-aggregated.` (resolved=false)
  - `Today — server-aggregated.` (defensive fallback when the field
    is missing, e.g. an older backend).
- **Out of scope (deferred).** No `TimeZone` field on
  `LinkedDeviceDto`; no parent-profile timezone setting; no UI
  picker; no DST-edge fixtures across `America/*` transitions; no
  schema change (`Device.TimeZone` already exists). Other
  parent-facing list views (Conversations, Flagged, audit feed)
  remain UTC-anchored and format timestamps client-side. Modes-
  used-today still deferred per the note above.

## Audit events

An append-only `AuditEvents` table records sensitive parent actions.
Rows are written inside the same `SaveChangesAsync` as the action they
describe.

**Events captured** (`AuditEventType`):
- `ParentAccountDeleted` — emitted in `ParentService.DeleteAccountAsync`.
- `ParentChildDeleted` — emitted in `ParentService.DeleteChildAsync`.
- `ParentDeviceUnlinked` — emitted in `ParentService.UnlinkDeviceAsync`
  (both the still-linked path and the orphan-cascade path). Metadata
  carries `orphan_cascaded: bool`.
- `ParentPasswordChanged` — emitted in `ParentService.ChangePasswordAsync`
  on success only; wrong-password failures are not audited.
- `ParentDevicePauseStateChanged` — emitted in
  `ParentService.SetDevicePauseStateAsync` when the pause flag actually
  flips. No-op idempotent calls (already in the requested state) do not
  produce a row. Metadata carries `is_paused: bool`.
- `ParentDeviceRevocationChanged` (#074) — emitted in
  `ParentService.SetDeviceRevocationAsync` when the server-side credential
  kill-switch (`Device.IsRevoked`) actually flips. No-op idempotent calls
  do not produce a row. Metadata carries `is_revoked: bool`.
- `ParentBedtimeWindowSet` — emitted in
  `ParentService.SetBedtimeWindowAsync` on every successful write.
  Metadata carries the post-normalization `start`/`end` (both null when
  the window is disabled or the caller passed half-null).
- `ParentDeviceModeFlagsSet` — emitted in
  `ParentService.SetDeviceModeFlagsAsync` on every successful write.
  Metadata carries the post-save four-bool state
  (`story`/`game`/`riddle`/`curiosity`).
- `ParentDataExported` — emitted in
  `ParentService.BuildExportAsync` on every successful
  `GET /api/parents/export`. Metadata is counts-only
  (`devices`/`children`/`conversations`/`messages`/`audit_events`) —
  no PII, no content, no identifiers beyond `ActorParentId`. Target
  ids are null because the event describes a whole-account export,
  not a single target.
- `ParentConversationDeleted` — emitted in
  `ParentService.DeleteConversationAsync` on every successful
  `DELETE /api/conversations/{conversationId}`. `ActorParentId` is
  the authenticated parent; `TargetDeviceId` is the owning device so
  the dashboard's device-name resolution reuses the existing path.
  There is no dedicated `TargetConversationId` column — the
  conversation id lives in metadata
  (`conversation_id`/`message_count_deleted`/`deleted_at_utc`). No
  PII, no message content. Failure paths (not found / not owned)
  write no row. Complements — does not replace — the scheduled
  `ConversationsPurgedByRetention` event.
- `ParentPasswordResetRequested` — emitted in
  `ParentService.RequestPasswordResetAsync` on the known-email path
  only. `ActorParentId` is the parent whose account was targeted;
  target ids are null; metadata is deliberately empty (no token, no
  token hash, no email). The unknown-email path writes no audit row
  — enumeration-resistance contract would fail if it did.
- `ParentPasswordResetCompleted` — emitted in
  `ParentService.CompletePasswordResetAsync` on successful token
  redemption. `ActorParentId` is the parent whose password just
  changed; metadata empty. Failure paths (unknown / expired /
  already-consumed token) write no row — the 400 response is
  uniform and the audit trail mirrors that uniformity.

Register / login / chat / moderation / rate-limit events remain
deliberately out of scope.

**Parent-facing read endpoint**:
`GET /api/parents/audit?limit=&offset=` — parent-JWT authenticated;
returns only rows where `ActorParentId == parentId`, newest first.
Same pagination contract as the conversation endpoints
(`offset < 0` and `limit < 1` → 400; `limit > 100` clamped to 100;
defaults `limit=20`, `offset=0`). Response wrapper:
`{ "events": [ { id, timestamp, eventType, targetDeviceId, targetChildId, metadata } ] }`.
`metadata` is returned as a JSON object (parsed from the stored blob),
not as an escaped JSON string. `ActorParentId` is not exposed on the
wire — every row a parent reads is their own by the query filter.

**Feed is per *actor parent*, not per device.** A device shared with
another parent does not leak the other parent's actions into this feed.
"What did I do?" — yes. "What happened to this shared device?" — no;
that would be a separate slice.

**Dashboard surface.** `parent.html` renders the feed in a dedicated
**Your activity** view, reached from the "View your activity →" link
on the linked-devices header. Rows show a friendly label (mapped
client-side with a raw-enum-name fallback for unknown types), the
timestamp, the resolved device/child name (short id fallback when the
target has since been deleted), and a one-line metadata summary. No
filters, no raw-JSON expander, no per-event detail page in this slice.
The view is placed at the devices-list level deliberately, because the
feed is per actor parent, not per device.

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

## Data export

`GET /api/parents/export` — parent-JWT authenticated. Returns a
single JSON document containing the parent's own scope:

- **parent**: safe profile fields only — `Id`, `Email`,
  `RegisteredAt`, `TermsAcceptedAt`, `TermsVersion`,
  `LastLoginAt`, `EmailVerifiedAt`, and `GoogleSubject` (null for
  password-only parents; the stamped Google `sub` claim for
  Google-linked parents — user-owned data, not credential material,
  so it is included in the export body). **Never** `PasswordHash`.
- **devices**: linked devices with safe fields only — device
  identity (`Id`, `MacAddress`, `Name`), `RegisteredAt` /
  `LastSeenAt`, pause state, bedtime window (start/end/timezone),
  and the four B5 mode flags. **Never** `ApiKey`. Each device
  nests:
  - **children** — id, device id, name, gender, date of birth /
    age, and the four per-child mode overrides.
  - **conversations** — reuses the existing
    `ConversationDto` / `MessageDto` shape (same as
    `GET /api/conversations/{id}`), newest-first.
- **auditEvents**: unpaginated per-actor audit feed, same shape as
  `GET /api/parents/audit` (the authenticated parent's own rows
  only).
- **schemaVersion** = `"1"` and **generatedAt** at the top so
  downstream readers can evolve with the shape. **excludedFields**
  documents intentional omissions inline
  (`Parent.PasswordHash`, `Device.ApiKey`).
- **audioDisclosure** (#035): binary voice recordings are referenced by
  message, not embedded in the JSON (impractical to inline). Rather than
  silently dropping them (a GDPR Art.15/20 honesty gap), the export carries
  a top-level `audioDisclosure { note, assistantAudioEndpoint,
  childAudioStatus }` that explains the omission + its reason, points at the
  per-message `audioAvailable` flag and the C2.1 streamer
  `GET /api/parents/messages/{messageId}/audio`, and honestly states that
  child-uploaded audio is retained but not yet individually downloadable
  (the child-audio replay slice is deferred — see § Voice chat C2.2). It is
  additive; `excludedFields` still carries only the credential omissions.
- **dataRetention** (#067): additive top-level
  `{ enabled, messageRetentionDays, description }` so a parent sees the
  storage-limitation policy ("deleted after N days", or that automatic
  deletion is off) directly in their export. Sourced from
  `RetentionPolicy` (same config + semantics as `RetentionPurgeService`).
  Publishing the period in the privacy policy/terms is the remaining
  owner/legal task (see the owner checklist).

Response headers: `Content-Type: application/json` and
`Content-Disposition: attachment; filename="areg-export-<utcts>.json"`.
The filename is timestamp-only — no email, no PII.

**Scope invariants** (do not regress):
- Every nested collection is filtered by the authenticated parent's
  id at query time via the `ParentDevice` join. A device shared
  with another parent exposes only *this* parent's audit rows in
  the export.
- No credential material ever appears in the body; the DTO shapes
  in `ParentExport.cs` omit `PasswordHash` / `ApiKey` by
  construction, and a test asserts the unique seeded markers never
  appear in the serialized response.
- No system telemetry (metrics, logs, histograms) and no moderation
  / prompt / model internals beyond what parent-facing read
  endpoints already surface.

**Audited.** Each successful export writes a `ParentDataExported`
audit row in the same transaction. Metadata is counts-only:
`devices`, `children`, `conversations`, `messages`, `audit_events`
— no PII. Target ids are null because the event describes a
whole-account export.

**Guarded.** `ExportCooldown` (singleton, process-local) enforces a
per-parent cooldown — default **60 seconds**. Repeated calls inside
the window get `429 Too Many Requests` with a whole-seconds
`Retry-After` header. The existing `ChatRateLimiter` is deliberately
not reused: it keys off the `X-Device-Id` header, not the JWT
parent claim.

**Out of scope for this slice** (deliberate): no filtering query
params, no async prepared exports, no zip format, no email
delivery, no signed URLs, no dashboard button. The endpoint is
callable today with any parent JWT.

## Retention

First scheduled-delete layer in the repo. Lives in
`backend/src/ArmenianAiToy.Infrastructure/Background/RetentionPurgeService.cs`
— a plain `BackgroundService` registered via
`AddHostedService<RetentionPurgeService>()` in
`Infrastructure/DependencyInjection.cs`. No Hangfire, Quartz, or
Polly; no new NuGet packages.

- **Messages + conversations.** Shipped default
  `Retention:Messages:MaxAgeDays = 90`. A conversation is eligible iff
  `max(StartedAt, EndedAt ?? min, most-recent Message.Timestamp) <
  cutoff`; a conversation with no messages anchors on `StartedAt`. The
  purge is **hard-delete**: `Conversation` rows are removed by the EF
  change tracker and `Message` rows cascade at the DB level via the
  existing schema FK. On every tick that actually deleted something,
  one `ConversationsPurgedByRetention` audit row is written in the
  same `SaveChangesAsync`. **Noop ticks write no audit row.**
  Additional knobs: `Retention:Messages:RunIntervalMinutes` (default
  `60`, floor-clamped to `15`), `Retention:Messages:MaxBatchSize`
  (default `500`, clamped to `[1, 10000]`). Query shape is a cheap
  projection (`Select` over `Conversations` with `Max(Timestamp)` and
  `Count`) — `Message.Content` is never materialized on this process.

- **System-actor audit event.**
  `ConversationsPurgedByRetention` is the **first** audit event with
  `ActorParentId = null`. The null actor is what keeps it out of
  every parent-facing read surface — `GET /api/parents/audit` and
  the `auditEvents` slice of `GET /api/parents/export` both filter
  `ActorParentId == parentId`, so a null-actor row is invisible to
  every parent by construction. Do not change the factory to
  populate `ActorParentId`; the invisibility is a contract, not an
  accident. Metadata is counts-only
  (`conversations_deleted` / `messages_deleted` / `cutoff_utc` /
  `batch_size_limit`) — same PII-free discipline as `ParentDataExported`.

- **Password-reset token cleanup.** Second cleanup pass inside the
  same worker tick. Deletes stale `ParentPasswordResetToken` rows
  with a single `ExecuteDeleteAsync` — no entity materialization,
  no audit row, no metric. Rule:
    `ConsumedAt != null  OR  ExpiresAt < UtcNow - grace`
  Grace window configurable via
  `Retention:PasswordResetTokens:GracePeriodHours` (default `24`;
  clamped to `>= 0`). Usable reset tokens (unconsumed + unexpired
  OR expired within the grace window) are never deleted. Runs on
  every tick that the worker is enabled, including ticks where the
  conversation pass finds nothing eligible. Shares the single
  disable gate — when `Retention:Messages:MaxAgeDays <= 0`, the
  whole worker short-circuits and no cleanup runs.

- **Disabled mode.** Reached ONLY via an explicit non-positive
  override (`Retention:Messages:MaxAgeDays <= 0`). Missing config
  resolves to `90` — never to `0`. Do not ship a
  `Retention:Messages:MaxAgeDays = 0` setting in
  `appsettings.Development.json` or any other overlay. When
  disabled, the worker logs once per tick and issues no DB query —
  this covers BOTH the conversation pass and the token-cleanup pass.
  **#067 startup alert**: additionally, `Program.cs` logs a loud
  `LogWarning` at startup when retention is disabled in a
  **non-Development** environment — a silent disable on a children's
  product (conversations kept forever) must never pass unnoticed in
  prod. The read-only projection used by this alert (and by the export
  disclosure below) is `RetentionPolicy.ResolveMessages` in
  `Application/Helpers`, which mirrors this same default-90 / `<=0`-
  disabled contract; keep `RetentionPolicy.DefaultMessagesMaxAgeDays`
  in sync with `RetentionPurgeService.DefaultMaxAgeDays`.

- **Device destructive pass (`Dormancy:Devices:DeleteAfterDays`).**
  Runs LAST in the tick, immediately after the device-warn pass.
  Deletes a dormant `Device` (per-device, whole device — NOT
  per-parent link) when all three hold:
  `Device.LastSeenAt < UtcNow - WarnAfterDays`,
  `Device.DormancyWarnedAt != null`, AND
  `Device.DormancyWarnedAt < UtcNow - DeleteAfterDays`. FK cascade
  removes children, conversations, messages, and `ParentDevice`
  join rows in the same transaction; one system-actor
  `DeviceDormancyDeleted` audit row per deleted device carries
  counts-only metadata (`warn_after_days`, `delete_after_days`,
  `last_seen_at_utc`, `linked_parents_at_delete`,
  `children_deleted`, `conversations_deleted`, `messages_deleted`
  — no device name, no parent ids, no emails). Shipped default
  `DeleteAfterDays = 0` (disabled). Positive values clamp to >= 1
  so a same-tick warn + delete race is structurally impossible.
  **Operator-facing invariant**: set
  `Dormancy:Devices:WarnRefireIntervalDays > Dormancy:Devices:DeleteAfterDays`
  — the warn pass's refire logic updates `DormancyWarnedAt` on
  every tick past the refire window, resetting the destructive
  clock. Recommended pairing: refire=90, delete=60. The DI-time
  precondition (Guard 4) additionally requires SMTP transport
  and `WarnAfterDays > 0` when `DeleteAfterDays > 0`. The
  existing device-warn email carries the effective delete date
  in its body via the notifier's pre-reserved `deleteAtUtc`
  parameter — no new notifier method, no separate "final notice"
  pass.

- **Audit stays forever — unchanged.** This slice does not add any
  trim/archival of the `AuditEvents` table. The "keep forever, no
  FK" invariant from § Audit events is preserved. Retention is
  about messages and conversations, not about the durable record of
  what happened to them.

- **Structured logs — stdout is the retention boundary.** The
  JSON-formatted logs (see § Structured console logging) go to
  stdout only. This repo ships no file sink and no rotation policy
  in code; log retention is the host's problem. Adding a file sink
  would create a second PII-adjacent surface (structured template
  holes carry `ParentId`, `DeviceId`, etc.) that today has no
  retention owner.

- **Export is never server-persisted.** `GET /api/parents/export`
  streams JSON; no artifact is written to disk. The 60-second
  `ExportCooldown` is process-local memory only. Both properties
  are unchanged by this slice.

**Forward-looking note on audio.** `Message.AudioBlobPath` stores
paths to audio stored *somewhere external*. No code in this repo
writes or cleans up those blobs today — the field is a dangling
reference. When the audio workstream lands, it owns the
conversation-delete → blob-delete hook; the retention purge here
will need to be extended at that point so deletions do not leave
orphaned audio.

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
- **Audited.** Setting or clearing the window writes a
  `ParentBedtimeWindowSet` audit row alongside the existing
  `LogInformation` line. Metadata carries the post-normalization
  start/end (both null when disabled).

Endpoint: `PUT /api/parents/devices/{deviceId}/bedtime-window` with body
`{ "start": "HH:mm:ss" | null, "end": "HH:mm:ss" | null }`. Parent-JWT
authenticated, ownership-checked against linked devices, silent 404 on
miss (same shape as pause/resume).

## Mode enable/disable (B5)

Parents can toggle availability of the four configurable modes — **Story,
Game, Riddle, Curiosity** — per device. When a mode is disabled and the
child's current message triggers that mode in `ModeDetector`, the chat
pipeline short-circuits at the HTTP boundary with a warm canned reply
(no OpenAI call, no conversation write), same envelope shape as the
pause path.

- **Per-device scope.** Stored on `Device` as four `bool` columns
  (`StoryEnabled`, `GameEnabled`, `RiddleEnabled`, `CuriosityEnabled`),
  all default `true`. The additive migration backfills existing rows
  with `true`, so no device changes behavior until a parent opts out.
- **Calm is always enabled.** There is no `CalmEnabled` column and no
  UI toggle by design — bedtime cues must always reach Calm handling
  regardless of parent config. Safety invariant from MODES.md.
- **Gate order is `pause > bedtime > mode`.** Pause blocks every
  request; bedtime blocks every request inside its window; mode only
  fires when the first two are inactive and `ModeDetector` makes a
  **definitive** Story/Game/Riddle/Curiosity call. Calm / None /
  ambiguous detections pass through — conservative "miss a
  classification, let it through."
- **No fallback routing.** A disabled mode does not route to another
  mode (e.g. disabled Story does not fall back to Game or to Calm). The
  response is a single short Armenian canned reply
  (`ChatController.ModeDisabledResponse`: "Եկ մի ուրիշ բան փորձենք։").
- **Audited.** Each successful `PUT` writes a
  `ParentDeviceModeFlagsSet` audit row with metadata carrying the
  four-bool post-save state. No migration was needed for the new
  `AuditEventType` value — the column is already string-converted.

Endpoint: `PUT /api/parents/devices/{deviceId}/mode-flags` with body
`{ "story": bool, "game": bool, "riddle": bool, "curiosity": bool }`.
Full-replacement shape — all four always supplied. Parent-JWT
authenticated, ownership-checked, silent 404 on miss (same shape as
pause/bedtime).

## Per-child mode overrides

Per-child overrides on top of the B5 device defaults. Lives on `Child`
as four nullable `bool?` columns (`StoryEnabled`, `GameEnabled`,
`RiddleEnabled`, `CuriosityEnabled`), each three-valued:

- `null` → **inherit** the device's B5 flag for this mode.
- `true` → force this mode **on** for this child, even if the device
  has it off.
- `false` → force this mode **off** for this child, even if the device
  has it on.

**Child override wins over device flag in both directions** when
non-null. Null means inherit, so a child with all four columns null
behaves exactly like B5's device-level defaults would.

- **Calm has no override column and no UI toggle**, same safety
  invariant B5 preserved: bedtime cues always reach Calm handling
  (MODES.md), regardless of device or child config.
- **Missing `ChildId` on a chat request** (either the firmware didn't
  supply one or it was null) falls back to the existing B5 device-level
  resolver. No child layer → device flags alone decide.
- **Cross-device probe guard**: the override lookup joins on both
  `Child.Id == childId` **and** `Child.DeviceId == deviceId`. A
  `ChildId` that belongs to a different device than the one making the
  request can never influence that device's gate.
- **Chat gate chain** stays `pause → bedtime → mode`. Only the mode
  step changed: it now calls
  `IDeviceService.IsModeEnabledForRequestAsync(deviceId, childId?, mode)`
  instead of the previous device-only resolver. The B5
  `IsDeviceModeEnabledAsync` is preserved for the
  null-ChildId fallback and for backward-compatibility tests.
- When the effective flag is `false` the existing B5 canned reply
  (`ModeDisabledResponse`) is used; no new response string, no fallback
  routing.

Audited: each successful `PUT` writes a `ChildModeOverridesSet` audit
row with the four post-save nullable states in metadata. No migration
needed for the new enum value — `EventType` stays string-converted.
Verified via `dotnet ef migrations has-pending-model-changes`.

Endpoint: `PUT /api/parents/children/{childId}/mode-flags` with body
`{ "story": bool|null, "game": bool|null, "riddle": bool|null, "curiosity": bool|null }`.
Parent-JWT authenticated; ownership is "parent must own the device the
child belongs to" (same shape as `DeleteChildAsync`). 404 on miss.
Dashboard exposes per-child tri-state selects (Inherit / On / Off)
with an "Inherit (on)" / "Inherit (off)" hint in the Inherit label so
parents never wonder what "Inherit" resolves to.

## Password reset

Parents who forget their password can recover their account via a
single-use, time-limited token flow. Two endpoints:

- `POST /api/parents/password/reset-request` body `{ email }`
  — begins the flow. **Always** returns 202 with body
  `{ resetRequested: true }` regardless of whether the email is
  known. Both paths pay the same BCrypt latency (same
  `_hashPassword` seam the register slice uses), so response timing
  cannot be used as an account-existence oracle. For a known email:
  generates a 32-byte CSPRNG token, stores only its SHA-256 hash in
  `ParentPasswordResetTokens`, calls `INotifier.SendPasswordResetAsync`
  with the raw token, and writes one `ParentPasswordResetRequested`
  audit row. For an unknown email: no token row, no notifier call,
  no audit row. Rate-limited via `[EnableRateLimiting("auth")]`.
- `POST /api/parents/password/reset` body `{ token, newPassword }`
  — completes the flow. Returns 200 with `{ reset: true }` on
  success; uniform 400 with `{ error: "Reset link is invalid or
  expired." }` on any failure (unknown / expired / already consumed
  / new password too short). Single-use is enforced by
  `ConsumedAt` on the token row. **No JWT is re-issued** — the
  parent logs in separately. Rate-limited via the auth policy.

**Token persistence.** `ParentPasswordResetToken` is a new entity
with FK cascade to `Parent`, so deleting a parent takes any
pending tokens with them. Only the **hash** of the token is
stored — the raw token travels exactly once, from the request
endpoint through `INotifier` to the parent's email. A DB exfil
therefore does not yield usable tokens. Default TTL is 60 minutes,
configurable via `Auth:PasswordResetTokenTtlMinutes`.

**Invariants (do not regress)**:
- Reset-request response is byte-identical across known and unknown
  emails (status + body). Pinned by
  `RequestPasswordReset_UnknownEmail_Returns202WithIdenticalBody`.
- BCrypt runs on both reset-request paths. Pinned by
  `RequestPasswordReset_HashesOnBothPaths_TimingNormalization` via
  the counting-spy seam. Skipping the unknown-email path would
  re-open the oracle through response latency.
- Reset-completion returns the **same** 400 body for every failure
  reason (unknown / expired / consumed / short password). Pinned
  by `CompletePasswordReset_ServiceReturnsFalse_ReturnsUniform400`
  and `CompletePasswordReset_ShortPassword_Returns400_WithoutCallingService`.
- The token is stored only as a hash — the raw token string never
  appears in the DB row. Pinned by
  `RequestPasswordReset_StoredRow_DoesNotContainRawToken`.
- Single-use: a successfully-completed token cannot be reused.
  Pinned by `CompletePasswordReset_TokenReused_ReturnsFalseAndWritesNoSecondAudit`.
- Account deletion cascades pending tokens. Pinned by
  `AccountDelete_CascadesPendingResetTokens`.
- `LoggingNotifier` never logs the raw token — not even a prefix.
  Pinned by `LoggingNotifierTests.SendPasswordResetAsync_DoesNotLogRawToken`.

**Notifier seam.** Minimal `INotifier` abstraction in
`Application/Notifications/INotifier.cs` with a single typed method
`SendPasswordResetAsync(email, resetToken, ct)`. Default
`LoggingNotifier` implementation in
`Infrastructure/Notifications/LoggingNotifier.cs` writes one
structured log line per call and does not actually deliver
anything. A future deploy slice can register a second
implementation (SMTP / webhook / provider SDK) without changing
any caller. **Typed methods, not a generic envelope** — future
consumers (dormant-purge warnings, register-collision mail, etc.)
extend the interface with their own method when they land.

**Dashboard UI.** `parent.html` closes the browser-visible loop for
this flow. The login view carries a subtle **Forgot password?**
affordance that routes to a small reset-request form (email-only,
POSTs to `/api/parents/password/reset-request`). The request UI
surfaces a single neutral message on any 202 response — *"If an
account exists for that email, a reset link has been sent. Check
your inbox."* — identical for known and unknown emails, mirroring
the backend's anti-enumeration contract. Clicking the emailed link
opens the dashboard with `?token=...`; the boot router captures the
token into a closure-scoped variable, strips it from the URL via
`history.replaceState` (so it is not left in browser history /
bookmarks / shared URLs), and shows a reset-password form that
POSTs to `/api/parents/password/reset`. Success routes back to the
login view with a success message; the backend does NOT re-issue
a JWT on reset, and the UI does NOT auto-log-in — the parent must
log in separately. Failure surfaces the backend's uniform 400
error message (`"Reset link is invalid or expired."`) verbatim,
preserving the "all failure reasons look identical on the wire"
contract at the UI layer too.

## Email verification

Tracking-only (T1) email-verification flow. `Parent.EmailVerifiedAt`
is stamped on successful completion of the verify-request → verify
round-trip. The only behavior gated on verification is the dormant-
parent warn pass — login, forgot-password, password change, account
delete, export, and every other parent endpoint are unaffected.
See § Retention for the warn-pass gate's full eligibility rules.

**Endpoints**:
- `POST /api/parents/verify-request` — anti-enum 202 on
  known-unverified / known-verified / unknown-email; only the
  known-unverified branch issues a token + calls the notifier.
  Rate-limited via the auth policy. BCrypt-on-every-path timing
  normalization preserved.
- `POST /api/parents/verify` — 200 `{ verified: true }` on success;
  uniform 400 `{ error: "Verification link is invalid or expired." }`
  on any failure (unknown / expired / already consumed / empty).
  Stamps `Parent.EmailVerifiedAt` and writes one
  `ParentEmailVerified` audit row in the same `SaveChangesAsync`.
  No JWT re-issue.
- `GET /api/parents/me` — minimal authenticated profile lookup
  returning `{ email, emailVerifiedAt }`. Used by the dashboard's
  verification-visibility surface so the "Send verification email"
  button can pass the parent's email to verify-request without a
  form input. Returns 404 on a parent whose row no longer exists or
  has been anonymized.

**Tokens**: `ParentEmailVerificationToken` entity, parallel shape to
`ParentPasswordResetToken`. SHA-256-hex hash, 32-byte CSPRNG raw
token, FK cascade to Parent, ConsumedAt single-use tombstone.
TTL via `Auth:EmailVerificationTokenTtlHours` (default 168 = 7
days). Cleanup via the existing `RetentionPurgeService`'s
`PurgeStaleEmailVerificationTokensAsync` pass —
`Retention:EmailVerificationTokens:GracePeriodHours` (default 24).

**Anti-enumeration invariants** (do not regress):
- Register: new-email path issues + sends; collision path is silent
  no-op (no token, no notifier call). HTTP response identical
  (`{ registered: true }`). BCrypt-on-both-paths timing.
- Verify-request: identical 202 across known-unverified /
  known-verified / unknown-email. BCrypt-on-every-path. Only
  known-unverified issues a token and calls the notifier.
- Verify-complete: uniform 400 across every failure reason.
- Dashboard: post-click confirmation message is the same neutral
  text regardless of backend response.

**Dashboard surface.** The login view carries a "Didn't get a
verification email?" link reaching a self-serve verify-request
form. Clicking the emailed link with `?verifyToken=...` opens a
small confirm view; success routes back to login with an info
message. The token is captured into a closure-scoped variable and
immediately stripped from the URL via `history.replaceState`. The
linked-devices summary block additionally surfaces an
unobtrusive `Email not verified yet.` line with a `Send
verification email` action when the authenticated parent is
unverified — verified parents see no extra UI.

## Register anti-enumeration

`POST /api/parents/register` is designed so that the new-email and
already-registered-email paths are externally indistinguishable — the
endpoint does not serve as an account-existence oracle.

- Both paths return **201 Created** with the identical neutral body
  `{ "registered": true }`. No `parentId` is echoed; a per-request
  identifier would be a first-class enumeration signal.
- The 409 "Email already registered" response is deliberately gone.
- `ParentService.RegisterAsync` silently no-ops on email collision —
  no throw, no second row, and **no mutation of the existing row's
  `PasswordHash` or any other field** (overwriting would be a silent
  takeover).
- **Mandatory timing normalization.** BCrypt hashing runs on BOTH
  paths — the hash is computed before the email-existence check and
  discarded on the collision path. Skipping it on the collision path
  would re-introduce the oracle via response latency (~10× gap).
  Pinned by `Register_HashesPasswordOnBothPaths` via a counting spy
  injected through `ParentService`'s optional 4th constructor
  parameter; that seam exists only for this invariant.
- Request-shape validations (empty fields, password < 8 chars,
  `AcceptedTerms=false`) still return 400 — these inspect only the
  submitted payload and do not depend on the registered set, so they
  do not leak.
- Auth rate limiter (`[EnableRateLimiting("auth")]`, 10 / 60 s per
  caller IP) remains attached to the endpoint.
- **UX debt:** a user who forgot they already have an account sees
  "registered" and discovers the mistake on their next login attempt.
  Bounded and self-correcting. A clean async-email flow with a
  forgot-password path is the future direction but is out of scope
  for this slice — notifications infra does not yet exist.
- **Audit:** register remains deliberately out of the audit scope
  (same posture as `/login`); this slice did not add an event type.
- **Login is unchanged.** `/api/parents/login` already masks account
  existence via a uniform 401 for both unknown-email and
  wrong-password, so this slice did not need to touch it.

## Google sign-in

Additive parent-auth method alongside email/password. Implemented
Google-specifically — there is deliberately no provider-agnostic
external-auth abstraction, no Apple/Facebook/phone shim, and no
refresh-token or disconnect-google flow in this slice.

- **Feature gate.** Controlled by `GoogleAuth:ClientId`. Empty /
  missing config means the feature is off: `POST /api/parents/google-login`
  returns **404** (concealment fail-closed, same posture as
  `/metrics`) and `GET /api/parents/google-config` returns
  `{ clientId: null }` so the dashboard hides the "Continue with
  Google" button. Enabling the feature is a one-config-key flip —
  no code change, no redeploy beyond config.

- **Endpoints.**
  - `POST /api/parents/google-login` — body
    `{ idToken: string, acceptedTerms: bool }`. Exchanges a Google
    ID token for a parent JWT. Returns the same
    `ParentLoginResponse { token }` shape as
    `POST /api/parents/login`. `[EnableRateLimiting("auth")]` —
    shares the per-IP auth bucket with register / login / password-
    change / delete-account.
  - `GET /api/parents/google-config` — public, returns the
    configured Google client id or `null`. Not rate-limited (static
    per deployment; not an account-existence signal).

- **Linking rules** (in order — first match wins):
  1. Lookup by `Parent.GoogleSubject == sub` (and `AnonymizedAt == null`)
     → sign in (returning user), stamp `LastLoginAt`, audit
     `first_time=false linked_to_password_account=false`.
  2. Lookup by `Parent.Email == claimEmail` (and `AnonymizedAt == null`)
     with `GoogleSubject == null` → link: stamp `GoogleSubject`,
     stamp `EmailVerifiedAt` **only if currently null** (never
     overwrite), stamp `LastLoginAt`, audit `first_time=true
     linked_to_password_account=true`.
  3. Email match with non-null different `GoogleSubject` → uniform
     auth failure (never overwrite; takeover primitive).
  4. Else create a new Parent row with `PasswordHash = ""`,
     `EmailVerifiedAt = UtcNow`, `TermsAcceptedAt = UtcNow`,
     `TermsVersion = current`, `LastLoginAt = UtcNow`. Requires
     `acceptedTerms == true`; else returns `TermsRequired` (400).

- **`email_verified: true` is load-bearing.** The Google ID token's
  `email_verified` claim MUST be true — any false / missing value
  is rejected with the uniform auth-failure response. This is the
  gate that makes "Google-linked accounts set `EmailVerifiedAt`"
  safe: an attacker-controlled unverified external address on a
  Google profile cannot stamp verification onto a row they don't
  own. Audience (`aud`) must equal `GoogleAuth:ClientId`; the
  validator library enforces this and the service re-checks
  defensively.

- **No password-flow changes.** Register, login, password change,
  forgot-password (request + reset), email verification (request +
  complete), account delete — all endpoints, anti-enumeration
  contracts, timing normalization, and uniform-400 failure shapes
  are unchanged. Google sign-in is additive, not a replacement.

- **Audit.** Each successful sign-in writes exactly one
  `ParentGoogleSignIn` row with metadata
  `{ first_time, linked_to_password_account }`. No email, no
  subject, no token, no device id. Failure paths write nothing.

- **Schema.** One additive column: `Parent.GoogleSubject : string?`
  with a filtered unique index (`WHERE "GoogleSubject" IS NOT NULL`)
  so password-only parents (sub null) coexist freely. The
  anonymize scrub nulls this column along with `Email` / `PasswordHash`
  so a later fresh Google sign-in using a previously-anonymized
  identity can create a new Parent row without colliding.

- **Validator seam.** `IGoogleIdTokenValidator` in
  `Application/Auth/`, with a production
  `GoogleIdTokenValidator` (`Infrastructure/Auth/`) wrapping
  `Google.Apis.Auth.GoogleJsonWebSignature.ValidateAsync` — JWKS
  fetch, `kid` rotation, `iss`/`aud`/`exp` checks all handled by
  the library. Tests swap a fake double; no test hits real Google
  endpoints.

## JWT key rotation

Parent JWTs support rotation: new tokens are signed with the **primary
key only**, while the validator accepts the full ordered list of
configured active keys. During rotation, a token still signed by the
previous key keeps working for its lifetime without a forced flush.

- **Preferred config** — `Jwt:Keys` ordered array. Index 0 is the
  primary (signing) key; indexes 1+ are accepted at validation only.
- **Legacy fallback** — scalar `Jwt:Key` is still honored as a
  single-element list. Deployments that predate this slice keep
  working unchanged. When both are present, `Jwt:Keys` wins.
- **Signing** — `ParentService.GenerateJwt` resolves the list via
  `JwtKeys.ResolveOrderedKeys` and signs with `JwtKeys.PrimaryKey`
  (HS256). It never falls back to a previous key for signing.
- **Validation** — `Program.cs` populates
  `TokenValidationParameters.IssuerSigningKeys` with every resolved
  key; existing issuer / audience / lifetime / signature checks are
  unchanged.

**Invariants (do not regress)**:
- The legacy-insecure-default literal
  (`ArmenianAiToyDefaultSecretKeyThatShouldBeChanged123!`) is
  rejected whether it appears in `Jwt:Key`, in `Jwt:Keys[0]`, or in
  **any** `Jwt:Keys[n]` — a single poisoned entry poisons the whole
  set. Guards against a paste from old appsettings history sneaking
  back in as a "previous key."
- Empty / whitespace-only entries in `Jwt:Keys` are filtered before
  the length check. An empty resulting set throws at startup.
- Signing always uses the first element. If a future edit changes
  this to scan the list or pick by some other rule, the
  `SigningSide_TokenSignedWithPrimary_FailsValidationAgainstOnlyPreviousKey`
  test fails.

## Host filtering & CORS (network-origin guards)

**CORS (#037).** `AddCors` default policy is permissive (`AllowAnyOrigin`)
**only** in Development; in any other environment it allows just the
origins listed in `Cors:AllowedOrigins` (empty ⇒ no cross-origin access).
The parent dashboard, admin console, and device are all same-origin, so the
strict prod policy doesn't affect them.

**Host filtering (#061).** `HostFilteringConfig.Resolve(isDevelopment,
AllowedHosts)` (pure helper in `Api/Security/`) feeds
`HostFilteringOptions` from `Program.cs`:
- **Development** → permissive (`*`), so a bench reached by IP keeps working.
- **Other environment, hosts pinned** → restrict to the semicolon-separated
  names in `AllowedHosts` (a bare `*` entry is stripped).
- **Other environment, unpinned** → STAYS permissive but logs a loud startup
  `LogWarning`. Failing closed (rejecting every request on a forgotten config
  key) would be a worse outage than a permissive filter, so the warning is
  the signal, not a hard block.
The `Configure<HostFilteringOptions>` call runs last, so it overrides the
framework's default `AllowedHosts`-config binding. Base `appsettings.json`
ships `AllowedHosts: ""` (nothing pinned); `appsettings.Development.json`
ships `"*"`. **Operators must set real hostnames in prod** to enable
Host-header filtering and silence the warning. Pinned by
`HostFilteringConfigTests`.

**HTTPS hardening (#007/#008).** `HttpsHardeningConfig.Resolve(requireHttps,
hstsMaxAgeDays)` (pure helper in `Api/Security/`) gates app-side HTTP→HTTPS
redirect + HSTS. **OFF by default in EVERY environment** — it is an explicit
deploy switch (`Security:RequireHttps`, default `false`), not environment-
derived, because whether the app should redirect/emit HSTS depends on the
topology the operator picks: TLS at Kestrel (enable here) vs. TLS at a reverse
proxy (usually let the proxy own it; if enabling here, the proxy must forward
`X-Forwarded-Proto` and `ForwardedHeaders` #039 must be on, or the redirect
loops). When enabled, `Program.cs` calls `app.UseHsts()` + `UseHttpsRedirection()`
right after `UseForwardedHeaders` (so the redirect sees the real scheme), and
`AddHsts` is registered with `Security:HstsMaxAgeDays` (default 365, floor-
clamped to 1). Default-off means dev/bench are unaffected. Pinned by
`HttpsHardeningConfigTests`. NOTE: this is the app-side half only — a domain +
certificate (at Kestrel or the proxy) is the owner/infra half still pending.

## Rate limiting

Two named ASP.NET rate-limit policies, both fixed-window, both
served by the same `OnRejected` handler in `Program.cs` (shared
`aat_rate_limit_rejected_total` counter and `{ error: "Too many
requests. Please slow down." }` 429 body).

**Per-account login throttle (#040)** complements the per-IP `auth`
policy below: the IP limiter does nothing against a targeted-takeover
attempt spread across many IPs. `LoginAttemptThrottle`
(`Application/Auth`, DI **singleton**, process-local) tracks FAILED
logins per account (keyed on the submitted email, across all IPs) and,
after 10 failures within a 15-min window, locks that email out for a
15-min cooldown (resets on success / window lapse). `LoginAsync`
checks it BEFORE the BCrypt verify and returns the SAME uniform null
(→ 401) as a wrong password, so a locked account is not an enumeration
oracle and costs no BCrypt during cooldown; unknown emails are tracked
identically. Temporary-lockout posture is a deliberate, owner-approved
trade-off (a bounded lockout-DoS is possible); a `MaxTrackedAccounts`
cap bounds memory. Multi-instance would need a shared store (noted).
Pinned by `LoginAttemptThrottleTests` + `ParentServiceLoginThrottleTests`.

- **`chat`** — per-device bucket keyed on the `X-Device-Id` header.
  Applied via `[EnableRateLimiting("chat")]` on `ChatController`.
  Defaults: `RateLimiting:Chat:PermitLimit = 30`,
  `RateLimiting:Chat:WindowSeconds = 60`. Cost-containment: sits
  ahead of `DeviceAuthMiddleware` so rejected requests never hit
  the DB or OpenAI. See `RateLimiting/ChatRateLimiter.cs`.

- **`auth`** — per-caller-IP bucket keyed on
  `Connection.RemoteIpAddress`. Applied per-action to the four
  parent auth / account-sensitive endpoints:
  `POST /api/parents/register`, `POST /api/parents/login`,
  `POST /api/parents/password`, `DELETE /api/parents/account`.
  Defaults: `RateLimiting:Auth:PermitLimit = 10`,
  `RateLimiting:Auth:WindowSeconds = 60` (tighter than chat —
  these are authentication actions, not a per-utterance pipeline).
  See `RateLimiting/AuthRateLimiter.cs`.

**Invariants (do not regress)**:
- The two policies are **separate buckets** — do not merge, do not
  let chat traffic consume the auth quota or vice versa.
- The limiters key on `Connection.RemoteIpAddress` only — they NEVER
  read `X-Forwarded-For` directly (that header is attacker-controlled).
  **Proxy-aware keying is now an opt-in seam (#039)**: when
  `ForwardedHeaders:Enabled=true` AND `ForwardedHeaders:KnownProxies`
  lists at least one valid proxy, `Program.cs` registers
  `UseForwardedHeaders` FIRST and it rewrites `RemoteIpAddress` from XFF
  — trusting ONLY those proxies — so the limiters key on the real client
  IP with no limiter-code change. Shipped default is OFF
  (`ForwardedHeadersConfig.TryBuild` returns null), so XFF is not
  processed and `RemoteIpAddress` stays the direct TCP peer. Enabled-but-
  no-valid-proxy fails safe to OFF (never trust all upstreams). The
  limiter-level contract that XFF is never read directly is pinned by
  `AuthRateLimiterTests.PolicyFactory_IgnoresXForwardedForHeader_InThisSlice`;
  the opt-in resolution is pinned by `ForwardedHeadersConfigTests`.
- Do not apply `[EnableRateLimiting("auth")]` to read-only parent
  endpoints (device listings, conversation reads, audit history,
  export) or to control endpoints that are already JWT-gated
  behind a parent session (pause/resume, bedtime, mode flags,
  link/unlink, delete-child, delete-conversation). They are not
  brute-force surfaces. Tests pin the non-applied set.
- `aat_rate_limit_rejected_total` carries a **bounded two-value**
  `policy` tag (`chat` / `auth`). The tag is derived from the matched
  endpoint's `[EnableRateLimiting]` metadata by
  `RateLimitRejectionPolicy.ResolvePolicyTag`, not from the request
  path. A future third policy would extend the `policy` value space
  by one, not add a new tag. The AppMeter no-high-cardinality
  invariant still binds: do NOT add `ip`, `device_id`, `route`,
  `email`, or any per-caller tag on this counter.

## Structured console logging

Console output is JSON, produced by the built-in
`JsonConsoleFormatter` and configured via `Logging:Console` in
`appsettings.json`:

- `FormatterName: "json"` — every log line is a single JSON object.
- `FormatterOptions.IncludeScopes: true` — ASP.NET Core's automatic
  request scope (`RequestId`, `RequestPath`, `SpanId`) surfaces in the
  `Scopes` field so a single HTTP request's lines are correlatable.
- `FormatterOptions.UseUtcTimestamp: true` +
  `TimestampFormat: "yyyy-MM-ddTHH:mm:ss.fffZ"` — ISO-like UTC
  timestamps regardless of host timezone.

Existing `ILogger<T>` call sites were **not changed**. Their named
template holes (e.g. `"Parent {ParentId} changed password"`) already
carried structured data inside the logger pipeline; switching the
formatter is what makes them land as named JSON fields on the wire.

**Complementary to audit rows, not a replacement.** Audit writes to the
`AuditEvents` DB table for the four destructive/auth-sensitive parent
actions and provides durability. Structured console logs cover the
broader operational surface (chat orchestration, moderation outcomes,
rate-limit rejections, startup, Path-5 failures) and provide live
visibility. Both channels exist by design — do not dedupe one into the
other.

No Serilog, no external sinks beyond stdout, no custom enrichers,
no request-logging middleware in this slice; stdout-JSON only.
OpenTelemetry metrics + auto-collected HTTP traces are wired in a
separate layer — see § Metrics below.

## Metrics (OpenTelemetry + Prometheus)

First observability slice beyond logs. Counters + two latency
histograms; no custom trace spans. Auto-collected HTTP traces
(AspNetCore + HttpClient instrumentation) are captured for free and
exported to the console in Development only; no OTLP endpoint is
assumed.

**Scrape endpoint**: `GET /metrics` (Prometheus text-format exposition),
registered via `OpenTelemetry.Exporter.Prometheus.AspNetCore`.
**Guarded by a narrow `Authorization: Bearer <token>` check** implemented
in `Observability/MetricsScrapeAuth.cs` and wired as an inline middleware
immediately before the OTel scrape mapping in `Program.cs`. The guard
only affects `/metrics`; unrelated endpoints are untouched.

- `Metrics:ScrapeToken` (string, default `""`) — the expected bearer
  token. Empty means "no token configured."
- `Metrics:AllowUnauthenticatedScrape` (bool, default `false`) — the
  explicit dev/local bypass. Tied to this flag, not to the Development
  environment, so a forgotten dev shortcut cannot silently expose
  metrics in prod.

**Shipped default is fail-closed.** With both keys at their
`appsettings.json` defaults, every request to `/metrics` gets a **404**
(concealment over 401: the scanner learns nothing about the endpoint's
existence, and we don't mimic a standard auth scheme we are not running).
Operators opt in by either setting the token and configuring Prometheus
to send `Authorization: Bearer <token>`, or flipping
`AllowUnauthenticatedScrape` to `true` in a local overlay. When
authenticated (or bypass on), the response body is exactly what the OTel
exporter would have produced — this guard changes who can read the
aggregate surface, not what the surface contains. Token compare is
constant-time via `CryptographicOperations.FixedTimeEquals`. The
no-high-cardinality invariant below continues to apply; this guard is
about access control, not about cardinality.

**Counters exposed (meter name `ArmenianAiToy`)**:

| Counter | Tag(s) | Tag value space | Increment site |
|---|---|---|---|
| `aat_chat_gate_trip_total` | `gate` | `paused` / `bedtime` / `mode_disabled` | `ChatController.Chat` short-circuit branches |
| `aat_chat_openai_failure_total` | `kind` | `rate_limited` / `timeout` / `upstream_5xx` / `auth_failure` / `other` | `OpenAIReliabilityGate` (after classification) |
| `aat_chat_openai_retry_total` | — | — | `OpenAIReliabilityGate` (before each retry attempt) |
| `aat_chat_openai_circuit_trip_total` | — | — | `OpenAIReliabilityGate` on each closed→open transition |
| `aat_chat_openai_circuit_short_circuit_total` | — | — | `OpenAIReliabilityGate` on each fail-fast while open |
| `aat_rate_limit_rejected_total` | `policy` | `chat` / `auth` | Shared `OnRejected` handler in `Program.cs`; tag derived from the matched endpoint's `[EnableRateLimiting]` metadata via `RateLimitRejectionPolicy.ResolvePolicyTag` |
| `aat_health_probe_total` | `result` | `ok` / `unhealthy` | `GET /api/health` endpoint lambda |
| `aat_audit_events_written_total` | `event_type` | enum names of `AuditEventType` | `ParentService.TrackAndAddAudit` helper on every successful `AuditEvent` write |
| `aat_moderation_failclosed_total` | `reason` | `rate_limited_retry_failed` / `auth_error` / `server_error` / `timeout` / `network_error` / `parse_error` / `unknown` | `OpenAIModerationAdapter.FailClosed` (one increment per outer `CheckContentAsync` that ends in fail-closed; genuine content flags do NOT increment this counter) |

**Invariants (do not regress)**:

- **No high-cardinality tags.** Tag values must come from small,
  bounded enumerations. Do NOT add `device_id`, `parent_id`,
  `child_id`, `mac_address`, or any free-form string as a metric
  tag. If you need that granularity, use the `AuditEvents` table
  (durable, queryable) or the structured log stream — not metrics.
- **Complementary to audit, not a replacement.** The audit counter
  is a volatile "are writes happening at all" pulse; the
  `AuditEvents` DB table remains the source of truth for which
  actions happened.
- **No custom spans in `ChatService`, `ModeDetector`, or the system
  prompt path.** Those files stay HIGH-risk and untouched by this
  slice.

**Packages**: `OpenTelemetry.Extensions.Hosting`,
`OpenTelemetry.Instrumentation.AspNetCore`,
`OpenTelemetry.Instrumentation.Runtime`,
`OpenTelemetry.Instrumentation.Http`,
`OpenTelemetry.Exporter.Console`,
`OpenTelemetry.Exporter.Prometheus.AspNetCore` (pre-release —
currently `1.15.3-beta.1`; the OpenTelemetry SIG keeps the
Prometheus-side exporter in `-beta` deliberately even though it is
mature in practice).

### Health endpoint (#070)

`GET /api/health` returns `{ status, service, database, openai }`.

- **Liveness verdict (200 vs 503) is DB-only**, on purpose. OpenAI is a
  SHARED downstream; failing liveness during an OpenAI outage would pull
  every instance from the load balancer at once — a self-inflicted
  fleet-wide outage on hosts that are otherwise fine. `status` /
  `database` reflect only `HealthProbe.IsDatabaseReachableAsync`.
- **`openai` is a NON-FATAL readiness field.** `"degraded"` when the
  reliability gate's circuit breaker is currently open (recent real
  failures), else `"ok"`. Sourced from
  `OpenAIReliabilityGate.IsCircuitOpen()` — a **passive, zero-cost**
  snapshot (no upstream probe call, no quota burn) that reads breaker
  state under the gate's lock and never mutates it (cannot consume the
  half-open probe). It does NOT change the HTTP status; it is for
  dashboards/alerts and complements `aat_chat_openai_circuit_trip_total`.
- No active OpenAI probe on the health tick by design — see the
  `HealthProbe` xmldoc rationale.

### Latency histograms

Two `Histogram<double>` instruments on the `ArmenianAiToy` meter,
unit = **seconds**:

| Histogram | Where recorded | Scope |
|---|---|---|
| `aat_chat_openai_duration_seconds` | `OpenAIReliabilityGate.RunAsync` (outer `try/finally`) | End-to-end gated call — **includes retry/backoff** and near-zero short-circuit samples when the breaker is open. |
| `aat_moderation_classify_duration_seconds` | `OpenAIModerationAdapter.CheckContentAsync` (outer `try/finally`, reuses the existing `Stopwatch`) | End-to-end moderation call including the D1 single-retry-on-429 and every fail-closed branch. |

Both use identical **explicit** bucket boundaries (seconds), wired
via `AddView` in `Program.cs`:

```
0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30
```

**Untagged in this slice — deliberate.** Splitting by outcome/kind
would duplicate signal already present on the existing counters
(`aat_chat_openai_failure_total{kind=…}`, `aat_chat_openai_retry_total`,
`aat_chat_openai_circuit_*`). The same no-high-cardinality invariant
from AppMeter applies: do NOT add `device_id`, `parent_id`,
`child_id`, `mac_address`, `model_name`, or free-form strings as tags
on these histograms.

### OpenAI reliability

`OpenAIReliabilityGate` wraps the chat SDK call with a classification-
aware retry policy and a minimal circuit breaker. User-facing response
shape is **unchanged** — on final failure the gate rethrows the
classified exception, which `ChatController`'s existing Path-5 catch
returns as the same sanitized 502.

**Failure classes** (`OpenAIFailureKind`): `RateLimited` (HTTP 429),
`Timeout` (`OperationCanceledException` / `TimeoutException`),
`UpstreamServerError` (HTTP 5xx), `AuthFailure` (HTTP 401/403),
`Other` (everything else).

**Retry policy**: at most one retry (`MaxAttempts = 2`); retryable
kinds are `RateLimited`, `Timeout`, `UpstreamServerError`.
`AuthFailure` and `Other` are **never** retried. Backoff is ~500 ms
with ±25% jitter; 429 honors a `Retry-After` header when present,
capped at 5 seconds. The existing 30-second adapter timeout is the
outer ceiling.

**Circuit breaker**: trips after **5 failures within a 30-second
rolling window**; open for **60 seconds**; at end-of-open one
half-open probe is allowed — success closes, failure reopens.
Short-circuited calls throw `OpenAIReliabilityCircuitOpenException`,
caught by the same Path-5 catch. One `LogWarning` on each
closed→open transition.

**Scope — moderation is NOT routed through this gate.**
`OpenAIModerationAdapter` has its own purpose-specific D1 policy
(single retry on 429, never-retry on 5xx / timeout / auth) and a
**fail-closed-to-sentinel** contract (`ModerationResult(IsSafe=false,
["moderation_unavailable"])`) that's child-safety-critical — a
moderation failure must always surface to `ChatService` as "unsafe,"
never as an exception. Routing moderation through the general-purpose
gate would change retry semantics for 5xx and timeout (the gate
retries them; moderation deliberately does not). See
`ModerationFailClosedTests` for the safety contract.

## Voice chat (C1 — Toy MVP)

First voice path in the repo. The product identity is a physical
Armenian-speaking toy; C1 ships the **backend half** of the
button-to-talk loop so a later firmware slice can attach to a
working endpoint.

- **Endpoint**: `POST /api/chat/audio`, device-authenticated via
  the existing `X-Device-Id` / `X-Api-Key` headers, rate-limited
  on the same `chat` policy as `POST /api/chat`. Request body is
  raw audio (default `audio/wav`); response body is MP3
  (`audio/mpeg`). Buffered response in C1; streaming is a C2+
  follow-up.
- **Voice is transport, text is canonical.** Audio in →
  `IAudioTranscriptionService` (OpenAI Whisper, forced
  `Language = "hy"`) → existing `ChatService.GetResponseAsync`
  pipeline **unchanged** → `IAudioSynthesisService` (OpenAI TTS
  `tts-1`, voice `Nova`, default MP3) → audio out.
  `Message.Content` remains the canonical textual record; audio
  is an attachment referenced by the existing
  `Message.AudioBlobPath` column (no new column, no migration).
- **Gates preserved**: paused / bedtime / Story-disabled short-
  circuit **before STT** — zero upstream cost on a gated
  request. Paused + bedtime reuse the extracted
  `ChatGateEvaluator` (same helper the text path now calls);
  Story-disabled on the voice path is checked directly against
  `IsModeEnabledForRequestAsync(deviceId, null, DetectedMode.Story)`
  because C1 voice is Story-only (other modes over voice come
  later).
- **Persistence**: both blobs. Child audio stored at
  `{convId:N}/{userMsgId:N}.{ext}` (extension from inbound
  Content-Type, default `.wav`); assistant audio stored at
  `{convId:N}/{assistantMsgId:N}.mp3`. Paths are relative — the
  DB column does not carry an absolute filesystem path.
  `Message.AudioBlobPath` is updated with an additional
  `SaveChangesAsync` after the blob writes (ChatService's
  persistence is not changed). The user-message id is recovered
  by querying the conversation for the most-recent `Role=User`
  row — ChatService writes it synchronously before the LLM
  call, so ordering by `Timestamp DESC` yields it deterministically.
- **Moderation fallback** flows through unchanged: ChatService
  returns the safety-fallback message as an `Assistant` row
  with `SafetyFlag.Blocked`; the audio controller TTS-renders
  that fallback text and stores both blobs normally. No new
  moderation surface on the voice path.
- **Canned fallback audio** for gated paths uses a tiny in-
  memory lazy cache (`CannedVoiceClips`): first gated hit
  renders via TTS and caches for the process lifetime. Zero
  committed audio files, zero manual audio-asset work — a
  later phase can swap to on-disk pre-rendered clips without
  changing any caller. Copy: paused / bedtime reuse the
  existing `ChatController.PausedResponse` text verbatim;
  mode-disabled reuses `ChatController.ModeDisabledResponse`.
- **Blob store**: `LocalDiskAudioBlobStore` under
  `Audio:BlobStoreRoot` (default `audio-blobs`). No
  `DeleteAsync` method — retention cascade on
  conversation-delete / device-delete / parent-anonymize is
  explicitly deferred to C2. Bench-scale usage is fine; local
  runs can `rm -rf` the root between sessions.
- **Failures are sanitized**: STT / TTS / ChatService
  exceptions collapse to the same 502 with body
  `{ "error": "AI service unavailable. Please try again." }`
  the text path already returns. No provider detail on the
  wire. Existing `ChatControllerPath5Tests` contract is
  mirrored by new `AudioChatControllerTests`.
- **No new**: migration, DB column, NuGet package, audit event
  type, rate-limit policy, dashboard view, metric counter.

**Not in C1**:
- Streaming response body (buffered only).
- Parent-dashboard "▶ Listen" button + blob-read endpoint (C2).
- Retention cascade for audio blobs (C2).
- Orphan blob sweeper (C2).
- Audio inclusion / exclusion in `GET /api/parents/export` (C2).
- Wake word / voice biometrics / barge-in / latency
  instrumentation split / second mode over voice / firmware
  code.

## Voice chat (C2.1 — assistant replay)

Closes the parent half of the C1 voice loop — parents can play
back what *Areg* said, but not what the child said. Strict
assistant-only contract.

- **Endpoint**: `GET /api/parents/messages/{messageId}/audio`,
  parent-JWT authenticated. Streams the assistant MP3 with
  `Content-Type: audio/mpeg`. **Uniform 404** with body
  `{ "error": "Audio not available." }` for every miss reason —
  unknown id, message owned by a different family, user/child
  role, null `AudioBlobPath`, blob file missing on disk, **or
  blob MIME other than `audio/mpeg`**. A parent cannot probe
  message existence, ownership across families, or attachment
  state.
- **MIME contract enforced at the HTTP boundary.** The controller
  whitelists `audio/mpeg` after the blob store read and serves
  the byte stream with a *constant* `audio/mpeg` content-type
  rather than echoing whatever the store reported. Defense-in-
  depth: today the only writer (`AudioChatController`) only
  persists assistant audio as MP3, so the whitelist is dead code
  in practice — but a future codec change, manual file
  placement, or misbehaving store implementation must not be
  able to leak a non-MP3 payload through this endpoint.
- **Ownership chain**: Message → Conversation → Device →
  ParentDevice, joined in a single query in
  `ParentService.GetAssistantAudioMessageAsync`. Role + audio-path
  gates are pushed into the same query so an unauthorized
  message never causes a blob-store probe.
- **`MessageDto.AudioAvailable`**: new field on the existing wire
  shape. Set to `true` ONLY when `Role == Assistant` AND
  `AudioBlobPath != null`. Drives the dashboard's `▶ Listen`
  affordance. **Child WAV uploads MUST NOT expose `true`** even
  if their `AudioBlobPath` is populated — the role gate is
  applied at every projection site (`ConversationService`,
  `ParentService.BuildExportAsync`) so the wire shape is the
  single source of truth.
- **Dashboard surface**: `parent.html` adds a `▶ Listen` button
  and inline status line on assistant messages with
  `audioAvailable === true`. The audio is fetched with the
  `Authorization` header, decoded into a `Blob`, and exposed via
  `URL.createObjectURL` so the bytes never appear in the URL bar
  / history. The Object URL is revoked on `ended` to avoid
  per-page memory growth across long detail sessions. The
  button is replaced by an `<audio controls autoplay>` element
  on first success so re-listens use the native player.
- **Pinned by `ParentMessageAudioTests`**: success, parent JWT
  required (`[Authorize]` attribute), not owned, unknown id,
  user-WAV role gate (KEYSTONE), null `AudioBlobPath`, missing
  blob, and a multi-branch "all miss reasons return null"
  no-existence-leak assertion.

**Not in C2.1** (deliberate — moved to C2.2):
- Child WAV playback.
- Audio export ZIP / binary inclusion in
  `GET /api/parents/export`.
- Retention cascade on conversation-delete /
  device-delete / parent-anonymize for audio blobs.
- Orphan blob sweeper.
- New audit event for the read endpoint (the durable record of
  *what was said* already lives on the conversation; logging
  every replay click would be PII-adjacent and out of scope).

The dangling-reference disclaimer in the Retention section
still applies — `Message.AudioBlobPath` is owned externally;
no code in this repo deletes blobs today. C2.2 owns that
cleanup hook.

## Voice chat (C2.2a — parent-driven blob delete cascade)

Hooks audio-blob cleanup into the four destructive
`ParentService` paths so blobs no longer outlive their database
records. Retention-worker and dormancy paths are out of scope —
they land in a follow-up C2.2b slice.

- **New blob-store API**:
  `IAudioBlobStore.DeleteConversationAudioAsync(Guid, CancellationToken)`
  returning a compact `AudioBlobDeleteResult` record
  (`FilesDeleted`, `DirectoryMissing`, `Failed`, `ErrorMessage`).
  The local layout groups every blob for a conversation under
  `{conversationId:N}/`, so cleanup is a single recursive directory
  delete in practice. Idempotent on a missing directory; never
  throws on per-file IO failure (cooperative cancellation does
  propagate).

- **Hooked sites**:
  - `ParentService.DeleteConversationAsync` — single conversation.
  - `ParentService.DeleteChildAsync` — every conversation the child
    owned.
  - `ParentService.UnlinkDeviceAsync` — orphan-cascade branch only;
    the still-linked branch performs no cleanup (audit metadata
    counts are zero on that branch).
  - `ParentService.DeleteAccountAsync` — every conversation under
    each *orphaned* device. Devices still linked to another parent
    keep their data and audio.

- **DB-first ordering**. Each site:
  1. Snapshots affected conversation ids,
  2. Mutates the DbSet and runs `SaveChangesAsync` (FK cascade
     handles Messages/Children/etc.),
  3. Calls `DeleteConversationAudioAsync` per id and aggregates
     counts,
  4. Writes the existing destructive audit row with the post-
     cleanup counts in metadata, then `SaveChangesAsync` again.
  Two SaveChanges per parent action by design: a blob-store IO
  failure cannot roll back a parent-initiated delete, and the
  durable audit row reflects what actually hit disk.

- **Audit metadata extension**. Three new keys appear in the
  `ParentConversationDeleted`, `ParentChildDeleted`,
  `ParentDeviceUnlinked`, and `ParentAccountDeleted` factories:
  - `audio_conversations_attempted: int`
  - `audio_files_deleted: int`
  - `audio_delete_failures: int`
  Counts only — no paths, no PII, mirrors the existing
  `messages_deleted` discipline. Defaults to zero on the
  still-linked unlink branch and on any factory call that omits
  the new parameters (additive-only signature change keeps every
  existing caller compiling).

- **Constructor seam**. `ParentService` gains an optional
  `IAudioBlobStore? blobStore` parameter that defaults to a
  private `NullAudioBlobStore` (returns `DirectoryMissing=true,
  FilesDeleted=0, Failed=false`). Real DI registers
  `LocalDiskAudioBlobStore`; the null fallback exists so the many
  pre-existing `ParentService`-constructing tests for
  non-destructive flows (pause, bedtime, mode flags, etc.)
  continue to compile and run unchanged.

- **No retention/dormancy hook in this slice**. Retention purge,
  dormant-parent anonymize, and dormant-device delete still
  accumulate orphaned blobs until a follow-up slice (C2.2b)
  attaches the same cleanup pattern there. The "audio blob
  retention is owned externally" disclaimer in
  § Retention remains true for those paths.

- **Orphan sweeper still deferred** to C2.3. C2.2a only catches
  blobs at known delete sites; orphans produced by IO failures
  here, or by retention paths that don't yet hook the API, are
  the orphan sweeper's job.

**Invariants pinned by tests** (do not regress):
- `LocalDiskAudioBlobStoreTests` — directory removal, missing-dir
  idempotent success, sibling-conversation isolation, empty-dir
  cleanup, cancellation propagation, locked-file partial delete.
- `ParentServiceAudioCascadeTests` — each of the four hooked
  sites cleans the right scope (and only that scope), audit
  metadata carries the three new keys with the correct counts,
  unrelated audio is preserved, and a failing blob store does
  NOT break the parent action.

## Voice chat (C2.2b — retention/dormancy blob delete cascade)

Extends the C2.2a parent-driven cleanup to the
`RetentionPurgeService` lifecycle paths so audio blobs no longer
outlive their database records when destruction comes from the
background worker rather than the parent dashboard. Parent-
driven C2.2a behavior is unchanged.

- **Hooked passes** (all three system-actor destructive sites):
  - `PurgeExpiredConversationsAsync` — every conversation past the
    retention cutoff. Conversation ids are already collected in
    the existing eligibility projection; cleanup reuses that list
    verbatim.
  - `AnonymizeDormantParentsAsync` — every conversation under any
    *orphaned* device the dormant parent unlinked. Per-orphaned-
    device conversation-id projection runs inside the existing
    orphan loop, BEFORE the device's `Remove`, so the FK cascade
    does not erase the information needed for cleanup. **Shared
    devices skip the projection by construction**, contribute zero
    ids, and their audio is preserved.
  - `DeleteDormantDevicesAsync` — every conversation on the device.
    Per-device conversation-id projection runs just before the
    `Device.Remove`. Eligibility-set already gates "this device is
    about to be deleted"; no risk of touching another device's
    blobs.

- **DB-first, two-SaveChanges shape**, identical to C2.2a:
  1. Snapshot conversation ids.
  2. Mutate the DbSet, run `SaveChangesAsync` (FK cascade handles
     Messages / Children / ParentDevices). Audit row is held back.
  3. Run `IAudioBlobStore.DeleteConversationAudioAsync` per id and
     aggregate counts via a private `RunAudioCleanupAsync` helper
     on the worker.
  4. Add the existing system-actor audit row with the post-cleanup
     counts in metadata, then `SaveChangesAsync` again.

- **Audit metadata extension**, same three keys C2.2a uses, added
  as additive optional parameters with defaults of zero on the
  three system-actor factories: `ConversationsPurgedByRetention`,
  `ParentDormancyAnonymized`, `DeviceDormancyDeleted`. **Always
  populated**, even on no-cleanup branches (shared device, empty
  device) — zero is the honest signal for "no audio to clean
  here," and never omitting the keys means downstream readers can
  sum across audit events without special-casing event types.
  No new `AuditEventType` values; no PII; mirrors the existing
  counts-only discipline (`conversations_deleted`, etc.).

- **`IAudioBlobStore` resolution**. Resolved from the per-tick
  scope via `scope.ServiceProvider.GetRequiredService<IAudioBlobStore>()`,
  same convention as `AppDbContext` and `INotifier`. The
  `RetentionPurgeService` constructor signature is unchanged —
  test harnesses register `IAudioBlobStore` on the same
  `ServiceCollection` and pick it up transparently.

- **Helper duplication, not extraction**. C2.2a's
  `RunAudioCleanupAsync` lives privately on `ParentService`;
  C2.2b adds an equivalent private helper on
  `RetentionPurgeService`. Same shape (`(int Attempted, int
  FilesDeleted, int Failures)`), same swallow-and-count contract,
  ~25 lines duplicated. A shared utility would be a third caller
  away from being justified — C2.3 (orphan sweeper) is the
  natural moment to lift one if it ever appears.

- **Test infrastructure**. `RecordingBlobStore` and
  `FailingBlobStore` were lifted from
  `ParentServiceAudioCascadeTests` into
  `tests/Helpers/RecordingBlobStore.cs` so both C2.2a and C2.2b
  share the same in-memory test double. Existing
  `RetentionPurgeServiceTests` harnesses register a default
  `RecordingBlobStore` on the `ServiceCollection`; new C2.2b
  tests live in `RetentionPurgeServiceAudioCascadeTests.cs` with
  a focused harness.

**Invariants pinned by tests** (do not regress):
- `RetentionPurgeServiceAudioCascadeTests` — happy path for each
  of the three hooked sites; non-eligible conversation audio
  preserved; failing blob store does not break the tick; shared-
  device audio preserved on dormant-parent anonymize; device with
  zero conversations reports zeros; audit metadata carries the
  three new keys with the correct counts.
- All existing `RetentionPurgeServiceTests` — re-run; should be
  green (the new IAudioBlobStore registration is additive on the
  harness, no existing test logic changed).
- All C2.2a `ParentServiceAudioCascadeTests` — re-run; should be
  green (helper lift is a structural change only).

**Out of scope for C2.2b** (deliberate):
- Orphan sweeper that scans `audio-blobs/` for directories without
  a backing `Conversation` row → still C2.3.
- Promotion of the audio cleanup loop to a shared utility class →
  wait for a third caller.
- Constructor-signature change on `RetentionPurgeService` → none
  needed; per-tick scope resolution covers it.
- Token-cleanup or warn-only passes → non-destructive, no audio.

## Voice chat (C2.3 — orphan audio sweeper, deferred)

C2.1 + C2.2a + C2.2b together cover every code path that destroys
a `Conversation` row: parent-driven deletes (conversation, child,
unlink-orphan, account-orphan) and lifecycle deletes (retention
expiry, dormant-parent anonymize, dormant-device delete). Each
calls `IAudioBlobStore.DeleteConversationAudioAsync` after its DB
commit, with counts surfaced in audit metadata. The C2 voice-data
hygiene story is functionally closed for everything that goes
through the application.

A standalone filesystem orphan sweeper (C2.3) is **intentionally
deferred**. It is not needed in the current dev/QA phase and adds
non-trivial design surface (path-traversal hardening, grace-window
selection, race with concurrent writes from `AudioChatController`,
batch caps, audit framing) that benefits from being landed against
real production constraints rather than speculative ones.

**Residual orphan scenarios that C2.3 would address:**
- **Legacy / pre-C2 blobs.** Dev and QA hosts that ran C1 voice
  flows before C2.2a/b shipped have `audio-blobs/` directories
  whose backing conversations may have been deleted by code paths
  that didn't yet clean blobs. Mitigation today is operator
  discipline: `rm -rf audio-blobs/` between bench sessions.
- **`Failed=true` cleanup residue.** Per-file IO failures during
  cascade cleanup (locked file, antivirus, permissions) leave
  individual files behind. The audit row's `audio_delete_failures`
  count makes this observable; the file itself becomes unreachable
  through normal code paths after the conversation row is gone.
- **Manual DB edits / direct SQL.** A `DELETE FROM Conversations`
  outside the application strands the corresponding blob
  directory. Production must never permit this; dev/QA might.
- **Future writers we haven't built.** Today exactly one writer
  exists (`AudioChatController`). Any future code path that
  writes a blob without committing the matching `Conversation`
  row would create permanent orphans. Not a concern today; would
  be the moment to land C2.3.
- **Crash between `WriteAsync` and the metadata
  `SaveChangesAsync` in `AudioChatController.PersistAudioPathsAsync`.**
  Not a true orphan: the `Conversation` row is already committed
  by the prior `ChatService` call, so the conversation's directory
  still gets cleaned by the next destructive event. Worth naming
  here only because it looks like an orphan source on a casual
  read of the code.

**Trigger conditions** (any one flips C2.3 from deferred to
"implement now"):
- Production or closed-beta deployment of the toy with real
  child voice landing on real hosts.
- A privacy / compliance requirement that promises a hard
  deletion timeline for child audio.
- Sustained nonzero `audio_delete_failures` in the audit feed for
  more than a week — steady residue is accumulating.
- A second blob writer lands beyond `AudioChatController` (e.g.
  background TTS pre-render, server-side mixing, alternate codec
  pipeline).
- `audio-blobs/` exceeds ~5 GB on any deployed host —
  operational threshold where ad-hoc cleanup stops being
  practical.

**Forward-looking design** (for the slice when it lands; not
implemented today): a fourth pass on `RetentionPurgeService`,
disabled by default, that scans only top-level directories under
`Audio:BlobStoreRoot` whose names match `^[0-9a-fA-F]{32}$`,
projects them against `Conversations.Id`, and removes those with
no DB match older than a configurable grace window (default
24 h). Per-tick directory cap, path-traversal hardening, system-
actor audit row on tick-with-deletions only, counts-only
metadata. No content inspection, no parent-/user-facing endpoint.

## Story Q&A text harness (`POST /api/story-qa-text`)

Unauthenticated TEXT-only harness for checking in-story Q&A answer
quality from a plain HTTP client — no STT, no TTS, no device, no
persistence. Distinct from `POST /api/internal/story-qa-test` (operator
console, admin-token gated, richer diagnostics) and from
`POST /api/chat/story-qa` (the device voice path).

**Development-only.** Outside Development every request is a **404**
before validation — same fail-closed concealment posture as `/metrics`
and `/api/internal/*`. An unauthenticated route that reaches GPT must not
exist in a deployed image: it would be an open relay against the
deployment's own OpenAI key, and it is outside the per-device daily cost
cap (which keys on `X-Device-Id`). The bench is unaffected —
`run-local.ps1` sets `ASPNETCORE_ENVIRONMENT=Development`, while every
deploy runbook sets `Production`.

**Dual moderation, mirroring the voice path.** `LibraryStoryQuestionService`
has no moderation of its own (it takes only `IAiChatClient`), and
`StoryAnswerFilter` validates story fidelity and format — it is **not** a
safety classifier. So the controller owns both checks:
- **Input** moderated BEFORE any model call. Unsafe ⇒ 200 with
  `Answer = StoryAnswerFilter.SafeFallback`, `UsedFallback = true`,
  `FirstRejection = "moderation_blocked"`, and GPT is never called.
- **Output** moderated only when the answer is model-authored
  (`!UsedFallback`). Unsafe ⇒ same fallback shape with
  `FirstRejection = "output_blocked"`. The canned fallback is
  pre-reviewed text and deliberately skips the second classifier call.
- `moderation_unavailable` is fail-closed to unsafe, same as everywhere
  else. Wire shape is unchanged — no new fields.

Pinned by `StoryQaTextControllerModerationTests` (keystones:
`UnsafeQuestion_NeverCallsGpt_ReturnsSafeFallback`,
`UnsafeAnswer_IsOutputModerated_ReturnsFallback_NotTheAnswer`).

## Internal console (superuser)

A read-only operator god-view across the WHOLE system — all parents,
devices, conversations, stories, audit, and cost. Distinct from the
parent dashboard (`parent.html`), which is per-parent-scoped. This is
the owner/operator surface.

**Auth — config admin token, fail-closed.** Gated by
`Api/Observability/InternalAdminAuth.cs`, a clone of the
`MetricsScrapeAuth` pattern: a bearer-token check wired as inline
middleware in `Program.cs` over the `/api/internal/*` path prefix, run
**before** `MapControllers`. NOT the parent JWT pipeline.

- `Internal:Operators` (default `[]`) — **preferred**: an ordered list of
  named per-operator credentials `[{ "Name": "...", "Token": "..." }]`. The
  presented bearer is matched constant-time against each operator's token;
  the FIRST match wins and resolves to that operator's name. A leaked token
  traces to one operator and can be revoked (drop that entry) without
  rotating everyone else.
- `Internal:AdminToken` (default `""`) — legacy single shared token, still
  honored as a fallback after the named operators. A match here resolves to
  the sentinel name `"admin (shared token)"` (no per-operator identity).
- `Internal:AllowUnauthenticated` (default `false`) — explicit dev bypass;
  resolves to the sentinel name `"dev-bypass"`.
- **Shipped default is fail-closed**: with `Operators` empty, `AdminToken`
  empty, and the bypass off, every `/api/internal/*` request gets a **404**
  (concealment, same posture as `/metrics`). Operator opts in by adding a
  named operator (or setting the legacy token) and sending
  `Authorization: Bearer <token>`. Constant-time compare throughout.
- The gate resolves the caller via
  `InternalAdminAuth.ResolveOperatorName(...)` (returns the operator name or
  `null` = 404), stashes the name in `ctx.Items["InternalOperator"]`, and
  sets `Cache-Control: no-store` on the response. `Evaluate(...)` is kept as
  a thin back-compat delegate (Allow iff `ResolveOperatorName` is non-null).

**JIT sessions + MFA (opt-in hardening).** Standing access from a static token
is the weak spot the security research flags. Opt-in via `Internal:RequireSession`
(default `false` → behavior unchanged):
- `POST /api/internal/session` exchanges the static token (first factor; the
  gate lets it reach ONLY this path when sessions are required) — plus a TOTP
  code (second factor) when the operator has an `Internal:Operators[].TotpSecret`
  — for a short-lived session token (`Internal:SessionTtlMinutes`, default 15,
  clamp 1–240). Wrong/absent code → 401; the endpoint always works so the
  console uses one sign-in flow in both modes.
- When `RequireSession` is on, the gate accepts ONLY a live session token for
  data endpoints (resolved via the process-local `OperatorSessionStore`); the
  static token alone reaches nothing but `/session`. So a leaked static token
  confers no standing data access (just-in-time, time-boxed). `GET /whoami`
  reports the resolved operator; `admin.html` exchanges token+2FA → session and
  uses the session token for every call (only the session token is persisted).
- `Totp` (RFC 6238, HMAC-SHA1, BCL-only — no new dependency) and
  `OperatorSessionStore` live in `Application/Auth`. Process-local store
  (multi-instance would need a shared one — noted). Pinned by `TotpTests`,
  `OperatorSessionStoreTests`, and `InternalControllerTests` (`CreateSession_*`,
  `WhoAmI_*`).
- The `wwwroot/admin.html` page is served openly but is useless without a
  token (all its data calls require it). It is a full read-only operator
  console: Overview (live, auto-refresh 20s, status-colored), Devices (search +
  click-through to that device's conversations), Parents (search), Stories,
  Flagged (prioritized triage — Blocked-first, All/Blocked/Flagged filter,
  click a row to open the conversation in context), Conversations (+ detail),
  Audit, and the Tuning playground. All read-only; operator ACTIONS remain a
  separately-approved future phase (see Out of scope).

**Access audit (#013).** Every cross-family content read — `GET /flagged`,
`GET /conversations`, `GET /conversations/{id}` — writes one
`InternalConsoleAccess` audit row (`AuditEvent.InternalConsoleAccess`)
carrying the resolved operator name, the endpoint, the target id, and the
row count. `ActorParentId` is **null** (system-actor), so these rows are
invisible to every parent-facing audit feed by the existing
`ActorParentId == parentId` query filter — they surface only in the
console's own GLOBAL `GET /audit`. `AuditEventType.InternalConsoleAccess`
is string-converted (no migration). The audit write is best-effort
(wrapped in try/catch + `LogWarning`) so an audit-write hiccup never breaks
a read. Aggregate-count reads (`/overview`, `/devices`, `/parents`,
`/stories`) are NOT audited — they expose no per-child transcript content.

**Read-only by design (Phase 1).** Every endpoint is a GET. An admin
token that could mutate (pause devices, promote drafts, delete) is a
much larger blast radius and is deferred to a later, separately-approved
phase. Endpoints under `/api/internal/`:

- `GET /overview` — system counts + today's activity + total in-process
  OpenAI cost (UTC day) + DB reachability.
- `GET /devices` — ALL devices (safe fields) + nested children +
  linked-parent count + per-device cost-today.
- `GET /parents` — ALL parents (safe fields) + linked-device count +
  audit-event count. Google linkage as a bool.
- `GET /stories` — the runtime library (curated + side-loaded drafts)
  with metadata (segments, bedtimeSafe, reflection text/question counts).
- `GET /flagged?limit=&offset=` — all non-Clean messages, all devices.
- `GET /conversations?deviceId=&limit=&offset=` (+ `/{id}`) — summaries
  (any/all devices) and full detail with messages.
- `GET /audit?limit=&offset=` — GLOBAL feed including system-actor rows
  (`ActorParentId == null`) that parents can never see.
- `GET /whoami` — the resolved console operator identity (accountability; the
  dashboard shows "operator: …" in the header). No data exposure.

Pagination guard mirrors the parent endpoints (`offset < 0` / `limit < 1`
→ 400; `limit` clamped to 100).

**Story-QA tuning playground (Phase 2).** The one non-GET endpoint:

- `POST /api/internal/story-qa-test` body `{ storyId, segmentIndex, question }`
  — runs a typed question through the REAL bounded in-story Q&A pipeline
  (`LibraryStoryQuestionService`: input moderation → GPT → `StoryAnswerFilter`
  / repair-once / canned fallback → output moderation) and returns the answer
  TEXT plus diagnostics (`usedFallback`, `firstRejection`, `retryRejection`,
  `inputSafe`, `outputSafe`, `outcome`, the segment text). **Text only** — no
  TTS, no persistence, no conversation write, no device gates. It **calls
  OpenAI (cost)** — operator-initiated, so no device cost-cap gate; it still
  mutates nothing. Mirrors `StoryQaController.Ask`'s decision logic minus the
  voice/transport concerns, so what you see is what a child would hear for the
  same (story, segment, question). Pinned by `InternalControllerTests`
  (`StoryQaTest_*`).

**Reversible operator actions (Phase 3).** Two mutating, operator-scoped
endpoints (NO parent ownership check — the console is superuser), each
requiring a `reason` and writing one system-actor `InternalConsoleAction`
audit row (`ActorParentId` null so it's console-only, but `TargetDeviceId` IS
set; metadata = operator name + action + new value + reason). Idempotent — a
no-op (flag already at the requested value) changes nothing and writes no row.

- `POST /api/internal/devices/{deviceId}/revoke` body `{ value, reason }` —
  operator kill-switch: `value=true` sets `Device.IsRevoked` (every device-auth
  path then 401s until re-provision); `value=false` restores. The admin analogue
  of the parent #074 revoke, minus the ownership gate.
- `POST /api/internal/devices/{deviceId}/pause` body `{ value, reason }` —
  operator pause/resume (soft; device still authenticates, chat short-circuits).

400 on a missing/blank reason; 404 on unknown device. Pinned by
`InternalControllerTests` (`RevokeDevice_*`, `PauseDeviceAction_*`). The
`admin.html` device drill-down surfaces Revoke/Restore + Pause/Resume buttons
(typed reason + confirm). **Only reversible actions** — destructive ones
(data deletion, story-draft promotion) remain deferred (see Out of scope).

**Secret invariants (do not regress):** the response DTOs in
`Controllers/InternalDtos.cs` never carry `Device.ApiKey` /
`Device.ApiKeyHash` or `Parent.PasswordHash` — excluded by construction.
Google linkage is surfaced as `GoogleLinked: bool`, never the raw
`GoogleSubject`. Pinned by `InternalControllerTests`
(`Parents_NeverExposePasswordHash`, `Devices_NeverExposeApiKeyOrHash`).
The fail-closed gate and per-operator resolution are pinned by
`InternalAdminAuthTests` (`ResolveOperator_*`); the access-audit write is
pinned by `InternalControllerTests`
(`ConversationDetail_WritesAccessAudit_WithOperator_AndNullParentActor`).

**Out of scope (still deferred):** DESTRUCTIVE operator actions (data
deletion / GDPR-erase as admin, story-draft promotion), bedtime/mode-as-admin,
and parent/admin role unification — separate approval each. (Shipped: the
read-only god view, the story-QA tuning playground, the Phase 3 REVERSIBLE
device actions — revoke/restore + pause/resume — and the opt-in JIT
sessions + TOTP MFA above.)

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

## Consumer platform (pairing / presence / device management)

The path that lets a parent buy a toy, pair it from their phone/app, see it
online, and manage it. Backend + firmware are code-complete; the mobile app
(Phase D) is specced but not built. Full design + phased plan live in
`PLATFORM-ARCHITECTURE.txt` (Desktop). Open decisions tracked in
`TODO/REMAINING-TODO.txt`.

**Three distinct secrets — do not conflate:**
- **Device key** (`X-Api-Key`) — the toy's backend credential. NEVER shown to a
  parent or placed in a QR. Factory-burned to NVS (firmware reads it NVS-first,
  `config.h` fallback — see firmware below).
- **Claim code** — single-use, printed on the box / in the QR. Proves physical
  possession; binds the toy to a parent account. NOT a backend credential.
- **Wi-Fi credentials** — travel phone→toy only (BLE provisioning), never to
  the backend.

**Pairing (Phase A.2).** `POST /api/parents/devices/claim`
`{ deviceId, claimCode }` (parent-JWT, `[EnableRateLimiting("auth")]`). On
success links the toy + consumes the code; **one uniform 400** for every
failure (unknown device / already claimed / wrong code) — no existence leak.
`Device.ClaimCodeHash` (PBKDF2 via `DeviceApiKeyHasher`, same as device keys) +
`ClaimedAt`. The mint side is the provisioning-secret-gated
`POST /api/devices/register`, whose `DeviceRegistrationResponse` carries
`DeviceId`, `ApiKey`, `ClaimCode`, and a `QrPayload` (`{ deviceId, claim }`
JSON) for the factory station's QR. Audited `ParentDeviceClaimed`.

**Presence.** `LinkedDeviceDto.IsOnline` is derived (reporting-only, nothing
gated on it): `UtcNow - LastSeenAt < Presence:OnlineThresholdSeconds`
(default 180s, floor 30s). `LastSeenAt` is refreshed by `DeviceAuthMiddleware`
on any device-authed request AND by the dedicated idle path
`POST /api/devices/heartbeat` (device-authed, body-less; the toy POSTs it every
~60s while idle so the online dot reflects an idle-but-powered toy). Same single
computation site as `IsDormant`.

**Device management (parent-JWT, ownership-checked, silent 404 on miss):**
- `PUT /api/parents/devices/{id}/name` `{ name }` — rename (1..60 chars, 400
  otherwise). Audited `ParentDeviceRenamed`.
- `PUT /api/parents/devices/{id}/revoke` `{ revoked }` (#074) — credential
  kill-switch; see Key Design Decisions. Audited `ParentDeviceRevocationChanged`.
- Existing pause / bedtime / mode-flags / unlink unchanged.

**Dashboard.** `parent.html` linked-devices view now surfaces all of the above:
a "＋ Add a toy" claim-code form (NOT the API key), an Online/Offline presence
badge, a per-device rename row, and a confirmed Revoke/Restore control.

**Firmware (ESP32, `esp32/AregVoiceMvp/`).** Onboarding so a parent sets up the
toy with no reflash. All gated/fallback so the bench build is byte-identical.
- **B.1** `wifi_creds.{h,cpp}` — NVS-backed Wi-Fi creds (`config.h` fallback) +
  `voice_wifi_set_credentials()` seam. Verified (compiled+flashed).
- **B.2** `ble_provisioning.{h,cpp}` — BLE Wi-Fi provisioning (Arduino
  `WiFiProv`, `NETWORK_PROV_*`). Gated behind `AREG_USE_BLE_PROVISIONING`;
  needs `PartitionScheme=huge_app` (BLE doesn't fit the 92% default partition).
  Compile-verified (48% of 3 MB); functional test needs a phone at the bench.
- **B.3** auto-fallback to provisioning after a long Wi-Fi outage +
  reboot-on-reprovision; button-hold-at-boot factory-reset gesture.
  Compile-verified.
- **Heartbeat** `voice_send_heartbeat()` — idle `POST /api/devices/heartbeat`
  (toy side of presence). Compile-verified.
- **Device identity** `device_creds.{h,cpp}` (Phase C) — NVS-first device
  id/key (`config.h` fallback), surfaced through one
  `add_device_auth_headers()` helper across every backend call. The factory
  station that burns the NVS is the owner process.

## Device OTA foundation (Proof 2 — backend contract)

The backend contract the firmware OTA foundation targets. **Backend-only in
this slice** — no on-device OTA, no Secure Boot/eFuse, no Feature-1 SD sync.
The device connects OUTBOUND only (polls); there is no inbound server on the
toy.

- **Firmware reporting on heartbeat.** `POST /api/devices/heartbeat` now
  accepts an OPTIONAL JSON body (`DeviceHeartbeatRequest`:
  `firmwareVersion`/`firmwareBuild`/`boardModel`/`partitionName`/`lastOtaStatus`).
  `EmptyBodyBehavior.Allow` keeps the legacy body-less presence heartbeat
  working. Only the non-null fields are stamped onto new `Device` columns
  (`FirmwareBuild`/`BoardModel`/`PartitionName`/`LastOtaStatus`/`FirmwareReportedAt`;
  `FirmwareVersion` already existed) plus `FirmwareReportedAt`. A partial
  report never blanks a previously-reported value.
- **Device command queue** (`DeviceCommand` entity + migration
  `AddDeviceOtaFoundation`). Columns: `Id`, `DeviceId` (FK cascade),
  `Type`, `PayloadJson`, `Status` (string enum
  `Pending/Sent/Acked/Failed/Expired`), `CreatedAt`, `ExpiresAt`, `SentAt`,
  `AckedAt`, `Result`, `Error`, `AckFirmwareVersion`, `AckDiagnosticsJson`.
  Index `(DeviceId, Status)`. The only wire type this slice enqueues is
  `firmware_update` (`DeviceCommandTypes`); an unknown type is rejected at
  enqueue (never delivered).
- **Poll** `GET /api/devices/commands` (device-authed): returns only THIS
  device's deliverable commands (`Pending`/`Sent`, not expired), marks
  `Pending → Sent`, and lazily marks overdue ones `Expired` (never
  delivered). At-least-once: a `Sent` command is re-delivered until acked or
  expired, so the device dedups by `Id`.
- **Ack** `POST /api/devices/commands/{id}/ack` (device-authed): idempotent
  (a terminal command is never re-applied — a duplicate ack is a safe
  no-op), ownership-checked (a command owned by another device returns a
  uniform **404**, no cross-device existence leak). Stores
  `Result`/`Error`/`AckFirmwareVersion`/`AckDiagnosticsJson`.
- **Firmware manifest** `GET /api/devices/firmware-manifest` (device-authed):
  compares the device's reported version/board against the config-driven
  current release (`FirmwareUpdate` section → `FirmwareUpdateOptions`,
  ships `Enabled=false`). Returns `{ updateAvailable: false }` or a manifest
  `{ version, boardModel, minVersion, url, sizeBytes, sha256, signature,
  expiresAt }`. Offer gate: enabled AND device strictly OLDER than
  `LatestVersion` AND (no `BoardModel` configured OR it matches). A null/
  unparseable device version is treated as oldest (offered). `signature` is
  an HMAC-SHA256 over the manifest's load-bearing fields when `SigningKey`
  is set, else an empty placeholder. **This signs the MANIFEST, not the
  image** — image signing (Secure Boot v2) is a separate, later step.
- **Auth.** The three new endpoints are added to `DeviceAuthMiddleware`'s
  device-auth path set, so a **revoked device is rejected (401) before it
  can poll or ack** (`ValidateDeviceAsync` returns null for a revoked
  device). `/api/devices/register` stays provisioning-secret gated.
- Pinned by `DeviceCommandServiceTests`, `FirmwareManifestServiceTests`,
  `DeviceServiceOtaTests`, `DeviceControllerOtaTests`.
- **Bench enqueue** `POST /api/internal/devices/{deviceId}/commands`
  (`{ type, payload?, ttlSeconds? }` → `{ commandId, … }`) — operator-gated
  like every `/api/internal/*` action (fail-closed 404 when unconfigured),
  known `DeviceCommandTypes` only (400 otherwise), 404 on unknown device,
  TTL clamped 60..86400 s (default 3600). Bench/test-only: NOT parent-facing,
  not in admin.html; one loud structured log (operator + type + command id)
  instead of an audit row until it becomes a real console surface. Pinned by
  `InternalControllerEnqueueCommandTests`.

### Firmware skeleton (device half — no OTA apply)

`esp32/AregVoiceMvp/ota_foundation.{h,cpp}` implements the device half of
the contract, SKELETON-scoped: **no firmware download, no flash write, no
Secure Boot, no SD sync**. Outbound-only polling.

- **Identity**: `AREG_FW_VERSION` / `AREG_FW_BUILD` / `AREG_BOARD_MODEL`
  (defaults in `ota_foundation.h`, overridable via `config.h` — documented
  in `config.h.example`); running partition label via
  `esp_ota_get_running_partition()`.
- **Heartbeat report**: `voice_send_heartbeat()` now POSTs the JSON identity
  body (backend body is optional, so legacy body-less builds keep working).
- **Poll loop**: `ota_foundation_tick()` from the `.ino` IDLE branch —
  boot-polls once when Wi-Fi is first up, then re-polls every
  `AREG_HEARTBEAT_INTERVAL_MS`. Never runs during a voice turn.
- **Dedup**: RAM ring of handled command ids; a re-delivered command
  (at-least-once transport) is never re-run, only re-acked (duplicate acks
  are server-side no-ops). Expiry is enforced server-side (the device has
  no synced wall clock; it logs `expiresAt` only).
- **`firmware_update` handling**: fetches `/api/devices/firmware-manifest`,
  logs the offered manifest (version/url/size/sha256/signature), acks
  `ok` + `{"status":"manifest_checked"}` — and explicitly does NOT apply.
  Unknown command types ack `failed`/`unsupported_type`.
- `voice_add_device_auth_headers()` is the shared device-auth seam other
  firmware modules use, so all backend traffic authenticates identically.

### Cloud→SD content sync (backend half — multi-story)

The story-audio counterpart of the firmware-manifest/image pair — the
backend contract the ESP32 SD-download firmware targets. **N configured
MP3 items**, config-driven (`ContentSync` section → `ContentSyncOptions`,
ships `Enabled=false`). Static config for every device; per-device /
per-tier entitlement is still a later slice on the same wire shape.

**Two config shapes, mirroring `Jwt:Keys` / `Jwt:Key`:**
- **Preferred** — `ContentSync:Stories`, an ordered array of
  `{ storyId, version, title, audioUrl, audioPath, sha256, sizeBytes }`.
  The manifest returns them in configured order.
- **Legacy** — the flat scalars (`ContentSync:StoryId`, `:Sha256`, …)
  describing ONE item, still honored so overlays written before
  multi-story keep working untouched.
- `Stories` wins when non-empty. `ContentSyncOptions.ResolveStories()` is
  the single place that decides, so the manifest service and the
  content-file endpoint can never disagree. Binding lives in the pure
  helper `ContentSyncOptions.Resolve(IConfiguration)` (same pattern as
  `RetentionPolicy.ResolveMessages`) rather than inline in DI, so the
  array binding is reachable by tests — a silent binding bug would
  otherwise leave every unit test green while devices got empty manifests.

- `GET /api/devices/content-manifest` (device-authed): `{ stories: [...] }`
  with N items `{ storyId, version, title, audioUrl, sha256
  (lowercased 64-hex), sizeBytes, enabled }`. `enabled:false` is on the
  wire from day one for future retirement (still hardcoded `true` — the
  retirement slice owns that knob).
  **Validation is PER ITEM**: a story missing its id, with a non-positive
  size, or with a sha that is not exactly 64 hex chars is dropped and the
  remaining stories are still served. (Before multi-story a single bad
  field emptied the whole manifest; with one configured story the observable
  behavior is unchanged, which is why the older fail-closed tests still
  pass.) Duplicate `storyId` keeps the FIRST and drops the rest — the id is
  the content-file lookup key, so a duplicate would make it ambiguous.
  The master switch still short-circuits everything: `Enabled=false` ⇒ empty.
- `GET /api/devices/content-file?storyId=<id>` (device-authed): streams
  that story's `AudioPath` — NOT wwwroot; same fail-closed 404 matrix as
  firmware-image (disabled / unset / relative / missing); `audio/mpeg`
  with Range processing (resume-ready). **`storyId` is only ever a lookup
  key against configured items — it never reaches the filesystem**, so it
  carries no traversal risk (pinned by traversal-shaped-input tests).
  **Omitting `storyId` resolves to the only configured story**, and 404s
  when more than one is configured rather than guessing. That is what keeps
  already-flashed firmware working: a legacy single-item config still
  advertises the bare `/api/devices/content-file` with no query string,
  and an item that configures no explicit `audioUrl` gets
  `/api/devices/content-file?storyId=<id>` filled in.
- Both paths are in `DeviceAuthMiddleware`'s device-auth list — unauth
  callers 401 before any controller runs (pinned by a middleware test),
  and revoked devices are rejected by `ValidateDeviceAsync`.
- Integrity = manifest sha256/sizeBytes, verified by the device while
  streaming to SD. Manifest HMAC signing (as firmware-manifest has) is a
  deliberate follow-up when multi-story/tiers land.
- PC/API bench verified 2026-07-05 (single-story config): manifest +
  authed download round-trip (sha256 `d3a6fbdb…` / 4,654,560 B matched),
  401 without headers. **The multi-story path has NOT been bench-verified
  against real hardware** — it is covered by tests only. The firmware still
  reads `stories[0]` and writes a single-object `/content_index.json`, so
  a 3-story manifest changes nothing on the device until the
  `content-sync-multi-item` firmware slice lands.
- Pinned by `ContentManifestServiceTests` (per-item validity, ordering,
  dedupe, legacy back-compat), `ContentSyncOptionsResolveTests` (config
  binding, both shapes), and `DeviceControllerContentSyncTests`
  (per-story addressing, traversal-shaped ids, no-storyId fallback).

### Cloud→SD content sync (firmware half — bench-verified on real hardware)

The ESP32 counterpart to the backend half above. Two bench-only firmware
modules, each gated behind its own build flag so **production builds compile
ZERO bytes of either** and stay byte-identical:

- `content_sync.{h,cpp}` (`-DAREG_CONTENT_SYNC_BENCH`) — one sync attempt per
  boot from the IDLE loop once Wi-Fi + SD are both up. **Multi-story as of
  the `content-sync-multi-item` slice**: every valid item in
  `GET /api/devices/content-manifest` is synced, up to `CS_MAX_STORIES` (8),
  each to `/stories/<storyId>-v<version>.mp3` via its own
  `/tmp/<storyId>-v<version>.mp3.part`, each independently SHA-256-verified
  **before** the atomic rename. `/content_index.json` is written LAST, once,
  in schema **v2**. Any per-story failure deletes only that `.part` and never
  touches a previously-good final file. See the firmware README
  (§ "Cloud→SD content sync — multi-story") for the full contract.
  - Decision logic is split into `content_sync_rules.h` (pure — id/sha/size
    validation, path construction, bounds; **no** Arduino/SD/HTTP deps) and
    `content_sync_model.{h,cpp}` (JSON ↔ `CsStory`, manifest parse + index
    parse/build/migrate, no IO), so both are testable without hardware.
  - **Story ids are allowlisted** to `a-z0-9-_` (lowercase only, ≤48). `..`,
    `/`, `\`, `:`, spaces and control characters are unrepresentable, not
    merely filtered — the id reaches an SD filename. Lowercase-only mirrors
    the backend's case-insensitive dedupe so one backend story can never
    become two files. Duplicates keep the first.
  - **Per-item fail-closed**: one bad item never denies the device its valid
    siblings; a manifest longer than the max is truncated with a log line.
  - **Index v2 carries a legacy compatibility mirror** (flat
    `storyId`/`version`/`sha256`/`file`/`sizeBytes`) pointing at
    `AREG_STORY_ID` when present, else the first entry. As of
    story-select-from-index, ACTIVE PLAYBACK no longer reads the flat
    fields; the mirror is RETAINED only for two bench harnesses —
    `resolve_path()` in `sd_playback.cpp` and the Test-E harness. See
    "Story selection from the index" below.
  - **A v1 (flat) index is migrated in memory, never erased**; a card never
    has to be wiped. `verified` is inferred true (v1 only wrote its index
    after a full SHA-256) and existence + size are re-checked anyway.
  - **Already-current**: index match (id/version/sha/size + `verified`) AND
    file exists at the recorded size ⇒ skip. Without a usable index entry the
    file is re-hashed, which is what the single-story build did every boot;
    restricting that to the no-entry case avoids tens of seconds of SPI reads
    per boot at 8 stories.
  - **Non-destructive**: an empty manifest leaves the index and every cached
    file untouched; a story absent from the manifest but still verified on
    the card is carried forward; `enabled:false` skips without deleting.
    Retirement deletion, orphan sweeping, eviction and download resume remain
    deferred. **Playback still does not select among index entries.**
- `content_sync_test.{h,cpp}` (`-DAREG_CONTENT_SYNC_TEST_BENCH`) — on-device
  assertions over the rules + model layers (no SD, no Wi-Fi, no backend;
  prints `[cs-test] RESULT PASS/FAIL`). This repo has **no host C/C++
  toolchain** — only the xtensa cross-compilers from the Arduino ESP32 core —
  so a host-run unit test is not buildable here; this follows the existing
  bench-harness pattern (`AREG_STORY_SD_FALLBACK_TEST_BENCH`). It does NOT
  cover real SD atomicity, real/failed HTTP downloads, the file-exists+size
  half of already-current, or reboot-after-partial-sync.
- `sd_diag.{h,cpp}` (`-DAREG_SD_DIAG_BENCH`) — standalone SD isolator used to
  diagnose the mount failure below. First run 20 s after boot, then **re-runs
  every 30 s until one attempt mounts** (then prints `PASS` and stops), so a
  monitor attached late still catches the next attempt. Tests
  `audio_sd_begin()` then raw `SD.begin` at 400 kHz/1/4/10 MHz, then
  read/write/verify/delete.

**HARDWARE NOTE — the SD module must be powered from ESP 5V, not 3V3.** The
bench SD module (WWZMDiB blue microSD board: onboard AMS1117 regulator +
level shifter) browns out on 3.3 V — `SD.begin` fails at *every* SPI speed,
which masquerades as a wiring/card fault. Moving its VCC to the board's 5V
rail fixed it. Note the bench dev board (a USB-C ESP32-S3-DevKitC-1 clone)
does **not** expose USB 5 V on any header pin (`5VIN` / J1-21 is input-only,
reads ~0.14 V), so the module VCC was fed from an external 5 V source with a
**shared ground** to the ESP. Final SD wiring:

| SD pin | ESP32-S3 |
|--------|----------|
| VCC    | **5V** (external 5 V, shared GND — never 3V3) |
| GND    | GND |
| CS     | GPIO10 |
| SCK    | GPIO12 |
| DI (MOSI) | GPIO11 |
| DO (MISO) | GPIO13 |

**BUILD NOTE — always build with `FlashSize=8M,PartitionScheme=custom`.**
The sketch ships a custom 8 MB dual-OTA `partitions.csv` with **3 MB OTA
app slots**. The correct FQBN is:

```
esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc
```

**Do NOT use `PartitionScheme=default`.** It measures firmware against the
default 1.25 MB (0x140000) app slot, which is the sole cause of the false
"96–97% of program storage" flash alarm. Built correctly, the production
image is ~1,264,539 B ≈ **40%** of the real **3 MB** slot (~1.88 MB free per
slot). No partition redesign is needed before SD MP3 playback. Full
compile/upload commands (production + both bench flags) are in
`esp32/AregVoiceMvp/README.md` → "arduino-cli — correct FQBN".

**Bench evidence (real ESP32-S3 hardware, 2026-07-11):**
- SD diag on 5 V: `audio_sd_begin ok`, card `SDHC/SDXC` 7680 MB, root list +
  `readwrite PASS` → `[sd-diag] PASS`.
- Content-sync first run: manifest `status=200 stories=1`, item
  `anban-huri v1 "Anban Huri" 4654560 bytes`, download 10→100 %,
  `sha256 ok` (`d3a6fbdb…b103b85`), moved
  `/tmp/anban-huri.mp3.part → /stories/anban-huri-v1.mp3`, `index written`,
  `[content-sync] PASS`, heartbeat `status=200`.
- Second boot idempotence: `already cached PASS` (no re-download).
- Matches the backend-half manifest (`sha256 d3a6fbdb…`, 4,654,560 B).

### Cached-MP3 SD playback (firmware bench — hardware-verified)

Closes the Cloud→SD→speaker loop: plays a story MP3 **already cached on the
SD card** (by the content-sync slice) out the MAX98357A speaker. Gated
behind `-DAREG_SD_PLAYBACK_BENCH`; production compiles **zero bytes** and
is byte-identical (verified: flag-off image = 1,264,539 B, unchanged).

- `sd_playback.{h,cpp}` — one shot per boot, 30 s after boot (armed
  heartbeat until then). Ensures SD via `audio_sd_begin()`/
  `audio_sd_available()`, resolves the file from `/content_index.json`
  (`"file"` field; falls back to `/stories/anban-huri-v1.mp3` with a logged
  reason), verifies existence + size, then plays.
- **Reuses the existing decoder verbatim — NO second decoder.** It calls
  `audio_speaker_begin()` then `audio_play_story_file(path, 0, nullptr,
  nullptr)` (`audio_io.cpp`), the same `AudioFileSourceSD` →
  `AudioGeneratorMP3` → `AudioOutputI2S` path the offline-story flow uses,
  on amp pins `AREG_PIN_AMP_BCK=15 / LRC=16 / DATA=7`. `barge_in=nullptr`
  ⇒ plays to natural end; the decoder feeds the task watchdog per frame.
  Success is `!interrupted` plus an elapsed-time plausibility check (a real
  ~4.6 MB story runs minutes; a decode/open bail returns instantly).
- The MP3 is opened read-only — never modified or deleted. No backend
  download, no content-sync, no recording/chat turn in this bench.
- **Bench evidence (real ESP32-S3 hardware, 2026-07-12):** operator heard
  the cached Anban Huri story from the speaker; serial showed `[story] SD
  end interrupted=false` then `[sd-playback] done ok=true (232745ms)` — a
  clean ~3.9-minute play from SD with no backend call, file left cached.
  Confirms the cached MP3 opens from SD, the existing MP3 decoder path
  works, and the I2S/MAX98357A speaker path works end-to-end.

### Story selection from the index (`story_select.{h,cpp}`)

**Supersedes the `AREG_STORY_SD_CACHE_FIRST` slice below.** The toy now
CHOOSES which cached story to play instead of always playing the
compile-time `AREG_STORY_ID`. Compiled into every build — normal playback,
not a bench path. The flag is **removed**; compatibility comes from the
fallback chain instead (a card with no v2 index yields zero eligible
stories and behaves exactly like the old flag-off build). This supersedes
the `docs/v2-backlog.md` entry "Promote `AREG_STORY_SD_CACHE_FIRST` to
default" — that line is now stale and only the owner may edit that file.

- **Deterministic round-robin, no-repeat by construction.** Pure
  `story_select_next()`: 0 eligible → none; 1 → that story (a one-story
  card must keep working, so no-repeat cannot apply); unknown/empty
  previous → first; else the entry AFTER the previous, wrapping. Three
  stories rotate `A→B→C→A`. With ≥2 eligible the result is never the
  previous one. Random selection was rejected: unreproducible on the
  bench, can repeat by chance, and leans on boot-time RNG the device
  lacks. Previous-id matching is case-insensitive, as the index is.
- **Eligible** = valid id AND `verified` AND `version>=1` AND positive
  `sizeBytes` AND a safe bounded `cachePath` (absolute, under `/stories/`,
  no `..`, no `\`) AND the file present AND its **actual size equal to**
  `sizeBytes`. Metadata alone never suffices — the file can vanish
  independently of the index. Duplicates keep the first; index order is
  preserved. The pure half is `story_entry_eligible(entry, actual_size)`
  so it is testable without a card.
- **Session stability (do not regress).** The selected id lives in
  `s_current_story_id` for the whole session. The **new-story boundary is
  `handle_story_session()` entered with `s_story_offset == 0`**; a resume
  (offset > 0) re-resolves the SAME story and never re-selects. Pause,
  resume, a Q&A barge-in and a stream-token retry therefore cannot land on
  a different story. Natural end sets the offset to 0, so the next press
  advances the rotation.
- **Last-played persistence**: NVS namespace `aregstory`, key `last_id`
  (Arduino `Preferences`, same idiom as `wifi_creds`/`device_creds`/
  `ota_state`). Id only — no secrets, no index. Written **only on change**
  so pause/resume does not burn flash. An invalid stored id is ignored,
  not trusted; a persistence failure is logged and swallowed and can never
  block playback.
- **The cursor advances ONLY after playback genuinely started** (do not
  regress to advancing at resolve time). `audio_play_story_file()` reports
  this via its new optional `out_started` flag, set once `mp3.begin()`
  succeeded AND the first `mp3.loop()` completed — decoder initialized,
  first frame handed to I2S. Every earlier bail-out (SD not mounted, open
  failed, #064 not-an-MP3 precheck, `mp3.begin()` failure) leaves it false
  and makes no sound. A story that resolved but was never heard must not
  become `last_id`, or the next press skips a story the child never got.
  The bookkeeping runs at most once per session (`selection_settled`), so
  pause, resume, a Q&A barge-in and the token retry never move the cursor.
- **Failed-start exclusion is boot-scoped RAM, never persisted.** A story
  that resolved but did not start is skipped by the next NEW-story
  selection (so a corrupt-but-right-sized file cannot trap the rotation),
  and the whole set is cleared once another story genuinely starts. A
  reboot retries it — safer than skipping forever on one bad start. The
  exclusion is **best-effort**: if it would leave nothing to play it is
  ignored, so a one-story card still retries rather than going silent (no
  loop — each press is one attempt). With exactly two stories and one
  broken, the good one replays back-to-back; availability beats strict
  no-repeat when the library is effectively one playable story.
  Worked example: `A played -> last_id=A; pick B; B fails -> last_id stays
  A, B excluded; next pick = C; C starts -> last_id=C, exclusions cleared`.
- **Story-aware resolver**: `story_select_resolve_path(story_id, out, len)`
  replaces `story_resolve_cache_path(out, len)`. Resolves ONLY the
  requested id; returns false — never a different story's path — on
  invalid/absent/unverified/unsafe/missing/size-mismatch. `AREG_STORY_ID`
  is no longer consulted in resolution.
- **Fallback order**: selected index story → content-pack narration →
  Wi-Fi stream. A selected story that fails to resolve falls through
  rather than silently playing a different cached story.
- **In-story Q&A follows the selection**: `voice_set_active_story_id()`
  grounds `/api/chat/story-qa` and the reflection endpoint in the story
  actually playing, so a question during story B is not answered about
  story A. Previously those URLs hardcoded `AREG_STORY_ID`.
- **Legacy index mirror RETAINED** (not removed): active playback no
  longer reads the flat root fields, but two readers still do —
  `sd_playback.cpp:41` (`AREG_SD_PLAYBACK_BENCH`) and Test-E in
  `AREG_STORY_SD_FALLBACK_TEST_BENCH`. Both are hardware-verification
  tools; removing the mirror for tidiness would break them.
- `story_select_test.{h,cpp}` (`-DAREG_STORY_SELECT_TEST_BENCH`) — on-device
  assertions over the PURE halves only (rotation, no-repeat, wrap,
  unknown-previous, bounds, eligibility, path safety). No SD, no NVS, no
  Wi-Fi. Does NOT cover real NVS durability, real SD index reads,
  `story_select_load_eligible`/`_resolve_path` end to end, or pause/resume
  in the live state machine.
- **SHIP A6 is NOT done.** Still required on hardware: three approved MP3
  stories (today 2 approved, only `anban-huri` SD-wired and itself still a
  `draft` pending its listen test), a real three-item sync, selection
  observed across repeated new-story requests, no back-to-back repeats,
  reboot persistence, and pause/resume staying on one story.

### SD-first story playback from the content-sync cache (SUPERSEDED)

> **Superseded by "Story selection from the index" above.**
> `AREG_STORY_SD_CACHE_FIRST` and `story_resolve_cache_path()` no longer
> exist; selection is always compiled and picks among N stories. The
> hardware evidence below (Test A, 2026-07-12) remains valid as proof that
> the cached-MP3 SD path plays in the real session — it is the mechanism
> selection now feeds.

Wires the content-sync cache into the **real** story flow: when a story MP3
is cached on SD, `handle_story_session()` plays it from the card (offline, no
Wi-Fi, no token) instead of streaming from the backend. Gated behind
`-DAREG_STORY_SD_CACHE_FIRST`; production compiles **byte-identical**
(flag-off image = 1,264,539 B, unchanged — every new line is `#ifdef`-guarded).

- **One production file** touched (`AregVoiceMvp.ino`) — no `audio_io.*`,
  `content_sync.*`, `sd_playback.*`, OTA, backend, or partition change.
- **Resolver** `story_resolve_cache_path()` (above `handle_story_session`):
  reads `/content_index.json`, returns the cached `file` path **only if** it
  starts with `/`, `storyId == AREG_STORY_ID` (single-story safety), and the
  file exists on SD (`audio_sd_has_file`); else logs a reason and returns
  false. Hardened over `sd_playback.cpp`'s bench resolver (which has no
  storyId/existence guard and a hard fallback).
- **Priority: content-sync cache → content-pack narration
  (`AREG_SD_STORY_NARRATION`) → Wi-Fi stream.** The decision computes
  `sd_narration_path` once; `use_sd = audio_sd_has_file(sd_narration_path)`;
  the resolved path feeds the existing `audio_play_story_file(...)` call.
  Barge-in/resume (`s_story_offset`), token logic, and `handle_post_story_flow`
  (self-gates on pack clips → no-ops on a cache hit) are unchanged.
- **When the flag is OFF** the block reduces to the original
  `audio_sd_has_file(AREG_SD_STORY_NARRATION)` + log line — no behavior or
  code change (verified byte-identical build).
- **Bench evidence — Test A, real ESP32-S3 hardware (2026-07-12):** button
  press ran the live story flow; serial showed `[story] cache index
  file=/stories/anban-huri-v1.mp3 storyId=anban-huri` → `[story] source = SD
  (cache)` → `[story] SD open: /stories/anban-huri-v1.mp3 @ 0` → `[story] SD
  end interrupted=false` → `[story] finished`, and the operator heard the
  story from the speaker. Confirms the real session reads `/content_index.json`,
  selects the cached MP3, prefers the cache over the Wi-Fi stream, reuses the
  `audio_play_story_file` decoder path, and returns cleanly to idle.
- **Fallback paths are now hardware-verified too** — see the fallback test
  harness below (Tests B / E / C, 2026-07-26).

#### SD-first fallback test harness (bench-only)

Automates the three fallback paths Test A did not cover, on real hardware.
Gated behind `-DAREG_STORY_SD_FALLBACK_TEST_BENCH` alone (its former
`#error` requiring `AREG_STORY_SD_CACHE_FIRST` is gone — the selection path
it exercises is now always compiled). Production compiles **zero bytes** of
it — every added line, including the `loop()` hook, is inside the `#ifdef`.

- **One file** (`AregVoiceMvp.ino`, +188 lines). No `audio_io.*`,
  `content_sync.*`, `sd_playback.*`, OTA, backend, or partition change.
- **One shot per boot**, 30 s after boot from the IDLE branch (armed status
  line every 5 s until then), so a monitor attached late still sees it.
- **Exercises the REAL selector**, not a copy: `fbtest_log_source()`
  replicates the production source decision from `handle_story_session()` and
  calls `story_select_load_eligible()` / `story_select_pick()` /
  `story_select_resolve_path()` / `audio_sd_has_file()` directly, so a PASS is
  evidence about the shipping code path. It deliberately does NOT persist the
  rotation cursor — a diagnostic must not move the child's place.
- **Manipulates then restores SD state**: renames `/content_index.json` (Test
  B), writes a temp index with a wrong `storyId` (Test E), moves both the
  index and the pack narration aside (Test C). Every path restores, and a
  final `restore check:` line asserts no leftovers.
- **Test C plays for real but is time-boxed** — it calls the existing
  `audio_play_story_stream()` and cuts at ~8 s through the barge-in seam, so
  the backend GET and real decode are proven without a 4-minute stream.

**Bench evidence — real ESP32-S3 hardware (2026-07-26):**

```
[fallback-test] Test B resolved source = Wi-Fi stream
[fallback-test] Test B PASS (SD-cache NOT selected)

[story] cache storyId mismatch (idx=wrong-story-id cfg=anban-huri) — trying pack
[fallback-test] Test E resolved source = Wi-Fi stream
[fallback-test] Test E PASS (mismatch rejected, SD-cache NOT selected)

[fallback-test] Test C resolved source = Wi-Fi stream
[story] stream open: http://192.168.1.11:5000/api/story-audio/anban-huri
[story] stream end interrupted=true
[fallback-test] Test C stream: open_failed=0 interrupted=1 resume=120064
[fallback-test] Test C PASS (Wi-Fi stream opened + played)

[fallback-test] restore check: index=1 mp3=1 leftover_bak=0 leftover_orig=0
```

Test B proves a missing index falls back rather than guessing; Test E proves
the `storyId` guard rejects a cached MP3 belonging to a different story
(the failure mode `sd_playback.cpp`'s bench resolver would NOT catch); Test C
proves the Wi-Fi stream still opens, decodes, and yields a resume offset when
no SD source exists.

**Compile check (2026-07-26), canonical FQBN
`esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc`:**

| Build | Size | Note |
|---|---|---|
| flag-OFF (production) | **1,264,539 B** | byte-identical to the documented baseline |
| `-DAREG_STORY_SD_CACHE_FIRST -DAREG_STORY_SD_FALLBACK_TEST_BENCH` | 1,272,999 B | +8,460 B, zero warnings |

> **Both figures above are historical (2026-07-26).** `AREG_STORY_SD_CACHE_FIRST`
> no longer exists — story-select-from-index promoted index-backed selection to
> the default and deleted the flag. Current sizes are in that section.

**Backend bench posture for Test C — set up and reverted.** Test C needs the
backend to actually serve `/api/story-audio/anban-huri`, which the deployed
`C:\AregDeploy` binary (2026-05-19) cannot: it predates the story-audio
feature (2026-06-14, commit `b8e1119`) and contains no `StoryAudioController`,
so the route 404s in routing with an EMPTY body — distinguishable from the
controller's own 26-byte `{"error":"Unknown story."}` 404. A temporary
current-source backend served :5000 for the test against an isolated copy of
the prod DB, then was torn down. Post-test revert verified:

```
health      HTTP=200 bytes=61
story-audio HTTP=404 bytes=0     <- original posture restored
```

No backend source, deployed binary, prod DB, or service config was changed by
this slice. Two operational traps worth remembering: `appsettings.json` pins
`"Urls": "http://0.0.0.0:5000"`, which **outranks** the `ASPNETCORE_URLS` env
var (use `--urls` to override), and the PC's LAN IP is DHCP — the firmware's
hardcoded `192.168.1.11` in `config.h` breaks silently whenever it moves.

### Real OTA apply (Proof 3 slice)

The `firmware_update` handler now REALLY applies (the skeleton's
manifest-check-only behavior is the `updateAvailable=false` path). Still
NOT in scope: Secure Boot/eFuse, SD story sync, staged rollout, production
TLS (Stage A runs over the HTTP LAN bench; `ota_http_begin()` in
`ota_apply.cpp` is the single transport seam where Stage B swaps in
`WiFiClientSecure` + pinned CA).

- **Backend image hosting**: `GET /api/devices/firmware-image` (device-authed
  via the middleware path list, so revoked devices 401) streams the file at
  `FirmwareUpdate:ImagePath` — deliberately NOT a public wwwroot file.
  Fail-closed 404 when disabled / unset / relative path / missing file.
  Range processing on (resume-ready). Pinned by `DeviceControllerOtaTests`
  (`GetFirmwareImage_*`).
- **Manifest signature canonical contract**: the HMAC signs
  `version\nurl\nsha256\nsizeBytes\nexpiresAtWire` where `expiresAtWire` is
  the JSON WIRE FORM of expiresAt (System.Text.Json rendering, NOT "O"
  format — fractional-second trailing zeros differ). The device rebuilds the
  canonical string from the raw JSON text it received. Pinned by
  `FirmwareManifestServiceTests.Signature_VerifiesAgainstJsonWireForm` —
  do not change either side without the other.
- **Firmware apply pipeline** (`ota_apply.{h,cpp}`): fetch manifest at
  execution time → gates in order: HMAC signature (`AREG_MANIFEST_HMAC_KEY`;
  empty key = skip with loud warning, Stage-A bench only) → boardModel →
  minVersion → strict upgrade only (`no_downgrade`; explicit allowDowngrade
  is a later addition) → sizeBytes bounds → persist NVS `downloading` →
  stream download with incremental SHA-256 + `Update` into the INACTIVE slot
  (watchdog fed per chunk) → sha256 verify (constant-time) BEFORE finalize →
  `Update.end()` (native image validation + boot-partition switch) → persist
  NVS `rebooting` → `ESP.restart()`. Device-side expiresAt is skipped
  (no RTC) and logged; expiry is server-enforced.
- **NO ACK BEFORE REBOOT** (invariant): the command stays `Sent` through the
  reboot. The NEW image acks the original command id from NVS after boot,
  and ONLY a 2xx ack triggers `esp_ota_mark_app_valid_cancel_rollback()` —
  the backend check-in IS the health gate. If the check-in can't succeed
  within `AREG_OTA_CHECKIN_DEADLINE_MS` (default 5 min), the image
  self-invalidates and the bootloader rolls back (native pending-verify:
  `CONFIG_BOOTLOADER_APP_ROLLBACK_ENABLE=y` verified in core 3.3.8).
  After rollback, the OLD image detects the version mismatch, persists
  `rolled_back`, and acks `failed/rollback_no_checkin`.
- **Persistent idempotency** (`ota_state.{h,cpp}`, NVS namespace `aregota`):
  state/cmd_id/pending_ver/last_error/applied_cmd/boot_attempts. RAM dedup
  is insufficient because the apply REBOOTS (wiping RAM) and a failed/rolled-
  back attempt would otherwise re-apply on every re-delivery — an infinite
  download/reboot/rollback loop. A command id matching `applied_cmd` is only
  RE-ACKED (stored outcome), never re-run. Crash mid-download normalizes to
  `failed/interrupted` at boot. Heartbeat `lastOtaStatus` reports
  `ota_state_status_cstr()` (e.g. `confirmed`, `failed:sha256_mismatch`).
- While an OTA outcome is pending (`rebooting`), command polling is paused —
  the check-in owns the tick until confirm or rollback.
- **Bench-verified on real hardware** (2026-07-03..05): happy path
  1.0.0→1.0.1 (incl. observed `img_state=pending_verify`), bad-sha256
  refusal (full download, no reboot, no brick), wrong-board server-side
  gating. Full evidence + serial/DB captures:
  `backend/docs/ota-bench-evidence.md`. Poison/dead-backend rollback test,
  corrupted-image test, and Stage-B TLS are deliberately NOT yet run.
- **Attempt-vs-health split (caveat RESOLVED at the API layer)**:
  `Device.LastOtaStatus` stays the verbatim device-reported LAST-ATTEMPT
  outcome (sticky by design — the device's NVS re-reports it every
  heartbeat, so a server-side clear would be overwritten within ~60 s;
  that's why the "clear stale status" option was rejected). Current health
  is DERIVED at read time by `DeviceOtaHealth.Resolve(lastOtaStatus,
  lastSeenAt, nowUtc)` → `ok` / `updating` (downloading/rebooting) /
  `offline` (same 180 s presence window as `LinkedDeviceDto.IsOnline`).
  `AdminDeviceDto` carries BOTH (`lastOtaStatus` + `otaHealth`) so the
  operator console shows the failed attempt as a diagnostic without
  painting a healthy, checking-in device broken. Firmware unchanged;
  `DeviceCommands` audit history unchanged. Pinned by
  `DeviceOtaHealthTests` (keystone: `failed:sha256_mismatch` + fresh
  heartbeat → `ok`) and the `Devices_*OtaHealth*` endpoint tests.

## Key Design Decisions

- Devices auth via `X-Device-Id`/`X-Api-Key` headers. Parents use JWT.
  `DeviceAuthMiddleware` refreshes `Device.LastSeenAt` **awaited** (not
  fire-and-forget — the old un-awaited call raced the request-scoped
  `DbContext`) and **throttled** to once per 60s per device, best-effort
  (a failed write is logged, never breaks the request). See #034.
  **Server-side revocation (#074):** `Device.IsRevoked` is a credential
  kill-switch — `DeviceService.ValidateDeviceAsync` rejects a revoked device
  with the uniform null (→ 401) *before* the key compare, so a leaked/
  compromised key dies across every device-auth path without re-flashing
  (device stays dead until it re-provisions a fresh key). Reversible via
  `PUT /api/parents/devices/{deviceId}/revoke` (parent-JWT, ownership-checked,
  audited `ParentDeviceRevocationChanged`). Distinct from pause (soft, still
  authenticates) and unlink (removes link + cascades data). Per-device keys at
  manufacture remain the owner/provisioning half (#043).
- `ChildService.BuildChildContext()` appends name/gender/age to system prompt. Gender matters for Armenian grammar.
- Conversations auto-expire after 30 min inactivity. Last 20 messages as context.
- Story choice labels handed off across requests via in-memory `ConcurrentDictionary` with 30-min expiry.
- `previous_story_choice: option_a|option_b|unclear` injected into prompt only during active story flow.
- Choice normalization happens only after input moderation passes.
- Story memory (character/place/mood) extracted from AI responses and re-injected into system prompt for continuity.
- OpenAI chat calls have a 30-second timeout via CancellationToken.

## Autonomous Workflow

Claude CLI operates on this project using a multi-agent pipeline. The agents and skills are defined in `.claude/agents/` and `.claude/skills/`.

**Before every task:**
1. Classify: workstream (story-core / safety / parent-surface / tests / hardening / tooling), mode (review-only / minimal-code-change / test-only / no-change-needed), risk (low / medium / high).
2. HIGH risk (ChatService, system prompt, domain entities, safety, auth) → produce plan, stop for approval.
3. MEDIUM risk (new endpoint, helper, DTO) → produce plan, pause for approval.
4. LOW risk (test, doc, UI polish) → brief plan, proceed.

**Available agents** (`.claude/agents/`):
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

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
dotnet test                                     # Run all tests (1653 tests)
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

## Rate limiting

Two named ASP.NET rate-limit policies, both fixed-window, both
served by the same `OnRejected` handler in `Program.cs` (shared
`aat_rate_limit_rejected_total` counter and `{ error: "Too many
requests. Please slow down." }` 429 body).

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
- The `wwwroot/admin.html` page is served openly (an empty shell); it is
  useless without a token, which all its data calls require.

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

**Out of scope (deferred):** operator ACTIONS (pause/bedtime/mode as
admin, draft promote, delete) and parent/admin role unification —
separate approval. (The read-only god view and the story-QA tuning
playground above are shipped.)

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
  `DeviceAuthMiddleware` refreshes `Device.LastSeenAt` **awaited** (not
  fire-and-forget — the old un-awaited call raced the request-scoped
  `DbContext`) and **throttled** to once per 60s per device, best-effort
  (a failed write is logged, never breaks the request). See #034.
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

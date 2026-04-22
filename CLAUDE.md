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
dotnet test                                     # Run all tests (954 tests)
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

**Manual QA checklist**
1. `dotnet run --project src/ArmenianAiToy.Api` → open `http://localhost:5000/` → click "Open the Parent Dashboard →".
2. Log in → devices list loads (or "No devices linked to this account yet." if none).
3. Click a device → Conversations tab active, summaries load. Click Flagged tab → flagged list loads (or "No safety-flagged messages on this device. ✓").
4. Click a row → detail view opens with messages; Blocked (red) and Flagged (amber) borders distinct. ← Back returns to the originating tab.
5. Pagination: ← Newer disabled on page 1, Older → disabled on last page, "Page N" label visible.
6. Bad inputs: `?offset=-1` → 400; `?limit=0` → 400; `?limit=500` → 200 with at most 100 rows.
7. Log out → returns to login view, token cleared from sessionStorage.
8. **Your activity**: in the linked-devices view, click "View your activity →". Empty-state copy appears for a freshly-registered parent. After triggering a parental action (pause/resume, bedtime window, mode flags toggle, child delete, etc.), refresh: a row appears with the friendly label (e.g. *Device paused/resumed*), timestamp, resolved device/child name where applicable, and one-line metadata summary. Pagination (← Newer / Older →) disables correctly on first page / short final page. ← Devices returns to the linked-devices view.

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
  `RegisteredAt`, `TermsAcceptedAt`, `TermsVersion`. **Never**
  `PasswordHash`.
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

**Manual QA**:
1. `dotnet run --project src/ArmenianAiToy.Api`
2. `POST /api/parents/login` to get a JWT.
3. `curl -H "Authorization: Bearer <jwt>" -OJ http://localhost:5000/api/parents/export`
   → writes a `areg-export-<utcts>.json` file. Open it and confirm
   top-level `schemaVersion`, `generatedAt`, `parent`, `devices`,
   `auditEvents`, `excludedFields`.
4. Re-run the curl immediately → 429 with `Retry-After` header.
5. Wait past the cooldown → success again.

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

**Manual QA**:
1. `dotnet run --project src/ArmenianAiToy.Api`
2. Structured logs should show a single "RetentionPurgeService tick:
   nothing eligible" (or "purged N conversations…") line within the
   configured interval. With the shipped 60-minute default the first
   tick is slow to observe; override `Retention:Messages:RunIntervalMinutes`
   via an environment variable (e.g. `RETENTION__MESSAGES__RUNINTERVALMINUTES=15`)
   for a smoke run — do not commit that override, and do not override
   `MaxAgeDays`.
3. `curl http://localhost:5000/metrics | grep aat_audit_events_written_total`
   → on a tick-with-deletions, the counter increments with tag
   `event_type="ConversationsPurgedByRetention"`.

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
- The `auth` policy keys on `Connection.RemoteIpAddress` only.
  **`X-Forwarded-For` is attacker-controlled today** — the repo does
  not configure `ForwardedHeaders` middleware. Proxy-aware keying
  is a deploy-slice concern: enabling it requires the operator to
  wire `ForwardedHeaders` with an explicit trusted-proxy list
  before the limiter's key is changed. A regression test
  (`AuthRateLimiterTests.PolicyFactory_IgnoresXForwardedForHeader_InThisSlice`)
  pins this contract.
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

**Manual QA**:
1. `dotnet run --project src/ArmenianAiToy.Api` (or set
   `Metrics:ScrapeToken` via user-secrets / env-var first).
2. `curl http://localhost:5000/api/health` → expect `{"status":"ok",…}`.
3. `curl -I http://localhost:5000/metrics` with no `Authorization`
   header → expect **404** (fail-closed default).
4. `curl -H "Authorization: Bearer <your-token>" http://localhost:5000/metrics`
   → expect Prometheus-format output including at minimum
   `aat_health_probe_total{result="ok"} 1`.
5. (Optional) For a no-token local run, export
   `METRICS__ALLOWUNAUTHENTICATEDSCRAPE=true` and re-verify step 3
   now returns 200.
6. (Optional) Development only — stdout shows a span emitted by the
   console trace exporter for the same request.

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

**Manual QA**:
1. `dotnet run --project src/ArmenianAiToy.Api`
2. Trigger at least one chat request (hits both the moderation path
   and the gated OpenAI call).
3. `curl http://localhost:5000/metrics | grep -E 'aat_(chat_openai|moderation_classify)_duration_seconds_(bucket|sum|count)'`
   → expect `_bucket`, `_sum`, `_count` lines for both histogram
   families.

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

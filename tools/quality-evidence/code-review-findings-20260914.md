# Verified findings — code review 2026-09-14

Baseline: dotnet test 3017 pass; 5/5 firmware host tests pass.

## VERIFIED BY HAND (Opus)

### F1 [P1] ContentSyncOptions.Resolve never binds `Retired`
File: Application/Helpers/ContentSyncOptions.cs:203-292
Binding loops read StoryId/Title/AudioUrl/Sha256/Version/SizeBytes but never
`child["Retired"]`. Property exists (:497,598,636,691), is propagated
(:311,328,346,418) and consumed by ContentManifestService. Operator-set
`ContentSync:Stories:N:Retired=true` is a silent no-op — the retirement
signal the firmware acts on can never be raised from config.
Tests construct options in C#, never through Resolve → gap unpinned.
FIX: read + bool.TryParse Retired in all four loops; add a Resolve() test.

### F2 [P1] Usage counter coupled to the flat cost-cap flag
Files: Api/Controllers/ChatController.cs:183-201, AudioChatController.cs:284-296
and ~380-384, StoryQaController.cs:574-593 and ~1053-1065
RecordUsageQuestionAsync (sole writer of DeviceUsageDay) sits inside
`if (costCapOpts.Enabled)`. The tier gate (GetUsageAllowanceStatusAsync)
reads that table. Config `Usage:Tiers:Enabled=true` + `OpenAI:DailyCostCap:
Enabled=false` ⇒ nothing recorded ⇒ IsExhausted always false ⇒ tier limit
silently unenforced fleet-wide. Cost cap defaults true today so latent.
The in-code comment claims the write is unconditional; it is not.
FIX: call it under `costCapOpts.Enabled || usageTiersOpts.Enabled`.

### F3 [P2] RecordUsageQuestionAsync has an unhandled upsert race
File: Application/Services/DeviceService.cs:701-719
Read-then-add on unique index IX_DeviceUsageDays_DeviceId_DayUtc with no
DbUpdateException catch. Sibling ReportStoryPlaysAsync (:399-405) handles
the identical race. Callers swallow, so a concurrent turn's question is
silently dropped from the counter.
FIX: catch DbUpdateException, re-read and retry once.

### F4 [P2] Three ChatService session dictionaries are never pruned
File: Application/Services/ChatService.cs:1409-1436
StoryMemories, RiddleSessions, GameSessions: zero TryRemove anywhere in the
codebase. PendingChoices and ActiveModes are removed on consume.
Keyed by conversation id, static, process-lifetime ⇒ steady unbounded growth
on a long-running instance. The 30-min expiry is read-time only.
(LibraryStorySessionTracker does lazy removal — better, still leaky.)
FIX: periodic sweep on RetentionPurgeService's existing tick.

### F5 [P3] ResponseCleaner line 50 regex not hoisted/compiled
File: Application/Helpers/ResponseCleaner.cs:50
`Regex.Replace(text, @"(\r?\n\s*){3,}", "\n\n")` recompiles per call and
breaks the file's own convention (3 compiled static regexes above it).
Hot chat path. Nested-quantifier shape; input is bounded LLM output so
exploitability is low.
FIX: hoist to static readonly Regex with RegexOptions.Compiled.

### F6 [P2] Two high-severity transitive vulnerabilities — OWNER DECISION
SQLitePCLRaw.lib.e_sqlite3 2.1.10 (GHSA-2m69-gcr7-jv3q)
Microsoft.OpenApi 2.4.1 (GHSA-v5pm-xwqc-g5wc)
EF Core 10.0.2 still resolves 2.1.11 (also vulnerable). Probed: pinning
2.1.13 + OpenApi 3.10.2 clears both and restores clean. Requires two NEW
direct PackageReferences = CLAUDE.md hard stop. OpenApi 2.x→3.x is a major
jump; Swashbuckle 10.1.7 compat unverified against the real solution.
NOT IMPLEMENTED — needs owner approval.

### F7 [P2] CI runs only the backend test suite
File: .github/workflows/ci.yml
No step runs esp32/AregVoiceMvp/host_tests (5 suites, all currently pass
with plain g++) or tools/firmware/test_check_release_image.py or
tools/factory/test_load_sd_card.py. A regression in the release-image
gate — the thing that stops a real key shipping in an OTA image — lands
silently on a PR.
FIX: two extra CI steps (g++ loop; python3 -m pytest tools/).

### F8 [P3] Stale comment in DeviceProvisioningAuth.cs:25-26
Says "the repo does not wire ForwardedHeaders" — untrue since N12.

### F9 [P2] content-manifest / heartbeat do redundant per-request queries
File: Api/Controllers/DeviceController.cs:534-587 (and :97-115)
Up to 9 sequential round-trips per manifest request (GetDeviceAsync + 4x
child override + 4x device flag fallback), most refetching a loaded row.
FIX: one projection, or pass loaded rows into a pure resolver.

### F10 [P3] ParentService.RegisterAsync logs the raw email
File: Application/Services/ParentService.cs:271-273
Success branch logs {Email} at Info while the collision branch is
deliberately email-less. Inconsistent with the file's stated PII posture.

### F11 [P3] CreateDeviceInviteAsync retry gives up silently after 5 tries
File: Application/Services/ParentService.cs:562-568
Proceeds with a colliding selector ⇒ uncaught DbUpdateException ⇒ raw 500.
Very low probability (~614k combinations, short lifetime).

### F12 [P3] Mode-flag setters always write an audit row
File: Application/Services/ParentService.cs:1844-1903
SetDeviceModeFlagsAsync / SetChildModeOverridesAsync audit even on a no-op
write, unlike every sibling toggle. Audit-log noise only.

## PENDING — reviewers still running
Infrastructure, firmware, dashboards, mobile+tools.

## INFRASTRUCTURE (verified by hand)

### F13 [P2] Four dormancy passes have no batch cap
File: Infrastructure/Background/RetentionPurgeService.cs:517, 658, 855 (+device delete)
Only the conversation purge caps its query (Take() appears exactly once, :376).
The four dormancy passes ToListAsync the whole eligible set, then send SMTP
serially inside the same tick, holding the DbContext scope open for the
duration. First enablement against an existing fleet = thousands of rows and
mails in one tick. D_BatchSizeCapRespected only covers the purge pass.
FIX: Take(batchSize) on all four.

### F14 [P2] Parent dormancy-warn commits once AFTER the loop; device pass commits inside
File: RetentionPurgeService.cs — foreach :529, save :584 (outside, guarded by
`if (warned > 0)`). Device equivalent: foreach :873, save :948 (INSIDE, with a
doc comment at :944-947 asserting per-device atomicity).
Crash after SMTP delivery but before the trailing commit ⇒ DormancyWarnedAt
never lands ⇒ next tick re-selects and re-emails those parents.
Duplicate warning emails to real people.
FIX: move SaveChangesAsync inside the loop, mirroring the device pass.

### F15 [P3] Devices.UsageTier default not mirrored in the EF model
Migration 20260911140000_AddUsageTiers.cs sets defaultValue "free" at SQL level;
OnModelCreating never calls HasDefaultValue for UsageTier (it does for TimeZone,
AppDbContext.cs:44). Snapshot :317-318 shows the drift. Harmless at runtime
(C# default always sent) but contradicts the migration's own doc comment.

### F16 [P3] Fire-and-forget alert dispatch
File: Infrastructure/Background/AlertingService.cs:262-296
`_ = SendAlertAsync(...)` in three checks. Currently safe (PostOnceAsync catches
all), but a future throw before the POST becomes an unobserved task exception
instead of reaching the tick's own catch. Latent trap.
FIX: await (already 10s-bounded).

INFRA CLEAN (do not re-check): moderation fails closed on every branch incl.
429/cancel/generic; reliability gate circuit breaker; caller-cancel excluded
from paid retries; STT/TTS bounded + never substitute empty output; blob store
and both orphan sweepers hardened against traversal; backup uses VACUUM INTO +
.part-then-Move, prune matches own prefix only; WAL + busy_timeout=5000 set per
connection; SecurityStamp backfill leaves no NULLs; dormancy transport
precondition fails closed; decimal config parsing pins InvariantCulture;
SmtpNotifier never logs tokens and returns false rather than throwing; all three
hosted loops catch Exception but rethrow OperationCanceledException.

## WEB (verified by hand)

### F17 [P1, stored XSS] bench.html renders message content unescaped
File: Api/wwwroot/bench.html:441-446
`html += `<div class="q">Child: ${m.content}</div>`` then `div.innerHTML = html`
for BOTH user and assistant messages. No escaping anywhere in the file.
Program.cs:441-443 calls UseStaticFiles with NO environment gate ⇒ /bench.html
is served in production.
Worse: bench.html:229/378 keeps the PARENT JWT in **localStorage**
(`localStorage.setItem('parentToken', ...)`) while parent.html deliberately
uses sessionStorage. So injected script on this origin can read a parent's
token directly.
Path: anyone with device credentials POSTs a message containing markup ⇒
stored in Message.Content ⇒ a parent opening bench.html for that device
executes it with their JWT readable.
FIX: build the rows with createElement/textContent (the story.html idiom),
AND gate bench.html to Development, AND move the token off localStorage.

### F18 [P2] admin.html uses esc() where it needs jsq() for an onclick string
File: Api/wwwroot/admin.html:769
`onclick="setUsageTier('${id}','${esc(d.usageTier || 'free')}')"`.
esc() only HTML-escapes; jsq() (:162) also backslash-escapes ' and \ and is
used for every other onclick value (e.g. :833). Server constrains tier names
to configured plans today, so not currently reachable — a plan name with an
apostrophe breaks out and runs arbitrary JS in the operator console.
FIX: esc( → jsq( at that one call site.

### F19 [P3] admin.html leaks an object URL per file selection
File: Api/wwwroot/admin.html:518 — new Audio(URL.createObjectURL(f)), never
revoked. parent.html:2688-2695 has proper trackObjectUrl/releaseObjectUrls
discipline for the same pattern.

### F20 [P2] Five parent.html state-changing handlers have no double-submit guard
Files: parent.html:4638 toggleDevicePause (NO confirm at all — worst case),
5222 deleteChild, 5256 deleteConversation, 5288 unlinkDevice,
5556 toggleDeviceRevocation. flagToggle (~4370) does disable its button.
Double-click on pause reads the same stale d.isPaused closure twice and fires
two overlapping POSTs; final state is whichever response lands last.
FIX: disable the button for the request duration, as flagToggle does.

WEB CLEAN: parent.html builds its whole DOM via createElement/textContent —
no XSS surface. admin.html applies esc/jsq consistently everywhere else
incl. a recursion cap on renderDiag. story.html uses textContent. JWT in
parent.html is sessionStorage, cleared through one sessionExpired() choke
point; resetSessionState() also clears cached DOM. Audio player object URL
tracked+revoked; the save-recording one deliberately untracked w/ 4s timeout.
~35 fetch call sites matched their real routes/verbs/DTOs. All 467 I18N
entries have en+ru+hy with no gaps.

## FIRMWARE (verified by hand)

### F21 [P2] voice_client.cpp read timeout is not rollover-safe
File: esp32/AregVoiceMvp/voice_client.cpp:731-733
`const uint32_t read_deadline = millis() + AREG_HTTP_READ_MS;` then
`if (millis() > read_deadline)`. Every sibling deadline uses the rollover-safe
form `(int32_t)(millis() - deadline) >= 0` — audio_io.cpp:770 (commented
"rollover-safe"), audio_io.cpp:1107, AregVoiceMvp.ino:2147.
At the ~49.7-day millis() wrap the sum overflows to a small value while
millis() is still near UINT32_MAX ⇒ the next check reads as already timed out
⇒ the STT/chat reply download aborts mid-body on a healthy connection.
One dropped voice turn, not a crash. Same class, self-healing, at
content_sync.cpp:2096 scheduler gate.
FIX: (int32_t)(millis() - read_deadline) >= 0

### F22 [P2] content_index.json publish has a power-loss gap
File: esp32/AregVoiceMvp/content_sync.cpp write_index() ~:640
Temp file is written, byte-verified, read back and re-parsed — careful — but
publish is SD.remove(primary) then SD.rename(tmp, primary) because FAT cannot
overwrite in place. Power loss between the two leaves NEITHER file.
load_previous_index() reads that as "absent", not "corrupt", so the
strike-counter/rebuild-refusal net never engages and the toy re-downloads the
entire library. Wasteful, not corrupting.
FIX: keep a .bak of last-known-good and fall back when primary is missing but
.bak exists. Constants kFbIndexBak/kFbIndexOrig already exist (test-only).

### F23 [P3] Six getString() sites read an unbounded body into internal heap
Files: voice_client.cpp:445,551,659; ota_apply.cpp:155; ota_foundation.cpp:399
Only content_sync.cpp:1630 documents this as accepted debt. A large HTTP 200
(e.g. a proxy HTML error page) can exhaust the ~300 KB internal heap.
FIX: check http.getSize() against a cap first, as the audio path already does.

FIRMWARE CLEAN: every flow exit traced — handle_welcome_flow's ST_PLAYING
exits are deliberate and every call site follows with an explicit
transition_to(ST_IDLE); handle_story_session_once and
handle_online_chat_session are single-exit with a trailing transition.
Retirement spares the paused story; orphan sweep can't touch a live index
entry; content_sync_tick only runs from the ST_IDLE branch. download_file_
verified checks status, bounds by sizeBytes, verifies sha before rename;
placeholder rows rejected before the downloader (no div-by-zero). GPIO0
avoided (button 18). Gain and SD clock within ceilings. NVS cursors are
write-on-change. Answer buttons polled, no ISR races. The cross-core QA
upload is mutex-guarded and the caller blocks before touching the shared TLS
client. No credentials in any Serial.print.

## MOBILE (verified by hand)

### F24 [P1] FlaggedMessage.id does not exist on the wire
Backend DTO: Application/DTOs/FlaggedMessageDto.cs:10 → `MessageId` ⇒ wire
field `messageId`. Mobile type api.ts:608-617 declares `id: string`.
FlaggedScreen.tsx:90 `keyExtractor={(m) => m.id}` ⇒ undefined for every row ⇒
duplicate/undefined React keys, broken list diffing and item-state reuse.
FIX: rename to messageId in the type and the keyExtractor.

### F25 [P3] Nine api.ts mutators skip the e_unreachable try/catch
api.ts:152,162,188,212,228,240,252,270,284 call fetch() bare while login/
getJson/mutate/fetchExport/changePassword/deleteAccount wrap it. Screens
catch generically so nothing crashes — an offline parent just gets the wrong
message.

### F26 [P3] Played-audio cache files are never deleted
mobile/AregParent/src/audio.ts:52-54 writes areg-audio-<ts>-<rand>.<ext> into
cacheDirectory on every Listen; nothing ever removes them.

### F27 [P3] Rename field seeds state from a prop and never resyncs
DevicesScreen.tsx:542 useState(device.deviceName ?? '').

MOBILE CLEAN: every other DTO shape matched (LinkedDevice, Conversation*,
MessageDto incl. audioAvailable/childAudioAvailable role gating, StoryPlay,
GamePlay, StoryRequest, AuditEntry, StoryLibrary); all endpoint paths/methods
matched; N9 consent gates and sends its real value; N10 fresh token persisted;
every destructive action behind Alert.alert; BLE permission gate mirrors the
native module, 20s scan timeout with retry, PoP in an in-memory Map only,
never logged or persisted; pairingQr parsing safe; all i18n keys resolve in
all three languages.

## TOOLS (verified by the reviewer, spot-checked)

### F28 [P3] render_story.py opens two ffmpeg list files without encoding=
tools/story-voices/render_story.py:437,520. ASCII paths only today.

TOOLS CLEAN: no gate script swallows an error into a PASS — no bare
except:pass/return 0 in any check_*.py. provision_toy.py NVS namespace/keys
(aregdev/devid/apikey/pop) match device_creds_rules.h byte-for-byte; the API
key touches disk only inside a TemporaryDirectory and never reaches the label
or a log; --dry-run makes no network call. load_sd_card.py validates every
server-supplied id against ^[a-z0-9_-]{1,48}$ before path building (no
traversal), sha+size verification is mandatory, index written via temp +
os.replace. check_release_image.py never prints a found secret's value. No
shell=True anywhere under tools/. Dockerfile/railway.json carry no secrets.

## TOOLCHAIN AVAILABLE HERE
dotnet 10.0.112 yes. g++ host tests yes. arduino-cli NO. mobile
node_modules ABSENT. pytest absent.

# CLAUDE.md

Slim edition (2026-09-11). The former 344 KB file is `docs/CLAUDE-history.md`;
every rule below has its full story there. Code comments that cite
`CLAUDE.md § <section>` refer to that archive. **Read the archive by section
only** (`grep -n '^## ' docs/CLAUDE-history.md`, then `sed -n A,Bp`), never
whole — loading it costs ~98,000 tokens and is why this file was split.

## Project

Armenian AI Toy ("Areg") — a physical children's toy (ages 4–7) with an
Armenian-speaking AI companion. ESP32-S3 firmware (`esp32/AregVoiceMvp/`) is
a thin client to a .NET 10 backend (`backend/`) that orchestrates OpenAI /
ElevenLabs for child-safe stories, games and questions. A parent dashboard
(`wwwroot/parent.html`), an operator console (`wwwroot/admin.html`) and an
Expo app (`mobile/AregParent`) sit on the same backend.

Areg is a **play leader and storyteller**, not an AI friend or chatbot.

## Communication rules (owner rules, in force)

**When the owner asks what he needs to do, answer with the actions and
nothing else.** A numbered list, shortest form that is still true. No
background, no reasoning, no alternatives, no recap of what was built. If a
line is not something for HIM to do, it does not belong in that answer.

- **Detail on request, never by default.** "Why?" is a question he can ask.
- **Bad news is an action, not detail.** A blocker, a cost, a thing that is
  broken, a decision only he can make — those stay in, stated plainly.
- **Length is not effort.** Thoroughness goes into the work, the tests and
  the commit message, not the chat reply.
- Applies to chat and to any status page written for him.

## Product constraints

- **Armenian-first.** All child-facing output is Armenian. Story text is
  **byte-frozen** except by owner edit; any text edit costs a re-render and a
  fresh listen test.
- **Safety-first.** Dual moderation (input + output), fail-closed
  (`moderation_unavailable` ⇒ unsafe). Never bypass a safety check.
- **Parent-trust-first.** No emotional-companion behaviour. **The Absence
  Test:** a child-facing line must stay true if Areg were powered off between
  sessions («I was waiting for you» fails; «Ուրախ եմ քեզ տեսնել» passes).
- **Bounded conversation.** Five modes only — Story, Game, Riddle, Curiosity
  Window, Calm/Bedtime. Spec: `.claude/MODES.md`. Calm can never be disabled.
- **Game honesty.** The toy claims only what it measured (a button, a count,
  a timestamp, something the child said). Never a physical action it cannot
  sense. Enforced by `ChatService.AllowedGameTypes` and the offline games'
  own rules.
- **Same-commit dashboard rule.** Every child-facing feature ships its
  `parent.html` counterpart in the same slice.
- **Folklore is postponed**, single exception `anban-huri` (owner decision).
- **Human listen test gates any shipped audio.** No tool can hear a seam.
  Approvals are pinned to sha256 in `tools/quality-evidence/`; any byte
  change invalidates the approval.

## Standing "never" list (each one bought with a real incident)

- Never write an API key, device key or pairing secret into a file or commit.
  OTA images carry **placeholder credentials only**;
  `tools/firmware/check_release_image.py` refuses an image with a real key.
- Never clone a real person's voice without their consent and a signed AI
  clause (ElevenLabs forbids third-party cloning even with consent).
- Never denoise a voice; re-clone it from clean samples.
- Never connect anything to **GPIO0** (strapping pin; button is GPIO18).
- Never allocate a large `JsonDocument` on internal heap in firmware — use the
  PSRAM allocator (`content_sync.cpp`, `story_select.cpp`).
- Never return from a menu/game/flow with `s_state != ST_IDLE` — end with an
  explicit `transition_to(ST_IDLE)` or the button dies until a power cycle.
- Never raise `AREG_SD_SPI_HZ` above 4 MHz without a read self-test PASS on
  hardware; never raise `AREG_VOLUME_MAX_GAIN` above 1.0 (clipping).
- Never skip, disable or quarantine a test to get green; never `EnsureCreated()`
  on a real DB; migrations are hand-written (`dotnet-ef` cannot run here).
- Never re-mint `Device.ClaimCodeHash` (the QR is printed on the toy).
- Never add high-cardinality metric tags (device/parent/child ids).
- Never add a NuGet package, touch `ChatService`, the system prompt, a domain
  entity, auth or moderation without a plan and owner approval (hard stops).

## Build & test

```bash
cd backend
dotnet build
dotnet test            # 2931 tests, ~35 s in Release
dotnet run --project src/ArmenianAiToy.Api   # http://0.0.0.0:5000
```

Two secrets are REQUIRED before the API starts (build/test do not need them):
`OpenAI:ApiKey` (a dummy boots everything non-AI) and `Jwt:Key` (≥32 chars,
not the legacy literal). In this container: `apt-get update -qq && apt-get
install -y dotnet-sdk-10.0` (Ubuntu archive; dot.net is proxy-blocked). See
`docs/container-toolchain.md`. SQLite migrations auto-apply at startup
(`Migrate()`); verify a hand-written migration by booting against a
throwaway DB, not with `dotnet ef`.

Firmware (`esp32/AregVoiceMvp/`): arduino-cli, core `esp32:esp32@3.3.8`,
FQBN `esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc`,
libraries in the firmware README, `config.h` from `config.h.example`
(gitignored — real creds live in NVS). Production image ≈ 1.33 MB of the 3 MB
OTA slot. Pure logic gets host tests under `host_tests/` (plain g++).
Nothing can be flashed from the cloud; say so.

Python tools need ffmpeg/ffprobe; `ELEVENLABS_API_KEY` comes from the
environment only, paid renders are `--render --confirm-paid-api`.

## Architecture

Backend — Clean Architecture, 4 projects: **Api** (controllers,
`DeviceAuthMiddleware`, static web UI), **Application** (services, DTOs,
helpers; `ChatService` is the orchestration core), **Domain** (entities,
enums), **Infrastructure** (EF Core SQLite, OpenAI/ElevenLabs/Gemini
adapters, background workers).

Key files: `ChatService.cs`, `ModeDetector.cs` (5-mode keyword detection),
`ChoiceNormalizer.cs`, `TailBlockParser.cs`, `WelcomeIntentDetector.cs`,
`StoryQaController.cs` (in-story Q&A, negotiated streaming), `ParentService.cs`,
`DeviceService.cs`, `RetentionPurgeService.cs`, `DatabaseBackupService.cs`,
`ContentSyncOptions.cs` / `ContentManifestService.cs` (what toys download).

Auth: devices use `X-Device-Id`/`X-Api-Key` (revocable via `Device.IsRevoked`);
parents use JWT (`Jwt:Keys` rotation list); operators use the fail-closed
`/api/internal/*` bearer gate (`Internal:Operators`, optional JIT sessions +
TOTP). Everything sensitive is audited in the append-only, FK-free
`AuditEvents` table; system-actor rows have null `ActorParentId` and are
invisible to parents by construction.

AI provider seam: `AI:ChatProvider|TranscriptionProvider|TtsProvider|
ModerationProvider` (openai default; gemini chat adapter; elevenlabs TTS).
Unknown values refuse boot. Moderation stays on OpenAI, fail-closed.

## Backend surface (compact map)

- **Device** (`/api/devices/*`, device-authed): `register` (provisioning
  secret → id, key, claim code, QR payload), `heartbeat` (presence + firmware
  + content report + sync diagnostics; response carries `isPaused`,
  `inBedtimeWindow`, `hasCommands`), `commands` / `commands/{id}/ack`,
  `firmware-manifest` / `firmware-image` (HMAC-signed manifest, real OTA
  apply with rollback), `content-manifest` / `content-file` (four namespaces:
  stories, voice clips, game clips, music; per-device entitlement),
  `story-plays`, `voice-intent` (bounded 8-token answer, no model).
- **Chat** (`/api/chat`, `/api/chat/audio`, `/api/chat/story-qa`, reflection):
  gate chain unclaimed → pause → bedtime → mode (device flag, per-child
  override) → rate limit → daily cost cap → STT → input moderation → model →
  output moderation → TTS. Story-qa streams first byte only when the toy
  sends `X-Areg-Accept-Stream: 1` (kill switch `StoryQa:StreamAnswerAudio`).
- **Parents** (`/api/parents/*`, JWT): register/login (anti-enumeration,
  uniform 401/202/400 shapes, BCrypt on both paths), Google sign-in, password
  reset, email verification, devices (claim by code, invite second parent,
  rename, pause, revoke, unlink = factory reset keeping the Device row),
  per-device toggles (bedtime window, mode flags, story intro / pauses /
  variant endings / questions, bedtime music), per-child mode overrides,
  conversations (summary, flagged, detail, delete, today-summary in the
  device's timezone), assistant audio replay, story library + previews,
  story plays + reflection answers, story requests, audit feed, export.
- **Internal** (`/api/internal/*`): overview, devices, parents, stories,
  flagged, conversations, audit, backup pull, story-qa test playground,
  reversible device actions (revoke, pause, usage tier, claim code, sync
  now, enqueue command), per-toy content entitlement, story upload/release/
  retire.
- **Ops**: `/api/health` (DB-only liveness, non-fatal `openai` field),
  `/metrics` (fail-closed bearer), JSON console logs, OTel counters with
  bounded tags, daily SQLite snapshots + upload-root archive, retention purge
  (90-day messages, token cleanup, dormancy), rate limits (`chat` per device,
  `auth` per IP, per-account login throttle).

## Firmware (current contract)

Pin map: mic 4/5/6, amp 15/16/7, SD 10/11/12/13 (4 MHz SPI), LED 48, main
button **18**, answer buttons 21/47, volume pot 8 (ADC1). Version in
`ota_foundation.h` (`AREG_FW_VERSION`, field release 1.3.2; 1.3.3
hold-to-menu is cable-flashed only).

Behaviour: quick press = play/resume the next story from SD (round-robin,
no repeat, `aregstory` NVS cursor advances only after audio actually
started); GREEN/RED during playback browse; hold 2 s in IDLE = welcome menu
(greeting pool, "what shall we do", story offered by name, child answers by
voice → `voice-intent`); after a story: summary clip, one rotating reflection
question, listen, AI reaction, conclusion; press during a story = in-story
question. Content sync runs ~180 s after boot then on a crash-safe backoff
schedule, writes the index once at the end (per-namespace writes still TODO),
reports what the card holds on the heartbeat. OTA: no ack before reboot, the
check-in is the health gate, bootloader rollback on failure. BLE Wi-Fi
provisioning is on by default (shared PoP `areg1234` — must become per-toy).

Bench truths: the SD breakout module needs 5 V (its own regulator), the card
itself is 3.3 V; corrupt SD reads at 16 MHz were the "aaaa" tone; a bench
build flag left in `config.h` once ate the button for two evenings — always
check `AREG_FW_BUILD` before diagnosing hardware.

## Content pipelines

- **Stories** live in `Application/Stories/Content/*.story.json` (10 runtime
  stories, 3 reflection questions paired 1:1 with 3 conclusions). Audio in
  `backend/story-audio/`, one `Version` per story in `ContentSync:Stories`;
  **bump `Version` or toys keep the old file**; a placeholder row is
  `SizeBytes 0` + all-zero sha (validator drops it safely).
- **Cast render**: `backend/content/story-voices/<id>.voices.json` (speaker
  map; `check_speaker_map.py` pins byte-for-byte reassembly) →
  `tools/story-voices/render_story.py` (one request per span, transcript /
  pitch / tail-chop guards, `RENDER_ONLY`, `REUSE_RAW`) → `align_spans.py` →
  `tools/story-audio/mix_ambience.py` (cues in
  `backend/content/story-ambience/ambience-cues.json`, generated sounds per
  (story, sound) pair, `insert: true` cuts the narration) → loudnorm
  **−16.4 LUFS**, 192 kbps, single ID3 → `segments_to_bytes.py` byte map →
  `check_story_audio.py` gate → `apply_story_clips.py` / `apply_voice_clips.py`
  for the 70 per-story clips and 42 welcome clips. Standing cast: narrator
  `areg-storyteller` (eleven_v3, .55/.8/.2 — an INSTANT clone, not a PVC),
  `katrin-v3`, `vardan-v2`, `areg-wolf`; characters on
  `eleven_v3_conversational`. `eleven_v3` is the only model that speaks
  Armenian and curtails ~1,300 chars per request — hence per-span rendering.
- **Live voice**: everything Areg says live is OpenAI TTS (`OpenAI:TtsVoice`)
  or ElevenLabs via the provider seam; the narrator decision (owner's own
  recording → Professional Voice Clone, 1 slot free) is pending his recording.
- **Text drafts with no audio yet**: `backend/content/variant-endings/`,
  `backend/content/serial-hero/` (Tsivik). Bedtime music: 4 tracks defined
  and reviewed in `backend/content/bedtime-music/tracks.json`, `ContentSync:
  Music` still placeholder-only — see "State of the toy" below.

## State of the toy (2026-09-11)

Active and verified on hardware: SD stories with cast voices and ambience,
browse/pause/resume, after-story flow, welcome flow, in-story Q&A, content
sync + content report, OTA, BLE Wi-Fi setup (toy side), volume knob, bedtime
silence, the whole parent dashboard, operator console, backend safety.

Built but not active or unverified: Game/Riddle/Curiosity/Calm by voice on
the toy (backend done, firmware online chat loop written and compile-
verified, never bench-flashed — see the subsection below); offline games
(compiled into every build since 2026-08-19, round-robin + play reporting
wired 2026-09-11, never bench-flashed — see the subsection below);
mid-story shout pauses (never bench-run); the 10 story variant endings
(all 10 rendered and shipped 2026-09-11 — see the subsection below —
`check_story_audio.py` PASS 20/20, `dotnet test` green, listen test
still open); the Tsivik serial (drafts + speaker maps + placeholder
ContentSync rows prepared 2026-09-11 — see the subsection below — still
not rendered or promoted, blocked on owner sign-off); bedtime music (all 4
tracks rendered and shipped 2026-09-11 — see the subsection below —
`check_music_audio.py` PASS 4/4, `dotnet test` green, listen test still
open); hold-to-menu (unverified by hand, not
staged for OTA); streaming Q&A firmware flag off; mobile app has a
documented, partially-exercised local Android build recipe but still no
verified APK or on-device run (see the subsection below); listen tests of
the cast library on the toy still open; per-toy BLE PoP +
factory station (backend done, firmware + factory station compile/dry-run
verified, never bench-flashed — see the subsection below); content
retirement, per-namespace index writes, and the bounded SD orphan sweep
(backend + firmware done, both compile-verified, never bench-flashed —
see the subsection below); durable child audio (backend fails closed and
tests green — see the subsection below — but `Audio__BlobStoreRoot` still
needs to actually be SET on Railway for the durability guarantee to hold;
until then voice chat refuses with 503 instead of losing recordings
silently, which is the point, but it is not yet a working deployment);
usage-tier metering foundation (backend done, tests green — see the
subsection below — shipped behind `Usage:Tiers:Enabled=false`, so today's
flat daily cost cap keeps governing every device until an operator opts a
fleet in; nothing about a price has been decided).

Still to implement: a `sound-detective` firmware game (backend content
ready — 21 clips already in `ContentSync:Games` — no engine written); two
Simon tone clips (`tone-green` / `tone-red`, not yet rendered); render
variant endings + serial (inputs prepared, see below — the render itself
is still open); narrator PVC; rev-A PCB routing + speaker test + order.

### Online Game/Riddle/Curiosity/Calm voice contract (2026-09-11)

Welcome-flow hold-to-menu now opens `handle_online_chat_session` for
Riddle, Curiosity, Calm (outside bedtime), and Game when no offline game
clips are synced. Loop: play reply → listen → upload → play, ending on
upload failure, the backend's `X-Areg-Turn-End` response header (Game
stop word or any parent-gate reply), two CONSECUTIVE silent windows, or
the turn cap (12). Always returns to `ST_IDLE` explicitly. Pure
loop-termination logic is host-tested (`online_session_rules.h` /
`host_tests/online_session_rules_test.cpp`). Nothing here has been heard
on hardware — see `esp32/AregVoiceMvp/README.md`'s bench checklist.

### Offline games — production contract (2026-09-11)

`offline_games.{h,cpp}` (mind-reader, who-first/two-player buzzer,
button-simon) compile into every build; only the bench 30-second
auto-start stays behind `AREG_OFFLINE_GAMES_BENCH`. Welcome-menu "game"
plays a real offline game whenever `offline_games_available()` says yes
(owner precedence: offline first, the online Game loop above is the
fallback for a card with no game clips). `offline_games_run_next()`
round-robins the three, **skipping any game missing its `intro` clip**,
cursor persisted in NVS (`"areggame"`, write-on-change, same idiom as
`aregstory`). `online_session_offline_game_fallback()` is now live: a
"game" session's first upload failure re-checks `offline_games_available()`
(content sync can finish mid-race) and hands off to an offline round
instead of the failure clip. Every finished session reports to the parent
dashboard (`game_report.{h,cpp}` → `POST /api/devices/game-plays`, backend
already shipped) with honesty-bounded fields only (`offline_games_rules.h`,
host-tested): who-first's outcome is always null, mind-reader reports from
its own side (won = its guess confirmed, lost = the child's win),
button-simon reports won/stopped + the length reached. Nothing here has
been heard on hardware; Simon still needs `tone-green`/`tone-red` clips —
see `esp32/AregVoiceMvp/README.md`'s "Offline games" section and bench
checklist.

### Per-toy BLE PoP + factory station (2026-09-11)

Every toy used to advertise the same shared BLE provisioning PoP
(`areg-pair`). `POST /api/devices/register` now mints a per-device PoP (8
chars, same read-aloud-safe alphabet as invite codes) alongside the claim
code, returned once and additive inside `QrPayload`
(`{deviceId, claim, pop}`). **Never persisted, not even hashed** — the
backend never verifies a PoP (the toy's own BLE stack does), so there is no
read side that would ever need it back; see `DeviceService.GeneratePop`'s
doc comment. Pinned by tests: alphabet/length, absence from the `Device`
row, absence from every log call the registration path makes.

Firmware: `ble_provisioning.cpp` reads the PoP from the `device_creds`
NVS namespace (`aregdev`, key `pop`, beside `devid`/`apikey`), validated by
the pure `device_creds_rules::pop_is_wellformed` before it is trusted over
the bench fallback (`AREG_PROV_POP`, unchanged, still `"areg-pair"`) —
host-tested (`host_tests/device_creds_rules_test.cpp`). The one-shot
`AREG_PROVISION_IDENTITY_ONCE` burn burns the PoP too when the bench-only
`AREG_BLE_POP` macro is defined; `check_release_image.py` now refuses an
OTA image containing a real-looking PoP (8 chars, the mint alphabet).
Proven twice: the synthetic byte-blob suite
(`tools/firmware/test_check_release_image.py`), and a REAL arduino-cli
build — which first caught the gate false-failing on an ordinary clean
image (four coincidental 8-char library-string collisions, fixed via an
exact-value allowlist, same idiom as the existing `KNOWN_LIBRARY_GUIDS`)
and then, once a real (non-placeholder) device id/key made the burn branch
reachable at compile time, correctly flagged the actual compiled-in PoP
string and nothing else.

Production path is `tools/factory/provision_toy.py`: registers a device,
builds an NVS partition image (id/key/pop) with Espressif's own
`nvs_partition_gen` (vendored via its PyPI package, not reimplemented),
writes it with esptool at the offset read live from `partitions.csv`,
verifies a post-flash heartbeat when a serial port is given, and renders a
QR + one-page PDF label — the device key is never printed or logged. A
`--dry-run` mode renders the label from a saved JSON response with no
network or hardware. Runbook: `docs/factory-provisioning-runbook.md`.
Compile-verified (firmware) and run end-to-end against a live local backend
(factory station, register→NVS build→label, no hardware in this
container) — nothing here has been heard on real hardware.

### Content retirement, per-namespace index writes, and the bounded orphan sweep (2026-09-11)

Three deferred content-sync cleanup items, closed together because they
share one contract change. Absence from the manifest and `enabled:false`
still mean "not offered, carried forward forever" (unchanged); an
additive `retired:true` per item is the one new signal that IS a
retirement instruction.

**Backend.** `ContentStoryItem`/`Music`/`Voice`/`Game` items gain a
`Retired` field (default false); a retired item is still emitted with a
fully valid url/sha/size (never a stub — the firmware's per-item
validation would reject an empty one before ever reading the flag), just
with `enabled:false` alongside it. Two sources feed it: a config-driven
item's own new `Retired` bool (an operator hand-edit, same posture as
every other config field), and an uploaded `ContentItem`'s existing
`RetiredAt` — no longer filtered out of the catalogue query entirely
(that made it silently vanish, indistinguishable from "never entitled");
it now reaches every device's manifest tagged retired, regardless of
entitlement, since a device with no cached copy simply ignores the id.

**Firmware.** `content_sync.cpp` now writes `/content_index.json` after
EACH namespace (stories, music, voice, games) finishes, not once at the
very end — a namespace not yet processed this attempt publishes its
unchanged previous state in the interim write, never an empty one, so a
crash partway through a later namespace can no longer discard an earlier
one's downloads (the 2026-08-14 crash-loop bug, closed for good). A
retired id is dropped from the index and its file deleted, unless it is
the story paused mid-way right now (spared until the session ends). A new
bounded, **off-by-default** SD orphan sweep (`orphanSweepEnabled` in the
manifest, `content_orphan_sweep_run()`) removes files under
`/stories`/`/voice`/`/games`/`/music` no index entry references, plus a
stale `/tmp/*.part`, at most `AREG_ORPHAN_SWEEP_MAX_PER_BOOT` (20) per
boot — games are not covered by the retirement signal itself (clip-pair
addressing made it low-value for this slice) but the sweep is a safety
net for them too. Pure decisions in `content_retirement_rules.h`,
host-tested. Deletion/sweep counts ride the heartbeat additively
(`contentRetiredDeleted`, `contentOrphansSwept`).

**Backend orphan sweepers** on `RetentionPurgeService`, both **off by
default**, per-tick capped, path-traversal hardened, system-actor audit
row only on a tick that deleted something: an audio-blob pass (a
`Audio:BlobStoreRoot` directory matching `^[0-9a-fA-F]{32}$` with no
`Conversation` row, past a 24 h grace window) and an uploaded-content pass
(a `ContentSync:UploadRoot` file with no `ContentItem` row, same grace —
`UploadStoryContent` writes its file before the row commits, so a fresh
file may simply be mid-request).

Compile-verified both sides (`dotnet build`/`dotnet test` green;
`arduino-cli compile` green, firmware image +~6 KB flash / +~5 KB RAM) —
nothing here has been heard on real hardware. See
`esp32/AregVoiceMvp/README.md`'s bench checklist for what a human still
has to verify.

### Durable child audio + child recording download (2026-09-11)

`Audio:BlobStoreRoot` used to have no fail-closed contract at all (unlike
`ContentSync:UploadRoot`): unset or relative on Railway meant every child
and assistant recording was written inside the container and silently
destroyed on the next redeploy.

**Fail-closed resolver.** `AudioBlobStoreRootResolver.Resolve` (pure,
`Api/Security/`, pinned by test) treats unset OR relative as "not
configured" in any non-Development environment; Development keeps the
historical relative default. `POST /api/chat/audio` calls it before
STT/chat/TTS and refuses the whole turn with 503 when unconfigured — never
silently drops a recording after the fact. `GET /api/health` gains an
additive, non-fatal `audioStore: ok|unconfigured` field (liveness stays
DB-only, same posture as the existing `openai` field). `Program.cs` logs a
loud startup warning. Operators: set `Audio__BlobStoreRoot=/data/audio-blobs`
on Railway — see `docs/ops-runbook.md`.

**Backups.** `DatabaseBackupService` now zips `Audio:BlobStoreRoot` beside
the DB snapshot and the uploads archive, same one-per-UTC-day idiom, with
its own opt-out (`Backup:AudioBlobs:Enabled=false`) and size cap
(`Backup:AudioBlobs:MaxSizeBytes`, default 500 MB — voice recordings have
no natural ceiling the way curated uploads do; over-cap skips the tick
with a warning, DB snapshot unaffected). Root resolution mirrors
`RetentionPurgeService`'s existing orphan-sweep fallback, so it archives
whatever directory the store is actually writing to. Does not touch, race,
or duplicate PR #40's orphan sweepers.

**Child audio download closes the C2.2 honesty gap.** Parents could
already replay Areg's assistant replies (C2.1) but never their own child's
recordings. `GET /api/parents/messages/{messageId}/child-audio` is the
mirror image of the assistant replay endpoint: same
Message→Conversation→Device→ParentDevice ownership join
(`ParentService.GetChildAudioMessageAsync`), same uniform 404 on every
miss, gated on `MessageRole.User` instead of `Assistant` (an assistant MP3
can never serve through this endpoint and vice versa — the keystone test
in both directions), a MIME whitelist (`audio/wav`, `audio/mpeg` — the
only two `LocalDiskAudioBlobStore.ReadAsync` can ever report), served as
`Content-Disposition: attachment` with a messageId-only (PII-free)
filename. No audit row for a read, same posture as the assistant replay.
`MessageDto.ChildAudioAvailable` (User role AND `AudioBlobPath != null`)
drives a new control on `parent.html`'s child message rows — listen
first (reusing "▶ Listen", the same fetch-with-Authorization-header +
inline `<audio>` pattern as the assistant control), then an additional
"⬇ Save recording" button on the same fetched blob saves it as a file
(untracked object URL, revoked on a timeout, matching the existing export
download's idiom — NOT the player's `trackObjectUrl`, which a view change
would revoke mid-save). The export's `audioDisclosure.childAudioStatus`
now describes the endpoint instead of disclaiming the gap;
`docs/privacy-parents.md` updated, including that a saved copy is the
parent's own and outlives conversation/account deletion. Mobile
(`mobile/AregParent`) has no conversation-detail audio UI at all yet for
either direction (C2.1 was never mirrored there either), so no mobile
changes — nothing to hook new i18n keys into.

**Known gap, not closed in this slice:** `ChildAudioAvailable` reflects
the DB column only (`AudioBlobPath != null`), not whether the file still
exists on disk. Every child recording written before this slice was
written under a NON-durable `Audio:BlobStoreRoot` on Railway, so on a
fleet that has ever redeployed since the toy launched, many historical
rows will show the control and 404 when pressed. Mitigated with calm,
non-red copy («այս ձայնագրությունն այլևս պահված չէ») rather than the red
"unavailable" wording, so it reads as an honest limitation, not a fault —
but the control still appears where it cannot work. Closing this for real
means an existence check in the `ConversationService`/export projections
(a new `IAudioBlobStore` method, and a per-row disk stat on every
conversation-list render), which is its own plan-and-approval item, not a
copy fix.

Backend fully compile- and test-verified (`dotnet build`/`dotnet test`
green, new resolver/backup/endpoint tests). NOT verified: an operator
actually setting `Audio__BlobStoreRoot` on a live Railway instance, or the
dashboard control against a real browser session.

### Loose ends — fault codes, modes-today, dev-harness gate, doc fixes (2026-09-11)

Seven small deferred items, closed together:

- **Parent-visible fault codes for `sync_failed`/`crash_looping`.**
  `DeviceFaultCode` gains `E-401` (library sync failed) and `E-501`
  (crash-looping), combined via `FromHealth` (crash-looping ranks
  highest). Surfaced through the existing `faultCode` mechanism already
  wired into parent.html and admin.html; added to mobile for the first
  time (`api.ts`, `DevicesScreen.tsx`, new `fault_help` i18n key) — mobile
  previously showed no fault code at all, not even E-101.
- **"Modes used today."** `GET /api/conversations/today-summary` gains
  additive `modes` (distinct `Message.Mode` values today, bounded to the
  five modes), rendered as chips on parent.html's Today panel and
  mobile's Today card (new `mode_calm` i18n key there). `ChildId` /
  `AudioBlobPath` absence stays pinned.
- **`/api/story-qa-text` dev-harness gate.** `StoryQaTextDevGate`
  middleware now runs before routing/model binding (same pre-routing
  pattern as `/metrics`, `/api/internal/*`), so a malformed or bodyless
  request outside Development gets the same 404 as a well-formed one —
  closing the gap `docs/CLAUDE-history.md` § "Story Q&A text harness"
  recorded as known-and-accepted. Controller's own check kept as defense
  in depth.
- **CLAUDE-history.md correction.** § "Story cast" and § "The voice Areg
  speaks in" each gain a one-line note that `areg-storyteller` is an
  ElevenLabs INSTANT clone, not a PVC — this file already said so; the
  archive did not.
- **Heartbeat `resetReason`/`bootCount` in admin.html** — checked,
  already fully wired (list + drill-down banners); nothing to add.
- **`anban-huri` source-fidelity re-check** — report only, no story text
  edited: `tools/quality-evidence/anban-huri-source-fidelity-20260911.md`
  finds the runtime text no longer byte-matches the 2026-07-27 pinned
  snapshot (`Հուռնին` → `Հուռուն`, two spots — the same dialect/standard
  difference class as the 19 already catalogued there), with no
  repository record explaining the edit. Owner decision plus an
  evidence-artifact regeneration is recommended, not made.
- **`manifest.webmanifest`.** Icons and `start_url` still resolve
  correctly; fixed one drift from documented intent — `theme_color` no
  longer matched the header's `<meta name="theme-color">` pixel for
  pixel.

Backend: `dotnet build` / `dotnet test` green (2871 tests). NOT verified:
the mobile app was not built or run (no toolchain in this container, per
`mobile/AregParent/AGENTS.md`) — the new fault codes and mode chips have
not been seen on a phone or in a browser.

### Usage-tier metering foundation (2026-09-11)

The machinery for `docs/usage-tiers-brainstorm.md` §6 steps 1 and 3 (fix
the counter, make it durable), shipped as pure plumbing behind
`Usage:Tiers:Enabled` (default **false**). Nothing about a price, a tier
number, or when tiers ship to production is decided here — that stays the
owner's, per the brainstorm doc's own recommendation (shape A now, shape D
later).

**Flag off (today's shipped state): byte-identical.** The flat per-device
daily `OpenAI:DailyCostCap` dollar cap keeps governing chat, `/api/chat/
audio`, and both story-qa voice endpoints exactly as before; the per-tier
allowance is never even queried. Pinned by test on all four gate sites.

**What exists, inert until flipped:** `Device.UsageTier` (string, defaults
`"free"`, hand-written migration `20260911140000_AddUsageTiers`); per-tier
allowances in `Usage:Tiers:Plans` config (name + questions/day + questions/
month, each cap independently optional); the pure `UsageAllowance` helper
that resolves a tier's effective allowance (falling back to `free`, then to
the first configured plan, so a renamed/removed plan can never strand a
device on an unbounded allowance); an additive `DeviceUsageDay` table
(device × UTC day → question count + estimated USD) upserted wherever the
existing cost estimator already records a turn — **written unconditionally**,
independent of the flag, so the counter is honest before anyone turns
gating on. When the flag is on, an exhausted allowance replaces the flat
cap in the gate and the child hears the SAME existing daily-cap canned
clip — no new copy, no number, no word for "limit" reaches the toy.

**Parent surface**, flag-gated, never a price: `LinkedDeviceDto.usage`
(`{ tier, questionsToday, allowanceToday }`, null when the flag is off,
pinned) renders as one line — "N of M questions today" — under the toy
card on `parent.html` and the Expo app's `DevicesScreen`, trilingual copy
reviewed by the armenian-story-master agent (Armenian places the numbers
in reversed order from English/Russian — the natural word order for the
language, not a bug).

**Operator surface:** `POST /api/internal/devices/{id}/tier` (reason +
audit, idempotent, 400 on a tier name outside `Usage:Tiers:Plans`) sets a
device's tier regardless of the flag, so a fleet's tiers can be staged
before it flips; `admin.html`'s device drill-down gained a matching
"Change tier" control, same reason-prompt idiom as pause/revoke.

**Metric** `aat_usage_allowance_exhausted_total{tier}` (bounded tag — the
configured plan-name vocabulary, never a device id) fires only on the
flag-on exhaustion path; the existing `aat_openai_cost_cap_trip_total`
keeps recording every flat-cap trip on the flag-off path, unchanged.

Backend fully compile- and test-verified (`dotnet build`/`dotnet test`
green, 50 new tests; migration verified by booting the API against a
throwaway SQLite DB and reading the produced schema). NOT verified: the
mobile app was not built or run (same standing limitation as every other
mobile change in this file) — the new usage line has not been seen on a
phone; no fleet has ever had the flag turned on.

### Mobile: Android build recipe, BLE hardening, parent-surface parity (2026-09-11)

`mobile/AregParent/scripts/build-android.sh` builds a local, signed release
APK with no EAS account — `expo prebuild`, a throwaway keystore generated
on the fly (never committed), `gradlew assembleRelease`. In this session's
container the Android SDK could not be fetched (`dl.google.com` 403s under
the network policy); `npm install`, `npx tsc --noEmit`, and `expo prebuild`
all ran clean and the script's keystore + Gradle signing-config patch steps
were exercised directly, but `assembleRelease` itself never ran — no APK,
no size, no on-device install this session. BLE provisioning
(`ProvisioningScreen.tsx`) gained Android 12+ runtime permission requests
(the native module only checks, never asks), a 20s scan timeout with retry,
and PoP pickup from a pairing QR pasted as decoded text (no camera scanner
added — see `mobile/AregParent/README.md`), parsed leniently for both the
pre-factory-pairing and current QR shapes and held in-memory only
(`knownPop.ts`), never persisted, mirroring the backend's own posture on
the PoP. Parity pass against `parent.html` (everything shipped since
2026-08-15) found two real gaps and closed both: the after-story question
toggle (`storyQuestionsEnabled`, `/story-questions`) and conversation audio
— Areg's spoken replies and the child's own recordings had no playback
control anywhere in the app; both now have ▶ Listen, and the child's own
also ⬇ Save recording, via `expo-audio` + `expo-file-system` + `expo-sharing`
(new dependencies, native-only, no config plugin applied for any of the
three — deliberately: the default `RECORD_AUDIO` permission expo-audio's
plugin would add is never needed since this app only ever plays audio, and
adding it would ask a parent for a permission the app does not use).
Everything else the parity pass checked (fault codes, invites, story
requests, modes-today chips, the usage line) was already present and wired.
`npx tsc --noEmit` is clean; the project has no lint config and no test
suite. NOT verified: none of this has run in a built app on a device —
same standing mobile limitation as every entry above.

### Variant endings + Tsivik serial — render inputs prepared (2026-09-11)

`ELEVENLABS_API_KEY` unset this session, so this slice prepares and
validates every input the render pipeline needs without spending
anything. Full detail: `docs/variant-endings-serial-render-runbook.md`,
`tools/quality-evidence/variant-endings-serial-prep-20260911.md`.

**Variant endings.** Verified against `story_select.cpp` (not guessed):
an alt plays as the WHOLE narration for a re-listen session, and
reflection dialogue always resolves by the BASE story id — so an alt
needs no `*.story.json` of its own, only a speaker map. All 10
`backend/content/story-voices/<id>-alt.voices.json` copy their base
story's segments verbatim and append one new final segment for the
owner-approved `endingText`, dialogue-split by hand against the text.

**Tsivik serial.** Six `backend/content/story-drafts/tsivik-{one..six}
.story.json` — episode body text byte-identical to the owner-approved
`tsivik-series.json`, re-segmented for TTS/ambience (verified by
reconstruction, no word touched); goal/lesson/reflection metadata is NEW
text pending armenian-story-master + owner review. **Deliberately NOT
promoted to `Stories/Content/`**: that embedding loader hard-requires
`review.status: "approved"` with a real `listenTestAt` for every file
under it, unconditionally, so an unlisten-tested episode there would
crash the whole app at startup — not just this feature. Matching
`backend/content/story-voices/tsivik-*.voices.json` cast: Ծիվիկ/Տատիկ
reuse exact standing-cast settings (Ուլիկ/old-woman precedents); the four
new supporting characters are a first pass flagged "CASTING OPEN FOR
OWNER EAR".

`tools/story-voices/check_speaker_map.py` and
`tools/story-audio/check_ambience_anchors.py` were extended to validate
both shapes (drafts via a `story-drafts/` fallback; alts via
`[base segments] + [endingText]` instead of a story file) — both PASS
(26 maps, 34 cues). 16 placeholder `ContentSync:Stories` rows appended
(`SizeBytes: 0`, dropped before the manifest ever reaches `AltOf`/series
resolution — same posture as every other unaudited story row), pinned by
the existing `ShippedConfig_AdvertisesExactlyTheStoriesThatHaveAudio`
test plus a new narrower one guarding `AltOf`/`SeriesIndex` typos.
`dotnet build`/`dotnet test` green (2929 tests). **Known gap surfaced,
not fixed**: several owner-approved episode bodies use `…` (ellipsis),
outside the TTS-safe whitelist — invisible today (drafts are not linted),
will fail at promotion, needs an owner call before then. NOT done: any
render, any promotion, any listen test, ambience for two episodes and all
10 alts, the `serialnext`/series-refrain clip tooling gap (documented,
not built).

### Bedtime music — render inputs prepared (2026-09-11)

`ContentSync:Music` shipped empty since the feature's plumbing (parent
opt-in toggle, firmware playback, Music dashboard view) was built; the
blocker was rights-cleared tracks. Solved the way ambience was solved:
GENERATED audio, no licence chain. `tools/story-ambience/generate_music.py`
(sibling of `generate_sounds.py`) calls the ElevenLabs Music API — **not
verified against live docs this session** (network egress to
`elevenlabs.io` was blocked; see the script's own docstring and
`docs/bedtime-music-render-runbook.md` step 0, which must be done before
the first real render). Four tracks defined in
`backend/content/bedtime-music/tracks.json`, titles reviewed by
armenian-story-master, post-processed in one ffmpeg pass (fade in 3s, fade
out 8s, loudnorm to **-23 LUFS** — quieter than the -16.4 LUFS narration
target because bedtime music plays alone with no voice over it; justified
in the script), 192 kbps mono MP3, one ID3 tag. `tools/story-audio/
check_music_audio.py` gates duration (3–5 min) and single-ID3-tag. Four
placeholder `ContentSync:Music` rows shipped (`SizeBytes: 0`), pinned by a
new `ContentSyncAudioRootTests` test (shipped rows point at real files,
placeholders exempt) and a new `ContentSyncMusicTests` test (shipped
placeholder rows drop from the manifest). The fade/loudnorm/encode pipeline
was run for real against synthetic audio (not a paid render) — see
`tools/quality-evidence/bedtime-music-prep-20260911.md`. `dotnet build`/
`dotnet test` green (2931 tests). NOT done: any paid render, any listen
test, any real track.

### Variant endings — 10 of 10 rendered and shipped (2026-09-11, render session)

`ELEVENLABS_API_KEY` became available in a later session the same day;
executed part 1 of `docs/variant-endings-serial-render-runbook.md` (the 10
alt endings only — the Tsivik serial stayed blocked, see below). All 10
`backend/content/story-voices/<id>-alt.voices.json` rendered via
`render_story.py`, shipped at -16.4 LUFS/192 kbps mono/single ID3 (a
manual ffmpeg equivalent of `Ship-StoryAudio.ps1`'s Repair-And-Level —
PowerShell is not on this host), byte-mapped via `segments_to_bytes.py`.
The 10 `ContentSync:Stories` placeholder rows now carry real
`SizeBytes`/`Sha256`. `check_story_audio.py` PASS across the full shipped
library (20/20: 10 base + 10 alt). `dotnet build`/`dotnet test` green
(2931 tests, none needed updating). Full sha256 table and commands:
`tools/quality-evidence/variant-endings-serial-prep-20260911.md`'s
"Render update" section.

Found and fixed two real tooling bugs while exercising the pipeline for
the first time with a paid key: `render_story.py`'s ffmpeg concat step
was silently failing on every story (relative paths resolved against the
wrong directory, no return-code check) — fixed with absolute paths and a
return-code check; `check_story_audio.py` had no way to compute an alt
ending's expected length and would have failed the whole gate on the
first shipped alt — extended to resolve it from the base story plus
`variant-endings.json`'s `endingText`. Three spans failed a render guard
and needed one `RENDER_ONLY` retry each (all resolved on the first
retry); `little-cloud-alt` was fully re-rendered once while diagnosing
the concat bug (an unavoidable extra cost from that bug, not repeated for
any other story) — see the evidence file for the honest accounting.

**The Tsivik serial was deliberately NOT rendered or promoted.** The
runbook's own step 1 requires owner sign-off on new reflection/goal/
lesson text, an owner decision on the pre-existing ellipsis character in
several episode bodies (it reaches the TTS-bound body text, not just
promotion metadata), and a casting confirmation on the sample-first
listen — none of which an unattended session can supply. The 6
placeholder `ContentSync:Stories` rows are unchanged from PR #45.

**The human listen test is still open for all 10 rendered endings** —
nobody has heard any of them yet.

### Bedtime music — 4 of 4 rendered and shipped (2026-09-11, render session)

`ELEVENLABS_API_KEY` became available. Step 0 of
`docs/bedtime-music-render-runbook.md` (verify the Music API before
spending) was closed by calling `api.elevenlabs.io` directly rather than
fetching docs — `elevenlabs.io` (the docs site) stayed `EGRESS_BLOCKED`,
but the API host itself is reachable: one cheap probe
(`music_length_ms: 10000`) returned HTTP 200 with a real MP3 back,
confirming `generate_music.py`'s `POST /v1/music` shape.
`force_instrumental: true` was added to the request body as a verified
belt-and-suspenders on the in-prompt instrumental instruction.

All four tracks rendered on the first call each (no retries), passed
`check_music_audio.py` (4/4, one ID3 tag each, all inside the 3–5 min
window at their exact requested lengths), and measured within 0.9 LU of
the -23 LUFS target. The four `ContentSync:Music` placeholder rows now
carry real `SizeBytes`/`Sha256`;
`ContentSyncMusicTests.Manifest_ShippedPlaceholderTracks_AreDropped` was
replaced with `Manifest_ShippedTracks_AllFourReachTheManifest`. `dotnet
build`/`dotnet test` green (2931 tests, none added — one renamed). Full
sha256 table and commands: `tools/quality-evidence/
bedtime-music-prep-20260911.md`'s "Render" section.

**The human listen test is still open for all four tracks** — nobody has
heard any of them yet, at any volume; the -23 LUFS target itself remains
unconfirmed by ear on the toy's actual speaker.

### Firmware P0s — PSRAM index parse, HMAC release gate (2026-09-12, N1)

`content_report.cpp` and several `story_select.cpp` accessors parsed
`/content_index.json` on internal heap (the 2026-08-14 failure class); all
now share `json_psram.h`'s `g_json_psram` with `content_sync.cpp`.
`check_release_image.py` now refuses an image with `ota_apply.cpp`'s
`OTA_SIG_CHECK_DISABLED` marker (HMAC key compiled empty). Compile-verified
(arduino-cli, canonical FQBN); host tests + gate tests green; NOT
bench-verified on hardware.

### Onboarding paste-QR parser, admin table overflow (2026-09-12, N2)

`parent.html`'s claim form gained a lenient paste-the-QR-JSON field
(mirrors `mobile/AregParent/src/pairingQr.ts`; `pop` is parsed and
discarded, never stored/shown), honest help copy (no app scanner claimed),
and a client-side Device ID shape check. `admin.html`'s `table()` now
wraps every table in a scrollable, shadowed `.table-wrap`; a ≤480px media
query relaxes the fixed input min-widths. ux-ui-designer review applied
(state-clear on re-claim, case-sensitive claim code, focus/aria fixes,
scroll affordance); its mobile-copy-sync suggestion is deferred, out of
scope for a web-only slice. `dotnet test` green (2931 tests); Playwright
confirmed no page-level horizontal scroll at 400/1280px. NOT verified: a
real browser session against the live backend.

### Factory SD-card loader (2026-09-12, N3)

`tools/factory/load_sd_card.py` fetches a device's content-manifest,
downloads every enabled/non-retired item into the firmware's exact
namespace layout, sha256-verifies each one, and writes
`/content_index.json` in the shape `content_sync.cpp` itself writes — a
pre-loaded card is trusted on first boot, no separate "synced once" gate.
Runbook §2a; `--dry-run`/`--verify`, stdlib-only, idempotent. Verified
end-to-end against a live throwaway backend (240 items, ~117 MB, the real
catalogue); `dotnet test` green (2931 tests). NOT verified: a real card in
a real toy.

## Working in this repo (agents)

- Classify first: workstream, mode, risk. HIGH risk (ChatService, system
  prompt, entities, safety, auth, new endpoint, NuGet) → plan, stop for
  approval. LOW risk → brief plan, proceed. Skills and agents in `.claude/`
  (`repo-scout`, `plan-proposer`, `backend-implementer`, `test-runner`,
  `doc-sync`, `armenian-story-master`, `ux-ui-designer` for ANY UI change,
  `hardware-schematic-engineer`, `esp32-s3-hardware-expert`).
- **Context discipline (new, load-bearing).** Ten parallel sessions burned a
  5-hour usage window in minutes because each sat at 300k tokens re-sent on
  every tool call. So: never `cat` a file over ~300 lines — `grep -n` then
  `sed -n A,Bp`; read the history archive by section; delegate wide searches
  to an `Explore` subagent and keep only its conclusion; prefer one session
  at a time; use Sonnet for firmware/dashboard/tooling and Fable only for
  ChatService, safety and Armenian text.
- Commit convention: `.claude/COMMIT-CONVENTION.md` (subject ≤70 chars,
  why-focused body, say what was verified and what was NOT). Never push to
  `main`; open a PR and wait for the owner's "merge".
- Before completing: tests green, no secrets staged, test count in this file
  matches, new endpoints documented, diff minimal, story-affecting changes
  benchmarked (`tools/StoryBenchmark`, `tools/GameBenchmark`).
- Key evidence lives in `tools/quality-evidence/`; runbooks in `docs/`
  (`ota-release-runbook.md`, `ops-runbook.md`, `story-audio-rerender-runbook.md`,
  `container-toolchain.md`, `voice-narrator-brief.md`, `latency-plan.md`).

## History archive index (`docs/CLAUDE-history.md`)

Sections, in order: Project · Communication rules · Product Constraints ·
Build & Test · Database migrations · Architecture · Parent monitoring surface
· Today summary (E1.1/E1.2/E2.1) · Audit events · Data export · Backups ·
Retention · Bedtime window · Mode flags · Per-child overrides · Password reset
· Email verification · Register anti-enumeration · Google sign-in · JWT
rotation · Host filtering & CORS · Rate limiting · Logging · Metrics · Voice
chat C1/C2.1/C2.2a/C2.2b/C2.3 · Story plays, reflection, library & clips ·
Story narration pipeline · Story ambience · Character voices & alignment ·
Spoken welcome flow · AI provider seam · The voice Areg speaks in · Story cast
· After-story question toggle · Story Q&A streaming · Story Q&A text harness ·
Internal console · Engineering guardrails · Consumer platform (pairing,
invites, presence) · Device OTA (contract, firmware, content sync, SD
playback, story selection, real OTA apply) · Content-depth batch · Owner batch
2026-08-07 · Dashboard redesign & cost cap · The toy says what content it has
(Stages 1–5) · Live device key incident · Owner batch 2026-08-15/16 · Firmware
fixes 2026-08-18/19 · SD bus / story stream arc · Key design decisions ·
Autonomous workflow.

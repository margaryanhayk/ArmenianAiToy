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

## Release freeze (2026-10-22, in force until the owner lifts it)

`SHIP.md` is the only finish line. Every other plan is archived with a
banner. The evidence audit is `docs/ship-md-evidence-audit-20261022.md`;
the owner's own steps are `docs/status/build-status.html`.

- **Before any code change, name the `SHIP.md` item it serves.** If none,
  write the idea into the reply as a `docs/v2-backlog.md` proposal and stop.
  Nobody adds a child-facing feature or another "built but never verified"
  slice while the freeze holds.
- Allowed without asking: fixing a bug in shipped behaviour, closing a
  `SHIP.md` item, recording evidence, docs, and the gates below.
- Gates: `tools/release/preflight.sh` (every automated check, one command),
  `python3 tools/listen/listen_test.py status` (what is still unheard),
  `tools/firmware/bench_watch.py` (turns a bench evening's serial log into
  SHIP evidence). All three are green/known before a PR is opened.

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
dotnet test            # 3346 tests, ~45 s in Release
export OpenAI__ApiKey="sk-dummy-for-local-boot"               # any non-empty value boots everything non-AI
export Jwt__Key="local-dev-jwt-key-0123456789abcdef0123456789"  # ≥32 chars, not the legacy literal
dotnet run --project src/ArmenianAiToy.Api   # http://0.0.0.0:5000  (Windows: $env:OpenAI__ApiKey=...)
```

Two secrets are REQUIRED before the API starts (build/test do not need them):
`OpenAI:ApiKey` (a dummy boots everything non-AI) and `Jwt:Key` (≥32 chars,
not the legacy literal). In this container: `apt-get update -qq && apt-get
install -y dotnet-sdk-10.0` (Ubuntu archive; dot.net is proxy-blocked). See
`docs/container-toolchain.md`. SQLite migrations auto-apply at startup
(`Migrate()`); verify a hand-written migration by booting against a
throwaway DB, not with `dotnet ef`.

`ArmenianAiToy.Infrastructure` direct-references `SQLitePCLRaw.lib.e_sqlite3`
2.1.13 (2026-09-15) to override the 2.1.10 that `Microsoft.EntityFrameworkCore
.Sqlite` 9.0.3 resolves transitively (GHSA-2m69-gcr7-jv3q, high; upgrading EF
Core does not fix it — 10.0.2 still resolves to a vulnerable 2.1.11). The
matching `Microsoft.OpenApi` 2.4.1 advisory (GHSA-v5pm-xwqc-g5wc, high, via
`Microsoft.AspNetCore.OpenApi`/`Swashbuckle.AspNetCore` in Api) is NOT pinned:
`Microsoft.OpenApi` 3.x made `IOpenApiMediaType.Example` read-only, which
breaks `Microsoft.AspNetCore.OpenApi` 10.0.2's XML-comment source generator
at compile time — a real incompatibility, not a config issue. That advisory
stays open pending a compatible `Microsoft.AspNetCore.OpenApi`/Swashbuckle
release.

Firmware (`esp32/AregVoiceMvp/`): arduino-cli, core `esp32:esp32@3.3.6` on
the release machine (3.3.7/3.3.8 boot-loop when BLE provisioning starts,
`config.h.example:530-533`; the cloud container compiles on 3.3.6 too — recipe
in `docs/container-toolchain.md`),
FQBN `esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc`,
libraries in the firmware README, `config.h` from `config.h.example`
(gitignored — real creds live in NVS). Production image ≈ 1.33 MB of the 3 MB
OTA slot. Pure logic gets host tests under `host_tests/` (plain g++);
`host_tests/virtual_card/run_virtual_card.py` also runs the real SD-index and
story-picking code on a PC against a fake card (preflight runs it).
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
parents use JWT (`Jwt:Keys` rotation list; each token carries the parent's
`SecurityStamp` as the `sst` claim and stops validating once a password
change / reset / anonymization rotates it — `Jwt:RequireSecurityStamp`,
default true, is the rollback switch); operators use the fail-closed
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
  sends `X-Areg-Accept-Stream: 1` (kill switch `StoryQa:StreamAnswerAudio`);
  `/api/chat/audio` streams only under the same header and
  `AudioChat:StreamReplyAudio` (2026-10-27, B1 — the toy needs Content-Length).
  Both chat endpoints hand `RequestAborted` to `ChatService`: a toy that
  drops mid-turn aborts the moderation/model calls with no assistant row
  stored (the child's own row is written before the model call, as on a
  model failure); a reply output moderation already approved is stored.
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
  flagged (+ flagged-reflections), conversations, audit, backup pull,
  system (models/keys-present/checks/spend/backup age), system/elevenlabs
  (quota), logs (recent Warning+), story-qa test playground,
  reversible device actions (revoke, pause, usage tier, claim code, sync
  now, enqueue command), per-toy content entitlement, story upload/release/
  retire.
- **Ops**: `/api/health` (DB-only liveness, non-fatal `openai` field),
  `/metrics` (fail-closed bearer), JSON console logs, OTel counters with
  bounded tags, daily SQLite snapshots + upload-root archive, retention purge
  (90-day messages, token cleanup, dormancy), rate limits (`chat` per device,
  `auth` per IP — real on Railway once `ForwardedHeaders__Enabled=true`, whose
  `KnownNetworks` defaults to private/CGNAT ranges so nothing else need be
  set — per-account login throttle), opt-in webhook alerter
  (`Alerts:WebhookUrl`, health/cost-cap/circuit/moderation/backup signals).

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
provisioning uses a per-toy PoP minted at registration (2026-09-11); the
shared `areg-pair` PoP remains only as the cable-flash bench fallback.

Bench truths: the SD breakout module needs 5 V (its own regulator), the card
itself is 3.3 V; corrupt SD reads at 16 MHz were the "aaaa" tone; a bench
build flag left in `config.h` once ate the button for two evenings — always
check `AREG_FW_BUILD` before diagnosing hardware.

## Content pipelines

- **Stories** live in `Application/Stories/Content/*.story.json` (10 runtime
  stories, 3 reflection questions paired 1:1 with 3 conclusions). Audio in
  `backend/src/ArmenianAiToy.Api/story-audio/` (`ContentSync:AudioRoot`),
  one `Version` per story in `ContentSync:Stories`;
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
  Music` — all four tracks rendered 2026-09-11, listen test open.

## State of the toy (summary; slice-by-slice detail in the archive § "State of the toy — slice log")

Verified on hardware (last bench 2026-09-02, fw 1.3.3): SD stories with
cast voices and ambience, browse/pause/resume, after-story flow, welcome
flow, in-story Q&A, content sync + content report, OTA, BLE Wi-Fi setup,
volume knob, bedtime silence, parent dashboard, operator console, backend
safety.

Built, tests green, never heard on a toy: online Game/Riddle/Curiosity/Calm
voice loop; offline games (mind-reader, who-first, button-simon; the Simon
tone clips are on disk, unverified by ear); mid-story shout pauses; the 10
variant endings and 4 bedtime tracks (rendered, listen test open);
hold-to-menu; streaming Q&A (firmware flag off); per-toy BLE PoP + factory
station; content retirement + bounded orphan sweep; durable child audio
(`Audio__BlobStoreRoot` still to SET on Railway); usage tiers
(`Usage:Tiers:Enabled=false`); mobile app (no verified APK); webhook/
Telegram alerts (`Alerts__WebhookUrl` to set); Vertex Gemini backend (no
live call); STT `gpt-transcribe`; self-harm line + secrecy guard (the
reflection path is NOT covered); fault codes E-401/E-501; operator
System/Logs tabs; backup restore drill (local only); release tooling
(listen test, preflight, bench_watch — see § Release freeze).

Not built: the `sound-detective` game engine; the Tsivik serial render
(owner sign-off); narrator PVC; rev-A PCB. Three facts outrank the rest:
HEAD firmware compiles clean on core 3.3.6 (cloud, 2026-10-27, 51.9 % of the OTA slot) but has never been flashed;
the shipped audio has never been played on a toy; the repo is public with
two credential leaks in history (SHIP B4, owner decision).

## Working in this repo (agents)

- **Release freeze first** (section above): name the `SHIP.md` item or stop.
- Classify first: workstream, mode, risk. HIGH risk (ChatService, system
  prompt, entities, safety, auth, new endpoint, NuGet) → plan, stop for
  approval. LOW risk → brief plan, proceed. Skills and agents in `.claude/`
  (`repo-scout`, `plan-proposer`, `backend-implementer`, `test-runner`,
  `doc-sync`, `armenian-story-master`, `ux-ui-designer` for ANY UI change,
  `hardware-schematic-engineer`, `esp32-s3-hardware-expert`).
- **Token budget (owner rule, 2026-10-27 — "you are eating my tokens").**
  This file is re-sent on every tool call: keep it under 24 KB
  (`tools/release/preflight.sh` fails above that); slice notes go to
  `docs/CLAUDE-history.md` § "State of the toy — slice log", never here.
  Never `cat` a file over ~300 lines — `grep -n` then `sed -n A,Bp`; read
  the archive by section. One session at a time. Subagents: at most two
  concurrent, Sonnet by default, Fable only for ChatService, safety and
  Armenian text; a "review everything" request is ONE reviewer pass with
  path:line evidence, not a finder plus a verifier per area (that pattern
  cost ~2.5 M tokens on 2026-10-27). No `Workflow` fan-out unless the owner
  names a token budget in the same message.
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
Autonomous workflow · State of the toy — slice log (2026-09-11 → 2026-10-22).

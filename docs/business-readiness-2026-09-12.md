# Business readiness — Areg, night of 2026-09-11 → 12

Written for the owner. Four read-only audits (backend, firmware, parent
surfaces, ops/business) ran against `main` at 9060afc (after PRs #36–#48).
Every finding below was verified against a file and line; anything the
auditors could not point to was dropped. Stale claims in older docs are
called out where found.

Legend: **MANDATORY** = cannot sell without it · **IMPORTANT** = fix before
100 units · LATER = after the first batch. **OWNER** = only the owner can do
it (hardware, accounts, money, legal sign-off). **HARD STOP** = needs the
owner's approval before code changes (ChatService, prompt, entities, auth,
moderation, NuGet).

---

## 1. Where we are (one screen)

| Area | State |
|---|---|
| Backend | Mature. Auth, IDOR, DST, retry/backoff, backups, retention, export, audit all in place. 2931 tests green. No P0 found. |
| Firmware | Field release 1.3.2 verified on hardware for stories, welcome, Q&A, sync, OTA, BLE setup. Everything merged today (voice modes, offline games, per-toy PoP, content cleanup) is compile-verified only, **never flashed**. |
| Content | 10 stories with cast voices + ambience, 42 welcome clips, ~70 clips per story, 10 variant endings, 4 bedtime music tracks, 21 sound-detective clips. Tsivik serial: text only. **No human has listened to today's renders.** |
| Parent surfaces | Web dashboard and operator console solid (sessionStorage JWT, escaped HTML, confirmations, en/ru/hy). Onboarding by claim code is the weak point. Mobile app never built for a device. |
| Ops | One Docker image + SQLite on Railway. No staging, no alerting, no restore drill, child recordings not backed up. |
| Legal | terms.html / privacy.html exist, consent timestamp recorded at registration. No COPPA / GDPR-K language, no verifiable parental consent. |
| Hardware | Rev-A PCB not routed, speaker test not done, narrator PVC not recorded. |

Bottom line: the software is closer to sellable than the operations,
onboarding and hardware are. The next hundred units are blocked by owner
actions (flash, listen, PCB, accounts, legal), not by backend code.

---

## 2. MANDATORY before the first 100 units

Ordered by what unblocks the most.

1. **OWNER — Flash `main` on one toy and run the bench checklist.** Voice
   modes, offline games (mind-reader / who-first / Simon), per-toy PoP,
   content retirement + orphan sweep, hold-to-menu, shout pause, streaming
   Q&A: all shipped blind. `esp32/AregVoiceMvp/README.md` § bench checklist.
2. **OWNER — Listen test:** 10 variant endings, 4 music tracks, cast library
   on the toy speaker. Approvals pin to sha256 in `tools/quality-evidence/`.
3. **Firmware P0 — OTA signature check can silently switch itself off.**
   `esp32/AregVoiceMvp/ota_apply.cpp:178-189` skips HMAC verification when
   `AREG_MANIFEST_HMAC_KEY` is empty and only logs "Stage-A bench only".
   `tools/firmware/check_release_image.py` never checks for it. Fix: the
   release gate refuses an image containing the skip string. *(night session)*
4. **Firmware P0 — `content_report.cpp:62` parses the whole content index
   on internal heap** (`JsonDocument doc;`) while every other reader uses the
   PSRAM allocator (`content_sync.cpp:83`). Violates the standing "never".
   Fix: use `s_json_psram`. *(night session)*
5. **OWNER — Railway:** confirm the `/data` volume is mounted on the live
   service; set `Audio__BlobStoreRoot=/data/audio-blobs`; set
   `ForwardedHeaders__Enabled=true` + known proxies, otherwise the auth rate
   limiter (`AuthRateLimiter.cs:53-58`) sees every parent as one IP and a
   handful of families lock each other out; set `Alerts__WebhookUrl` (a
   Slack incoming-webhook URL or any generic JSON receiver) to turn on the
   webhook alerter (item 7). **Boot warning added (N4); alerter shipped
   off-by-default (N5); Railway env vars still OWNER.**
6. **Backup restore drill, once, documented.** Snapshots exist
   (`docs/ops-runbook.md:93-107`) but no restore has ever been proven.
   *(night session, against a throwaway DB, evidence file)*
7. **Alerting.** Nothing pages anyone when health fails, the cost cap
   trips, or the OpenAI circuit opens. `/metrics` exists but nothing reads
   it. *(night session: webhook alerter, off by default)* — **done (N5):**
   `AlertingService` (Infrastructure background worker, off by default via
   `Alerts:WebhookUrl`) posts `{text,key,severity,at}` to a Slack-style or
   generic webhook on DB-health transitions, `Audio:BlobStoreRoot`
   transitions, cost-cap trips over threshold in the last hour, the OpenAI
   circuit opening, moderation fail-closing, and a stale (>36h) or absent
   database backup — see `docs/ops-runbook.md` § Alerting. Per-key cooldown,
   one retry, never throws on a broken webhook. Verified against a real
   local receiver (payload pasted in the commit message) and 20 new unit
   tests (`dotnet test` green). **Not verified:** a real Slack/Railway
   delivery — `Alerts__WebhookUrl` is still OWNER's to set.
8. **Onboarding: claim a toy without typing a 36-character GUID.**
   `parent.html:1207-1211` promises an app QR scanner that does not exist
   (`ProvisioningScreen.tsx:118-121`) and asks for the device id by hand.
   Fix: paste-the-QR-string parser on web, honest copy, scanner later.
   *(night session)* — **web paste parser done (N2), scanner still open.**
9. **SD-card content loading is undocumented and unscripted** for the
   factory. Identity provisioning is automated
   (`tools/factory/provision_toy.py`) but the card that carries every story
   is a manual step. *(night session: `tools/factory/load_sd_card.py`)* —
   **done (N3): `tools/factory/load_sd_card.py` downloads every entitled
   item and writes a firmware-shaped `/content_index.json`; runbook §2a.
   Verified against a live throwaway backend (240 items, ~117 MB, the full
   real story/music/voice/game catalogue) — a real card in a real toy is
   still NOT verified.**
10. **OWNER + legal — parental consent.** Add COPPA / GDPR-K wording and a
    verifiable parental-consent step (not just a ToS click). *(night session
    drafts the text; owner signs off; consent step is a HARD STOP)*
11. **OWNER — Rotate the burned ElevenLabs key** and the shared bench PoP
    once per-toy PoP is verified.
12. **OWNER — Pricing decision.** Tiers are built behind
    `Usage:Tiers:Enabled=false`; the flat $0.25/day cap is "must change before
    production" per `tools/quality-evidence/cost-per-hour-of-play-20260812.md`.

---

## 3. IMPORTANT (before 100 units, not before the first 10)

- **HARD STOP — JWTs survive a password change** (`Program.cs:146-154`,
  30-day tokens). Add a security-stamp claim checked in `OnTokenValidated`.
- **HARD STOP — `ChatService.GetResponseAsync` has no `CancellationToken`**
  (`ChatService.cs:1685`): a toy that drops Wi-Fi mid-turn cannot abort the
  paid OpenAI calls.
- Async Q&A upload task runs TLS in an 8 KB stack
  (`voice_client.cpp:1256-1268`, self-flagged, never measured). Measure
  `uxTaskGetStackHighWaterMark` on hardware before enabling streaming Q&A.
- Mobile parity: no unlink, no delete-conversation, no delete-child in
  `mobile/AregParent/src/api.ts`; `app.json:24` owner is a placeholder EAS
  account; `eas.json` production submit block empty. *(night session for the
  API calls; accounts are OWNER)*
- `admin.html` tables overflow the page at 400 px. *(night session)* — **done
  (N2): tables scroll inside their own container, inputs no longer force a
  wide layout below 480 px.**
- Staging environment and a written backend rollback procedure.
- PoP re-issue when a label is lost is a support case by design
  (`docs/factory-provisioning-runbook.md:142-157`). Decide the policy.
- OTA has no downgrade path; a bad-but-booting release needs a higher
  version or cable flash.
- Wi-Fi reset / resale flow on the toy side is not documented.
- No CPU light sleep; irrelevant if wall-powered, decide before battery.

## 4. LATER

- SQLite → Postgres before any second instance.
- Log retention/rotation.
- `today-summary` does four `CountAsync` round-trips
  (`ConversationService.cs:294-314`).
- Stale comments/docs → **done (N4).** `InternalController.cs` class doc now
  describes the real, guarded ~dozen-`[HttpPost]` mutation surface instead of
  "every action is a GET"; `StoryAudioController.cs`'s unset-token comment now
  says fail-closed (matching `IsTokenAccepted`), not open/opt-in;
  `docs/railway-deploy.md`'s branch note now says the deploy config is on
  `main`; `docs/ops-runbook.md` gained a `ForwardedHeaders__Enabled` section
  next to `Audio__BlobStoreRoot`; CLAUDE.md's "Still to implement" and
  "Firmware (current contract)" sentences no longer list the variant-ending
  render or the shared BLE PoP as open.

---

## 5. Unit economics (from the repo's own measurement)

`tools/quality-evidence/cost-per-hour-of-play-20260812.md`: measured
**$0.0078 per online turn** (STT `gpt-4o-mini-transcribe`, chat `gpt-4o`,
TTS `gpt-4o-mini-tts`, OpenAI moderation). Stories from the SD card cost
nothing per play.

| Online turns / day | Cost / child / month |
|---|---|
| 5 | $1.17 |
| 15 | $3.51 |
| 30 (= today's cap) | $7.02 |

Fixed: Railway hobby instance, one ElevenLabs plan (library renders are a
one-off; the dollar figure is not recorded in the repo — record it next
render). The daily cap guarantees the worst case per toy; tiers decide the
price.

---

## 6. What one sellable unit takes today

1. Flash the release image (cable; `docs/ota-release-runbook.md`).
2. `tools/factory/provision_toy.py`: register, per-toy PoP, NVS burn, label.
3. Delete the plaintext `nvs.bin` (manual, trusted step).
4. Load the SD card — `tools/factory/load_sd_card.py`
   (`docs/factory-provisioning-runbook.md` §2a; mandatory item 9, **done N3**).
5. Print the claim QR (already on the label).
6. Box it.

Steps 1, 3 and 4 are where a hundred units will go wrong.

---

## 7. Night plan (serial Sonnet sessions, one at a time, stacked PRs)

| # | Slice | Risk | Files |
|---|---|---|---|
| N1 | Firmware P0s: PSRAM allocator in `content_report.cpp`; release gate refuses an image with the HMAC-skip string; compile both builds | LOW | esp32, tools/firmware |
| N2 | Onboarding: paste-QR parser + honest copy in `parent.html`; `admin.html` table overflow | LOW (ux-ui-designer review) | wwwroot |
| N3 | `tools/factory/load_sd_card.py` + runbook section | LOW | tools/factory, docs |
| N4 | Backend small: forwarded-headers boot warning; stale comments; stale docs; CLAUDE.md state | LOW | Api, docs |
| N5 | Alerting: `Alerts:WebhookUrl` background alerter (health, cost cap, OpenAI circuit), off by default, tests | MEDIUM (no new endpoint, no NuGet) | Infrastructure |
| N6 | Backup restore drill against a throwaway DB, evidence file | LOW | tools/quality-evidence, docs |
| N7 | Mobile parity: unlink, delete conversation, delete child | LOW | mobile |
| N8 | Legal draft: COPPA / GDPR-K section + consent-step proposal (docs only) | LOW | docs/legal |

Not touched tonight (HARD STOP, owner's word needed): JWT security stamp,
ChatService cancellation, consent step in registration, pricing/tiers.

Every PR waits for the owner's "merge". Each session branches from the
previous one so the PRs merge in order without conflicts.

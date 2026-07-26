# v2 backlog — explicitly NOT in v1.0

Companion to `SHIP.md`. **`SHIP.md` is the finish line; this file is everything
that is deliberately not on it.**

The purpose is to stop re-deciding. When an idea arrives mid-session, it goes
here instead of quietly becoming work. Nothing in this file blocks v1.0, and
nothing here should be started until v1.0 ships to one real child.

Only the human moves an item from here into `SHIP.md`. Agents may read this file
and may propose additions in their response, but must not edit it to expand
scope.

---

## Cut from v1.0

### Product surface

- **Parent mobile app.** The pairing/presence/device-management backend exists
  (Phase A–C), and `PLATFORM-ARCHITECTURE.txt` specs the app, but no app is
  built and none is needed for v1.0.
- **Payment / subscriptions.** No billing, no tiers, no entitlement checks. Note
  this blocks any tier-gated feature, so build features ungated for now.
- **Admin / parent dashboard — no further work.** `wwwroot/parent.html` and
  `wwwroot/admin.html` already exist and stay as they are. This item means *stop
  investing*, not *remove*. Do not add views, filters, or exports for v1.0.
- **Multi-child profiles.** Per-child mode overrides already exist in the schema;
  a full multi-profile product experience does not, and is not needed.
- **More than the current core modes.** Story, Game, Riddle, Calm and Curiosity
  are the complete set. No sixth mode. See `.claude/MODES.md`.

### Language and voice

- **Voice cloning / custom TTS voice.** OpenAI TTS `tts-1` / `Nova` is the v1.0
  voice. No custom model, no cloned narrator.
- **Western Armenian support.** Eastern Armenian only.

### Hardware

- **Custom PCB.** The ESP32-S3 dev board is the v1.0 hardware.
- **Manufacturing, packaging, Kickstarter.** No enclosure design, no production
  run, no crowdfunding, no retail packaging.

### Operations

- **Server deployment polish beyond what a real ESP test needs.** A backend the
  toy can reach on the bench is sufficient. Explicitly deferred: TLS/domain
  (`docs/ADR-001-deploy-target-and-tls.md`), moving off SQLite, orchestration,
  autoscaling, CDN, and any managed-hosting migration.

---

## Deferred engineering already tracked elsewhere

These are recorded in `CLAUDE.md` with full rationale. Listed here so the backlog
is one place to look.

- **C2.3 orphan audio sweeper.** Trigger conditions are written down in
  `CLAUDE.md` § "Voice chat (C2.3 …)". Not needed pre-launch.
- **Move off SQLite.** The WAL/busy-timeout PRAGMA layer is a documented stopgap.
- **E1.3 modes-used-today.** Needs a persisted `Message.Mode` column and a
  migration.
- **Promote `AREG_STORY_SD_CACHE_FIRST` to default.** Cache-hit and fallback
  B/E/C are hardware-verified, but the flag stays opt-in until deliberately
  promoted.
- **Stage-B OTA TLS.** `ota_apply.cpp`'s `ota_http_begin()` is the single seam;
  Stage A (HTTP LAN bench) is what v1.0 uses.
- **Firmware bench flags.** `AREG_CONTENT_SYNC_BENCH`, `AREG_SD_DIAG_BENCH`,
  `AREG_SD_PLAYBACK_BENCH`, `AREG_STORY_SD_FALLBACK_TEST_BENCH` stay bench-only
  and compile to zero bytes in production.

---

## Tooling considered and not adopted

- **`install-areg-claude.ps1` setup pack** (reviewed 2026-07-26, not installed).
  It overwrites `CLAUDE.md` (2,522 lines → ~70) and `.claude/settings.json`,
  where the latter silently removes the `block-secret-commit.py` hook wiring and
  denies `git push`. It has no dry-run and its completion message overstates its
  backup coverage. Only `SHIP.md` and this file were adopted, by hand.
  Ideas from it worth revisiting later, on their own slices: extracting mode
  prompt text out of `ChatService.cs` into `backend/prompts/<mode>.txt`, and
  adding a `guard.ps1` PreToolUse hook *alongside* — never replacing — the
  existing secret-blocking hook.

---

## How to use this file

1. New idea appears → add it here in one line, with the reason it is not v1.0.
2. Something on `SHIP.md` turns out not to matter → the human moves it here.
3. Something here turns out to block a real child using the toy → the human
   moves it to `SHIP.md`.

Steps 2 and 3 are human-only.

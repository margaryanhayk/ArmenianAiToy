// -------------------------------------------------------------
// AregVoiceMvp / content_sync.h — Cloud→SD story sync (bench slice)
//
// Gated behind AREG_CONTENT_SYNC_BENCH (a build flag, not config.h).
// Production builds compile ZERO bytes of this module and are
// byte-identical.
//
// Flow (one attempt per boot, from the idle loop, when Wi-Fi is up and
// the SD card is mounted):
//   GET /api/devices/content-manifest (device-authed)
//     → stories[]: EVERY item is parsed and validated independently,
//       up to CS_MAX_STORIES (content_sync_rules.h). A backend offering
//       more is truncated with a log line, never a crash.
//   per story:
//     final path   /stories/<storyId>-v<version>.mp3
//     temp path    /tmp/<storyId>-v<version>.mp3.part   (unique per story)
//     already-current: index entry matches (id/version/sha/size, verified)
//       AND the file exists at the recorded size → skipped, no re-download
//       and no re-hash. Without a usable index entry the existing file is
//       streamed through SHA-256 instead (the pre-multi-story behavior).
//     else: chunked download → temp, SHA-256 while streaming, size check,
//       verify BEFORE touching the final path, then atomic rename.
//   /content_index.json is written LAST, once, describing every verified
//   story (schema v2) — see the index contract below.
//
// Partial failure is normal and non-fatal: one bad manifest item, or one
// failed download, never denies the device the stories that ARE valid,
// and NEVER deletes a known-good cached file.
//
// ---- /content_index.json (schema v2) ----
// {
//   "schemaVersion": 2,
//   "stories": [ { storyId, version, title, sha256, sizeBytes,
//                  cachePath, verified }, ... ],
//   "storyId": "...", "version": 1, "sha256": "...",
//   "file": "/stories/...", "sizeBytes": 123
// }
// The four flat fields after "stories" are a LEGACY COMPATIBILITY MIRROR
// of one entry, not a second source of truth.
//
// As of story-select-from-index, ACTIVE PLAYBACK no longer reads them:
// story_select.cpp uses "stories" only, so a stale mirror can never
// override a valid v2 selection. It is RETAINED because two BENCH
// harnesses still parse the flat shape — resolve_path() in
// sd_playback.cpp (AREG_SD_PLAYBACK_BENCH) and Test-E in
// AREG_STORY_SD_FALLBACK_TEST_BENCH, which writes a flat index on
// purpose. Both are hardware-verification tools; dropping the mirror for
// tidiness would break them. The mirror points at the entry whose
// storyId equals AREG_STORY_ID when present, else the first verified
// entry.
//
// A v1 (flat, no "schemaVersion") index on the card is MIGRATED in
// memory, not erased: its entry is carried forward when the MP3 it names
// still exists at the recorded size. A card never has to be wiped.
//
// Deliberately NOT here: story SELECTION (which story to play),
// no-repeat, playback changes, retirement/orphan deletion, eviction,
// download resume, backend changes, OTA changes.
// -------------------------------------------------------------
#pragma once

#ifdef AREG_CONTENT_SYNC_BENCH
// Call every loop() iteration while IDLE. Cheap no-op until Wi-Fi AND the
// SD mount are both up, then runs a sync attempt on its own SCHEDULE.
//
// Not one-shot since 2026-08-14. It used to be: a static latch was set before
// the run and never reset, so a single failed attempt — or a panic — meant the
// toy never asked again until someone power-cycled it. A story added on the
// backend could not reach a running toy at all. Now: ~6 h after a clean pass,
// and 5/15/60/240 min after a failure, with the streak persisted so a
// crash-reboot loop backs off instead of retrying every 3 minutes forever.
void content_sync_tick();

// Ask for an attempt on the next tick (the `refresh_story_manifest` command).
// Honours every existing guard — IDLE-only, Wi-Fi, SD, OTA-pending — so a
// console button can never interrupt a story or race a firmware update.
void content_sync_request_now();

// Last attempt's outcome, for the heartbeat. Bounded vocabulary:
//   "never"   no attempt since boot
//   "ok"      everything the manifest offered is now on the card
//   "partial" some items failed; the rest are current
//   "failed"  the attempt did not get far enough to sync anything
const char *content_sync_status();

// Short bounded reason for the last failure ("" when none). Never free text —
// these are the same fixed strings the serial log prints, e.g. "no_space",
// "sha256_mismatch", "manifest_fetch_failed".
const char *content_sync_error();

// Seconds since the last attempt that finished cleanly, or -1 if there has not
// been one since boot. The backend turns this into an absolute timestamp.
int32_t content_sync_seconds_since_ok();

// Consecutive failed attempts, persisted across reboots. This is what makes a
// crash loop visible: a toy that panics mid-sync reports a climbing streak on
// the next boot even though it never got to report a status at all.
uint16_t content_sync_fail_streak();
#endif

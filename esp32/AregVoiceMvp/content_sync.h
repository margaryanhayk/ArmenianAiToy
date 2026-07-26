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
// of one entry, not a second source of truth. Three readers still parse
// the pre-multi-story flat shape — story_resolve_cache_path() in the
// .ino (the hardware-verified SD-first playback path), resolve_path() in
// sd_playback.cpp, and the Test-E fallback harness — and this slice is
// explicitly not allowed to change playback selection. The mirror points
// at the entry whose storyId equals AREG_STORY_ID when present, else the
// first verified entry, which reproduces the old single-story behavior
// exactly. story-select-from-index owns migrating those readers to
// "stories" and dropping the mirror.
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
// SD mount are both up, then runs exactly ONE sync attempt per boot.
void content_sync_tick();
#endif

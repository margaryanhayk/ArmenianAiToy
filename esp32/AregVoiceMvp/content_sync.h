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
//     → stories[0]: storyId/version/title/audioUrl/sha256/sizeBytes/enabled
//   final path   /stories/<storyId>-v<version>.mp3
//   temp path    /tmp/<storyId>.mp3.part
//   already-cached check: final file streamed through SHA-256 → on match
//     "[content-sync] already cached PASS" (no re-download — the
//     second-boot idempotence proof)
//   else: chunked download → temp, SHA-256 while streaming, size check,
//     atomic rename temp → final, /content_index.json written LAST.
//   Any failure: delete the .part, NEVER touch a previously good final
//     file, log the reason.
//
// Deliberately NOT here: MP3 playback, eviction, multi-story, backend
// changes, OTA changes, resume (fresh .part each attempt).
// -------------------------------------------------------------
#pragma once

#ifdef AREG_CONTENT_SYNC_BENCH
// Call every loop() iteration while IDLE. Cheap no-op until Wi-Fi AND the
// SD mount are both up, then runs exactly ONE sync attempt per boot.
void content_sync_tick();
#endif

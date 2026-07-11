// -------------------------------------------------------------
// AregVoiceMvp / sd_playback.h — cached-MP3 SD playback (bench-only)
//
// Gated behind AREG_SD_PLAYBACK_BENCH (a build flag, not config.h — pass
// -DAREG_SD_PLAYBACK_BENCH via build flags). Production builds compile ZERO
// bytes of this module and are byte-identical.
//
// Purpose: prove the LAST link of the Cloud→SD chain — that a story MP3
// already cached on the SD card (by the content-sync slice) actually plays
// out the MAX98357A speaker through the EXISTING decoder path. It reuses
// audio_play_story_file() (audio_io.cpp) verbatim — the same
// AudioFileSourceSD + AudioGeneratorMP3 + AudioOutputI2S path the offline
// story flow uses. NO second decoder is added.
//
// Flow (runs ONCE per boot, 30 s after power-up so a monitor can attach;
// prints an armed heartbeat until then):
//   1) resolve the file from /content_index.json ("file" field); if the
//      index is missing/invalid, fall back to /stories/anban-huri-v1.mp3
//      and log the fallback.
//   2) ensure the SD card is mounted via audio_sd_begin()/audio_sd_available()
//      → "[sd-playback] sd ok" / "[sd-playback] FAIL sd unavailable".
//   3) verify the file exists + read its size.
//   4) audio_speaker_begin() then audio_play_story_file(path,0,nullptr,nullptr)
//      — the exact sequence the real offline-story callers use.
//   5) "[sd-playback] done ok=true" / "[sd-playback] FAIL <reason>".
//
// Deliberately NOT here: backend downloads, content-sync, OTA, voice
// recording / chat turns, file mutation (the MP3 is never modified/deleted).
// -------------------------------------------------------------
#pragma once

#ifdef AREG_SD_PLAYBACK_BENCH
// Call every loop() iteration while IDLE. Cheap no-op before ms>=30000
// (armed heartbeat every 5 s), then plays the cached MP3 exactly once per
// boot. Playback is synchronous (blocks the idle loop for the clip's
// duration); the reused decoder feeds the task watchdog per frame.
void sd_playback_tick();
#endif

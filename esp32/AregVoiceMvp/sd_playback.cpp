// -------------------------------------------------------------
// AregVoiceMvp / sd_playback.cpp — cached-MP3 SD playback (bench-only)
// See sd_playback.h for the scope statement. Entire file compiles out
// unless AREG_SD_PLAYBACK_BENCH is defined.
// -------------------------------------------------------------
#ifdef AREG_SD_PLAYBACK_BENCH

#include "sd_playback.h"

#include <Arduino.h>
#include <FS.h>
#include <SD.h>
#include <ArduinoJson.h>

#include "config.h"
#include "audio_io.h"  // audio_sd_begin/available/has_file + audio_play_story_file

namespace {

constexpr uint32_t kStartMs       = 30000UL;  // 30 s arm delay (monitor attach)
constexpr uint32_t kStatusEveryMs = 5000UL;
constexpr const char *kIndexPath    = "/content_index.json";
constexpr const char *kFallbackPath = "/stories/anban-huri-v1.mp3";
// A real ~4.6 MB story plays for minutes; a decode/open failure returns
// almost instantly. Anything faster than this is treated as a failure so
// the bench never reports ok=true for silence. (The reused decoder also
// prints its own [story] diagnostics.)
constexpr uint32_t kMinPlausiblePlayMs = 2000UL;

// Resolve the MP3 path from /content_index.json's "file" field. Falls back
// to kFallbackPath (and logs the reason) when the index is absent or does
// not carry a usable absolute path. `out` must be >= 96 bytes.
void resolve_path(char *out, size_t out_len) {
    if (SD.exists(kIndexPath)) {
        File idx = SD.open(kIndexPath, FILE_READ);
        if (idx) {
            JsonDocument doc;
            const DeserializationError err = deserializeJson(doc, idx);
            idx.close();
            if (err == DeserializationError::Ok) {
                const char *file = doc["file"] | "";
                if (file[0] == '/') {
                    snprintf(out, out_len, "%s", file);
                    Serial.printf("[sd-playback] index file=%s\n", out);
                    Serial.flush();
                    return;
                }
                Serial.println("[sd-playback] index has no usable 'file' — fallback");
            } else {
                Serial.println("[sd-playback] index parse failed — fallback");
            }
        } else {
            Serial.println("[sd-playback] index open failed — fallback");
        }
    } else {
        Serial.println("[sd-playback] no /content_index.json — fallback");
    }
    snprintf(out, out_len, "%s", kFallbackPath);
    Serial.printf("[sd-playback] fallback file=%s\n", out);
    Serial.flush();
}

void fail(const char *reason) {
    Serial.printf("[sd-playback] FAIL %s\n", reason);
    Serial.flush();
}

// The one playback attempt. Reuses audio_play_story_file() — NO new decoder.
void sd_playback_run() {
    // ---- 1. Ensure SD is mounted FIRST (reuse the boot-mount helper) ----
    // Must precede the index read below — resolve_path() reads a file off
    // the card, so the mount has to be up before it can find the index.
    if (!audio_sd_available()) {
        audio_sd_begin();  // idempotent; prints its own "[sd] ..." reason line
    }
    if (!audio_sd_available()) {
        fail("sd unavailable");
        return;
    }
    Serial.println("[sd-playback] sd ok");
    Serial.flush();

    // ---- 2. Resolve the file (index → fallback), now that SD is up ----
    char path[96];
    resolve_path(path, sizeof(path));

    // ---- 3. Verify the file exists + read size ----
    if (!audio_sd_has_file(path)) {
        Serial.printf("[sd-playback] opening %s\n", path);
        Serial.flush();
        fail("file not found");
        return;
    }
    Serial.printf("[sd-playback] opening %s\n", path);
    Serial.flush();
    uint32_t file_size = 0;
    {
        File f = SD.open(path, FILE_READ);
        if (!f) {
            fail("file open failed");
            return;
        }
        file_size = (uint32_t)f.size();
        f.close();
    }
    Serial.printf("[sd-playback] file size=%u\n", (unsigned)file_size);
    Serial.flush();
    if (file_size == 0) {
        fail("file empty");
        return;
    }

    // ---- 4. Play through the EXISTING I2S/MP3 path ----
    // Same sequence the offline-story callers use: audio_speaker_begin()
    // (a no-op seam) then audio_play_story_file(). barge_in=nullptr so it
    // plays to the natural end; the decoder feeds the WDT per frame.
    Serial.println("[sd-playback] playing");
    Serial.flush();
    audio_speaker_begin();
    const uint32_t t0 = millis();
    const bool interrupted =
        audio_play_story_file(path, 0, nullptr, nullptr);
    const uint32_t elapsed = millis() - t0;

    // ---- 5. Verdict ----
    // barge_in was nullptr, so `interrupted` must be false on a real play.
    // A too-fast return means the reused path bailed (open/decoder error);
    // its own [story] line carries the specifics.
    if (interrupted) {
        fail("unexpected barge-in");
        return;
    }
    if (elapsed < kMinPlausiblePlayMs) {
        Serial.printf("[sd-playback] playback returned in %ums (too fast)\n",
                      (unsigned)elapsed);
        Serial.flush();
        fail("playback did not run (decode/open error)");
        return;
    }
    Serial.printf("[sd-playback] done ok=true (%ums)\n", (unsigned)elapsed);
    Serial.flush();
}

}  // namespace

void sd_playback_tick() {
    static bool s_done = false;
    static bool s_stamped = false;
    static uint32_t s_last_status_ms = 0;
    if (s_done) {
        return;
    }

    if (!s_stamped) {
        // One-time build stamp so the serial log itself proves WHICH bench
        // image is running.
        s_stamped = true;
        Serial.println("[sd-playback] bench fw built " __DATE__ " " __TIME__);
        Serial.flush();
    }

    const uint32_t now = millis();
    if (now < kStartMs) {
        if (s_last_status_ms == 0 || now - s_last_status_ms >= kStatusEveryMs) {
            s_last_status_ms = now;
            Serial.println("[sd-playback] armed; will start at ms>=30000");
            Serial.flush();
        }
        return;
    }

    s_done = true;  // exactly one playback per boot
    sd_playback_run();
}

#endif  // AREG_SD_PLAYBACK_BENCH

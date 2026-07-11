// -------------------------------------------------------------
// AregVoiceMvp / sd_diag.cpp — standalone microSD DIAGNOSTIC (bench-only)
// See sd_diag.h for the scope statement. Entire file compiles out
// unless AREG_SD_DIAG_BENCH is defined.
// -------------------------------------------------------------
#ifdef AREG_SD_DIAG_BENCH

#include "sd_diag.h"

#include <Arduino.h>
#include <SPI.h>
#include <FS.h>
#include <SD.h>

#include "config.h"    // AREG_PIN_SD_* — same pins every SD caller uses
#include "audio_io.h"  // audio_sd_begin() — the helper path under test

namespace {

constexpr uint32_t kDiagStartMs  = 20000UL;  // 20 s arm delay (monitor attach)
constexpr uint32_t kRetryEveryMs = 30000UL;  // re-run every 30 s until a pass
constexpr uint32_t kStatusEveryMs = 5000UL;
constexpr const char *kTestPath  = "/sd_diag.txt";
constexpr int kMaxRootEntries    = 32;

const char *card_type_name(sdcard_type_t t) {
    switch (t) {
        case CARD_MMC:  return "MMC";
        case CARD_SD:   return "SDSC";
        case CARD_SDHC: return "SDHC/SDXC";
        case CARD_NONE: return "NONE";
        default:        return "UNKNOWN";
    }
}

// Card identity + root listing + create/write/read-back/delete of
// kTestPath. Only called after some SD.begin succeeded.
void sd_diag_inspect_and_readwrite() {
    Serial.printf("[sd-diag] card type=%s\n", card_type_name(SD.cardType()));
    Serial.printf("[sd-diag] card size=%lluMB fat_total=%lluMB fat_used=%lluMB\n",
                  (unsigned long long)(SD.cardSize() / (1024ULL * 1024ULL)),
                  (unsigned long long)(SD.totalBytes() / (1024ULL * 1024ULL)),
                  (unsigned long long)(SD.usedBytes() / (1024ULL * 1024ULL)));
    Serial.flush();

    File root = SD.open("/");
    if (root) {
        Serial.println("[sd-diag] root listing:");
        int count = 0;
        File entry = root.openNextFile();
        while (entry && count < kMaxRootEntries) {
            Serial.printf("[sd-diag]   %s%s (%u bytes)\n",
                          entry.isDirectory() ? "<dir> " : "",
                          entry.name(), (unsigned)entry.size());
            entry = root.openNextFile();
            count++;
        }
        if (count == 0) {
            Serial.println("[sd-diag]   (empty)");
        } else if (entry) {
            Serial.println("[sd-diag]   ... (listing capped)");
        }
        root.close();
    } else {
        Serial.println("[sd-diag] root listing open failed");
    }
    Serial.flush();

    // Payload unique to this boot so a stale file can never fake a pass.
    char payload[64];
    snprintf(payload, sizeof(payload), "areg sd diag millis=%lu",
             (unsigned long)millis());

    {
        File f = SD.open(kTestPath, FILE_WRITE);
        if (!f) {
            Serial.println("[sd-diag] readwrite FAIL (open for write failed)");
            Serial.flush();
            return;
        }
        const size_t wrote = f.print(payload);
        f.close();
        if (wrote != strlen(payload)) {
            Serial.println("[sd-diag] readwrite FAIL (short write)");
            Serial.flush();
            return;
        }
        Serial.printf("[sd-diag] wrote %u bytes to %s\n", (unsigned)wrote, kTestPath);
    }

    {
        File f = SD.open(kTestPath, FILE_READ);
        if (!f) {
            Serial.println("[sd-diag] readwrite FAIL (open for read failed)");
            Serial.flush();
            return;
        }
        char readback[64] = {0};
        const int got = f.readBytes(readback, sizeof(readback) - 1);
        f.close();
        if ((size_t)got != strlen(payload) || strcmp(readback, payload) != 0) {
            Serial.printf("[sd-diag] readwrite FAIL (read-back mismatch: \"%s\")\n",
                          readback);
            Serial.flush();
            return;
        }
        Serial.printf("[sd-diag] read back verified: \"%s\"\n", readback);
    }

    if (!SD.remove(kTestPath)) {
        Serial.println("[sd-diag] readwrite FAIL (delete failed)");
        Serial.flush();
        return;
    }
    Serial.printf("[sd-diag] deleted %s\n", kTestPath);
    Serial.println("[sd-diag] readwrite PASS");
    Serial.flush();
}

// Runs the full SD diagnostic once. Returns true iff some SD.begin
// mounted the card (helper path or raw sweep). The caller owns the
// per-attempt "attempt N start" / "attempt N FAIL" / "PASS" lines so the
// attempt counter stays in one place.
bool sd_diag_run() {
    Serial.printf("[sd-diag] pins cs=%d sck=%d mosi=%d miso=%d\n",
                  AREG_PIN_SD_CS, AREG_PIN_SD_SCK,
                  AREG_PIN_SD_MOSI, AREG_PIN_SD_MISO);
    Serial.flush();

    bool mounted = false;

    // ---- A) Existing helper path (what content-sync gates on) ----
    Serial.println("[sd-diag] audio_sd_begin start");
    Serial.flush();
    if (audio_sd_begin()) {
        Serial.println("[sd-diag] audio_sd_begin ok");
        mounted = true;
    } else {
        Serial.println("[sd-diag] audio_sd_begin failed");
    }
    Serial.flush();

    // ---- B) Raw SPI/SD path, same pins, ascending speeds ----
    if (mounted) {
        Serial.println("[sd-diag] raw speed sweep skipped (helper path ok)");
        Serial.flush();
    } else {
        static const uint32_t kSpeeds[] =
            {400000U, 1000000U, 4000000U, 10000000U};
        SPI.begin(AREG_PIN_SD_SCK, AREG_PIN_SD_MISO, AREG_PIN_SD_MOSI,
                  AREG_PIN_SD_CS);
        for (size_t i = 0; i < sizeof(kSpeeds) / sizeof(kSpeeds[0]); i++) {
            const uint32_t speed = kSpeeds[i];
            SD.end();  // reset library state between attempts
            Serial.printf("[sd-diag] raw SD.begin speed=%lu start\n",
                          (unsigned long)speed);
            Serial.flush();
            if (SD.begin(AREG_PIN_SD_CS, SPI, speed)) {
                Serial.printf("[sd-diag] raw SD.begin speed=%lu ok\n",
                              (unsigned long)speed);
                Serial.flush();
                mounted = true;
                break;
            }
            Serial.printf("[sd-diag] raw SD.begin speed=%lu failed\n",
                          (unsigned long)speed);
            Serial.flush();
        }
    }

    if (!mounted) {
        return false;
    }

    sd_diag_inspect_and_readwrite();
    return true;
}

}  // namespace

void sd_diag_tick() {
    static bool s_passed = false;       // set once an attempt mounts the card
    static bool s_stamped = false;      // build stamp printed exactly once
    static uint32_t s_attempt = 0;      // 1-based attempt counter
    static uint32_t s_last_status_ms = 0;
    static uint32_t s_next_run_ms = kDiagStartMs;

    if (s_passed) {
        return;  // stop repeating once SD mounted
    }

    const uint32_t now = millis();
    if (now < s_next_run_ms) {
        // Armed heartbeat only before the very first attempt, so a monitor
        // that attaches during the initial 20 s arm window sees the arm.
        if (s_attempt == 0 &&
            (s_last_status_ms == 0 || now - s_last_status_ms >= kStatusEveryMs)) {
            s_last_status_ms = now;
            Serial.println("[sd-diag] armed; first run at ms>=20000, then every 30s until pass");
            Serial.flush();
        }
        return;
    }

    if (!s_stamped) {
        s_stamped = true;
        Serial.println("[sd-diag] bench fw built " __DATE__ " " __TIME__);
        Serial.flush();
    }

    s_attempt++;
    Serial.printf("[sd-diag] attempt %lu start\n", (unsigned long)s_attempt);
    Serial.flush();

    if (sd_diag_run()) {
        Serial.println("[sd-diag] PASS");
        Serial.flush();
        s_passed = true;  // one success ends the repeat
        return;
    }

    Serial.printf("[sd-diag] attempt %lu FAIL all SD.begin attempts failed\n",
                  (unsigned long)s_attempt);
    Serial.flush();
    s_next_run_ms = now + kRetryEveryMs;  // retry in 30 s
}

#endif  // AREG_SD_DIAG_BENCH

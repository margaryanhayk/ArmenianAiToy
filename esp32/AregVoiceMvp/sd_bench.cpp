// -------------------------------------------------------------
// AregVoiceMvp / sd_bench.cpp — microSD HARDWARE PROOF (bench-only)
// See sd_bench.h for scope. Entire file is compiled out unless
// AREG_SD_BENCH_TEST is defined.
// -------------------------------------------------------------
#ifdef AREG_SD_BENCH_TEST

#include "sd_bench.h"

#include <Arduino.h>
#include <FS.h>
#include <SD.h>

#include "audio_io.h"        // audio_sd_available() — reuse the boot mount
#include "ota_foundation.h"  // AREG_FW_VERSION for the payload line

namespace {

constexpr const char *kTestPath = "/areg_sd_test.txt";

const char *card_type_name(sdcard_type_t t) {
    switch (t) {
        case CARD_MMC:  return "MMC";
        case CARD_SD:   return "SDSC";
        case CARD_SDHC: return "SDHC/SDXC";
        case CARD_NONE: return "NONE";
        default:        return "UNKNOWN";
    }
}

void fail(const char *reason) {
    Serial.printf("[sd-bench] FAIL (%s)\n", reason);
    Serial.flush();
}

}  // namespace

void sd_bench_run() {
    Serial.println("[sd-bench] starting (write + read-back proof)");
    Serial.flush();

    // 1. Card must already be mounted by audio_sd_begin() at boot.
    if (!audio_sd_available()) {
        fail("card not mounted — check wiring/format, see [sd] boot lines");
        return;
    }

    // 2. Identity: type + sizes. cardSize is the raw card capacity;
    // totalBytes/usedBytes describe the mounted FAT volume.
    Serial.printf("[sd-bench] card type=%s size=%lluMB fat_total=%lluMB fat_used=%lluMB\n",
                  card_type_name(SD.cardType()),
                  (unsigned long long)(SD.cardSize() / (1024ULL * 1024ULL)),
                  (unsigned long long)(SD.totalBytes() / (1024ULL * 1024ULL)),
                  (unsigned long long)(SD.usedBytes() / (1024ULL * 1024ULL)));
    Serial.flush();

    // 3. Compose a payload unique to this boot so a stale file from an
    // earlier run can never fake a pass.
    char payload[96];
    snprintf(payload, sizeof(payload),
             "areg sd bench fw=%s millis=%lu", AREG_FW_VERSION,
             (unsigned long)millis());

    // 4. Write (FILE_WRITE truncates on ESP32 — a fresh file every run).
    {
        File f = SD.open(kTestPath, FILE_WRITE);
        if (!f) {
            fail("open for write failed");
            return;
        }
        const size_t wrote = f.print(payload);
        f.close();
        if (wrote != strlen(payload)) {
            fail("short write");
            return;
        }
        Serial.printf("[sd-bench] wrote %u bytes to %s\n",
                      (unsigned)wrote, kTestPath);
        Serial.flush();
    }

    // 5. Read back and verify byte-for-byte.
    {
        File f = SD.open(kTestPath, FILE_READ);
        if (!f) {
            fail("open for read failed");
            return;
        }
        char readback[96] = {0};
        const size_t expected = strlen(payload);
        const int got = f.readBytes(readback, sizeof(readback) - 1);
        const size_t fsize = f.size();
        f.close();
        if ((size_t)got != expected || fsize != expected) {
            Serial.printf("[sd-bench] size mismatch (wrote=%u read=%d fsize=%u)\n",
                          (unsigned)expected, got, (unsigned)fsize);
            fail("read-back size mismatch");
            return;
        }
        if (strcmp(readback, payload) != 0) {
            Serial.printf("[sd-bench] content mismatch (read: %s)\n", readback);
            fail("read-back content mismatch");
            return;
        }
        Serial.printf("[sd-bench] read back %d bytes, content verified: \"%s\"\n",
                      got, readback);
    }

    // Test file is deliberately LEFT on the card — mount it on a PC and
    // /areg_sd_test.txt is physical evidence of the write.
    Serial.println("[sd-bench] PASS — mount, identify, write, read-back all OK");
    Serial.flush();
}

#endif  // AREG_SD_BENCH_TEST

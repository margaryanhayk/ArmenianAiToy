// -------------------------------------------------------------
// AregVoiceMvp / content_index_io.cpp - see content_index_io.h.
// -------------------------------------------------------------
#include "content_index_io.h"

#include <Arduino.h>
#include <FS.h>
#include <SD.h>
#include <esp_task_wdt.h>

#include "audio_io.h"            // audio_sd_available()
#include "content_sync_rules.h"  // cs_index_next_step and its constants

namespace {

constexpr const char *kPrimary = "/content_index.json";
constexpr const char *kBackup  = "/content_index.json.prev";

// 0 = no memo. Otherwise: do not try again before this millis() value.
uint32_t s_dead_until_ms = 0;

enum ReadResult { READ_OK, READ_OPEN_FAILED, READ_PARSE_FAILED };

ReadResult read_one(const char *path, JsonDocument &doc, DeserializationError &err) {
    File f = SD.open(path, FILE_READ);
    if (!f) {
        return READ_OPEN_FAILED;
    }
    // A HEALTHY card parses this in milliseconds; a DYING one (2026-08-19:
    // flaky SPI, IncompleteInput, then no mount at all) makes every read a
    // multi-second timeout. Feed the watchdog on both sides of the read.
    esp_task_wdt_reset();
    doc.clear();               // a failed attempt leaves a partial document behind
    err = deserializeJson(doc, f);
    f.close();
    esp_task_wdt_reset();
    return err == DeserializationError::Ok ? READ_OK : READ_PARSE_FAILED;
}

void settle() {
    esp_task_wdt_reset();
    delay(CS_INDEX_RETRY_GAP_MS);
    esp_task_wdt_reset();
}

}  // namespace

ContentIndexStatus content_index_load(JsonDocument &doc, const char *who) {
    if (!audio_sd_available()) {
        return CONTENT_INDEX_ABSENT;
    }
    const bool primary_exists = SD.exists(kPrimary);
    const bool backup_exists  = SD.exists(kBackup);
    if (!primary_exists && !backup_exists) {
        return CONTENT_INDEX_ABSENT;
    }
    if (s_dead_until_ms != 0 && (int32_t)(millis() - s_dead_until_ms) < 0) {
        return CONTENT_INDEX_UNREADABLE;   // a caller just paid for this; see CS_INDEX_DEAD_MEMO_MS
    }

    int primary_failures = 0;
    int backup_failures = 0;
    const char *why = "";
    DeserializationError err = DeserializationError::Ok;

    for (;;) {
        const CsIndexStep step = cs_index_next_step(primary_exists, primary_failures,
                                                    backup_exists, backup_failures);
        if (step == CS_INDEX_STEP_READ_PRIMARY) {
            const ReadResult r = read_one(kPrimary, doc, err);
            if (r == READ_OK) {
                if (primary_failures > 0) {
                    Serial.printf("[%s] index read recovered on attempt %d\n",
                                  who, primary_failures + 1);
                    Serial.flush();
                }
                s_dead_until_ms = 0;
                return CONTENT_INDEX_OK;
            }
            why = (r == READ_OPEN_FAILED) ? "open failed" : err.c_str();
            primary_failures++;
            if (primary_failures < CS_INDEX_READ_TRIES) {
                settle();
            }
        } else if (step == CS_INDEX_STEP_READ_BACKUP) {
            const ReadResult r = read_one(kBackup, doc, err);
            if (r == READ_OK) {
                Serial.printf("[%s] index: using the previous copy (primary %s)\n",
                              who, primary_exists ? "unreadable" : "missing");
                Serial.flush();
                s_dead_until_ms = 0;
                return CONTENT_INDEX_OK_FROM_BACKUP;
            }
            why = (r == READ_OPEN_FAILED) ? "open failed" : err.c_str();
            backup_failures++;
            if (backup_failures < CS_INDEX_READ_TRIES) {
                settle();
            }
        } else {
            // ABSENT cannot happen here (checked above); this is UNREADABLE.
            doc.clear();
            // Keep "index parse failed" verbatim: tools/firmware/bench_watch.py
            // flags that phrase as an R9 problem.
            Serial.printf("[%s] index parse failed: %s (heap=%u psram=%u)\n",
                          who, why,
                          (unsigned)ESP.getFreeHeap(), (unsigned)ESP.getFreePsram());
            Serial.flush();
            s_dead_until_ms = millis() + CS_INDEX_DEAD_MEMO_MS;
            if (s_dead_until_ms == 0) {
                s_dead_until_ms = 1;
            }
            return CONTENT_INDEX_UNREADABLE;
        }
    }
}

bool content_index_repair_from_backup() {
    if (!SD.exists(kBackup)) {
        return false;
    }
    if (SD.exists(kPrimary) && !SD.remove(kPrimary)) {
        return false;
    }
    return SD.rename(kBackup, kPrimary);
}

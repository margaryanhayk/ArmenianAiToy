// -------------------------------------------------------------
// AregVoiceMvp / content_sync.cpp — Cloud→SD story sync (bench slice)
// See content_sync.h for the scope statement. Entire file compiles out
// unless AREG_CONTENT_SYNC_BENCH is defined.
// -------------------------------------------------------------
#ifdef AREG_CONTENT_SYNC_BENCH

#include "content_sync.h"

#include <Arduino.h>
#include <WiFi.h>
#include <HTTPClient.h>
#include <ArduinoJson.h>
#include <FS.h>
#include <SD.h>
#include <esp_task_wdt.h>
#include <mbedtls/sha256.h>

#include "config.h"
#include "audio_io.h"      // audio_sd_available() — reuse the boot mount
#include "voice_client.h"  // voice_wifi_is_connected / voice_add_device_auth_headers

#ifndef AREG_HTTP_CONNECT_MS
#define AREG_HTTP_CONNECT_MS 5000
#endif
#ifndef AREG_HTTP_READ_MS
#define AREG_HTTP_READ_MS 30000
#endif

namespace {

constexpr const char *kIndexPath = "/content_index.json";
// Free-space slack beyond sizeBytes (FAT allocation overhead headroom).
constexpr uint64_t kFreeSpaceSlack = 256ULL * 1024ULL;

// ---- small helpers (duplicated from ota_apply's file-locals by repo
// convention: no shared util until a third caller) ----

void to_hex_lower(const uint8_t *in, size_t len, char *out /* 2*len+1 */) {
    static const char *kHex = "0123456789abcdef";
    for (size_t i = 0; i < len; i++) {
        out[2 * i]     = kHex[in[i] >> 4];
        out[2 * i + 1] = kHex[in[i] & 0x0F];
    }
    out[2 * len] = '\0';
}

bool hex_equals_ci(const char *a, const char *b) {
    const size_t la = strlen(a), lb = strlen(b);
    if (la != lb || la == 0) return false;
    uint8_t diff = 0;
    for (size_t i = 0; i < la; i++) {
        diff |= (uint8_t)(tolower((unsigned char)a[i]) ^ tolower((unsigned char)b[i]));
    }
    return diff == 0;
}

// Resolve a manifest URL: absolute http(s) passes through; a bare path is
// resolved against the backend base derived from AREG_BACKEND_URL.
String resolve_url(const char *url) {
    if (strncmp(url, "http://", 7) == 0 || strncmp(url, "https://", 8) == 0) {
        return String(url);
    }
    String base = AREG_BACKEND_URL;
    base.replace("/api/chat/audio", "");
    return base + url;
}

void fail(const char *reason) {
    Serial.printf("[content-sync] FAIL (%s)\n", reason);
    Serial.flush();
}

// Streaming SHA-256 of an existing SD file. Returns false when the file
// can't be opened. Feeds the task watchdog per chunk (a 4.6 MB file at
// SPI speed takes a few seconds).
bool sha256_of_file(const char *path, char *out_hex /* 65 */, size_t *out_size) {
    File f = SD.open(path, FILE_READ);
    if (!f) {
        return false;
    }
    mbedtls_sha256_context sha;
    mbedtls_sha256_init(&sha);
    mbedtls_sha256_starts(&sha, 0);
    static uint8_t buf[4096];
    size_t total = 0;
    while (true) {
        esp_task_wdt_reset();
        const int n = f.read(buf, sizeof(buf));
        if (n <= 0) break;
        mbedtls_sha256_update(&sha, buf, (size_t)n);
        total += (size_t)n;
    }
    f.close();
    uint8_t digest[32];
    mbedtls_sha256_finish(&sha, digest);
    mbedtls_sha256_free(&sha);
    to_hex_lower(digest, sizeof(digest), out_hex);
    if (out_size != nullptr) *out_size = total;
    return true;
}

void ensure_dir(const char *dir) {
    if (!SD.exists(dir)) {
        SD.mkdir(dir);
    }
}

// The one sync attempt. All [content-sync] serial lines here are the
// bench PASS/FAIL evidence contract — keep them stable.
void content_sync_run() {
    Serial.println("[content-sync] starting");
    Serial.flush();

    // ---- 1. Manifest ----
    HTTPClient http;
    if (!http.begin(resolve_url("/api/devices/content-manifest"))) {
        fail("manifest_begin_failed");
        return;
    }
    voice_add_device_auth_headers(http);
    http.setConnectTimeout(AREG_HTTP_CONNECT_MS);
    http.setTimeout(AREG_HTTP_READ_MS);
    const int mstatus = http.GET();
    if (mstatus != 200) {
        http.end();
        Serial.printf("[content-sync] manifest status=%d\n", mstatus);
        fail("manifest_fetch_failed");
        return;
    }
    JsonDocument doc;
    const DeserializationError jerr = deserializeJson(doc, http.getString());
    http.end();
    if (jerr != DeserializationError::Ok) {
        fail("manifest_parse_failed");
        return;
    }
    JsonArray stories = doc["stories"].as<JsonArray>();
    Serial.printf("[content-sync] manifest status=200 stories=%u\n",
                  stories.isNull() ? 0U : (unsigned)stories.size());
    Serial.flush();
    if (stories.isNull() || stories.size() == 0) {
        Serial.println("[content-sync] no content");
        Serial.flush();
        return;
    }

    JsonObject item = stories[0];
    const char *story_id  = item["storyId"]  | "";
    const int   version   = item["version"]  | 1;
    const char *title     = item["title"]    | "";
    const char *audio_url = item["audioUrl"] | "";
    const char *sha256    = item["sha256"]   | "";
    const long  size      = item["sizeBytes"] | 0L;
    const bool  enabled   = item["enabled"]  | false;
    if (story_id[0] == '\0' || audio_url[0] == '\0' || strlen(sha256) != 64 || size <= 0) {
        fail("manifest_item_invalid");
        return;
    }
    if (!enabled) {
        // Retirement handling (deleting a cached copy) is a later slice.
        Serial.printf("[content-sync] item %s disabled — skip\n", story_id);
        Serial.flush();
        return;
    }
    Serial.printf("[content-sync] item %s v%d \"%s\" %ld bytes\n",
                  story_id, version, title, size);
    Serial.flush();

    char final_path[96];
    char temp_path[96];
    snprintf(final_path, sizeof(final_path), "/stories/%s-v%d.mp3", story_id, version);
    snprintf(temp_path, sizeof(temp_path), "/tmp/%s.mp3.part", story_id);

    // ---- 2. Already-cached check (the second-boot idempotence proof) ----
    if (SD.exists(final_path)) {
        char existing_hex[65];
        size_t existing_size = 0;
        if (sha256_of_file(final_path, existing_hex, &existing_size)
            && existing_size == (size_t)size
            && hex_equals_ci(existing_hex, sha256)) {
            Serial.println("[content-sync] already cached PASS");
            Serial.flush();
            return;
        }
        // Exists but wrong (stale version content / corruption): keep it in
        // place — it is only replaced AFTER a fully verified download.
        Serial.printf("[content-sync] cached copy stale/corrupt — re-downloading\n");
        Serial.flush();
    }

    // ---- 3. Preconditions for the download ----
    ensure_dir("/tmp");
    ensure_dir("/stories");
    const uint64_t free_bytes = SD.totalBytes() - SD.usedBytes();
    if (free_bytes < (uint64_t)size + kFreeSpaceSlack) {
        fail("no_space");
        return;
    }
    if (SD.exists(temp_path)) {
        SD.remove(temp_path);  // no resume in this slice — fresh .part
    }

    // ---- 4. Chunked download → temp, SHA-256 while streaming ----
    HTTPClient dl;
    if (!dl.begin(resolve_url(audio_url))) {
        fail("download_begin_failed");
        return;
    }
    voice_add_device_auth_headers(dl);
    dl.setConnectTimeout(AREG_HTTP_CONNECT_MS);
    dl.setTimeout(AREG_HTTP_READ_MS);
    const int dstatus = dl.GET();
    if (dstatus != 200) {
        dl.end();
        Serial.printf("[content-sync] download status=%d\n", dstatus);
        fail("download_failed");
        return;
    }

    File out = SD.open(temp_path, FILE_WRITE);
    if (!out) {
        dl.end();
        fail("temp_open_failed");
        return;
    }

    mbedtls_sha256_context sha;
    mbedtls_sha256_init(&sha);
    mbedtls_sha256_starts(&sha, 0);

    WiFiClient *stream = dl.getStreamPtr();
    static uint8_t buf[4096];
    long received = 0;
    int last_pct = -1;
    uint32_t last_data_ms = millis();
    bool io_ok = true;
    const char *io_err = "download_incomplete";

    while (received < size) {
        esp_task_wdt_reset();
        const size_t avail = stream->available();
        if (avail == 0) {
            if (!dl.connected() && stream->available() == 0) {
                io_ok = false;
                io_err = "connection_closed_early";
                break;
            }
            if (millis() - last_data_ms > (uint32_t)AREG_HTTP_READ_MS) {
                io_ok = false;
                io_err = "download_stalled";
                break;
            }
            delay(2);
            continue;
        }
        const size_t want = (avail < sizeof(buf)) ? avail : sizeof(buf);
        const size_t cap = (size_t)(size - received) < want
                               ? (size_t)(size - received) : want;
        const int n = stream->read(buf, cap);
        if (n <= 0) {
            delay(2);
            continue;
        }
        last_data_ms = millis();
        mbedtls_sha256_update(&sha, buf, (size_t)n);
        if (out.write(buf, (size_t)n) != (size_t)n) {
            io_ok = false;
            io_err = "sd_write_failed";
            break;
        }
        received += n;
        const int pct = (int)((received * 100) / size);
        if (pct / 10 != last_pct / 10) {
            last_pct = pct;
            Serial.printf("[content-sync] download %d%% (%ld/%ld)\n", pct, received, size);
        }
    }
    dl.end();
    out.close();

    uint8_t digest[32];
    mbedtls_sha256_finish(&sha, digest);
    mbedtls_sha256_free(&sha);

    if (!io_ok || received != size) {
        SD.remove(temp_path);  // failed download never lingers
        fail(io_err);
        return;
    }

    // ---- 5. Verify SHA-256 BEFORE touching the final path ----
    char actual_hex[65];
    to_hex_lower(digest, sizeof(digest), actual_hex);
    if (!hex_equals_ci(actual_hex, sha256)) {
        SD.remove(temp_path);
        Serial.printf("[content-sync] sha256 mismatch (got %s)\n", actual_hex);
        fail("sha256_mismatch");
        return;
    }
    Serial.println("[content-sync] sha256 ok");
    Serial.flush();

    // ---- 6. Atomic move into place (only now may an old copy go) ----
    if (SD.exists(final_path)) {
        SD.remove(final_path);  // FAT rename cannot overwrite in place
    }
    if (!SD.rename(temp_path, final_path)) {
        SD.remove(temp_path);
        fail("rename_failed");
        return;
    }
    Serial.printf("[content-sync] moved %s -> %s\n", temp_path, final_path);
    Serial.flush();

    // ---- 7. Index written LAST (crash before this = re-verify next boot) ----
    {
        JsonDocument idx;
        idx["storyId"]   = story_id;
        idx["version"]   = version;
        idx["sha256"]    = sha256;
        idx["file"]      = final_path;
        idx["sizeBytes"] = size;
        File f = SD.open(kIndexPath, FILE_WRITE);
        if (!f) {
            fail("index_write_failed");
            return;
        }
        serializeJson(idx, f);
        f.close();
        Serial.println("[content-sync] index written");
        Serial.flush();
    }

    Serial.println("[content-sync] PASS");
    Serial.flush();
}

}  // namespace

// Bench observability: hold the one-shot until ms >= kBenchStartMs so the
// operator has time to attach the serial monitor after an upload (the
// first bench run fired inside the upload→monitor gap and was never
// seen; RST replays are not an option on this bench — EN resets drop
// COM7). Until then, a heartbeat line prints every few seconds so a
// silent monitor is immediately distinguishable from a wrong build.
static constexpr uint32_t kBenchStartMs   = 180000UL;  // 3 min arm delay
static constexpr uint32_t kStatusEveryMs  = 5000UL;
static constexpr uint32_t kSdInitRetryMs  = 30000UL;   // remount retry cadence

void content_sync_tick() {
    static bool s_done = false;
    static bool s_stamped = false;
    static uint32_t s_last_status_ms = 0;
    static uint32_t s_last_sd_init_ms = 0;
    if (s_done) {
        return;
    }

    if (!s_stamped) {
        // One-time build stamp so the serial log itself proves WHICH bench
        // image is running (a stale flash was mistaken for missing code once).
        s_stamped = true;
        Serial.println("[content-sync] bench fw built " __DATE__ " " __TIME__);
        Serial.flush();
    }

    const uint32_t now = millis();
    const bool status_due =
        (s_last_status_ms == 0) || (now - s_last_status_ms >= kStatusEveryMs);

    if (now < kBenchStartMs) {
        if (status_due) {
            s_last_status_ms = now;
            Serial.println("[content-sync] bench build enabled; waiting for "
                           "WiFi+SD; will start at ms>=180000");
            Serial.flush();
        }
        return;
    }
    if (!voice_wifi_is_connected()) {
        if (status_due) {
            s_last_status_ms = now;
            Serial.println("[content-sync] waiting wifi");
            Serial.flush();
        }
        return;
    }

    if (!audio_sd_available()) {
        // Explicit ACTIVE remount, printed BEFORE the availability re-check.
        // audio_sd_begin() (audio_io.cpp) is the exact helper the SD bench
        // proof used — idempotent and retryable; it prints its own
        // "[sd] mounted ..." / "[sd] SD.begin failed ..." reason line between
        // our start/ok/failed markers. Retried every kSdInitRetryMs so a
        // reseated card recovers WITHOUT an RST (RST drops COM7 on this
        // bench); the boot-time mount result was never visible on a monitor
        // attached post-upload.
        const bool init_due =
            (s_last_sd_init_ms == 0) || (now - s_last_sd_init_ms >= kSdInitRetryMs);
        if (init_due) {
            s_last_sd_init_ms = now;
            Serial.printf("[content-sync] sd init start cs=%d sck=%d mosi=%d miso=%d\n",
                          AREG_PIN_SD_CS, AREG_PIN_SD_SCK,
                          AREG_PIN_SD_MOSI, AREG_PIN_SD_MISO);
            Serial.flush();
            if (audio_sd_begin()) {
                Serial.println("[content-sync] sd init ok");
                Serial.flush();
                // Fall through — audio_sd_available() is true now; the sync
                // starts in THIS tick.
            } else {
                Serial.println("[content-sync] sd init failed");
                Serial.flush();
            }
        }
        if (!audio_sd_available()) {
            if (status_due) {
                s_last_status_ms = now;
                Serial.println("[content-sync] waiting sd");
                Serial.flush();
            }
            return;
        }
    }

    s_done = true;  // one attempt per boot, even if it fails (bench slice)
    content_sync_run();
}

#endif  // AREG_CONTENT_SYNC_BENCH

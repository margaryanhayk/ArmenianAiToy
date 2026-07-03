// -------------------------------------------------------------
// AregVoiceMvp / ota_apply.cpp — real OTA apply (download → inactive slot)
// See ota_apply.h for the pipeline contract and scope statement.
// -------------------------------------------------------------
#include "ota_apply.h"

#include <WiFi.h>        // WiFiClient (HTTPClient::getStreamPtr)
#include <HTTPClient.h>
#include <ArduinoJson.h>
#include <Update.h>
#include <esp_task_wdt.h>
#include <mbedtls/sha256.h>
#include <mbedtls/md.h>

#include "config.h"
#include "ota_foundation.h"  // AREG_FW_VERSION / AREG_BOARD_MODEL
#include "ota_state.h"
#include "voice_client.h"    // voice_add_device_auth_headers

#ifndef AREG_HTTP_CONNECT_MS
#define AREG_HTTP_CONNECT_MS 5000
#endif
#ifndef AREG_HTTP_READ_MS
#define AREG_HTTP_READ_MS 30000
#endif

namespace {

// ---------- small helpers ----------

void set_err(char *err_out, size_t err_cap, const char *err) {
    if (err_out != nullptr && err_cap > 0) {
        snprintf(err_out, err_cap, "%s", err);
    }
}

// Tolerant MAJOR.MINOR.PATCH parse — mirrors the backend's
// FirmwareVersionComparer ("v" prefix ok, missing parts = 0).
void parse_semver(const char *v, int out[3]) {
    out[0] = out[1] = out[2] = 0;
    if (v == nullptr) return;
    if (*v == 'v' || *v == 'V') v++;
    sscanf(v, "%d.%d.%d", &out[0], &out[1], &out[2]);
}

int compare_semver(const char *a, const char *b) {
    int pa[3], pb[3];
    parse_semver(a, pa);
    parse_semver(b, pb);
    for (int i = 0; i < 3; i++) {
        if (pa[i] != pb[i]) return (pa[i] < pb[i]) ? -1 : 1;
    }
    return 0;
}

// Resolve the manifest's url: absolute http(s) used as-is; a bare path is
// resolved against the backend base derived from AREG_BACKEND_URL.
String resolve_image_url(const char *url) {
    if (strncmp(url, "http://", 7) == 0 || strncmp(url, "https://", 8) == 0) {
        return String(url);
    }
    String base = AREG_BACKEND_URL;
    base.replace("/api/chat/audio", "");
    return base + url;
}

// Transport seam — Stage B (pinned-CA HTTPS) swaps this single site to
// http.begin(secureClient, url) with setCACert(); every OTA HTTP call in
// this module goes through here.
bool ota_http_begin(HTTPClient &http, const String &url) {
    return http.begin(url);
}

// Lowercase-hex encode.
void to_hex_lower(const uint8_t *in, size_t len, char *out /* 2*len+1 */) {
    static const char *kHex = "0123456789abcdef";
    for (size_t i = 0; i < len; i++) {
        out[2 * i] = kHex[in[i] >> 4];
        out[2 * i + 1] = kHex[in[i] & 0x0F];
    }
    out[2 * len] = '\0';
}

// Constant-time case-insensitive hex-string compare (both must be same len).
bool hex_equals_ct(const char *a, const char *b) {
    const size_t la = strlen(a), lb = strlen(b);
    if (la != lb || la == 0) return false;
    uint8_t diff = 0;
    for (size_t i = 0; i < la; i++) {
        diff |= (uint8_t)(tolower((unsigned char)a[i]) ^ tolower((unsigned char)b[i]));
    }
    return diff == 0;
}

// HMAC-SHA256 over the backend's canonical string, hex-lowercase out.
// Canonical (byte-for-byte contract, pinned by the backend test
// Signature_VerifiesAgainstJsonWireForm):
//   version \n url \n sha256 \n sizeBytes \n expiresAt(raw JSON string)
bool manifest_hmac_hex(const char *version, const char *url, const char *sha256,
                       long size_bytes, const char *expires_at_raw,
                       char *out_hex /* 65 */) {
    char canonical[512];
    const int n = snprintf(canonical, sizeof(canonical), "%s\n%s\n%s\n%ld\n%s",
                           version, url, sha256, size_bytes, expires_at_raw);
    if (n < 0 || n >= (int)sizeof(canonical)) {
        return false;  // over-long manifest fields — refuse rather than truncate
    }
    const mbedtls_md_info_t *md = mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);
    uint8_t mac[32];
    if (mbedtls_md_hmac(md, (const uint8_t *)AREG_MANIFEST_HMAC_KEY,
                        strlen(AREG_MANIFEST_HMAC_KEY),
                        (const uint8_t *)canonical, (size_t)n, mac) != 0) {
        return false;
    }
    to_hex_lower(mac, sizeof(mac), out_hex);
    return true;
}

// Persist a post-gate FAILURE terminally: the same command id will re-ack
// this outcome instead of ever re-downloading (see ota_foundation).
void persist_failed(const char *command_id, const char *version, const char *err) {
    OtaPersist st;
    ota_state_load(st);
    st.state = OTA_STATE_FAILED;
    snprintf(st.cmd_id, sizeof(st.cmd_id), "%s", command_id);
    snprintf(st.pending_ver, sizeof(st.pending_ver), "%s", version);
    snprintf(st.last_error, sizeof(st.last_error), "%s", err);
    snprintf(st.applied_cmd, sizeof(st.applied_cmd), "%s", command_id);
    ota_state_save(st);
}

}  // namespace

OtaApplyOutcome ota_apply_run(const char *command_id, char *err_out, size_t err_cap) {
    // ---- 1. Fetch the manifest (at execution time — never a stale copy) ----
    HTTPClient mh;
    String manifest_url = AREG_BACKEND_URL;
    manifest_url.replace("/api/chat/audio", "/api/devices/firmware-manifest");
    if (!ota_http_begin(mh, manifest_url)) {
        set_err(err_out, err_cap, "manifest_begin_failed");
        return OTA_APPLY_REFUSED;
    }
    voice_add_device_auth_headers(mh);
    mh.setConnectTimeout(AREG_HTTP_CONNECT_MS);
    mh.setTimeout(AREG_HTTP_READ_MS);
    const int mstatus = mh.GET();
    if (mstatus != 200) {
        mh.end();
        Serial.printf("[ota] manifest fetch failed status=%d\n", mstatus);
        set_err(err_out, err_cap, "manifest_fetch_failed");
        return OTA_APPLY_REFUSED;
    }
    JsonDocument doc;
    const DeserializationError jerr = deserializeJson(doc, mh.getString());
    mh.end();
    if (jerr != DeserializationError::Ok) {
        set_err(err_out, err_cap, "manifest_parse_failed");
        return OTA_APPLY_REFUSED;
    }

    if (!(doc["updateAvailable"] | false)) {
        Serial.printf("[ota] manifest: no update (running %s)\n", AREG_FW_VERSION);
        return OTA_APPLY_NO_UPDATE;
    }

    const char *version    = doc["version"]    | "";
    const char *board      = doc["boardModel"] | "";
    const char *min_ver    = doc["minVersion"] | "";
    const char *url        = doc["url"]        | "";
    const long  size_bytes = doc["sizeBytes"]  | 0L;
    const char *sha256     = doc["sha256"]     | "";
    const char *signature  = doc["signature"]  | "";
    // Raw JSON text of expiresAt — the exact bytes the backend signed.
    const char *expires_at = doc["expiresAt"]  | "";

    // ---- 2. Gates (order: authenticity first, then policy, then size) ----
    if (strlen(AREG_MANIFEST_HMAC_KEY) > 0) {
        char expected[65];
        if (!manifest_hmac_hex(version, url, sha256, size_bytes, expires_at, expected)
            || !hex_equals_ct(expected, signature)) {
            Serial.println("[ota] REFUSED: manifest signature invalid");
            set_err(err_out, err_cap, "manifest_sig_invalid");
            return OTA_APPLY_REFUSED;
        }
        Serial.println("[ota] manifest signature OK");
    } else {
        Serial.println("[ota] WARNING: manifest signature check SKIPPED "
                       "(no AREG_MANIFEST_HMAC_KEY — Stage-A bench only)");
    }
    // Device has no synced wall clock (no RTC/NTP in this slice): device-side
    // expiry is skipped; the backend enforces manifest TTL + command expiry.
    Serial.printf("[ota] expiresAt=%s (device-side expiry skipped — no RTC; "
                  "server-enforced)\n", expires_at[0] ? expires_at : "(none)");

    if (board[0] != '\0' && strcmp(board, AREG_BOARD_MODEL) != 0) {
        Serial.printf("[ota] REFUSED: board mismatch (%s != %s)\n", board, AREG_BOARD_MODEL);
        set_err(err_out, err_cap, "board_mismatch");
        return OTA_APPLY_REFUSED;
    }
    if (min_ver[0] != '\0' && compare_semver(AREG_FW_VERSION, min_ver) < 0) {
        Serial.printf("[ota] REFUSED: running %s below minVersion %s\n",
                      AREG_FW_VERSION, min_ver);
        set_err(err_out, err_cap, "below_min_version");
        return OTA_APPLY_REFUSED;
    }
    // Strict upgrade only. (An explicit allowDowngrade command-payload flag is
    // a documented LATER addition — refused unconditionally in this slice.)
    if (compare_semver(version, AREG_FW_VERSION) <= 0) {
        Serial.printf("[ota] REFUSED: no downgrade/sidegrade (%s <= %s)\n",
                      version, AREG_FW_VERSION);
        set_err(err_out, err_cap, "no_downgrade");
        return OTA_APPLY_REFUSED;
    }
    if (size_bytes <= 0 || size_bytes > AREG_OTA_MAX_IMAGE_BYTES) {
        set_err(err_out, err_cap, "size_invalid");
        return OTA_APPLY_REFUSED;
    }
    if (strlen(sha256) != 64) {
        set_err(err_out, err_cap, "sha256_missing");
        return OTA_APPLY_REFUSED;
    }

    Serial.printf("[ota] manifest: UPDATE %s -> %s (sig=%s board=ok size=%ld)\n",
                  AREG_FW_VERSION, version,
                  strlen(AREG_MANIFEST_HMAC_KEY) > 0 ? "ok" : "skipped", size_bytes);

    // ---- 3. Persist state BEFORE any flash write ----
    {
        OtaPersist st;
        ota_state_load(st);
        st.state = OTA_STATE_DOWNLOADING;
        snprintf(st.cmd_id, sizeof(st.cmd_id), "%s", command_id);
        snprintf(st.pending_ver, sizeof(st.pending_ver), "%s", version);
        st.last_error[0] = '\0';
        st.boot_attempts = 0;
        ota_state_save(st);
    }

    // ---- 4. Stream download → SHA-256 + Update (inactive slot) ----
    HTTPClient http;
    const String image_url = resolve_image_url(url);
    if (!ota_http_begin(http, image_url)) {
        persist_failed(command_id, version, "download_begin_failed");
        set_err(err_out, err_cap, "download_begin_failed");
        return OTA_APPLY_FAILED;
    }
    voice_add_device_auth_headers(http);
    http.setConnectTimeout(AREG_HTTP_CONNECT_MS);
    http.setTimeout(AREG_HTTP_READ_MS);
    const int dstatus = http.GET();
    if (dstatus != 200) {
        http.end();
        Serial.printf("[ota] download failed status=%d\n", dstatus);
        persist_failed(command_id, version, "download_failed");
        set_err(err_out, err_cap, "download_failed");
        return OTA_APPLY_FAILED;
    }

    if (!Update.begin((size_t)size_bytes, U_FLASH)) {
        http.end();
        Serial.printf("[ota] Update.begin failed: %s\n", Update.errorString());
        persist_failed(command_id, version, "update_begin_failed");
        set_err(err_out, err_cap, "update_begin_failed");
        return OTA_APPLY_FAILED;
    }

    mbedtls_sha256_context sha;
    mbedtls_sha256_init(&sha);
    mbedtls_sha256_starts(&sha, /*is224=*/0);

    WiFiClient *stream = http.getStreamPtr();
    static uint8_t buf[4096];
    long received = 0;
    int last_pct = -1;
    uint32_t last_data_ms = millis();
    bool io_ok = true;

    while (received < size_bytes) {
        esp_task_wdt_reset();  // #047 — loop task is WDT-subscribed; feed per chunk
        const size_t avail = stream->available();
        if (avail == 0) {
            if (!http.connected() && stream->available() == 0) {
                io_ok = false;
                Serial.println("[ota] download: connection closed early");
                break;
            }
            if (millis() - last_data_ms > (uint32_t)AREG_HTTP_READ_MS) {
                io_ok = false;
                Serial.println("[ota] download: stalled (read timeout)");
                break;
            }
            delay(2);
            continue;
        }
        const size_t want = (avail < sizeof(buf)) ? avail : sizeof(buf);
        const size_t cap = (size_t)(size_bytes - received) < want
                               ? (size_t)(size_bytes - received) : want;
        const int n = stream->read(buf, cap);
        if (n <= 0) {
            delay(2);
            continue;
        }
        last_data_ms = millis();
        mbedtls_sha256_update(&sha, buf, (size_t)n);
        if (Update.write(buf, (size_t)n) != (size_t)n) {
            io_ok = false;
            Serial.printf("[ota] flash write failed: %s\n", Update.errorString());
            break;
        }
        received += n;
        const int pct = (int)((received * 100) / size_bytes);
        if (pct / 10 != last_pct / 10) {
            last_pct = pct;
            Serial.printf("[ota] downloading %d%% (%ld/%ld)\n", pct, received, size_bytes);
        }
    }
    http.end();

    uint8_t digest[32];
    mbedtls_sha256_finish(&sha, digest);
    mbedtls_sha256_free(&sha);

    if (!io_ok || received != size_bytes) {
        Update.abort();
        persist_failed(command_id, version, "download_incomplete");
        set_err(err_out, err_cap, "download_incomplete");
        return OTA_APPLY_FAILED;
    }

    // ---- 5. Verify sha256 BEFORE finalizing ----
    char actual_hex[65];
    to_hex_lower(digest, sizeof(digest), actual_hex);
    if (!hex_equals_ct(actual_hex, sha256)) {
        Update.abort();  // never finalize an unverified image; boot slot unchanged
        Serial.printf("[ota] sha256 MISMATCH (got %s) — NOT applying\n", actual_hex);
        persist_failed(command_id, version, "sha256_mismatch");
        set_err(err_out, err_cap, "sha256_mismatch");
        return OTA_APPLY_FAILED;
    }
    Serial.println("[ota] sha256 ok");

    // ---- 6. Finalize: native image validation + boot-partition switch ----
    if (!Update.end(/*evenIfRemaining=*/true) || !Update.isFinished()) {
        Serial.printf("[ota] image validation/finalize failed: %s\n", Update.errorString());
        persist_failed(command_id, version, "image_invalid");
        set_err(err_out, err_cap, "image_invalid");
        return OTA_APPLY_FAILED;
    }

    // ---- 7. Persist REBOOTING and go. NO ack here — the ack that matters is
    // sent by the NEW image after a healthy boot (or by the OLD image after a
    // rollback). An "ok" now would assert an outcome we don't know yet. ----
    {
        OtaPersist st;
        ota_state_load(st);
        st.state = OTA_STATE_REBOOTING;
        snprintf(st.cmd_id, sizeof(st.cmd_id), "%s", command_id);
        snprintf(st.pending_ver, sizeof(st.pending_ver), "%s", version);
        st.last_error[0] = '\0';
        st.boot_attempts = 0;
        ota_state_save(st);
    }
    Serial.printf("[ota] APPLYING %s -> reboot into pending-verify\n", version);
    Serial.flush();
    delay(250);  // let the serial line drain for the bench log
    ESP.restart();
    return OTA_APPLY_FAILED;  // unreachable
}

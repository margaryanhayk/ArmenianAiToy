// -------------------------------------------------------------
// AregVoiceMvp / voice_client.cpp
//
// Wi-Fi join + POST /api/chat/audio. Intentionally small and
// synchronous. No retry, no reconnect — a failure path plays
// the canned clip (handled by the state machine caller) and
// returns to idle.
// -------------------------------------------------------------
#include "voice_client.h"
#include "config.h"
#include "diag.h"

#include <WiFi.h>
#include <HTTPClient.h>
#include <esp_heap_caps.h>

static uint8_t *s_response_buffer = nullptr;

// -------------------------------------------------------------
// Wi-Fi
// -------------------------------------------------------------

bool voice_wifi_begin() {
    // Arduino WiFi.begin() is asynchronous; spin with a timeout
    // so boot doesn't hang forever if credentials are wrong.
    WiFi.mode(WIFI_STA);
    WiFi.setAutoReconnect(true);
    WiFi.begin(AREG_WIFI_SSID, AREG_WIFI_PASSWORD);
    Serial.printf("[wifi] connecting to %s ...\n", AREG_WIFI_SSID);
    const uint32_t timeout_ms = 20000;
    uint32_t started = millis();
    while (WiFi.status() != WL_CONNECTED) {
        if (millis() - started > timeout_ms) {
            Serial.println("[wifi] connect timeout");
            return false;
        }
        delay(250);
    }
    Serial.print("[wifi] ip=");
    Serial.println(WiFi.localIP());
    return true;
}

bool voice_wifi_is_connected() {
    return WiFi.status() == WL_CONNECTED;
}

// -------------------------------------------------------------
// Upload
// -------------------------------------------------------------

void voice_release_last_response() {
    if (s_response_buffer != nullptr) {
        heap_caps_free(s_response_buffer);
        s_response_buffer = nullptr;
    }
}

// Reads an already-confirmed-200 response body in full into a fresh
// PSRAM buffer and records the X-Areg-Continue header. Leaves `http`
// open for the caller to end(). Returns true on a fully-buffered body;
// on false it has freed any partial buffer and set nothing on result.
static bool read_response_into(HTTPClient &http, VoiceTurnResult &result) {
    const int body_len = http.getSize();
    if (body_len <= 0) {
        Serial.printf("[voice] http: unexpected body length %d\n", body_len);
        return false;
    }
    if ((size_t)body_len > AREG_PLAYBACK_BUFFER_BYTES) {
        Serial.printf("[voice] http: body %d exceeds playback buffer %u\n",
                      body_len, (unsigned)AREG_PLAYBACK_BUFFER_BYTES);
        return false;
    }
    uint8_t *buf = (uint8_t *)heap_caps_malloc((size_t)body_len, MALLOC_CAP_SPIRAM);
    if (buf == nullptr) {
        Serial.println("[voice] psram allocation failed for response");
        return false;
    }
    WiFiClient *stream = http.getStreamPtr();
    size_t read_total = 0;
    const uint32_t read_deadline = millis() + AREG_HTTP_READ_MS;
    while (read_total < (size_t)body_len) {
        if (millis() > read_deadline) {
            Serial.println("[voice] http read timeout");
            heap_caps_free(buf);
            return false;
        }
        size_t avail = stream->available();
        if (avail == 0) {
            delay(5);
            continue;
        }
        size_t want = (size_t)body_len - read_total;
        if (avail > want) avail = want;
        int got = stream->readBytes(buf + read_total, avail);
        if (got <= 0) {
            delay(5);
            continue;
        }
        read_total += (size_t)got;
    }
    s_response_buffer = buf;
    result.ok = true;
    result.response_bytes = buf;
    result.response_length = (size_t)body_len;
    result.continue_more = (http.header("X-Areg-Continue") == "1");
    Serial.printf("[voice] http 200, body=%u bytes (psram), continue=%s\n",
                  (unsigned)result.response_length,
                  result.continue_more ? "1" : "0");
    Serial.flush();
    return true;
}

VoiceTurnResult voice_upload_turn(const uint8_t *payload, size_t length) {
    VoiceTurnResult result;
    voice_release_last_response();  // defensive — prior call's buffer

    if (!voice_wifi_is_connected()) {
        Serial.println("[voice] upload: wifi not connected");
        result.http_status = -1001;
        return result;
    }
    if (payload == nullptr || length == 0) {
        Serial.println("[voice] upload: empty payload");
        return result;
    }

    HTTPClient http;
    http.setConnectTimeout(AREG_HTTP_CONNECT_MS);
    http.setTimeout(AREG_HTTP_READ_MS);
    DIAG_MARK(5000, "http_begin_before");
    if (!http.begin(AREG_BACKEND_URL)) {
        DIAG_MARK(5001, "http_begin_fail");
        Serial.println("[voice] http.begin failed");
        Serial.flush();
        return result;
    }
    DIAG_MARK(5002, "http_begin_after_ok");
    http.addHeader("Content-Type", "audio/wav");
    http.addHeader("X-Device-Id", AREG_DEVICE_ID);
    http.addHeader("X-Api-Key", AREG_DEVICE_API_KEY);
    static const char *kCollectHeaders[] = {"X-Areg-Continue"};
    http.collectHeaders(kCollectHeaders, 1);

    DIAG_MARK(5010, "http_post_before");
    const int status = http.POST((uint8_t *)payload, length);
    DIAG_MARK(5011, "http_post_after");
    result.http_status = status;
    if (status != 200) {
        Serial.printf("[voice] http POST non-200: %d\n", status);
        http.end();
        return result;
    }

    DIAG_MARK(5020, "http_read_body_before");
    const bool read_ok = read_response_into(http, result);
    DIAG_MARK(5021, "http_read_body_after_ok");
    http.end();
    DIAG_MARK(5030, "http_end_after");
    if (!read_ok) {
        voice_release_last_response();
        return result;  // ok stays false
    }
    DIAG_MARK(5099, "voice_upload_exit_ok");
    return result;
}

VoiceTurnResult voice_upload_question(const uint8_t *payload, size_t length,
                                      uint32_t offset) {
    VoiceTurnResult result;
    voice_release_last_response();

    if (!voice_wifi_is_connected()) {
        Serial.println("[qa] upload: wifi not connected");
        result.http_status = -1001;
        return result;
    }
    if (payload == nullptr || length == 0) {
        Serial.println("[qa] upload: empty payload");
        return result;
    }

    char url[384];
    snprintf(url, sizeof(url), "%s?storyId=%s&offset=%u",
             AREG_STORY_QA_URL, AREG_STORY_ID, (unsigned)offset);

    HTTPClient http;
    http.setConnectTimeout(AREG_HTTP_CONNECT_MS);
    http.setTimeout(AREG_HTTP_READ_MS);
    if (!http.begin(url)) {
        Serial.println("[qa] http.begin failed");
        Serial.flush();
        return result;
    }
    http.addHeader("Content-Type", "audio/wav");
    http.addHeader("X-Device-Id", AREG_DEVICE_ID);
    http.addHeader("X-Api-Key", AREG_DEVICE_API_KEY);

    Serial.printf("[qa] POST question (%u bytes) offset=%u\n",
                  (unsigned)length, (unsigned)offset);
    Serial.flush();
    const int status = http.POST((uint8_t *)payload, length);
    result.http_status = status;
    if (status != 200) {
        Serial.printf("[qa] http POST non-200: %d\n", status);
        http.end();
        return result;
    }

    const bool read_ok = read_response_into(http, result);
    http.end();
    if (!read_ok) {
        voice_release_last_response();
        return result;
    }
    Serial.printf("[qa] answer %u bytes\n", (unsigned)result.response_length);
    Serial.flush();
    return result;
}

VoiceTurnResult voice_continue_turn() {
    VoiceTurnResult result;
    voice_release_last_response();  // defensive — prior call's buffer

    if (!voice_wifi_is_connected()) {
        Serial.println("[voice] continue: wifi not connected");
        result.http_status = -1001;
        return result;
    }

    HTTPClient http;
    http.setConnectTimeout(AREG_HTTP_CONNECT_MS);
    http.setTimeout(AREG_HTTP_READ_MS);
    if (!http.begin(AREG_BACKEND_URL)) {
        Serial.println("[voice] continue: http.begin failed");
        return result;
    }
    http.addHeader("X-Device-Id", AREG_DEVICE_ID);
    http.addHeader("X-Api-Key", AREG_DEVICE_API_KEY);
    http.addHeader("X-Areg-Continue", "1");
    static const char *kCollectHeaders[] = {"X-Areg-Continue"};
    http.collectHeaders(kCollectHeaders, 1);

    // Empty body — the backend skips STT and advances the active
    // library story.
    const int status = http.POST((uint8_t *)"", 0);
    result.http_status = status;
    if (status == 204) {
        // No more story to play — clean end of autoplay.
        Serial.println("[voice] continue: 204 (story complete)");
        http.end();
        return result;  // ok=false, continue_more=false
    }
    if (status != 200) {
        Serial.printf("[voice] continue: non-200 %d\n", status);
        http.end();
        return result;
    }

    const bool read_ok = read_response_into(http, result);
    http.end();
    if (!read_ok) {
        voice_release_last_response();
        return result;
    }
    return result;
}

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
    if (!http.begin(AREG_BACKEND_URL)) {
        Serial.println("[voice] http.begin failed");
        return result;
    }
    http.addHeader("Content-Type", "audio/wav");
    http.addHeader("X-Device-Id", AREG_DEVICE_ID);
    http.addHeader("X-Api-Key", AREG_DEVICE_API_KEY);

    const int status = http.POST((uint8_t *)payload, length);
    result.http_status = status;
    if (status != 200) {
        Serial.printf("[voice] http POST non-200: %d\n", status);
        http.end();
        return result;
    }

    // Response body — read in full into PSRAM. HTTP/1.1 against
    // the dev backend returns Content-Length reliably.
    const int body_len = http.getSize();
    if (body_len <= 0) {
        Serial.printf("[voice] http: unexpected body length %d\n", body_len);
        http.end();
        return result;
    }
    if ((size_t)body_len > AREG_PLAYBACK_BUFFER_BYTES) {
        Serial.printf("[voice] http: body %d exceeds playback buffer %u\n",
                      body_len, (unsigned)AREG_PLAYBACK_BUFFER_BYTES);
        http.end();
        return result;
    }

    uint8_t *buf = (uint8_t *)heap_caps_malloc((size_t)body_len, MALLOC_CAP_SPIRAM);
    if (buf == nullptr) {
        Serial.println("[voice] psram allocation failed for response");
        http.end();
        return result;
    }

    WiFiClient *stream = http.getStreamPtr();
    size_t read_total = 0;
    const uint32_t read_deadline = millis() + AREG_HTTP_READ_MS;
    while (read_total < (size_t)body_len) {
        if (millis() > read_deadline) {
            Serial.println("[voice] http read timeout");
            heap_caps_free(buf);
            http.end();
            return result;
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
    http.end();

    s_response_buffer = buf;
    result.ok = true;
    result.response_bytes = buf;
    result.response_length = (size_t)body_len;
    Serial.printf("[voice] http 200, body=%u bytes (psram)\n",
                  (unsigned)result.response_length);
    return result;
}

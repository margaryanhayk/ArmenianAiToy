// -------------------------------------------------------------
// AregVoiceMvp / voice_client.cpp
//
// Wi-Fi join + POST /api/chat/audio. Intentionally small and
// synchronous. A per-turn upload failure plays the canned clip
// (handled by the state machine caller) and returns to idle.
// Wi-Fi LINK recovery, however, is handled out-of-band by
// voice_wifi_tick() (#045): a dropped link reconnects in the
// background with backoff rather than requiring a power-cycle.
// -------------------------------------------------------------
#include "voice_client.h"
#include "config.h"
#include "net_transport.h"
#include "audio_io.h"   // audio_sd_available() - SD health in the heartbeat
#include "diag.h"
#include "wifi_creds.h"     // Phase B.1 — NVS-backed Wi-Fi credentials
#include "device_creds.h"   // Phase C   — NVS-backed device identity
#include "ota_foundation.h" // Proof 2 — AREG_FW_* identity + running-partition label
#include "ota_state.h"      // OTA apply — lastOtaStatus for the heartbeat report
#include "content_sync_rules.h"  // cs_copy_bounded — bounded intent-token copy

#include <Preferences.h>    // welcome flow — persisted last-known pause/bedtime

#include <WiFi.h>
#include <HTTPClient.h>
#include <ArduinoJson.h>         // Slice E — heartbeat-response parse (bedtime flag)
#include <esp_heap_caps.h>
#include <freertos/FreeRTOS.h>   // xTaskCreatePinnedToCore (S3 async upload, UNVERIFIED)
#include <freertos/task.h>       // vTaskDelete, BaseType_t
#include <freertos/semphr.h>     // #046 — mutex for the cross-core result handoff

// #045 — Wi-Fi reconnect tuning. Defaulted here (not required in config.h) so
// the build never depends on config.h carrying them; an operator may override
// either in config.h. MIN is the first retry delay after a drop; backoff
// doubles up to MAX so a long outage doesn't hammer the radio.
#ifndef AREG_WIFI_RECONNECT_MIN_MS
#define AREG_WIFI_RECONNECT_MIN_MS  3000
#endif
#ifndef AREG_WIFI_RECONNECT_MAX_MS
#define AREG_WIFI_RECONNECT_MAX_MS  60000
#endif

static uint8_t *s_response_buffer = nullptr;

// -------------------------------------------------------------
// Device identity (Phase C — factory-burned, NVS-first with config.h fallback)
// -------------------------------------------------------------
// EFFECTIVE device creds: the factory-burned identity from NVS if present,
// else the compile-time config.h values (bench). Loaded once, lazily, the
// first time any backend call builds its auth headers. Device identity does
// not change at runtime, so a one-time load is correct. With no NVS creds the
// buffers are filled byte-for-byte from AREG_DEVICE_ID / AREG_DEVICE_API_KEY,
// so the bench behaves exactly as before this change.
static char s_device_id[48]      = {0};
static char s_device_api_key[128] = {0};
static bool s_device_creds_loaded = false;

static void ensure_device_creds() {
    if (s_device_creds_loaded) {
        return;
    }
    if (device_creds_load(s_device_id, sizeof(s_device_id),
                          s_device_api_key, sizeof(s_device_api_key))) {
        Serial.printf("[device] using provisioned identity (id=%s)\n", s_device_id);
    } else {
        snprintf(s_device_id, sizeof(s_device_id), "%s", AREG_DEVICE_ID);
        snprintf(s_device_api_key, sizeof(s_device_api_key), "%s", AREG_DEVICE_API_KEY);
        Serial.printf("[device] using compile-time identity (id=%s)\n", s_device_id);
    }
    Serial.flush();
    s_device_creds_loaded = true;
}

// Single source of truth for the device-auth headers every backend call needs.
// Replaces the previously-duplicated addHeader pair at each call site and
// guarantees they all use the same EFFECTIVE identity (NVS or fallback).
static void add_device_auth_headers(HTTPClient &http) {
    ensure_device_creds();
    http.addHeader("X-Device-Id", s_device_id);
    http.addHeader("X-Api-Key", s_device_api_key);
}

// Public seam for other modules (ota_foundation.cpp) — same identity,
// same headers, one source of truth.
void voice_add_device_auth_headers(HTTPClient &http) {
    add_device_auth_headers(http);
}

// -------------------------------------------------------------
// Wi-Fi
// -------------------------------------------------------------

// Phase B.1 — EFFECTIVE Wi-Fi credentials: the provisioned creds from NVS if
// present, else the compile-time config.h fallback (bench). Loaded into module
// state so voice_wifi_begin() and the reconnect tick use the same network.
static char s_wifi_ssid[65] = {0};
static char s_wifi_pass[65] = {0};

// B.3 — millis() when the link was first observed down (0 = connected / not yet
// observed down). Set/cleared by voice_wifi_tick(); read by
// voice_wifi_down_duration_ms() to drive the auto-fallback to provisioning.
static uint32_t s_wifi_down_since_ms = 0;

// Slice E — last-known bedtime-window state from the heartbeat response.
static bool s_in_bedtime_window = false;
// Last-known PAUSE state from the heartbeat response, so a paused toy stays
// fully silent even for local SD playback (pause used to gate only online
// chat). Defaults false; cached between heartbeats like the bedtime flag.
static bool s_is_paused = false;

// Welcome flow — both flags above are also mirrored into NVS so the boot
// greeting can honor them BEFORE the first heartbeat of a power-on. Own
// namespace, per the same isolation rule as aregstory / aregmusic / aregota.
static constexpr const char *kStatePrefsNamespace  = "aregstate";
static constexpr const char *kStatePrefsPausedKey  = "paused";
static constexpr const char *kStatePrefsBedtimeKey = "bedtime";

// The story every in-story backend call is grounded in. Empty means "use the
// compile-time configured story", which is the pre-selection behavior. Once
// story-select-from-index picks a cached story, the .ino sets this so a
// question asked during story B is not answered about story A.
static char s_active_story_id[64] = {0};

void voice_set_active_story_id(const char *story_id) {
    if (story_id == nullptr || story_id[0] == '\0') {
        s_active_story_id[0] = '\0';
        return;
    }
    snprintf(s_active_story_id, sizeof(s_active_story_id), "%s", story_id);
}

const char *voice_active_story_id() {
    return s_active_story_id[0] ? s_active_story_id : AREG_STORY_ID;
}

static void wifi_load_effective_creds() {
    if (wifi_creds_load(s_wifi_ssid, sizeof(s_wifi_ssid),
                        s_wifi_pass, sizeof(s_wifi_pass))) {
        Serial.printf("[wifi] using provisioned creds (ssid=%s)\n", s_wifi_ssid);
    } else {
        // Fallback: compile-time creds (config.h). Behavior-neutral for the
        // bench, which has no NVS creds.
        snprintf(s_wifi_ssid, sizeof(s_wifi_ssid), "%s", AREG_WIFI_SSID);
        snprintf(s_wifi_pass, sizeof(s_wifi_pass), "%s", AREG_WIFI_PASSWORD);
        Serial.printf("[wifi] using compile-time fallback creds (ssid=%s)\n", s_wifi_ssid);
    }
    Serial.flush();
}

bool voice_wifi_is_provisioned() {
    return wifi_creds_present();
}

void voice_wifi_set_credentials(const char *ssid, const char *password) {
    // Persist + switch to the new network (the seam the BLE provisioning layer
    // calls). Survives reboots.
    wifi_creds_save(ssid, password);
    wifi_load_effective_creds();
    WiFi.disconnect();
    WiFi.begin(s_wifi_ssid, s_wifi_pass);
    Serial.printf("[wifi] credentials updated; reconnecting to %s\n", s_wifi_ssid);
    Serial.flush();
}

bool voice_wifi_begin() {
    // Arduino WiFi.begin() is asynchronous; spin with a timeout
    // so boot doesn't hang forever if credentials are wrong.
    wifi_load_effective_creds();
    WiFi.mode(WIFI_STA);
    WiFi.setAutoReconnect(true);
    // Modem power save (hardware review 2026-08-07): the firmware never
    // sleeps, and IDLE at ~70 mA costs 4.6x the energy of actually telling
    // a story. MIN_MODEM keeps the radio associated but dozes it between
    // DTIM beacons (~100 ms wake granularity) — invisible to every existing
    // flow: the heartbeat/command-poll cadence is 60 s, chat/OTA/sync are
    // all device-initiated, and nothing here listens for unsolicited
    // inbound traffic. Roughly halves idle current; the bigger light-sleep
    // slice stays deliberately separate from any OTA release.
    WiFi.setSleep(WIFI_PS_MIN_MODEM);
    WiFi.begin(s_wifi_ssid, s_wifi_pass);
    Serial.printf("[wifi] connecting to %s ...\n", s_wifi_ssid);
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

// #045 — non-blocking link maintenance. Cheap when connected; on a drop it
// re-issues a join at most once per backoff window (capped exponential), so a
// router blip recovers in the background without a power-cycle and without
// blocking loop().
void voice_wifi_tick() {
    static uint32_t s_last_attempt_ms = 0;
    static uint32_t s_backoff_ms = AREG_WIFI_RECONNECT_MIN_MS;
    static bool s_attempted = false;

    if (WiFi.status() == WL_CONNECTED) {
        // Healthy — arm a prompt first retry for the NEXT drop.
        s_backoff_ms = AREG_WIFI_RECONNECT_MIN_MS;
        s_attempted = false;
        s_wifi_down_since_ms = 0;   // B.3 — link healthy, reset the outage clock
        return;
    }

    const uint32_t now = millis();
    if (s_wifi_down_since_ms == 0) {
        // B.3 — first tick that observed the drop; start the outage clock.
        // (millis() is never 0 here in practice — boot spends >0 ms before the
        // first tick — but if it were, the next tick stamps a nonzero value.)
        s_wifi_down_since_ms = now;
    }
    // Rollover-safe elapsed check (same idiom as the idle heartbeat).
    if (s_attempted && (now - s_last_attempt_ms) < s_backoff_ms) {
        return;  // still inside the current backoff window
    }

    Serial.printf("[wifi] down (status=%d); reconnect attempt (backoff=%u ms)\n",
                  (int)WiFi.status(), (unsigned)s_backoff_ms);
    Serial.flush();
    WiFi.disconnect();   // clear any half-open state before a clean rejoin
    WiFi.begin(s_wifi_ssid, s_wifi_pass);   // effective creds (NVS or fallback)

    s_last_attempt_ms = now;
    s_attempted = true;
    const uint32_t next = s_backoff_ms * 2;
    s_backoff_ms = (next > AREG_WIFI_RECONNECT_MAX_MS)
                       ? (uint32_t)AREG_WIFI_RECONNECT_MAX_MS
                       : next;
}

uint32_t voice_wifi_down_duration_ms() {
    if (WiFi.status() == WL_CONNECTED || s_wifi_down_since_ms == 0) {
        return 0;
    }
    return millis() - s_wifi_down_since_ms;  // rollover-safe (unsigned wrap)
}

void voice_send_heartbeat() {
    if (!voice_wifi_is_connected()) {
        return;  // nothing to do offline; the reconnect tick owns recovery
    }
    // Derive the heartbeat URL from the configured backend URL (same trick as
    // voice_fetch_story_audio_token) so there is no separate config constant to
    // keep in sync: ".../api/chat/audio" -> ".../api/devices/heartbeat".
    String url = AREG_BACKEND_URL;
    url.replace("/api/chat/audio", "/api/devices/heartbeat");

    HTTPClient http;
    if (!areg_http_begin(http, url)) {
        return;
    }
    add_device_auth_headers(http);
    http.setConnectTimeout(AREG_HTTP_CONNECT_MS);
    http.setTimeout(AREG_HTTP_READ_MS);

    // OTA foundation: report the firmware identity alongside presence. The
    // heartbeat body is OPTIONAL on the backend (EmptyBodyBehavior.Allow),
    // so a legacy build's body-less POST keeps working — and this build's
    // body additionally stamps FirmwareVersion / Build / BoardModel /
    // PartitionName / LastOtaStatus onto the device row (the fields the
    // firmware-manifest offer gate reads). Values are compile-time
    // constants + the running partition label — no JSON escaping needed.
    // Best-effort exactly like before: any status (incl. 401 on a stale
    // key, or a transport failure) is fine, the next interval retries.
    //
    // SELF-DIAGNOSTICS (sdCardOk). A toy whose SD card is not mounted plays
    // no stories at all: the button appears dead and the toy is simply
    // silent. That happened on the bench (a 5 V wire to the card reader came
    // loose) and was invisible without a serial cable — the parent's only
    // signal was "it stopped working". The toy KNOWS within a second of boot,
    // so it reports it here and the parent surface can say so in plain words.
    // Bounded boolean, no free-form strings, no PII — same discipline as the
    // metric tags.
    char body[320];
    snprintf(body, sizeof(body),
             "{\"firmwareVersion\":\"%s\",\"firmwareBuild\":\"%s\","
             "\"boardModel\":\"%s\",\"partitionName\":\"%s\","
             "\"lastOtaStatus\":\"%s\",\"sdCardOk\":%s}",
             AREG_FW_VERSION, AREG_FW_BUILD, AREG_BOARD_MODEL,
             ota_running_partition_label(), ota_state_status_cstr(),
             audio_sd_available() ? "true" : "false");
    http.addHeader("Content-Type", "application/json");
    const int status = http.POST(body);
    if (status == 200) {
        // Slice E — the server tells us whether the bedtime window is
        // active right now (additive field; older backends omit it and the
        // cached value defaults false). Best-effort parse: a malformed
        // body just leaves the previous value standing.
        const String resp = http.getString();
        JsonDocument doc;
        if (deserializeJson(doc, resp) == DeserializationError::Ok) {
            const bool bedtime = doc["inBedtimeWindow"] | false;
            const bool paused  = doc["isPaused"] | false;
            // Welcome flow — persist ONLY on a real change. The heartbeat
            // fires every ~60 s, so writing unconditionally would be
            // ~1,440 flash writes a day for a value that rarely moves.
            //
            // Why persist at all: the boot greeting has to decide whether
            // to speak BEFORE the first heartbeat of this power-on, and
            // these were RAM-only statics defaulting to false. A toy
            // powered off for a week would otherwise greet a child whose
            // parent paused it six days ago.
            if (bedtime != s_in_bedtime_window || paused != s_is_paused) {
                Preferences prefs;
                if (prefs.begin(kStatePrefsNamespace, /*readOnly=*/false)) {
                    prefs.putBool(kStatePrefsPausedKey, paused);
                    prefs.putBool(kStatePrefsBedtimeKey, bedtime);
                    prefs.end();
                }
            }
            s_in_bedtime_window = bedtime;
            s_is_paused = paused;
        }
    }
    http.end();
    Serial.printf("[heartbeat] status=%d (fw=%s bedtime=%d paused=%d)\n",
                  status, AREG_FW_VERSION, s_in_bedtime_window ? 1 : 0,
                  s_is_paused ? 1 : 0);
    Serial.flush();
}

bool voice_in_bedtime_window() {
    return s_in_bedtime_window;
}

bool voice_is_paused() {
    return s_is_paused;
}

void voice_state_restore() {
    Preferences prefs;
    if (!prefs.begin(kStatePrefsNamespace, /*readOnly=*/true)) {
        return;   // never provisioned — the false defaults stand
    }
    s_is_paused         = prefs.getBool(kStatePrefsPausedKey, false);
    s_in_bedtime_window = prefs.getBool(kStatePrefsBedtimeKey, false);
    prefs.end();
    Serial.printf("[state] restored paused=%d bedtime=%d\n",
                  s_is_paused ? 1 : 0, s_in_bedtime_window ? 1 : 0);
    Serial.flush();
}

// -------------------------------------------------------------
// Welcome flow — POST the child's spoken menu answer and read back the
// single bounded intent token. Deliberately NOT a VoiceTurnResult: this
// endpoint returns a tiny JSON body, never audio, so there is no PSRAM
// buffer to own or release.
//
// Every failure path yields "unknown", because the toy's handling for
// "I didn't understand" is already the graceful one — a second failure
// mode on the device would be dead weight.
// -------------------------------------------------------------
bool voice_post_voice_intent(const uint8_t *payload, size_t length,
                             const char *expect, char *out_intent,
                             size_t out_len) {
    if (out_intent == nullptr || out_len == 0) {
        return false;
    }
    out_intent[0] = '\0';
    if (payload == nullptr || length == 0 || !voice_wifi_is_connected()) {
        return false;
    }

    // Same URL-derivation trick as the heartbeat: no new config constant.
    String url = AREG_BACKEND_URL;
    url.replace("/api/chat/audio", "/api/devices/voice-intent");
    url += "?expect=";
    url += (expect != nullptr && expect[0] != '\0') ? expect : "mode";

    HTTPClient http;
    http.setConnectTimeout(AREG_HTTP_CONNECT_MS);
    http.setTimeout(AREG_HTTP_READ_MS);
    if (!areg_http_begin(http, url)) {
        Serial.println("[welcome] intent http.begin failed");
        Serial.flush();
        return false;
    }
    http.addHeader("Content-Type", "audio/wav");
    add_device_auth_headers(http);

    const int status = http.POST((uint8_t *)payload, length);
    if (status != 200) {
        http.end();
        Serial.printf("[welcome] intent status=%d\n", status);
        Serial.flush();
        return false;
    }
    const String resp = http.getString();
    http.end();

    JsonDocument doc;
    if (deserializeJson(doc, resp) != DeserializationError::Ok) {
        Serial.println("[welcome] intent parse failed");
        Serial.flush();
        return false;
    }
    const char *intent = doc["intent"] | "";
    if (intent[0] == '\0') {
        return false;
    }
    return cs_copy_bounded(out_intent, out_len, intent);
}

int voice_post_story_plays(const char *json_body) {
    if (json_body == nullptr || !voice_wifi_is_connected()) {
        return -1;
    }
    // Same URL-derivation trick as the heartbeat: no new config constant.
    String url = AREG_BACKEND_URL;
    url.replace("/api/chat/audio", "/api/devices/story-plays");

    HTTPClient http;
    if (!areg_http_begin(http, url)) {
        return -1;
    }
    add_device_auth_headers(http);
    http.setConnectTimeout(AREG_HTTP_CONNECT_MS);
    http.setTimeout(AREG_HTTP_READ_MS);
    http.addHeader("Content-Type", "application/json");
    const int status = http.POST((uint8_t *)json_body, strlen(json_body));
    http.end();
    return status;
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

// -------------------------------------------------------------
// Story-audio access token (gap 1). UNVERIFIED — not compiled/flashed.
//
// The header-less GET /api/story-audio stream requires ?token= when the
// backend has StoryAudio:SigningKey set. We fetch a short-lived signed
// token from the device-authed /api/chat/story-audio-token endpoint. The
// URL is derived from AREG_BACKEND_URL (".../api/chat/audio" ->
// ".../api/chat/story-audio-token") so there is no new config constant to
// keep in sync. A null token (enforcement off) or any error returns false,
// and the caller streams without a token — correct while the key is unset.
// -------------------------------------------------------------
bool voice_fetch_story_audio_token(const char *story_id, char *out_token, size_t out_cap) {
    if (out_token == nullptr || out_cap == 0) {
        return false;
    }
    out_token[0] = '\0';
    if (story_id == nullptr || !voice_wifi_is_connected()) {
        return false;
    }

    String tokenUrl = AREG_BACKEND_URL;
    tokenUrl.replace("/api/chat/audio", "/api/chat/story-audio-token");
    tokenUrl += "?storyId=";
    tokenUrl += story_id;

    HTTPClient http;
    if (!areg_http_begin(http, tokenUrl)) {
        return false;
    }
    add_device_auth_headers(http);
    http.setConnectTimeout(AREG_HTTP_CONNECT_MS);
    http.setTimeout(AREG_HTTP_READ_MS);

    const int status = http.GET();
    if (status != 200) {
        Serial.printf("[token] story-audio-token GET status=%d\n", status);
        Serial.flush();
        http.end();
        return false;
    }
    String body = http.getString();
    http.end();

    // Minimal parse of {"token":"<opaque>",...} or {"token":null,...}. No
    // ArduinoJson dependency — the body is tiny and the token is a base64url
    // string ('.'-separated, no embedded quotes).
    int tk = body.indexOf("\"token\"");
    if (tk < 0) {
        return false;
    }
    int colon = body.indexOf(':', tk);
    if (colon < 0) {
        return false;
    }
    int p = colon + 1;
    while (p < (int)body.length() && (body.charAt(p) == ' ' || body.charAt(p) == '\t')) {
        p++;
    }
    if (p >= (int)body.length() || body.charAt(p) != '"') {
        // "token": null → enforcement is off; stream without a token.
        return false;
    }
    int start = p + 1;
    int end = body.indexOf('"', start);
    if (end <= start || (size_t)(end - start) >= out_cap) {
        return false;
    }
    body.substring(start, end).toCharArray(out_token, out_cap);
    return out_token[0] != '\0';
}

// #048 — treat the response body as UNTRUSTED before it reaches the MP3
// decoder. The body must begin with a real MP3 signature; an accidental
// non-audio body (a proxy/error HTML page, a JSON error, a misrouted
// endpoint) is rejected here instead of becoming a memory-safety surface in
// the decoder, in a child's room. Accepts the two real-world MP3 starts:
//   - an MPEG audio frame sync (0xFF, then the top 3 sync bits set) — this is
//     what OpenAI's tts-1 output begins with (verified: FF F3 ...), and
//   - an ID3v2 tag ("ID3"), emitted by other encoders.
// NOTE: over plaintext HTTP this is DEFENSE-IN-DEPTH, not an MITM defense (a
// MITM controls the bytes AND any header); TLS (#008) is the real fix. It
// still stops the realistic failure today — a garbage/error body reaching the
// decoder. Pairs with the existing size cap below.
static bool looks_like_mp3(const uint8_t *buf, size_t len) {
    if (buf == nullptr || len < 3) return false;
    if (buf[0] == 'I' && buf[1] == 'D' && buf[2] == '3') return true;     // ID3v2 tag
    if (buf[0] == 0xFF && (buf[1] & 0xE0) == 0xE0) return true;           // MPEG frame sync
    return false;
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
    // #048 — reject a non-MP3 body before it reaches the decoder.
    if (!looks_like_mp3(buf, (size_t)body_len)) {
        Serial.printf("[voice] http: body is not MP3 (first bytes %02X %02X %02X); rejecting\n",
                      buf[0], body_len > 1 ? buf[1] : 0, body_len > 2 ? buf[2] : 0);
        Serial.flush();
        heap_caps_free(buf);
        return false;
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
    if (!areg_http_begin(http, AREG_BACKEND_URL)) {
        DIAG_MARK(5001, "http_begin_fail");
        Serial.println("[voice] http.begin failed");
        Serial.flush();
        return result;
    }
    DIAG_MARK(5002, "http_begin_after_ok");
    http.addHeader("Content-Type", "audio/wav");
    add_device_auth_headers(http);
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
             AREG_STORY_QA_URL, voice_active_story_id(), (unsigned)offset);

    HTTPClient http;
    http.setConnectTimeout(AREG_HTTP_CONNECT_MS);
    http.setTimeout(AREG_HTTP_READ_MS);
    if (!areg_http_begin(http, url)) {
        Serial.println("[qa] http.begin failed");
        Serial.flush();
        return result;
    }
    http.addHeader("Content-Type", "audio/wav");
    add_device_auth_headers(http);

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

// -------------------------------------------------------------
// Post-story reflection answer upload (Slice 3)
// UNVERIFIED — not compiled/flashed. See HARDENING-INTEGRATION.md §6.
// Mirrors voice_upload_question but targets the reflection-answer endpoint
// and carries questionIndex instead of the story byte offset.
// -------------------------------------------------------------
VoiceTurnResult voice_upload_reflection_answer(const uint8_t *payload, size_t length,
                                               int question_index, bool last) {
    VoiceTurnResult result;
    voice_release_last_response();

    if (!voice_wifi_is_connected()) {
        Serial.println("[post] upload: wifi not connected");
        result.http_status = -1001;
        return result;
    }
    if (payload == nullptr || length == 0) {
        Serial.println("[post] upload: empty payload");
        return result;
    }

    // `last=false` only when the dialogue has MORE questions after this one
    // — the backend then omits its goodbye line (it belongs to the final
    // round). Absent (the default) means "final", matching legacy behavior.
    char url[384];
    snprintf(url, sizeof(url), "%s?storyId=%s&questionIndex=%d%s",
             AREG_STORY_REFLECTION_URL, voice_active_story_id(), question_index,
             last ? "" : "&last=false");

    HTTPClient http;
    http.setConnectTimeout(AREG_HTTP_CONNECT_MS);
    http.setTimeout(AREG_HTTP_READ_MS);
    if (!areg_http_begin(http, url)) {
        Serial.println("[post] http.begin failed");
        Serial.flush();
        return result;
    }
    http.addHeader("Content-Type", "audio/wav");
    add_device_auth_headers(http);

    Serial.printf("[post] POST answer (%u bytes) qIndex=%d\n",
                  (unsigned)length, question_index);
    Serial.flush();
    const int status = http.POST((uint8_t *)payload, length);
    result.http_status = status;
    if (status != 200) {
        Serial.printf("[post] http POST non-200: %d\n", status);
        http.end();
        return result;
    }

    const bool read_ok = read_response_into(http, result);
    http.end();
    if (!read_ok) {
        voice_release_last_response();
        return result;
    }
    Serial.printf("[post] ack %u bytes\n", (unsigned)result.response_length);
    Serial.flush();
    return result;
}

// -------------------------------------------------------------
// Async Q&A upload (S3 dead-air mitigation)
// UNVERIFIED — not compiled/flashed. See HARDENING-INTEGRATION.md §2.
// -------------------------------------------------------------
//
// Design: the FreeRTOS upload task is pinned to CORE 0 so it owns the
// Wi-Fi TCP socket on that core. The Arduino loop() runs on CORE 1
// (default for Arduino-ESP32) and drives the thinking-bed audio there.
// Both cores share the same ESP-IDF Wi-Fi driver — sockets are
// accessible from either core — but pinning the network work to CORE 0
// prevents any scheduling jitter from the loop() watchdog.
//
// HARDWARE ASSUMPTION: ESP32-S3 two-core SMP. If a single-core variant
// is ever used (ESP32-S3FN4R2 does have two cores; the single-core
// ESP32-S0 is a different chip family). Core index 0 = PRO_CPU.
//
// PSRAM ownership (see voice_client.h comment):
//   - s_async_payload: borrowed pointer to caller-owned PSRAM. NOT freed here.
//   - s_response_buffer (module-level): allocated by the task inside
//     read_response_into(); freed by voice_release_last_response().
//   - s_async_result: value-type struct; result.response_bytes points into
//     s_response_buffer when ok==true.

// Shared state between the async upload task (CORE 0) and the polling caller
// (CORE 1 loop). #046 — the done flag + the multi-field result struct are
// handed across cores. A bare `volatile bool` is NOT a cross-core memory
// barrier on the Xtensa SMP cores: the reader could observe s_async_done==true
// while the struct write to s_async_result was not yet visible -> a TORN read
// (e.g. ok=true with a stale/null response pointer or a prior turn's length)
// = a memory-safety crash when those bytes reach the MP3 decoder. A FreeRTOS
// mutex around BOTH fields fixes it: xSemaphoreTake/Give are full memory
// fences, so a reader that sees done==true UNDER the mutex is guaranteed to
// see the fully-published result. The mutex is created in
// voice_start_question_upload_async() before xTaskCreate, so it always exists
// before the task or any poll runs.
static SemaphoreHandle_t  s_async_mutex   = nullptr;
static bool               s_async_done    = false;  // guarded by s_async_mutex
static VoiceTurnResult    s_async_result;            // guarded by s_async_mutex
// loop-task-only (set + read in start()); never touched by the task -> no guard.
static bool               s_async_started = false;

// Task parameters — set before xTaskCreate, read by the task. xTaskCreate is
// itself a publish barrier (the task starts after it returns), so these need
// no mutex. payload pointer + length are caller-owned and must remain valid
// until voice_async_upload_done() returns true.
static const uint8_t     *s_async_payload  = nullptr;
static size_t             s_async_length   = 0;
static uint32_t           s_async_offset   = 0;

// --- #046 cross-core handoff helpers (mutex = full memory barrier) ---
static void async_ensure_mutex() {
    if (s_async_mutex == nullptr) {
        s_async_mutex = xSemaphoreCreateMutex();
    }
}
// Publish the completed result + done flag atomically (called from the task
// and the synchronous fallback). The mutex give is the barrier that makes the
// whole struct visible to a reader that subsequently sees done==true.
static void async_publish_result(const VoiceTurnResult &result) {
    if (s_async_mutex != nullptr) xSemaphoreTake(s_async_mutex, portMAX_DELAY);
    s_async_result = result;
    s_async_done   = true;
    if (s_async_mutex != nullptr) xSemaphoreGive(s_async_mutex);
}

// FreeRTOS task: same logic as voice_upload_question() but writes the
// result into s_async_result and sets s_async_done on completion.
// HARDWARE ASSUMPTION: 8 KB stack is sufficient for HTTPClient + WiFiClient
// on the ESP32-S3. Increase if stack overflow occurs (configurable at call site).
static void upload_question_task(void * /*pvParams*/) {
    VoiceTurnResult result;

    if (!voice_wifi_is_connected()) {
        Serial.println("[qa-async] wifi not connected");
        result.http_status = -1001;
        async_publish_result(result);
        vTaskDelete(nullptr);
        return;
    }
    if (s_async_payload == nullptr || s_async_length == 0) {
        Serial.println("[qa-async] empty payload");
        async_publish_result(result);
        vTaskDelete(nullptr);
        return;
    }

    char url[384];
    snprintf(url, sizeof(url), "%s?storyId=%s&offset=%u",
             AREG_STORY_QA_URL, voice_active_story_id(), (unsigned)s_async_offset);

    HTTPClient http;
    http.setConnectTimeout(AREG_HTTP_CONNECT_MS);
    http.setTimeout(AREG_HTTP_READ_MS);
    if (!areg_http_begin(http, url)) {
        Serial.println("[qa-async] http.begin failed");
        Serial.flush();
        async_publish_result(result);
        vTaskDelete(nullptr);
        return;
    }
    http.addHeader("Content-Type", "audio/wav");
    add_device_auth_headers(http);

    Serial.printf("[qa-async] POST (%u bytes) offset=%u\n",
                  (unsigned)s_async_length, (unsigned)s_async_offset);
    Serial.flush();

    // HARDWARE ASSUMPTION: http.POST() from CORE 0 while CORE 1 drives I2S
    // is safe. The ESP-IDF lwIP stack is thread-safe across cores; Arduino
    // HTTPClient is not interrupt-safe but is core-reentrant when called
    // from different tasks (not the same task simultaneously).
    // NOTE: we release the prior response buffer HERE (from the task) rather
    // than from the caller, because both paths share s_response_buffer.
    // This is safe because the task runs AFTER the caller has already finished
    // playing the previous answer (the story playback was cut before record_question).
    voice_release_last_response();
    const int status = http.POST((uint8_t *)s_async_payload, s_async_length);
    result.http_status = status;
    if (status != 200) {
        Serial.printf("[qa-async] POST non-200: %d\n", status);
        http.end();
        async_publish_result(result);
        vTaskDelete(nullptr);
        return;
    }

    const bool read_ok = read_response_into(http, result);
    http.end();
    if (!read_ok) {
        voice_release_last_response();
    } else {
        Serial.printf("[qa-async] answer %u bytes\n",
                      (unsigned)result.response_length);
        Serial.flush();
    }
    async_publish_result(result);
    vTaskDelete(nullptr);
}

void voice_start_question_upload_async(const uint8_t *payload,
                                       size_t length,
                                       uint32_t offset) {
    async_ensure_mutex();  // #046 — create the cross-core barrier before the task

    // Guard: don't start a second task if one is still running.
    // Caller must wait for voice_async_upload_done() before calling again.
    if (s_async_started && !voice_async_upload_done()) {
        Serial.println("[qa-async] WARNING: prior task still running; skipping");
        Serial.flush();
        return;
    }

    // Set shared state before xTaskCreate so the task sees valid pointers.
    s_async_payload  = payload;
    s_async_length   = length;
    s_async_offset   = offset;
    s_async_started  = true;
    // Clear the done flag + any leftover result UNDER the mutex so the next
    // poll can't transiently see a stale done==true from the previous turn.
    if (s_async_mutex != nullptr) xSemaphoreTake(s_async_mutex, portMAX_DELAY);
    s_async_done   = false;
    s_async_result = VoiceTurnResult{};
    if (s_async_mutex != nullptr) xSemaphoreGive(s_async_mutex);

    // HARDWARE ASSUMPTION: stack size 8192 bytes. If stack overflow occurs
    // (monitor with uxTaskGetStackHighWaterMark), raise to 10240 or 12288.
    // HARDWARE ASSUMPTION: pinned to CORE 0 (APP_CPU_NUM = 1 on ESP32,
    // but on ESP32-S3 with Arduino-ESP32 the convention is the same:
    // PRO_CPU=0, APP_CPU=1; loop() runs on APP_CPU=1).
    // We pin to PRO_CPU_NUM (= 0) so the blocking TCP work stays off the
    // loop() core.
    BaseType_t created = xTaskCreatePinnedToCore(
        upload_question_task,
        "qa_upload",        // task name (appears in task list)
        8192,               // stack bytes — HARDWARE ASSUMPTION: sufficient
        nullptr,            // pvParameters (task reads from module globals)
        5,                  // priority: 5 = above idle (0), below Wi-Fi (10+)
        nullptr,            // task handle (we don't need to track it)
        0                   // core: 0 = PRO_CPU — HARDWARE ASSUMPTION
    );
    if (created != pdPASS) {
        Serial.println("[qa-async] xTaskCreate FAILED; falling back to sync upload");
        Serial.flush();
        // Fallback: run synchronously in the caller's context.
        // This means no thinking-bed plays, but audio_play_mp3_buffer
        // will be called when voice_get_async_result() is called.
        async_publish_result(voice_upload_question(payload, length, offset));
    }
}

bool voice_async_upload_done() {
    // #046 — read the done flag under the mutex so the barrier orders this
    // read after the task's struct publish. Mutex null = nothing started yet.
    if (s_async_mutex == nullptr) return false;
    xSemaphoreTake(s_async_mutex, portMAX_DELAY);
    const bool done = s_async_done;
    xSemaphoreGive(s_async_mutex);
    return done;
}

VoiceTurnResult voice_get_async_result() {
    // MUST only be called after voice_async_upload_done() == true. Copy under
    // the mutex so the whole struct is read consistently (no torn read).
    VoiceTurnResult copy;
    if (s_async_mutex != nullptr) xSemaphoreTake(s_async_mutex, portMAX_DELAY);
    copy = s_async_result;
    if (s_async_mutex != nullptr) xSemaphoreGive(s_async_mutex);
    return copy;
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
    if (!areg_http_begin(http, AREG_BACKEND_URL)) {
        Serial.println("[voice] continue: http.begin failed");
        return result;
    }
    add_device_auth_headers(http);
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

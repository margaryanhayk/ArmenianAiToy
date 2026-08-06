// -------------------------------------------------------------
// AregVoiceMvp.ino
//
// C1 bench proof: button-to-talk Armenian voice loop between
// one ESP32-S3 prototype and the already-shipped backend at
// POST /api/chat/audio. Buffered playback, one canned failure
// clip, five LED states, one serial latency print per turn.
//
// Everything that is not strictly needed for the three-turn
// bench demo is deliberately absent. See toy-mvp skill / C1
// firmware slice plan. No wake word. No barge-in. No retry.
// No battery. No enclosure. No provisioning UX.
// -------------------------------------------------------------
#include <Arduino.h>
#include <Adafruit_NeoPixel.h>
#include <esp_heap_caps.h>
#include <esp_system.h>
#include <esp_task_wdt.h>   // #047 — application task watchdog
#include <WiFi.h>

#include "config.h"
#include "net_transport.h"     // TLS/plain transport seam for every backend call
#include "audio_io.h"
#include "voice_client.h"
#include "canned_clip.h"
#include "diag.h"
#include "wifi_creds.h"        // B.1 — NVS cred clear (factory reset gesture)
#include "ble_provisioning.h"  // B.2 — BLE provisioning (gated; no-op when flag off)
#include "ota_foundation.h"    // Proof 2 — phone-home command poll (no OTA apply)
#include "sd_bench.h"          // microSD hardware proof (AREG_SD_BENCH_TEST builds only)
#include "content_sync.h"      // Cloud→SD story sync (AREG_CONTENT_SYNC_BENCH builds only)
#include "content_sync_test.h" // content-sync decision-logic tests (AREG_CONTENT_SYNC_TEST_BENCH only)
#include "sd_diag.h"           // standalone SD diagnostic (AREG_SD_DIAG_BENCH builds only)
#include "sd_playback.h"       // cached-MP3 SD playback (AREG_SD_PLAYBACK_BENCH builds only)
#include "answer_buttons.h"    // optional GREEN/RED answer buttons (no-op unless pins defined)
#include "offline_quiz.h"      // offline true/false quiz (AREG_OFFLINE_QUIZ_BENCH builds only)
#include "offline_games.h"     // mind-reader / buzzer / Simon (AREG_OFFLINE_GAMES_BENCH builds only)

#include "story_select.h"      // which cached story to play (index v2 + no-repeat)
#include "story_report.h"      // story-play reporting (store-and-forward to backend)
#include "story_select_test.h" // selection tests (AREG_STORY_SELECT_TEST_BENCH only)
#include <SD.h>            // FS.h + SD — read /content_index.json (already linked)
#include <ArduinoJson.h>   // JsonDocument/deserializeJson (already a project dep)

// #047 — hang-protection tunables. Defaulted here so the build never depends
// on config.h carrying them; overridable in config.h. See config.h.example.
#ifndef AREG_WDT_TIMEOUT_S
#define AREG_WDT_TIMEOUT_S            60
#endif
#ifndef AREG_ASYNC_UPLOAD_TIMEOUT_MS
#define AREG_ASYNC_UPLOAD_TIMEOUT_MS  45000
#endif
// B.2 — hold the button this long at power-on to forget the saved Wi-Fi and
// re-enter BLE provisioning. Only consulted in the AREG_USE_BLE_PROVISIONING build.
#ifndef AREG_PROV_RESET_HOLD_MS
#define AREG_PROV_RESET_HOLD_MS       5000
#endif
// Phase A.1 (toy side) — interval between idle presence heartbeats. The backend
// presence threshold is ~180 s and LastSeenAt is throttled to 60 s, so 60 s
// keeps the parent app's online dot fresh without chatter.
#ifndef AREG_HEARTBEAT_INTERVAL_MS
#define AREG_HEARTBEAT_INTERVAL_MS    60000UL
#endif
// B.3 — a provisioned toy that cannot rejoin Wi-Fi for this long (ms) auto-opens
// BLE provisioning so a moved toy / new router can be re-onboarded with no
// gesture. Only consulted in the AREG_USE_BLE_PROVISIONING build. 5 min default.
#ifndef AREG_PROV_FALLBACK_AFTER_MS
#define AREG_PROV_FALLBACK_AFTER_MS   300000UL
#endif

// --- State machine -------------------------------------------
enum State {
    ST_IDLE = 0,
    ST_RECORDING,
    ST_UPLOADING,
    ST_PLAYING,
    ST_ERROR
};

static State s_state = ST_IDLE;

// --- Diag helpers (additive, no behavior change) -------------
// Bench-bring-up diagnostic logging only. Every Serial line in
// this section is followed by Serial.flush() so the last log
// before a hang/reset reaches the host even when the chip is
// about to disappear (native USB CDC drops on chip reset; see
// the 2026-05-25 hardware bring-up diagnosis report).
//
// NOTE: this block sits AFTER the State enum on purpose. The
// Arduino IDE auto-generates forward declarations for every
// free function in the .ino and injects them just before the
// first defined function in the file. Placing these helpers
// above the enum makes the injected prototypes for
// `led_for_state(State)` / `transition_to(State)` land before
// `State` is visible and the build fails.
static const char *reset_reason_name(esp_reset_reason_t r) {
    switch (r) {
        case ESP_RST_POWERON:   return "POWERON";
        case ESP_RST_EXT:       return "EXT";
        case ESP_RST_SW:        return "SW";
        case ESP_RST_PANIC:     return "PANIC";
        case ESP_RST_INT_WDT:   return "INT_WDT";
        case ESP_RST_TASK_WDT:  return "TASK_WDT";
        case ESP_RST_WDT:       return "WDT";
        case ESP_RST_DEEPSLEEP: return "DEEPSLEEP";
        case ESP_RST_BROWNOUT:  return "BROWNOUT";
        case ESP_RST_SDIO:      return "SDIO";
        default:                return "UNKNOWN";
    }
}

static void wifi_event_handler(WiFiEvent_t event, WiFiEventInfo_t info) {
    switch (event) {
        case ARDUINO_EVENT_WIFI_STA_CONNECTED:
            Serial.println("[wifi] event=connected");
            Serial.flush();
            break;
        case ARDUINO_EVENT_WIFI_STA_GOT_IP:
            Serial.println("[wifi] event=got_ip");
            Serial.flush();
            break;
        case ARDUINO_EVENT_WIFI_STA_DISCONNECTED:
            Serial.printf("[wifi] event=disconnected reason=%d\n",
                          info.wifi_sta_disconnected.reason);
            Serial.flush();
            break;
        default:
            break;
    }
}

static uint32_t s_last_heartbeat_ms = 0;       // serial [alive] log (5 s)
static uint32_t s_last_net_heartbeat_ms = 0;   // network presence POST (A.1)

// --- LED -----------------------------------------------------
static Adafruit_NeoPixel s_led(1, AREG_PIN_LED, NEO_GRB + NEO_KHZ800);

static void led_set(uint8_t r, uint8_t g, uint8_t b) {
    s_led.setPixelColor(0, s_led.Color(r, g, b));
    s_led.show();
}
static void led_for_state(State st) {
    switch (st) {
    case ST_IDLE:
        led_set(AREG_LED_IDLE_R, AREG_LED_IDLE_G, AREG_LED_IDLE_B);
        break;
    case ST_RECORDING:
        led_set(AREG_LED_REC_R, AREG_LED_REC_G, AREG_LED_REC_B);
        break;
    case ST_UPLOADING:
        led_set(AREG_LED_UPLOAD_R, AREG_LED_UPLOAD_G, AREG_LED_UPLOAD_B);
        break;
    case ST_PLAYING:
        led_set(AREG_LED_PLAY_R, AREG_LED_PLAY_G, AREG_LED_PLAY_B);
        break;
    case ST_ERROR:
        led_set(AREG_LED_ERROR_R, AREG_LED_ERROR_G, AREG_LED_ERROR_B);
        break;
    }
}
static void transition_to(State next) {
    Serial.printf("[state] %d -> %d\n", (int)s_state, (int)next);
    Serial.flush();
    s_state = next;
    led_for_state(next);
}

// --- Button --------------------------------------------------
// Polled every loop iteration; press/release edges are defined
// by 30 ms of stable contact. No interrupts — simpler and the
// loop runs at >>100 Hz even while not recording.
static bool s_button_pressed = false;
static uint32_t s_last_edge_ms = 0;
static uint8_t s_raw_last = HIGH;

static void button_begin() {
    pinMode(AREG_PIN_BUTTON, INPUT_PULLUP);
    s_raw_last = digitalRead(AREG_PIN_BUTTON);
    s_button_pressed = false;
}

// Returns 'P' on a press edge, 'R' on a release edge, 0 otherwise.
static char button_poll() {
    uint8_t raw = digitalRead(AREG_PIN_BUTTON);
    if (raw != s_raw_last) {
        s_last_edge_ms = millis();
        s_raw_last = raw;
    }
    if ((millis() - s_last_edge_ms) < AREG_BUTTON_DEBOUNCE_MS) {
        return 0;
    }
    bool now_pressed = (raw == LOW);
    if (now_pressed != s_button_pressed) {
        s_button_pressed = now_pressed;
        return now_pressed ? 'P' : 'R';
    }
    return 0;
}
static bool button_is_released() {
    // Used by audio_mic_capture's should_stop callback. Skipped
    // debounce here — capture loop polls often enough that a
    // single stray sample is not a concern.
    return digitalRead(AREG_PIN_BUTTON) != LOW;
}

// --- PSRAM buffers -------------------------------------------
// Capture buffer is sized for the 15 s hard cap. Allocated once
// at boot. Playback response buffer is allocated per-turn by
// voice_client (to bound process-wide memory residency).
static int16_t *s_capture_buf = nullptr;

// --- Latency print ------------------------------------------
static uint32_t s_release_ms = 0;
static bool s_awaiting_first_play_ms = false;

// --- Story playback position (continuous stream + barge-in) --
// 0 = play from the beginning; >0 = the byte offset saved at the
// last barge-in. Encodes "paused, resume here" without a new state.
static uint32_t s_story_offset = 0;

// --- Which story this session is playing ---------------------
// Set ONCE at a new-story boundary and held for the whole session, so
// pause/resume, a Q&A barge-in, and a stream retry all stay on the SAME
// story. Empty means "no index selection" — the pack/Wi-Fi fallback
// chain then behaves exactly as it did before selection existed.
//
// s_story_offset is what distinguishes the two entry cases: 0 means a
// fresh story (select), >0 means resuming the one that was paused (do
// NOT re-select). That is the existing sticky-pause mechanism, reused
// rather than a new state flag.
static char s_current_story_id[CS_MAX_STORY_ID_LEN + 1] = "";

// --- Welcome flow: the child chose this story out loud -------
// One-shot. Set with s_current_story_id just before handle_story_session,
// consumed (and cleared) by story_pick_for_session so the very next press
// goes back to the normal rotation.
static bool s_story_preselected = false;

// --- Failure clip playback ----------------------------------
static void play_canned_failure_clip() {
    if (canned_clip_mp3_len < 8) {
        // Developer hasn't regenerated the clip yet. Skip
        // playback silently rather than crash the decoder.
        Serial.println("[fail] canned clip stub is empty; skipping playback");
        return;
    }
    audio_speaker_begin();
    audio_play_mp3_buffer(canned_clip_mp3, canned_clip_mp3_len);
}

// --- One full RECORDING -> UPLOADING -> PLAYING cycle --------

static void handle_record_upload_playback() {
    DIAG_MARK(210, "handler_enter");
    Serial.printf("[diag] before_record heap=%u psram=%u\n",
                  (unsigned)ESP.getFreeHeap(),
                  (unsigned)ESP.getFreePsram());
    Serial.flush();
    transition_to(ST_RECORDING);

    // Fresh mic session per turn. Torn down before playback.
    DIAG_MARK(220, "audio_mic_begin_before");
    if (!audio_mic_begin()) {
        Serial.println("[cap] mic_begin failed");
        Serial.flush();
        transition_to(ST_ERROR);
        play_canned_failure_clip();
        transition_to(ST_IDLE);
        return;
    }

    DIAG_MARK(221, "audio_mic_begin_after_ok");
    const size_t max_samples =
        AREG_RECORD_BUFFER_BYTES / sizeof(int16_t);
    DIAG_MARK(230, "audio_mic_capture_before");
    const size_t captured_samples = audio_mic_capture(
        s_capture_buf, max_samples,
        AREG_MAX_RECORD_MS, button_is_released);
    DIAG_MARK(231, "audio_mic_capture_after");
    DIAG_MARK(240, "audio_mic_end_before");
    audio_mic_end();
    DIAG_MARK(241, "audio_mic_end_after");
    // audio_mic_capture returns when button_is_released sees the
    // button go high — that edge IS the release we want to
    // anchor the latency measurement against. Everything before
    // this point (press-to-capture-start latency, capture
    // duration) is not what the child perceives.
    s_release_ms = millis();
    Serial.printf("[cap] samples=%u\n", (unsigned)captured_samples);
    Serial.printf("[diag] after_record bytes=%u heap=%u psram=%u\n",
                  (unsigned)(captured_samples * sizeof(int16_t)),
                  (unsigned)ESP.getFreeHeap(),
                  (unsigned)ESP.getFreePsram());
    Serial.flush();

    // Enforce the minimum-press guard AFTER capture so the
    // mic path is at least exercised on every press even when
    // the capture is too short to upload. Prevents a 20 ms
    // tap from hitting the backend.
    const uint32_t ms_held = (captured_samples * 1000) / AREG_SAMPLE_RATE_HZ;
    if (ms_held < AREG_MIN_RECORD_MS) {
        Serial.printf("[cap] press too short (%u ms); ignoring\n",
                      (unsigned)ms_held);
        transition_to(ST_IDLE);
        return;
    }

    // Compose the request body: 44-byte WAV header + PCM.
    // Header bytes live at offset 0 of the send buffer; we
    // reuse the already-allocated capture buffer by shifting
    // samples right 44 bytes before send. That would double
    // the time; instead we allocate a small header scratch
    // and do two HTTPClient-style writes via a stack-assembled
    // payload copy into PSRAM.
    const size_t pcm_bytes = captured_samples * sizeof(int16_t);
    const size_t payload_bytes = 44 + pcm_bytes;
    uint8_t *payload = (uint8_t *)heap_caps_malloc(payload_bytes, MALLOC_CAP_SPIRAM);
    if (payload == nullptr) {
        Serial.println("[cap] psram alloc for payload failed");
        transition_to(ST_ERROR);
        play_canned_failure_clip();
        transition_to(ST_IDLE);
        return;
    }
    audio_write_wav_header(payload, (uint32_t)captured_samples);
    memcpy(payload + 44, s_capture_buf, pcm_bytes);

    Serial.printf("[diag] before_upload bytes=%u\n",
                  (unsigned)payload_bytes);
    Serial.flush();
    DIAG_MARK(250, "voice_upload_before");
    transition_to(ST_UPLOADING);
    s_awaiting_first_play_ms = true;

    VoiceTurnResult turn = voice_upload_turn(payload, payload_bytes);
    DIAG_MARK(251, "voice_upload_after");
    heap_caps_free(payload);

    Serial.printf("[diag] after_upload ok=%s http=%d bytes=%u\n",
                  turn.ok ? "true" : "false",
                  turn.http_status,
                  (unsigned)turn.response_length);
    Serial.flush();

    if (!turn.ok) {
        Serial.printf("[upload] failed (status=%d)\n", turn.http_status);
        voice_release_last_response();
        transition_to(ST_ERROR);
        play_canned_failure_clip();
        transition_to(ST_IDLE);
        return;
    }

    transition_to(ST_PLAYING);
    if (s_awaiting_first_play_ms) {
        const uint32_t first_audio_ms = millis() - s_release_ms;
        Serial.printf("[latency] release->play_begin_ms=%u\n",
                      (unsigned)first_audio_ms);
        s_awaiting_first_play_ms = false;
    }

    Serial.printf("[diag] before_playback bytes=%u\n",
                  (unsigned)turn.response_length);
    Serial.flush();
    DIAG_MARK(260, "playback_before");

    audio_speaker_begin();
    const bool played = audio_play_mp3_buffer(
        turn.response_bytes, turn.response_length);
    DIAG_MARK(261, "playback_after");
    voice_release_last_response();

    Serial.printf("[diag] after_playback heap=%u psram=%u played=%s\n",
                  (unsigned)ESP.getFreeHeap(),
                  (unsigned)ESP.getFreePsram(),
                  played ? "true" : "false");
    Serial.flush();

    if (!played) {
        Serial.println("[play] decoder error");
        Serial.flush();
        transition_to(ST_ERROR);
        play_canned_failure_clip();
    } else {
        // Hands-free library-story autoplay. While the backend says the
        // story has more (X-Areg-Continue: 1), fetch and play the next
        // segment with no button press. Stays in ST_PLAYING throughout
        // (button is ignored while playing), so the whole story tells
        // itself end-to-end. Capped by AREG_MAX_AUTOPLAY_SEGMENTS.
        int autoplay_guard = 0;
        while (turn.continue_more && autoplay_guard < AREG_MAX_AUTOPLAY_SEGMENTS) {
            autoplay_guard++;
            Serial.printf("[autoplay] fetching next segment #%d\n", autoplay_guard);
            Serial.flush();
            turn = voice_continue_turn();
            if (!turn.ok) {
                Serial.printf("[autoplay] stop (http=%d)\n", turn.http_status);
                Serial.flush();
                voice_release_last_response();
                break;
            }
            audio_speaker_begin();
            const bool seg_played = audio_play_mp3_buffer(
                turn.response_bytes, turn.response_length);
            voice_release_last_response();
            if (!seg_played) {
                Serial.println("[autoplay] decode error");
                Serial.flush();
                break;
            }
        }
        if (autoplay_guard >= AREG_MAX_AUTOPLAY_SEGMENTS) {
            Serial.println("[autoplay] hit segment cap; stopping");
            Serial.flush();
        }
    }
    transition_to(ST_IDLE);
}

// --- Continuous story playback (stream + barge-in + resume) --

// Polled inside audio_play_story_stream every decode iteration; a
// fresh press edge DURING playback is a barge-in (the start press has
// already been released by then).
static bool story_barge_in_poll() {
    return button_poll() == 'P';
}

// Records the child's question while the button is held and returns the
// number of 16-bit samples captured (0 on mic failure). Shared by the
// barge-in Q&A path.
static size_t record_question() {
    if (!audio_mic_begin()) {
        Serial.println("[qa] mic begin failed");
        Serial.flush();
        audio_mic_end();
        return 0;
    }
    const size_t max_samples = AREG_RECORD_BUFFER_BYTES / sizeof(int16_t);
    const size_t captured = audio_mic_capture(
        s_capture_buf, max_samples, AREG_MAX_RECORD_MS, button_is_released);
    audio_mic_end();
    return captured;
}

// Post-story flow (Slice 3). UNVERIFIED — not compiled/flashed.
//
// Called once the story reaches its natural end. Plays the offline conclusion,
// asks the offline reflection question, then — ONLINE ONLY — opens a listening
// window for the child's spoken answer and plays the warm acknowledgement the
// backend returns (POST /api/chat/story-qa/reflection-answer). Offline, the
// answer step is skipped: the question's answer needs the cloud (STT + GPT).
// Every clip comes from the SD content pack (Slice 1 layout); each step
// self-gates on the file being present, so this is a safe no-op when playing
// the Wi-Fi stream (no pack on the card).
static void handle_post_story_flow() {
    // B2 — clips for the ACTIVE story come from the content-sync cache
    // (index-resolved, verified) with the legacy compile-time SD-pack
    // paths as fallback. This is what makes the after-story talk work for
    // synced stories: the pack paths only ever existed for AREG_STORY_ID,
    // so a cache-selected story used to skip the whole flow silently.
    const char *post_story_id = voice_active_story_id();

    // 1. Summary / conclusion (offline) — the toy's short "what the story
    //    teaches" line, in the storyteller voice when the clip is synced.
    char summary_path[CS_MAX_PATH_LEN];
    const char *summary_clip = nullptr;
    if (story_select_resolve_clip_path(post_story_id, "summary",
                                       summary_path, sizeof(summary_path))) {
        summary_clip = summary_path;
    } else if (audio_sd_has_file(AREG_SD_STORY_CONCLUSION)) {
        summary_clip = AREG_SD_STORY_CONCLUSION;
    }
    if (summary_clip != nullptr) {
        transition_to(ST_PLAYING);
        audio_speaker_begin();
        Serial.printf("[post] summary/conclusion (%s)\n", summary_clip);
        Serial.flush();
        audio_play_story_file(summary_clip, 0, nullptr, nullptr);
    }

    // 2..N — the reflection DIALOGUE (owner request 2026-08-03): up to 3
    // questions per story (clip kinds question / question1 / question2),
    // each round = ask → listen → record → upload → play the backend's
    // reaction + conclusion. The FINAL round's reply carries the goodbye
    // line (the `last` flag tells the backend which round that is). The
    // child is never badgered: no press in the window, a too-short answer,
    // or any failure ends the dialogue quietly.
    for (int q = 0; q < 3; q++) {
        // Resolve THIS round's question clip. Question 0 keeps the legacy
        // SD-pack fallback; later questions exist only via content sync.
        char question_path[CS_MAX_PATH_LEN];
        const char *question_clip = nullptr;
        const char *kind = cs_question_clip_kind(q);
        if (kind != nullptr
            && story_select_resolve_clip_path(post_story_id, kind,
                                              question_path, sizeof(question_path))) {
            question_clip = question_path;
        } else if (q == 0 && audio_sd_has_file(AREG_SD_STORY_QUESTION0)) {
            question_clip = AREG_SD_STORY_QUESTION0;
        }
        if (question_clip == nullptr) {
            // No clip for this round → the dialogue is over (round 0 with no
            // clip means the story ships no reflection at all).
            if (q == 0) return;
            break;
        }

        // Is there a NEXT question after this one? Decides the `last` flag
        // so the backend appends the goodbye exactly once per dialogue.
        bool has_next = false;
        if (q < 2) {
            char next_path[CS_MAX_PATH_LEN];
            const char *next_kind = cs_question_clip_kind(q + 1);
            has_next = next_kind != nullptr
                && story_select_resolve_clip_path(post_story_id, next_kind,
                                                  next_path, sizeof(next_path));
        }

        transition_to(ST_PLAYING);
        audio_speaker_begin();
        Serial.printf("[post] question %d (%s)\n", q, question_clip);
        Serial.flush();
        audio_play_story_file(question_clip, 0, nullptr, nullptr);

        // The ANSWER needs the cloud (STT + the bounded reaction). Offline →
        // optional close, stop the dialogue.
        if (!voice_wifi_is_connected()) {
            Serial.println("[post] offline — answer needs connectivity; closing");
            Serial.flush();
            if (audio_sd_has_file(AREG_SD_OFFLINE_CLOSE)) {
                audio_speaker_begin();
                audio_play_story_file(AREG_SD_OFFLINE_CLOSE, 0, nullptr, nullptr);
            }
            led_for_state(ST_IDLE);
            return;
        }

        // Listening window: the recording color is the "your turn" cue. No
        // press → quiet close (never force an answer from a small child).
        Serial.printf("[post] listening for answer %d (press & hold to talk)\n", q);
        Serial.flush();
        led_for_state(ST_RECORDING);
        bool got_press = false;
        const uint32_t listen_started = millis();
        while (millis() - listen_started < AREG_REFLECTION_LISTEN_MS) {
            if (button_poll() == 'P') {
                got_press = true;
                break;
            }
            delay(AREG_BUTTON_POLL_MS);
        }
        if (!got_press) {
            Serial.println("[post] no answer in window; closing quietly");
            Serial.flush();
            led_for_state(ST_IDLE);
            return;
        }

        // Record the answer while held, then POST to the reflection endpoint.
        transition_to(ST_RECORDING);
        const size_t captured = record_question();
        const uint32_t ms_held = (captured * 1000) / AREG_SAMPLE_RATE_HZ;
        if (ms_held < AREG_MIN_RECORD_MS) {
            Serial.printf("[post] answer too short (%u ms); closing\n", (unsigned)ms_held);
            Serial.flush();
            led_for_state(ST_IDLE);
            return;
        }

        const size_t pcm_bytes = captured * sizeof(int16_t);
        const size_t payload_bytes = 44 + pcm_bytes;
        uint8_t *payload = (uint8_t *)heap_caps_malloc(payload_bytes, MALLOC_CAP_SPIRAM);
        if (payload == nullptr) {
            Serial.println("[post] payload alloc failed; closing");
            Serial.flush();
            led_for_state(ST_IDLE);
            return;
        }
        audio_write_wav_header(payload, (uint32_t)captured);
        memcpy(payload + 44, s_capture_buf, pcm_bytes);

        transition_to(ST_UPLOADING);
        audio_speaker_begin();
        audio_play_thinking_earcon();  // immediate acoustic ack while we upload
        Serial.printf("[post] uploading answer %d (last=%d)\n", q, has_next ? 0 : 1);
        Serial.flush();

        VoiceTurnResult turn = voice_upload_reflection_answer(
            payload, payload_bytes, q, /*last=*/!has_next);
        heap_caps_free(payload);
        payload = nullptr;

        if (turn.ok) {
            transition_to(ST_PLAYING);
            audio_speaker_begin();
            audio_play_mp3_buffer(turn.response_bytes, turn.response_length);
            Serial.printf("[post] reply %d played\n", q);
            Serial.flush();
        } else {
            Serial.printf("[post] reflection upload failed (status=%d)\n", turn.http_status);
            Serial.flush();
            voice_release_last_response();
            led_for_state(ST_IDLE);
            return;  // a failed round ends the dialogue quietly
        }
        voice_release_last_response();

        if (!has_next) {
            break;   // that reply carried the goodbye — dialogue complete
        }
    }
    led_for_state(ST_IDLE);
}

// ---------------------------------------------------------------
// Welcome flow — the toy's opening.
//
// Greet → "what shall we do?" → the child answers OUT LOUD → offer a
// story by name → "yes" → play it.
//
// Shape is copied from handle_post_story_flow above (play a clip, open a
// listening window, record, upload, act) rather than invented: that shape
// is already shipped and hardware-verified, and reusing it means no new
// state enum, no new LED vocabulary, and no state machine — this is a
// blocking call from the IDLE branch exactly like a story session.
//
// EVERYTHING the toy says here is a pre-rendered clip from its SD card,
// so speaking works with no network, no cost and no delay. Only HEARING
// the child needs the cloud.
//
// Owner decision: the child chooses by VOICE only. So there is no button
// menu anywhere below. When the toy is offline, or mishears twice, it
// does not open a second way to choose — it says one short line and
// starts a story, which is exactly what a press did before this existed.
// ---------------------------------------------------------------

static void handle_story_session();   // defined below; the flow ends in it

// The ONE eligible-story table in the sketch (~10 KB), shared by the
// welcome flow's offer loop and story_pick_for_session. They are never
// live at the same time: welcome_offer_story finishes with it — it has
// already copied the chosen id into s_current_story_id — before calling
// handle_story_session, which is what reaches the other user. Anything
// added AFTER that call must not expect this table to still hold the
// offer pool.
static CsStory s_eligible_stories[CS_MAX_STORIES];

// Plays a device-global clip by id if it is on the card. Returns false
// when the clip has not been synced yet, which every caller treats as
// "skip this line" — a half-synced card degrades quietly instead of
// going silent mid-sentence.
static bool welcome_say(const char *voice_id) {
    char path[CS_MAX_PATH_LEN];
    if (!voice_clip_resolve_path(voice_id, path, sizeof(path))) {
        Serial.printf("[welcome] clip %s not on card — skipping\n", voice_id);
        Serial.flush();
        return false;
    }
    transition_to(ST_PLAYING);
    audio_speaker_begin();
    audio_play_story_file(path, 0, nullptr, nullptr);
    return true;
}

// Opens a listening window, records while the button is held, and posts
// the answer. Fills `out_intent` with one of the eight bounded tokens.
//
// Returns false ONLY for silence — nobody pressed, or the press was too
// short to be speech. Silence is not a failed attempt: at power-on it
// usually means nobody is there, and a toy that keeps asking an empty
// room is the opposite of what a parent wants.
// keep_payload / keep_len: when non-null, ownership of the recorded WAV
// payload transfers to the caller on a successful listen (so the child's
// own words can open an online chat session — see
// handle_online_chat_session). Caller frees with heap_caps_free. Pass
// nullptr/nullptr for the original post-and-free behavior. Not default
// arguments on purpose: the Arduino auto-prototype generator mishandles
// them.
static bool welcome_listen(const char *expect, char *out_intent, size_t out_len,
                           uint8_t **keep_payload, size_t *keep_len) {
    out_intent[0] = '\0';
    if (keep_payload != nullptr) *keep_payload = nullptr;
    if (keep_len != nullptr) *keep_len = 0;

    Serial.printf("[welcome] listening (%s)\n", expect);
    Serial.flush();
    led_for_state(ST_RECORDING);          // the "your turn" cue
    bool got_press = false;
    const uint32_t listen_started = millis();
    while (millis() - listen_started < AREG_WELCOME_LISTEN_MS) {
        if (button_poll() == 'P') {
            got_press = true;
            break;
        }
        delay(AREG_BUTTON_POLL_MS);
        esp_task_wdt_reset();
    }
    if (!got_press) {
        Serial.println("[welcome] no answer — closing quietly");
        Serial.flush();
        return false;
    }

    transition_to(ST_RECORDING);
    const size_t captured = record_question();
    const uint32_t ms_held = (captured * 1000) / AREG_SAMPLE_RATE_HZ;
    if (ms_held < AREG_MIN_RECORD_MS) {
        Serial.printf("[welcome] answer too short (%u ms)\n", (unsigned)ms_held);
        Serial.flush();
        return false;
    }

    const size_t pcm_bytes = captured * sizeof(int16_t);
    const size_t payload_bytes = 44 + pcm_bytes;
    uint8_t *payload = (uint8_t *)heap_caps_malloc(payload_bytes, MALLOC_CAP_SPIRAM);
    if (payload == nullptr) {
        Serial.println("[welcome] payload alloc failed");
        Serial.flush();
        cs_copy_bounded(out_intent, out_len, "unknown");
        return true;   // heard something, could not ask — counts as an attempt
    }
    audio_write_wav_header(payload, (uint32_t)captured);
    memcpy(payload + 44, s_capture_buf, pcm_bytes);

    transition_to(ST_UPLOADING);
    audio_speaker_begin();
    audio_play_thinking_earcon();   // immediate acoustic ack while we upload

    if (!voice_post_voice_intent(payload, payload_bytes, expect,
                                 out_intent, out_len)) {
        cs_copy_bounded(out_intent, out_len, "unknown");
    }
    if (keep_payload != nullptr) {
        // Ownership transfers — the caller will open an online chat
        // session with the child's own recorded words.
        *keep_payload = payload;
        if (keep_len != nullptr) *keep_len = payload_bytes;
    } else {
        heap_caps_free(payload);
    }
    Serial.printf("[welcome] intent=%s\n", out_intent);
    Serial.flush();
    return true;
}

// --- Online multi-turn chat session (game / riddle / curiosity) -----
// The welcome flow lands here when the child asked for a mode the toy
// holds no offline content for. The recorded utterance itself opens the
// session — POSTed to /api/chat/audio, where the backend's ModeDetector
// routes «խաղանք» to Game (or riddle/curiosity) and speaks the opener.
// Then the loop is: play reply → press-to-talk within the listen window
// → upload → play, until the child stops answering. Silence closes the
// session quietly (never badger); the turn cap bounds cost. The parent
// gates (pause / bedtime / per-mode flags) are enforced server-side on
// every single turn.
//
// Takes ownership of `payload` (PSRAM); frees it on every path.

#ifndef AREG_CHAT_LISTEN_MS
#define AREG_CHAT_LISTEN_MS 12000UL       // child's window to answer, ms
#endif
#ifndef AREG_CHAT_SESSION_MAX_TURNS
#define AREG_CHAT_SESSION_MAX_TURNS 30    // cost-bound per session
#endif

static void handle_online_chat_session(uint8_t *payload, size_t payload_len) {
    for (int turn_no = 1; turn_no <= AREG_CHAT_SESSION_MAX_TURNS; turn_no++) {
        transition_to(ST_UPLOADING);
        audio_speaker_begin();
        audio_play_thinking_earcon();   // immediate acoustic ack while we upload

        VoiceTurnResult turn = voice_upload_turn(payload, payload_len);
        heap_caps_free(payload);
        payload = nullptr;
        if (!turn.ok) {
            Serial.printf("[chat] upload failed (http=%d)\n", turn.http_status);
            Serial.flush();
            voice_release_last_response();
            transition_to(ST_ERROR);
            play_canned_failure_clip();
            break;
        }

        transition_to(ST_PLAYING);
        const bool played = audio_play_mp3_buffer(
            turn.response_bytes, turn.response_length);
        voice_release_last_response();
        if (!played) {
            Serial.println("[chat] decoder error");
            Serial.flush();
            transition_to(ST_ERROR);
            play_canned_failure_clip();
            break;
        }
        if (turn.continue_more) {
            // Library-autoplay marker on a chat session — games and
            // riddles are not library stories; log and ignore.
            Serial.println("[chat] unexpected continue flag — ignoring");
        }

        // The child's turn: press-to-talk within the window, else the
        // session is over. Longer window than the welcome menu — a game
        // answer may need a moment's thought.
        Serial.printf("[chat] turn %d played — listening\n", turn_no);
        Serial.flush();
        led_for_state(ST_RECORDING);
        bool got_press = false;
        const uint32_t started = millis();
        while (millis() - started < AREG_CHAT_LISTEN_MS) {
            if (button_poll() == 'P') { got_press = true; break; }
            delay(AREG_BUTTON_POLL_MS);
            esp_task_wdt_reset();
        }
        if (!got_press) {
            Serial.println("[chat] no answer — session over");
            Serial.flush();
            break;
        }

        transition_to(ST_RECORDING);
        const size_t captured = record_question();
        const uint32_t ms_held = (captured * 1000) / AREG_SAMPLE_RATE_HZ;
        if (ms_held < AREG_MIN_RECORD_MS) {
            Serial.printf("[chat] answer too short (%u ms) — session over\n",
                          (unsigned)ms_held);
            Serial.flush();
            break;
        }
        const size_t pcm_bytes = captured * sizeof(int16_t);
        payload_len = 44 + pcm_bytes;
        payload = (uint8_t *)heap_caps_malloc(payload_len, MALLOC_CAP_SPIRAM);
        if (payload == nullptr) {
            Serial.println("[chat] payload alloc failed — session over");
            Serial.flush();
            break;
        }
        audio_write_wav_header(payload, (uint32_t)captured);
        memcpy(payload + 44, s_capture_buf, pcm_bytes);
        // Loop continues: upload this answer as the next turn.
    }
    if (payload != nullptr) heap_caps_free(payload);
    led_for_state(ST_IDLE);
}

// Offers stories by name until the child says yes, then plays one.
// Always ends by starting SOME story — falling silent after asking a
// child what they want is worse than playing something.
static void welcome_offer_story() {
    // Filtered IN PLACE in the shared table: the offer pool is a subset
    // of the eligible list, so a second CsStory[CS_MAX_STORIES] would be
    // ~10 KB of .bss for nothing.
    CsStory *pool = s_eligible_stories;
    const int count = story_select_load_eligible(pool, CS_MAX_STORIES);
    if (count <= 0) {
        Serial.println("[welcome] no cached stories — falling through to the story session");
        Serial.flush();
        handle_story_session();
        return;
    }

    // Prefer stories the child has not heard. When every one has been
    // heard we do NOT quietly forget — we say so, with the reoffer line.
    int pool_count = 0;
    for (int i = 0; i < count; i++) {
        if (!story_heard_contains(pool[i].story_id)) {
            if (pool_count != i) pool[pool_count] = pool[i];
            pool_count++;
        }
    }
    const bool all_heard = (pool_count == 0);
    if (all_heard) {
        pool_count = count;   // nothing was removed, so the table is intact
    }
    Serial.printf("[welcome] offering from %d %s stories\n",
                  pool_count, all_heard ? "already-heard" : "unheard");
    Serial.flush();

    char chosen[CS_MAX_STORY_ID_LEN + 1] = "";
    for (int attempt = 0; attempt < AREG_WELCOME_MAX_OFFERS && pool_count > 0; attempt++) {
        if (!story_select_pick(pool, pool_count, chosen, sizeof(chosen))) {
            break;
        }
        char clip[CS_MAX_PATH_LEN];
        const char *kind = all_heard ? CS_CLIP_KIND_REOFFER : CS_CLIP_KIND_OFFER;
        if (!story_select_resolve_clip_path(chosen, kind, clip, sizeof(clip))) {
            // No spoken offer rendered for this story yet. Never let a
            // missing recording stop a child hearing a story: just play it.
            Serial.printf("[welcome] no %s clip for %s — playing it\n", kind, chosen);
            Serial.flush();
            break;
        }
        transition_to(ST_PLAYING);
        audio_speaker_begin();
        audio_play_story_file(clip, 0, nullptr, nullptr);

        if (!voice_wifi_is_connected()) {
            break;   // cannot hear a yes — just play what we offered
        }

        char intent[16];
        if (!welcome_listen("yesno", intent, sizeof(intent), nullptr, nullptr)) {
            led_for_state(ST_IDLE);
            return;   // silence — do not start a story into an empty room
        }
        if (strcmp(intent, "yes") == 0) {
            break;
        }
        if (strcmp(intent, "no") != 0) {
            break;   // not understood — stop asking and offer what we have
        }
        // A "no" — drop it from the pool and offer the next one.
        for (int i = 0; i < pool_count; i++) {
            if (cs_story_ids_equal(pool[i].story_id, chosen)) {
                for (int j = i + 1; j < pool_count; j++) pool[j - 1] = pool[j];
                pool_count--;
                break;
            }
        }
        chosen[0] = '\0';
    }

    if (chosen[0] == '\0') {
        // Every offer was refused, or nothing resolved. Play the rotation's
        // pick rather than leaving the child with nothing.
        welcome_say(CS_VOICE_ID_JUST_STORY);
        handle_story_session();
        return;
    }

    s_story_offset = 0;
    cs_copy_bounded(s_current_story_id, sizeof(s_current_story_id), chosen);
    s_story_preselected = true;
    handle_story_session();
}

static void handle_welcome_flow() {
    // ---- preconditions: return SILENTLY, no sound, no LED change ----
    // A paused toy is fully silent, and the greeting is the first thing
    // that would break that promise. The pause flag is seeded from NVS in
    // setup() and refreshed by a heartbeat just before this runs, so an
    // offline toy still honors a pause set days ago.
    if (voice_is_paused()) {
        Serial.println("[welcome] toy is paused — staying silent");
        Serial.flush();
        return;
    }
    // Inside the bedtime window the toy's job is to be quiet (or play
    // music on a press). A cheerful greeting at 21:30 is exactly the kind
    // of thing that loses a parent's trust.
    if (voice_in_bedtime_window()) {
        Serial.println("[welcome] bedtime window — staying silent");
        Serial.flush();
        return;
    }
    if (!audio_sd_available()) {
        return;   // every line lives on the card; there is nothing to say
    }

    // ---- 1. greeting ----
    char greeting[CS_MAX_PATH_LEN];
    if (voice_clip_next_greeting(greeting, sizeof(greeting))) {
        transition_to(ST_PLAYING);
        audio_speaker_begin();
        // Barge-in allowed: an impatient child pressing during the hello
        // should move things along, not be ignored.
        audio_play_story_file(greeting, 0, story_barge_in_poll, nullptr);
    }

    // Story disabled at the device AND nothing else to offer → the
    // greeting was the whole interaction. Never offer a mode the parent
    // switched off, and never promise a story we are not allowed to tell.
    const bool story_ok     = story_select_mode_enabled('s');
    const bool game_ok      = story_select_mode_enabled('g');
    const bool riddle_ok    = story_select_mode_enabled('r');
    const bool curiosity_ok = story_select_mode_enabled('c');
    if (!story_ok && !game_ok && !riddle_ok && !curiosity_ok) {
        Serial.println("[welcome] every mode disabled — greeting only");
        Serial.flush();
        led_for_state(ST_IDLE);
        return;
    }

    // ---- 2. offline short-circuit ----
    // Hearing the child needs the cloud and there is no offline path to
    // it. Owner decision was voice-only, so the answer is not a second
    // menu — it is one short line and a story.
    if (!voice_wifi_is_connected()) {
        Serial.println("[welcome] offline — going straight to a story");
        Serial.flush();
        if (story_ok) {
            welcome_say(CS_VOICE_ID_JUST_STORY);
            welcome_offer_story();
        } else {
            led_for_state(ST_IDLE);
        }
        return;
    }

    // ---- 3. ask, using the clip that names exactly the enabled modes ----
    char ask_id[16];
    char ask_path[CS_MAX_PATH_LEN];
    bool asked = false;
    if (cs_build_ask_voice_id(ask_id, sizeof(ask_id),
                              story_ok, game_ok, riddle_ok, curiosity_ok)
        && voice_clip_resolve_path(ask_id, ask_path, sizeof(ask_path))) {
        Serial.printf("[welcome] ask %s\n", ask_id);
        Serial.flush();
        transition_to(ST_PLAYING);
        audio_speaker_begin();
        audio_play_story_file(ask_path, 0, nullptr, nullptr);
        asked = true;
    } else if (welcome_say(CS_VOICE_ID_ASK_ANY)) {
        asked = true;
    }
    if (!asked) {
        // No prompt is on the card yet. Do not interrogate a child with
        // silence — just do the thing they most likely wanted.
        Serial.println("[welcome] no ask clip — going straight to a story");
        Serial.flush();
        if (story_ok) welcome_offer_story(); else led_for_state(ST_IDLE);
        return;
    }

    // ---- 4. listen, with exactly one retry ----
    for (int attempt = 1; attempt <= AREG_WELCOME_MAX_TRIES; attempt++) {
        char intent[16];
        uint8_t *heard = nullptr;
        size_t heard_len = 0;
        if (!welcome_listen("mode", intent, sizeof(intent), &heard, &heard_len)) {
            led_for_state(ST_IDLE);
            return;   // silence — never badger
        }

        if (strcmp(intent, "story") == 0) {
            if (heard != nullptr) heap_caps_free(heard);
            if (story_ok) { welcome_offer_story(); return; }
            break;
        }
        // Calm is always available (MODES.md), and a bedtime cue must not
        // open a menu — it should settle things down, which here means a
        // story rather than a game.
        if (strcmp(intent, "calm") == 0) {
            if (heard != nullptr) heap_caps_free(heard);
            break;
        }
        // game / riddle / curiosity → the ONLINE chat session. The
        // child's own recorded words open it (the backend's ModeDetector
        // routes the transcript; the game engine, riddle engine, and the
        // Curiosity window all live server-side). The voice-intent call
        // above already confirmed the parent left this mode enabled —
        // and the backend re-checks per turn anyway.
        if (strcmp(intent, "game") == 0 || strcmp(intent, "riddle") == 0
            || strcmp(intent, "curiosity") == 0) {
            if (heard != nullptr) {
                handle_online_chat_session(heard, heard_len);
                return;
            }
            break;   // alloc edge — fall to the graceful default story
        }

        if (heard != nullptr) heap_caps_free(heard);
        // "unknown" — ask once more, then stop asking.
        if (attempt < AREG_WELCOME_MAX_TRIES) {
            welcome_say(CS_VOICE_ID_SAY_AGAIN);
            continue;
        }
        break;
    }

    // Fell out: mis-heard twice, or asked for something we cannot yet do.
    // One short line, then a story — the graceful default.
    welcome_say(CS_VOICE_ID_JUST_STORY);
    if (story_ok) {
        welcome_offer_story();
    } else {
        led_for_state(ST_IDLE);
    }
}

// One continuous story session.
//
// Streams the whole story from s_story_offset. A button press cuts the
// audio instantly (true barge-in) and saves the exact resume offset,
// then:
//   - HOLD + speak  → record the question, POST it, play the spoken
//                     answer, and AUTO-RESUME the story from the saved
//                     offset (the loop continues);
//   - quick TAP     → sticky pause: leave the session, the saved offset
//                     stays, and the next button press resumes it.
// Reaching the natural end resets to the beginning.
// Chooses the story for a NEW session and resolves its cached MP3.
//
// New-story boundary = entering handle_story_session with
// s_story_offset == 0. A resume (offset > 0) keeps s_current_story_id
// untouched, so pause/resume, a Q&A barge-in and a stream retry can
// never land on a different story mid-session.
//
// Returns true and fills `out` when a verified cached story was chosen;
// false leaves s_current_story_id empty and the caller falls back to the
// content pack, then the Wi-Fi stream — exactly the pre-selection chain.
static bool story_pick_for_session(char *out, size_t out_len) {
    // Welcome flow — the child already chose this story out loud, so honor
    // that instead of the rotation. One-shot: cleared immediately, so a
    // later press falls back to normal selection. Everything downstream
    // (the started gate, the cursor, play reporting, the intro clip, the
    // after-story dialogue) is untouched — which is why preselection is a
    // flag rather than a second session function.
    if (s_story_preselected && s_current_story_id[0] != '\0') {
        s_story_preselected = false;
        if (story_select_resolve_playback_path(s_current_story_id, out, out_len)) {
            Serial.printf("[welcome] playing chosen story %s\n", s_current_story_id);
            Serial.flush();
            return true;
        }
        Serial.printf("[welcome] chosen story %s did not resolve — normal selection\n",
                      s_current_story_id);
        Serial.flush();
        s_current_story_id[0] = '\0';
    }

    CsStory *eligible = s_eligible_stories;   // shared table, never on the stack
    const int count = story_select_load_eligible(eligible, CS_MAX_STORIES);
    if (count <= 0) {
        Serial.println("[story] no eligible cached stories — using fallback chain");
        Serial.flush();
        return false;
    }

    char chosen[CS_MAX_STORY_ID_LEN + 1];
    if (!story_select_pick(eligible, count, chosen, sizeof(chosen))) {
        return false;
    }
    // Variant-aware: on a RE-listen (this story already in the heard set),
    // with the parent's variant-endings toggle on and an alt file cached and
    // verified, this resolves the alternate ending instead of the base — so a
    // favourite story does not become word-for-word predictable. Falls back
    // to the base narration in every other case, which is every case on a
    // card that ships no variants.
    if (!story_select_resolve_playback_path(chosen, out, out_len)) {
        // Selected but unusable: refuse rather than quietly play a
        // different story than the one chosen.
        Serial.printf("[story] selected %s but it did not resolve — using fallback chain\n",
                      chosen);
        Serial.flush();
        return false;
    }

    cs_copy_bounded(s_current_story_id, sizeof(s_current_story_id), chosen);
    Serial.printf("[story] selected %s (%d eligible)\n", chosen, count);
    Serial.flush();

    // NOTE: the rotation cursor is deliberately NOT advanced here. A story
    // that merely RESOLVED has not been heard — SD, I2S or decoder startup
    // can still fail silently. Persisting now would make the next press
    // skip a story the child never heard. The cursor moves only once
    // audio_play_story_file reports it genuinely started (see the
    // playback loop below).
    return true;
}

static void handle_story_session() {
    // Story-audio access token (gap 1). UNVERIFIED — not compiled/flashed.
    // When the backend has StoryAudio:SigningKey set, the header-less
    // /api/story-audio stream requires ?token=. Fetch it once per session
    // (TTL ~1 h >> a story). Empty/false when enforcement is OFF → we stream
    // without a token, which is correct in that case.
    //
    // SOURCE PRIORITY (decided once per session):
    //   1. the selected verified story from the schema-v2 index;
    //   2. the content-pack narration for the configured story;
    //   3. the Wi-Fi story stream.
    char sd_cache_path[CS_MAX_PATH_LEN];
    bool cache_hit = false;

    if (s_story_offset == 0) {
        // NEW story.
        s_current_story_id[0] = '\0';
        cache_hit = story_pick_for_session(sd_cache_path, sizeof(sd_cache_path));
    } else if (s_current_story_id[0] != '\0') {
        // RESUME: re-resolve the SAME story, never re-select. The variant
        // decision is deterministic for a given card + heard-set, and the
        // heard-set is only written at a story's natural END, so a resume
        // always lands on the same FILE the session started on — the byte
        // offset can never be applied to different audio mid-story.
        cache_hit = story_select_resolve_playback_path(
            s_current_story_id, sd_cache_path, sizeof(sd_cache_path));
        if (cache_hit) {
            Serial.printf("[story] resuming %s at byte %u\n",
                          s_current_story_id, (unsigned)s_story_offset);
        } else {
            // The cached story vanished mid-session (card pulled, file
            // deleted). We are about to play the PACK narration instead, so
            // the selected id must be dropped — leaving it set would ground
            // the in-story Q&A in a story that is no longer the one playing,
            // and would let the rotation bookkeeping below attribute the
            // pack playback to it.
            Serial.printf("[story] resume: %s no longer resolvable — fallback chain\n",
                          s_current_story_id);
            s_current_story_id[0] = '\0';
            s_story_offset = 0;   // a different audio file: byte offset is meaningless
        }
        Serial.flush();
    }

    const char *sd_narration_path = cache_hit ? sd_cache_path : AREG_SD_STORY_NARRATION;
    const bool use_sd = audio_sd_has_file(sd_narration_path);
    Serial.printf("[story] source = %s\n",
                  use_sd ? (cache_hit ? "SD (cache)" : "SD (pack)") : "Wi-Fi stream");
    Serial.flush();

    // The story id every backend call for THIS session must use. Falls back
    // to the configured id when nothing was selected, which is the
    // pre-selection behavior. Set before the token fetch so the in-story
    // Q&A and reflection endpoints are grounded in the story actually
    // playing, not in AREG_STORY_ID.
    const char *active_story_id =
        s_current_story_id[0] ? s_current_story_id : AREG_STORY_ID;
    voice_set_active_story_id(active_story_id);

    // B2/B3 — spoken story intro («Հեքիաթ՝ …, հեղինակ՝ …») before a NEW
    // story, when the parent toggle (cached in the index) is on and the
    // intro clip is synced+verified for the selected story. Plays to its
    // natural end (a few seconds); a resume (offset > 0) never replays it.
    if (s_story_offset == 0 && cache_hit && story_select_intro_enabled()) {
        char intro_path[CS_MAX_PATH_LEN];
        if (story_select_resolve_clip_path(active_story_id, "intro",
                                           intro_path, sizeof(intro_path))) {
            transition_to(ST_PLAYING);
            audio_speaker_begin();
            Serial.printf("[story] intro clip (%s)\n", intro_path);
            Serial.flush();
            audio_play_story_file(intro_path, 0, nullptr, nullptr);
        }
    }

    // Story-audio access token (gap 1) — only the Wi-Fi stream needs it.
    static char story_token[256];
    bool have_token = use_sd
        ? false
        : voice_fetch_story_audio_token(active_story_id, story_token, sizeof(story_token));
    bool token_retry_used = false;
    // The rotation cursor advances at most once per session, and ONLY after
    // playback genuinely started.
    bool selection_settled = false;
    // Story-play reporting: enqueue at most one event per NEW story session
    // (a resume re-enters this function with offset > 0 and must not
    // double-count the listen — the open event from the original start is
    // still queued and gets closed at natural end).
    const bool report_new_start = (s_story_offset == 0);
    bool play_reported = false;
    bool active = true;
    while (active) {
        transition_to(ST_PLAYING);
        audio_speaker_begin();
        uint32_t resume_offset = 0;

        bool interrupted;
        bool started = false;
        bool stream_open_failed = false;
        if (use_sd) {
            Serial.printf("[story] SD play from byte %u\n", (unsigned)s_story_offset);
            Serial.flush();
            interrupted = audio_play_story_file(
                sd_narration_path, s_story_offset, story_barge_in_poll, &resume_offset,
                &started);
        } else {
            // Room for the base URL + ?from=<u32> + &token=<opaque>.
            char url[640];
            int url_n;
            if (s_story_offset > 0) {
                url_n = snprintf(url, sizeof(url), "%s?from=%u%s%s",
                         AREG_STORY_AUDIO_URL, (unsigned)s_story_offset,
                         have_token ? "&token=" : "", have_token ? story_token : "");
                Serial.printf("[story] play from byte %u (token=%d)\n",
                              (unsigned)s_story_offset, have_token ? 1 : 0);
            } else {
                url_n = snprintf(url, sizeof(url), "%s%s%s",
                         AREG_STORY_AUDIO_URL,
                         have_token ? "?token=" : "", have_token ? story_token : "");
                Serial.printf("[story] play from beginning (token=%d)\n", have_token ? 1 : 0);
            }
            Serial.flush();
            // #063 — never open a SILENTLY TRUNCATED URL (a clipped token would
            // fail to validate and waste a turn). snprintf returns the length it
            // WOULD have written; >= capacity means truncation.
            if (url_n < 0 || (size_t)url_n >= sizeof(url)) {
                Serial.printf("[story] URL compose truncated (need %d, cap %u) — ending session\n",
                              url_n, (unsigned)sizeof(url));
                Serial.flush();
                break;  // config error won't self-heal; end the session cleanly
            }
            interrupted = audio_play_story_stream(
                url, s_story_offset, story_barge_in_poll, &resume_offset, &stream_open_failed);
            started = !stream_open_failed;
        }

        // Rotation bookkeeping — the ONLY place the cursor moves.
        //
        // `started` is true only once the decoder produced its first frame,
        // so a story that resolved but died in SD/I2S/decoder startup is
        // never recorded as played: last_id keeps pointing at whatever the
        // child last actually heard, and the next press does not skip a
        // story they never got.
        //
        // Guarded by selection_settled so a Q&A barge-in, a resume, or the
        // token retry cannot re-run it mid-session; and it does nothing at
        // all unless THIS session selected a story from the index.
        if (!selection_settled && s_current_story_id[0] != '\0') {
            if (started) {
                selection_settled = true;
                story_select_save_last(s_current_story_id);   // failure is logged + ignored
                story_select_clear_failed();
                // Welcome flow — same `started` gate, same reasoning: a
                // story that resolved but made no sound has NOT been heard,
                // and recording it would make the toy stop offering a story
                // the child never got.
                story_heard_mark(s_current_story_id);
            } else {
                // Boot-scoped only: a reboot retries this story, which is
                // safer than skipping it forever on one bad start.
                story_select_mark_failed(s_current_story_id);
            }
        }

        // Story-play reporting: one queued event per NEW session, only after
        // playback genuinely started (same `started` gate as the rotation
        // cursor — a story that resolved but made no sound is not a play).
        // Fires for every source (cache / pack / stream) via active_story_id.
        if (report_new_start && started && !play_reported) {
            play_reported = true;
            story_report_on_started(active_story_id,
                                    cache_hit ? "sd" : (use_sd ? "pack" : "stream"));
        }

        // #063 — token-rejection recovery now driven by the REAL stream-open
        // status surfaced from the decoder layer (a non-200 GET = the
        // concealment 404 of an expired/rejected token), NOT a wall-clock guess.
        // Re-fetch the token ONCE and retry the same position before treating
        // the return as a natural end.
        if (!use_sd && stream_open_failed && have_token && !token_retry_used) {
            Serial.println("[story] stream open failed with a token — re-fetching token, retrying once");
            Serial.flush();
            token_retry_used = true;
            have_token = voice_fetch_story_audio_token(
                active_story_id, story_token, sizeof(story_token));
            continue;  // retry from the same s_story_offset
        }

        if (!interrupted) {
            s_story_offset = 0;
            // Story-play reporting: natural end closes the open event (a
            // resumed session closes the event its original start opened).
            // Guarded on `started` so an SD open-failure (which also returns
            // interrupted=false) cannot close an unrelated open event.
            if (started) {
                story_report_on_finished();
            }
            // Serial support — «Շարունակությունը՝ վաղը». Played BEFORE the
            // reflection flow, not after: handle_post_story_flow() returns
            // early on several ordinary paths (offline, no answer in the
            // window), and a closing line the child usually never hears is
            // worse than one that arrives a beat early. Self-gates twice —
            // only for a real serial episode, and only when the clip is
            // synced and verified — so it is a no-op for every standalone
            // story and for any card that has not got the clip yet.
            if (started) {
                char episode_series[CS_MAX_STORY_ID_LEN + 1];
                char serialnext_clip[CS_MAX_PATH_LEN];
                if (story_series_of(active_story_id, episode_series, sizeof(episode_series))
                    && story_select_resolve_clip_path(
                           active_story_id, CS_CLIP_KIND_SERIALNEXT,
                           serialnext_clip, sizeof(serialnext_clip))) {
                    audio_speaker_begin();
                    Serial.printf("[story] serial %s — next-episode clip (%s)\n",
                                  episode_series, serialnext_clip);
                    Serial.flush();
                    audio_play_story_file(serialnext_clip, 0, nullptr, nullptr);
                }
            }
            // Slice 3: conclusion → reflection question → (online) listen for the
            // child's answer → warm acknowledgement. Self-gates on the SD pack,
            // so it is a no-op when playing the Wi-Fi stream.
            handle_post_story_flow();
            Serial.println("[story] finished — press to play again");
            Serial.flush();
            break;
        }
        s_story_offset = resume_offset;
        token_retry_used = false;  // a real segment played; allow a fresh retry later

        // Barge-in: capture the question while the button stays held.
        transition_to(ST_RECORDING);
        const size_t captured = record_question();
        const uint32_t ms_held = (captured * 1000) / AREG_SAMPLE_RATE_HZ;

        if (ms_held < AREG_MIN_RECORD_MS) {
            // Quick tap → sticky pause. Next press resumes from the
            // saved offset (handle_story_session re-entered from IDLE).
            Serial.printf("[story] tap (%u ms) -> paused at byte %u\n",
                          (unsigned)ms_held, (unsigned)s_story_offset);
            Serial.flush();
            break;
        }

        // Compose WAV and POST the question to the Q&A endpoint.
        const size_t pcm_bytes = captured * sizeof(int16_t);
        const size_t payload_bytes = 44 + pcm_bytes;
        uint8_t *payload = (uint8_t *)heap_caps_malloc(payload_bytes, MALLOC_CAP_SPIRAM);
        if (payload == nullptr) {
            Serial.println("[qa] payload alloc failed; resuming");
            Serial.flush();
            continue;  // auto-resume the story
        }
        audio_write_wav_header(payload, (uint32_t)captured);
        memcpy(payload + 44, s_capture_buf, pcm_bytes);

        transition_to(ST_UPLOADING);

        // -------------------------------------------------------
        // S1 — Instant "thinking" earcon (UNVERIFIED — not compiled/flashed)
        //
        // Play the earcon the moment recording ends and upload begins.
        // The child gets IMMEDIATE acoustic acknowledgement instead of
        // the ~7–8 s silent gap that was here before. The earcon is a
        // ~600 ms synthesized tone; it returns before the upload call below.
        //
        // audio_speaker_begin() is called first because the earcon
        // function creates AudioOutputI2S internally (same as
        // audio_play_mp3_buffer). After audio_play_thinking_earcon()
        // returns the speaker is left in a valid state for the thinking
        // bed + answer playback below.
        // -------------------------------------------------------
        audio_speaker_begin();
        audio_play_thinking_earcon();  // S1: immediate acoustic ack
        Serial.println("[qa] earcon done; starting async upload + thinking bed");
        Serial.flush();

        // -------------------------------------------------------
        // S3 — Fire async upload, play thinking-bed while network blocks
        // (UNVERIFIED — not compiled/flashed)
        //
        // voice_start_question_upload_async() launches a FreeRTOS task
        // on CORE 0 that does the full POST + read_response_into().
        // This returns immediately. Meanwhile this loop (on CORE 1) plays
        // short synthesized "thinking-bed" pulses until the task signals
        // completion via voice_async_upload_done().
        //
        // PSRAM OWNERSHIP:
        //   `payload` remains caller-owned (this scope) until
        //   heap_caps_free(payload) below — AFTER the task is done.
        //   The task reads from s_async_payload but does NOT free it.
        //   The response buffer (s_response_buffer inside voice_client) is
        //   allocated by the task and freed by voice_release_last_response().
        //
        // CORE ASSIGNMENT:
        //   Upload task → CORE 0 (PRO_CPU, see voice_client.cpp).
        //   This loop (thinking-bed + subsequent playback) → CORE 1 (APP_CPU).
        //   I2S DMA is handled by the hardware; both cores can call
        //   AudioOutputI2S safely because the think-bed loop and the
        //   upload task never call I2S concurrently — the upload task
        //   is network-only (HTTPClient/WiFiClient). I2S is only driven
        //   from this core (CORE 1) throughout.
        //
        // FALLBACK:
        //   If xTaskCreate fails, voice_start_question_upload_async() runs
        //   synchronously (see voice_client.cpp comment). In that case
        //   voice_async_upload_done() returns true immediately on the next
        //   check and we skip the thinking-bed and go straight to playback —
        //   same behavior as the old code (minus the earcon, which already played).
        // -------------------------------------------------------
        {
            const uint32_t qa_release_ms = millis();  // latency anchor

            voice_start_question_upload_async(payload, payload_bytes,
                                              s_story_offset);

            // Play thinking-bed pulses while the upload is in flight.
            // Each pulse is a short synthesized tone; we poll done after
            // each one. The pulse duration (AREG_THINKBED_PULSE_MS) trades
            // responsiveness (shorter → answer starts sooner after upload)
            // against audio quality (longer → fewer AudioOutputI2S re-inits).
            //
            // HARDWARE ASSUMPTION: repeated AudioOutputI2S begin/stop within
            // audio_play_thinking_earcon()'s internal helper is well-tolerated
            // by the MAX98357A. If re-init clicks are audible, replace the
            // per-pulse I2S construction with a long tone whose amplitude we
            // fade down (requires exposing a "play N samples then stop" API
            // or restructuring synth_write_tone to accept a done_fn callback).
            int bed_count = 0;
            while (!voice_async_upload_done() &&
                   bed_count < AREG_THINKBED_MAX_PULSES) {
                // Reuse audio_play_thinking_earcon() with thinking-bed params.
                // HARDWARE ASSUMPTION: the earcon function reads
                // AREG_EARCON_FREQ_HZ / AREG_EARCON_DURATION_MS internally.
                // For the thinking bed we want different freq/duration, so we
                // call a single-pulse synth directly.
                // TODO (on device): refactor synth_write_tone() to accept
                // freq/duration/amplitude args so we can call it with
                // AREG_THINKBED_FREQ_HZ / AREG_THINKBED_PULSE_MS /
                // AREG_THINKBED_AMPLITUDE here without rebuilding AudioOutputI2S
                // on every pulse. For now, reuse the earcon (same freq/duration)
                // so we can verify the FreeRTOS + I2S coexistence first.
                esp_task_wdt_reset();  // #047 — feed across the ~0.6s-per-pulse bed
                audio_play_thinking_earcon();
                bed_count++;
            }
            Serial.printf("[qa] thinking-bed done after %d pulses; upload_done=%s\n",
                          bed_count,
                          voice_async_upload_done() ? "true" : "false");
            Serial.flush();

            // Wait (without playing anything) for the task to finish if it
            // outlasted our pulse cap. This is the fallback busy-wait; in
            // practice the server responds within AREG_THINKBED_MAX_PULSES *
            // AREG_THINKBED_PULSE_MS ms, and the upload task self-caps at its
            // own HTTP timeouts (~35 s). #047 — BOUND it: a genuinely stuck
            // upload task cannot be safely cancelled or reaped (it owns `payload`
            // and the response buffer across cores, see #046), so on timeout we
            // do a CONTROLLED reboot — the clean recovery from a cross-core
            // deadlock, vs ST_ERROR which would leave a zombie task and still
            // need the power-cycle this is meant to eliminate. The WDT (above)
            // is the backstop; this bound makes the recovery prompt + explicit.
            const uint32_t async_deadline = millis() + AREG_ASYNC_UPLOAD_TIMEOUT_MS;
            while (!voice_async_upload_done()) {
                if ((int32_t)(millis() - async_deadline) >= 0) {  // rollover-safe
                    Serial.println("[qa] async upload wait timeout — stuck task, rebooting to recover");
                    Serial.flush();
                    esp_restart();
                }
                esp_task_wdt_reset();  // feed while we legitimately wait
                delay(20);
            }

            VoiceTurnResult turn = voice_get_async_result();

            // The payload is no longer needed by the task (it's done).
            heap_caps_free(payload);
            payload = nullptr;

            if (turn.ok) {
                transition_to(ST_PLAYING);
                const uint32_t qa_latency_ms = millis() - qa_release_ms;
                Serial.printf("[latency] qa_release->play_begin_ms=%u\n",
                              (unsigned)qa_latency_ms);
                Serial.flush();

                // S3 — Play the answer: try streamed first, fall back to buffered.
                //
                // The backend (another agent) is making the Q&A response a
                // chunked/streamed audio/mpeg. We try audio_play_qa_stream()
                // on the same Q&A URL with the same query params. If the backend
                // doesn't support streaming yet (or the connection fails), we
                // fall back to audio_play_mp3_buffer() with the already-buffered
                // bytes from the async task.
                //
                // HARDWARE ASSUMPTION: the Q&A URL (AREG_STORY_QA_URL +
                // ?storyId=...&offset=...) accepts both POST (WAV upload,
                // used by the async task) and GET (fetch the pre-composed
                // streamed answer). This GET path is the "backend streams the
                // validated answer" described in the task brief. If the backend
                // does NOT yet support a GET endpoint here, the stream will
                // return non-200 and we fall through to the buffered path —
                // which is what the original code did, so there is no regression.
                //
                // NOTE: in the current architecture the Q&A POST returns the
                // answer MP3 as the response body, so the async task already
                // buffered it in turn.response_bytes. The streaming path below
                // opens a NEW connection to the same URL as a GET. For this to
                // work the backend must implement a GET endpoint that returns
                // the latest pre-rendered answer for this (storyId, offset) pair.
                // If the backend instead streams the answer as part of the POST
                // response (not yet implemented), a future firmware revision can
                // modify the async task to read incrementally from the HTTP
                // response stream instead of calling read_response_into().
                //
                // For now this is a best-effort streaming attempt with a reliable
                // buffered fallback.
                // Play the answer the async task already buffered from the
                // POST response body. DO NOT open a separate GET to the Q&A
                // URL to "stream": that route is POST-only, so a GET 404s —
                // and if a GET were ever added it would RE-RUN the whole
                // STT+GPT+TTS pipeline and DOUBLE-BILL for one question.
                // The real latency win (TODO, needs on-device verification) is
                // to decode incrementally from THIS POST response stream
                // instead of read_response_into() buffering it first — i.e.
                // change the async task to hand the live HTTP stream to the
                // MP3 decoder. Until that lands, buffered playback is correct
                // and matches the backend's byte-identical streamed body.
                audio_speaker_begin();
                audio_play_mp3_buffer(turn.response_bytes, turn.response_length);
                Serial.println("[qa] answer played (buffered POST response)");
                Serial.flush();
                voice_release_last_response();
            } else {
                if (payload != nullptr) {
                    heap_caps_free(payload);
                    payload = nullptr;
                }
                Serial.printf("[qa] upload failed (status=%d); resuming\n",
                              turn.http_status);
                Serial.flush();
                voice_release_last_response();
                play_canned_failure_clip();
            }
        }
        // Loop continues → auto-resume the story from s_story_offset.
    }
    transition_to(ST_IDLE);
}

// ---------------------------------------------------------------
// SD-first fallback test harness (bench-only, temporary)
// Gated behind AREG_STORY_SD_FALLBACK_TEST_BENCH; requires
// nothing else (the selection path it exercises is now always compiled).
// Automates fallback Tests B/E/C on-device: it manipulates the SD files,
// runs the REAL source resolver / playback path, restores the files, and
// prints PASS/FAIL. Compiles to ZERO bytes without the flag; no production
// behavior change unless this flag is also set.
// ---------------------------------------------------------------
#ifdef AREG_STORY_SD_FALLBACK_TEST_BENCH

// Replicates the production source decision (handle_story_session, the
// sd_narration_path/use_sd block) using the REAL selector + resolver, so a
// test proves which source the story flow WOULD pick without playing 3-4 min
// of audio. Mirrors story_pick_for_session WITHOUT persisting the rotation
// cursor — a diagnostic must not move the child's place in the rotation.
static void fbtest_log_source(const char *label, bool *out_is_cache, bool *out_use_sd) {
    const char *path = AREG_SD_STORY_NARRATION;
    char cache_path[CS_MAX_PATH_LEN];
    bool cache_hit = false;
    static CsStory eligible[CS_MAX_STORIES];
    const int count = story_select_load_eligible(eligible, CS_MAX_STORIES);
    if (count > 0) {
        char chosen[CS_MAX_STORY_ID_LEN + 1];
        if (story_select_pick(eligible, count, chosen, sizeof(chosen))) {
            cache_hit = story_select_resolve_path(chosen, cache_path, sizeof(cache_path));
        }
    }
    if (cache_hit) path = cache_path;
    const bool use_sd = audio_sd_has_file(path);
    Serial.printf("[fallback-test] %s resolved source = %s\n", label,
                  use_sd ? (cache_hit ? "SD (cache)" : "SD (pack)") : "Wi-Fi stream");
    Serial.flush();
    if (out_is_cache) *out_is_cache = (cache_hit && use_sd);
    if (out_use_sd)   *out_use_sd   = use_sd;
}

// Time-boxed auto-stop for Test C's Wi-Fi stream — the barge-in seam lets us
// trigger the real backend GET + a few seconds of playback, then cut cleanly
// (no button, no Q&A cascade).
static uint32_t g_fbtest_autostop_ms = 0;
static bool fbtest_autostop_barge_in() { return millis() >= g_fbtest_autostop_ms; }

static const char *kFbIndex     = "/content_index.json";
static const char *kFbIndexBak  = "/content_index.json.bak";
static const char *kFbIndexOrig = "/content_index.json.orig";
static const char *kFbPackBak   = "/stories/pack_narration.bak";

static void story_fallback_test_run() {
    Serial.println("[fallback-test] ===== SD-first fallback Tests B/E/C =====");
    Serial.flush();
    if (!audio_sd_available()) {
        Serial.println("[fallback-test] FAIL sd unavailable — cannot run");
        Serial.flush();
        return;
    }

    // ---------- Test B: /content_index.json missing ----------
    Serial.println("[fallback-test] --- Test B: content index missing ---");
    Serial.flush();
    if (SD.rename(kFbIndex, kFbIndexBak)) {
        bool is_cache = false;
        fbtest_log_source("Test B", &is_cache, nullptr);
        Serial.printf("[fallback-test] Test B %s (SD-cache NOT selected)\n",
                      !is_cache ? "PASS" : "FAIL");
        if (!SD.rename(kFbIndexBak, kFbIndex)) {
            Serial.println("[fallback-test] WARN Test B restore failed");
        }
    } else {
        Serial.println("[fallback-test] Test B SETUP-FAIL: could not rename index");
    }
    Serial.flush();

    // ---------- Test E: storyId mismatch ----------
    Serial.println("[fallback-test] --- Test E: storyId mismatch ---");
    Serial.flush();
    if (SD.rename(kFbIndex, kFbIndexOrig)) {
        // Write a temporary index that points at the real MP3 but a WRONG story.
        File f = SD.open(kFbIndex, FILE_WRITE);
        bool wrote = false;
        if (f) {
            JsonDocument idx;
            idx["storyId"]   = "wrong-story-id";
            idx["version"]   = 1;
            idx["file"]      = "/stories/anban-huri-v1.mp3";
            idx["sizeBytes"] = 4654560;
            wrote = (serializeJson(idx, f) > 0);
            f.close();
        }
        if (wrote) {
            bool is_cache = false;
            // Since story-select-from-index the rejection reason CHANGED: a
            // foreign storyId is no longer "not my story" (selection has no
            // configured story to compare against). This temp index is a
            // LEGACY flat object with no sha256, so cs_index_parse cannot
            // migrate it and it yields zero eligible entries. Either way the
            // SD cache must not be selected, which is what the test asserts.
            fbtest_log_source("Test E", &is_cache, nullptr);
            Serial.printf("[fallback-test] Test E %s (unusable index rejected, SD-cache NOT selected)\n",
                          !is_cache ? "PASS" : "FAIL");
        } else {
            Serial.println("[fallback-test] Test E SETUP-FAIL: could not write temp index");
        }
        SD.remove(kFbIndex);  // drop the temp index
        if (!SD.rename(kFbIndexOrig, kFbIndex)) {
            Serial.println("[fallback-test] WARN Test E restore failed");
        }
    } else {
        Serial.println("[fallback-test] Test E SETUP-FAIL: could not rename index");
    }
    Serial.flush();

    // ---------- Test C: no SD source -> Wi-Fi stream + backend GET ----------
    Serial.println("[fallback-test] --- Test C: Wi-Fi fallback ---");
    Serial.flush();
    const bool moved_index = SD.rename(kFbIndex, kFbIndexBak);
    const bool had_pack = audio_sd_has_file(AREG_SD_STORY_NARRATION);
    const bool moved_pack = had_pack ? SD.rename(AREG_SD_STORY_NARRATION, kFbPackBak) : false;
    if (!moved_index) {
        Serial.println("[fallback-test] Test C SETUP-FAIL: could not rename index");
    } else {
        bool use_sd = true;
        fbtest_log_source("Test C", nullptr, &use_sd);
        if (use_sd) {
            Serial.println("[fallback-test] Test C FAIL (SD source still selected)");
        } else if (!voice_wifi_is_connected()) {
            Serial.println("[fallback-test] Test C source=Wi-Fi OK, but Wi-Fi down — skipping stream GET");
        } else {
            // Invoke the EXISTING stream path (same fn handle_story_session
            // calls); base URL only, since StoryAudio enforcement is a dev/bench
            // knob. Auto-stop after ~8 s so we prove the GET + real playback
            // without a full 4-minute stream.
            Serial.printf("[fallback-test] Test C streaming %s (auto-stop ~8s)\n",
                          AREG_STORY_AUDIO_URL);
            Serial.flush();
            audio_speaker_begin();
            g_fbtest_autostop_ms = millis() + 8000;
            uint32_t resume = 0;
            bool open_failed = false;
            const bool interrupted = audio_play_story_stream(
                AREG_STORY_AUDIO_URL, 0, fbtest_autostop_barge_in, &resume, &open_failed);
            Serial.printf("[fallback-test] Test C stream: open_failed=%d interrupted=%d resume=%u\n",
                          open_failed ? 1 : 0, interrupted ? 1 : 0, (unsigned)resume);
            Serial.printf("[fallback-test] Test C %s (Wi-Fi stream %s)\n",
                          open_failed ? "FAIL" : "PASS",
                          open_failed ? "did not open (backend 404?)" : "opened + played");
        }
    }
    // Restore both moved files.
    if (moved_pack && !SD.rename(kFbPackBak, AREG_SD_STORY_NARRATION)) {
        Serial.println("[fallback-test] WARN Test C pack restore failed");
    }
    if (moved_index && !SD.rename(kFbIndexBak, kFbIndex)) {
        Serial.println("[fallback-test] WARN Test C index restore failed");
    }
    Serial.flush();

    // ---------- Final SD-state verification ----------
    Serial.printf("[fallback-test] restore check: index=%d mp3=%d leftover_bak=%d leftover_orig=%d\n",
                  SD.exists(kFbIndex) ? 1 : 0,
                  SD.exists("/stories/anban-huri-v1.mp3") ? 1 : 0,
                  SD.exists(kFbIndexBak) ? 1 : 0,
                  SD.exists(kFbIndexOrig) ? 1 : 0);
    Serial.println("[fallback-test] ===== done =====");
    Serial.flush();
}

void story_fallback_test_tick() {
    static bool s_done = false;
    static bool s_stamped = false;
    static uint32_t s_last_status_ms = 0;
    if (s_done) {
        return;
    }
    if (!s_stamped) {
        s_stamped = true;
        Serial.println("[fallback-test] bench fw built " __DATE__ " " __TIME__);
        Serial.flush();
    }
    const uint32_t now = millis();
    if (now < 30000UL) {
        if (s_last_status_ms == 0 || now - s_last_status_ms >= 5000UL) {
            s_last_status_ms = now;
            Serial.println("[fallback-test] armed; will start at ms>=30000");
            Serial.flush();
        }
        return;
    }
    s_done = true;  // one run per boot
    story_fallback_test_run();
}
#endif  // AREG_STORY_SD_FALLBACK_TEST_BENCH

// --- Arduino entry points -----------------------------------

void setup() {
    Serial.begin(AREG_SERIAL_BAUD);
    delay(200);
    Serial.println();
    Serial.println("[boot] AregVoiceMvp starting");
    Serial.printf("[boot] backend = %s\n", AREG_BACKEND_BASE_URL);
    areg_transport_log_policy();
    Serial.flush();
    // Diag: reset reason. Distinguishes power-on / EN / panic /
    // brownout / watchdog so a "monitor came back" after a hang
    // can be classified without guessing.
    {
        esp_reset_reason_t r = esp_reset_reason();
        Serial.printf("[boot] reset_reason=%d/%s\n",
                      (int)r, reset_reason_name(r));
        Serial.flush();
    }
    // Diag: print the prior-boot breadcrumb (if any) BEFORE any
    // new DIAG_MARK in this boot would overwrite it.
    diag_print_previous_boot_context();
    DIAG_MARK(100, "setup_after_serial");
    // Echo bench config up front so the first real bring-up log
    // unambiguously shows what this build targets. No secrets
    // printed (SSID is already logged by voice_wifi_begin;
    // Wi-Fi password / device id / api key are intentionally not).
    Serial.printf("[boot] backend=%s\n", AREG_BACKEND_URL);
    Serial.flush();
    Serial.printf("[boot] pins button=%d led=%d\n",
                  AREG_PIN_BUTTON, AREG_PIN_LED);
    Serial.printf("[boot] mic_i2s bck=%d ws=%d sd=%d\n",
                  AREG_PIN_MIC_BCK, AREG_PIN_MIC_WS, AREG_PIN_MIC_DATA);
    Serial.printf("[boot] amp_i2s bck=%d lrc=%d din=%d\n",
                  AREG_PIN_AMP_BCK, AREG_PIN_AMP_LRC, AREG_PIN_AMP_DATA);
    Serial.flush();

    s_led.begin();
    s_led.setBrightness(60);
    led_for_state(ST_IDLE);
    DIAG_MARK(110, "led_initialised");

    // #047 — application task watchdog. Subscribes the Arduino loop task; a
    // genuine hang (a loop that stops iterating, hence stops feeding) forces a
    // clean reset instead of a silent freeze only a power-cycle clears. The
    // timeout is generous (> any legitimate single block: <=30 s HTTP read,
    // <=15 s record, <=20 s Wi-Fi join), so only a real stall trips it; the
    // minutes-long decode loops feed it each iteration (see audio_io.cpp).
    // Core 3.x / IDF5 config-struct API; reconfigure if the core already
    // initialised the TWDT for the idle tasks.
    {
        esp_task_wdt_config_t wdt_cfg = {};
        wdt_cfg.timeout_ms = (uint32_t)AREG_WDT_TIMEOUT_S * 1000u;
        wdt_cfg.idle_core_mask = 0;     // don't watch idle tasks — avoid fighting Arduino
        wdt_cfg.trigger_panic = true;   // panic handler resets the chip
        esp_err_t werr = esp_task_wdt_init(&wdt_cfg);
        if (werr == ESP_ERR_INVALID_STATE) {
            esp_task_wdt_reconfigure(&wdt_cfg);  // already initialised by the core
        }
        esp_task_wdt_add(NULL);   // subscribe THIS (the loop) task
        esp_task_wdt_reset();
        Serial.printf("[boot] task watchdog enabled (%ds)\n", AREG_WDT_TIMEOUT_S);
        Serial.flush();
    }
    DIAG_MARK(115, "wdt_enabled");

    button_begin();
    answer_buttons_begin();   // no-op unless AREG_PIN_BUTTON_YES/NO defined
    DIAG_MARK(120, "button_initialised");

    // Seed the last-known pause / bedtime state from NVS before anything
    // can make a sound. Without this both read false at power-on, and a
    // toy that has been off for a week would greet a child whose parent
    // paused it six days ago.
    voice_state_restore();

    // One-shot PSRAM allocation for the capture buffer.
    s_capture_buf = (int16_t *)heap_caps_malloc(
        AREG_RECORD_BUFFER_BYTES, MALLOC_CAP_SPIRAM);
    if (s_capture_buf == nullptr) {
        Serial.println("[boot] PSRAM capture buffer allocation failed");
        Serial.flush();
        DIAG_MARK(131, "psram_capture_buf_alloc_fail");
        transition_to(ST_ERROR);
        // Spin — there is no point continuing without capture.
        while (true) {
            delay(1000);
        }
    }
    DIAG_MARK(130, "psram_capture_buf_alloc_ok");

    // microSD mount (offline content pack, Slice 2). NON-FATAL: a missing or
    // failed card just means the device falls back to Wi-Fi streaming — never
    // a hard stop. Logs whether the configured story's narration is on the card.
    Serial.printf("[boot] sd_spi cs=%d sck=%d mosi=%d miso=%d\n",
                  AREG_PIN_SD_CS, AREG_PIN_SD_SCK, AREG_PIN_SD_MOSI, AREG_PIN_SD_MISO);
    Serial.flush();
    if (audio_sd_begin()) {
        Serial.printf("[boot] SD mounted; offline narration %s = %s\n",
                      AREG_SD_STORY_NARRATION,
                      audio_sd_has_file(AREG_SD_STORY_NARRATION) ? "present" : "absent");
    } else {
        Serial.println("[boot] SD not mounted — Wi-Fi streaming only");
    }
#ifdef AREG_SD_BENCH_TEST
    // microSD HARDWARE PROOF (bench builds only): write + read-back on the
    // just-mounted card — the precondition for the future Cloud→SD story
    // sync. Zero bytes of this exist in production builds.
    sd_bench_run();
#endif
    Serial.flush();
    DIAG_MARK(135, "sd_mount_done");

    // Diag: register Wi-Fi event handler BEFORE join so the
    // initial CONNECTED / GOT_IP / DISCONNECTED events surface
    // in the boot log alongside the existing [wifi] lines.
    WiFi.onEvent(wifi_event_handler);

    DIAG_MARK(140, "wifi_begin_before");
#ifdef AREG_USE_BLE_PROVISIONING
    // B.2 — boot-time re-provision gesture: HOLD the button while powering on
    // for AREG_PROV_RESET_HOLD_MS to forget the saved network and re-enter BLE
    // provisioning (moved toy / new router). Evaluated ONCE here at boot, so it
    // never conflicts with the normal in-loop press (which starts the story).
    {
        const uint32_t hold_ms = AREG_PROV_RESET_HOLD_MS;
        const uint32_t held_start = millis();
        bool held = (digitalRead(AREG_PIN_BUTTON) == LOW);
        while (held && (millis() - held_start) < hold_ms) {
            if (digitalRead(AREG_PIN_BUTTON) != LOW) { held = false; break; }
            delay(10);
        }
        if (held) {
            Serial.println("[prov] button held at boot — forgetting Wi-Fi, entering provisioning");
            Serial.flush();
            wifi_creds_clear();
        }
    }

    if (voice_wifi_is_provisioned()) {
        // Known network → connect normally (B.1 path reads NVS creds).
        if (voice_wifi_begin()) {
            DIAG_MARK(141, "wifi_begin_after_ok");
        } else {
            Serial.println("[boot] wifi join failed; will keep retrying in background");
            Serial.flush();
            DIAG_MARK(142, "wifi_begin_after_fail_nonfatal");
        }
    } else {
        // No saved network → start BLE provisioning, then proceed to IDLE. The
        // provisioning manager connects the STA once the phone delivers creds;
        // any voice turn attempted before then fails gracefully to the canned
        // clip (every upload path checks voice_wifi_is_connected()).
        Serial.println("[boot] no saved Wi-Fi — starting BLE provisioning");
        Serial.flush();
        ble_provisioning_begin();
        DIAG_MARK(143, "ble_provisioning_started");
    }
#else
    if (voice_wifi_begin()) {
        DIAG_MARK(141, "wifi_begin_after_ok");
    } else {
        // #045 — a transient join failure is NOT fatal. Proceed to IDLE;
        // loop()'s voice_wifi_tick() keeps retrying with backoff, so a router
        // that is slow or absent at power-on recovers without a power-cycle.
        // Voice turns attempted while still down fail gracefully to the canned
        // clip (every upload path checks voice_wifi_is_connected()).
        Serial.println("[boot] wifi join failed; will keep retrying in background");
        Serial.flush();
        DIAG_MARK(142, "wifi_begin_after_fail_nonfatal");
    }
#endif

    Serial.println("[boot] ready — press button to speak");
    Serial.flush();
    DIAG_MARK(150, "ready_idle");
    transition_to(ST_IDLE);

    // ---- the toy's opening ----
    // Runs LAST in setup, after the provisioning gesture has had its
    // chance (holding the button at boot must never be mistaken for an
    // answer to a menu question).
    //
    // One best-effort heartbeat first: it is a ~200 ms POST that already
    // exists, and it makes the greeting reflect the parent's CURRENT
    // pause/bedtime state whenever the toy is online. Offline, the values
    // voice_state_restore() seeded from NVS stand.
    if (voice_wifi_is_connected()) {
        voice_send_heartbeat();
    }
    handle_welcome_flow();
    transition_to(ST_IDLE);
}

void loop() {
    // #047 — feed the task watchdog every loop iteration. IDLE iterates at
    // ~100 Hz (button poll + 10 ms delay), so the WDT is fed constantly when
    // not in a handler; the long handlers feed it from their decode loops.
    esp_task_wdt_reset();

    // Only IDLE accepts input. During RECORDING / UPLOADING /
    // PLAYING / ERROR the loop is blocked inside the handler.
    if (s_state == ST_IDLE) {
        // #045 — keep the Wi-Fi link alive with a non-blocking, backed-off
        // reconnect while idle. Cheap when connected; recovers from router
        // blips (and a failed boot-time join) without a power-cycle. Runs only
        // in IDLE — RECORDING/UPLOADING/PLAYING block inside their handlers, so
        // a reconnect never disrupts an active turn.
#ifdef AREG_USE_BLE_PROVISIONING
        // B.3 — auto-fallback to provisioning. While a provisioning session is
        // running we stop the normal reconnect (it would fight the manager for
        // the radio). On a successful re-provision we reboot so the new NVS
        // creds load via the proven B.1 voice_wifi_begin() path.
        if (ble_provisioning_succeeded()) {
            Serial.println("[prov] re-provisioned — rebooting to apply new Wi-Fi");
            Serial.flush();
            delay(2000);  // let the phone receive the success ack first
            esp_restart();
        }
        if (!ble_provisioning_active()) {
            voice_wifi_tick();
            // A toy that WAS provisioned but has been offline too long (moved
            // house / router replaced) re-opens BLE provisioning on its own.
            if (voice_wifi_is_provisioned() &&
                voice_wifi_down_duration_ms() >= AREG_PROV_FALLBACK_AFTER_MS) {
                Serial.println("[prov] long Wi-Fi outage — opening BLE provisioning for re-onboarding");
                Serial.flush();
                ble_provisioning_begin();
            }
        }
#else
        voice_wifi_tick();
#endif

        // Diag: 5 s idle heartbeat — surfaces Wi-Fi drops and
        // proves the chip is alive between button presses, since
        // the steady state otherwise prints nothing.
        const uint32_t now = millis();
        if (now - s_last_heartbeat_ms >= 5000) {
            s_last_heartbeat_ms = now;
            Serial.printf(
                "[alive] ms=%u heap=%u psram=%u wifi=%d ip=%s rssi=%d\n",
                (unsigned)now,
                (unsigned)ESP.getFreeHeap(),
                (unsigned)ESP.getFreePsram(),
                (int)WiFi.status(),
                WiFi.localIP().toString().c_str(),
                (int)WiFi.RSSI());
            Serial.flush();
        }

        // Phase A.1 (toy side) — periodic presence heartbeat so the parent
        // app's online dot reflects an idle-but-powered toy. Best-effort and
        // brief; runs only in IDLE (never during a turn). Chat turns already
        // refresh LastSeenAt, so this only matters during long idle stretches.
        if (now - s_last_net_heartbeat_ms >= AREG_HEARTBEAT_INTERVAL_MS) {
            s_last_net_heartbeat_ms = now;
            voice_send_heartbeat();
        }

        // OTA foundation (Proof 2 skeleton) — phone-home command poll +
        // manifest check. Boot-polls once when Wi-Fi is first up, then
        // re-polls on its own AREG_HEARTBEAT_INTERVAL_MS cadence. IDLE-only
        // (this branch), same as the heartbeat, so a poll can never stall a
        // voice turn. NO firmware download/apply in this slice.
        ota_foundation_tick();

        // Story-play reporting — upload queued play events (SD-cache plays
        // never touch the backend, so without this the parent dashboard
        // under-reports). Prompt after a story ends, else on the heartbeat
        // cadence while anything is queued. IDLE-only, best-effort; events
        // are deleted only after a server 2xx.
        story_report_tick();

#ifdef AREG_CONTENT_SYNC_BENCH
        // Cloud→SD story sync (bench builds only): one attempt per boot,
        // once Wi-Fi + SD are both up. IDLE-only — a 4.6 MB download can
        // never stall a voice turn. Zero bytes of this in production.
        content_sync_tick();
#endif

#ifdef AREG_CONTENT_SYNC_TEST_BENCH
        // Content-sync decision-logic tests (bench builds only): pure
        // validation / manifest / index checks, no SD, no Wi-Fi, no
        // backend. IDLE-only. Zero bytes of this in production.
        content_sync_test_tick();
#endif

#ifdef AREG_STORY_SELECT_TEST_BENCH
        // Story-selection tests (bench builds only): pure round-robin +
        // eligibility checks, no SD, no NVS, no Wi-Fi. IDLE-only.
        // Zero bytes of this in production.
        story_select_test_tick();
#endif

#ifdef AREG_SD_DIAG_BENCH
        // Standalone SD diagnostic (bench builds only): isolates the
        // content-sync "SD.begin failed" into hardware-vs-integration.
        // First run 20 s after boot, then every 30 s until a pass. No
        // backend, no network needed.
        sd_diag_tick();
#endif

#ifdef AREG_SD_PLAYBACK_BENCH
        // Cached-MP3 SD playback (bench builds only): plays the story MP3
        // already cached on SD (by content-sync) through the EXISTING
        // audio_play_story_file() decoder path. One shot, 30 s after boot,
        // IDLE-only. No backend download, no recording, no content-sync.
        // Playback blocks the idle loop for the clip's duration by design.
        sd_playback_tick();
#endif

#ifdef AREG_STORY_SD_FALLBACK_TEST_BENCH
        // SD-first fallback test harness (bench builds only): auto-runs
        // fallback Tests B/E/C by manipulating SD files + exercising the real
        // resolver/stream path, then restores. One shot, 30 s after boot.
        story_fallback_test_tick();
#endif

#ifdef AREG_OFFLINE_QUIZ_BENCH
        // Offline true/false quiz (bench builds only): plays /quiz clips
        // from SD, child answers with the GREEN/RED buttons, answers are
        // verified against the clip filename. One quiz per boot, 30 s
        // after boot, IDLE-only. Zero bytes of this in production.
        offline_quiz_tick();
#endif

#ifdef AREG_OFFLINE_GAMES_BENCH
        // Offline games (bench builds only): mind-reader / two-player
        // buzzer / button Simon, all from /games clips on SD with the
        // GREEN/RED buttons. One game per boot, 30 s after boot, IDLE-only;
        // which one is a build-time pick (AREG_OFFLINE_GAMES_PICK). Zero
        // bytes of this in production.
        offline_games_tick();
#endif

        char ev = button_poll();
        if (ev == 'P') {
            Serial.println("[button] pressed");
            Serial.flush();
            DIAG_MARK(200, "button_press");
            // Parent PAUSE: a paused toy is fully silent — even local SD
            // story/music playback is skipped (pause used to gate only the
            // online chat path, so a child could still play cached stories).
            // The pause state is heartbeat-cached; when offline the last-known
            // value stands. A paused press just flicks the LED, no sound.
            char music_path[CS_MAX_PATH_LEN];
            if (voice_is_paused()) {
                Serial.println("[button] ignored — toy is paused");
                Serial.flush();
                led_for_state(ST_IDLE);
            } else if (s_story_offset == 0
                       && voice_in_bedtime_window()
                       && story_select_music_enabled()
                       && music_select_next(music_path, sizeof(music_path))) {
                // Slice E — bedtime music: while the server says the bedtime
                // window is active (heartbeat-cached; the toy has no clock)
                // AND the parent opted in (index-cached) AND a verified track
                // is on the card, a press plays calm music instead of a
                // story. A press during music stops it quietly (no Q&A, no
                // resume bookkeeping — it's music, not a narrative). Never
                // touches a paused story's resume offset.
                transition_to(ST_PLAYING);
                audio_speaker_begin();
                Serial.printf("[music] playing %s\n", music_path);
                Serial.flush();
                audio_play_story_file(music_path, 0, story_barge_in_poll, nullptr);
                Serial.println("[music] done");
                Serial.flush();
                transition_to(ST_IDLE);
            } else {
                // Continuous story: a press starts the story (or resumes
                // it from the last barge-in offset). During playback a
                // press cuts the audio instantly; holding + speaking asks
                // a question (answered, then the story auto-resumes), a
                // quick tap just pauses. All handled in handle_story_session.
                handle_story_session();
            }
        }
    }
    delay(AREG_BUTTON_POLL_MS);
}

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
#include <WiFi.h>

#include "config.h"
#include "audio_io.h"
#include "voice_client.h"
#include "canned_clip.h"
#include "diag.h"

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

static uint32_t s_last_heartbeat_ms = 0;

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
static void handle_story_session() {
    bool active = true;
    while (active) {
        char url[320];
        if (s_story_offset > 0) {
            snprintf(url, sizeof(url), "%s?from=%u",
                     AREG_STORY_AUDIO_URL, (unsigned)s_story_offset);
            Serial.printf("[story] play from byte %u\n", (unsigned)s_story_offset);
        } else {
            snprintf(url, sizeof(url), "%s", AREG_STORY_AUDIO_URL);
            Serial.println("[story] play from beginning");
        }
        Serial.flush();

        transition_to(ST_PLAYING);
        audio_speaker_begin();
        uint32_t resume_offset = 0;
        const bool interrupted = audio_play_story_stream(
            url, s_story_offset, story_barge_in_poll, &resume_offset);

        if (!interrupted) {
            s_story_offset = 0;
            Serial.println("[story] finished — press to play again");
            Serial.flush();
            break;
        }
        s_story_offset = resume_offset;

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
        VoiceTurnResult turn =
            voice_upload_question(payload, payload_bytes, s_story_offset);
        heap_caps_free(payload);

        if (turn.ok) {
            transition_to(ST_PLAYING);
            audio_speaker_begin();
            audio_play_mp3_buffer(turn.response_bytes, turn.response_length);
            voice_release_last_response();
        } else {
            Serial.printf("[qa] upload failed (status=%d); resuming\n",
                          turn.http_status);
            Serial.flush();
            voice_release_last_response();
            play_canned_failure_clip();
        }
        // Loop continues → auto-resume the story from s_story_offset.
    }
    transition_to(ST_IDLE);
}

// --- Arduino entry points -----------------------------------

void setup() {
    Serial.begin(AREG_SERIAL_BAUD);
    delay(200);
    Serial.println();
    Serial.println("[boot] AregVoiceMvp starting");
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

    button_begin();
    DIAG_MARK(120, "button_initialised");

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

    // Diag: register Wi-Fi event handler BEFORE join so the
    // initial CONNECTED / GOT_IP / DISCONNECTED events surface
    // in the boot log alongside the existing [wifi] lines.
    WiFi.onEvent(wifi_event_handler);

    DIAG_MARK(140, "wifi_begin_before");
    if (!voice_wifi_begin()) {
        Serial.println("[boot] wifi join failed; staying in ERROR");
        Serial.flush();
        DIAG_MARK(142, "wifi_begin_after_fail");
        transition_to(ST_ERROR);
        // Device can still recover on next boot; leave LED red
        // so an operator sees the state without opening serial.
        return;
    }
    DIAG_MARK(141, "wifi_begin_after_ok");

    Serial.println("[boot] ready — press button to speak");
    Serial.flush();
    DIAG_MARK(150, "ready_idle");
    transition_to(ST_IDLE);
}

void loop() {
    // Only IDLE accepts input. During RECORDING / UPLOADING /
    // PLAYING / ERROR the loop is blocked inside the handler.
    if (s_state == ST_IDLE) {
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

        char ev = button_poll();
        if (ev == 'P') {
            Serial.println("[button] pressed");
            Serial.flush();
            DIAG_MARK(200, "button_press");
            // Continuous story: a press starts the story (or resumes
            // it from the last barge-in offset). During playback a
            // press cuts the audio instantly; holding + speaking asks
            // a question (answered, then the story auto-resumes), a
            // quick tap just pauses. All handled in handle_story_session.
            handle_story_session();
        }
    }
    delay(AREG_BUTTON_POLL_MS);
}

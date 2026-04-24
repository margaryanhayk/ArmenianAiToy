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

#include "config.h"
#include "audio_io.h"
#include "voice_client.h"
#include "canned_clip.h"

// --- State machine -------------------------------------------
enum State {
    ST_IDLE = 0,
    ST_RECORDING,
    ST_UPLOADING,
    ST_PLAYING,
    ST_ERROR
};

static State s_state = ST_IDLE;

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
    transition_to(ST_RECORDING);

    // Fresh mic session per turn. Torn down before playback.
    if (!audio_mic_begin()) {
        Serial.println("[cap] mic_begin failed");
        transition_to(ST_ERROR);
        play_canned_failure_clip();
        transition_to(ST_IDLE);
        return;
    }

    const size_t max_samples =
        AREG_RECORD_BUFFER_BYTES / sizeof(int16_t);
    const size_t captured_samples = audio_mic_capture(
        s_capture_buf, max_samples,
        AREG_MAX_RECORD_MS, button_is_released);
    audio_mic_end();
    // audio_mic_capture returns when button_is_released sees the
    // button go high — that edge IS the release we want to
    // anchor the latency measurement against. Everything before
    // this point (press-to-capture-start latency, capture
    // duration) is not what the child perceives.
    s_release_ms = millis();
    Serial.printf("[cap] samples=%u\n", (unsigned)captured_samples);

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

    transition_to(ST_UPLOADING);
    s_awaiting_first_play_ms = true;

    VoiceTurnResult turn = voice_upload_turn(payload, payload_bytes);
    heap_caps_free(payload);

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

    audio_speaker_begin();
    const bool played = audio_play_mp3_buffer(
        turn.response_bytes, turn.response_length);
    voice_release_last_response();

    if (!played) {
        Serial.println("[play] decoder error");
        transition_to(ST_ERROR);
        play_canned_failure_clip();
    }
    transition_to(ST_IDLE);
}

// --- Arduino entry points -----------------------------------

void setup() {
    Serial.begin(AREG_SERIAL_BAUD);
    delay(200);
    Serial.println();
    Serial.println("[boot] AregVoiceMvp starting");
    // Echo bench config up front so the first real bring-up log
    // unambiguously shows what this build targets. No secrets
    // printed (SSID is already logged by voice_wifi_begin;
    // Wi-Fi password / device id / api key are intentionally not).
    Serial.printf("[boot] backend=%s\n", AREG_BACKEND_URL);
    Serial.printf("[boot] pins button=%d led=%d\n",
                  AREG_PIN_BUTTON, AREG_PIN_LED);
    Serial.printf("[boot] mic_i2s bck=%d ws=%d sd=%d\n",
                  AREG_PIN_MIC_BCK, AREG_PIN_MIC_WS, AREG_PIN_MIC_DATA);
    Serial.printf("[boot] amp_i2s bck=%d lrc=%d din=%d\n",
                  AREG_PIN_AMP_BCK, AREG_PIN_AMP_LRC, AREG_PIN_AMP_DATA);

    s_led.begin();
    s_led.setBrightness(60);
    led_for_state(ST_IDLE);

    button_begin();

    // One-shot PSRAM allocation for the capture buffer.
    s_capture_buf = (int16_t *)heap_caps_malloc(
        AREG_RECORD_BUFFER_BYTES, MALLOC_CAP_SPIRAM);
    if (s_capture_buf == nullptr) {
        Serial.println("[boot] PSRAM capture buffer allocation failed");
        transition_to(ST_ERROR);
        // Spin — there is no point continuing without capture.
        while (true) {
            delay(1000);
        }
    }

    if (!voice_wifi_begin()) {
        Serial.println("[boot] wifi join failed; staying in ERROR");
        transition_to(ST_ERROR);
        // Device can still recover on next boot; leave LED red
        // so an operator sees the state without opening serial.
        return;
    }

    Serial.println("[boot] ready — press button to speak");
    transition_to(ST_IDLE);
}

void loop() {
    // Only IDLE accepts input. During RECORDING / UPLOADING /
    // PLAYING / ERROR the loop is blocked inside the handler.
    if (s_state == ST_IDLE) {
        char ev = button_poll();
        if (ev == 'P') {
            // handle_record_upload_playback owns the full
            // press→release→upload→play cycle. s_release_ms
            // is stamped inside it right after capture ends.
            handle_record_upload_playback();
        }
    }
    delay(AREG_BUTTON_POLL_MS);
}

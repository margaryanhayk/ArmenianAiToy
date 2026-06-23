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
#include "audio_io.h"
#include "voice_client.h"
#include "canned_clip.h"
#include "diag.h"

// #047 — hang-protection tunables. Defaulted here so the build never depends
// on config.h carrying them; overridable in config.h. See config.h.example.
#ifndef AREG_WDT_TIMEOUT_S
#define AREG_WDT_TIMEOUT_S            60
#endif
#ifndef AREG_ASYNC_UPLOAD_TIMEOUT_MS
#define AREG_ASYNC_UPLOAD_TIMEOUT_MS  45000
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
    // 1. Conclusion (offline) — the finalization of the story.
    if (audio_sd_has_file(AREG_SD_STORY_CONCLUSION)) {
        transition_to(ST_PLAYING);
        audio_speaker_begin();
        Serial.println("[post] conclusion");
        Serial.flush();
        audio_play_story_file(AREG_SD_STORY_CONCLUSION, 0, nullptr, nullptr);
    }

    // 2. Reflection question (offline). No question on the card → done.
    if (!audio_sd_has_file(AREG_SD_STORY_QUESTION0)) {
        return;
    }
    transition_to(ST_PLAYING);
    audio_speaker_begin();
    Serial.println("[post] question");
    Serial.flush();
    audio_play_story_file(AREG_SD_STORY_QUESTION0, 0, nullptr, nullptr);

    // 3. The ANSWER needs the cloud (STT + GPT). Offline → optional close, stop.
    if (!voice_wifi_is_connected()) {
        Serial.println("[post] offline — answer needs connectivity; closing");
        Serial.flush();
        if (audio_sd_has_file(AREG_SD_OFFLINE_CLOSE)) {
            audio_speaker_begin();
            audio_play_story_file(AREG_SD_OFFLINE_CLOSE, 0, nullptr, nullptr);
        }
        return;
    }

    // 4. Listening window: invite the child to press & hold and answer. The
    //    recording color is the "your turn" cue. No press in the window → quiet
    //    close (never force an answer from a small child).
    Serial.println("[post] listening for the answer (press & hold to talk)");
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

    // 5. Record the answer while held, then POST to the reflection endpoint.
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
    Serial.println("[post] uploading answer to reflection endpoint");
    Serial.flush();

    // questionIndex 0 — this slice asks the first reflection question only.
    VoiceTurnResult turn = voice_upload_reflection_answer(payload, payload_bytes, 0);
    heap_caps_free(payload);
    payload = nullptr;

    if (turn.ok) {
        transition_to(ST_PLAYING);
        audio_speaker_begin();
        audio_play_mp3_buffer(turn.response_bytes, turn.response_length);
        Serial.println("[post] acknowledgement played");
        Serial.flush();
    } else {
        Serial.printf("[post] reflection upload failed (status=%d)\n", turn.http_status);
        Serial.flush();
    }
    voice_release_last_response();
    led_for_state(ST_IDLE);
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
    // Story-audio access token (gap 1). UNVERIFIED — not compiled/flashed.
    // When the backend has StoryAudio:SigningKey set, the header-less
    // /api/story-audio stream requires ?token=. Fetch it once per session
    // (TTL ~1 h >> a story). Empty/false when enforcement is OFF → we stream
    // without a token, which is correct in that case.
    // OFFLINE-FIRST source (Slice 2). If the content pack's narration MP3 is on
    // the SD card, play it from the card (no Wi-Fi, no token); otherwise fall
    // back to the Wi-Fi story stream. Decided once per session.
    const bool use_sd = audio_sd_has_file(AREG_SD_STORY_NARRATION);
    Serial.printf("[story] source = %s\n", use_sd ? "SD (offline)" : "Wi-Fi stream");
    Serial.flush();

    // Story-audio access token (gap 1) — only the Wi-Fi stream needs it.
    static char story_token[256];
    bool have_token = use_sd
        ? false
        : voice_fetch_story_audio_token(AREG_STORY_ID, story_token, sizeof(story_token));
    bool token_retry_used = false;

    bool active = true;
    while (active) {
        transition_to(ST_PLAYING);
        audio_speaker_begin();
        uint32_t resume_offset = 0;

        bool interrupted;
        bool stream_open_failed = false;
        if (use_sd) {
            Serial.printf("[story] SD play from byte %u\n", (unsigned)s_story_offset);
            Serial.flush();
            interrupted = audio_play_story_file(
                AREG_SD_STORY_NARRATION, s_story_offset, story_barge_in_poll, &resume_offset);
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
                AREG_STORY_ID, story_token, sizeof(story_token));
            continue;  // retry from the same s_story_offset
        }

        if (!interrupted) {
            s_story_offset = 0;
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
    Serial.flush();
    DIAG_MARK(135, "sd_mount_done");

    // Diag: register Wi-Fi event handler BEFORE join so the
    // initial CONNECTED / GOT_IP / DISCONNECTED events surface
    // in the boot log alongside the existing [wifi] lines.
    WiFi.onEvent(wifi_event_handler);

    DIAG_MARK(140, "wifi_begin_before");
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

    Serial.println("[boot] ready — press button to speak");
    Serial.flush();
    DIAG_MARK(150, "ready_idle");
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
        voice_wifi_tick();

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

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
#include "device_creds.h"      // NVS-first device identity (one-shot burn in setup)
#include "ble_provisioning.h"  // B.2 — BLE provisioning (gated; no-op when flag off)
#include "ota_foundation.h"    // Proof 2 — phone-home command poll (no OTA apply)
#include "sd_bench.h"          // microSD hardware proof (AREG_SD_BENCH_TEST builds only)
#include "content_sync.h"      // Cloud→SD content sync
#include "content_report.h"    // what this toy HAS on its card, for the heartbeat
#include "content_sync_test.h" // content-sync decision-logic tests (AREG_CONTENT_SYNC_TEST_BENCH only)
#include "sd_diag.h"           // standalone SD diagnostic (AREG_SD_DIAG_BENCH builds only)
#include "sd_playback.h"       // cached-MP3 SD playback (AREG_SD_PLAYBACK_BENCH builds only)
#include "answer_buttons.h"    // optional GREEN/RED answer buttons (no-op unless pins defined)
#include "volume_pot.h"        // optional hardware volume knob (no-op unless pot pin defined)
#include "offline_quiz.h"      // offline true/false quiz (AREG_OFFLINE_QUIZ_BENCH builds only)
#include "offline_games.h"     // mind-reader / buzzer / Simon (AREG_OFFLINE_GAMES_BENCH builds only)

#include "story_select.h"      // which cached story to play (index v2 + no-repeat)
#include "story_pause.h"       // shout-it-out pauses inside an SD story (parent-gated)
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
    // Resting level, printed once. With INPUT_PULLUP and nothing pressed this
    // must read UP; a boot that reports DOWN means the pin is held low by the
    // wiring, which on GPIO0 is also the download-mode gesture.
    Serial.printf("[button] pin=%d resting=%s\n",
                  (int)AREG_PIN_BUTTON, (s_raw_last == LOW) ? "DOWN" : "UP");
}

// Returns 'P' on a press edge, 'R' on a release edge, 0 otherwise.
static char button_poll() {
    uint8_t raw = digitalRead(AREG_PIN_BUTTON);
    if (raw != s_raw_last) {
        s_last_edge_ms = millis();
        s_raw_last = raw;
        // The button is the toy's ONLY physical input, and when it appears
        // dead there is no way to tell "the wire is off" from "the firmware
        // ignored it" -- 2026-08-18 cost an evening and a multimeter to that
        // ambiguity, and the real cause turned out to be neither. This prints
        // the raw pin edge BEFORE debounce, so a press shows up even when the
        // state machine is busy and drops it. It cannot flood: it fires only
        // on a physical level change, and the 30 ms debounce below still
        // decides what the toy acts on.
        Serial.printf("[button] raw=%s\n", (raw == LOW) ? "DOWN" : "UP");
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

// --- "What shall we do next?" after an activity ends ----------
// An activity (a story reaching its natural end, a finished offline game)
// asks the child what to do next. The flag is DEFERRED on purpose and is
// consumed ONLY at the top of loop(): handle_welcome_flow ->
// welcome_offer_story -> handle_story_session is already a call chain, so
// opening the menu from INSIDE a story session would be unbounded mutual
// recursion through the largest stack frame in the sketch — story ends,
// menu, story, ends, menu, until the stack is gone.
static bool s_ask_next_pending = false;
// Consecutive menus the toy opened by itself, with no press in between.
// Bounded so a child who walks away is not offered a story forever: two
// auto-menus, then the toy goes quiet until someone touches it again.
// Reset by any real button press.
static uint8_t s_auto_menu_chain = 0;

// --- Story browsing on the GREEN / RED buttons ---------------
// GREEN = the next story, RED = the previous one, while a story plays.
// story_barge_in_poll() returns a bare bool ("stop now") and cannot say
// WHY it stopped, so the reason is left here for the session loop to pick
// up — the same shape story_pause_take_pending() already uses to tell a
// self-inflicted pause apart from a child's press.
//
// Every declaration folds away without the answer-button pins, so a
// one-button build compiles exactly the code it compiled before (the
// AREG_HAS_ANSWER_BUTTONS idiom from answer_buttons.h).
#if AREG_HAS_ANSWER_BUTTONS
static char s_browse_request = 0;    // 'Y' = next story, 'N' = previous
static bool s_browse_restart  = false;  // the session must restart on a new story
static inline bool browse_pending() { return s_browse_request != 0; }
// One press is one hop: the request is cleared as it is read, so a single
// press can never be acted on twice.
static inline char browse_take_request() {
    const char r = s_browse_request;
    s_browse_request = 0;
    return r;
}
#else
// Only browse_pending() has a caller outside the guarded blocks (the
// rotation bookkeeping reads it unconditionally, and folds to a constant
// here). browse_take_request has none, so it is not defined at all.
static inline bool browse_pending() { return false; }
#endif

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
    // The child comes first: a real press is always a barge-in, and the
    // button must be polled every iteration to keep its edge state fresh.
    if (button_poll() == 'P') {
        return true;
    }
#if AREG_HAS_ANSWER_BUTTONS
    // Story browsing: GREEN = next story, RED = previous. Polled HERE
    // rather than in the decode loops because this callback is already
    // wired into all three of them — the same reason the volume knob is
    // read beside this call. The MAIN button is polled first and
    // unchanged, so barge-in latency and the sticky pause are untouched.
    const char ab = answer_buttons_poll();
    if (ab == 'Y' || ab == 'N') {
        if (!voice_in_bedtime_window()) {
            s_browse_request = ab;
            return true;
        }
        // Bedtime: swallow it. The same rule the shout-it-out pauses
        // already follow — being asked to choose is what 21:30 is not for,
        // and a story that jumped to a different one at bedtime would be
        // the loudest possible version of that.
    }
#endif
    // A shout-it-out pause is a self-inflicted barge-in — same stop, same
    // resume offset, no second decoder. story_pause_take_pending() is how
    // the session loop tells the two apart afterwards. Returns false
    // immediately whenever nothing is armed, which is every build/card/
    // moment the feature is not active.
    return story_pause_poll();
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

    // 2. The reflection question — exactly ONE (owner request 2026-08-15).
    //    This used to loop rounds 0..2, asking every rendered question back
    //    to back; that is two questions more than a four-year-old will
    //    answer, and the third arrived long after the child had wandered
    //    off. One round = ask → listen → record → upload → play the
    //    backend's reaction. The child is never badgered: no press in the
    //    window, a too-short answer, or any failure closes quietly.
    //
    //    Because only one is asked, WHICH one has to rotate, or a child who
    //    re-listens to a favourite is asked the same thing every time. The
    //    cursor is per story and persisted (see story_select.h).
    const int wanted = story_question_cursor_next(post_story_id);

    // Resolve with FALLBACK. The cursor names the question this story owes
    // the child, but a story may only ever have rendered `question` — most
    // do, and today none has any of the three. Probing the remaining kinds
    // in ascending order is what stops the toy going silent merely because
    // the cursor landed on a kind nobody authored.
    char question_path[CS_MAX_PATH_LEN];
    const char *question_clip = nullptr;
    int q = -1;
    for (int probe = 0; probe < CS_QUESTION_KINDS && question_clip == nullptr; probe++) {
        const int idx = (wanted + probe) % CS_QUESTION_KINDS;
        const char *kind = cs_question_clip_kind(idx);
        if (kind != nullptr
            && story_select_resolve_clip_path(post_story_id, kind,
                                              question_path, sizeof(question_path))) {
            question_clip = question_path;
            q = idx;
        }
    }
    if (question_clip == nullptr && audio_sd_has_file(AREG_SD_STORY_QUESTION0)) {
        // Legacy SD-pack fallback. It only ever held question 0, so it is
        // tried once, after every synced kind has been ruled out.
        question_clip = AREG_SD_STORY_QUESTION0;
        q = 0;
    }
    if (question_clip == nullptr) {
        // The story ships no reflection at all. This is the ORDINARY path
        // today — no story has a question clip configured — so it stays a
        // silent return rather than a line printed after every story end.
        return;
    }
    if (q != wanted) {
        Serial.printf("[post] question %d not on the card — asking %d instead\n",
                      wanted, q);
        Serial.flush();
    }

    transition_to(ST_PLAYING);
    audio_speaker_begin();
    Serial.printf("[post] question %d (%s)\n", q, question_clip);
    Serial.flush();
    bool question_started = false;
    audio_play_story_file(question_clip, 0, nullptr, nullptr, &question_started);

    // Advance the cursor only once the clip GENUINELY PLAYED — the same
    // rule story_select uses for `last_id` and the heard set. A question
    // that made no sound was not asked, and moving past it would skip it
    // for good on the next listen.
    if (question_started) {
        story_question_cursor_commit(post_story_id, q);
    }

    // The ANSWER needs the cloud (STT + the bounded reaction). Offline →
    // optional close, then stop.
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
    Serial.printf("[post] uploading answer %d (last=1)\n", q);
    Serial.flush();

    // last=true unconditionally: one question is always the final round, and
    // `last` is what makes the backend append the goodbye line.
    VoiceTurnResult turn = voice_upload_reflection_answer(
        payload, payload_bytes, q, /*last=*/true);
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
    }
    voice_release_last_response();
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

// May the toy open the menu BY ITSELF, because an activity just ended?
// Every clause is a reason it must not.
static bool ask_next_is_allowed() {
    // A paused toy is fully silent. handle_welcome_flow checks this too;
    // the constraint must not depend on a guard living in another
    // function, and checking here also keeps the chain counter still.
    if (voice_is_paused()) {
        return false;
    }
    // MODES.md forbids this at bedtime in two separate clauses: Calm mode
    // is "no tension, no cliffhangers, no choices that demand a decision",
    // and "questions of any kind" are listed under Forbidden. «What shall
    // we do next?» is exactly a question that demands a decision, and the
    // whole point of the window is winding down, not choosing.
    if (voice_in_bedtime_window()) {
        return false;
    }
    // A child who walked away must not be offered a story forever.
    if (s_auto_menu_chain >= 2) {
        return false;
    }
    // Never over a story that is only stickily PAUSED: the next press is
    // owed to that story, not to a menu.
    if (s_story_offset != 0) {
        return false;
    }
    // Never open a menu we cannot ask. Without the ask clip the flow's own
    // fallback is to start a story into a room nobody answered from, and a
    // missing clip turning into silence is the exact failure the owner hit
    // on the bench. If we cannot ask, the activity's own ending audio stays
    // the last thing heard, which is honest and quiet.
    char ask_path[CS_MAX_PATH_LEN];
    if (!voice_clip_resolve_path(CS_VOICE_ID_ASK_ANY, ask_path, sizeof(ask_path))) {
        Serial.println("[menu] ask clip missing — not opening the menu");
        Serial.flush();
        return false;
    }
    return true;
}

// The ONE eligible-story table in the sketch (~10 KB), shared by the
// welcome flow's offer loop and story_pick_for_session. They are never
// live at the same time: welcome_offer_story finishes with it — it has
// already copied the chosen id into s_current_story_id — before calling
// handle_story_session, which is what reaches the other user. Anything
// added AFTER that call must not expect this table to still hold the
// offer pool.
static CsStory s_eligible_stories[CS_MAX_STORIES];

#if AREG_HAS_ANSWER_BUTTONS
// Picks the story one hop from `current_id` — 'Y' forward, 'N' back —
// and writes its id into `out_id`. False means "nothing to browse to",
// and the caller must then leave the current story playing.
//
// ORDER: unheard stories first, then heard ones, each group keeping index
// order. That is the honest reading of the owner's "prioritize by which
// one is less told": the toy stores a heard / not-heard SET (NVS
// `aregheard`), NOT a play count, so a true least-played ordering does not
// exist to be computed. Do not assume counts are available here.
//
// Reuses s_eligible_stories rather than a second ~10 KB table. Safe by the
// same argument the table's own comment makes: every earlier user is
// finished with it by the time a story is playing.
static bool browse_pick(char dir, const char *current_id,
                        char *out_id, size_t out_len) {
    // One bit per eligible story. Keep the mask wide enough for the table.
    static_assert(CS_MAX_STORIES <= 16, "browse_pick's heard mask is 16 bits");

    CsStory *pool = s_eligible_stories;
    const int count = story_select_load_eligible(pool, CS_MAX_STORIES);
    if (count <= 1) {
        // 0 = nothing cached. 1 = one story is not a library: there is
        // nowhere to browse TO, and restarting the only story from the top
        // would read as a bug rather than as a choice.
        return false;
    }

    // Ask the heard-set ONCE per story. story_heard_contains re-reads the
    // whole NVS set on every call and builds a ~780-byte HeardSet on the
    // stack to do it, so the obvious two-pass loop would pay for that
    // thirty-two times on a button press, inside a story's stack frame.
    uint16_t heard = 0;
    for (int i = 0; i < count; i++) {
        if (story_heard_contains(pool[i].story_id)) {
            heard |= (uint16_t)(1u << i);
        }
    }

    // The browse order as INDICES — nothing in the shared table moves, so
    // this costs 16 bytes of stack instead of shuffling ~640-byte records.
    uint8_t order[CS_MAX_STORIES];
    int n = 0;
    for (int i = 0; i < count; i++) {
        if (!(heard & (uint16_t)(1u << i))) order[n++] = (uint8_t)i;
    }
    for (int i = 0; i < count; i++) {
        if (heard & (uint16_t)(1u << i)) order[n++] = (uint8_t)i;
    }

    // Where we are now. A story that is no longer in the list (denied by
    // the operator, retired, or its file vanished) is not an error — start
    // the walk at the top rather than refusing to browse.
    int at = 0;
    for (int k = 0; k < n; k++) {
        if (cs_story_ids_equal(pool[order[k]].story_id, current_id)) { at = k; break; }
    }

    const int step = (dir == 'N') ? -1 : 1;
    const int next = (at + step + n) % n;   // wraps both ways
    cs_copy_bounded(out_id, out_len, pool[order[next]].story_id);
    return true;
}
#endif  // AREG_HAS_ANSWER_BUTTONS

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
//
// child_present: true when a human physically held the button seconds
// ago, so the room is known to be occupied and silence means "did not
// understand", not "nobody is there". False at power-on, where the toy
// has no such evidence. Not a default argument on purpose: the Arduino
// auto-prototype generator mishandles them (see welcome_listen above).
static void welcome_offer_story(bool child_present) {
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
            // The asymmetry is the whole point: at power-on silence almost
            // always means the room is empty, so we close quietly. After a
            // hold it means a child who is standing right there did not
            // answer — and asking «shall I tell you X?» and then going dead
            // silent is the exact failure this function's header rejects.
            if (!child_present) {
                led_for_state(ST_IDLE);
                return;   // silence — do not start a story into an empty room
            }
            break;        // a child is here — play the story we just named
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

// child_present: true when a human physically held the button seconds ago
// (the IDLE hold-to-menu gesture), so silence means "did not understand"
// rather than "nobody is there". False at power-on. Not a default argument
// on purpose: the Arduino auto-prototype generator mishandles them.
static void handle_welcome_flow(bool child_present) {
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
            welcome_offer_story(child_present);
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
        if (story_ok) welcome_offer_story(child_present); else led_for_state(ST_IDLE);
        return;
    }

    // ---- 4. listen, with exactly one retry ----
    for (int attempt = 1; attempt <= AREG_WELCOME_MAX_TRIES; attempt++) {
        char intent[16];
        uint8_t *heard = nullptr;
        size_t heard_len = 0;
        if (!welcome_listen("mode", intent, sizeof(intent), &heard, &heard_len)) {
            // Same asymmetry as welcome_offer_story: at power-on silence
            // means an empty room, so close quietly. After a hold a child
            // is standing there — falling silent right after asking them
            // what they want is the failure this flow exists to avoid, so
            // break out to the graceful default below.
            // No heap_caps_free(heard) here: welcome_listen nulls
            // *keep_payload at entry and only assigns it after both of its
            // `return false` paths, so on this branch it is provably null.
            if (!child_present) {
                led_for_state(ST_IDLE);
                return;   // silence — never badger
            }
            break;
        }

        if (strcmp(intent, "story") == 0) {
            if (heard != nullptr) heap_caps_free(heard);
            if (story_ok) { welcome_offer_story(child_present); return; }
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
        welcome_offer_story(child_present);
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

// ONE story, start to finish. Called only by handle_story_session() below,
// which re-runs it when the child browses to a different story — a loop in
// ONE stack frame, never a recursive call into this frame again.
static void handle_story_session_once() {
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
        //
        // The wipe MUST NOT run when a caller has preselected a story, and for
        // a year it did. story_pick_for_session() opens with
        //     if (s_story_preselected && s_current_story_id[0] != '\0')
        // and this line cleared that id on the line above it, so the branch has
        // been unreachable since the day it was written. The wipe landed in
        // ee358ba (2026-07-27, story selection); the preselect consumer in
        // e137d9d (2026-08-04, welcome flow) was added BELOW a wipe that had
        // already been there a week, and nobody read the two together.
        //
        // What it cost: every time the welcome flow asked a child «do you want
        // «X»?» and the child said yes, the toy played whatever the rotation
        // picked instead. It always played SOMETHING, so it never looked broken.
        // The story browser depends on the same seam and would have restarted
        // the same story on every press.
        if (!s_story_preselected) {
            s_current_story_id[0] = '\0';
        }
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

    // Shout-it-out pauses. The whole gate chain collapses into one bool
    // here, and every part of it is a reason a child should NOT be invited
    // to shout:
    //   - SD only. The pause needs the file's size to know where the
    //     ending is, and a Wi-Fi stream cannot tell us that.
    //   - the parent's toggle, cached in the index so it applies offline;
    //   - the bedtime window (server-owned; the toy has no clock). A pause
    //     invites shouting, which is the opposite of what 21:30 is for.
    // Clips-on-the-card and story-long-enough are checked inside
    // story_pause_session_begin. new_story = false on a resume, so the
    // at-most-twice ceiling counts per STORY, not per press.
    // AREG_STORY_PAUSES_ENABLED — DEFAULT OFF (owner report 2026-08-10:
    // "the story is cut"). Mid-story pauses were wired on 2026-08-07 and
    // have NEVER been bench-run; like the welcome flow, they went live the
    // moment their clips reached the card, changing playback nobody had
    // tested. The parent toggle and the manifest still work — this is an
    // extra firmware-side gate that keeps an untested behaviour off until
    // it is deliberately tested. Set to 1 to bench it.
#ifndef AREG_STORY_PAUSES_ENABLED
#define AREG_STORY_PAUSES_ENABLED 0
#endif
    const bool pauses_ok = AREG_STORY_PAUSES_ENABLED
                           && use_sd
                           && !voice_in_bedtime_window()
                           && story_pauses_enabled();
    story_pause_session_begin(s_story_offset == 0, pauses_ok, sd_narration_path);

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
        // Arm (or disarm) the next shout-it-out pause for THIS segment.
        // A no-op unless the session gate above passed.
        story_pause_segment_begin(s_story_offset);
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
        //
        // browse_pending() is the fourth guard, and it is the reason this
        // line moved: the child has just asked for a DIFFERENT story, so
        // this one is abandoned, not heard. Without it a browse would both
        // advance the rotation cursor and mark the abandoned story heard —
        // the toy would stop offering a story nobody ever listened to. It
        // must not mark a failed start either: an abandoned story has not
        // failed at anything. Folds to a constant on a one-button build.
        if (!selection_settled && s_current_story_id[0] != '\0' && !browse_pending()) {
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
            // A story that reached its natural END asks what to do next.
            // ORDER IS LOAD-BEARING, twice over. It sits AFTER
            // handle_post_story_flow() so the reflection dialogue finishes
            // first — and because it only SETS A FLAG it could not
            // pre-empt that dialogue even if it ran early. And it is
            // inside the !interrupted branch ONLY: every interrupted path
            // (shout-pause, sticky pause, a Q&A barge-in, the token retry,
            // a browse) leaves the flag alone, so a story the child merely
            // paused is never followed by a menu.
            s_ask_next_pending = ask_next_is_allowed();
            Serial.println("[story] finished — press to play again");
            Serial.flush();
            break;
        }
        s_story_offset = resume_offset;
        token_retry_used = false;  // a real segment played; allow a fresh retry later

#if AREG_HAS_ANSWER_BUTTONS
        // Story browsing: this stop was a GREEN/RED press, not the main
        // button. Runs BEFORE the pause and barge-in handling below so the
        // press is never also read as a question or a sticky pause.
        //
        // The chosen story starts from the beginning immediately — the
        // story IS the preview. Nothing announces its title, because no
        // per-story name clip exists on any card (the offer/reoffer clip
        // texts are written but none is rendered or configured), and
        // inventing a spoken title here would mean a clip that is not
        // there, i.e. silence.
        if (const char dir = browse_take_request()) {
            char picked[CS_MAX_STORY_ID_LEN + 1];
            if (browse_pick(dir, s_current_story_id, picked, sizeof(picked))) {
                Serial.printf("[browse] %s -> %s (%s)\n",
                              s_current_story_id[0] ? s_current_story_id : "(none)",
                              picked, dir == 'N' ? "prev" : "next");
                Serial.flush();
                // Exactly the commit pattern welcome_offer_story uses. The
                // offset of 0 is what makes the restart a genuine NEW-story
                // session, so story_pause_session_begin re-runs and the
                // abandoned story's pause state is cleared for free.
                //
                // BLOCKED — DO NOT FLASH AND EXPECT THIS TO WORK YET.
                // That pattern has never worked. The new-story boundary at
                // the top of this function clears s_current_story_id on the
                // line BEFORE it calls story_pick_for_session, whose
                // preselect branch then tests s_current_story_id[0] != '\0'
                // and is therefore unreachable. The wipe landed 2026-07-27
                // (story selection), the preselect consumer 2026-08-04
                // (welcome flow), and nothing since has read the two
                // together — so «yes, tell me that one» in the welcome flow
                // has always played the rotation's pick instead. Browsing
                // inherits the same fault: without the ordering fix, every
                // press restarts whatever the rotation cursor points at
                // rather than the story picked here. Flagged for the owner,
                // deliberately NOT fixed here — repairing it changes live
                // welcome-flow behaviour that has never been bench-run.
                s_story_offset = 0;
                cs_copy_bounded(s_current_story_id, sizeof(s_current_story_id), picked);
                s_story_preselected = true;
                s_browse_restart = true;
                break;   // the wrapper re-enters on the picked story
            }
            // Nothing to browse to (an empty or one-story card). Fall
            // through to the normal interrupted handling rather than
            // swallowing the press — a press that does nothing at all
            // reads as a broken toy.
            Serial.println("[browse] nothing to browse to");
            Serial.flush();
        }
#endif

        // Shout-it-out pause: this stop was OUR timer, not the child. Play
        // the invite, hold a short silent beat, play the resume line, then
        // fall back into the same loop — which re-opens the SAME file at
        // the SAME offset. Nothing else moves: s_current_story_id is
        // untouched (so no re-selection), selection_settled and
        // play_reported are already latched (so the rotation cursor, the
        // heard-set and the play report cannot fire twice), and
        // `interrupted` is true (so this is never mistaken for a natural
        // end). The mic is never opened on this path.
        if (story_pause_take_pending()) {
            story_pause_run(s_story_offset);
            continue;
        }

        // Barge-in: capture the question while the button stays held.
        transition_to(ST_RECORDING);
        const size_t captured = record_question();
        // Latency anchor. Taken HERE — the instant the child stops speaking —
        // not after the earcon, which is where it used to sit and which made
        // the printed figure ~600 ms kinder than what the child experienced.
        const uint32_t qa_release_ms = millis();
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
        //
        // ORDER (latency, 2026-08-10): the upload starts FIRST, and the earcon
        // is simply the first bed pulse. It used to be the other way round —
        // a blocking ~600 ms tone played to completion and only then was the
        // POST fired — so every question paid 600 ms of network idle before a
        // single byte left the toy. The child's experience is unchanged (the
        // tone still begins within a few ms of them letting go); the request
        // is now 600 ms further ahead.
        // -------------------------------------------------------
        {
            voice_start_question_upload_async(payload, payload_bytes,
                                              s_story_offset);

            // audio_speaker_begin() before the first pulse: the earcon builds
            // its own AudioOutputI2S, same as audio_play_mp3_buffer.
            audio_speaker_begin();

            // Play thinking-bed pulses while the upload is in flight.
            // Each pulse is a short synthesized tone; we poll done after
            // each one. The pulse duration (AREG_THINKBED_PULSE_MS) trades
            // responsiveness (shorter → answer starts sooner after upload)
            // against audio quality (longer → fewer AudioOutputI2S re-inits).
            //
            // HARDWARE ASSUMPTION: repeated AudioOutputI2S begin/stop within
            // synth_play_pulse() is well-tolerated by the MAX98357A. If
            // re-init clicks are audible, replace the per-pulse I2S
            // construction with a long tone whose amplitude we fade down
            // (requires exposing a "play N samples then stop" API or
            // restructuring synth_write_tone to accept a done_fn callback).
            //
            // PULSE 1 IS THE EARCON AND STAYS EXACTLY AS IT WAS: it is the
            // child's acoustic receipt for letting go of the button, so it
            // keeps its own pitch, length and loudness. Pulses 2+ are the
            // thinking bed proper — lower, shorter, quieter, and moving.
            //
            // FIXED 2026-08-16 (this is the TODO that used to sit here): the
            // bed was a second call to the earcon, so the entire wait was one
            // 440 Hz 600 ms beep repeated up to 70 times — the 4th identical
            // beep by second 2, the 16th by second 10. The monotony was the
            // boredom, more than the duration. The three AREG_THINKBED_ tone
            // constants had been declared for exactly this from the start and
            // were read by no sound path at all; only the pulse cap was live.
            // synth_write_tone() was already fully parameterised, so the fix
            // was to stop calling the earcon here — not to write a new synth.
            int bed_count = 0;
            while (!voice_async_upload_done() &&
                   bed_count < AREG_THINKBED_MAX_PULSES) {
                esp_task_wdt_reset();  // #047 — feed across the per-pulse bed
                // ABORTABLE (latency, 2026-08-10): the pulse checks the
                // upload's done flag every ~16 ms and fades out early instead
                // of running its full length. Before this, an answer that
                // arrived 20 ms into a pulse still waited out the other 580 ms
                // — 0-600 ms of dead time on every question, ~300 ms on
                // average. BOTH calls below keep it, and the bed's shorter
                // pulse only adds more between-pulse chances to notice, so
                // the worst-case wait after an answer lands cannot grow.
                if (bed_count == 0) {
                    audio_play_thinking_earcon_abortable(voice_async_upload_done);
                } else {
                    audio_play_thinking_bed_abortable((uint32_t)(bed_count - 1),
                                                      voice_async_upload_done);
                }
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
                // THE KEY NAME CHANGES WITH THE PATH, and that is deliberate.
                // On the streaming path turn.ok is published at the response
                // HEADERS (voice_client.cpp), not after the body has landed,
                // so this stopwatch stops at the first byte -- a different
                // quantity from the buffered build's, and a much smaller
                // number. Printing both under one name would let a definition
                // change be read as a saving; the two are not comparable, and
                // a log line has to say which one it is on its own.
#ifdef AREG_QA_STREAM_PLAYBACK
                Serial.printf("[latency] qa_release->%s=%u\n",
                              turn.streaming ? "first_byte_ms" : "play_begin_ms",
                              (unsigned)qa_latency_ms);
#else
                Serial.printf("[latency] qa_release->play_begin_ms=%u\n",
                              (unsigned)qa_latency_ms);
#endif
                Serial.flush();

                // Play the answer.
                //
                // NOTE the thing NOT done here: audio_play_qa_stream(url) opens
                // its OWN GET, and /api/chat/story-qa is POST-only — and if a
                // GET were ever added there it would re-run STT+GPT+TTS and
                // double-bill one child's question. The answer only ever exists
                // as the body of the POST the toy already made, so the streaming
                // form below decodes THAT body instead of fetching a second one.
                //
                // Default (flag off): the async task buffered the whole body in
                // PSRAM and we decode from memory. Correct, and the only shape
                // that has ever run on hardware.
#ifdef AREG_QA_STREAM_PLAYBACK
                if (turn.streaming) {
                    // Flag on: the task stopped at the headers. Decode the live
                    // socket, so sound starts on the first frames instead of
                    // after the last byte lands. No buffered copy exists on
                    // this path — a failure means the child heard nothing, so
                    // it falls through to the canned failure clip.
                    audio_speaker_begin();
                    const bool streamed = audio_play_qa_stream_response(
                        voice_qa_stream_body(), voice_qa_stream_content_length());
                    voice_qa_stream_finish();   // ALWAYS — closes the response
                    if (streamed) {
                        Serial.println("[qa] answer played (streamed POST response)");
                    } else {
                        Serial.println("[qa] streamed answer failed; playing failure clip");
                        play_canned_failure_clip();
                    }
                    Serial.flush();
                } else
#endif
                {
                    audio_speaker_begin();
                    audio_play_mp3_buffer(turn.response_bytes, turn.response_length);
                    Serial.println("[qa] answer played (buffered POST response)");
                    Serial.flush();
                }
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
    // Leave nothing armed behind: a clip played elsewhere (the welcome
    // greeting, an offline game) shares this barge-in callback and must
    // never be cut by a timer belonging to a story that stopped.
    story_pause_disarm();
    transition_to(ST_IDLE);
}

// The story session every caller sees. Browsing restarts the session on
// the picked story from HERE — a loop in one stack frame — instead of
// handle_story_session_once calling itself. Recursion would grow the
// sketch's largest frame once per button press, and the child holding
// GREEN is exactly the case that would find the bottom of the stack.
//
// One pass per story played; each pass re-does precisely what a fresh
// new-story session does, because s_story_offset was reset to 0 and the
// picked id was handed over through the existing preselect flag.
static void handle_story_session() {
#if AREG_HAS_ANSWER_BUTTONS
    for (;;) {
        s_browse_restart = false;
        handle_story_session_once();
        if (!s_browse_restart) {
            break;
        }
    }
#else
    handle_story_session_once();
#endif
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

    // One-shot identity burn into NVS (2026-08-07). Why this exists:
    // this toy has always authenticated with the COMPILE-TIME identity,
    // so its credentials lived in exactly one place — the image on the
    // chip — and `config.h` is gitignored. When that file was lost and
    // restored from an old build cache it came back with a STALE device
    // id, every new build 401'd, three OTA attempts rolled back, and the
    // working key was destroyed by the next flash because a device key
    // is only ever stored hashed server-side.
    //
    // device_creds is already NVS-first (see voice_client.cpp), so once
    // the identity is in NVS it survives any config.h and every future
    // image can ship WITHOUT a real credential in it — which also means
    // one OTA image stops being one toy's secret.
    //
    // Guarded three ways: the flag must be defined, NVS must be empty
    // (never overwrites a provisioned toy), and the compile-time id must
    // not be the placeholder. Flash once with the flag, then drop it.
#ifdef AREG_PROVISION_IDENTITY_ONCE
    if (device_creds_present()) {
        Serial.println("[device] identity already in NVS — burn skipped");
    } else if (strcmp(AREG_DEVICE_ID, "YOUR_DEVICE_ID") == 0
               || strcmp(AREG_DEVICE_API_KEY, "YOUR_DEVICE_API_KEY") == 0) {
        Serial.println("[device] refusing to burn placeholder credentials");
    } else {
        device_creds_save(AREG_DEVICE_ID, AREG_DEVICE_API_KEY);
        Serial.printf("[device] identity burned to NVS (id=%s)\n", AREG_DEVICE_ID);
    }
    Serial.flush();
#endif

    // Same one-shot burn, for Wi-Fi. Added 2026-08-16 as the ORDERING FIX that
    // makes BLE provisioning safe to switch on.
    //
    // With AREG_USE_BLE_PROVISIONING on, setup() branches on
    // voice_wifi_is_provisioned() and, when NVS is empty, opens provisioning
    // and NEVER calls voice_wifi_begin() -- the config.h fallback is not
    // consulted at all. The owner's toy has real Wi-Fi ONLY in config.h and an
    // empty aregwifi namespace, so enabling the flag alone would have taken a
    // working toy off the network and left it waiting for a phone app that has
    // no Android build yet. This burn runs first, so the toy is genuinely
    // provisioned before that branch is reached.
    //
    // Guarded the same three ways as the identity burn: flag defined, NVS
    // empty (never overwrites a toy a parent has already provisioned), and the
    // compile-time SSID not a placeholder. Burn once, then drop the flag --
    // and never build an OTA image with it, for the reason c9e6593 records.
#ifdef AREG_PROVISION_WIFI_ONCE
    if (wifi_creds_present()) {
        Serial.println("[wifi] credentials already in NVS — burn skipped");
    } else if (strlen(AREG_WIFI_SSID) == 0
               || strcmp(AREG_WIFI_SSID, "YOUR_WIFI_SSID") == 0) {
        Serial.println("[wifi] refusing to burn a placeholder SSID");
    } else {
        wifi_creds_save(AREG_WIFI_SSID, AREG_WIFI_PASSWORD);
        Serial.printf("[wifi] credentials burned to NVS (ssid=%s)\n", AREG_WIFI_SSID);
    }
    Serial.flush();
#endif

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
    // Before anything can make a sound: this takes the first ADC reading, so
    // the greeting already comes out at the knob's position instead of opening
    // at the default and correcting itself audibly a moment later.
    volume_pot_begin();       // no-op unless AREG_PIN_VOLUME_POT defined
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
        // Read what is on the card BEFORE the first heartbeat. Without this,
        // a toy that boots with a complete, current library and finds
        // nothing to sync would report nothing at all — and the dashboard
        // would say "this toy has not told us yet" about a toy that is
        // perfectly up to date.
        content_report_refresh();
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

    // ---- first boot after an OTA: check in BEFORE anything else ----
    // The check-in deadline is measured from BOOT, and everything below
    // this line can take minutes: handle_welcome_flow() plays a greeting,
    // opens a listening window, and on the ordinary path goes straight
    // into handle_story_session() — a whole 3-4 minute story, its pauses,
    // and the reflection dialogue — WITHOUT returning to loop(). So on the
    // boot that decides confirm-vs-rollback, the very first check-in
    // attempt was minutes late (2026-08-07: 1.1.0 rolled back with
    // `rollback_no_checkin`). This is the earliest point in setup() where
    // the radio is up and the device is in a defined state, so the attempt
    // happens here.
    //
    // Only run when an outcome is actually pending: on an ordinary boot
    // this is a cheap NVS read and changes nothing (in particular it must
    // NOT pull the command poll forward, or a fresh firmware_update could
    // start a download inside setup()).
    if (ota_outcome_pending()) {
        ota_foundation_tick();   // state==REBOOTING -> runs the check-in
    }
    if (ota_outcome_pending()) {
        // Still unresolved (ack failed, or the link is not up yet). Skip
        // the greeting THIS boot and hand the loop straight to the retry.
        // One silent power-on beats rolling a healthy image back — and
        // this only ever happens on the single boot after an update.
        Serial.println("[ota] outcome still pending — skipping the greeting so "
                       "the check-in owns the loop");
        Serial.flush();
        transition_to(ST_IDLE);
        return;
    }

    // ---- the toy's opening ----
    // Runs LAST in setup, after the provisioning gesture has had its
    // chance (holding the button at boot must never be mistaken for an
    // answer to a menu question).
    //
    // DEFAULT ON (owner, 2026-08-10: "I turn on the toy, it greets me,
    // it asks me what to do" — that IS the product). It was switched off
    // on 2026-08-09 on a misreading of "give me an initial point"; the
    // owner wanted this flow to WORK, not to disappear.
    //
    // What actually made it feel broken on 08-09 was NOT the greeting: it
    // was the one-time content sync downloading 135 new clips before the
    // toy settled, so minutes passed before it spoke and the button did
    // nothing in the meantime. Those files are on the card now, so the
    // greeting starts promptly.
    //
    // Set AREG_WELCOME_FLOW_ENABLED to 0 to silence the opening again.
#ifndef AREG_WELCOME_FLOW_ENABLED
#define AREG_WELCOME_FLOW_ENABLED 1
#endif
#if AREG_WELCOME_FLOW_ENABLED
    // One best-effort heartbeat first: it is a ~200 ms POST that already
    // exists, and it makes the greeting reflect the parent's CURRENT
    // pause/bedtime state whenever the toy is online. Offline, the values
    // voice_state_restore() seeded from NVS stand.
    if (voice_wifi_is_connected()) {
        voice_send_heartbeat();
    }
    // child_present=false: at power-on the toy has no evidence anyone is in
    // the room, so silence must stay a quiet close. Only the IDLE hold
    // gesture (a hand on the button seconds ago) passes true.
    handle_welcome_flow(/*child_present=*/false);
#else
    (void)handle_welcome_flow;   // keep it compiled (and warning-free) while unused
    Serial.println("[boot] welcome flow disabled - press the button to talk");
    Serial.flush();
#endif
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

        // Read the volume knob while nothing is playing, so a turn made
        // between stories is already applied when the next one starts. The
        // three long decode loops in audio_io.cpp do their own reading — this
        // branch cannot run while they are blocked. Deliberately NOT wrapped
        // in an s_last_..._ms gate like the ticks around it: volume_pot_tick()
        // self-throttles to AREG_VOLUME_READ_MS, and a second timer on top
        // would beat against the first and could stretch the effective period
        // to twice that. Unthrottled here it is a millis() compare per pass.
        volume_pot_tick();

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

        // FIRST BOOT AFTER AN UPDATE: the check-in owns the tick, and that
        // means nothing long-running may run beside it.
        //
        // FIELD EVIDENCE (2026-08-07, owner's toy, 1.1.0 over the air): the
        // device polled, applied and went quiet — correct, there is no ack
        // before the reboot — and ~5m41s later the OLD image acked
        // `failed / rollback_no_checkin` with
        // {"status":"rolled_back","attemptedVersion":"1.1.0"}. Download,
        // flash and bootloader rollback all worked. What did NOT happen is
        // the new image finishing its check-in inside the deadline.
        //
        // content_sync_tick() is the most likely reason it never got the
        // chance: it arms 180 s after boot and then downloads the WHOLE
        // library — stories plus ~4.6 MB of game clips — inside a SINGLE
        // loop iteration. While it is in there, ota_foundation_tick() above
        // is not running, so neither the check-in retry nor the deadline
        // test happens. story_report_tick() is smaller but is the same
        // class of thing (a network upload before the verdict), so it is
        // gated too. Both come back the moment the image is confirmed (or
        // rolled back), which is one tick later in the normal case.
        const bool ota_pending = ota_outcome_pending();
        if (ota_pending) {
            static bool s_logged_hold = false;
            if (!s_logged_hold) {
                s_logged_hold = true;
                Serial.println("[ota] outcome pending — holding content sync and "
                               "story-play upload until the image is confirmed");
                Serial.flush();
            }
        } else {
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
        }

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
        // A finished game asks what to do next, exactly as a finished
        // story does. Same deferred flag, for the same reason: the menu
        // can start a story, and starting one from inside a game's own
        // call stack is the recursion this flag exists to avoid.
        if (offline_games_consume_finished() && ask_next_is_allowed()) {
            s_ask_next_pending = true;
        }
#endif

        // An activity ended and the toy owes the child a question. Consumed
        // HERE, at the top level, and nowhere else — see s_ask_next_pending.
        if (s_ask_next_pending) {
            s_ask_next_pending = false;
            s_auto_menu_chain++;
            Serial.printf("[menu] activity ended — asking what next (chain=%u)\n",
                          (unsigned)s_auto_menu_chain);
            Serial.flush();
            // child_present = FALSE, and this is load-bearing: nobody
            // touched the toy when a story or a game ended. False is what
            // selects the quiet-close path on silence — which is exactly
            // "the child walked away". True would make the toy say
            // «I didn't hear you», ask again, and then start four minutes
            // of story into an empty room.
            handle_welcome_flow(/*child_present=*/false);
            // MANDATORY, same reason as the hold-to-menu call below.
            transition_to(ST_IDLE);
            return;   // one action per loop pass
        }

        char ev = button_poll();
        if (ev == 'P') {
            // A real press means the child is driving. Drop anything the
            // toy had queued for itself: an auto-menu must never stack on
            // top of a deliberate action, and the chain that bounds those
            // menus starts again from here.
            s_auto_menu_chain = 0;
            s_ask_next_pending = false;
            Serial.println("[button] pressed");
            Serial.flush();
            DIAG_MARK(200, "button_press");
            // Parent PAUSE: a paused toy is fully silent — even local SD
            // story/music playback is skipped (pause used to gate only the
            // online chat path, so a child could still play cached stories).
            // The pause state is heartbeat-cached; when offline the last-known
            // value stands. A paused press just flicks the LED, no sound.
            if (voice_is_paused()) {
                Serial.println("[button] ignored — toy is paused");
                Serial.flush();
                led_for_state(ST_IDLE);
            } else if (voice_in_bedtime_window()) {
                // Inside the bedtime window a hold is just a press — never
                // the menu. A cheerful greeting at 21:30 is what
                // handle_welcome_flow's own bedtime guard exists to prevent,
                // and a hold that did nothing at all would read as a broken
                // toy. Behaviour here is byte-identical to before this change.
                char music_path[CS_MAX_PATH_LEN];
                if (s_story_offset == 0
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
                    handle_story_session();
                }
            } else {
                // Classify hold vs quick press. Local timer on purpose:
                // button_poll() has four callers and three of them run during
                // playback where a duration is meaningless, so widening its
                // signature would put hold state on the barge-in hot path for
                // one consumer's benefit.
                const uint32_t press_started = millis();
                bool released_early = false;
                while (millis() - press_started < AREG_MENU_HOLD_MS) {
                    if (button_poll() == 'R') { released_early = true; break; }
                    delay(AREG_BUTTON_POLL_MS);
                    esp_task_wdt_reset();
                }
                if (!released_early) {
                    Serial.println("[button] hold — opening the menu");
                    Serial.flush();
                    handle_welcome_flow(/*child_present=*/true);
                    // MANDATORY. handle_welcome_flow was written to be called
                    // only from setup(), which restores the state itself.
                    // SEVEN of its exits return with s_state still ST_PLAYING
                    // (five in handle_welcome_flow itself, one in
                    // welcome_offer_story, one in handle_online_chat_session
                    // — the count read "six" here and was one short), and
                    // loop() only accepts input while s_state == ST_IDLE —
                    // without this line the toy takes one hold and then
                    // ignores the button until a power cycle, which is the
                    // exact failure this whole change exists to remove.
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
    }
    delay(AREG_BUTTON_POLL_MS);
}

// -------------------------------------------------------------
// AregVoiceMvp / voice_client.h
//
// Wi-Fi association + the one HTTP POST to /api/chat/audio.
// Request body is a pre-composed WAV payload (header + PCM)
// held in the caller-owned buffer. Response body is read in
// full into a PSRAM buffer that this module owns, and returned
// as a pointer + length for the caller to feed into the MP3
// decoder. Lifetime is scoped to a single turn.
// -------------------------------------------------------------
#pragma once

#include <Arduino.h>

// Block until Wi-Fi is associated. Returns true on success,
// false on timeout. Called once in setup(). A false return is NOT fatal —
// setup() proceeds to IDLE and voice_wifi_tick() recovers in the background.
bool voice_wifi_begin();

// Return true if Wi-Fi is currently up. Fast check.
bool voice_wifi_is_connected();

// Phase B.1 — true once the toy has been provisioned with a real network
// (credentials stored in NVS). When false, the toy is running on the
// compile-time fallback creds (bench); the BLE provisioning layer (B.2) uses
// this at boot to decide whether to enter provisioning mode.
bool voice_wifi_is_provisioned();

// Phase B.1 — store new Wi-Fi credentials (from provisioning) in NVS and
// reconnect to that network. Persists across reboots. This is the seam the
// BLE provisioning layer (B.2) calls when the parent's app supplies the home
// Wi-Fi name + password over Bluetooth.
void voice_wifi_set_credentials(const char *ssid, const char *password);

// #045 — non-blocking Wi-Fi maintenance. Call frequently from loop() while
// idle. When the link is down it re-issues a join with capped exponential
// backoff so a router blip (or a slow/absent router at power-on) recovers
// WITHOUT a power-cycle; when up it is nearly free (a status check) and
// resets the backoff. Never blocks. Tunable via AREG_WIFI_RECONNECT_MIN_MS /
// AREG_WIFI_RECONNECT_MAX_MS (see config.h.example).
void voice_wifi_tick();

// B.3 — how long the link has been CONTINUOUSLY down, in ms. Returns 0 when
// connected (or before the first tick observed a drop). Drives the auto-
// fallback to BLE provisioning: a toy that was provisioned but cannot rejoin
// for a long stretch (moved house / router replaced) re-opens provisioning on
// its own. Updated by voice_wifi_tick(); call that first each loop.
uint32_t voice_wifi_down_duration_ms();

// Result of a single voice turn upload.
struct VoiceTurnResult {
    // true iff HTTP status was 200 AND a response body was
    // successfully buffered. false on any failure path.
    bool ok = false;
    // HTTP status code as reported by HTTPClient. Negative on
    // transport-level failures (no response at all).
    int http_status = 0;
    // When ok == true: pointer to a PSRAM buffer the caller
    // must NOT free; voice_release_last_response() frees it.
    const uint8_t *response_bytes = nullptr;
    size_t response_length = 0;
    // true when the backend set `X-Areg-Continue: 1` on the
    // response — a library story has more to play. The caller
    // should auto-fetch the next segment via voice_continue_turn()
    // with no button press. false ends hands-free autoplay.
    bool continue_more = false;
};

// POST `payload` (WAV header + PCM) to AREG_BACKEND_URL with
// the required device-auth headers. Allocates a PSRAM buffer
// for the response body (up to AREG_PLAYBACK_BUFFER_BYTES) and
// returns it via the result struct. Call voice_release_last_response()
// once the caller is done with the bytes (always — including
// on ok == false, it is a no-op in that case).
VoiceTurnResult voice_upload_turn(const uint8_t *payload, size_t length);

// In-story Q&A upload. POSTs the WAV `payload` (the recorded question)
// to AREG_STORY_QA_URL with "?storyId=AREG_STORY_ID&offset=<offset>" and
// the device-auth headers. The response body (on HTTP 200) is the spoken
// answer MP3, returned via the result's PSRAM buffer exactly like
// voice_upload_turn. `offset` is the story byte position at the
// barge-in, used by the backend to ground the answer's context.
VoiceTurnResult voice_upload_question(const uint8_t *payload, size_t length,
                                      uint32_t offset);

// Post-story reflection answer upload (Slice 3). POSTs the WAV `payload` (the
// child's recorded answer to Areg's reflection question) to
// AREG_STORY_REFLECTION_URL with "?storyId=AREG_STORY_ID&questionIndex=<n>"
// and the device-auth headers. The response body (on HTTP 200) is the spoken
// warm-acknowledgement MP3, returned via the result's PSRAM buffer exactly
// like voice_upload_question. Synchronous; call voice_release_last_response()
// when done with the bytes. UNVERIFIED — not compiled/flashed.
VoiceTurnResult voice_upload_reflection_answer(const uint8_t *payload, size_t length,
                                               int question_index);

// Hands-free autoplay continuation: POST to AREG_BACKEND_URL with
// the device-auth headers AND `X-Areg-Continue: 1` and an EMPTY
// body. The backend advances the active library story and returns
// the next segment's MP3 (with `X-Areg-Continue` again while more
// remains), or HTTP 204 when the story is over. On 204 / any
// non-200 the result is ok == false, which ends the autoplay loop.
// Same PSRAM ownership contract as voice_upload_turn.
VoiceTurnResult voice_continue_turn();

// Free the PSRAM response buffer owned by voice_client. Safe
// to call even if the last upload failed.
void voice_release_last_response();

// Fetches a story-audio access token from the backend (device-authed GET to
// /api/chat/story-audio-token?storyId=<id>; the URL is derived from
// AREG_BACKEND_URL, so no extra config constant is needed). Writes the token
// into out_token (capacity out_cap) and returns true on HTTP 200 with a
// non-null token. Returns false when enforcement is OFF (the backend returns
// token: null) OR on any error — the caller then streams WITHOUT a token,
// which is the correct behavior while StoryAudio:SigningKey is unset.
// UNVERIFIED — not compiled or flashed.
bool voice_fetch_story_audio_token(const char *story_id, char *out_token, size_t out_cap);

// ---------------------------------------------------------------
// Async Q&A upload (S3 dead-air mitigation)
// UNVERIFIED — not compiled/flashed. See HARDENING-INTEGRATION.md §2.
// ---------------------------------------------------------------
//
// voice_start_question_upload_async() launches a FreeRTOS task on CORE 1
// that performs the same POST as voice_upload_question() while the main
// loop (CORE 0) plays the thinking-bed audio.
//
// OWNERSHIP CONTRACT:
//   - `payload` MUST remain valid (caller-owned PSRAM) until
//     voice_async_upload_done() returns true AND the caller calls
//     voice_release_last_response(). The task reads from the payload
//     pointer directly; it does NOT copy it.
//   - The internal PSRAM response buffer is allocated by the task
//     (heap_caps_malloc on PSRAM) and owned by voice_client, exactly
//     like the synchronous voice_upload_question() path. voice_release_
//     last_response() frees it in both cases.
//   - voice_start_question_upload_async() returns immediately. The task
//     handle is private. Do NOT call it again before the prior task has
//     been reaped (i.e. voice_async_upload_done() returned true AND
//     voice_release_last_response() called OR handle cleanup done).
//
// CORE ASSIGNMENT:
//   - The upload task is pinned to CORE 1 (xTaskCreatePinnedToCore,
//     tskNO_AFFINITY falls back to core 1 when core 0 is saturated).
//   - HARDWARE ASSUMPTION: the ESP32-S3 has two cores; the Arduino
//     loop() runs on CORE 1 by default in the Arduino-ESP32 framework.
//     Pinning the upload task to CORE 0 leaves CORE 1 (where loop() runs)
//     free for the thinking-bed decode. If Arduino changes its core
//     assignment, update the pinToCore constant below.
//   - I2S access is CORE-agnostic (I2S DMA is handled by the hardware +
//     ESP-IDF interrupt, not tied to a single core), so the upload task
//     holding the Wi-Fi socket while CORE 1 calls AudioOutputI2S is safe.
//
// Start the async upload. Returns immediately. The task is pinned to CORE 0.
// payload / length / offset have the same meaning as voice_upload_question().
void voice_start_question_upload_async(const uint8_t *payload,
                                       size_t length,
                                       uint32_t offset);

// Returns true once the background task has finished (successfully or not).
// Call this in a polling loop from CORE 1's thinking-bed player. It is
// safe to call repeatedly; once it returns true the task has exited and
// voice_client owns the completed result in its internal state.
//
// After this returns true, retrieve the result via voice_get_async_result()
// then call voice_release_last_response() when done with the bytes.
bool voice_async_upload_done();

// Retrieve the result after voice_async_upload_done() returns true.
// The returned VoiceTurnResult has the same semantics as voice_upload_question().
// MUST only be called after voice_async_upload_done() == true.
VoiceTurnResult voice_get_async_result();

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
// false on timeout. Called once in setup().
bool voice_wifi_begin();

// Return true if Wi-Fi is currently up. Fast check.
bool voice_wifi_is_connected();

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

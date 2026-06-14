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

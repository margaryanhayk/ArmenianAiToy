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
};

// POST `payload` (WAV header + PCM) to AREG_BACKEND_URL with
// the required device-auth headers. Allocates a PSRAM buffer
// for the response body (up to AREG_PLAYBACK_BUFFER_BYTES) and
// returns it via the result struct. Call voice_release_last_response()
// once the caller is done with the bytes (always — including
// on ok == false, it is a no-op in that case).
VoiceTurnResult voice_upload_turn(const uint8_t *payload, size_t length);

// Free the PSRAM response buffer owned by voice_client. Safe
// to call even if the last upload failed.
void voice_release_last_response();

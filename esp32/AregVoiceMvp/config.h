// -------------------------------------------------------------
// AregVoiceMvp / config.h
//
// All compile-time constants for the C1 bench voice loop. Hardcoded
// on purpose — provisioning UX, config files, NVS storage, and OTA
// are explicitly out of scope for this slice.
//
// Fill in the four credential values before the first flash:
//   - WIFI_SSID / WIFI_PASSWORD — your dev Wi-Fi
//   - BACKEND_URL — your dev laptop's LAN address + port
//   - DEVICE_ID / DEVICE_API_KEY — returned from POST /api/devices/register
//
// Pin numbers below default to ESP32-S3-DevKitC-1 sensible choices.
// Adjust if your wiring differs.
// -------------------------------------------------------------
#pragma once

#include <Arduino.h>

// --- Wi-Fi credentials ---------------------------------------
#ifndef AREG_WIFI_SSID
#define AREG_WIFI_SSID          "OVIO_0114707"
#endif
#ifndef AREG_WIFI_PASSWORD
#define AREG_WIFI_PASSWORD      "Katrin2018"
#endif

// --- Backend endpoint ----------------------------------------
// Point this at your dev laptop on the same LAN. Plain HTTP is
// fine on a bench LAN; TLS is a later-phase concern.
#ifndef AREG_BACKEND_URL
#define AREG_BACKEND_URL        "http://192.168.1.8:5000/api/chat/audio"
#endif

// Continuous story narration (pre-rendered, streamed). The device
// streams this MP3 and decodes it on the fly; resume appends
// "?from=<byteOffset>". Same host/port as the backend above.
#ifndef AREG_STORY_AUDIO_URL
#define AREG_STORY_AUDIO_URL    "http://192.168.1.8:5000/api/story-audio/anban-huri"
#endif

// Library story id (matches the path in AREG_STORY_AUDIO_URL); sent to
// the Q&A endpoint so the backend answers from the right story.
#ifndef AREG_STORY_ID
#define AREG_STORY_ID           "anban-huri"
#endif

// In-story Q&A. On barge-in the device records the question and POSTs
// the WAV here as "?storyId=<id>&offset=<byteOffset>"; the response is
// the spoken answer MP3. Device-auth headers ARE sent on this POST.
#ifndef AREG_STORY_QA_URL
#define AREG_STORY_QA_URL       "http://192.168.1.8:5000/api/chat/story-qa"
#endif

// Resume-offset correction. getPos() reports bytes the decoder has
// CONSUMED, which runs ahead of what the speaker has actually played
// (decoded audio still buffered in the I2S DMA when barge-in cuts it).
// We resume from (getPos - this) so playback lands at — or slightly
// before — the audible pause point. Erring slightly toward overlap
// (re-hear a moment) is far better than skipping words. ~24 KB ≈ ~1 s
// at the narration bitrate. Tune here if resume skips (raise) or
// repeats too much (lower).
#ifndef AREG_STORY_RESUME_FUDGE_BYTES
#define AREG_STORY_RESUME_FUDGE_BYTES 8192
#endif

// --- Device credentials --------------------------------------
// Get these once via POST /api/devices/register against the backend.
// See README.md "First-run provisioning" for the curl invocation.
#ifndef AREG_DEVICE_ID
#define AREG_DEVICE_ID          "8E1B6F80-B189-4301-9C61-52D6630E254E"
#endif
#ifndef AREG_DEVICE_API_KEY
#define AREG_DEVICE_API_KEY     "dtk_demo_local_only_do_not_distribute"
#endif

// --- Pin map (ESP32-S3-DevKitC-1 defaults) -------------------
// INMP441 I2S mic (RX)
#define AREG_PIN_MIC_BCK        4      // SCK
#define AREG_PIN_MIC_WS         5      // WS / L-R
#define AREG_PIN_MIC_DATA       6      // SD
// MAX98357A I2S amp (TX)
#define AREG_PIN_AMP_BCK        15
#define AREG_PIN_AMP_LRC        16
#define AREG_PIN_AMP_DATA       7
// Button to GND, internal pullup
#define AREG_PIN_BUTTON         0      // BOOT button is fine for bench
// Onboard WS2812 RGB LED on S3-DevKitC-1
#define AREG_PIN_LED            48

// --- Audio parameters ----------------------------------------
#define AREG_SAMPLE_RATE_HZ     16000  // Whisper-friendly, bandwidth-friendly
#define AREG_SAMPLE_BITS        16     // linear PCM

// --- Hands-free library-story autoplay -----------------------
// After playing a library segment whose response carried
// X-Areg-Continue: 1, the device auto-fetches the next segment with
// no button press, looping until the backend returns 204 / no
// continue header. This cap is a safety stop so a backend bug can
// never spin the loop forever (the longest curated story is well
// under this).
#define AREG_MAX_AUTOPLAY_SEGMENTS 30

// --- Capture + playback limits -------------------------------
#define AREG_MAX_RECORD_MS      15000  // 15 s hard cap on button-hold
#define AREG_MIN_RECORD_MS      250    // below this, treat as misfire
#define AREG_RECORD_BUFFER_BYTES (AREG_SAMPLE_RATE_HZ * 2 * (AREG_MAX_RECORD_MS / 1000))
// 16 kHz * 2 bytes/sample * 15 s = 480 000 bytes. Lives in PSRAM.
#define AREG_PLAYBACK_BUFFER_BYTES (512 * 1024)  // 512 KB PSRAM headroom for MP3 response

// --- Timing --------------------------------------------------
#define AREG_BUTTON_POLL_MS     10
#define AREG_BUTTON_DEBOUNCE_MS 30
#define AREG_HTTP_CONNECT_MS    5000
#define AREG_HTTP_READ_MS       30000

// --- LED colors (GRB order for NeoPixel) ---------------------
#define AREG_LED_IDLE_R         8
#define AREG_LED_IDLE_G         16
#define AREG_LED_IDLE_B         64
#define AREG_LED_REC_R          180
#define AREG_LED_REC_G          0
#define AREG_LED_REC_B          0
#define AREG_LED_UPLOAD_R       180
#define AREG_LED_UPLOAD_G       120
#define AREG_LED_UPLOAD_B       0
#define AREG_LED_PLAY_R         0
#define AREG_LED_PLAY_G         160
#define AREG_LED_PLAY_B         40
#define AREG_LED_ERROR_R        200
#define AREG_LED_ERROR_G        60
#define AREG_LED_ERROR_B        0

// --- Serial --------------------------------------------------
#define AREG_SERIAL_BAUD        115200

// --- Dead-air mitigation (S1 earcon + S3 streamed Q&A) -------
// UNVERIFIED — not compiled/flashed. All values are best-guess
// for a 16 kHz / MAX98357A setup; tune on the bench.

// S1: Thinking-earcon synthesized tone.
//   Frequency of the soft "thinking" chime written directly to I2S.
//   440 Hz = A4; lower values (e.g. 330 Hz) feel warmer on a small speaker.
// HARDWARE ASSUMPTION: the MAX98357A accepts 16 kHz / 16-bit PCM from
//   AudioOutputI2S at the rate set by AREG_SAMPLE_RATE_HZ.
#ifndef AREG_EARCON_FREQ_HZ
#define AREG_EARCON_FREQ_HZ     440
#endif
// Duration of the synthesized earcon in milliseconds.
// Keep <= 800 ms so it doesn't drag; the child needs to hear it reacted
// before the server returns.
#ifndef AREG_EARCON_DURATION_MS
#define AREG_EARCON_DURATION_MS 600
#endif
// Amplitude of the synthesized sine (0..32767). Keep gentle (< 2000)
// so it doesn't startle and the child understands it as a soft cue.
// HARDWARE ASSUMPTION: amplitude below full scale avoids clipping on the
//   MAX98357A's default gain strapping. Adjust with the GAIN pin.
#ifndef AREG_EARCON_AMPLITUDE
#define AREG_EARCON_AMPLITUDE   1200
#endif

// S3: Thinking-bed tone played while network fetch is in flight.
//   Synthesized the same way as the earcon but at a lower pitch —
//   acts as a soft "hmm, still thinking" pulse.
// Frequency of the thinking-bed soft tone.
#ifndef AREG_THINKBED_FREQ_HZ
#define AREG_THINKBED_FREQ_HZ   280
#endif
// Duration of one thinking-bed pulse (ms). The loop plays N of these
// until the FreeRTOS upload task signals completion.
#ifndef AREG_THINKBED_PULSE_MS
#define AREG_THINKBED_PULSE_MS  500
#endif
// Amplitude of the thinking-bed tone (should be quieter than the earcon).
#ifndef AREG_THINKBED_AMPLITUDE
#define AREG_THINKBED_AMPLITUDE 700
#endif
// Maximum number of thinking-bed pulses before hard-stopping and waiting
// for the upload task to complete anyway. Guards against a hung network call
// lasting long enough to confuse the child (= AREG_HTTP_READ_MS / pulse ms +
// a few extra). At 500 ms/pulse and 30 s read timeout: 70 pulses = 35 s cap.
#ifndef AREG_THINKBED_MAX_PULSES
#define AREG_THINKBED_MAX_PULSES 70
#endif

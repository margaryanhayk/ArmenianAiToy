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
#define AREG_WIFI_SSID          "CHANGEME_SSID"
#endif
#ifndef AREG_WIFI_PASSWORD
#define AREG_WIFI_PASSWORD      "CHANGEME_PASSWORD"
#endif

// --- Backend endpoint ----------------------------------------
// Point this at your dev laptop on the same LAN. Plain HTTP is
// fine on a bench LAN; TLS is a later-phase concern.
#ifndef AREG_BACKEND_URL
#define AREG_BACKEND_URL        "http://192.168.1.100:5000/api/chat/audio"
#endif

// --- Device credentials --------------------------------------
// Get these once via POST /api/devices/register against the backend.
// See README.md "First-run provisioning" for the curl invocation.
#ifndef AREG_DEVICE_ID
#define AREG_DEVICE_ID          "00000000-0000-0000-0000-000000000000"
#endif
#ifndef AREG_DEVICE_API_KEY
#define AREG_DEVICE_API_KEY     "dtk_change_me"
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

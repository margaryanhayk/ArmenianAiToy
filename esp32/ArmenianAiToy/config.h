#ifndef CONFIG_H
#define CONFIG_H

// ========================
// Pin Definitions
// ========================

// INMP441 Microphone (I2S_NUM_0) — Phase 3
#define I2S_MIC_SCK   14   // Serial Clock (BCLK)
#define I2S_MIC_WS    15   // Word Select (LRCLK)
#define I2S_MIC_SD    32   // Serial Data (data out from mic)

// MAX98357A Amplifier (I2S_NUM_1) — Phase 4
#define I2S_SPK_BCLK  26   // Bit Clock
#define I2S_SPK_LRC   25   // Left/Right Clock
#define I2S_SPK_DIN   22   // Data In

// Status LED
#define LED_PIN        2    // Onboard LED

// ========================
// Audio Parameters (Phase 3-4)
// ========================
#define SAMPLE_RATE       16000
#define SAMPLE_BITS       16
#define RECORD_CHUNK_MS   2000  // Record in 2-second chunks to save RAM

// ========================
// Network
// ========================
#define AP_NAME_PREFIX    "AiToy-"
#define WEB_SERVER_PORT   80
#define HTTP_TIMEOUT_MS   30000  // 30 seconds for OpenAI responses

// ========================
// Backend
// ========================
#define BACKEND_URL_MAX_LEN  128

#endif // CONFIG_H

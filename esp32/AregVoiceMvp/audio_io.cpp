// -------------------------------------------------------------
// AregVoiceMvp / audio_io.cpp
//
// I2S capture + playback implementation.
//
// Mic capture uses the ESP-IDF v5 i2s_std driver (channel-handle
// API in <driver/i2s_std.h>). This is the supported API on
// ESP32 Arduino core 3.x and is the same family of API that
// ESP8266Audio's AudioOutputI2S targets, so capture and playback
// can coexist in one firmware without the historical
// legacy-vs-new I2S driver abort.
//
// Playback delegates MP3 decoding to ESP8266Audio; we do not
// ship a decoder ourselves.
// -------------------------------------------------------------
#include "audio_io.h"
#include "config.h"

#include <driver/i2s_std.h>
// AREG_DISABLE_MP3_PLAYBACK — bench rollback switch.
//
// Capture has been migrated to the new i2s_std driver, so the
// historical legacy-vs-new IDF conflict no longer applies and
// ESP8266Audio's AudioOutputI2S can be linked alongside without
// the boot-time abort. The macro is preserved as an instant
// rollback to the speaker-disabled bench mode in case the new
// playback path regresses on hardware — defining it strips
// every ESP8266Audio symbol from the binary and makes
// audio_play_mp3_buffer a logged no-op that the state machine
// treats as success. Capture + HTTP upload are unaffected by
// the macro either way.
#ifndef AREG_DISABLE_MP3_PLAYBACK
// ESP8266Audio exposes several AudioFileSource subclasses.
// `AudioFileSourcePROGMEM` despite its AVR-era name works
// identically against RAM and PSRAM on ESP32 — it uses
// `pgm_read_byte` which is a plain dereference outside the
// AVR family. This is the canonical way to decode an in-
// memory MP3 buffer on ESP32 with ESP8266Audio.
#include <AudioFileSourcePROGMEM.h>
#include <AudioGeneratorMP3.h>
#include <AudioOutputI2S.h>
#endif

// Use I2S port 0 for both capture and playback — we tear down
// and reconfigure between phases rather than running two ports
// in parallel. Simpler, avoids pin-driver conflicts on S3.
#define AREG_I2S_PORT           I2S_NUM_0

// Chunk size for each blocking i2s_channel_read. Tuned for
// responsive button-release handling without thrashing the
// driver.
static constexpr size_t kCaptureChunkSamples = 256;

// -------------------------------------------------------------
// Mic
// -------------------------------------------------------------

// Channel handle owned by audio_mic_begin/end. Null when no
// capture is active. The new i2s_std API replaces the legacy
// "port + global state" model with explicit per-channel handles.
static i2s_chan_handle_t s_mic_chan = nullptr;

bool audio_mic_begin() {
    if (s_mic_chan != nullptr) {
        Serial.println("[audio] mic begin: channel already live");
        return false;
    }

    i2s_chan_config_t chan_cfg =
        I2S_CHANNEL_DEFAULT_CONFIG(AREG_I2S_PORT, I2S_ROLE_MASTER);
    // Match the legacy capture's DMA shape: 4 descriptors of 1024
    // 32-bit-mono frames each (legacy dma_buf_count=4,
    // dma_buf_len=1024). One frame at 32-bit mono is 4 bytes, so
    // total DMA residency is 4 × 4096 = 16 KB, same as before.
    chan_cfg.dma_desc_num  = 4;
    chan_cfg.dma_frame_num = 1024;

    if (i2s_new_channel(&chan_cfg, nullptr, &s_mic_chan) != ESP_OK) {
        Serial.println("[audio] mic new_channel failed");
        s_mic_chan = nullptr;
        return false;
    }

    // INMP441 outputs 24-bit data in a 32-bit slot. We read the
    // 32-bit slot and right-shift to 16-bit below. L/R is tied
    // to GND on the bench board, which places the mic on the
    // LEFT slot — make that explicit instead of relying on the
    // mono-default's chip-specific behaviour.
    i2s_std_config_t std_cfg = {
        .clk_cfg = I2S_STD_CLK_DEFAULT_CONFIG(AREG_SAMPLE_RATE_HZ),
        .slot_cfg = I2S_STD_PHILIPS_SLOT_DEFAULT_CONFIG(
            I2S_DATA_BIT_WIDTH_32BIT, I2S_SLOT_MODE_MONO),
        .gpio_cfg = {
            .mclk = I2S_GPIO_UNUSED,
            .bclk = (gpio_num_t)AREG_PIN_MIC_BCK,
            .ws   = (gpio_num_t)AREG_PIN_MIC_WS,
            .dout = I2S_GPIO_UNUSED,
            .din  = (gpio_num_t)AREG_PIN_MIC_DATA,
            .invert_flags = {
                .mclk_inv = false,
                .bclk_inv = false,
                .ws_inv   = false,
            },
        },
    };
    std_cfg.slot_cfg.slot_mask = I2S_STD_SLOT_LEFT;

    if (i2s_channel_init_std_mode(s_mic_chan, &std_cfg) != ESP_OK) {
        Serial.println("[audio] mic init_std_mode failed");
        i2s_del_channel(s_mic_chan);
        s_mic_chan = nullptr;
        return false;
    }
    if (i2s_channel_enable(s_mic_chan) != ESP_OK) {
        Serial.println("[audio] mic channel_enable failed");
        i2s_del_channel(s_mic_chan);
        s_mic_chan = nullptr;
        return false;
    }
    return true;
}

void audio_mic_end() {
    if (s_mic_chan == nullptr) {
        return;
    }
    i2s_channel_disable(s_mic_chan);
    i2s_del_channel(s_mic_chan);
    s_mic_chan = nullptr;
}

size_t audio_mic_capture(int16_t *out_buffer,
                         size_t max_samples,
                         uint32_t max_duration_ms,
                         audio_should_stop_fn should_stop) {
    if (s_mic_chan == nullptr) {
        return 0;
    }
    // Temporary 32-bit read buffer; INMP441 delivers 32-bit
    // slots that we narrow to 16-bit by right-shifting 14 bits
    // (INMP441 data lives in the upper 18 bits; 14 keeps it
    // comfortably within 16-bit range for Whisper).
    int32_t tmp[kCaptureChunkSamples];
    size_t total_samples = 0;
    uint32_t started_at = millis();

    while (total_samples < max_samples) {
        if (max_duration_ms > 0 && (millis() - started_at) >= max_duration_ms) {
            break;
        }
        if (should_stop && should_stop()) {
            break;
        }
        size_t bytes_read = 0;
        // 10 ms per blocking call keeps the should_stop() poll
        // responsive on button-release. Unlike the legacy i2s_read
        // (which returned ESP_OK with bytes_read=0 on a window
        // with no data), i2s_channel_read returns ESP_ERR_TIMEOUT.
        // Treat that as "no data this tick, keep polling" so the
        // loop's release/duration checks still run.
        esp_err_t err = i2s_channel_read(s_mic_chan, tmp, sizeof(tmp),
                                         &bytes_read, pdMS_TO_TICKS(10));
        if (err == ESP_ERR_TIMEOUT) {
            continue;
        }
        if (err != ESP_OK) {
            break;
        }
        size_t samples_read = bytes_read / sizeof(int32_t);
        if (samples_read == 0) {
            continue;
        }
        size_t headroom = max_samples - total_samples;
        if (samples_read > headroom) {
            samples_read = headroom;
        }
        for (size_t i = 0; i < samples_read; ++i) {
            // Narrow INMP441 24-bit-in-32 to 16-bit signed.
            out_buffer[total_samples + i] = (int16_t)(tmp[i] >> 14);
        }
        total_samples += samples_read;
    }
    return total_samples;
}

// -------------------------------------------------------------
// Speaker
// -------------------------------------------------------------
// AudioOutputI2S from ESP8266Audio configures the I2S peripheral
// itself — we just hand it the pin numbers and sample rate. It
// uses i2s_driver_install internally, which is why we
// audio_mic_end() before calling audio_speaker_begin().

bool audio_speaker_begin() {
    // Defer the actual AudioOutputI2S object to play-time so
    // the lifecycle is bounded to a single playback call. Nothing
    // to do here pre-play. Kept as a seam so future phases can
    // pre-configure without touching callers.
    return true;
}

bool audio_play_mp3_buffer(const uint8_t *data, size_t length) {
    if (data == nullptr || length == 0) {
        return false;
    }
#ifdef AREG_DISABLE_MP3_PLAYBACK
    (void)data;
    (void)length;
    // Return true so the state machine treats the no-op as a
    // successful playback and routes back to IDLE instead of
    // ERROR. We already validated upstream (HTTP 200 + MP3 body)
    // by the time we got here; "didn't play through the speaker"
    // is the expected bench behavior, not a decode failure.
    Serial.println("[audio] speaker playback disabled for bench I2S conflict isolation; treating as success");
    return true;
#else
    AudioFileSourcePROGMEM source((const void *)data, (uint32_t)length);
    AudioOutputI2S out;
    out.SetPinout(AREG_PIN_AMP_BCK, AREG_PIN_AMP_LRC, AREG_PIN_AMP_DATA);
    // Output gain is conservative — raise in config.h later if
    // needed. 0.0f .. ~4.0f; 1.0f is unity.
    out.SetGain(0.6f);

    AudioGeneratorMP3 mp3;
    if (!mp3.begin(&source, &out)) {
        Serial.println("[audio] mp3.begin failed");
        return false;
    }
    uint32_t last_watchdog_tickle = millis();
    while (mp3.isRunning()) {
        if (!mp3.loop()) {
            mp3.stop();
            break;
        }
        // Yield to FreeRTOS occasionally so Wi-Fi housekeeping
        // and watchdog resets are not starved during long reads.
        if (millis() - last_watchdog_tickle > 50) {
            delay(1);
            last_watchdog_tickle = millis();
        }
    }
    out.stop();
    return true;
#endif
}

// -------------------------------------------------------------
// WAV header
// -------------------------------------------------------------

static void write_u32_le(uint8_t *dst, uint32_t v) {
    dst[0] = (uint8_t)(v & 0xFF);
    dst[1] = (uint8_t)((v >> 8) & 0xFF);
    dst[2] = (uint8_t)((v >> 16) & 0xFF);
    dst[3] = (uint8_t)((v >> 24) & 0xFF);
}
static void write_u16_le(uint8_t *dst, uint16_t v) {
    dst[0] = (uint8_t)(v & 0xFF);
    dst[1] = (uint8_t)((v >> 8) & 0xFF);
}

void audio_write_wav_header(uint8_t *hdr_out, uint32_t pcm_sample_count) {
    const uint32_t data_bytes = pcm_sample_count * 2; // mono 16-bit
    const uint32_t file_size_minus_8 = 36 + data_bytes;
    const uint32_t byte_rate = AREG_SAMPLE_RATE_HZ * 1 * (AREG_SAMPLE_BITS / 8);
    const uint16_t block_align = 1 * (AREG_SAMPLE_BITS / 8);

    memcpy(hdr_out + 0, "RIFF", 4);
    write_u32_le(hdr_out + 4, file_size_minus_8);
    memcpy(hdr_out + 8, "WAVE", 4);
    memcpy(hdr_out + 12, "fmt ", 4);
    write_u32_le(hdr_out + 16, 16);               // fmt chunk size (PCM)
    write_u16_le(hdr_out + 20, 1);                // audio format (PCM)
    write_u16_le(hdr_out + 22, 1);                // num channels
    write_u32_le(hdr_out + 24, AREG_SAMPLE_RATE_HZ);
    write_u32_le(hdr_out + 28, byte_rate);
    write_u16_le(hdr_out + 32, block_align);
    write_u16_le(hdr_out + 34, AREG_SAMPLE_BITS);
    memcpy(hdr_out + 36, "data", 4);
    write_u32_le(hdr_out + 40, data_bytes);
}

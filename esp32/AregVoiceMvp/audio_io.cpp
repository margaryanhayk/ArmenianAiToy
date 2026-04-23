// -------------------------------------------------------------
// AregVoiceMvp / audio_io.cpp
//
// I2S capture + playback implementation. Uses the legacy
// driver/i2s.h API because it is stable across ESP32 Arduino
// cores 2.x and 3.x, well documented, and sufficient for C1.
//
// Playback delegates MP3 decoding to ESP8266Audio; we do not
// ship a decoder ourselves.
// -------------------------------------------------------------
#include "audio_io.h"
#include "config.h"

#include <driver/i2s.h>
// ESP8266Audio exposes several AudioFileSource subclasses.
// `AudioFileSourcePROGMEM` despite its AVR-era name works
// identically against RAM and PSRAM on ESP32 — it uses
// `pgm_read_byte` which is a plain dereference outside the
// AVR family. This is the canonical way to decode an in-
// memory MP3 buffer on ESP32 with ESP8266Audio.
#include <AudioFileSourcePROGMEM.h>
#include <AudioGeneratorMP3.h>
#include <AudioOutputI2S.h>

// Use I2S port 0 for both capture and playback — we tear down
// and reconfigure between phases rather than running two ports
// in parallel. Simpler, avoids pin-driver conflicts on S3.
#define AREG_I2S_PORT           I2S_NUM_0

// Chunk size for each blocking i2s_read. Tuned for responsive
// button-release handling without thrashing the driver.
static constexpr size_t kCaptureChunkSamples = 256;

// -------------------------------------------------------------
// Mic
// -------------------------------------------------------------

bool audio_mic_begin() {
    i2s_config_t cfg = {};
    cfg.mode = (i2s_mode_t)(I2S_MODE_MASTER | I2S_MODE_RX);
    cfg.sample_rate = AREG_SAMPLE_RATE_HZ;
    // INMP441 outputs 24-bit data in a 32-bit slot; we read 32-bit
    // and right-shift to 16-bit below.
    cfg.bits_per_sample = I2S_BITS_PER_SAMPLE_32BIT;
    cfg.channel_format = I2S_CHANNEL_FMT_ONLY_LEFT;
    cfg.communication_format = I2S_COMM_FORMAT_STAND_I2S;
    cfg.intr_alloc_flags = ESP_INTR_FLAG_LEVEL1;
    cfg.dma_buf_count = 4;
    cfg.dma_buf_len = 1024;
    cfg.use_apll = false;
    cfg.tx_desc_auto_clear = false;
    cfg.fixed_mclk = 0;

    if (i2s_driver_install(AREG_I2S_PORT, &cfg, 0, nullptr) != ESP_OK) {
        Serial.println("[audio] mic driver_install failed");
        return false;
    }
    i2s_pin_config_t pins = {};
    pins.bck_io_num = AREG_PIN_MIC_BCK;
    pins.ws_io_num = AREG_PIN_MIC_WS;
    pins.data_out_num = I2S_PIN_NO_CHANGE;
    pins.data_in_num = AREG_PIN_MIC_DATA;
    if (i2s_set_pin(AREG_I2S_PORT, &pins) != ESP_OK) {
        Serial.println("[audio] mic set_pin failed");
        i2s_driver_uninstall(AREG_I2S_PORT);
        return false;
    }
    i2s_zero_dma_buffer(AREG_I2S_PORT);
    return true;
}

void audio_mic_end() {
    i2s_driver_uninstall(AREG_I2S_PORT);
}

size_t audio_mic_capture(int16_t *out_buffer,
                         size_t max_samples,
                         uint32_t max_duration_ms,
                         audio_should_stop_fn should_stop) {
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
        // responsive on button-release.
        if (i2s_read(AREG_I2S_PORT, tmp, sizeof(tmp), &bytes_read,
                     pdMS_TO_TICKS(10)) != ESP_OK) {
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

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
#include "diag.h"

#include <driver/i2s_std.h>
#include <esp_err.h>
#include <math.h>   // sinf() for S1 earcon tone synthesis (UNVERIFIED)
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
#include <AudioFileSourceHTTPStream.h>
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
    DIAG_MARK(1000, "mic_begin_enter");
    Serial.println("[audio] record_begin");
    Serial.flush();
    if (s_mic_chan != nullptr) {
        DIAG_MARK(1001, "mic_begin_channel_already_live");
        Serial.println("[audio] mic begin: channel already live");
        Serial.flush();
        return false;
    }

    i2s_chan_config_t chan_cfg =
        I2S_CHANNEL_DEFAULT_CONFIG(AREG_I2S_PORT, I2S_ROLE_MASTER);
    // Bring-up stability experiment: smaller RX DMA buffers reduce
    // DMA/interrupt pressure during i2s_channel_enable on ESP32-S3.
    // (Was 4×1024 = 16 KB DMA residency; matched the legacy capture
    // shape. Lower values let the channel come up with much smaller
    // contiguous DMA descriptors — if that survives, the original
    // values were starving the I2S driver's allocation path or
    // tripping an interrupt-context issue at enable time.)
    chan_cfg.dma_desc_num  = 2;
    chan_cfg.dma_frame_num = 256;

    DIAG_MARK(1010, "mic_new_channel_before");
    esp_err_t err_new = i2s_new_channel(&chan_cfg, nullptr, &s_mic_chan);
    Serial.printf("[audio] i2s_new_channel err=%d/%s\n",
                  (int)err_new, esp_err_to_name(err_new));
    Serial.flush();
    if (err_new != ESP_OK) {
        DIAG_MARK(1011, "mic_new_channel_after_fail");
        Serial.println("[audio] mic new_channel failed");
        Serial.flush();
        s_mic_chan = nullptr;
        return false;
    }
    DIAG_MARK(1012, "mic_new_channel_after_ok");

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

    DIAG_MARK(1020, "mic_init_std_before");
    esp_err_t err_init = i2s_channel_init_std_mode(s_mic_chan, &std_cfg);
    Serial.printf("[audio] i2s_channel_init_std_mode err=%d/%s\n",
                  (int)err_init, esp_err_to_name(err_init));
    Serial.flush();
    if (err_init != ESP_OK) {
        DIAG_MARK(1021, "mic_init_std_after_fail");
        Serial.println("[audio] mic init_std_mode failed");
        Serial.flush();
        i2s_del_channel(s_mic_chan);
        s_mic_chan = nullptr;
        return false;
    }
    DIAG_MARK(1022, "mic_init_std_after_ok");

    DIAG_MARK(1030, "mic_enable_before");
    esp_err_t err_en = i2s_channel_enable(s_mic_chan);
    DIAG_MARK(1033, "mic_enable_returned");
    Serial.printf("[audio] i2s_channel_enable err=%d/%s\n",
                  (int)err_en, esp_err_to_name(err_en));
    Serial.flush();
    if (err_en != ESP_OK) {
        DIAG_MARK(1031, "mic_enable_after_fail");
        Serial.println("[audio] mic channel_enable failed");
        Serial.flush();
        i2s_del_channel(s_mic_chan);
        s_mic_chan = nullptr;
        return false;
    }
    DIAG_MARK(1032, "mic_enable_after_ok");

    DIAG_MARK(1099, "mic_begin_exit_ok");
    return true;
}

void audio_mic_end() {
    DIAG_MARK(3000, "mic_teardown_enter");
    if (s_mic_chan == nullptr) {
        DIAG_MARK(3001, "mic_teardown_no_channel");
        return;
    }
    DIAG_MARK(3010, "mic_teardown_before_disable");
    i2s_channel_disable(s_mic_chan);
    DIAG_MARK(3011, "mic_teardown_after_disable");
    DIAG_MARK(3020, "mic_teardown_before_del");
    i2s_del_channel(s_mic_chan);
    DIAG_MARK(3021, "mic_teardown_after_del");
    s_mic_chan = nullptr;
    DIAG_MARK(3099, "mic_teardown_exit");
}

size_t audio_mic_capture(int16_t *out_buffer,
                         size_t max_samples,
                         uint32_t max_duration_ms,
                         audio_should_stop_fn should_stop) {
    DIAG_MARK(2000, "mic_capture_enter");
    if (s_mic_chan == nullptr) {
        DIAG_MARK(2001, "mic_capture_no_channel");
        return 0;
    }
    // Temporary 32-bit read buffer; INMP441 delivers 32-bit
    // slots that we narrow to 16-bit by right-shifting 14 bits
    // (INMP441 data lives in the upper 18 bits; 14 keeps it
    // comfortably within 16-bit range for Whisper).
    int32_t tmp[kCaptureChunkSamples];
    size_t total_samples = 0;
    uint32_t started_at = millis();
    bool first_read_done = false;
    uint32_t last_progress_ms = started_at;

    while (total_samples < max_samples) {
        if (max_duration_ms > 0 && (millis() - started_at) >= max_duration_ms) {
            break;
        }
        if (should_stop && should_stop()) {
            break;
        }
        size_t bytes_read = 0;
        if (!first_read_done) {
            DIAG_MARK(2010, "mic_read_first_before");
        }
        // 10 ms per blocking call keeps the should_stop() poll
        // responsive on button-release. Unlike the legacy i2s_read
        // (which returned ESP_OK with bytes_read=0 on a window
        // with no data), i2s_channel_read returns ESP_ERR_TIMEOUT.
        // Treat that as "no data this tick, keep polling" so the
        // loop's release/duration checks still run.
        esp_err_t err = i2s_channel_read(s_mic_chan, tmp, sizeof(tmp),
                                         &bytes_read, pdMS_TO_TICKS(10));
        if (!first_read_done && err == ESP_OK) {
            DIAG_MARK(2011, "mic_read_first_after_ok");
            first_read_done = true;
        }
        if (err == ESP_ERR_TIMEOUT) {
            continue;
        }
        if (err != ESP_OK) {
            DIAG_MARK(2012, "mic_read_error");
            Serial.printf("[audio] i2s_channel_read err=%d/%s\n",
                          (int)err, esp_err_to_name(err));
            Serial.flush();
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

        // 1 Hz capture progress — surfaces stalls without spam.
        const uint32_t now = millis();
        if (now - last_progress_ms >= 1000) {
            last_progress_ms = now;
            Serial.printf(
                "[audio] record_progress samples=%u bytes=%u heap=%u psram=%u\n",
                (unsigned)total_samples,
                (unsigned)(total_samples * sizeof(int16_t)),
                (unsigned)ESP.getFreeHeap(),
                (unsigned)ESP.getFreePsram());
            Serial.flush();
        }
    }
    Serial.printf("[audio] record_end bytes=%u\n",
                  (unsigned)(total_samples * sizeof(int16_t)));
    Serial.flush();
    DIAG_MARK(2099, "mic_capture_exit");
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
    DIAG_MARK(4000, "play_enter");
    Serial.printf("[audio] play_begin bytes=%u\n", (unsigned)length);
    Serial.flush();
    if (data == nullptr || length == 0) {
        DIAG_MARK(4001, "play_empty_buffer");
        Serial.println("[audio] play_end ok=false");
        Serial.flush();
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
    Serial.println("[audio] play_end ok=true");
    Serial.flush();
    DIAG_MARK(4002, "play_exit_disabled");
    return true;
#else
    DIAG_MARK(4010, "play_audio_out_setup_before");
    AudioFileSourcePROGMEM source((const void *)data, (uint32_t)length);
    AudioOutputI2S out;
    out.SetPinout(AREG_PIN_AMP_BCK, AREG_PIN_AMP_LRC, AREG_PIN_AMP_DATA);
    // Output gain is conservative — raise in config.h later if
    // needed. 0.0f .. ~4.0f; 1.0f is unity.
    out.SetGain(0.6f);
    DIAG_MARK(4011, "play_audio_out_setup_after");

    AudioGeneratorMP3 mp3;
    DIAG_MARK(4020, "play_mp3_begin_before");
    if (!mp3.begin(&source, &out)) {
        DIAG_MARK(4021, "play_mp3_begin_fail");
        Serial.println("[audio] mp3.begin failed");
        Serial.println("[audio] play_end ok=false");
        Serial.flush();
        return false;
    }
    DIAG_MARK(4022, "play_mp3_loop_enter");
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
    DIAG_MARK(4030, "play_mp3_loop_exit");
    out.stop();
    Serial.println("[audio] play_end ok=true");
    Serial.flush();
    DIAG_MARK(4099, "play_exit_ok");
    return true;
#endif
}

// -------------------------------------------------------------
// Story stream (continuous, interruptible)
// -------------------------------------------------------------

bool audio_play_story_stream(const char *url,
                             uint32_t base_offset,
                             audio_barge_in_fn barge_in,
                             uint32_t *out_resume_offset) {
    if (out_resume_offset != nullptr) {
        *out_resume_offset = 0;
    }
#ifdef AREG_DISABLE_MP3_PLAYBACK
    (void)url; (void)base_offset; (void)barge_in;
    Serial.println("[story] playback disabled (AREG_DISABLE_MP3_PLAYBACK)");
    Serial.flush();
    return false;
#else
    Serial.printf("[story] stream open: %s\n", url);
    Serial.flush();

    AudioFileSourceHTTPStream http(url);
    if (!http.isOpen()) {
        Serial.println("[story] http stream open failed");
        Serial.flush();
        return false;  // nothing to resume
    }

    AudioOutputI2S out;
    out.SetPinout(AREG_PIN_AMP_BCK, AREG_PIN_AMP_LRC, AREG_PIN_AMP_DATA);
    out.SetGain(0.6f);

    AudioGeneratorMP3 mp3;
    if (!mp3.begin(&http, &out)) {
        Serial.println("[story] mp3.begin (stream) failed");
        Serial.flush();
        return false;
    }

    bool interrupted = false;
    uint32_t last_yield = millis();
    while (mp3.isRunning()) {
        // True barge-in: poll the button DURING decode and cut
        // instantly on a press.
        if (barge_in != nullptr && barge_in()) {
            // getPos() counts from 0 of THIS (partial) response, so the
            // absolute file position is base_offset + getPos().
            uint32_t abs_pos = base_offset + (uint32_t)http.getPos();
            // Back up past the decoded-but-unplayed I2S-buffered tail so
            // resume lands at the audible pause point (overlap, not skip).
            *out_resume_offset = (abs_pos > AREG_STORY_RESUME_FUDGE_BYTES)
                ? (abs_pos - AREG_STORY_RESUME_FUDGE_BYTES) : 0;
            mp3.stop();
            interrupted = true;
            Serial.printf("[story] barge-in: base=%u rel_pos=%u abs=%u resume_from=%u\n",
                          (unsigned)base_offset, (unsigned)http.getPos(),
                          (unsigned)abs_pos, (unsigned)*out_resume_offset);
            Serial.flush();
            break;
        }
        if (!mp3.loop()) {
            mp3.stop();
            break;
        }
        // Yield to FreeRTOS so Wi-Fi housekeeping / watchdog are not
        // starved during the long continuous stream.
        if (millis() - last_yield > 50) {
            delay(1);
            last_yield = millis();
        }
    }
    out.stop();
    Serial.printf("[story] stream end interrupted=%s\n",
                  interrupted ? "true" : "false");
    Serial.flush();
    return interrupted;
#endif
}

// -------------------------------------------------------------
// Dead-air mitigation — S1 (earcon) + S3 (Q&A stream)
// UNVERIFIED — not compiled/flashed. See HARDENING-INTEGRATION.md §2.
// -------------------------------------------------------------

// ---- Shared helper: synthesize and write a soft sine tone ----
//
// Writes a pure sine at `freq_hz` for `duration_ms` milliseconds directly
// to an already-opened AudioOutputI2S. The tone is generated sample-by-
// sample into a small stack buffer and pushed via out.ConsumeSample().
//
// HARDWARE ASSUMPTION: AudioOutputI2S is already begin()-ed and configured
// at AREG_SAMPLE_RATE_HZ. ConsumeSample() is the ESP8266Audio sample-push
// API — it takes a pair of 16-bit values (left, right, packed as int16_t[2])
// and returns false when the I2S DMA buffer is full (back-pressure signal).
// When it returns false we yield briefly with delay(1) and retry.
//
// The envelope ramps the amplitude up for the first 50 ms and down for the
// last 50 ms (linear fade) to avoid a click at start/end.
#ifndef AREG_DISABLE_MP3_PLAYBACK
static void synth_write_tone(AudioOutputI2S &out,
                             uint16_t freq_hz,
                             uint32_t duration_ms,
                             int16_t  amplitude) {
    // HARDWARE ASSUMPTION: AREG_SAMPLE_RATE_HZ is 16000. If it is changed,
    // this function adapts automatically via the constant.
    const uint32_t total_samples =
        (uint32_t)AREG_SAMPLE_RATE_HZ * duration_ms / 1000;
    const uint32_t fade_samples  = (uint32_t)AREG_SAMPLE_RATE_HZ * 50 / 1000; // 50 ms fade
    // Phase accumulator: integer steps of (freq_hz / sample_rate) in
    // units of 1/65536 of a cycle. Stays exact over the call's lifetime.
    uint32_t phase     = 0;
    uint32_t phase_inc = ((uint32_t)freq_hz << 16) / AREG_SAMPLE_RATE_HZ;

    for (uint32_t i = 0; i < total_samples; ++i) {
        // Map the 32-bit phase accumulator (0..0xFFFFFFFF) onto 0..2π and
        // compute sinf(). The Xtensa LX7 has hardware FPU — this is fast.
        // HARDWARE ASSUMPTION: ESP32-S3 Xtensa LX7 FPU handles sinf in ~10 cycles.
        float angle = ((float)(phase) / (float)0xFFFFFFFFu) * (2.0f * 3.14159265f);
        int16_t raw = (int16_t)(sinf(angle) * (float)amplitude);

        // Linear fade envelope.
        if (i < fade_samples) {
            raw = (int16_t)((int32_t)raw * (int32_t)i / (int32_t)fade_samples);
        } else if (i > total_samples - fade_samples) {
            uint32_t tail = total_samples - i;
            raw = (int16_t)((int32_t)raw * (int32_t)tail / (int32_t)fade_samples);
        }

        // ConsumeSample expects AudioOutput::AudioType (int16_t[2] packed
        // as a uint32_t on some versions, or two separate calls — the
        // public API is ConsumeSample(int16_t lr[2])). Use the two-element
        // array form which is consistent across ESP8266Audio versions.
        // HARDWARE ASSUMPTION: mono signal — copy left to right.
        int16_t lr[2] = { raw, raw };
        // Back-pressure: if DMA buffers are full, yield and retry.
        while (!out.ConsumeSample(lr)) {
            delay(1);
        }
        phase += phase_inc;
    }
}
#endif  // AREG_DISABLE_MP3_PLAYBACK

// ---- S1: audio_play_thinking_earcon() -----------------------
bool audio_play_thinking_earcon() {
    Serial.println("[audio] earcon_begin");
    Serial.flush();
#ifdef AREG_DISABLE_MP3_PLAYBACK
    // Playback disabled for bench I2S isolation — treat as success
    // (the important thing is we didn't add silence; earcon is optional).
    Serial.println("[audio] earcon: playback disabled, skipping");
    Serial.flush();
    return true;
#else
    // HARDWARE ASSUMPTION: audio_speaker_begin() was called before this.
    // AudioOutputI2S is constructed fresh each call so it does its own
    // I2S peripheral init, matching the pattern in audio_play_mp3_buffer.
    AudioOutputI2S out;
    out.SetPinout(AREG_PIN_AMP_BCK, AREG_PIN_AMP_LRC, AREG_PIN_AMP_DATA);
    // HARDWARE ASSUMPTION: OutputMode is INTERNAL_DAC=0, I2S=1. The
    // default constructor on ESP8266Audio AudioOutputI2S uses I2S mode.
    // HARDWARE ASSUMPTION: SetBitsPerSample(16) is the default; not
    // calling it explicitly here to match audio_play_mp3_buffer style.
    out.SetGain(0.6f);
    // The synth path MUST set the I2S sample rate. The MP3 path gets it from
    // the decoder (mp3.begin → out.SetRate); without it here the channel runs
    // at ESP8266Audio's default 44.1 kHz, so the 16 kHz-generated tone is
    // clocked ~2.75x too fast — mis-paced and inaudible — and synth_write_tone
    // returns almost instantly (which is why the thinking-bed loop spins,
    // spamming earcon_begin/earcon_end). Setting the rate fixes both.
    out.SetRate(AREG_SAMPLE_RATE_HZ);
    if (!out.begin()) {
        Serial.println("[audio] earcon: out.begin() failed");
        Serial.flush();
        return false;
    }

    synth_write_tone(out,
                     AREG_EARCON_FREQ_HZ,
                     AREG_EARCON_DURATION_MS,
                     AREG_EARCON_AMPLITUDE);

    out.stop();
    Serial.println("[audio] earcon_end");
    Serial.flush();
    return true;
#endif
}

// ---- S3: audio_play_qa_stream() -----------------------------
//
// Streams the Q&A answer MP3 from a URL incrementally, playing audio
// as bytes arrive. Reuses the exact same ESP8266Audio HTTP path used by
// audio_play_story_stream but without barge-in / resume machinery.
//
// HARDWARE ASSUMPTION: the ESP8266Audio AudioFileSourceHTTPStream is
// capable of streaming from the backend's chunked HTTP/1.1 response.
// The transfer-encoding=chunked decoding happens inside HTTPClient /
// Arduino WiFiClient; the AudioFileSourceHTTPStream layer just reads
// bytes and the MP3 decoder consumes them incrementally.
bool audio_play_qa_stream(const char *url) {
    Serial.printf("[audio] qa_stream_begin url=%s\n", url);
    Serial.flush();
#ifdef AREG_DISABLE_MP3_PLAYBACK
    (void)url;
    Serial.println("[audio] qa_stream: playback disabled, skipping");
    Serial.flush();
    return false;  // false = caller should try buffered fallback (none here)
#else
    // HARDWARE ASSUMPTION: audio_speaker_begin() was already called.
    // AudioFileSourceHTTPStream opens a TCP connection and begins the GET.
    // On a first-response latency of e.g. 300 ms the mp3.loop() decode
    // loop below will block briefly until the server sends the first MP3
    // sync word — this is fine; the decoder handles streaming natively.
    AudioFileSourceHTTPStream http(url);
    if (!http.isOpen()) {
        Serial.println("[audio] qa_stream: http open failed; caller may use buffered fallback");
        Serial.flush();
        return false;
    }

    AudioOutputI2S out;
    out.SetPinout(AREG_PIN_AMP_BCK, AREG_PIN_AMP_LRC, AREG_PIN_AMP_DATA);
    out.SetGain(0.6f);

    AudioGeneratorMP3 mp3;
    if (!mp3.begin(&http, &out)) {
        Serial.println("[audio] qa_stream: mp3.begin failed; caller may use buffered fallback");
        Serial.flush();
        return false;
    }

    uint32_t last_yield = millis();
    while (mp3.isRunning()) {
        if (!mp3.loop()) {
            mp3.stop();
            break;
        }
        // Yield to FreeRTOS so Wi-Fi housekeeping / watchdog are not
        // starved. Same 50 ms window as audio_play_story_stream.
        if (millis() - last_yield > 50) {
            delay(1);
            last_yield = millis();
        }
    }
    out.stop();
    Serial.println("[audio] qa_stream_end ok=true");
    Serial.flush();
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

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
#include "volume_pot.h"   // hardware volume knob; folds to a fixed 0.6f when no pot pin is defined

#include <driver/i2s_std.h>
#include <esp_err.h>
#include <esp_task_wdt.h>   // #047 â€” feed the task watchdog from the long decode loops
#include <math.h>   // sinf() for S1 earcon tone synthesis (UNVERIFIED)

// #047 â€” per-sample cap on synth_write_tone's I2S back-pressure wait. Defaulted
// here so the build never depends on config.h carrying it; overridable there.
#ifndef AREG_I2S_CONSUME_TIMEOUT_MS
#define AREG_I2S_CONSUME_TIMEOUT_MS  1000
#endif
// AREG_DISABLE_MP3_PLAYBACK â€” bench rollback switch.
//
// Capture has been migrated to the new i2s_std driver, so the
// historical legacy-vs-new IDF conflict no longer applies and
// ESP8266Audio's AudioOutputI2S can be linked alongside without
// the boot-time abort. The macro is preserved as an instant
// rollback to the speaker-disabled bench mode in case the new
// playback path regresses on hardware â€” defining it strips
// every ESP8266Audio symbol from the binary and makes
// audio_play_mp3_buffer a logged no-op that the state machine
// treats as success. Capture + HTTP upload are unaffected by
// the macro either way.
#ifndef AREG_DISABLE_MP3_PLAYBACK
// ESP8266Audio exposes several AudioFileSource subclasses.
// `AudioFileSourcePROGMEM` despite its AVR-era name works
// identically against RAM and PSRAM on ESP32 â€” it uses
// `pgm_read_byte` which is a plain dereference outside the
// AVR family. This is the canonical way to decode an in-
// memory MP3 buffer on ESP32 with ESP8266Audio.
#include <AudioFileSourcePROGMEM.h>
#include <AudioFileSourceHTTPStream.h>
#include <AudioGeneratorMP3.h>
#include <AudioOutputI2S.h>
// Slice 2 (offline content pack): decode narration straight off the SD card.
#include <AudioFileSourceSD.h>
#include <SPI.h>
#include <SD.h>
#endif

// Use I2S port 0 for both capture and playback â€” we tear down
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
    // (Was 4Ã—1024 = 16 KB DMA residency; matched the legacy capture
    // shape. Lower values let the channel come up with much smaller
    // contiguous DMA descriptors â€” if that survives, the original
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
    // LEFT slot â€” make that explicit instead of relying on the
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

        // 1 Hz capture progress â€” surfaces stalls without spam.
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
// itself â€” we just hand it the pin numbers and sample rate. It
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
    // Output gain now comes from the volume knob. 0.0f .. ~4.0f; 1.0f is
    // unity. With no pot pin defined this is the same fixed 0.6f that was
    // hardcoded here before, so a knobless build is unchanged.
    out.SetGain(volume_pot_gain());
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
        esp_task_wdt_reset();  // #047 â€” feed the WDT each decode iteration
        if (!mp3.loop()) {
            mp3.stop();
            break;
        }
        // Yield to FreeRTOS occasionally so Wi-Fi housekeeping
        // and watchdog resets are not starved during long reads.
        if (millis() - last_watchdog_tickle > 50) {
            delay(1);
            last_watchdog_tickle = millis();
            // A child turns the knob WHILE something is playing â€” which is
            // exactly when loop()'s IDLE branch cannot run, since we are
            // blocked in here for the whole clip. That is the only reason
            // this lives on the decode hot path. It rides the existing yield
            // throttle rather than adding a timer, and volume_pot_tick() is
            // itself rate-limited, so the ADC is read a few times a second
            // and SetGain only touches a member when the knob really moved.
            if (volume_pot_tick_playing()) out.SetGain(volume_pot_gain());
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
                             uint32_t *out_resume_offset,
                             bool *out_open_failed) {
    if (out_resume_offset != nullptr) {
        *out_resume_offset = 0;
    }
    if (out_open_failed != nullptr) {
        *out_open_failed = false;
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
        // #063 â€” a non-200 GET (the concealment 404 of a rejected/expired
        // token) lands here. Surface it as the REAL open-failure signal so the
        // caller's token-retry no longer guesses from wall-clock latency.
        if (out_open_failed != nullptr) {
            *out_open_failed = true;
        }
        Serial.println("[story] http stream open failed (non-200)");
        Serial.flush();
        return false;  // nothing to resume
    }

    AudioOutputI2S out;
    out.SetPinout(AREG_PIN_AMP_BCK, AREG_PIN_AMP_LRC, AREG_PIN_AMP_DATA);
    out.SetGain(volume_pot_gain());

    AudioGeneratorMP3 mp3;
    if (!mp3.begin(&http, &out)) {
        // Stream opened (200) but the body would not start decoding â€” NOT an
        // open failure, so out_open_failed stays false (no token retry).
        Serial.println("[story] mp3.begin (stream) failed");
        Serial.flush();
        return false;
    }

    bool interrupted = false;
    uint32_t last_yield = millis();
    while (mp3.isRunning()) {
        esp_task_wdt_reset();  // #047 â€” feed the WDT each decode iteration
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
            // A child turns the knob WHILE the story plays â€” precisely when
            // loop()'s IDLE branch cannot run, because we are blocked in here
            // for minutes. Hence the hot path. Placed after the barge-in check
            // above so button latency is untouched, and riding the existing
            // yield throttle rather than adding a second timer.
            if (volume_pot_tick_playing()) out.SetGain(volume_pot_gain());
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
// Offline story playback from the microSD content pack (Slice 2)
// UNVERIFIED â€” not compiled/flashed. See HARDENING-INTEGRATION.md Â§6.
// -------------------------------------------------------------

static bool s_sd_ok = false;

bool audio_sd_begin() {
#ifdef AREG_DISABLE_MP3_PLAYBACK
    Serial.println("[sd] disabled (AREG_DISABLE_MP3_PLAYBACK)");
    Serial.flush();
    return false;
#else
    if (s_sd_ok) {
        return true;  // idempotent
    }
    // Dedicated SPI bus for the card. Pins from config.h; clear of the
    // strapping / USB pins (see HARDENING-INTEGRATION.md Â§6.3).
    SPI.begin(AREG_PIN_SD_SCK, AREG_PIN_SD_MISO, AREG_PIN_SD_MOSI, AREG_PIN_SD_CS);
    // 16 MHz is well within any genuine card's spec and far above the
    // ~16 KB/s an MP3 needs; lower it if your wiring is long / noisy.
    s_sd_ok = SD.begin(AREG_PIN_SD_CS, SPI, 16000000U);
    if (s_sd_ok) {
        Serial.printf("[sd] mounted; type=%d size=%lluMB\n",
                      (int)SD.cardType(),
                      (unsigned long long)(SD.cardSize() / (1024ULL * 1024ULL)));
    } else {
        Serial.println("[sd] SD.begin failed (no card / wiring / format?)");
    }
    Serial.flush();
    return s_sd_ok;
#endif
}

bool audio_sd_available() {
    return s_sd_ok;
}

bool audio_sd_has_file(const char *path) {
#ifdef AREG_DISABLE_MP3_PLAYBACK
    (void)path;
    return false;
#else
    if (!s_sd_ok || path == nullptr) {
        return false;
    }
    return SD.exists(path);
#endif
}

#ifndef AREG_DISABLE_MP3_PLAYBACK
// #064 â€” sanity-check that an SD file actually starts like MP3 before handing
// it to the decoder. A corrupt card or a wrong/renamed file would otherwise
// feed arbitrary bytes to the MP3 generator. Accepts the two real MP3 starts
// (ID3v2 tag or an MPEG frame sync â€” same check as the network path #048),
// then rewinds the source to 0 for the decoder. This is an integrity/typo
// guard, NOT tamper-proofing â€” a signed content manifest (operator signing
// key) is the deferred full fix for #064.
static bool sd_file_looks_like_mp3(AudioFileSourceSD &file) {
    uint8_t hdr[3] = { 0, 0, 0 };
    uint32_t n = file.read(hdr, sizeof(hdr));
    file.seek(0, SEEK_SET);  // rewind so the decoder starts at byte 0
    if (n < 3) return false;
    if (hdr[0] == 'I' && hdr[1] == 'D' && hdr[2] == '3') return true;   // ID3v2 tag
    if (hdr[0] == 0xFF && (hdr[1] & 0xE0) == 0xE0) return true;         // MPEG frame sync
    return false;
}
#endif

bool audio_play_story_file(const char *path,
                           uint32_t start_byte,
                           audio_barge_in_fn barge_in,
                           uint32_t *out_resume_offset,
                           bool *out_started) {
    if (out_resume_offset != nullptr) {
        *out_resume_offset = 0;
    }
    // False until the decoder has actually produced a frame. Every bail-out
    // below returns with this still false, and none of them make a sound.
    if (out_started != nullptr) {
        *out_started = false;
    }
#ifdef AREG_DISABLE_MP3_PLAYBACK
    (void)path; (void)start_byte; (void)barge_in;
    Serial.println("[story] SD playback disabled (AREG_DISABLE_MP3_PLAYBACK)");
    Serial.flush();
    return false;
#else
    if (!s_sd_ok) {
        Serial.println("[story] SD not mounted");
        Serial.flush();
        return false;
    }
    Serial.printf("[story] SD open: %s @ %u\n", path, (unsigned)start_byte);
    Serial.flush();

    AudioFileSourceSD file(path);
    if (!file.isOpen()) {
        Serial.printf("[story] SD open failed: %s\n", path);
        Serial.flush();
        return false;  // nothing to resume; caller treats as natural end
    }
    // #064 â€” integrity precheck on a from-start play. A mid-stream resume
    // (start_byte > 0) trusts the file already validated when it started; a
    // frame-sync sniff at an arbitrary resume offset wouldn't be meaningful.
    if (start_byte == 0 && !sd_file_looks_like_mp3(file)) {
        Serial.printf("[story] SD file is not MP3 (corrupt/wrong file): %s â€” refusing to decode\n", path);
        Serial.flush();
        return false;  // caller treats as natural end / falls back to Wi-Fi
    }
    if (start_byte > 0) {
        // Seek to the resume byte; the MP3 decoder re-syncs to the next frame
        // header, exactly like the server-side ?from= resume.
        if (!file.seek((int32_t)start_byte, SEEK_SET)) {
            Serial.printf("[story] SD seek to %u failed; playing from start\n",
                          (unsigned)start_byte);
            Serial.flush();
        }
    }

    AudioOutputI2S out;
    out.SetPinout(AREG_PIN_AMP_BCK, AREG_PIN_AMP_LRC, AREG_PIN_AMP_DATA);
    out.SetGain(volume_pot_gain());

    AudioGeneratorMP3 mp3;
    if (!mp3.begin(&file, &out)) {
        Serial.println("[story] mp3.begin (SD) failed");
        Serial.flush();
        return false;
    }

    bool interrupted = false;
    uint32_t last_yield = millis();
    while (mp3.isRunning()) {
        esp_task_wdt_reset();  // #047 â€” feed the WDT each decode iteration
        // True barge-in: poll the button DURING decode and cut instantly.
        if (barge_in != nullptr && barge_in()) {
            // getPos() on a file source is the ABSOLUTE file position â€” no
            // base_offset bookkeeping needed (unlike the HTTP stream).
            uint32_t abs_pos = (uint32_t)file.getPos();
            // Back up past the decoded-but-unplayed I2S-buffered tail so resume
            // lands at the audible pause point (overlap, not skip).
            *out_resume_offset = (abs_pos > AREG_STORY_RESUME_FUDGE_BYTES)
                ? (abs_pos - AREG_STORY_RESUME_FUDGE_BYTES) : 0;
            mp3.stop();
            interrupted = true;
            Serial.printf("[story] SD barge-in: abs=%u resume_from=%u\n",
                          (unsigned)abs_pos, (unsigned)*out_resume_offset);
            Serial.flush();
            break;
        }
        if (!mp3.loop()) {
            mp3.stop();
            break;
        }
        // First completed decode iteration = decoder initialized and the
        // first frame handed to I2S. This is the earliest point at which
        // the child can actually have heard something.
        if (out_started != nullptr && !*out_started) {
            *out_started = true;
        }
        // Yield to FreeRTOS so watchdog / housekeeping are not starved.
        if (millis() - last_yield > 50) {
            delay(1);
            last_yield = millis();
            // The SD story is the long one â€” four minutes blocked in here,
            // with loop()'s IDLE branch unable to run, which is exactly the
            // window in which a child reaches for the knob. Placed after the
            // barge-in check above so button latency is untouched, and riding
            // the existing yield throttle rather than adding a second timer.
            if (volume_pot_tick_playing()) out.SetGain(volume_pot_gain());
        }
    }
    out.stop();
    file.close();
    Serial.printf("[story] SD end interrupted=%s\n", interrupted ? "true" : "false");
    Serial.flush();
    return interrupted;
#endif
}

// -------------------------------------------------------------
// Dead-air mitigation â€” S1 (earcon) + S3 (Q&A stream)
// UNVERIFIED â€” not compiled/flashed. See HARDENING-INTEGRATION.md Â§2.
// -------------------------------------------------------------

// ---- Shared helper: synthesize and write a soft sine tone ----
//
// Writes a pure sine at `freq_hz` for `duration_ms` milliseconds directly
// to an already-opened AudioOutputI2S. The tone is generated sample-by-
// sample into a small stack buffer and pushed via out.ConsumeSample().
//
// HARDWARE ASSUMPTION: AudioOutputI2S is already begin()-ed and configured
// at AREG_SAMPLE_RATE_HZ. ConsumeSample() is the ESP8266Audio sample-push
// API â€” it takes a pair of 16-bit values (left, right, packed as int16_t[2])
// and returns false when the I2S DMA buffer is full (back-pressure signal).
// When it returns false we yield briefly with delay(1) and retry.
//
// The envelope ramps the amplitude up for the first 50 ms and down for the
// last 50 ms (linear fade) to avoid a click at start/end.
#ifndef AREG_DISABLE_MP3_PLAYBACK
//
// `abort` (optional, added 2026-08-10 for the Q&A latency slice) is polled
// every kAbortPollSamples samples. When it returns true the tone stops EARLY
// with a short linear fade-out instead of running out its full duration.
// Rationale: the thinking-bed loop could only notice the answer had arrived
// BETWEEN pulses, so a reply that landed 20 ms into a 600 ms pulse still waited
// out the remaining 580 ms of tone before playing. The fade is what keeps an
// early stop from clicking; a hard cut mid-sine is audible on the MAX98357A.
static void synth_write_tone(AudioOutputI2S &out,
                             uint16_t freq_hz,
                             uint32_t duration_ms,
                             int16_t  amplitude,
                             audio_abort_fn abort = nullptr) {
    // HARDWARE ASSUMPTION: AREG_SAMPLE_RATE_HZ is 16000. If it is changed,
    // this function adapts automatically via the constant.
    const uint32_t total_samples =
        (uint32_t)AREG_SAMPLE_RATE_HZ * duration_ms / 1000;
    const uint32_t fade_samples  = (uint32_t)AREG_SAMPLE_RATE_HZ * 50 / 1000; // 50 ms fade
    // Phase accumulator: integer steps of (freq_hz / sample_rate) in
    // units of 1/65536 of a cycle. Stays exact over the call's lifetime.
    uint32_t phase     = 0;
    uint32_t phase_inc = ((uint32_t)freq_hz << 16) / AREG_SAMPLE_RATE_HZ;

    // Early-stop state. `abort_at` is the sample index the abort fired on;
    // from there the envelope ramps to zero over kAbortFadeSamples and the
    // loop ends. 0xFFFFFFFF = "not aborting".
    static constexpr uint32_t kAbortPollSamples = 256;                       // ~16 ms @16 kHz
    const uint32_t kAbortFadeSamples = (uint32_t)AREG_SAMPLE_RATE_HZ * 8 / 1000;  // 8 ms
    uint32_t abort_at = 0xFFFFFFFFu;

    for (uint32_t i = 0; i < total_samples; ++i) {
        if (abort != nullptr && abort_at == 0xFFFFFFFFu &&
            (i % kAbortPollSamples) == 0 && abort()) {
            abort_at = i;
        }
        if (abort_at != 0xFFFFFFFFu && i >= abort_at + kAbortFadeSamples) {
            return;  // faded out cleanly above
        }
        // Map the 32-bit phase accumulator (0..0xFFFFFFFF) onto 0..2Ï€ and
        // compute sinf(). The Xtensa LX7 has hardware FPU â€” this is fast.
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
        // Abort fade-out overrides the normal envelope (it is always steeper).
        if (abort_at != 0xFFFFFFFFu) {
            const uint32_t done = i - abort_at;
            raw = (int16_t)((int32_t)raw *
                            (int32_t)(kAbortFadeSamples - done) /
                            (int32_t)kAbortFadeSamples);
        }

        // ConsumeSample expects AudioOutput::AudioType (int16_t[2] packed
        // as a uint32_t on some versions, or two separate calls â€” the
        // public API is ConsumeSample(int16_t lr[2])). Use the two-element
        // array form which is consistent across ESP8266Audio versions.
        // HARDWARE ASSUMPTION: mono signal â€” copy left to right.
        int16_t lr[2] = { raw, raw };
        // Back-pressure: if DMA buffers are full, yield and retry â€” but BOUND
        // the wait (#047). A never-clearing I2S stall (dead amp / DMA wedged)
        // would otherwise hang here forever; on timeout we abort the tone, which
        // is optional (earcon / thinking-bed), so the caller continues cleanly.
        uint32_t consume_deadline = millis() + AREG_I2S_CONSUME_TIMEOUT_MS;
        while (!out.ConsumeSample(lr)) {
            if ((int32_t)(millis() - consume_deadline) >= 0) {  // rollover-safe
                Serial.println("[audio] synth: I2S back-pressure timeout â€” aborting tone");
                Serial.flush();
                return;
            }
            delay(1);
        }
        phase += phase_inc;
    }
}
#endif  // AREG_DISABLE_MP3_PLAYBACK

// ---- Shared: bring up I2S and write one synthesized pulse ----
//
// Both the earcon and the thinking bed are "make one tone now" with
// different numbers, so the peripheral bring-up lives here once instead of
// being copied per tone. Extracted 2026-08-16 when the bed stopped being a
// second call to the earcon; nothing about the earcon's own sound, timing
// or logging changed in the move.
static bool synth_play_pulse(uint16_t freq_hz,
                             uint32_t duration_ms,
                             int16_t  amplitude,
                             audio_abort_fn abort,
                             const char *tag) {
    Serial.printf("[audio] %s_begin\n", tag);
    Serial.flush();
#ifdef AREG_DISABLE_MP3_PLAYBACK
    (void)freq_hz;
    (void)duration_ms;
    (void)amplitude;
    (void)abort;
    // Playback disabled for bench I2S isolation -- treat as success
    // (the important thing is we didn't add silence; the tone is optional).
    Serial.printf("[audio] %s: playback disabled, skipping\n", tag);
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
    out.SetGain(volume_pot_gain());
    // The synth path MUST set the I2S sample rate. The MP3 path gets it from
    // the decoder (mp3.begin -> out.SetRate); without it here the channel runs
    // at ESP8266Audio's default 44.1 kHz, so the 16 kHz-generated tone is
    // clocked ~2.75x too fast -- mis-paced and inaudible -- and synth_write_tone
    // returns almost instantly (which is why the thinking-bed loop spins,
    // spamming earcon_begin/earcon_end). Setting the rate fixes both.
    out.SetRate(AREG_SAMPLE_RATE_HZ);
    if (!out.begin()) {
        Serial.printf("[audio] %s: out.begin() failed\n", tag);
        Serial.flush();
        return false;
    }

    synth_write_tone(out, freq_hz, duration_ms, amplitude, abort);

    out.stop();
    Serial.printf("[audio] %s_end\n", tag);
    Serial.flush();
    return true;
#endif
}

// ---- S1: audio_play_thinking_earcon() -----------------------
bool audio_play_thinking_earcon() {
    return audio_play_thinking_earcon_abortable(nullptr);
}

bool audio_play_thinking_earcon_abortable(audio_abort_fn abort) {
    return synth_play_pulse(AREG_EARCON_FREQ_HZ,
                            AREG_EARCON_DURATION_MS,
                            AREG_EARCON_AMPLITUDE,
                            abort,
                            "earcon");
}

// ---- S3: audio_play_thinking_bed_abortable() ----------------
//
// The pitch figure the wait is built from. Pulse 1 of a wait is the earcon
// and is deliberately NOT this: it is the child's acoustic receipt for
// letting go of the button, so it keeps its own pitch, length and loudness.
// Everything after it is this -- lower, shorter and quieter, on the
// AREG_THINKBED_* constants that were declared for exactly this in config.h
// and, until 2026-08-16, were never read by any sound path. Only
// AREG_THINKBED_MAX_PULSES was ever live, so the "thinking bed" was in
// truth the 440 Hz earcon repeated up to 70 times.
//
// WHY a contour and not one repeated note. The defect is monotony, not
// pitch: a child waiting ten seconds heard the same beep sixteen times,
// which is what made the wait feel like a stuck machine rather than a toy
// that is working. A strict two-note alternation is still a clock, so the
// pitch instead walks up and back down a six-step figure -- about three
// seconds to come round, so by two seconds the child has heard movement,
// and by ten no short loop has repeated often enough to be countable.
//
// WHY it stays this narrow. The top step is one whole tone above the base
// (base * 9/8, since 3/24 = 1/8) and no further. Wider begins to sound like
// a tune asking for attention; this has to sit behind a child's shoulder
// while they wait, which is the same reason it runs at the thinking-bed's
// quieter amplitude rather than the earcon's.
//
// WHY it never speeds up or climbs as the wait grows: an accelerating or
// rising beep reads as an alarm. The figure at second ten is identical to
// the figure at second two -- calm, and finite-feeling, per the tone
// contract in CLAUDE.md. The wait is still bounded by
// AREG_THINKBED_MAX_PULSES exactly as before.
static const uint8_t kBedContourSteps[] = { 0, 1, 2, 3, 2, 1 };

bool audio_play_thinking_bed_abortable(uint32_t pulse_index,
                                       audio_abort_fn abort) {
    const uint8_t step =
        kBedContourSteps[pulse_index %
                         (sizeof(kBedContourSteps) / sizeof(kBedContourSteps[0]))];
    // Derived from the constant rather than tabulated, so retuning
    // AREG_THINKBED_FREQ_HZ moves the whole figure and keeps its shape.
    const uint16_t freq_hz =
        (uint16_t)(AREG_THINKBED_FREQ_HZ +
                   ((uint32_t)AREG_THINKBED_FREQ_HZ * step) / 24u);

    // Once per wait, not per pulse: enough for a bench listener to confirm
    // the contour is live without 70 lines of it scrolling past.
    if (pulse_index == 0) {
        Serial.printf("[audio] thinkbed contour base=%u Hz top=%u Hz %u ms amp=%u\n",
                      (unsigned)AREG_THINKBED_FREQ_HZ,
                      (unsigned)(AREG_THINKBED_FREQ_HZ +
                                 ((uint32_t)AREG_THINKBED_FREQ_HZ * 3u) / 24u),
                      (unsigned)AREG_THINKBED_PULSE_MS,
                      (unsigned)AREG_THINKBED_AMPLITUDE);
        Serial.flush();
    }

    return synth_play_pulse(freq_hz,
                            AREG_THINKBED_PULSE_MS,
                            AREG_THINKBED_AMPLITUDE,
                            abort,
                            "thinkbed");
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
    // sync word â€” this is fine; the decoder handles streaming natively.
    AudioFileSourceHTTPStream http(url);
    if (!http.isOpen()) {
        Serial.println("[audio] qa_stream: http open failed; caller may use buffered fallback");
        Serial.flush();
        return false;
    }

    AudioOutputI2S out;
    out.SetPinout(AREG_PIN_AMP_BCK, AREG_PIN_AMP_LRC, AREG_PIN_AMP_DATA);
    out.SetGain(volume_pot_gain());

    AudioGeneratorMP3 mp3;
    if (!mp3.begin(&http, &out)) {
        Serial.println("[audio] qa_stream: mp3.begin failed; caller may use buffered fallback");
        Serial.flush();
        return false;
    }

    uint32_t last_yield = millis();
    while (mp3.isRunning()) {
        esp_task_wdt_reset();  // #047 â€” feed the WDT each decode iteration
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


// ---- AREG_QA_STREAM_PLAYBACK: decode the live POST response --------
//
// See audio_io.h for the contract, and latency-firmware-notes.md for why
// the URL form above cannot serve the Q&A answer. Everything in this block
// compiles to ZERO bytes without the flag.
#ifdef AREG_QA_STREAM_PLAYBACK
#ifdef AREG_DISABLE_MP3_PLAYBACK

bool audio_play_qa_stream_response(Stream *, int) {
    Serial.println("[audio] qa_stream: playback disabled, skipping");
    Serial.flush();
    return false;
}

#else

// An AudioFileSource over an already-open HTTP response body.
//
// Two shapes of body, one class:
//   identity â€” `_remaining` counts down from Content-Length.
//   chunked  â€” `_remaining` is refilled from each hex chunk-size line and a
//              zero-size chunk ends the body. HTTPClient does NOT de-chunk
//              for us on getStreamPtr(); it only does that inside
//              writeToStream(). Feeding raw chunk headers to the MP3 decoder
//              would be garbage-in, so the de-framing lives here.
//
// The first bytes of the body are sniffed for an MP3 signature before any
// byte can reach the decoder (#048 parity with the buffered path) and then
// replayed from `_head`, because a socket cannot be rewound.
//
// Not seekable, and getSize() is honestly unknown; AudioGeneratorMP3
// requires neither.
class AudioFileSourceHttpBody : public AudioFileSource {
public:
    AudioFileSourceHttpBody(Stream *body, int content_length)
        : _body(body),
          _chunked(content_length <= 0),
          _remaining(content_length > 0 ? (uint32_t)content_length : 0) {}

    // Reads and validates the leading bytes. Must be called once, before
    // the generator. Returns false when the body is short or not MP3.
    bool prime() {
        uint8_t head[3];
        if (pull(head, sizeof(head)) != sizeof(head)) {
            Serial.println("[audio] qa_stream: body too short");
            Serial.flush();
            return false;
        }
        const bool is_mp3 = (head[0] == 'I' && head[1] == 'D' && head[2] == '3') ||
                            (head[0] == 0xFF && (head[1] & 0xE0) == 0xE0);
        if (!is_mp3) {
            Serial.printf("[audio] qa_stream: body is not MP3 (%02X %02X %02X); rejecting\n",
                          head[0], head[1], head[2]);
            Serial.flush();
            return false;
        }
        memcpy(_head, head, sizeof(head));
        _head_len = sizeof(head);
        _head_pos = 0;
        return true;
    }

    // True when bytes this source was still owed never arrived -- a stalled
    // socket, or the server dropping the connection mid-answer.
    //
    // NOT the same as "the body had bytes left in it". The MP3 decoder stops
    // at the audio's end, which need not be the body's end (ID3v1 trailer, a
    // final partial frame) -- voice_qa_stream_finish() drops the connection
    // for exactly that reason. So leftover bytes are normal and `_remaining`
    // is useless as a completeness test; the only honest signal is a read
    // that failed while the decoder was still asking for data.
    bool truncated() const { return _truncated; }

    bool isOpen() override { return _body != nullptr; }
    bool close() override { _body = nullptr; return true; }
    uint32_t getSize() override { return 0; }   // unknown â€” streaming
    uint32_t getPos() override { return _pos; }
    bool seek(int32_t, int) override { return false; }

    uint32_t read(void *data, uint32_t len) override {
        uint8_t *dst = (uint8_t *)data;
        uint32_t done = 0;
        // Replay the sniffed bytes first.
        while (done < len && _head_pos < _head_len) {
            dst[done++] = _head[_head_pos++];
            _pos++;
        }
        if (done < len) {
            done += pull(dst + done, len - done);
        }
        return done;
    }

private:
    // Body bytes, transfer-encoding aware. Returns a short count at EOF.
    uint32_t pull(uint8_t *dst, uint32_t len) {
        uint32_t done = 0;
        while (done < len && !_eof) {
            if (_remaining == 0) {
                if (!_chunked)       { _eof = true; break; }  // identity complete
                if (!next_chunk())   { _eof = true; break; }  // 0-size chunk / error
            }
            uint32_t want = len - done;
            if (want > _remaining) want = _remaining;
            const int got = wait_read(dst + done, want);
            if (got <= 0) {
                // Owed bytes that never came. Record it: pull() collapses this
                // into the same _eof a clean end produces, and without the
                // distinction half an answer is indistinguishable from a whole
                // one to the caller.
                _truncated = true;
                _eof       = true;
                break;
            }
            done       += (uint32_t)got;
            _pos       += (uint32_t)got;
            _remaining -= (uint32_t)got;
        }
        return done;
    }

    // Blocking read with a stall bound + watchdog feeding. The decoder calls
    // this from inside mp3.loop(), which is exactly where the toy legitimately
    // waits on the network â€” an unbounded wait here is a watchdog reboot.
    int wait_read(uint8_t *dst, uint32_t want) {
        const uint32_t deadline = millis() + (uint32_t)AREG_HTTP_READ_MS;
        while (_body->available() == 0) {
            esp_task_wdt_reset();
            if ((int32_t)(millis() - deadline) >= 0) {
                Serial.println("[audio] qa_stream: body stalled");
                Serial.flush();
                return -1;
            }
            delay(2);
        }
        int avail = _body->available();
        if ((uint32_t)avail > want) avail = (int)want;
        return (int)_body->readBytes(dst, (size_t)avail);
    }

    // Reads "<hex>[;ext]\r\n" and arms _remaining. A zero-size chunk is the
    // end of the body and returns false (trailers are ignored â€” the socket
    // is about to be closed or drained by HTTPClient::end()).
    bool next_chunk() {
        if (_saw_chunk) {                       // CRLF terminating the previous chunk
            uint8_t crlf[2];
            if (wait_read(crlf, 2) != 2) { _truncated = true; return false; }
        }
        char line[24];
        size_t n = 0;
        for (;;) {
            uint8_t c;
            // Dying part-way through a chunk header is a truncated body; the
            // zero-size chunk below is the clean end and must not be flagged.
            if (wait_read(&c, 1) != 1) { _truncated = true; return false; }
            if (c == '\n') break;
            if (c != '\r' && n < sizeof(line) - 1) line[n++] = (char)c;
        }
        line[n] = '\0';
        const long size = strtol(line, nullptr, 16);
        if (size <= 0) return false;
        _remaining = (uint32_t)size;
        _saw_chunk = true;
        return true;
    }

    Stream  *_body;
    bool     _chunked;
    uint32_t _remaining;
    uint32_t _pos       = 0;
    bool     _eof       = false;
    bool     _truncated = false;
    bool     _saw_chunk = false;
    uint8_t  _head[3]   = {0, 0, 0};
    uint8_t  _head_len  = 0;
    uint8_t  _head_pos  = 0;
};

bool audio_play_qa_stream_response(Stream *body, int content_length) {
    if (body == nullptr) return false;
    Serial.printf("[audio] qa_stream_begin (live POST body, len=%d, %s)\n",
                  content_length, content_length > 0 ? "identity" : "chunked");
    Serial.flush();

    AudioFileSourceHttpBody source(body, content_length);
    if (!source.prime()) {
        return false;
    }

    AudioOutputI2S out;
    out.SetPinout(AREG_PIN_AMP_BCK, AREG_PIN_AMP_LRC, AREG_PIN_AMP_DATA);
    out.SetGain(volume_pot_gain());

    AudioGeneratorMP3 mp3;
    if (!mp3.begin(&source, &out)) {
        Serial.println("[audio] qa_stream: mp3.begin failed");
        Serial.flush();
        return false;
    }

    uint32_t last_yield = millis();
    while (mp3.isRunning()) {
        esp_task_wdt_reset();
        if (!mp3.loop()) {
            mp3.stop();
            break;
        }
        if (millis() - last_yield > 50) {
            delay(1);
            last_yield = millis();
        }
    }
    out.stop();

    // Report what actually happened, not that we reached the end of the
    // function. This used to be an unconditional `return true`, so a body
    // that stalled half-way through the answer was reported to the caller as
    // a good turn: the child heard a sentence and a half, and the toy neither
    // played the failure clip nor logged anything wrong. A partial answer is
    // a failed turn -- on this path there is no buffered copy, so the caller
    // playing the canned failure clip is the only recovery available.
    const bool ok = !source.truncated();
    Serial.printf("[audio] qa_stream_end ok=%s bytes=%u%s\n",
                  ok ? "true" : "false",
                  (unsigned)source.getPos(),
                  ok ? "" : " (body stopped mid-answer)");
    Serial.flush();
    return ok;
}

#endif  // AREG_DISABLE_MP3_PLAYBACK
#endif  // AREG_QA_STREAM_PLAYBACK

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

// -------------------------------------------------------------
// AregVoiceMvp / audio_io.h
//
// Thin wrappers around the ESP32 I2S peripheral for the two
// C1 audio flows: capture from INMP441 (RX) and playback to
// MAX98357A (TX). Capture is done directly; playback delegates
// to ESP8266Audio's AudioGeneratorMP3 + AudioOutputI2S so this
// sketch never ships an MP3 decoder.
// -------------------------------------------------------------
#pragma once

#include <Arduino.h>

// Initialize the INMP441 I2S mic. Call once at boot.
// Returns true on success.
bool audio_mic_begin();

// Capture PCM samples into `out_buffer` for up to
// `max_duration_ms` milliseconds or until `should_stop()`
// returns true (checked between chunks). Returns the number of
// 16-bit samples written. Fills the buffer with 16-bit signed
// mono little-endian samples at AREG_SAMPLE_RATE_HZ.
//
// The poll callback is how the state machine breaks out on
// button-release without needing a second task.
typedef bool (*audio_should_stop_fn)();
size_t audio_mic_capture(int16_t *out_buffer,
                         size_t max_samples,
                         uint32_t max_duration_ms,
                         audio_should_stop_fn should_stop);

// Tear down the mic I2S peripheral. Called before starting
// playback — MAX98357A output needs its own I2S instance on an
// ESP32-S3-DevKitC-1, and sharing the same peripheral number
// via reconfigure is simpler than running both at once.
void audio_mic_end();

// Initialize the MAX98357A I2S output. Call once per playback
// session (after `audio_mic_end`); teardown handled internally
// once `audio_play_mp3_buffer` returns.
bool audio_speaker_begin();

// Buffered MP3 playback from memory. Decodes + streams to I2S
// synchronously. Returns true on clean end-of-stream; false on
// decoder error, empty buffer, or setup failure.
bool audio_play_mp3_buffer(const uint8_t *data, size_t length);

// Write a canonical 44-byte PCM WAV header into `hdr_out`
// describing `pcm_sample_count` mono 16-bit samples at
// AREG_SAMPLE_RATE_HZ. `hdr_out` must be at least 44 bytes.
void audio_write_wav_header(uint8_t *hdr_out, uint32_t pcm_sample_count);

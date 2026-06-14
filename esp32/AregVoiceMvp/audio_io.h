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

// Continuous, interruptible STORY playback. Streams an MP3 directly
// from `url` (ESP8266Audio HTTP source) and decodes it as it
// downloads — so an arbitrarily long story plays start-to-finish
// without buffering the whole clip (no 512 KB limit, no segments).
//
// `barge_in` is polled every decode iteration; when it returns true
// the audio is cut IMMEDIATELY (true barge-in) and the byte offset
// reached is written to `*out_resume_offset` so the caller can resume
// the SAME file from that exact point (append `?from=<offset>` to the
// URL). A small offset fudge is subtracted so resume overlaps rather
// than skips the I2S-buffered tail.
//
// `base_offset` is the absolute byte offset this stream was opened at
// (the `?from=` value, or 0 from the start). It is REQUIRED for correct
// resume because the HTTP source's getPos() counts from 0 of the
// current (partial) response, so the absolute file position is
// base_offset + getPos().
//
// Returns true when interrupted (resume from *out_resume_offset, an
// ABSOLUTE file offset); false when the story played to its natural end
// (*out_resume_offset is left 0).
typedef bool (*audio_barge_in_fn)();
bool audio_play_story_stream(const char *url,
                             uint32_t base_offset,
                             audio_barge_in_fn barge_in,
                             uint32_t *out_resume_offset);

// Write a canonical 44-byte PCM WAV header into `hdr_out`
// describing `pcm_sample_count` mono 16-bit samples at
// AREG_SAMPLE_RATE_HZ. `hdr_out` must be at least 44 bytes.
void audio_write_wav_header(uint8_t *hdr_out, uint32_t pcm_sample_count);

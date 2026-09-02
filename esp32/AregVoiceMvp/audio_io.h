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

// Build flags this header switches on (AREG_QA_STREAM_PLAYBACK) live in
// config.h, so it must be visible here regardless of include order at the
// call site. config.h is #pragma once and macro-only, so this is free.
#include "config.h"

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
//
// #063 — `out_open_failed` (optional) surfaces the REAL stream-open result so
// the caller no longer has to infer a token rejection from wall-clock latency.
// It is set true ONLY when the HTTP GET did not open with 200 (the concealment
// 404 a rejected/expired story-audio token produces); it stays false on a
// natural end, a barge-in, or a post-open decode error. Pass nullptr to ignore.
typedef bool (*audio_barge_in_fn)();
bool audio_play_story_stream(const char *url,
                             uint32_t base_offset,
                             audio_barge_in_fn barge_in,
                             uint32_t *out_resume_offset,
                             bool *out_open_failed = nullptr);

// ---------------------------------------------------------------
// Offline story playback from the microSD content pack (Slice 2)
// UNVERIFIED — not compiled/flashed. See HARDENING-INTEGRATION.md §6.
// ---------------------------------------------------------------

// Mount the microSD card (SPI). Call once at boot, after Serial. Returns
// true when a FAT card is mounted and readable; false otherwise (no card,
// wiring fault, unformatted). NON-FATAL by contract: the caller logs and
// continues, falling back to Wi-Fi streaming — a missing card never bricks
// the toy.
bool audio_sd_begin();

// Whether the card mounted (audio_sd_begin() returned true this boot).
bool audio_sd_available();

// Whether `path` exists on the mounted card. Always false when no card is
// mounted (so callers can gate offline playback on a single check).
bool audio_sd_has_file(const char *path);

// Continuous, interruptible STORY playback from a FILE on the SD card — the
// offline sibling of audio_play_story_stream(). Same barge-in / resume
// contract, with two simplifications because the source is a seekable local
// file (AudioFileSourceSD) rather than an HTTP response:
//   - resume is a file seek(start_byte) before decode (the MP3 decoder
//     re-syncs to the next frame header, exactly like the server ?from=),
//   - getPos() is already the ABSOLUTE file position, so there is no
//     base_offset parameter.
// `barge_in` is polled every decode iteration; on a press the audio cuts
// instantly and (abs_pos - AREG_STORY_RESUME_FUDGE_BYTES) is written to
// *out_resume_offset so resume overlaps rather than skips the buffered tail.
//
// Returns true when interrupted (resume from *out_resume_offset); false when
// the story played to its natural end (*out_resume_offset = 0) OR the file
// could not be opened. Callers should audio_sd_has_file() first, so a false
// return reliably means "natural end".
// `out_started` (optional) reports whether playback GENUINELY BEGAN: it is
// set true only after mp3.begin() succeeded AND the first mp3.loop() decode
// iteration completed, i.e. the decoder was initialized and the first frame
// was handed to I2S. Every earlier bail-out (SD not mounted, open failed,
// the #064 not-an-MP3 precheck, mp3.begin() failure) leaves it false, and
// all of those produce NO audible output. It is written on entry, so a
// caller may pass it uninitialized.
//
// This is what lets story selection distinguish "the child heard this
// story" from "it never started" — the rotation cursor must only advance
// on the former.
bool audio_play_story_file(const char *path,
                           uint32_t start_byte,
                           audio_barge_in_fn barge_in,
                           uint32_t *out_resume_offset,
                           bool *out_started = nullptr);

// ---------------------------------------------------------------
// Dead-air mitigation (S1 + S3)
// UNVERIFIED — not compiled/flashed. See HARDENING-INTEGRATION.md §2.
// ---------------------------------------------------------------

// S1 — Instant "thinking" earcon.
//
// Synthesizes a short soft tone (~AREG_EARCON_DURATION_MS ms at
// AREG_EARCON_FREQ_HZ) and writes it directly to the I2S output in one
// blocking call. No network, no SD, no heap allocation beyond a small
// stack-local sine table. Intended to be called the instant recording
// ends and upload begins (ST_UPLOADING), so the child gets IMMEDIATE
// acoustic feedback instead of silence.
//
// HARDWARE ASSUMPTION: audio_speaker_begin() must have been called
// before this. The function creates its own AudioOutputI2S + i2s_write
// path (same pins as audio_play_mp3_buffer) and leaves the speaker
// peripheral in a clean state on return.
//
// Returns true on success, false if I2S setup fails (non-fatal —
// silent earcon is still better than a crash).
bool audio_play_thinking_earcon();

// Same tone, but interruptible. `abort` is polled roughly every 16 ms of
// audio; when it returns true the tone fades out over ~8 ms and returns
// early instead of running the full AREG_EARCON_DURATION_MS.
//
// WHY (Q&A latency, 2026-08-10): the thinking-bed loop between the upload
// and the answer could only check "has the answer arrived?" BETWEEN pulses,
// so an answer that landed just after a pulse started still waited out the
// rest of that 600 ms pulse — 0-600 ms of pure dead time added to every
// question, averaging ~300 ms. Passing voice_async_upload_done here removes
// it. Passing nullptr is exactly audio_play_thinking_earcon().
typedef bool (*audio_abort_fn)();
bool audio_play_thinking_earcon_abortable(audio_abort_fn abort);

// S3 — one pulse of the thinking bed, the sound of the wait AFTER the
// opening earcon. Lower, shorter and quieter than the earcon, on the
// AREG_THINKBED_* constants in config.h.
//
// WHY (2026-08-16): the bed was a second call to the earcon, so a child
// waiting for an answer heard one identical 440 Hz 600 ms beep up to 70
// times — the 4th by second 2, the 16th by second 10. That monotony, more
// than the wait itself, is what made the toy sound stuck. The three
// AREG_THINKBED_ tone constants had been declared for this from the start
// and were never read by any sound path; only the pulse COUNT was live.
//
// `pulse_index` is 0 for the first BED pulse (i.e. the second pulse of the
// wait overall) and counts up from there. It selects the pitch from a slow
// rise-and-fall figure — see the contour comment in audio_io.cpp for why it
// moves at all, why it stays inside one whole tone, and why it deliberately
// never accelerates. Callers pass a plain counter; the wrap is handled here.
//
// `abort` behaves exactly as it does for the earcon: polled roughly every
// 16 ms of audio, fading out over ~8 ms. The shorter pulse means the loop
// also gets BETWEEN-pulse chances to notice more often, so this cannot
// lengthen the worst-case wait after an answer lands.
bool audio_play_thinking_bed_abortable(uint32_t pulse_index,
                                       audio_abort_fn abort);

// S3 — Stream a Q&A answer incrementally from a URL.
//
// Opens `url` as an HTTP stream and decodes the MP3 response chunk-by-
// chunk via ESP8266Audio's AudioFileSourceHTTPStream, playing audio as
// bytes arrive. The first audible audio begins as soon as the server
// sends its first MP3 frame — no full-buffer-wait.
//
// Semantically identical to audio_play_story_stream but:
//   - No barge-in poll (Q&A answers are short; interrupt at this layer
//     would cut the child's answer mid-sentence).
//   - No resume-offset output (Q&A is not a pausable asset).
//   - Falls through cleanly if the stream fails to open, so the caller
//     can play the buffered fallback instead.
//
// Returns true on clean playback (stream played to end-of-body or
// decoder reached EOS); false on stream-open failure or decode error.
// In the false case the caller should play the buffered fallback via
// audio_play_mp3_buffer() if it already has a response in hand.
//
// HARDWARE ASSUMPTION: audio_speaker_begin() must have been called
// before this. The URL must be reachable (caller is responsible for
// checking Wi-Fi connectivity).
bool audio_play_qa_stream(const char *url);

// ---------------------------------------------------------------
// AREG_QA_STREAM_PLAYBACK — play the Q&A answer AS IT ARRIVES
// (compile-gated, OFF by default; see latency-firmware-notes.md)
// ---------------------------------------------------------------
//
// audio_play_qa_stream() above is the URL form, and it is UNUSABLE for the
// in-story Q&A answer: AudioFileSourceHTTPStream issues its own GET, while
// /api/chat/story-qa is POST-only — and if a GET were ever added there it
// would re-run STT+moderation+GPT+TTS and double-bill one question. The
// answer only ever exists as the body of the POST the toy already made.
//
// So this is the form that can actually be used: it decodes from an
// ALREADY-OPEN HTTP response stream (the one voice_client's Q&A POST is
// sitting on) and starts making sound on the first frames instead of after
// the whole body has landed in PSRAM.
//
// `content_length` is HTTPClient::getSize():
//   > 0  — Content-Length known; exactly that many raw MP3 bytes are read.
//   <= 0 — no Content-Length, i.e. Transfer-Encoding: chunked. The chunk
//          framing is de-framed HERE, because HTTPClient only de-chunks
//          inside writeToStream(); getStreamPtr() hands back the RAW socket
//          with the hex size lines still in it, and feeding those to the MP3
//          decoder is garbage-in. This is the half that makes a chunked
//          backend response work instead of failing the turn.
//
// Keeps the #048 contract: the first bytes are sniffed for an MP3 signature
// (ID3 tag or MPEG frame sync) before ANY byte reaches the decoder, exactly
// as the buffered path does.
//
// Returns true when the answer played to its end. False means nothing (or
// only part of the answer) was heard — the caller has no buffered copy to
// fall back to on this path and should play the canned failure clip.
//
// The caller owns the stream and must close the HTTP response afterwards.
#ifdef AREG_QA_STREAM_PLAYBACK
bool audio_play_qa_stream_response(Stream *body, int content_length);
#endif

// Write a canonical 44-byte PCM WAV header into `hdr_out`
// describing `pcm_sample_count` mono 16-bit samples at
// AREG_SAMPLE_RATE_HZ. `hdr_out` must be at least 44 bytes.
void audio_write_wav_header(uint8_t *hdr_out, uint32_t pcm_sample_count);

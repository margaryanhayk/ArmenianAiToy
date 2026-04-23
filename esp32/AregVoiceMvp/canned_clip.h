// -------------------------------------------------------------
// AregVoiceMvp / canned_clip.h
//
// PROGMEM-backed MP3 bytes for the one Armenian failure clip
// the firmware plays on any error path ("Հիմա չեմ լսում քեզ…"
// — "I can't hear you right now, try again in a little while").
//
// THIS FILE IS A COMMITTED STUB. Before first flash, regenerate
// it by running:
//
//     ./tools/render_canned_clip.sh
//
// from the AregVoiceMvp directory. The script calls OpenAI TTS
// with the same voice identity the backend uses (tts-1 / Nova),
// reads the resulting MP3, and rewrites this file as a PROGMEM
// byte array.
//
// The stub below is a single zero byte. AudioGeneratorMP3
// rejects it gracefully at begin(), so a developer who flashes
// without regenerating hears silence on the error path instead
// of a decoder crash.
// -------------------------------------------------------------
#pragma once

#include <Arduino.h>
#include <pgmspace.h>

static const uint8_t canned_clip_mp3[] PROGMEM = {
    0x00
};

static const size_t canned_clip_mp3_len = sizeof(canned_clip_mp3);

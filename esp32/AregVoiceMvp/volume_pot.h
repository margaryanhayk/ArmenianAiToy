// Optional hardware volume knob (analog potentiometer on an ADC1 pin).
//
// Until this module existed the amp gain was a hardcoded 0.6f at six call
// sites in audio_io.cpp, and the README's answer to "the toy is too quiet"
// was to edit that constant and re-flash — not an answer a parent has. A
// 10 kOhm linear pot turns it into something a child can reach mid-story.
//
// Wiring (bench 2026-08-15): wiper -> GPIO8 (ADC1_CH7), the two end legs to
// 3V3 and GND. GPIO8 is free on WROOM-1-N8R8 — not strapping, not on ADC2
// (which is unusable while Wi-Fi holds the radio), and no clash with mic
// 4/5/6, amp 15/16/7, SD 10/11/12/13, LED 48, or buttons 0/21/47.
//
// Compile contract, same idiom as answer_buttons.h: with AREG_PIN_VOLUME_POT
// undefined every call folds to a header-only no-op and volume_pot_gain()
// returns the historical fixed 0.6f, so a build without the knob behaves
// exactly as it did before this module landed.
//
// The pin define IS the contract, and that is the point of making this
// opt-in rather than always-on: an ADC pin with nothing wired to it floats,
// and a floating read wanders over the whole range. As a gain that is random
// volume, drifting on its own, indistinguishable from a fault. Requiring the
// pin means an unwired build cannot produce it.
#pragma once

#include <Arduino.h>
#include "config.h"

// Defaulted here as well as in config.h so an older config.h that predates
// the knob still compiles — and still compiles to today's exact behaviour.
#ifndef AREG_VOLUME_FIXED_GAIN
#define AREG_VOLUME_FIXED_GAIN 0.6f
#endif
#ifndef AREG_VOLUME_MIN_GAIN
#define AREG_VOLUME_MIN_GAIN 0.15f
#endif
#ifndef AREG_VOLUME_MAX_GAIN
#define AREG_VOLUME_MAX_GAIN 3.0f
#endif
#ifndef AREG_VOLUME_READ_MS
#define AREG_VOLUME_READ_MS 200
#endif
#ifndef AREG_VOLUME_DEADBAND_GAIN
#define AREG_VOLUME_DEADBAND_GAIN 0.03f
#endif

#if defined(AREG_PIN_VOLUME_POT)
#define AREG_HAS_VOLUME_POT 1

// Configure the ADC pin and take the first reading. Call once from setup(),
// beside answer_buttons_begin().
void volume_pot_begin();

// The gain last published by volume_pot_tick(). Cached — no ADC conversion,
// no floating-point work — so the decode loops can call it per iteration.
float volume_pot_gain();

// Re-read the pot if AREG_VOLUME_READ_MS has elapsed. Returns true only when
// that produced a materially different gain, so the caller can push it into
// the live AudioOutputI2S without doing so on every idle pass.
bool volume_pot_tick();

static inline bool volume_pot_present() { return true; }

#else
#define AREG_HAS_VOLUME_POT 0

// No knob on this build — every call folds away and the gain is the same
// fixed value the six audio_io.cpp sites hardcoded before this module.
static inline void volume_pot_begin() {}
static inline float volume_pot_gain() { return AREG_VOLUME_FIXED_GAIN; }
static inline bool volume_pot_tick() { return false; }
static inline bool volume_pot_present() { return false; }

#endif

// Optional YES / NO answer buttons (3-button hardware revision).
//
// The v1 enclosure has ONE button. The planned revision adds two smaller
// color-coded answer buttons — GREEN = yes/true, RED = no/false — so a
// 4-year-old can answer quiz questions offline, with zero cloud latency,
// without the double-press timing trick young children find hard.
//
// Wiring (recommended, per the ESP32-S3 pin audit 2026-08-05):
//   YES → GPIO21,  NO → GPIO47 — both free, non-strapping, PSRAM-safe.
//   One leg to the pin, other leg to GND; internal pull-ups are enabled
//   in software, same as the main button. Do NOT use GPIO0/3/45/46
//   (boot-mode pins), GPIO35-37 (OPI PSRAM), or GPIO19/20 (USB).
//
// Compile contract: when AREG_PIN_BUTTON_YES / AREG_PIN_BUTTON_NO are
// not defined in config.h (every existing build), everything here folds
// to header-only no-ops and the production image stays byte-identical.
// Define both pins to compile the real implementation in.
#pragma once

#include <Arduino.h>
#include "config.h"

#if defined(AREG_PIN_BUTTON_YES) && defined(AREG_PIN_BUTTON_NO)
#define AREG_HAS_ANSWER_BUTTONS 1

// Configure both pins with internal pull-ups. Call once from setup(),
// after the main button_begin().
void answer_buttons_begin();

// Debounced edge poll, same discipline as the main button_poll():
// returns 'Y' on a YES press edge, 'N' on a NO press edge, 0 otherwise.
// Simultaneous presses resolve to whichever edge is seen first.
char answer_buttons_poll();

static inline bool answer_buttons_present() { return true; }

#else
#define AREG_HAS_ANSWER_BUTTONS 0

// No answer buttons on this build — every call folds away.
static inline void answer_buttons_begin() {}
static inline char answer_buttons_poll() { return 0; }
static inline bool answer_buttons_present() { return false; }

#endif

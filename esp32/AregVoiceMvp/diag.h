// -------------------------------------------------------------
// AregVoiceMvp / diag.h
//
// Crash-surviving breadcrumb helpers for ESP32-S3 bench bring-up
// debugging. Each DIAG_MARK records (step_id, label, line) into
// RTC slow memory AND prints + flushes a [mark] line on Serial.
//
// RTC slow memory survives software resets, watchdog resets and
// brownout resets (but not hard power loss). When the chip
// crashes mid-audio and re-enumerates, the NEXT boot's
// `diag_print_previous_boot_context()` call reveals exactly
// which step was last reached before the disappearance.
//
// Pure diagnostic. No behavior change. Remove the call sites
// + the include from audio_io.cpp / AregVoiceMvp.ino /
// voice_client.cpp once the failure point is identified.
// -------------------------------------------------------------
#pragma once

#include <Arduino.h>

// Print the breadcrumb context recorded by the PREVIOUS boot
// (if any). Call ONCE in setup() AFTER Serial.begin and the
// reset-reason print, BEFORE the first DIAG_MARK in this boot.
//
// On a cold boot (RTC memory uninitialised) prints:
//   [boot] diag_boot_counter=0 (cold)
//   [boot] previous_step=(none)
//
// On a warm boot (panic / brownout / watchdog / EN reset)
// prints whatever the prior boot's last DIAG_MARK recorded:
//   [boot] diag_boot_counter=<n>
//   [boot] previous_step=<id> label=<label> line=<line>
void diag_print_previous_boot_context();

// Record a breadcrumb. ALWAYS writes (step_id, label, line_no) into
// RTC slow memory — that is the crash-forensics half, it survives the
// reset, and it is essentially free (three word stores + a strncpy).
//
// The Serial half — printing `[mark] …`, flushing the TX buffer, and
// waiting a few ms so native USB CDC actually transmits before a
// possible crash on the next instruction — is compiled in ONLY when
// AREG_DIAG_MARK_SERIAL is defined (bench opt-in; off by default).
//
// WHY IT IS OFF BY DEFAULT (latency, 2026-08-10). Each mark cost a
// Serial.flush() plus a hard delay(5). An in-story Q&A turn crosses
// ~26 marks — mic begin (9), capture (4), teardown (6), MP3 playback
// (7) — so it added ~130 ms of pure delay to every question a child
// asks, on a path the whole product is trying to shorten. The next-boot
// breadcrumb (diag_print_previous_boot_context) is unaffected: it reads
// RTC memory, not the serial log, so crash localisation still works
// with this off. Define AREG_DIAG_MARK_SERIAL in config.h when you need
// the live trace of a hang in progress.
//
// `label` is truncated to fit a fixed 48-byte buffer in RTC.
// nullptr is treated as the empty string.
void diag_mark(uint32_t step_id, const char *label, uint32_t line_no);

// Capture __LINE__ at the call site.
#define DIAG_MARK(id, label) diag_mark((uint32_t)(id), (label), (uint32_t)__LINE__)

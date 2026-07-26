// -------------------------------------------------------------
// AregVoiceMvp / story_select_test.h — on-device tests for story
// selection (bench-only).
//
// Gated behind AREG_STORY_SELECT_TEST_BENCH. Production compiles ZERO
// bytes of this module.
//
// WHY ON-DEVICE: this repository has no host C/C++ toolchain — only the
// xtensa cross-compilers from the Arduino ESP32 core — so a host-run
// unit test is not buildable here. This follows the existing bench-flag
// harness pattern (AREG_STORY_SD_FALLBACK_TEST_BENCH,
// AREG_CONTENT_SYNC_TEST_BENCH): assert on-device, print PASS/FAIL.
//
// SCOPE: the PURE halves only — story_select_next() (round-robin,
// no-repeat, wrap, unknown-previous, bounds) and story_entry_eligible()
// / story_is_safe_cache_path() (verified flag, version, size agreement,
// path safety). Needs no SD card, no Wi-Fi, no NVS, and writes nothing.
//
// It therefore does NOT cover, and must never be cited as covering:
//   - real NVS durability across a power cycle;
//   - real SD reads, real index files, or a real missing file;
//   - story_select_load_eligible / story_select_resolve_path end to end;
//   - pause/resume behavior in the live state machine.
// Those need the hardware bench.
// -------------------------------------------------------------
#pragma once

#ifdef AREG_STORY_SELECT_TEST_BENCH
// Call every loop() iteration while IDLE. Runs once, a short delay after
// boot so a serial monitor attached post-upload still catches it.
void story_select_test_tick();
#endif

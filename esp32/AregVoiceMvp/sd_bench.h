// -------------------------------------------------------------
// AregVoiceMvp / sd_bench.h — microSD HARDWARE PROOF (bench-only)
//
// Gated behind AREG_SD_BENCH_TEST (a compile flag, not config.h — pass
// -DAREG_SD_BENCH_TEST via build flags). Production builds compile ZERO
// bytes of this module and behave identically.
//
// Scope: prove the physical card + wiring can be mounted, identified,
// WRITTEN, and read back — the write half is the one thing the existing
// firmware never exercises (offline playback only reads), and the
// future Cloud→SD story sync depends on it. Deliberately NOT here:
// no MP3 download, no MP3 playback, no backend calls, no OTA changes.
// -------------------------------------------------------------
#pragma once

#ifdef AREG_SD_BENCH_TEST
// Run once at boot, AFTER audio_sd_begin() has mounted the card (the test
// reuses that mount — it never re-initializes the SPI bus). Prints card
// identity, then creates /areg_sd_test.txt, writes one line, reads it
// back, verifies byte-for-byte, and prints a single [sd-bench] PASS/FAIL
// verdict to serial. The test file is left on the card as evidence.
void sd_bench_run();
#endif

// -------------------------------------------------------------
// AregVoiceMvp / content_sync_test.h — on-device test harness for the
// multi-story content-sync decision logic (bench-only).
//
// Gated behind AREG_CONTENT_SYNC_TEST_BENCH. Production compiles ZERO
// bytes of this module.
//
// WHY ON-DEVICE: this repository has no host C/C++ toolchain — the only
// compilers installed are the xtensa cross-compilers that ship with the
// Arduino ESP32 core, so a host-run unit test is not buildable here.
// The established repo pattern for firmware verification is a bench-flag
// harness that asserts on-device and prints PASS/FAIL over serial (see
// AREG_STORY_SD_FALLBACK_TEST_BENCH in AregVoiceMvp.ino), and this
// follows it.
//
// SCOPE: exercises content_sync_rules.h and content_sync_model.cpp only
// — validation, path construction, bounds, manifest parsing, and index
// serialize/parse/migrate. It needs no SD card, no Wi-Fi and no backend,
// and it writes nothing.
//
// It therefore does NOT cover, and must never be cited as covering:
//   - real SD atomicity (rename/remove behavior on a FAT card);
//   - a real HTTP download, or a download that fails midway;
//   - the file-existence / actual-size half of the already-current
//     decision;
//   - reboot behavior after a partially successful sync.
// Those need the hardware bench.
// -------------------------------------------------------------
#pragma once

#ifdef AREG_CONTENT_SYNC_TEST_BENCH
// Call every loop() iteration while IDLE. Runs once, a short delay after
// boot so a serial monitor attached post-upload still catches it.
void content_sync_test_tick();
#endif

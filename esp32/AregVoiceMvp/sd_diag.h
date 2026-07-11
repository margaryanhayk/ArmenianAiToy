// -------------------------------------------------------------
// AregVoiceMvp / sd_diag.h — standalone microSD DIAGNOSTIC (bench-only)
//
// Gated behind AREG_SD_DIAG_BENCH (a compile flag, not config.h — pass
// -DAREG_SD_DIAG_BENCH via build flags). Production builds compile ZERO
// bytes of this module and are byte-identical.
//
// Purpose: isolate "SD.begin failed" seen by the content-sync bench into
// one of two verdicts, on the serial log alone:
//   - hardware/module/power/contact  → raw SD.begin fails at EVERY speed
//   - integration bug in the audio_sd_begin()/audio_sd_available() path
//     → raw mount works but the helper path doesn't
//
// Flow (first attempt 20 s after power-up so a monitor can attach, then
// RE-RUNS every 30 s until one attempt succeeds; prints an armed heartbeat
// until the first attempt so a late monitor still catches the next run):
//   before each attempt → "[sd-diag] attempt N start"
//   A) helper path: audio_sd_begin() (the exact helper the SD bench proof
//      and content-sync use)
//   B) if A failed: raw SPI.begin + SD.begin at 400 kHz / 1 MHz / 4 MHz /
//      10 MHz, SD.end() between attempts
//   any mount → card type, card size, root listing, create /sd_diag.txt,
//   write, read back, delete → "[sd-diag] readwrite PASS" then
//   "[sd-diag] PASS" and the repeat stops
//   no mount  → "[sd-diag] attempt N FAIL all SD.begin attempts failed",
//   retried again 30 s later
//
// Deliberately NOT here: backend calls, OTA, content-sync changes,
// playback.
// -------------------------------------------------------------
#pragma once

#ifdef AREG_SD_DIAG_BENCH
// Call every loop() iteration. Cheap no-op before ms>=20000 (armed
// heartbeat every 5 s), then runs the diagnostic and re-runs it every 30 s
// until one attempt mounts the card, after which it is a no-op for the boot.
void sd_diag_tick();
#endif

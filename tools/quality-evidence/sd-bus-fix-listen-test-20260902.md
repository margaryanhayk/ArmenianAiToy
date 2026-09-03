# SD-bus corruption: fixed, healed, and heard (2026-08-30 .. 09-02)

Symptom (owner report): a constant tone ("aaaa"/"eee") mixed into real
words, in every mode, at any volume.

Chain of proof:
1. Read self-test instrument added (content_sync_read_selftest): same
   file hashed twice at 16 MHz SPI gave DIFFERENT hashes and different
   byte counts (8,138,336 vs 2,682,880 B) — sd-selftest-20260830.log.
   Garbage bytes into the MP3 decoder = the tone.
2. SD SPI clock 16 MHz -> 4 MHz (AREG_SD_SPI_HZ). Self-test PASS on the
   same card and wiring — sd-postflash-20260830.log.
3. GPIO0 regression (unrelated, same week): chip proven parked in the
   ROM bootloader via esptool --before no-reset ("Staying in
   bootloader") — a stray wire on GPIO0. Wire removed, cold power-on
   boots normally (rst SPI_FAST_FLASH_BOOT). NOTHING may ever connect
   to GPIO0 again.
4. Card heal: operator sync-now re-verified all content at the clean
   bus; one damaged voice clip (say-again) re-downloaded sha-ok;
   sutlik-orskan's "present but not usable" proved to be a corrupted
   READ, not a corrupted file — card-heal-20260902.log.
5. OWNER LISTEN TEST 2026-09-02: one full story + after-story flow on
   the toy, by ear: "its ok". The tone is gone.

Still open, honestly: the full 70-clip listen gate — the owner heard
ONE story's clip set (intro/question/summary path), not all ten.

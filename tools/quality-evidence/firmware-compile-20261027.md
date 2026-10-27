# Firmware compile — HEAD on esp32 core 3.3.6 (2026-10-27)

First compile of the firmware at `main` HEAD by anyone (the last firmware
commit, `5cf65b6` 2026-09-14, said "NOT compile-verified"). Done in the cloud
container because the owner asked for step 1 of the bench evening to be done
for him. **Compile only. Nothing here was flashed, booted or heard.**

| | |
|---|---|
| Commit built | `a2389ba` (main; includes the 2026-10-27 firmware fixes F1 F3 F5 F6) |
| Tool | `arduino-cli` 1.5.2-rc.1, from `downloads.arduino.cc` |
| Core | `esp32:esp32` **3.3.6** (the release machine's version; 3.3.7/3.3.8 boot-loop with BLE) |
| Libraries | Adafruit NeoPixel 1.15.5 · ESP8266Audio 2.4.1 · ArduinoJson 7.4.2 |
| FQBN | `esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc` |
| config.h | a copy of `config.h.example` (placeholder credentials, BLE provisioning on, library defaults); gitignored, not committed |
| Result | **exit 0, 0 warnings**, 1 m 32 s |
| App image | `AregVoiceMvp.ino.bin` 1,632,992 B = **51.9 %** of the 3 MB OTA slot (`app0` 0x300000 in `partitions.csv`) |
| Sketch / RAM | 1,632,775 B program; 199,920 B globals (61 % of 327,680 B), 127,760 B left |
| sha256 | `c1b94d71c8a3bf0262ffa6145a4009318974c93374779ab94fdcddadf6f79aa5` |

The compiler's own "9 % of 16,777,216" line measures against the flash size,
not the OTA slot; the 51.9 % above is the number that matters and matches the
1.3.4 image (1,638,720 B = 52.1 %).

## Release-image gate on this binary: FAIL, as designed

`python3 tools/firmware/check_release_image.py <bin>` refuses this image:
`AREG_MANIFEST_HMAC_KEY` is empty in `config.h.example`, so the
`OTA_SIG_CHECK_DISABLED` marker is compiled in. That is the gate working. This
build is a compile check, not a release image; the owner's real `config.h`
carries the signing key. Placeholders `YOUR_DEVICE_GUID` / `YOUR_DEVICE_API_KEY`
were present (no real credential in the image).

## NOT verified

- Boot, flash, any runtime behaviour. F1 F3 F5 F6 are compiled, not exercised;
  their bench steps are in `esp32/AregVoiceMvp/README.md`.
- The owner's real `config.h` (his flags and Wi-Fi/OTA settings may change size
  slightly; a bench flag left in it can still eat the button).
- ESP8266Audio's version was never pinned in the README; 2.4.1 is what
  Library Manager gave today.

## Reproduce

`docs/container-toolchain.md` § "Firmware compile".

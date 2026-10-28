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

## Flashed on the owner's PC (2026-10-29, local night) — reported by the PC session

Relayed from the Claude session on the owner's PC; not independently verified from the cloud.

| | |
|---|---|
| Source | local main after merging origin/main `a2389ba` (merge `b33e148`; the one local-only commit `cb7c4e7` is docs) |
| Toolchain | arduino-cli 1.4.1 (PC); core 3.3.6 (the script refuses 3.3.7/3.3.8 with BLE on) |
| Label | `fw=1.3.5-bench build=bench-20261029` (set by `bench_session.sh --label`) |
| Compile | 1,649,487 B = **52.4 %** of the 3 MB slot; globals 200,360 B (61 %) |
| Upload | COM7, "hash of data verified", hard reset by RTS, script printed "flashed" |
| Config | the owner's real `config.h`: `AREG_QA_STREAM_PLAYBACK` OFF (first flash = regression build); `AREG_PROVISION_WIFI_ONCE` and `_FORCE` left ON (no-ops with Wi-Fi already saved) |
| SD card | not loaded by the script; the toy syncs over Wi-Fi about 180 s after boot |

An earlier flash the same night used a stale checkout (no `--label`, streaming flag ON, none of the
2026-10-27 fixes); it is superseded by this one. **Still not verified: any runtime behaviour.**

## Reproduce

`docs/container-toolchain.md` § "Firmware compile".

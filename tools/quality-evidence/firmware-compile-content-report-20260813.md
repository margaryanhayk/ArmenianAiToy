# Firmware compile verification — the content report (2026-08-13)

The content-report firmware (`content_report.{h,cpp}`, `content_report_rules.h`,
the rewritten heartbeat body in `voice_client.cpp`) had never been compiled when
it was written: the container had no Arduino toolchain and the downloads were
blocked at the proxy. The owner opened `downloads.arduino.cc`,
`espressif.github.io` and `dl.espressif.com`, and this is the result.

## Toolchain

| | |
|---|---|
| arduino-cli | 1.5.2-rc.1 |
| ESP32 core | **esp32:esp32@3.3.8** — the version CLAUDE.md records as verified for the OTA rollback path |
| ArduinoJson | 7.4.3 |
| ESP8266Audio | 2.4.1 |
| Adafruit NeoPixel | 1.15.5 |

Built in a COPY of the sketch, with `config.h` from `config.h.example`, so the
repo gained neither a `config.h` nor build output.

```
arduino-cli compile \
  --fqbn "esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc" \
  --build-property "compiler.cpp.extra_flags=-DAREG_CONTENT_SYNC_BENCH" \
  <sketch>
```

## It compiles

First attempt, no errors. A second pass adding `-Wall -Wextra` produces **zero
warnings** from `content_report.cpp` or from the rewritten heartbeat in
`voice_client.cpp`.

## What the change costs

Measured against a build of the SAME tree at `a95b27a^` — the commit before the
content report — with identical flags and toolchain. An absolute figure would
have been meaningless; only the delta is evidence.

| | before | after | delta |
|---|---|---|---|
| Flash (`.ino.bin`) | 1,615,518 | 1,619,242 | **+3,724 B** |
| Static RAM (globals) | 189,608 | 190,280 | **+672 B** |

+672 B is the two 320-byte buffers in `content_report.cpp` plus the handful of
ints — i.e. what was predicted when the size was chosen, and nothing
unaccounted for.

51.5% of the 3,145,728 B OTA slot, 1,526,486 B free. (`arduino-cli` prints
"Maximum is 16777216" under a custom partition scheme; that is its own display
quirk, not the slot. `partitions.csv` defines two 3 MB app slots.)

## The image built here is NOT the release image

A 1.2.1 image was built (`-DAREG_FW_VERSION="1.2.1"`), verified to contain only
`YOUR_DEVICE_GUID` / `YOUR_DEVICE_API_KEY` placeholders and zero real GUIDs, and
then **deliberately not staged**.

The reason is a gap that source cannot explain:

| | bytes |
|---|---|
| Field 1.2.0 (`firmware/areg-current.bin`) | 1,297,904 |
| This toolchain, same source minus the content report | 1,615,518 |
| **Unexplained** | **317,614** |

`git diff 64a6957..HEAD -- esp32/` — 64a6957 being the commit that shipped
1.2.0 — contains nothing but the content report itself and a 14-line
`offline_quiz.cpp` change. So ~318 KB comes from the toolchain, not the code:
different core or library versions than the machine that built the field image.

Shipping a binary whose size differs from the field one by a quarter of its own
size, for a reason nobody has established, onto a children's toy that has
already rolled back twice in the field, is not a defensible act. **The release
image should be built on the machine that built 1.2.0**, with the runbook's
command. The compile evidence above stands on its own: the code is correct C++,
it fits, and its cost is known.

## Still not done

Compiling is not bench-running. Nothing here proves the toy reads its card,
sends the report, or that the dashboard changes. That needs the toy.

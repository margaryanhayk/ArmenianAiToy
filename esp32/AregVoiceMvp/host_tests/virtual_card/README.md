# Virtual card: the firmware's SD logic on a PC

Runs the toy's real story-selection and index-reading code against an
in-memory SD card, so card trouble can be tested without a toy, a cable or a
bench evening. Nothing in here is a copy of firmware logic: the runner stages
the sketch sources into a scratch folder and replaces only the hardware edge
(SD, flash storage, the Arduino core).

```bash
python3 esp32/AregVoiceMvp/host_tests/virtual_card/run_virtual_card.py            # the virtual card
python3 esp32/AregVoiceMvp/host_tests/virtual_card/run_virtual_card.py --host-tests   # the plain host tests
```

`tools/release/preflight.sh` runs both (`firmware-virtual-card`,
`firmware-host-tests`). Exit 0 = pass, 1 = fail, 77 = skipped because there is
no C++ compiler or no ArduinoJson 7 (a SKIP, not a failure).

## What it runs

| Program | What it is |
|---|---|
| `story_select_selftest` | `story_select_test.cpp`, the on-device selection suite, unchanged |
| `content_sync_selftest` | `content_sync_test.cpp`, the on-device content-sync suite, unchanged |
| `virtual_card` | `virtual_card_test.cpp`: the real `content_index_io.cpp`, `story_select.cpp`, `content_report.cpp` and `content_sync_model.cpp` on a fake card |

The virtual card covers: a healthy 43 KB index (16 stories, 42 voice, 104
games, 4 music, the card the bench toy reported); a short read that succeeds on
a retry; reads that keep failing and fall back to the previous copy
(`content_index.json.prev`); a truncated or missing primary; no card; the
five-second memo that stops a dying card being hammered; the repair content
sync does; a parent's "intro off" surviving a bad read; and SHIP A6 (no
back-to-back repeat across 60 simulated power cycles for 2 to 5 stories).

## What you need

- g++ or clang++, or on Windows the Visual Studio C++ Build Tools (found
  automatically).
- ArduinoJson 7: `$ARDUINOJSON_DIR` (the folder holding `ArduinoJson.h`) or the
  usual `~/Documents/Arduino/libraries/ArduinoJson` folder.

## What it does not cover

Real SPI timing, a real dying card, the roughly 90 KB of heap the toy really
has, the audio a child hears, and the state machine in the `.ino`. Those need
the bench.

## Why a stand-in layer exists

`stubs/` holds the stand-ins (`Arduino.h`, `SD.h`, `Preferences.h`, ...) and
`overlay/audio_io.h` replaces the real audio header inside the staged copy. The
fake SD can truncate a file, make reads stop part-way (a dying card), or make
the next N opens read only half (a flaky one). `delay()` advances a clock
instead of sleeping, so retry gaps and memo windows behave as on the toy.

The real `config.h` is never read: `config.h.example` is staged in its place.

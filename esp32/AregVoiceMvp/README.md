# AregVoiceMvp — C1 bench firmware

First firmware slice for the Armenian AI Toy voice loop. One
ESP32-S3 dev board on a breadboard presses the backend's
already-shipped `POST /api/chat/audio` endpoint end-to-end:

```
button press-hold   →  INMP441 records PCM @ 16 kHz mono
button release      →  WAV header + PCM POST to /api/chat/audio
backend returns MP3 →  buffered fully in PSRAM
minimp3 decodes     →  I²S out → MAX98357A → speaker
```

Five LED states (IDLE / RECORDING / UPLOADING / PLAYING /
ERROR). One flash-embedded Armenian failure clip on any error.
One serial latency print per successful turn.

This is a **bench prototype**. No wake word, no barge-in, no
retries, no battery, no enclosure, no provisioning UX, no OTA.
Those are later-phase concerns per the toy-mvp scope guard.

## Hardware assumptions

Defaults target an **ESP32-S3-DevKitC-1** (N8R8 or N16R8 —
PSRAM required for the 480 KB capture + 512 KB response
buffers). Other S3 boards work; adjust pin numbers in
`config.h`.

| Component      | Part        | Pin (config.h)          |
|----------------|-------------|-------------------------|
| Mic (I2S RX)   | INMP441     | BCK=4 / WS=5 / SD=6     |
| Mic L/R select | INMP441 L/R | **tie to GND**          |
| Amp (I2S TX)   | MAX98357A   | BCK=15 / LRC=16 / DIN=7 |
| Button to GND  | tactile     | 0 (BOOT)                |
| LED (WS2812)   | onboard     | 48                      |

The firmware reads only the left I2S slot
(`I2S_CHANNEL_FMT_ONLY_LEFT` in `audio_io.cpp`), so the
INMP441's L/R pin must be tied to GND. Floating L/R produces
silence or noise that looks like a working capture.

Wire all three ground returns (board, mic, amp) to the same
ground rail — shared ground is the most common wiring mistake
on this kind of bench.

**Wi-Fi must be 2.4 GHz.** ESP32-S3 has no 5 GHz radio. If your
router exposes one merged SSID across both bands and prefers
5 GHz for new clients, the board can sit in
`[wifi] connecting...` indefinitely on otherwise-correct
credentials. Move the bench AP to a 2.4 GHz-only SSID for
bring-up.

**BOOT button is the press-to-talk button.** GPIO 0 is wired to
the BOOT button on DevKitC-1, so don't hold it while resetting
(you'll land in flash-mode); use the EN/RESET button for a clean
restart instead.

## Arduino IDE setup

- **Board**: "ESP32S3 Dev Module"
- **PSRAM**: "OPI PSRAM" (or "QSPI PSRAM" depending on your
  board variant — required either way)
- **Partition Scheme**: default "Default 4MB with spiffs" is
  fine
- **Flash Size**: set to match your chip — "8MB (64Mb)" for
  N8R8, "16MB (128Mb)" for N16R8. The Arduino IDE does not
  detect this automatically.
- **USB CDC On Boot**: Enabled (so Serial Monitor works over
  the native USB port)
- **Libraries (via Library Manager)**:
  - `Adafruit NeoPixel` by Adafruit
  - `ESP8266Audio` by Earle Philhower — version not pinned yet;
    record the exact installed version in this README after the
    first successful C3.1 bench compile so future bench machines
    can reproduce the build.

No other libraries. `WiFi`, `HTTPClient`, `driver/i2s.h`, and
`esp_heap_caps.h` all ship with the ESP32 Arduino core.

## First-run provisioning (one-time)

1. Start the backend on your dev laptop. Device registration is FAIL-CLOSED by
   default (#009), so a dev/bench host must opt into open registration:
   ```
   cd backend
   # bash / git-bash:
   export Devices__AllowOpenRegistration=true
   dotnet run --project src/ArmenianAiToy.Api
   ```
   In PRODUCTION do NOT enable open registration — instead set
   `Devices:ProvisioningSecret` and send it in the `X-Provisioning-Secret`
   header on the register call below.
2. Find your laptop's LAN IP (the ESP32-S3 must be on the same
   network).
3. Register one device against the backend and save the
   returned `DeviceId` + `ApiKey`:
   ```
   curl -s -X POST http://<laptop-ip>:5000/api/devices/register \
     -H 'Content-Type: application/json' \
     -d '{"macAddress":"bench-01"}'
   ```
   > With a prod provisioning secret set, add
   > `-H 'X-Provisioning-Secret: <secret>'`. Registering an ALREADY-registered
   > MAC is REFUSED with 409 and the device keeps its key (#011); to
   > deliberately rotate a lost/compromised key, re-send the request with
   > `-H 'X-Force-Rotate: true'`.
4. Copy the four values into `config.h`:
   - `AREG_WIFI_SSID` / `AREG_WIFI_PASSWORD`
   - `AREG_BACKEND_URL` (e.g. `http://192.168.1.100:5000/api/chat/audio`
     — pick a free port; pass `--urls http://0.0.0.0:5050` to
     `dotnet run` if `:5000` is already in use, and match the
     port in the URL above)
   - `AREG_DEVICE_ID` / `AREG_DEVICE_API_KEY`

   > **Do not commit `config.h` after pasting real credentials.**
   > The file is tracked (so pin/timing constants ship with the
   > repo) and `.gitignore` does not exclude it. Run
   > `git diff --staged -- esp32/AregVoiceMvp/config.h` before
   > every commit and confirm it prints nothing. Optionally,
   > mark the file skip-worktree for the duration of bench work:
   >
   > ```
   > git update-index --skip-worktree esp32/AregVoiceMvp/config.h
   > # revert later with --no-skip-worktree
   > ```

## Render the failure clip (optional, one-time per voice change)

This step is **optional**. It calls OpenAI's TTS API and **costs
OpenAI credits** every time you run it. Skip it if you already
have a non-stub `canned_clip.h`, or if you accept silent error
paths during bench bring-up — the firmware logs
`[fail] canned clip stub is empty; skipping playback` and
returns to idle cleanly.

The committed `canned_clip.h` is a 1-byte stub — the firmware
plays silence on error paths until you regenerate it.

```
cd esp32/AregVoiceMvp
export OPENAI_API_KEY=sk-...
./tools/render_canned_clip.sh
```

This calls OpenAI TTS (tts-1 / Nova — same voice identity as
the backend's runtime responses) and rewrites `canned_clip.h`
as a PROGMEM byte array. Rebuild + reflash after running it.

## Build + flash

Open `AregVoiceMvp.ino` in the Arduino IDE. Hit Upload. Open
Serial Monitor at 115200 baud.

You should see (matches `setup()` output in `AregVoiceMvp.ino`):
```
[boot] AregVoiceMvp starting
[boot] backend=http://<your-host>:<port>/api/chat/audio
[boot] pins button=0 led=48
[boot] mic_i2s bck=4 ws=5 sd=6
[boot] amp_i2s bck=15 lrc=16 din=7
[wifi] connecting to <your SSID> ...
[wifi] ip=192.168.1.X
[boot] ready — press button to speak
[state] 0 -> 0
```

### arduino-cli — correct FQBN (avoids the false 96–97% flash alarm)

This sketch ships a custom **8 MB dual-OTA `partitions.csv`** with **3 MB
OTA app slots**. Always build/upload with `FlashSize=8M` and
`PartitionScheme=custom` so the flash-size check matches the real
partition table:

```
esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc
```

**⚠️ Do NOT use `PartitionScheme=default` for this project.** The default
scheme reports a **1.25 MB** app slot (0x140000) and makes the toolchain
measure firmware against the wrong ceiling — that is the sole source of
the false "96–97% of program storage" alarm. Built correctly, the
production image is ~**1,264,539 bytes ≈ 40%** of the real **3 MB** slot,
with ~1.88 MB free per slot.

Production compile (flag-off):
```
arduino-cli compile --fqbn "esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc" ".\esp32\AregVoiceMvp"
```

Upload:
```
arduino-cli upload -p COM7 --fqbn "esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc" ".\esp32\AregVoiceMvp"
```

Content-sync bench (compile + upload, `-DAREG_CONTENT_SYNC_BENCH`):
```
arduino-cli compile --upload -p COM7 --fqbn "esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc" --build-property "compiler.cpp.extra_flags=-DAREG_CONTENT_SYNC_BENCH" ".\esp32\AregVoiceMvp"
```

SD diagnostic bench (compile + upload, `-DAREG_SD_DIAG_BENCH`):
```
arduino-cli compile --upload -p COM7 --fqbn "esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc" --build-property "compiler.cpp.extra_flags=-DAREG_SD_DIAG_BENCH" ".\esp32\AregVoiceMvp"
```

> Adjust `-p COM7` to your serial port. Production builds must define
> **neither** bench flag — each `-DAREG_*_BENCH` module compiles to zero
> bytes without its flag.
>
> The custom partition table has **3 MB OTA app slots**, so SD MP3
> playback can proceed **without any partition migration**.

## Bench demo

1. Press and hold the BOOT button. LED turns red.
2. Speak one Armenian sentence — e.g. *"Պատմիր հեքիաթ"* —
   then release. LED turns yellow.
3. Within a few seconds, LED turns green and the speaker plays
   Areg's Armenian response.
4. Press the button again and say *"Ա"* — the story continues.
5. Repeat once more. Three-turn success is the C1 exit
   condition.

On any failure (Wi-Fi down, backend unreachable, non-200
response, decoder error), LED turns orange and the canned
Armenian failure clip plays once before the device returns to
idle. The next press works normally.

## What to look at in the serial log

Every turn emits:
- `[state] N -> M` lines for each transition
- `[cap] samples=...` — size of the captured PCM
- `[voice] http 200, body=N bytes (psram)` — upload + response
  read succeeded
- `[latency] release->play_begin_ms=N` — wall-clock from
  button-release to first audio byte out of the speaker

C1 latency target: **≤ 7 s perceptual**. Good: **≤ 4 s**. If
you are consistently above 7 s, stop adding features and
profile the longest stage.

## Known C1 limitations (deliberate, deferred)

- Buffered response playback — streaming comes later.
- One attempt per turn. No retry, no reconnect. A dropped
  Wi-Fi packet mid-upload trips the error clip.
- Button presses during UPLOADING / PLAYING / ERROR are
  ignored. No barge-in.
- Plain HTTP only. No TLS on the bench LAN.
- Wi-Fi SSID / password / device credentials compiled in.
- One device only. One voice identity.
- Amp output gain hardcoded conservative; raise in
  `audio_io.cpp`'s `SetGain` call if the speaker is too quiet.

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

| Component      | Part       | Pin (config.h)       |
|----------------|------------|----------------------|
| Mic (I2S RX)   | INMP441    | BCK=4 / WS=5 / SD=6  |
| Amp (I2S TX)   | MAX98357A  | BCK=15 / LRC=16 / DIN=7 |
| Button to GND  | tactile    | 0 (BOOT)             |
| LED (WS2812)   | onboard    | 48                   |

Wire all three ground returns (board, mic, amp) to the same
ground rail — shared ground is the most common wiring mistake
on this kind of bench.

## Arduino IDE setup

- **Board**: "ESP32S3 Dev Module"
- **PSRAM**: "OPI PSRAM" (or "QSPI PSRAM" depending on your
  board variant — required either way)
- **Partition Scheme**: default "Default 4MB with spiffs" is
  fine
- **USB CDC On Boot**: Enabled (so Serial Monitor works over
  the native USB port)
- **Libraries (via Library Manager)**:
  - `Adafruit NeoPixel` by Adafruit
  - `ESP8266Audio` by Earle Philhower

No other libraries. `WiFi`, `HTTPClient`, `driver/i2s.h`, and
`esp_heap_caps.h` all ship with the ESP32 Arduino core.

## First-run provisioning (one-time)

1. Start the backend on your dev laptop:
   ```
   cd backend
   dotnet run --project src/ArmenianAiToy.Api
   ```
2. Find your laptop's LAN IP (the ESP32-S3 must be on the same
   network).
3. Register one device against the backend and save the
   returned `DeviceId` + `ApiKey`:
   ```
   curl -s -X POST http://<laptop-ip>:5000/api/devices/register \
     -H 'Content-Type: application/json' \
     -d '{"macAddress":"bench-01","name":"AregBench"}'
   ```
4. Copy the four values into `config.h`:
   - `AREG_WIFI_SSID` / `AREG_WIFI_PASSWORD`
   - `AREG_BACKEND_URL` (e.g. `http://192.168.1.100:5000/api/chat/audio`)
   - `AREG_DEVICE_ID` / `AREG_DEVICE_API_KEY`

## Render the failure clip (one-time per voice change)

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

You should see:
```
[boot] AregVoiceMvp starting
[wifi] connecting to <your SSID> ...
[wifi] ip=192.168.1.X
[boot] ready — press button to speak
[state] 0 -> 0
```

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

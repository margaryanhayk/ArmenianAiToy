# Areg toy -- virtual board (Wokwi simulator)

The whole bench toy, wired virtually: ESP32-S3-DevKitC-1, MAIN button on
GPIO18, YES/NO buttons on 21/47, microSD on SPI 10/11/12/13, volume pot on
GPIO8, onboard RGB LED on 48. The I2S mic (4/5/6) and amp (15/16/7) have no
Wokwi part -- audio is the one thing this board cannot test.

## Run it
1. Install VS Code + the "Wokwi Simulator" extension (free license via
   wokwi.com).
2. Build the sim image (from esp32/AregVoiceMvp/):
       cp config.h config.h.hwbackup
       cp wokwi/config-wokwi.h config.h
       arduino-cli compile --fqbn "esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc" --build-path build_wokwi .
       cp config.h.hwbackup config.h && rm config.h.hwbackup
3. Open this folder in VS Code, open wokwi.toml, press F1 ->
   "Wokwi: Start Simulator".

## What to expect
- Boots, joins Wokwi-GUEST (the simulator's open Wi-Fi), reaches the real
  Railway backend over TLS.
- Device identity is a PLACEHOLDER -- device-authed calls (heartbeat,
  content sync) get 401 in the sim. That is deliberate: a shared sim must
  never carry a real device key (a leaked key = someone else's toy).
- The microSD starts with no image attached; SD.begin fails and the toy
  runs in Wi-Fi-only mode. To test SD flows, add an "image" attr to the
  sd part in diagram.json pointing at a FAT-formatted .img file.

## What this board can and cannot test
CAN:  boot flow, state machine, buttons (press/hold menu), story
      selection logic, serial diagnostics, crashes/heap issues.
CANNOT: sound (no I2S parts), SD data corruption, brownouts, loose
      wires, GPIO0 strapping faults, BLE provisioning. Every fault we
      chased on the real bench this week was in the CANNOT column --
      the simulator complements the bench, it does not replace it.

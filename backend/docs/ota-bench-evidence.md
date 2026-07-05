# OTA Bench Evidence — real ESP32-S3 hardware

Status: **real OTA apply verified on hardware** (happy path + two negative
tests). Remaining planned tests are listed at the bottom — deliberately NOT
run yet. This file is the durable record of what was actually observed on
the bench, so later claims ("OTA works") trace to evidence.

Bench window: 2026-07-03 → 2026-07-05.
Device: ESP32-S3 DevKitC-1 (16 MB flash, octal PSRAM), bench device id
`017c0f71-c52e-418c-8288-b02d402f70ed`. Firmware branch: `feat/ota-apply`.
Partition: custom 8 MB dual-OTA table (`esp32/AregVoiceMvp/partitions.csv`,
two 3 MB slots `app0`/`app1` + `otadata`); the 16 MB physical flash simply
leaves the upper half unused. Stage A transport: HTTP LAN (integrity via
HMAC-signed manifest + sha256 — production TLS is the separate Stage B).
Note: the bench moved networks mid-window (backend `192.168.1.4` →
`192.168.1.11`; device re-flashed 1.0.1 over USB after the move) — none of
the results below depend on the specific LAN.

## 1. Foundation skeleton proof — PASSED (2026-07-03)

Phone-home loop end to end, before any real apply existed:

```
[heartbeat] status=200 (fw=1.0.0)
[ota] 1 command(s) pending
[ota] command id=38f45bf0-6b87-4fbd-a4ba-89fddd9f437f type=firmware_update expiresAt=2026-07-03T00:27:58.7233066
[ota] manifest: no update (running 1.0.0)
[ota] ack 38f45bf0-6b87-4fbd-a4ba-89fddd9f437f result=ok status=200
```
DB: `firmware_update | Acked | ok | 1.0.0 | {"status":"manifest_checked","updateAvailable":false}`.
Heartbeat firmware report (version/build/board/partition/lastOtaStatus)
stored on the Device row.

## 2. Real OTA happy path 1.0.0 → 1.0.1 — PASSED

Full pipeline: manifest (HMAC signature verified) → streaming download →
incremental sha256 → Arduino `Update` into the inactive slot → native image
validation + boot-partition switch → reboot into **pending-verify** →
post-reboot check-in ack (from persisted NVS command id) → only after the
2xx ack: `esp_ota_mark_app_valid_cancel_rollback()`.

Evidence (DB after success):

| field | value |
|---|---|
| Devices.FirmwareVersion | `1.0.1` |
| Devices.PartitionName | `app1` (switched from `app0`) |
| Devices.LastOtaStatus | `confirmed` |
| Command Id | `0450F3ED-2DF9-47CC-A6B9-DD047BCD2978` |
| Command Status / Result | `Acked` / `ok` |
| AckFirmwareVersion | `1.0.1` |
| AckDiagnosticsJson | `{"status":"ota_applied","version":"1.0.1","partition":"app1"}` |

This also confirms on real hardware that the Arduino core 3.3.8 prebuilt
bootloader honors pending-verify/rollback
(`CONFIG_BOOTLOADER_APP_ROLLBACK_ENABLE=y` — was design-verified from the
sdkconfig, now hardware-observed).

## 3. Negative test: bad sha256 — PASSED (no reboot, no brick)

Backend offered `1.0.2` with a deliberately wrong `Sha256`. Device
downloaded the full image, hash-verified BEFORE finalizing, refused, never
switched the boot partition, and kept working:

```
[ota] sha256 MISMATCH (...) — NOT applying
[ota] ack 7f4d65ba-ca7e-43ec-b6a0-3144b1205edc result=failed status=200
[heartbeat] status=200 (fw=1.0.1)
```
DB: Device still `1.0.1`; `LastOtaStatus = failed:sha256_mismatch`;
command `7F4D65BA…` = `Failed / failed / sha256_mismatch / AckFirmwareVersion 1.0.1`.
NVS terminal-state guard means the same command id can only re-ack, never
re-download.

## 4. Negative test: wrong boardModel (server-side gate) — PASSED

Backend `FirmwareUpdate:BoardModel` set to `wrong-board-model`; device
(reporting `areg-s3-n8`) was simply not offered the release:

```
[ota] manifest: no update (running 1.0.1)
[ota] ack 9500a9c2-e814-4eef-85d4-06880d56d32a result=ok status=200
```
DB: command `9500A9C2…` = `Acked / ok / 1.0.1 /
{"status":"manifest_checked","updateAvailable":false}`.
(The DEVICE-side board gate is defense-in-depth and is not reachable
through the real backend — both gates read the same field; it stays
covered by code review, not a network bench.)

## Known caveat + TODO (dashboard honesty)

**Caveat observed on the bench:** after the bad-sha test, the device's
`LastOtaStatus` stays `failed:sha256_mismatch` even though a LATER
`firmware_update` check succeeded (test 4 acked ok) and heartbeats are
healthy. The field reports the last **attempt outcome**, not current
device health — by design, but it reads scary on an operator/parent
surface forever.

**TODO (future slice, not started):** either (a) a successful no-update
manifest check / confirmed apply should clear a stale `failed:*` status,
or (b) split the surface into `lastOtaAttempt` (sticky, diagnostic) vs a
derived current-health field, so a dashboard never shows a red
`failed:sha256_mismatch` on a perfectly healthy, up-to-date toy. Decide
shape when the operator console gains a firmware view.

## Deliberately NOT yet run (planned, in order)

1. **Poison/dead-backend rollback test** — build 1.0.3 pointing at an
   unroutable backend; expect check-in deadline (5 min) →
   self-invalidate → bootloader rollback → old image acks
   `failed/rollback_no_checkin`. The rollback *mechanism* is
   hardware-confirmed (pending-verify observed in test 2); this test
   exercises the deadline path end to end.
2. Corrupted-image (sha-valid) test — `Update.end()`/native validation
   layer.
3. Stage B: pinned-CA HTTPS (`ota_http_begin()` in `ota_apply.cpp` is the
   single transport seam).
4. Cloud→SD MP3 story sync (Feature 1 body) — separate slice.

## Bench config notes (for reproducing)

- Device `config.h` (local, gitignored) carries `AREG_FW_VERSION`,
  `AREG_BOARD_MODEL "areg-s3-n8"`, and `AREG_MANIFEST_HMAC_KEY` equal to
  the backend `FirmwareUpdate:SigningKey`.
- Backend bench block lives in `appsettings.Development.json`
  (kept LOCAL/uncommitted — it carries the bench HMAC key and machine
  paths). After the bench it was left with `Enabled=false`.
- Version-toggle builds: edit the one `AREG_FW_VERSION` line, compile with
  `--output-dir`, serve `AregVoiceMvp.ino.bin` (the APP image — never
  `merged.bin`) via `FirmwareUpdate:ImagePath`.

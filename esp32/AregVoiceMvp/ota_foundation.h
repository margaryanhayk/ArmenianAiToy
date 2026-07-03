// -------------------------------------------------------------
// AregVoiceMvp / ota_foundation.h — OTA foundation SKELETON (Proof 2)
//
// The device-side half of the backend OTA contract:
//   * firmware identity reported on the presence heartbeat,
//   * command poll  (GET  /api/devices/commands),
//   * command ack   (POST /api/devices/commands/{id}/ack),
//   * manifest check (GET /api/devices/firmware-manifest).
//
// DELIBERATELY NOT IN THIS SLICE: no firmware download, no flash write,
// no esp_https_ota apply, no Secure Boot/eFuse, no SD story sync. A
// `firmware_update` command only CHECKS the manifest, logs what it would
// do, and acks — proving the phone-home loop end to end without touching
// the flash.
//
// The device is OUTBOUND-ONLY: it polls over its authenticated HTTPS/HTTP
// client connection; there is no inbound server on the toy.
// -------------------------------------------------------------
#pragma once

#include <Arduino.h>
// config.h MUST be consumed before the #ifndef defaults below, or a
// translation unit that includes this header first (e.g. ota_foundation.cpp,
// whose own-header-first include order is idiomatic) would silently compile
// with the DEFAULT version while other units use the config.h override —
// and the post-OTA check-in compares versions across those units.
#include "config.h"

// --- Firmware identity (override from config.h / build flags) ---
// AREG_FW_VERSION is the plain MAJOR.MINOR.PATCH the backend's
// FirmwareVersionComparer understands; bump it on every release build.
#ifndef AREG_FW_VERSION
#define AREG_FW_VERSION   "1.0.0"
#endif
// Free-form build tag (date / CI id) — informational only.
#ifndef AREG_FW_BUILD
#define AREG_FW_BUILD     "dev"
#endif
// Board identity used by the backend's board gate: a release configured
// with FirmwareUpdate:BoardModel is only offered to matching boards.
#ifndef AREG_BOARD_MODEL
#define AREG_BOARD_MODEL  "areg-s3-n8"
#endif

// Label of the partition we are currently RUNNING from ("app0"/"app1" on
// an OTA table). Reported on the heartbeat so the fleet view shows which
// slot each device booted. Safe on any partition scheme.
const char *ota_running_partition_label();

// Call every loop() iteration while IDLE (never during a voice turn).
// Cheap no-op when Wi-Fi is down or between intervals. Behavior:
//   * first call with Wi-Fi up  -> immediate boot poll,
//   * then                      -> re-poll every AREG_HEARTBEAT_INTERVAL_MS.
// Each poll: fetch this device's pending commands, dedup by command id,
// handle known types (firmware_update -> manifest check only), ack each.
void ota_foundation_tick();

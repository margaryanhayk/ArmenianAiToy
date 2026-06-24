// -------------------------------------------------------------
// AregVoiceMvp / ble_provisioning.h   (Phase B.2)
//
// BLE Wi-Fi provisioning: the parent's phone connects to the toy over
// Bluetooth LE and hands it the home Wi-Fi name + password (encrypted,
// proof-of-possession = the toy's pairing code). The received credentials
// are persisted to NVS via the Phase B.1 seam, so the toy remembers the
// network across reboots and can be re-onboarded for a new router without
// re-flashing.
//
// GATED behind AREG_USE_BLE_PROVISIONING. The default (bench) build does NOT
// define it, so none of this is compiled and the firmware is byte-identical
// to B.1. The BLE stack does not fit the `default` partition — the flag-on
// build MUST be compiled/flashed with PartitionScheme=huge_app. See
// PLATFORM-ARCHITECTURE.txt (Phase B.2 build spec) and config.h.example.
// -------------------------------------------------------------
#pragma once

#ifdef AREG_USE_BLE_PROVISIONING

#include <Arduino.h>

// Start BLE provisioning. Advertises a BLE service (AREG_PROV_SERVICE_NAME);
// the parent app connects with the proof-of-possession (AREG_PROV_POP) and
// sends the home Wi-Fi SSID + password encrypted over BLE. On receipt the
// creds are persisted to NVS (B.1) and the provisioning manager connects the
// STA to validate them. Non-blocking: returns immediately; the manager runs
// in the background and surfaces progress via the serial log.
void ble_provisioning_begin();

// True while a provisioning session is advertising / in progress (i.e. no
// credentials accepted yet). Lets the main loop reflect "setup mode" if it
// wants to; not required for correctness.
bool ble_provisioning_active();

#endif  // AREG_USE_BLE_PROVISIONING

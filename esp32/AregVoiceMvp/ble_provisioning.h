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
// GATED behind AREG_USE_BLE_PROVISIONING, which is ON as of 2026-08-16 — see
// the note in config.h. With the flag off none of this is compiled and the
// firmware is byte-identical to B.1.
//
// CORRECTED 2026-08-16. This header used to say the BLE stack "does not fit
// the `default` partition" and that the flag-on build MUST use
// PartitionScheme=huge_app. That was true against the 1.25 MB slots of the
// `default` table and has been stale since this sketch got its own 8 MB
// dual-OTA partitions.csv with 3 MB slots. Measured, flag on, on the shipping
// FQBN: 1,631,423 bytes — 51.9% of the slot. huge_app is not needed and would
// be wrong anyway, because it is OTA-incapable and this toy updates over the
// air. Keep PartitionScheme=custom.
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

// B.3 — true once credentials were accepted and Wi-Fi connected during this
// session (latched). The main loop reboots on this so the new NVS creds take
// effect via the proven B.1 voice_wifi_begin() path on the next boot, instead
// of relying on the provisioning manager's in-session connection persisting.
bool ble_provisioning_succeeded();

#endif  // AREG_USE_BLE_PROVISIONING

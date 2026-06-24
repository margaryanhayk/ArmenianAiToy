// -------------------------------------------------------------
// AregVoiceMvp / wifi_creds.h   (Phase B.1)
//
// Wi-Fi credentials persisted in NVS (flash) via the Arduino Preferences
// library. This is the seam the BLE provisioning layer (Phase B.2) writes to,
// and what lets the toy remember the home network across reboots and be
// re-provisioned (new router) WITHOUT re-flashing.
//
// When NVS holds no credentials, the firmware falls back to the compile-time
// config.h creds (the bench model) — so this change is behavior-neutral until
// a network is actually provisioned. No partition change (uses the existing
// `nvs` partition).
// -------------------------------------------------------------
#pragma once

#include <Arduino.h>

// True iff a non-empty SSID is stored in NVS (i.e. the toy has been
// provisioned with a real network at least once).
bool wifi_creds_present();

// Load stored creds into the caller's buffers. Returns true iff present and
// the SSID is non-empty (in which case both buffers are filled). On false the
// buffers are left untouched and the caller should use the config.h fallback.
bool wifi_creds_load(char *ssid, size_t ssid_cap, char *pass, size_t pass_cap);

// Persist creds to NVS (called by provisioning). A null/empty SSID is ignored
// (we never store an empty network that would brick the next boot's lookup).
void wifi_creds_save(const char *ssid, const char *pass);

// Erase stored creds (factory reset / forces the next boot back to fallback
// or provisioning).
void wifi_creds_clear();

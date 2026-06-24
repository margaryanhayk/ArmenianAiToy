// -------------------------------------------------------------
// AregVoiceMvp / device_creds.h   (Phase C — factory provisioning, toy side)
//
// Device identity (deviceId + backend api key) persisted in NVS (flash),
// burned at manufacture by the factory station. This is the device-key
// analogue of wifi_creds.h: it keeps the per-unit secret OUT of the compiled
// binary so one firmware image flashes to every unit and each gets its own
// identity from NVS.
//
// When NVS holds no device creds, the firmware falls back to the compile-time
// config.h values (AREG_DEVICE_ID / AREG_DEVICE_API_KEY) — the bench model —
// so this change is behavior-neutral until a unit is actually provisioned.
// Uses the existing `nvs` partition (no partition change), same as wifi_creds.
// -------------------------------------------------------------
#pragma once

#include <Arduino.h>

// True iff a non-empty deviceId is stored in NVS (i.e. this unit was
// factory-provisioned with its own identity at least once).
bool device_creds_present();

// Load stored creds into the caller's buffers. Returns true iff present and
// the deviceId is non-empty (in which case both buffers are filled). On false
// the buffers are left untouched and the caller should use the config.h
// fallback.
bool device_creds_load(char *id, size_t id_cap, char *key, size_t key_cap);

// Persist creds to NVS (called by the factory provisioning path). A null/empty
// deviceId is ignored (never store an identity that would brick the next
// boot's lookup).
void device_creds_save(const char *id, const char *key);

// Erase stored creds (factory reset / re-provision).
void device_creds_clear();

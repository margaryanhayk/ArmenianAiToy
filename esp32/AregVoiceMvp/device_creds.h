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
//
// Factory pairing (2026-09-11): the same NVS namespace also carries the
// toy's per-device BLE provisioning PoP, burned alongside the id and key by
// the same factory station write. See ble_provisioning.cpp for the read
// side and its bench-only compile-time fallback.
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

// Load the stored BLE PoP into the caller's buffer. Returns true iff a
// non-empty PoP is stored (in which case the buffer is filled). On false the
// buffer is left untouched and the caller should use its own bench-only
// compile-time fallback — the PoP is independent of device_creds_present():
// an id/key can be provisioned with no PoP stored (older factory run) and
// vice versa is never produced by this module, but callers must not assume
// one implies the other.
bool device_creds_pop_load(char *pop, size_t pop_cap);

// Persist creds to NVS (called by the factory provisioning path). A null/empty
// deviceId is ignored (never store an identity that would brick the next
// boot's lookup). `pop` is optional (nullptr/empty leaves any existing stored
// PoP untouched) — the one-shot bench burn in AregVoiceMvp.ino only passes
// one when AREG_BLE_POP is defined.
void device_creds_save(const char *id, const char *key, const char *pop = nullptr);

// Erase stored creds (factory reset / re-provision). Clears the PoP too.
void device_creds_clear();

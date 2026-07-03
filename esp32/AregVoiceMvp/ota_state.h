// -------------------------------------------------------------
// AregVoiceMvp / ota_state.h — persistent OTA state (NVS)
//
// Why NVS and not RAM: the OTA apply path REBOOTS, wiping RAM. Without
// persisted state, the un-acked firmware_update command is re-delivered
// after reboot (at-least-once transport) and a FAILED/rolled-back update
// would loop forever: apply → fail → rollback → re-deliver → re-apply.
// This module is the cross-reboot memory that breaks that loop and lets
// the NEW image ack the ORIGINAL command id after boot.
//
// Same Preferences/NVS idiom as wifi_creds / device_creds (namespace on
// the existing `nvs` partition; keys ≤ 15 chars).
// -------------------------------------------------------------
#pragma once

#include <Arduino.h>

// Lifecycle of the persisted OTA attempt.
enum OtaState : uint8_t {
    OTA_STATE_IDLE        = 0,  // no attempt in flight
    OTA_STATE_DOWNLOADING = 1,  // download/write started (crash here = failed)
    OTA_STATE_REBOOTING   = 2,  // boot partition switched; reboot issued;
                                //   next boot must check in + confirm
    OTA_STATE_CONFIRMED   = 3,  // new image acked + marked valid
    OTA_STATE_FAILED      = 4,  // pre-reboot failure (gate/download/sha/image)
    OTA_STATE_ROLLED_BACK = 5,  // bootloader returned us to the old image
};

// The full persisted record. cmd ids are GUID strings (36 + NUL).
struct OtaPersist {
    uint8_t state = OTA_STATE_IDLE;
    char    cmd_id[40] = {0};       // lastOtaCommandId — command being applied
    char    pending_ver[16] = {0};  // pendingOtaVersion — version we attempted
    char    last_error[32] = {0};   // lastOtaError — machine-readable reason
    char    applied_cmd[40] = {0};  // lastAppliedFirmwareCommandId — terminal-
                                    //   outcome guard: same id never re-applies
    uint8_t boot_attempts = 0;      // check-in boots while REBOOTING (belt-and-
                                    //   braces beside the millis() deadline)
};

// Load the persisted record (all-defaults when never written).
void ota_state_load(OtaPersist &out);

// Persist the record (overwrites all fields).
void ota_state_save(const OtaPersist &st);

// Human/backend-readable name for a state value ("idle", "rebooting", ...).
const char *ota_state_name(uint8_t state);

// Status string for the heartbeat's lastOtaStatus field: the state name,
// or "name:error" when a failure reason is recorded. Points at a static
// buffer — copy if you need it beyond the next call.
const char *ota_state_status_cstr();

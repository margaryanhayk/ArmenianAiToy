// -------------------------------------------------------------
// AregVoiceMvp / ota_state.cpp — persistent OTA state (NVS)
// See ota_state.h for the contract and the why-NVS rationale.
// -------------------------------------------------------------
#include "ota_state.h"
#include <Preferences.h>

namespace {
// Short namespace + keys (NVS keys are limited to 15 chars).
constexpr const char *kNamespace  = "aregota";
constexpr const char *kStateKey   = "state";
constexpr const char *kCmdKey     = "cmd";
constexpr const char *kVerKey     = "pver";
constexpr const char *kErrKey     = "err";
constexpr const char *kAppliedKey = "acmd";
constexpr const char *kBootsKey   = "boots";
}  // namespace

void ota_state_load(OtaPersist &out) {
    out = OtaPersist{};  // defaults when the namespace has never been written
    Preferences prefs;
    if (!prefs.begin(kNamespace, /*readOnly=*/true)) {
        return;
    }
    out.state = prefs.getUChar(kStateKey, OTA_STATE_IDLE);
    snprintf(out.cmd_id, sizeof(out.cmd_id), "%s",
             prefs.getString(kCmdKey, "").c_str());
    snprintf(out.pending_ver, sizeof(out.pending_ver), "%s",
             prefs.getString(kVerKey, "").c_str());
    snprintf(out.last_error, sizeof(out.last_error), "%s",
             prefs.getString(kErrKey, "").c_str());
    snprintf(out.applied_cmd, sizeof(out.applied_cmd), "%s",
             prefs.getString(kAppliedKey, "").c_str());
    out.boot_attempts = prefs.getUChar(kBootsKey, 0);
    prefs.end();
}

void ota_state_save(const OtaPersist &st) {
    Preferences prefs;
    if (!prefs.begin(kNamespace, /*readOnly=*/false)) {
        Serial.println("[ota] WARNING: NVS open failed; state not persisted");
        return;
    }
    prefs.putUChar(kStateKey, st.state);
    prefs.putString(kCmdKey, st.cmd_id);
    prefs.putString(kVerKey, st.pending_ver);
    prefs.putString(kErrKey, st.last_error);
    prefs.putString(kAppliedKey, st.applied_cmd);
    prefs.putUChar(kBootsKey, st.boot_attempts);
    prefs.end();
}

const char *ota_state_name(uint8_t state) {
    switch (state) {
        case OTA_STATE_IDLE:        return "idle";
        case OTA_STATE_DOWNLOADING: return "downloading";
        case OTA_STATE_REBOOTING:   return "rebooting";
        case OTA_STATE_CONFIRMED:   return "confirmed";
        case OTA_STATE_FAILED:      return "failed";
        case OTA_STATE_ROLLED_BACK: return "rolled_back";
        default:                    return "unknown";
    }
}

const char *ota_state_status_cstr() {
    static char buf[48];
    OtaPersist st;
    ota_state_load(st);
    if (st.last_error[0] != '\0'
        && (st.state == OTA_STATE_FAILED || st.state == OTA_STATE_ROLLED_BACK)) {
        snprintf(buf, sizeof(buf), "%s:%s", ota_state_name(st.state), st.last_error);
    } else {
        snprintf(buf, sizeof(buf), "%s", ota_state_name(st.state));
    }
    return buf;
}

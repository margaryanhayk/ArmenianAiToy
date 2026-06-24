// -------------------------------------------------------------
// AregVoiceMvp / device_creds.cpp   (Phase C — factory provisioning, toy side)
//
// NVS-backed device identity via Arduino Preferences. See device_creds.h for
// the contract. Mirrors wifi_creds.cpp: lightweight, uses the existing `nvs`
// partition, no partition-table change.
// -------------------------------------------------------------
#include "device_creds.h"
#include <Preferences.h>

namespace {
// Short namespace + keys (NVS keys are limited to 15 chars).
constexpr const char *kNamespace = "aregdev";
constexpr const char *kIdKey     = "devid";
constexpr const char *kKeyKey    = "apikey";
}  // namespace

bool device_creds_present() {
    Preferences prefs;
    if (!prefs.begin(kNamespace, /*readOnly=*/true)) {
        return false;  // namespace doesn't exist yet → never provisioned
    }
    const bool has = prefs.getString(kIdKey, "").length() > 0;
    prefs.end();
    return has;
}

bool device_creds_load(char *id, size_t id_cap, char *key, size_t key_cap) {
    Preferences prefs;
    if (!prefs.begin(kNamespace, /*readOnly=*/true)) {
        return false;
    }
    const String i = prefs.getString(kIdKey, "");
    const String k = prefs.getString(kKeyKey, "");
    prefs.end();
    if (i.length() == 0) {
        return false;
    }
    snprintf(id, id_cap, "%s", i.c_str());
    snprintf(key, key_cap, "%s", k.c_str());
    return true;
}

void device_creds_save(const char *id, const char *key) {
    if (id == nullptr || id[0] == '\0') {
        return;  // never persist an empty identity
    }
    Preferences prefs;
    if (!prefs.begin(kNamespace, /*readOnly=*/false)) {
        return;
    }
    prefs.putString(kIdKey, id);
    prefs.putString(kKeyKey, key != nullptr ? key : "");
    prefs.end();
}

void device_creds_clear() {
    Preferences prefs;
    if (!prefs.begin(kNamespace, /*readOnly=*/false)) {
        return;
    }
    prefs.clear();
    prefs.end();
}

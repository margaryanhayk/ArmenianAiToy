// -------------------------------------------------------------
// AregVoiceMvp / device_creds.cpp   (Phase C — factory provisioning, toy side)
//
// NVS-backed device identity via Arduino Preferences. See device_creds.h for
// the contract. Mirrors wifi_creds.cpp: lightweight, uses the existing `nvs`
// partition, no partition-table change.
// -------------------------------------------------------------
#include "device_creds.h"
#include <Preferences.h>
#include "device_creds_rules.h"

namespace {
// Namespace + key names live in device_creds_rules.h (host-testable, single
// source of truth shared with ble_provisioning.cpp).
using device_creds_rules::kNamespace;
using device_creds_rules::kIdKey;
using device_creds_rules::kKeyKey;
using device_creds_rules::kPopKey;
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

bool device_creds_pop_load(char *pop, size_t pop_cap) {
    Preferences prefs;
    if (!prefs.begin(kNamespace, /*readOnly=*/true)) {
        return false;
    }
    const String p = prefs.getString(kPopKey, "");
    prefs.end();
    if (p.length() == 0) {
        return false;
    }
    snprintf(pop, pop_cap, "%s", p.c_str());
    return true;
}

void device_creds_save(const char *id, const char *key, const char *pop) {
    if (id == nullptr || id[0] == '\0') {
        return;  // never persist an empty identity
    }
    Preferences prefs;
    if (!prefs.begin(kNamespace, /*readOnly=*/false)) {
        return;
    }
    prefs.putString(kIdKey, id);
    prefs.putString(kKeyKey, key != nullptr ? key : "");
    // A null/empty pop leaves any previously-stored PoP untouched — the
    // factory station writes id+key+pop together, but the bench one-shot
    // burn (AregVoiceMvp.ino) only has a pop to pass when AREG_BLE_POP is
    // defined, and must not blank a PoP a real factory run already wrote.
    if (pop != nullptr && pop[0] != '\0') {
        prefs.putString(kPopKey, pop);
    }
    prefs.end();
}

void device_creds_clear() {
    Preferences prefs;
    if (!prefs.begin(kNamespace, /*readOnly=*/false)) {
        return;
    }
    prefs.clear();  // clears id, key AND pop — same namespace, same reset
    prefs.end();
}

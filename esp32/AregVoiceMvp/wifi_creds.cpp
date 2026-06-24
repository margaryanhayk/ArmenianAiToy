// -------------------------------------------------------------
// AregVoiceMvp / wifi_creds.cpp   (Phase B.1)
//
// NVS-backed Wi-Fi credential storage via Arduino Preferences. See
// wifi_creds.h for the contract. Lightweight — Preferences uses the existing
// `nvs` partition, so no partition-table change and negligible flash impact.
// -------------------------------------------------------------
#include "wifi_creds.h"
#include <Preferences.h>

namespace {
// Short namespace + keys (NVS keys are limited to 15 chars).
constexpr const char *kNamespace = "aregwifi";
constexpr const char *kSsidKey   = "ssid";
constexpr const char *kPassKey   = "pass";
}  // namespace

bool wifi_creds_present() {
    Preferences prefs;
    if (!prefs.begin(kNamespace, /*readOnly=*/true)) {
        return false;  // namespace doesn't exist yet → never provisioned
    }
    const bool has = prefs.getString(kSsidKey, "").length() > 0;
    prefs.end();
    return has;
}

bool wifi_creds_load(char *ssid, size_t ssid_cap, char *pass, size_t pass_cap) {
    Preferences prefs;
    if (!prefs.begin(kNamespace, /*readOnly=*/true)) {
        return false;
    }
    const String s = prefs.getString(kSsidKey, "");
    const String p = prefs.getString(kPassKey, "");
    prefs.end();
    if (s.length() == 0) {
        return false;
    }
    snprintf(ssid, ssid_cap, "%s", s.c_str());
    snprintf(pass, pass_cap, "%s", p.c_str());
    return true;
}

void wifi_creds_save(const char *ssid, const char *pass) {
    if (ssid == nullptr || ssid[0] == '\0') {
        return;  // never persist an empty network
    }
    Preferences prefs;
    if (!prefs.begin(kNamespace, /*readOnly=*/false)) {
        return;
    }
    prefs.putString(kSsidKey, ssid);
    prefs.putString(kPassKey, pass != nullptr ? pass : "");
    prefs.end();
}

void wifi_creds_clear() {
    Preferences prefs;
    if (!prefs.begin(kNamespace, /*readOnly=*/false)) {
        return;
    }
    prefs.clear();
    prefs.end();
}

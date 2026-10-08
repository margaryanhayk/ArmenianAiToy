// -------------------------------------------------------------
// AregVoiceMvp / secure_store.cpp -- see secure_store.h for the contract and
// docs/firmware-security.md section 3 for why the store is a separate
// partition. The decision itself is pure (secure_store_rules.h, host-tested);
// this file only reads eFuses and drives the ESP-IDF NVS calls.
//
// Exercised in Espressif QEMU with emulated eFuses during the design (probe
// app, identical API calls): HMAC secure init OK; host-built identity read
// back; tampered / plaintext / erased / wrong-key images -> namespace
// NOT_FOUND. NOT yet run on real silicon.
// -------------------------------------------------------------
#include "secure_store.h"

#include <string.h>

#include <esp_efuse.h>
#include <esp_efuse_table.h>
#include <esp_flash_encrypt.h>
#include <esp_partition.h>
#include <esp_secure_boot.h>
#include <nvs_flash.h>
#include <soc/soc_caps.h>
#if SOC_HMAC_SUPPORTED
#include <esp_hmac.h>
#include <nvs_sec_provider.h>
#endif

#include "security_profile.h"

using secure_store_rules::StoreMode;
namespace ssr = secure_store_rules;

namespace {

StoreMode s_mode = StoreMode::RefuseUnsecuredChip;
bool s_ready = false;

#if SOC_HMAC_SUPPORTED
nvs_sec_scheme_t *s_scheme = nullptr;  // registered once, reused by wipe()
#endif

bool hmac_key4_present() {
    return esp_efuse_get_key_purpose(EFUSE_BLK_KEY4) == ESP_EFUSE_KEY_PURPOSE_HMAC_UP;
}

// The pre-2026-10-08 table has `coredump` where nvs_sec now lives. A toy
// still on it (field fleet 1.3.2, a bench board not re-flashed by cable) runs
// a DEV image in legacy mode instead of losing its identity.
bool sec_partition_present() {
    return esp_partition_find_first(ESP_PARTITION_TYPE_DATA, ESP_PARTITION_SUBTYPE_DATA_NVS,
                                    ssr::kSecPartition) != nullptr;
}

// Opens nvs_sec in `mode`. Never generates keys: the factory burned the HMAC
// key; nvs_flash_generate_keys* would try to burn an eFuse from the app.
esp_err_t open_store(StoreMode mode) {
    if (mode == StoreMode::LegacyDefaultNvs) {
        // initArduino() already initialised the default "nvs" before setup();
        // Preferences::begin(ns, ro) with no label opens it. Nothing to do.
        return ESP_OK;
    }
    if (mode == StoreMode::PlaintextDev) {
        esp_err_t e = nvs_flash_init_partition(ssr::kSecPartition);
        if (e == ESP_ERR_NVS_NO_FREE_PAGES || e == ESP_ERR_NVS_NEW_VERSION_FOUND) {
            // DEV only: a bench board converted from the old table has the
            // former coredump region here. Nothing of value can be in it.
            Serial.printf("[sec] dev store: %s -> erasing nvs_sec\n", esp_err_to_name(e));
            nvs_flash_erase_partition(ssr::kSecPartition);
            e = nvs_flash_init_partition(ssr::kSecPartition);
        }
        return e;
    }
    if (mode != StoreMode::Encrypted) {
        return ESP_ERR_INVALID_STATE;
    }
#if SOC_HMAC_SUPPORTED
    if (s_scheme == nullptr) {
        nvs_sec_config_hmac_t cfg = {};
        cfg.hmac_key_id = HMAC_KEY4;
        const esp_err_t r = nvs_sec_provider_register_hmac(&cfg, &s_scheme);
        if (r != ESP_OK) {
            s_scheme = nullptr;
            return r;
        }
    }
    nvs_sec_cfg_t keys;
    memset(&keys, 0, sizeof(keys));
    esp_err_t e = nvs_flash_read_security_cfg_v2(s_scheme, &keys);
    if (e == ESP_OK) {
        // No erase-and-retry here, on purpose: a factory-written partition
        // that fails to open would lose the identity either way, and leaving
        // it untouched keeps the evidence for the support bench.
        e = nvs_flash_secure_init_partition(ssr::kSecPartition, &keys);
    }
    memset(&keys, 0, sizeof(keys));  // derived XTS keys never outlive the call
    return e;
#else
    return ESP_ERR_NOT_SUPPORTED;
#endif
}

void stamp_layout() {
    if (ssr::store_is_default_nvs(s_mode)) {
        return;  // the layout stamp is an nvs_sec property; legacy has none
    }
    Preferences p;
    if (p.begin(ssr::kSysNs, /*readOnly=*/false, ssr::kSecPartition)) {
        if (p.getUChar(ssr::kLayoutKey, 0) != ssr::kLayoutVersion) {
            p.putUChar(ssr::kLayoutKey, (uint8_t)ssr::kLayoutVersion);
        }
        p.end();
    }
}

// Physically erases the default "nvs" if an earlier boot asked for it. Runs
// before Wi-Fi/BT exist, so nothing holds a handle into "nvs" yet (only
// initArduino()'s plain nvs_flash_init(), which deinit releases).
void run_purge_check() {
    // Never touch nvs_sec unless it was opened in its proper mode: a plain
    // Preferences::begin() on a refused store would plain-init it -- on a
    // dev image running on a LOCKED chip that destroys the encrypted store.
    if (!s_ready || !ssr::purge_allowed(s_mode)) {
        return;  // legacy mode: "nvs" IS the app store -- never purge it
    }
    Preferences p;
    bool read_ok = false;
    unsigned flag = 0;
    if (p.begin(ssr::kSysNs, /*readOnly=*/true, ssr::kSecPartition)) {
        read_ok = true;
        flag = p.getUChar(ssr::kPurgeKey, 0);
        p.end();
    }
    if (!ssr::purge_due(s_ready, read_ok, flag)) {
        return;
    }
    const esp_err_t a = nvs_flash_deinit_partition(ssr::kDefaultPartition);
    const esp_err_t b = nvs_flash_erase_partition(ssr::kDefaultPartition);
    const esp_err_t c = nvs_flash_init_partition(ssr::kDefaultPartition);
    Serial.printf("[sec] default nvs purged (deinit=%s erase=%s init=%s)\n",
                  esp_err_to_name(a), esp_err_to_name(b), esp_err_to_name(c));
    if (b == ESP_OK && p.begin(ssr::kSysNs, /*readOnly=*/false, ssr::kSecPartition)) {
        p.putUChar(ssr::kPurgeKey, 0);
        p.end();
    }
}

}  // namespace

bool secure_store_begin() {
    const bool fe = esp_flash_encryption_enabled();
    const bool sb = esp_secure_boot_enabled();
    const bool hmac = hmac_key4_present();
    const bool sec_part = sec_partition_present();
    s_mode = ssr::decide(AREG_IS_RELEASE != 0, fe, sb, hmac, sec_part);
    s_ready = false;
    if (ssr::store_usable(s_mode)) {
        const esp_err_t e = open_store(s_mode);
        if (e == ESP_OK) {
            s_ready = true;
            stamp_layout();
            if (ssr::store_is_default_nvs(s_mode)) {
                Serial.println("[sec] no nvs_sec partition (pre-2026-10-08 table) - app state stays "
                               "in the default nvs (legacy mode); cable conversion needed to move it");
            }
        } else {
            Serial.printf("[sec] store open failed (%s, %s)\n", ssr::mode_name(s_mode),
                          esp_err_to_name(e));
        }
    } else {
        Serial.printf("[sec] store refused (%s)\n", ssr::mode_name(s_mode));
    }
    run_purge_check();
    return s_ready;
}

bool secure_store_ready() { return s_ready; }

StoreMode secure_store_mode() { return s_mode; }

bool areg_prefs_begin(Preferences &p, const char *ns, bool read_only) {
    if (!s_ready) {
        return false;
    }
    if (ssr::store_is_default_nvs(s_mode)) {
        return p.begin(ns, read_only);  // legacy table: the default "nvs"
    }
    return p.begin(ns, read_only, ssr::kSecPartition);
}

bool secure_store_request_default_nvs_purge() {
    if (!ssr::purge_allowed(s_mode)) {
        return false;  // legacy mode: a purge would erase the identity itself
    }
    Preferences p;
    if (!areg_prefs_begin(p, ssr::kSysNs, /*read_only=*/false)) {
        return false;
    }
    bool pending = p.getUChar(ssr::kPurgeKey, 0) == 1;
    if (!pending) {
        pending = p.putUChar(ssr::kPurgeKey, 1) == 1;
    }
    p.end();
    return pending;
}

bool secure_store_wipe() {
    if (!s_ready || ssr::store_is_default_nvs(s_mode)) {
        return false;  // legacy mode: nvs_sec does not exist on this table
    }
    s_ready = false;
    nvs_flash_deinit_partition(ssr::kSecPartition);
    const esp_err_t er = nvs_flash_erase_partition(ssr::kSecPartition);
    const esp_err_t eo = open_store(s_mode);
    Serial.printf("[sec] store wiped (erase=%s reopen=%s)\n", esp_err_to_name(er),
                  esp_err_to_name(eo));
    if (eo != ESP_OK) {
        return false;
    }
    s_ready = true;
    stamp_layout();
    secure_store_request_default_nvs_purge();
    return er == ESP_OK;
}

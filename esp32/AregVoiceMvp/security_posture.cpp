// -------------------------------------------------------------
// AregVoiceMvp / security_posture.cpp -- see security_posture.h.
//
// Every API used here is exported by the precompiled arduino-esp32 3.3.8
// libs and by the IDF v5.5.4 component build alike (checked with nm during
// the design); the same calls ran in the design's QEMU probe app.
// -------------------------------------------------------------
#include "security_posture.h"

#include <Arduino.h>
#include <string.h>

#include <esp_efuse.h>
#include <esp_efuse_table.h>
#include <esp_flash_encrypt.h>
#include <esp_secure_boot.h>
#include <mbedtls/sha256.h>

#include "device_creds.h"
#include "secure_store.h"
#include "security_posture_rules.h"
#include "security_profile.h"

namespace spr = security_posture_rules;

static spr::DlMode read_dl_mode();

static spr::Posture read_posture() {
    spr::Posture p;
    p.release_profile = AREG_IS_RELEASE != 0;
    p.sb = esp_secure_boot_enabled();
    switch (esp_get_flash_encryption_mode()) {
        case ESP_FLASH_ENC_MODE_RELEASE: p.fe = spr::FeMode::Release; break;
        case ESP_FLASH_ENC_MODE_DEVELOPMENT: p.fe = spr::FeMode::Development; break;
        default: p.fe = spr::FeMode::Off; break;
    }
    p.dl = read_dl_mode();
    p.jtag_pad_off = esp_efuse_read_field_bit(ESP_EFUSE_DIS_PAD_JTAG);
    p.jtag_usb_off = esp_efuse_read_field_bit(ESP_EFUSE_DIS_USB_JTAG);
    p.hmac_key4 = esp_efuse_get_key_purpose(EFUSE_BLK_KEY4) == ESP_EFUSE_KEY_PURPOSE_HMAC_UP;
    static const esp_efuse_purpose_t kDigests[3] = {
        ESP_EFUSE_KEY_PURPOSE_SECURE_BOOT_DIGEST0,
        ESP_EFUSE_KEY_PURPOSE_SECURE_BOOT_DIGEST1,
        ESP_EFUSE_KEY_PURPOSE_SECURE_BOOT_DIGEST2,
    };
    for (unsigned i = 0; i < 3; i++) {
        if (esp_efuse_find_purpose(kDigests[i], nullptr)) p.sb_slots |= 1u << i;
        if (esp_efuse_get_digest_revoke(i)) p.revoked |= 1u << i;
    }
    p.sb_release_ok = esp_secure_boot_cfg_verify_release_mode();
    p.fe_release_ok = esp_flash_encryption_cfg_verify_release_mode();
    p.store_ready = secure_store_ready();
    p.store_encrypted = secure_store_mode() == secure_store_rules::StoreMode::Encrypted;
    p.store = secure_store_rules::mode_name(secure_store_mode());
    return p;
}

static spr::DlMode read_dl_mode() {
    if (esp_efuse_read_field_bit(ESP_EFUSE_DIS_DOWNLOAD_MODE)) {
        return spr::DlMode::Disabled;
    }
    if (esp_efuse_read_field_bit(ESP_EFUSE_ENABLE_SECURITY_DOWNLOAD)) {
        return spr::DlMode::Secure;
    }
    return spr::DlMode::Enabled;
}

bool security_network_allowed() {
    // eFuses only ever change at the factory (and an app never burns
    // DIS_DOWNLOAD_MODE: only the station does, then resets the toy), so one
    // read per boot is the truth for the whole boot.
    static int s_allowed = -1;
    if (s_allowed < 0) {
        s_allowed = spr::network_allowed(AREG_IS_RELEASE != 0, read_dl_mode()) ? 1 : 0;
    }
    return s_allowed == 1;
}

void security_posture_print() {
    const spr::Posture p = read_posture();
    char line[200];
    spr::format_line(p, line, sizeof(line));
    Serial.println(line);
    if (spr::release_on_unsecured_chip(p)) {
        Serial.println("[sec] release image on UNSECURED chip - secrets disabled");
    }
    if (!spr::network_allowed(p.release_profile, p.dl)) {
        Serial.println("[sec] factory not finished (download mode open) - offline, BLE setup disabled");
    }
    // Factory window only (ROM download mode still enabled): 4 bytes of
    // SHA-256 of the device key, so the station can match the key it just
    // minted without the key itself ever leaving the chip.
    if (p.dl == spr::DlMode::Enabled && p.store_ready) {
        static char id[48];
        static char key[128];
        const bool have = device_creds_load(id, sizeof(id), key, sizeof(key)) && key[0] != '\0';
        if (spr::should_print_key_fp(p, have)) {
            uint8_t d[32];
            mbedtls_sha256((const uint8_t *)key, strlen(key), d, /*is224=*/0);
            char fp[40];
            spr::format_key_fp(d, fp, sizeof(fp));
            Serial.println(fp);
            memset(d, 0, sizeof(d));
        }
        memset(key, 0, sizeof(key));
    }
    Serial.flush();
}

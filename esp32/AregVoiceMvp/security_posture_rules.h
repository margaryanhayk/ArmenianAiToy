#pragma once
// -------------------------------------------------------------
// AregVoiceMvp / security_posture_rules.h -- PURE formatting of the one-line
// security posture the toy prints at every boot. No Arduino: host-tested by
// host_tests/security_posture_rules_test.cpp.
//
// The factory station (tools/factory/secure_provision.py) parses this exact
// line, so its field names and value spellings are a contract:
//   [sec] profile=%s sb=%d fe=%s dl=%s jtag=%s nvs=%s hmac=%s
//         sbslots=0x%x revoked=0x%x sb_rel=%d fe_rel=%d store=%s
// (one line). After factory step 8 a good toy reads
//   sb=1 fe=release dl=enabled nvs=encrypted store=encrypted
// and after step 9 (download mode burned)
//   dl=disabled sb_rel=1 fe_rel=1
// No secret is ever part of it. The separate key fingerprint line
// ("[sec] key_fp=xxxxxxxx", 4 bytes of SHA-256 of the device key, enough to
// match the key the station just minted, useless to an attacker) is printed
// ONLY while ROM download mode is still enabled -- i.e. only during the
// factory window.
// -------------------------------------------------------------
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

namespace security_posture_rules {

enum class FeMode { Off = 0, Development = 1, Release = 2 };
enum class DlMode { Enabled, Secure, Disabled };

struct Posture {
    bool release_profile = false;
    bool sb = false;
    FeMode fe = FeMode::Off;
    DlMode dl = DlMode::Enabled;
    bool jtag_pad_off = false;
    bool jtag_usb_off = false;
    bool hmac_key4 = false;
    unsigned sb_slots = 0;   // bit i = a SECURE_BOOT_DIGESTi key block exists
    unsigned revoked = 0;    // bit i = SECURE_BOOT_KEY_REVOKEi burned
    bool sb_release_ok = false;   // esp_secure_boot_cfg_verify_release_mode()
    bool fe_release_ok = false;   // esp_flash_encryption_cfg_verify_release_mode()
    const char *store = "none";   // secure_store_rules::mode_name()
    bool store_encrypted = false;
    bool store_ready = false;
};

inline const char *fe_name(FeMode m) {
    return m == FeMode::Release ? "release" : m == FeMode::Development ? "dev" : "off";
}
inline const char *dl_name(DlMode m) {
    return m == DlMode::Disabled ? "disabled" : m == DlMode::Secure ? "secure" : "enabled";
}
inline const char *jtag_name(bool pad_off, bool usb_off) {
    return (pad_off && usb_off) ? "off" : (!pad_off && !usb_off) ? "on" : "partial";
}
inline const char *nvs_name(const Posture &p) {
    return !p.store_ready ? "none" : p.store_encrypted ? "encrypted" : "plaintext";
}

// Returns snprintf's result (the would-be length); the line is always
// NUL-terminated when cap > 0.
inline int format_line(const Posture &p, char *buf, size_t cap) {
    return snprintf(buf, cap,
                    "[sec] profile=%s sb=%d fe=%s dl=%s jtag=%s nvs=%s hmac=%s sbslots=0x%x "
                    "revoked=0x%x sb_rel=%d fe_rel=%d store=%s",
                    p.release_profile ? "release" : "dev", p.sb ? 1 : 0, fe_name(p.fe),
                    dl_name(p.dl), jtag_name(p.jtag_pad_off, p.jtag_usb_off), nvs_name(p),
                    p.hmac_key4 ? "key4" : "none", p.sb_slots, p.revoked,
                    p.sb_release_ok ? 1 : 0, p.fe_release_ok ? 1 : 0, p.store);
}

// The finished, shippable state: everything the factory's 11 steps burn.
inline bool locked(const Posture &p) {
    return p.release_profile && p.sb && p.fe == FeMode::Release && p.dl == DlMode::Disabled &&
           p.jtag_pad_off && p.jtag_usb_off && p.hmac_key4 && p.store_ready &&
           p.store_encrypted && p.sb_release_ok && p.fe_release_ok &&
           (p.revoked & 0x4u) != 0;  // the unused slot 2 is revoked
}

// A release image that found an unsecured chip keeps no secrets at all.
inline bool release_on_unsecured_chip(const Posture &p) {
    return p.release_profile && !p.store_ready;
}

// A RELEASE image goes online (backend calls, BLE Wi-Fi setup) only once the
// factory has finished: ROM download mode permanently disabled (step 9). A
// toy that escaped the station half-way -- Secure Boot and flash encryption
// on, download mode still open -- can load RAM code over the cable, and that
// code can use the HMAC_UP key to derive the NVS keys and decrypt nvs_sec
// (device key, PoP, and a family's Wi-Fi once it had one). Such a toy stays
// visibly dead instead: offline, no BLE setup, but still printing its [sec]
// / [device] / key_fp console lines, which is all factory step 8 reads.
// "Secure download mode" is not the finished state either. DEV: always.
inline bool network_allowed(bool release_profile, DlMode dl) {
    return !release_profile || dl == DlMode::Disabled;
}

// The key fingerprint may only appear while ROM download mode is still
// enabled (the factory window), and only if there is a key.
inline bool should_print_key_fp(const Posture &p, bool have_key) {
    return p.dl == DlMode::Enabled && have_key;
}

inline int format_key_fp(const uint8_t sha256_of_key[32], char *buf, size_t cap) {
    return snprintf(buf, cap, "[sec] key_fp=%02x%02x%02x%02x", sha256_of_key[0], sha256_of_key[1],
                    sha256_of_key[2], sha256_of_key[3]);
}

}  // namespace security_posture_rules

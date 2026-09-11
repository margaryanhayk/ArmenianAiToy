// -------------------------------------------------------------
// AregVoiceMvp / device_creds_rules.h   (Factory pairing, 2026-09-11)
//
// Pure, Arduino-free rules shared by device_creds.{h,cpp} and
// ble_provisioning.cpp: the NVS namespace/key names, and the BLE PoP shape
// check. Split out from device_creds.cpp (which needs Preferences/NVS) so
// this half is host-testable with plain g++ — see
// host_tests/device_creds_rules_test.cpp.
// -------------------------------------------------------------
#pragma once

#include <cstddef>
#include <cstring>

namespace device_creds_rules {

// NVS namespace + key names (Arduino Preferences path). esp-idf's
// nvs_set_*/nvs_get_* caps a key at 15 chars — get this wrong and
// Preferences::begin()/putString() truncate or fail silently, so these are
// pinned here rather than left as untested local literals.
constexpr const char *kNamespace = "aregdev";
constexpr const char *kIdKey     = "devid";
constexpr const char *kKeyKey    = "apikey";
constexpr const char *kPopKey    = "pop";

constexpr size_t kMaxNvsKeyLen = 15;

// The exact alphabet DeviceService.cs (backend) mints a PoP from — excludes
// I, L, O, U, 0, 1 because the code is printed on the box and often read
// aloud down a phone. Mirrored here, not shared across the language
// boundary, same "own copy, no coupling" call DeviceService.cs itself makes
// against ParentService's invite-code alphabet.
constexpr const char *kPopAlphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";
constexpr size_t kPopLength = 8;

inline bool is_pop_alphabet_char(char c) {
    for (const char *p = kPopAlphabet; *p != '\0'; ++p) {
        if (*p == c) return true;
    }
    return false;
}

// True iff `pop` is exactly kPopLength chars, every one drawn from
// kPopAlphabet. Lets a caller (ble_provisioning.cpp) distinguish a real
// factory-burned PoP from NVS garbage — a torn write, a hand-edited NVS
// image, a stale/short value left by an older factory-station version —
// before advertising it as though it were a real pairing code. A null or
// malformed value must fall back to the bench default, never be trusted
// half-formed.
inline bool pop_is_wellformed(const char *pop) {
    if (pop == nullptr) return false;
    const size_t len = std::strlen(pop);
    if (len != kPopLength) return false;
    for (size_t i = 0; i < len; ++i) {
        if (!is_pop_alphabet_char(pop[i])) return false;
    }
    return true;
}

}  // namespace device_creds_rules

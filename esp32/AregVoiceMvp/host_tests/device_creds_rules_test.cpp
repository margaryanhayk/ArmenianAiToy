// Host test for device_creds_rules.h (factory pairing, 2026-09-11). Runs
// with plain g++ — no Arduino core, no board, no cable:
//
//     g++ -std=c++17 -Wall -Wextra -o /tmp/dcr_test
//         esp32/AregVoiceMvp/host_tests/device_creds_rules_test.cpp && /tmp/dcr_test
//
//   (one line; split here only so this comment stays a comment)
//
// It compiles the REAL header (device_creds_rules.h) — a copy of the
// constants/function would only ever prove the copy right.
//
// What it does NOT cover: the actual NVS read/write (Preferences), which
// needs hardware or the Arduino core; this covers only the pure shape check
// and the key-name/length invariants ble_provisioning.cpp and
// device_creds.cpp both depend on.
#include "../device_creds_rules.h"
#include <cstdio>
#include <cstring>

static int failures = 0;
static void check(bool ok, const char *what) {
    printf("  %-4s %s\n", ok ? "ok" : "FAIL", what);
    if (!ok) failures++;
}

int main() {
    using namespace device_creds_rules;

    // --- NVS key names: length + no collisions ---
    check(std::strlen(kNamespace) <= kMaxNvsKeyLen, "namespace fits the 15-char NVS key limit");
    check(std::strlen(kIdKey) <= kMaxNvsKeyLen, "id key fits the 15-char NVS key limit");
    check(std::strlen(kKeyKey) <= kMaxNvsKeyLen, "api-key key fits the 15-char NVS key limit");
    check(std::strlen(kPopKey) <= kMaxNvsKeyLen, "pop key fits the 15-char NVS key limit");
    check(std::strcmp(kIdKey, kKeyKey) != 0, "id and api-key NVS keys are distinct");
    check(std::strcmp(kIdKey, kPopKey) != 0, "id and pop NVS keys are distinct");
    check(std::strcmp(kKeyKey, kPopKey) != 0, "api-key and pop NVS keys are distinct");

    // --- PoP alphabet: excludes the read-aloud-confusable characters ---
    check(!is_pop_alphabet_char('I'), "alphabet excludes I");
    check(!is_pop_alphabet_char('L'), "alphabet excludes L");
    check(!is_pop_alphabet_char('O'), "alphabet excludes O");
    check(!is_pop_alphabet_char('U'), "alphabet excludes U");
    check(!is_pop_alphabet_char('0'), "alphabet excludes 0");
    check(!is_pop_alphabet_char('1'), "alphabet excludes 1");
    check(is_pop_alphabet_char('A'), "alphabet includes A");
    check(is_pop_alphabet_char('9'), "alphabet includes 9");
    check(kPopLength == 8, "pop length matches DeviceService.cs's mint length");

    // --- pop_is_wellformed: the actual gate ble_provisioning.cpp trusts ---
    check(pop_is_wellformed("AREGX234"), "a real-shaped 8-char pop from the alphabet passes");
    check(!pop_is_wellformed(nullptr), "a null pop is rejected, never dereferenced");
    check(!pop_is_wellformed(""), "an empty pop is rejected");
    check(!pop_is_wellformed("AREGX23"), "7 chars (short/torn write) is rejected");
    check(!pop_is_wellformed("AREGX2345"), "9 chars is rejected");
    check(!pop_is_wellformed("areg-pair"), "the bench fallback string itself never passes as a stored pop");
    check(!pop_is_wellformed("AREGXIL0"), "a confusable-alphabet char (I/L/0) fails even at the right length");
    check(!pop_is_wellformed("areGX234"), "lowercase is rejected — the mint alphabet is upper-only");

    printf(failures == 0 ? "PASS\n" : "FAIL (%d)\n", failures);
    return failures == 0 ? 0 : 1;
}

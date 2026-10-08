// Host test for secure_store_rules.h (chip security, 2026-10-08). Plain g++:
//
//     g++ -std=c++17 -Wall -Wextra -I.. -o /tmp/ssr_test secure_store_rules_test.cpp && /tmp/ssr_test
//
// Compiles the REAL header. Covers the full 2x2x2x2x2 decision matrix (incl. the old-table legacy mode) that
// decides whether the app store (nvs_sec) opens encrypted, plaintext, or not
// at all, plus the purge predicate and the namespace-name invariants the
// factory station shares. NOT covered: the ESP-IDF NVS calls themselves
// (secure_store.cpp) -- exercised in QEMU during the design, not here.
#include "../secure_store_rules.h"
#include <cstdio>
#include <cstring>

static int failures = 0;
static void check(bool ok, const char *what) {
    printf("  %-4s %s\n", ok ? "ok" : "FAIL", what);
    if (!ok) failures++;
}

int main() {
    using namespace secure_store_rules;
    int encrypted = 0, plaintext = 0, legacy = 0, refused_unsecured = 0, refused_dev = 0;
    for (int bits = 0; bits < 32; bits++) {
        const bool release = bits & 1, fe = bits & 2, sb = bits & 4, hmac = bits & 8, part = bits & 16;
        const StoreMode m = decide(release, fe, sb, hmac, part);
        StoreMode want;
        if (release) want = (fe && sb && hmac && part) ? StoreMode::Encrypted : StoreMode::RefuseUnsecuredChip;
        else if (hmac) want = StoreMode::RefuseDevOnSecuredChip;
        else want = part ? StoreMode::PlaintextDev : StoreMode::LegacyDefaultNvs;
        char what[160];
        snprintf(what, sizeof what, "release=%d fe=%d sb=%d hmac=%d nvs_sec=%d -> %s", release, fe, sb, hmac,
                 part, mode_name(m));
        check(m == want, what);
        encrypted += m == StoreMode::Encrypted;
        plaintext += m == StoreMode::PlaintextDev;
        legacy += m == StoreMode::LegacyDefaultNvs;
        refused_unsecured += m == StoreMode::RefuseUnsecuredChip;
        refused_dev += m == StoreMode::RefuseDevOnSecuredChip;
    }
    check(encrypted == 1, "exactly one combination (release + FE + SB + HMAC + nvs_sec) opens encrypted");
    check(refused_unsecured == 15, "every other release combination refuses (no plaintext secrets)");
    check(plaintext == 4 && legacy == 4 && refused_dev == 8,
          "dev: plaintext nvs_sec, legacy default nvs without it, refused if the chip carries the HMAC key");
    check(!store_usable(StoreMode::RefuseUnsecuredChip) && !store_usable(StoreMode::RefuseDevOnSecuredChip),
          "refusals leave the store unusable");
    check(store_usable(StoreMode::Encrypted) && store_usable(StoreMode::PlaintextDev) &&
              store_usable(StoreMode::LegacyDefaultNvs),
          "the three open modes are usable");
    check(decide(true, true, true, false, true) == StoreMode::RefuseUnsecuredChip,
          "release without the HMAC key never falls back to plaintext");
    check(decide(true, true, true, true, false) == StoreMode::RefuseUnsecuredChip,
          "release without nvs_sec never falls back to the plaintext default nvs");
    check(decide(false, true, true, true, true) == StoreMode::RefuseDevOnSecuredChip,
          "a dev image never plain-inits a locked toy's encrypted store");
    check(decide(false, false, false, false, false) == StoreMode::LegacyDefaultNvs,
          "a field toy on the old table (no nvs_sec) running a DEV image keeps its default-nvs state");
    check(store_is_default_nvs(StoreMode::LegacyDefaultNvs) && !store_is_default_nvs(StoreMode::PlaintextDev) &&
              !store_is_default_nvs(StoreMode::Encrypted),
          "only legacy mode stores app state in the default nvs");
    check(!purge_allowed(StoreMode::LegacyDefaultNvs), "legacy mode never purges nvs (it IS the identity)");
    check(purge_allowed(StoreMode::Encrypted) && purge_allowed(StoreMode::PlaintextDev),
          "the nvs_sec modes may purge the default nvs");
    check(!purge_allowed(StoreMode::RefuseUnsecuredChip) && !purge_allowed(StoreMode::RefuseDevOnSecuredChip),
          "no purge without a usable store");
    check(std::strcmp(mode_name(StoreMode::LegacyDefaultNvs), "legacy") == 0, "legacy mode prints store=legacy");

    check(purge_due(true, true, 1), "purge runs when requested and the store is ready");
    check(!purge_due(false, true, 1), "no purge without a store (flag unreadable by definition)");
    check(!purge_due(true, false, 1), "no purge when the flag could not be read");
    check(!purge_due(true, true, 0) && !purge_due(true, true, 2), "only the value 1 means purge");

    check(std::strcmp(kSecPartition, "nvs_sec") == 0, "partition label matches partitions.csv");
    check(std::strcmp(kDefaultPartition, "nvs") == 0, "default partition label");
    check(std::strcmp(kSysNs, "aregsys") == 0 && std::strcmp(kLayoutKey, "layout") == 0 &&
              std::strcmp(kPurgeKey, "purge") == 0,
          "aregsys/layout/purge match the factory station's identity CSV");
    check(std::strlen(kSecPartition) <= 15 && std::strlen(kSysNs) <= 15, "names fit NVS's 15-char limit");
    check(kLayoutVersion == 1, "layout version 1 (the factory writes layout=1)");

    printf(failures == 0 ? "PASS\n" : "FAIL (%d)\n", failures);
    return failures == 0 ? 0 : 1;
}

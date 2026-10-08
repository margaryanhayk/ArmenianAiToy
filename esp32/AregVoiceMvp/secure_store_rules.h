#pragma once
// -------------------------------------------------------------
// AregVoiceMvp / secure_store_rules.h -- PURE decision logic for the
// application NVS store (the dedicated "nvs_sec" partition). No Arduino, no
// ESP-IDF: host-tested by host_tests/secure_store_rules_test.cpp.
//
// The store holds everything the app persists (device identity, Wi-Fi,
// OTA state, cursors, queues). How it may be opened depends on the image's
// security profile AND on what the chip's eFuses say:
//
//   RELEASE image on a locked chip  (flash encryption + Secure Boot + the
//       HMAC_UP key in BLOCK_KEY4)               -> Encrypted (NVS encryption,
//                                                  keys derived in the HMAC
//                                                  peripheral, never readable)
//   RELEASE image, anything missing           -> RefuseUnsecuredChip: no
//       store at all. A release image on an unsecured chip must not hold a
//       secret in plaintext -- that is exactly the leak this whole design
//       closes (docs/firmware-security.md).
//   DEV image on a chip that HAS the HMAC key -> RefuseDevOnSecuredChip:
//       a plaintext init would destroy the encrypted partition (and a DEV
//       image cannot boot a Secure Boot chip anyway -- belt and braces).
//   DEV image, nvs_sec partition present      -> PlaintextDev (bench).
//   DEV image, NO nvs_sec partition           -> LegacyDefaultNvs: the chip
//       still carries the pre-2026-10-08 partition table (coredump where
//       nvs_sec now lives) -- every field toy on 1.3.2 and any bench board
//       not re-flashed by cable. Its identity, Wi-Fi and OTA state live in
//       the DEFAULT "nvs", so a DEV image reached by OTA keeps using them
//       there instead of losing them for good (review finding 2026-10-08).
//       No purge in this mode: purging "nvs" would erase the identity.
//       Moving such a toy to nvs_sec is a cable conversion
//       (docs/ota-release-runbook.md).
//   RELEASE image, NO nvs_sec partition       -> RefuseUnsecuredChip (a
//       locked toy always has it; a release image never falls back to the
//       plaintext default nvs).
//
// Never generate keys at runtime: the factory burns the HMAC key; asking
// the NVS layer to generate one would try to burn an eFuse from the app.
// -------------------------------------------------------------

namespace secure_store_rules {

enum class StoreMode {
    Encrypted,
    PlaintextDev,
    LegacyDefaultNvs,
    RefuseUnsecuredChip,
    RefuseDevOnSecuredChip,
};

// sec_partition_present: the running partition table has an "nvs_sec" data
// partition (esp_partition_find_first(DATA, NVS, "nvs_sec") != nullptr).
constexpr StoreMode decide(bool release, bool fe_on, bool sb_on, bool hmac_key4_present,
                           bool sec_partition_present) {
    return release
               ? ((fe_on && sb_on && hmac_key4_present && sec_partition_present)
                      ? StoreMode::Encrypted
                      : StoreMode::RefuseUnsecuredChip)
               : (hmac_key4_present       ? StoreMode::RefuseDevOnSecuredChip
                  : sec_partition_present ? StoreMode::PlaintextDev
                                          : StoreMode::LegacyDefaultNvs);
}

constexpr bool store_usable(StoreMode m) {
    return m == StoreMode::Encrypted || m == StoreMode::PlaintextDev ||
           m == StoreMode::LegacyDefaultNvs;
}

// True when the app store is the DEFAULT "nvs" partition (old table). Purges
// of "nvs" are then forbidden: they would erase the identity itself.
constexpr bool store_is_default_nvs(StoreMode m) { return m == StoreMode::LegacyDefaultNvs; }

constexpr const char *mode_name(StoreMode m) {
    return m == StoreMode::Encrypted              ? "encrypted"
           : m == StoreMode::PlaintextDev         ? "plaintext"
           : m == StoreMode::LegacyDefaultNvs     ? "legacy"
           : m == StoreMode::RefuseUnsecuredChip  ? "refused-unsecured-chip"
                                                  : "refused-dev-on-secured-chip";
}

// Partition + bookkeeping namespace. Every string here is shared with the
// factory station (tools/factory/secure_provision.py builds the nvs_sec image
// with an "aregsys" namespace carrying layout=1) -- change both or neither.
constexpr const char *kSecPartition = "nvs_sec";
constexpr const char *kDefaultPartition = "nvs";
constexpr const char *kSysNs = "aregsys";
constexpr const char *kPurgeKey = "purge";
constexpr const char *kLayoutKey = "layout";
constexpr unsigned kLayoutVersion = 1;

// The boot-time purge of the DEFAULT nvs partition. ESP-IDF's Wi-Fi driver
// and the BLE provisioning manager both keep their own plaintext copy of the
// Wi-Fi password in "nvs" (nvs.net80211); NVS "erase" only marks entries, so
// the password would survive on flash. A request sets purge=1 in nvs_sec;
// the next boot physically erases "nvs" BEFORE Wi-Fi/BT start and clears it.
constexpr bool purge_due(bool store_ready, bool flag_read_ok, unsigned flag_value) {
    return store_ready && flag_read_ok && flag_value == 1;
}

// Whether a purge may even be considered in this mode (never in legacy mode,
// where "nvs" IS the app store).
constexpr bool purge_allowed(StoreMode m) {
    return store_usable(m) && !store_is_default_nvs(m);
}

}  // namespace secure_store_rules

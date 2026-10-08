#pragma once
// -------------------------------------------------------------
// AregVoiceMvp / ota_slot_rules.h -- PURE decision: when a RELEASE toy
// retires the older firmware image left in its inactive OTA slot. No
// Arduino, no ESP-IDF: host-tested by host_tests/ota_slot_rules_test.cpp.
//
// WHY (review 2026-10-08, "physical downgrade"): the release bootloader has
// no anti-rollback, and erasing `otadata` needs no key -- with both copies
// invalid the ESP-IDF v5.5.4 bootloader logs "No factory image, trying OTA
// 0" and boots ota_0 (bootloader_utility.c). So anyone with a flash clip
// could make a locked toy boot whatever OLDER owner-signed release is still
// sitting in the other slot -- and an old release with a parser bug is a
// way in. Once the running image has passed its OTA check-in (the backend
// ack that marks it valid), the old image is no longer needed for rollback,
// so the toy makes it unbootable by erasing the slot's first flash sector
// (image header + first segment header; the bootloader rejects a slot whose
// first byte is not the 0xE9 image magic, then falls through to the running
// image's slot).
//
// One sector, not the whole 3 MB slot: an erase of 4 KB takes tens of ms
// instead of many seconds of a blocked loop, and against an attacker
// WITHOUT an earlier dump of this toy's flash it is exactly as strong --
// the rest of the old image is ciphertext under this toy's per-device key
// and cannot be made bootable without a valid encrypted header. An attacker
// who DID dump this toy's flash before the update can replay that ciphertext
// either way; only the anti-rollback eFuse closes that (owner decision,
// docs/firmware-security.md s11).
//
// Never while an OTA outcome is pending (the old image IS the rollback
// target until the check-in confirms the new one), never in a DEV build
// (bench boards keep their manual-rollback image), and only when the slot
// is not already erased (idempotent; runs at boot too, so a power cut
// between the confirm and the erase is caught on the next boot).
// -------------------------------------------------------------
#include <stddef.h>
#include <stdint.h>

namespace ota_slot_rules {

// Bytes erased at the start of the inactive slot: one flash sector.
constexpr uint32_t kRetireEraseBytes = 0x1000;
// Bytes read (raw, not decrypted) to tell whether the slot is already erased.
constexpr size_t kProbeBytes = 32;

constexpr bool all_erased(const uint8_t *buf, size_t n) {
    return n == 0 ? true : (buf[0] == 0xFF && all_erased(buf + 1, n - 1));
}

// release:                 AREG_IS_RELEASE
// outcome_pending:         ota_state == REBOOTING (check-in not yet decided)
// running_pending_verify:  esp_ota_get_state_partition(running) == PENDING_VERIFY
// inactive_is_running:     the "inactive" slot is the running one (no OTA
//                          pair / lookup failed) -- never erase ourselves
// inactive_already_erased: the first kProbeBytes of the slot read raw 0xFF
constexpr bool retire_inactive_due(bool release, bool outcome_pending, bool running_pending_verify,
                                   bool inactive_is_running, bool inactive_already_erased) {
    return release && !outcome_pending && !running_pending_verify && !inactive_is_running &&
           !inactive_already_erased;
}

}  // namespace ota_slot_rules

// Host test for ota_slot_rules.h (physical-downgrade guard, 2026-10-08).
// Plain g++:
//
//     g++ -std=c++17 -Wall -Wextra -I.. -o /tmp/osr_test ota_slot_rules_test.cpp && /tmp/osr_test
//
// Compiles the REAL header. Covers the full decision matrix for retiring the
// older image in the inactive OTA slot of a RELEASE toy, and the erased-probe
// helper. NOT covered: the esp_partition_* calls (ota_foundation.cpp) -- not
// run on hardware yet (docs/firmware-security.md s12).
#include "../ota_slot_rules.h"
#include <cstdio>

static int failures = 0;
static void check(bool ok, const char *what) {
    printf("  %-4s %s\n", ok ? "ok" : "FAIL", what);
    if (!ok) failures++;
}

int main() {
    using namespace ota_slot_rules;
    int due = 0;
    for (int bits = 0; bits < 32; bits++) {
        const bool rel = bits & 1, pending = bits & 2, pv = bits & 4, self = bits & 8, erased = bits & 16;
        const bool got = retire_inactive_due(rel, pending, pv, self, erased);
        const bool want = rel && !pending && !pv && !self && !erased;
        char what[160];
        snprintf(what, sizeof what, "release=%d pending=%d pending_verify=%d self=%d erased=%d -> %d", rel, pending,
                 pv, self, erased, got);
        check(got == want, what);
        due += got;
    }
    check(due == 1, "exactly one combination erases: release, confirmed, other slot, not yet erased");
    check(!retire_inactive_due(false, false, false, false, false), "DEV builds never erase (bench rollback image kept)");
    check(!retire_inactive_due(true, true, false, false, false),
          "never while the check-in is pending: the old image is the rollback target");
    check(!retire_inactive_due(true, false, true, false, false), "never while the running image is pending-verify");
    check(!retire_inactive_due(true, false, false, true, false), "never the running slot itself");
    check(!retire_inactive_due(true, false, false, false, true), "idempotent: an erased slot is left alone");

    const uint8_t blank[kProbeBytes] = {0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                                        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                                        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF};
    uint8_t image[kProbeBytes];
    for (size_t i = 0; i < kProbeBytes; i++) image[i] = 0xFF;
    image[0] = 0xE9;  // image magic
    check(all_erased(blank, kProbeBytes), "an erased header reads all 0xFF");
    check(!all_erased(image, kProbeBytes), "an image header is not erased");
    image[0] = 0xFF;
    image[kProbeBytes - 1] = 0x00;
    check(!all_erased(image, kProbeBytes), "any non-0xFF byte means not erased");
    check(kRetireEraseBytes == 0x1000 && kRetireEraseBytes % 0x1000 == 0, "one 4 KB flash sector, sector-aligned");
    check(kProbeBytes <= kRetireEraseBytes, "the probe lies inside the erased sector");

    printf(failures == 0 ? "PASS\n" : "FAIL (%d)\n", failures);
    return failures == 0 ? 0 : 1;
}

// Host test for security_posture_rules.h (boot posture line, 2026-10-08).
// Plain g++:
//
//     g++ -std=c++17 -Wall -Wextra -I.. -o /tmp/spr_test security_posture_rules_test.cpp && /tmp/spr_test
//
// The factory station parses this line (tools/factory/secure_provision.py),
// so the exact spelling is pinned here. NOT covered: reading the eFuses
// (security_posture.cpp) -- that needs a chip.
#include "../security_posture_rules.h"
#include <cstdio>
#include <cstring>

static int failures = 0;
static void check(bool ok, const char *what) {
    printf("  %-4s %s\n", ok ? "ok" : "FAIL", what);
    if (!ok) failures++;
}

int main() {
    using namespace security_posture_rules;
    char line[256];

    Posture bench;  // all defaults: an untouched dev board
    bench.store = "plaintext";
    bench.store_ready = true;
    format_line(bench, line, sizeof line);
    check(strcmp(line, "[sec] profile=dev sb=0 fe=off dl=enabled jtag=on nvs=plaintext hmac=none sbslots=0x0 "
                       "revoked=0x0 sb_rel=0 fe_rel=0 store=plaintext") == 0,
          "bench board line, exact");
    check(!locked(bench), "a bench board is not locked");

    Posture step8;  // after factory step 7, before DIS_DOWNLOAD_MODE
    step8.release_profile = true;
    step8.sb = true;
    step8.fe = FeMode::Release;
    step8.dl = DlMode::Enabled;
    step8.jtag_pad_off = step8.jtag_usb_off = true;
    step8.hmac_key4 = true;
    step8.sb_slots = 0x3;
    step8.revoked = 0x4;
    step8.sb_release_ok = false;
    step8.fe_release_ok = false;
    step8.store = "encrypted";
    step8.store_ready = step8.store_encrypted = true;
    format_line(step8, line, sizeof line);
    check(strcmp(line, "[sec] profile=release sb=1 fe=release dl=enabled jtag=off nvs=encrypted hmac=key4 "
                       "sbslots=0x3 revoked=0x4 sb_rel=0 fe_rel=0 store=encrypted") == 0,
          "factory step-8 line, exact");
    check(strstr(line, "sb=1 fe=release dl=enabled") != nullptr && strstr(line, "store=encrypted") != nullptr,
          "contains what step 8 checks");
    check(!locked(step8), "step 8 is not yet locked (download mode open)");
    check(should_print_key_fp(step8, true), "key fingerprint allowed in the factory window");
    check(!should_print_key_fp(step8, false), "no key, no fingerprint");

    Posture done = step8;  // after step 9
    done.dl = DlMode::Disabled;
    done.sb_release_ok = done.fe_release_ok = true;
    format_line(done, line, sizeof line);
    check(strstr(line, "dl=disabled") != nullptr && strstr(line, "sb_rel=1 fe_rel=1") != nullptr,
          "step-9 line shows dl=disabled sb_rel=1 fe_rel=1");
    check(locked(done), "a fully provisioned toy is locked");
    check(!should_print_key_fp(done, true), "never a key fingerprint once download mode is disabled");

    Posture p = done; p.revoked = 0;
    check(!locked(p), "not locked while unused slot 2 is not revoked");
    p = done; p.jtag_usb_off = false;
    check(!locked(p), "not locked with USB-JTAG on");
    format_line(p, line, sizeof line);
    check(strstr(line, "jtag=partial") != nullptr, "one JTAG off reads 'partial'");
    p = done; p.store_encrypted = false;
    check(!locked(p), "not locked with a plaintext store");
    p = done; p.release_profile = false;
    check(!locked(p), "a dev image is never 'locked'");
    p = done; p.dl = DlMode::Secure;
    check(!locked(p), "secure download mode is not the chosen final state");
    format_line(p, line, sizeof line);
    check(strstr(line, "dl=secure") != nullptr, "secure download mode spelled 'secure'");

    Posture unsecured;
    unsecured.release_profile = true;
    unsecured.store = "refused-unsecured-chip";
    format_line(unsecured, line, sizeof line);
    check(release_on_unsecured_chip(unsecured), "release image on an unsecured chip is flagged");
    check(strstr(line, "nvs=none") != nullptr && strstr(line, "store=refused-unsecured-chip") != nullptr,
          "refused store reads nvs=none store=refused-unsecured-chip");
    check(!release_on_unsecured_chip(bench), "dev board is not flagged");

    // Half-secured toy (review 2026-10-08): a RELEASE image stays offline
    // until download mode is permanently disabled; DEV is never gated.
    check(!network_allowed(true, DlMode::Enabled), "release + download mode open (step 8 / escaped toy): offline");
    check(!network_allowed(true, DlMode::Secure), "release + secure download mode: offline (not the final state)");
    check(network_allowed(true, DlMode::Disabled), "release after step 9: online");
    check(network_allowed(false, DlMode::Enabled) && network_allowed(false, DlMode::Disabled) &&
              network_allowed(false, DlMode::Secure),
          "dev bench boards are always online");
    check(!network_allowed(step8.release_profile, step8.dl) && network_allowed(done.release_profile, done.dl),
          "step-8 posture offline, finished posture online");

    const uint8_t d[32] = {0xde, 0xad, 0xbe, 0xef, 0x01};
    format_key_fp(d, line, sizeof line);
    check(strcmp(line, "[sec] key_fp=deadbeef") == 0, "key_fp = first 4 bytes, lowercase hex");

    char small[16];
    const int n = format_line(done, small, sizeof small);
    check(n > (int)sizeof small && strlen(small) == sizeof small - 1, "truncates safely into a small buffer");

    printf(failures == 0 ? "PASS\n" : "FAIL (%d)\n", failures);
    return failures == 0 ? 0 : 1;
}

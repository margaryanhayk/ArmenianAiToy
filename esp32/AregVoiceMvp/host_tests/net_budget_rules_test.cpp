// Host test for net_budget_rules.h (network time budget, 2026-10-29). Runs
// with plain g++ -- no Arduino core, no board, no cable:
//
//     g++ -std=c++17 -Wall -Wextra -o /tmp/nbr_test
//         esp32/AregVoiceMvp/host_tests/net_budget_rules_test.cpp && /tmp/nbr_test
//
//   (one line; split here only so this comment stays a comment)
//
// It compiles the REAL header -- a copy of the arithmetic would only ever
// prove the copy right.
//
// The incident behind it: an idle toy rebooted with TASK_WDT because a stalled
// heartbeat could block loop() for DNS + connect + a 120 s TLS handshake + a
// 30 s read while the watchdog was 30 s. This pins the rule that stops it.
//
// What it does NOT cover: that net_transport.cpp really calls
// setHandshakeTimeout, that the idle calls really use AREG_POLL_READ_MS, or
// that a real stalled link behaves -- those need the compile and the bench.
#include "../net_budget_rules.h"
#include <cstdio>

static int failures = 0;
static void check(bool ok, const char *what) {
    printf("  %-4s %s\n", ok ? "ok" : "FAIL", what);
    if (!ok) failures++;
}

int main() {
    const uint32_t connect_ms = 5000;   // AREG_HTTP_CONNECT_MS as shipped
    const uint32_t shipped_wdt_s = 60;  // AREG_WDT_TIMEOUT_S default and config.h.example
    const uint32_t stale_bench_wdt_s = 30;  // the override that rebooted the toy

    // --- the bounds the firmware ships with ---
    check(AREG_TLS_HANDSHAKE_S > 0 && AREG_TLS_HANDSHAKE_S <= 15,
          "TLS handshake is bounded far below the core's 120 s default");
    check(AREG_POLL_READ_MS < 30000,
          "idle polls wait less than the 30 s read used for AI turns");

    // --- the arithmetic ---
    check(areg_net_call_worst_ms(connect_ms, AREG_POLL_READ_MS) == 43000u,
          "worst idle call = DNS 20 s + connect 5 s + handshake 10 s + read 8 s");
    check(areg_wdt_min_s(connect_ms, AREG_POLL_READ_MS) == 53u,
          "watchdog floor = 43 s + 10 s margin = 53 s");
    check(areg_wdt_min_s(connect_ms, AREG_POLL_READ_MS + 1u) == 54u,
          "a partial second rounds the floor UP, never down");

    // --- what setup() arms ---
    check(areg_wdt_effective_s(shipped_wdt_s, connect_ms, AREG_POLL_READ_MS) == 60u,
          "the shipped 60 s default already clears the floor and is kept");
    check(areg_wdt_effective_s(stale_bench_wdt_s, connect_ms, AREG_POLL_READ_MS) == 53u,
          "the stale 30 s bench override is raised to the floor");
    check(areg_wdt_effective_s(0, connect_ms, AREG_POLL_READ_MS) == 53u,
          "an unset or zero value cannot disable the margin");
    check(areg_wdt_effective_s(120, connect_ms, AREG_POLL_READ_MS) == 120u,
          "a deliberately larger value is respected");

    // --- the invariant, over every plausible configured value ---
    bool never_below_call = true, never_lowers = true;
    const uint32_t worst_ms = areg_net_call_worst_ms(connect_ms, AREG_POLL_READ_MS);
    for (uint32_t cfg = 0; cfg <= 300; cfg++) {
        const uint32_t eff = areg_wdt_effective_s(cfg, connect_ms, AREG_POLL_READ_MS);
        if (eff * 1000u <= worst_ms) never_below_call = false;
        if (eff < cfg) never_lowers = false;
    }
    check(never_below_call, "for any configured value the armed watchdog outlasts the worst idle call");
    check(never_lowers, "the armed watchdog is never LOWER than what was configured");

    // --- the regression itself ---
    const uint32_t before_ms = (uint32_t)AREG_NET_DNS_WORST_MS + connect_ms + 120000u + 30000u;
    check(before_ms > stale_bench_wdt_s * 1000u,
          "before the fix a stalled poll could outlast the 30 s watchdog (the 2026-10-29 reset)");
    check(worst_ms < shipped_wdt_s * 1000u,
          "after the fix a stalled poll ends before even the shipped 60 s watchdog");

    printf(failures == 0 ? "PASS\n" : "FAIL (%d)\n", failures);
    return failures == 0 ? 0 : 1;
}

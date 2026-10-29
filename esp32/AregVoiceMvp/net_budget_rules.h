// -------------------------------------------------------------
// AregVoiceMvp / net_budget_rules.h
//
// The network time budget: the few numbers that decide whether a slow or
// dead network can reboot the toy. Pure -- no Arduino core, no ESP-IDF -- so
// the invariant has a host test (host_tests/net_budget_rules_test.cpp).
//
// WHY THIS EXISTS. 2026-10-29, bench: an idle toy rebooted itself with
// reset_reason=6/TASK_WDT 136 minutes after boot. The last line before the
// reset was an ordinary [alive]; the next 60 s heartbeat never came back.
// Three things made one stalled heartbeat fatal:
//
//   1. The TLS handshake had no bound of its own. Core 3.3.6 gives
//      WiFiClientSecure a 120 s default, and HTTPClient's connect and read
//      timeouts do not cover it. (net_transport.cpp now sets it.)
//   2. The heartbeat and the OTA command poll waited up to
//      AREG_HTTP_READ_MS (30 s) for a reply of a few hundred bytes.
//   3. That bench's config.h set AREG_WDT_TIMEOUT_S to 30 -- the same value
//      as the read timeout. That exact collision had already rebooted the
//      toy on 2026-08-16 (see WdtPause in voice_client.cpp). The tracked
//      default and config.h.example say 60; an old local override beat them.
//
// THE RULE. The watchdog is fed once per loop() pass. Anything loop() calls
// that blocks without feeding it must finish -- by its OWN timeouts -- well
// inside the watchdog, or a slow network turns into a reboot. The idle polls
// (heartbeat, command poll) therefore use the short AREG_POLL_READ_MS, and
// setup() never lets the watchdog sit below the worst such call. A stale
// config.h can no longer reintroduce the collision: the firmware raises the
// value and says so on the serial line.
//
// The floor covers ONE call, so loop() feeds the watchdog after every network
// call it chains on the idle path (heartbeat, OTA poll, both play reports, the
// OTA check-in's heartbeat then ack), and the few calls that still wait the
// long AREG_HTTP_READ_MS (content-sync manifest, OTA manifest and image GET)
// start on a fresh window. tools/firmware/test_net_budget_sites.py reads the
// sources and fails if any of that is reverted. The allowances below add every
// phase together on purpose; a real stall hits one phase, not all of them.
//
// What this does NOT do: bound DNS (lwIP gives up on its own, 20 s is the
// allowance below) or make a blocked loop responsive -- the button is dead
// for the length of a stalled poll, which is why the polls are short.
// -------------------------------------------------------------
#pragma once

#include <stdint.h>

// TLS handshake bound, seconds. A normal handshake to the backend takes well
// under a second; 10 s is generous for a weak link and a hard stop for a dead
// one. Core 3.3.6's own default is 120.
#ifndef AREG_TLS_HANDSHAKE_S
#define AREG_TLS_HANDSHAKE_S 10
#endif

// Reply wait for the small idle calls (heartbeat, command poll, ack, story
// token, play reports). Their replies are tiny. Calls that wait on STT, the
// model or TTS keep AREG_HTTP_READ_MS and pause the watchdog (WdtPause).
#ifndef AREG_POLL_READ_MS
#define AREG_POLL_READ_MS 8000
#endif

// Allowance for a DNS lookup that never answers (not controllable per call).
#ifndef AREG_NET_DNS_WORST_MS
#define AREG_NET_DNS_WORST_MS 20000
#endif

// Headroom kept between the worst bounded call and the watchdog.
#ifndef AREG_WDT_MARGIN_S
#define AREG_WDT_MARGIN_S 10
#endif

/// Longest a single small network call can block loop(): DNS, TCP connect,
/// TLS handshake, then the reply wait.
inline uint32_t areg_net_call_worst_ms(uint32_t connect_ms, uint32_t read_ms) {
    return (uint32_t)AREG_NET_DNS_WORST_MS + connect_ms
         + (uint32_t)AREG_TLS_HANDSHAKE_S * 1000u + read_ms;
}

/// Smallest watchdog (seconds) that outlasts that call with the margin.
inline uint32_t areg_wdt_min_s(uint32_t connect_ms, uint32_t read_ms) {
    return (areg_net_call_worst_ms(connect_ms, read_ms) + 999u) / 1000u
         + (uint32_t)AREG_WDT_MARGIN_S;
}

/// The watchdog setup() actually arms: the configured value, raised to the
/// floor when it is below it. A value at or above the floor is kept as is.
inline uint32_t areg_wdt_effective_s(uint32_t configured_s,
                                     uint32_t connect_ms, uint32_t read_ms) {
    const uint32_t floor_s = areg_wdt_min_s(connect_ms, read_ms);
    return configured_s < floor_s ? floor_s : configured_s;
}

#!/usr/bin/env python3
"""Source guard for the network time budget (esp32/AregVoiceMvp/net_budget_rules.h).

A host test can prove the arithmetic, but not that the firmware still USES it:
that net_transport.cpp bounds the TLS handshake, that the small idle calls wait
the short time, that chained idle calls are separated by a watchdog feed, and
that the card index is read through the one retrying reader. This reads the
sources and fails if a call site is reverted - the way the 2026-10-29 reboot
(a stalled heartbeat outlasting the watchdog) would come back.

Stdlib only; no toolchain, no toy.

USAGE
    python3 tools/firmware/test_net_budget_sites.py -v
"""
from __future__ import annotations

import re
import unittest
from pathlib import Path

SKETCH = Path(__file__).resolve().parents[2] / "esp32" / "AregVoiceMvp"


def read(name: str) -> str:
    return (SKETCH / name).read_text(encoding="utf-8", errors="replace").replace("\r\n", "\n")


def body_of(src: str, signature: str) -> str:
    """Text of the function whose header matches `signature` (regex), braces
    balanced, skipping strings, char literals and comments."""
    m = re.search(signature, src)
    assert m, f"function not found: {signature}"
    i = src.index("{", m.end() - 1) if src[m.end() - 1] != "{" else m.end() - 1
    depth, j, n = 0, i, len(src)
    while j < n:
        c = src[j]
        if src.startswith("//", j):
            j = src.find("\n", j)
            j = n if j < 0 else j
            continue
        if src.startswith("/*", j):
            j = src.find("*/", j) + 2
            continue
        if c in "\"'":
            q = c
            j += 1
            while j < n and src[j] != q:
                j += 2 if src[j] == "\\" else 1
        elif c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                return src[i:j + 1]
        j += 1
    raise AssertionError(f"unbalanced braces after {signature}")


def fed_just_before(src: str, statement: str, window: int = 400) -> bool:
    k = src.index(statement)
    return "esp_task_wdt_reset();" in src[max(0, k - window):k]


class TlsHandshakeIsBounded(unittest.TestCase):
    def test_shared_client_sets_the_handshake_timeout(self):
        body = body_of(read("net_transport.cpp"), r"static\s+WiFiClientSecure\s*&\s*tls_client\s*\(\s*\)\s*\{")
        self.assertIn("setHandshakeTimeout(AREG_TLS_HANDSHAKE_S)", body,
                      "core 3.3.6 defaults the handshake to 120 s; nothing else bounds it")

    def test_there_is_only_one_tls_client(self):
        # Only real declarations count: comments and the include are fine.
        decl = re.compile(r"\bnew\s+WiFiClientSecure\b|\bWiFiClientSecure\s+[A-Za-z_]\w*\s*[;({=]")
        offenders = []
        for f in sorted(SKETCH.glob("*.cpp")) + [SKETCH / "AregVoiceMvp.ino"]:
            if f.name == "net_transport.cpp":
                continue
            for n, line in enumerate(read(f.name).splitlines(), 1):
                code = line.split("//", 1)[0]
                if decl.search(code):
                    offenders.append(f"{f.name}:{n}")
        self.assertEqual(offenders, [], "a second TLS client would bypass the bounded handshake")


class IdleCallsWaitTheShortTime(unittest.TestCase):
    CASES = [
        ("voice_client.cpp", r"void\s+voice_send_heartbeat\s*\(\s*\)\s*\{"),
        ("voice_client.cpp", r"int\s+voice_post_story_plays\s*\([^)]*\)\s*\{"),
        ("voice_client.cpp", r"int\s+voice_post_game_plays\s*\([^)]*\)\s*\{"),
        ("voice_client.cpp", r"bool\s+voice_fetch_story_audio_token\s*\([^)]*\)\s*\{"),
        ("ota_foundation.cpp", r"static\s+void\s+poll_commands\s*\(\s*\)\s*\{"),
        ("ota_foundation.cpp", r"static\s+bool\s+ack_command\s*\([^)]*\)\s*\{"),
    ]

    def test_small_idle_calls_use_the_poll_read_timeout(self):
        for fname, sig in self.CASES:
            with self.subTest(function=sig.split(r"\s+")[-1][:40], file=fname):
                body = body_of(read(fname), sig)
                self.assertIn("setTimeout(AREG_POLL_READ_MS)", body)
                self.assertNotIn("setTimeout(AREG_HTTP_READ_MS)", body,
                                 "a 30 s reply wait on a tiny reply can outlast the watchdog")

    def test_voice_intent_keeps_the_long_read_but_pauses_the_watchdog(self):
        body = body_of(read("voice_client.cpp"), r"bool\s+voice_post_voice_intent\s*\([^)]*\)\s*\{")
        self.assertIn("setTimeout(AREG_HTTP_READ_MS)", body)
        self.assertIn("WdtPause", body, "its reply waits on speech-to-text")


class WatchdogIsFedBetweenChainedCalls(unittest.TestCase):
    def test_idle_chain_in_loop(self):
        body = body_of(read("AregVoiceMvp.ino"), r"void\s+loop\s*\(\s*\)\s*\{")
        for call in ("voice_send_heartbeat();", "ota_foundation_tick();",
                     "story_report_tick();", "game_report_tick();"):
            with self.subTest(call=call):
                self.assertRegex(body, re.escape(call) + r"\s*esp_task_wdt_reset\(\);",
                                 "the budget covers ONE network call per feed")

    def test_ota_checkin_feeds_between_heartbeat_and_ack(self):
        body = body_of(read("ota_foundation.cpp"), r"static\s+void\s+ota_checkin_tick\s*\(\s*\)\s*\{")
        self.assertRegex(body, r"voice_send_heartbeat\(\);\s*esp_task_wdt_reset\(\);")

    def test_long_reads_start_on_a_fresh_window(self):
        ota = read("ota_apply.cpp")
        self.assertTrue(fed_just_before(ota, "const int mstatus = mh.GET();"))
        self.assertTrue(fed_just_before(ota, "const int dstatus = http.GET();"))
        self.assertTrue(fed_just_before(read("content_sync.cpp"), "const int mstatus = http.GET();"))

    def test_setup_arms_the_computed_watchdog(self):
        ino = read("AregVoiceMvp.ino")
        self.assertIn("areg_wdt_effective_s(", ino)
        self.assertIn("wdt_cfg.timeout_ms = wdt_s * 1000u;", ino,
                      "setup must arm the raised value, not the raw config.h one")


class IndexIsReadThroughOneReader(unittest.TestCase):
    def test_no_direct_index_reads_outside_the_reader_and_the_writer(self):
        for name in ("story_select.cpp", "content_report.cpp"):
            with self.subTest(file=name):
                src = read(name)
                self.assertNotIn('"/content_index.json"', src, "read the index only via content_index_load()")
                self.assertIn("content_index_load(", src)

    def test_content_sync_reads_afresh(self):
        src = read("content_sync.cpp")
        self.assertIn('content_index_load(doc, "content-sync", /*fresh=*/true)', src,
                      "sync decides whether to repair or strike the card: it must not inherit another reader's memo")


if __name__ == "__main__":
    unittest.main()

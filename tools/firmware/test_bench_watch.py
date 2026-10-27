#!/usr/bin/env python3
"""Proves bench_watch.py scores the SHIP evidence the way it claims.

Dependency-free (stdlib unittest only, no pyserial, no toy, no toolchain),
same discipline as test_check_release_image.py: synthetic serial logs go
through the Watcher exactly as live lines would, with controlled
timestamps where the point is timing. The line shapes are the ones the
firmware really prints (grepped from esp32/AregVoiceMvp/*.ino|cpp and
checked against tools/quality-evidence/*.log), including the benign
`task_wdt: ... already initialized` line every boot prints.

USAGE
    python3 -I tools/firmware/test_bench_watch.py -v
"""
from __future__ import annotations

import contextlib
import datetime as dt
import io
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import bench_watch as bw  # noqa: E402

EVIDENCE = Path(__file__).resolve().parents[1] / "quality-evidence"
DASH = "—"   # the firmware's em dash ...
MOJI = "???"      # ... which the saved logs show as this


def boot_lines(num: int = 1, name: str = "POWERON") -> list[str]:
    """What a real boot prints (button-jumper3-20260819.log / menu-redesign)."""
    return [
        "ESP-ROM:esp32s3-20210327",
        "rst:0x15 (USB_UART_CHIP_RESET),boot:0x2b (SPI_FAST_FLASH_BOOT)",
        "[boot] AregVoiceMvp starting",
        f"[boot] reset_reason={num}/{name}",
        "E (225) task_wdt: esp_task_wdt_init(517): TWDT already initialized",
    ]


QA = "[qa] answer played (buffered POST response)"
QA_STREAM = "[qa] answer played (streamed POST response)"
REFLECT = "[post] reply 0 played"
CHAT = f"[chat] turn 2 (riddle) played {DASH} listening"
WDT = ("E (30980) task_wdt: Task watchdog got triggered. The following "
       "tasks/users did not reset the watchdog in time:")


def sel(story: str, n: int = 10) -> str:
    return f"[story] selected {story} ({n} eligible)"


def run_out(lines, *, t0: float | None = None, step: float = 1.0, echo: bool = False):
    """Feed lines; with t0 each line is stamped t0 + i*step, else untimed.
    Returns (watcher, everything it printed)."""
    w = bw.Watcher(echo=echo)
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        for i, line in enumerate(lines):
            w.feed(line, None if t0 is None else t0 + i * step)
    return w, buf.getvalue()


def run(lines, **kw) -> bw.Watcher:
    return run_out(lines, **kw)[0]


def feed_at(w: bw.Watcher, stamped) -> None:
    with contextlib.redirect_stdout(io.StringIO()):
        for t, line in stamped:
            w.feed(line, t)


def captured(fn, *a, **k) -> str:
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        fn(*a, **k)
    return buf.getvalue()


class ChainCounterTests(unittest.TestCase):
    """SHIP C1."""

    def test_ten_chains_with_no_reset_pass(self):
        w = run(boot_lines() + [QA] * 10)
        c = w.check_c1()
        self.assertEqual(w.longest_run, 10)
        self.assertEqual(c.verdict, "PASS")
        self.assertEqual(c.headline,
                         "SHIP C1  longest run of voice chains without a reset: 10 (needs 10)")

    def test_nine_chains_is_not_yet(self):
        w = run(boot_lines() + [QA] * 9)
        self.assertEqual(w.check_c1().verdict, "NOT YET")

    def test_three_chain_shapes_all_count(self):
        w = run(boot_lines() + [QA, QA_STREAM, REFLECT, CHAT,
                                f"[chat] turn 3 (game) played {MOJI} listening"])
        self.assertEqual(w.chains, 5)
        self.assertEqual(dict(w.chain_kinds), {"qa": 2, "reflection": 1, "chat": 2})

    def test_task_watchdog_in_the_middle_breaks_the_run(self):
        lines = (boot_lines() + [QA] * 5 + [WDT, "E (30980) task_wdt: Aborting.", "Rebooting..."]
                 + boot_lines(6, "TASK_WDT") + [QA] * 5)
        w = run(lines)
        c = w.check_c1()
        self.assertEqual(w.chains, 10)
        self.assertEqual(w.longest_run, 5)
        self.assertLess(w.longest_run, bw.C1_NEEDS)
        self.assertEqual(c.verdict, "FAIL")   # ten were attempted and the run never reached ten
        self.assertTrue(any("task watchdog" in t for t in w.troubles))
        self.assertTrue(any("after 5 chain(s)" in n for n in c.notes))

    def test_the_benign_twdt_init_line_is_not_a_watchdog(self):
        w = run(boot_lines() + [QA] * 4
                + ["E (228) task_wdt: esp_task_wdt_init(517): TWDT already initialized"]
                + [QA] * 6)
        self.assertEqual(w.longest_run, 10)
        self.assertEqual(w.check_c1().verdict, "PASS")
        self.assertEqual(w.troubles, [])

    def test_guru_meditation_breaks_the_run(self):
        w = run(boot_lines() + [QA] * 6
                + ["Guru Meditation Error: Core  1 panic'ed (LoadProhibited). Exception was unhandled."]
                + [QA] * 4)
        self.assertEqual(w.longest_run, 6)

    def test_a_second_boot_banner_breaks_the_run_but_the_first_does_not(self):
        w = run(boot_lines() + [QA] * 4 + boot_lines() + [QA] * 3)
        self.assertEqual(w.boots, 2)
        self.assertEqual(w.longest_run, 4)

    def test_joining_mid_boot_a_reset_reason_line_alone_counts_as_a_boot(self):
        w = run([QA] * 3 + ["[boot] reset_reason=4/PANIC"] + [QA] * 2)
        self.assertEqual(w.longest_run, 3)
        self.assertEqual(len(w.boot_events), 1)

    def test_a_failed_chain_ends_a_run_ten_in_a_row_means_in_a_row(self):
        w = run(boot_lines() + [QA] * 5
                + ["[qa] upload failed (status=500); resuming"] + [QA] * 5)
        c = w.check_c1()
        self.assertEqual(w.longest_run, 5)
        self.assertEqual(w.longest_reset_only, 10)   # no reset happened
        self.assertEqual(w.chains_failed, 1)
        self.assertEqual(c.verdict, "FAIL")
        self.assertTrue(any("counting resets only" in n for n in c.notes))

    def test_other_failure_shapes_are_failed_chains(self):
        for bad in ("[qa] streamed answer failed; playing failure clip",
                    "[post] reflection upload failed (status=502)",
                    "[chat] upload failed (http=500)",
                    "[chat] decoder error"):
            with self.subTest(bad=bad):
                w = run(boot_lines() + [QA, bad, QA])
                self.assertEqual(w.chains_failed, 1)
                self.assertEqual(w.longest_run, 1)

    def test_no_chains_says_how_to_make_one(self):
        c = run(boot_lines()).check_c1()
        self.assertEqual(c.verdict, "NOT YET")
        self.assertTrue(any("press during a story" in n for n in c.notes))


class SelectionTests(unittest.TestCase):
    """SHIP A6."""

    def test_three_distinct_selections_and_a_power_cycle_pass(self):
        lines = (boot_lines() + [sel("ulik"), sel("hedgehog-apple")]
                 + boot_lines(1, "POWERON") + [sel("three-piglets"), sel("little-cloud")])
        w = run(lines)
        c = w.check_a6()
        self.assertEqual(c.verdict, "PASS")
        self.assertEqual(c.headline,
                         "SHIP A6  4 selections, adjacent repeats: 0, power-cycles between "
                         "selections: 1 (needs ≥3 selections + ≥1 power-cycle, 0 repeats)")
        self.assertIn("ulik -> hedgehog-apple -> [power-cycle] -> three-piglets -> little-cloud",
                      " ".join(c.notes))

    def test_adjacent_repeat_is_flagged_with_its_line(self):
        w = run(boot_lines() + [sel("ulik"), sel("ulik"), sel("khosogh-dzuk")])
        c = w.check_a6()
        self.assertEqual(c.verdict, "FAIL")
        self.assertIn("adjacent repeats: 1", c.headline)
        self.assertTrue(any(n.startswith("REPEAT: ulik") and "log line 7" in n for n in c.notes))

    def test_a_repeat_across_a_power_cycle_is_still_a_repeat(self):
        w = run(boot_lines() + [sel("ulik")] + boot_lines() + [sel("ulik")])
        self.assertEqual(w.check_a6().verdict, "FAIL")

    def test_three_selections_with_no_power_cycle_is_not_yet(self):
        c = run(boot_lines() + [sel("a"), sel("b"), sel("c")]).check_a6()
        self.assertEqual(c.verdict, "NOT YET")
        self.assertTrue(any("power-cycle" in n and "unplug" in n for n in c.notes))

    def test_a_crash_reboot_between_selections_is_not_a_power_cycle(self):
        lines = (boot_lines() + [sel("a"), sel("b")] + boot_lines(4, "PANIC")
                 + [sel("c"), sel("d")])
        c = run(lines).check_a6()
        self.assertEqual(c.verdict, "NOT YET")
        self.assertIn("power-cycles between selections: 0", c.headline)
        self.assertTrue(any("other reboot" in n for n in c.notes))

    def test_a_software_reset_is_not_a_power_cycle_either(self):
        lines = boot_lines() + [sel("a")] + boot_lines(3, "SW") + [sel("b"), sel("c")]
        self.assertIn("power-cycles between selections: 0", run(lines).check_a6().headline)

    def test_usb_cable_reset_counts_as_a_power_cycle(self):
        # the firmware has no name for reset_reason 11 (USB) and prints UNKNOWN
        lines = boot_lines() + [sel("a")] + boot_lines(11, "UNKNOWN") + [sel("b"), sel("c")]
        self.assertEqual(run(lines).check_a6().verdict, "PASS")

    def test_a_selection_that_did_not_resolve_is_trouble_not_a_selection(self):
        w = run(boot_lines() + [f"[story] selected ulik but it did not resolve {MOJI} using fallback chain",
                                sel("a")])
        self.assertEqual(len(w.selections), 1)
        self.assertEqual(w.selections[0][0], "a")
        self.assertEqual(w.selections_unresolved, 1)
        self.assertTrue(any("did not resolve" in t for t in w.troubles))
        self.assertTrue(any("1 selection(s) did not resolve" in n for n in w.check_a6().notes))

    def test_browse_and_menu_starts_are_not_scored(self):
        w = run(boot_lines() + [sel("a"), "[browse] a -> b (next)",
                                "[welcome] playing chosen story c", sel("d"), sel("e")])
        c = w.check_a6()
        self.assertEqual(len(w.selections), 3)
        self.assertTrue(any("not rotation selections" in n for n in c.notes))

    def test_selection_with_a_leading_timestamp_column_parses(self):
        w = run(["   95.8  [boot] AregVoiceMvp starting", "  101.0  " + sel("ulik")])
        self.assertEqual(w.selections[0][0], "ulik")


class BargeInTests(unittest.TestCase):
    """SHIP A4 - the log holds the 'toy responds' half only. A press->cut
    interval is not in it: `[button] pressed` is idle-only and the barge-in
    line is printed at the instant the decoder stops."""

    SD = "[story] SD barge-in: abs=1581 resume_from=0"
    STREAM = "[story] barge-in: base=0 rel_pos=4000 abs=4000 resume_from=0"
    REC = "[audio] record_begin"

    def test_cut_listen_answer_counts_as_answered(self):
        w = run(boot_lines() + ["[button] raw=DOWN", self.SD, "[story] SD end interrupted=true",
                                "[state] 3 -> 1", self.REC, QA_STREAM])
        self.assertEqual(w.a4_counts(), (1, 1, 1))
        c = w.check_a4()
        self.assertEqual(c.headline, "SHIP A4  1 barge-ins, 1 answered; the <1 s stop is "
                                     "attested by ear or phone video, not by this log")

    def test_both_barge_in_shapes_are_counted(self):
        w = run([self.SD, self.REC, QA, self.STREAM, self.REC, QA])
        self.assertEqual(w.counts["barge_in"], 2)
        self.assertEqual(w.a4_counts(), (2, 2, 2))

    def test_a_cut_with_no_recording_is_not_answered(self):
        w = run([self.SD, "[story] SD end interrupted=true", sel("a")])
        self.assertEqual(w.a4_counts(), (1, 0, 0))

    def test_an_answer_with_no_prior_cut_is_not_credited_to_a4(self):
        w = run([QA])
        self.assertEqual(w.a4_counts(), (0, 0, 0))

    def test_a_late_record_begin_does_not_attach_to_an_old_cut(self):
        # cut -> new story selected -> much later a reflection answer records
        w = run([self.SD, sel("b"), self.REC, REFLECT])
        self.assertEqual(w.a4_counts(), (1, 0, 0))

    def test_a_browse_cut_is_not_a_question(self):
        w = run([self.SD, "[browse] a -> b (next)", self.REC, QA])
        self.assertEqual(w.a4_counts(), (0, 0, 0))
        self.assertTrue(any("browse cut" in n for n in w.check_a4().notes))

    def test_each_answer_is_credited_to_its_own_cut(self):
        w = run([self.SD, self.REC, QA, self.SD, self.REC, "[qa] upload failed (status=500); resuming"])
        self.assertEqual(w.a4_counts(), (2, 2, 1))

    def test_never_passes_from_the_log_alone(self):
        lines = []
        for _ in range(6):
            lines += [self.SD, self.REC, QA]
        c = run(lines).check_a4()
        self.assertEqual(c.verdict, "NOT YET")
        self.assertIn("6 barge-ins, 6 answered", c.headline)
        self.assertIn("phone video", " ".join(c.notes))
        self.assertIn("the log holds no press-to-silence interval", " ".join(c.notes))

    def test_fewer_than_five_says_so(self):
        c = run([self.SD, self.REC, QA]).check_a4()
        self.assertTrue(any("only 1 barge-in" in n for n in c.notes))

    def test_no_barge_in_says_how_to_make_one(self):
        c = run(boot_lines()).check_a4()
        self.assertIn("0 barge-ins, 0 answered", c.headline)
        self.assertTrue(any("press the main button while a story plays" in n for n in c.notes))

    def test_timestamps_change_nothing_about_a4(self):
        stamped = run([self.SD, self.REC, QA], t0=10.0, step=0.3).check_a4()
        plain = run([self.SD, self.REC, QA]).check_a4()
        self.assertEqual((stamped.headline, stamped.verdict), (plain.headline, plain.verdict))

    def test_no_press_to_cut_interval_is_ever_reported(self):
        w, out = run_out(["[button] raw=DOWN", self.SD], t0=10.0, step=0.3)
        self.assertNotIn(" ms after the press", out)
        self.assertFalse(hasattr(w, "a4_samples"))


class WifiRecoveryTests(unittest.TestCase):
    """SHIP C3."""

    UP = "[wifi] event=got_ip"
    DOWN = "[wifi] event=disconnected reason=8"

    def test_drop_reconnect_and_a_chain_pass(self):
        w = bw.Watcher(echo=False)
        feed_at(w, [(1.0, b) for b in boot_lines()[:4]])
        feed_at(w, [(10.0, self.UP), (100.0, self.DOWN), (110.0, self.DOWN),
                    (135.0, self.UP), (150.0, QA)])
        c = w.check_c3()
        self.assertEqual(c.verdict, "PASS")
        self.assertIn("outage 35 s", " ".join(c.notes))

    def test_a_short_blip_before_the_real_test_does_not_hide_the_recovery(self):
        # a 5 s router blip (with a chain after it), THEN the real 35 s test
        w = bw.Watcher(echo=False)
        feed_at(w, [(1.0, self.UP), (50.0, self.DOWN), (55.0, self.UP), (60.0, QA),
                    (100.0, self.DOWN), (135.0, self.UP), (150.0, QA)])
        c = w.check_c3()
        self.assertEqual(c.verdict, "PASS")
        notes = " ".join(c.notes)
        self.assertIn("outage 35 s", notes)
        self.assertIn("drop 1: 5 s", notes)          # every outage is reported
        self.assertIn("drop 2: 35 s", notes)

    def test_the_longest_qualifying_outage_is_the_one_reported(self):
        w = bw.Watcher(echo=False)
        feed_at(w, [(1.0, self.UP), (50.0, self.DOWN), (75.0, self.UP), (80.0, QA),
                    (100.0, self.DOWN), (160.0, self.UP), (170.0, QA)])
        c = w.check_c3()
        self.assertEqual(c.verdict, "PASS")
        self.assertIn("outage 60 s", " ".join(c.notes))

    def test_only_short_outages_report_the_longest_one(self):
        w = bw.Watcher(echo=False)
        feed_at(w, [(1.0, self.UP), (50.0, self.DOWN), (55.0, self.UP), (60.0, QA),
                    (100.0, self.DOWN), (108.0, self.UP), (110.0, QA)])
        c = w.check_c3()
        self.assertEqual(c.verdict, "NOT YET")
        self.assertIn("longest recovered outage lasted only 8 s", " ".join(c.notes))

    def test_a_long_outage_that_never_got_a_chain_does_not_count_over_a_short_one(self):
        w = bw.Watcher(echo=False)
        feed_at(w, [(1.0, self.UP), (50.0, self.DOWN), (55.0, self.UP), (60.0, QA),
                    (100.0, self.DOWN), (160.0, self.UP)])       # 60 s, but no chain after
        c = w.check_c3()
        self.assertEqual(c.verdict, "NOT YET")
        self.assertIn("drop 2: 60 s, link back but no voice chain", " ".join(c.notes))

    def test_boot_time_disconnects_are_not_an_outage(self):
        w = run(boot_lines() + [self.DOWN, self.DOWN, self.UP, QA], t0=1.0)
        self.assertEqual(w.outages, [])
        self.assertEqual(w.check_c3().verdict, "NOT YET")

    def test_a_five_second_blip_is_not_the_thirty_second_test(self):
        w = bw.Watcher(echo=False)
        feed_at(w, [(1.0, self.UP), (50.0, self.DOWN), (55.0, self.UP), (60.0, QA)])
        c = w.check_c3()
        self.assertEqual(c.verdict, "NOT YET")
        self.assertIn("only 5 s", " ".join(c.notes))

    def test_reconnect_without_a_chain_is_not_yet(self):
        w = bw.Watcher(echo=False)
        feed_at(w, [(1.0, self.UP), (50.0, self.DOWN), (90.0, self.UP)])
        c = w.check_c3()
        self.assertEqual(c.verdict, "NOT YET")
        self.assertTrue(any("no voice chain completed" in n for n in c.notes))

    def test_a_reboot_during_the_outage_is_not_a_recovery(self):
        w = bw.Watcher(echo=False)
        feed_at(w, [(1.0, self.UP), (50.0, self.DOWN)])
        feed_at(w, [(60.0, "[boot] AregVoiceMvp starting"), (70.0, self.UP), (80.0, QA)])
        self.assertEqual(w.check_c3().verdict, "NOT YET")

    def test_a_replayed_log_can_prove_recovery_but_not_its_length(self):
        w = run([self.UP, self.DOWN, self.UP, QA])
        c = w.check_c3()
        self.assertEqual(c.verdict, "PASS")
        self.assertIn("not measurable from a replayed log", " ".join(c.notes))


class WifiProvisioningFallbackTests(unittest.TestCase):
    PROV = f"[prov] long Wi-Fi outage {DASH} opening BLE provisioning for re-onboarding"

    def test_ble_fallback_is_a_warning_event_and_not_a_recovery(self):
        w = bw.Watcher(echo=False)
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            for t, ln in [(1.0, "[wifi] event=got_ip"), (50.0, "[wifi] event=disconnected reason=8"),
                          (351.0, self.PROV), (400.0, "[wifi] event=got_ip"), (410.0, QA)]:
                w.feed(ln, t)
        self.assertIn("PROV", buf.getvalue())
        self.assertEqual(w.counts["prov_outage"], 1)
        c = w.check_c3()
        self.assertEqual(c.verdict, "NOT YET")
        self.assertTrue(any("needed an adult" in n for n in c.notes))
        self.assertTrue(any("BLE provisioning opened" in m for m in w._marker_lines()))

    def test_the_other_prov_lines_are_not_this_event(self):
        w = run([f"[prov] button held at boot {DASH} forgetting Wi-Fi, entering provisioning"])
        self.assertEqual(w.counts["prov_outage"], 0)


class SessionDenominatorTests(unittest.TestCase):
    """SHIP D3 needs a denominator: online turns and minutes."""

    def test_turns_and_minutes(self):
        w = bw.Watcher(echo=False)
        feed_at(w, [(100.0, "[boot] AregVoiceMvp starting"), (130.0, QA), (400.0, REFLECT),
                    (700.0, CHAT), (820.0, "[alive] ms=1")])
        c = w.check_d3()
        self.assertEqual(c.headline, "SHIP D3  3 online turns in 12.0 min "
                                     "(the denominator for the provider bill)")
        self.assertEqual(c.verdict, "NOT YET")
        self.assertIn("qa 1", " ".join(c.notes))
        self.assertIn("reflection 1", " ".join(c.notes))
        self.assertIn("chat 1", " ".join(c.notes))

    def test_failed_turns_are_named_not_counted(self):
        w = bw.Watcher(echo=False)
        feed_at(w, [(0.0, QA), (60.0, "[qa] upload failed (status=500); resuming")])
        c = w.check_d3()
        self.assertIn("1 online turns in 1.0 min", c.headline)
        self.assertIn("1 failed turn(s)", " ".join(c.notes))

    def test_replay_says_the_length_is_unknown(self):
        w = run([QA, REFLECT])
        self.assertIn("2 online turns in an unknown number of minutes (replayed log, no clock)",
                      w.check_d3().headline)
        self.assertIsNone(w.session_minutes())
        self.assertIn("unknown (a replayed log has no clock)", w.session_text())

    def test_evidence_header_carries_the_denominator(self):
        w = bw.Watcher(echo=False)
        feed_at(w, [(0.0, QA), (150.0, QA)])
        text = w.render_evidence(when=dt.datetime(2026, 10, 22, 19, 30), source="s", raw_log="r",
                                 raw_sha256="0" * 64, port=None)
        self.assertIn("- Session: 2.5 min, first line to last line on the host clock; 2 online turn(s) completed", text)
        self.assertIn("| D3 | NOT YET |", text)


class MarkerTests(unittest.TestCase):
    """Welcome / online / games markers are events, not dim echo."""

    def test_unknown_intent_is_a_warning_and_counted(self):
        _w, out = run_out(["[welcome] intent=unknown"], echo=True)
        self.assertIn("INTENT", out)
        self.assertIn("voice-only doors", out)
        w = run(["[welcome] intent=unknown", "[welcome] intent=story", "[welcome] intent=unknown"])
        self.assertEqual(w.counts["intent:unknown"], 2)
        self.assertEqual(w.counts["intent:story"], 1)
        self.assertTrue(any("welcome intents: story 1, unknown 2" in m for m in w._marker_lines()))
        self.assertTrue(any("WARN" in m and "did not resolve" in m for m in w._marker_lines()))

    def test_a_known_intent_is_not_a_warning(self):
        _w, out = run_out(["[welcome] intent=riddle"])
        self.assertNotIn("voice-only doors", out)
        self.assertIn("riddle", out)

    def test_hold_is_recognised(self):
        _w, out = run_out(["[button] hold -- opening the menu"])
        self.assertIn("HOLD", out)
        self.assertEqual(run(["[button] hold -- opening the menu"]).counts["hold"], 1)

    def test_chat_turns_and_session_ends_are_counted(self):
        w = run([f"[chat] turn 1 (game) played {DASH} listening",
                 f"[chat] silent window {DASH} listening once more",
                 f"[chat] two silent windows {DASH} session over"])
        self.assertEqual(w.counts["chat_turn"], 1)
        self.assertEqual(w.counts["chat_session_end"], 1)
        self.assertEqual(w.chains, 1)

    def test_backend_closed_turn_ends_a_session(self):
        w = run(["[chat] backend closed the turn (X-Areg-Turn-End)"])
        self.assertEqual(w.counts["chat_session_end"], 1)

    def test_offline_game_lines_are_counted_per_game(self):
        w = run(["[games] mind-reader: guessing g-1101", "[games] mind-reader: guessed right",
                 f"[games] simon: tone clip missing {DASH} cannot play",
                 "[game-report] played mind-reader key=b41-1 queued=1"])
        self.assertEqual(w.counts["games:mind-reader"], 2)
        self.assertEqual(w.counts["games:simon"], 1)
        self.assertEqual(w.counts["game_report"], 1)

    def test_bench_autostart_is_called_out(self):
        w = run(["[games] armed (pick=1), starting in 4s"])
        self.assertTrue(any("AREG_OFFLINE_GAMES_BENCH" in t for t in w.troubles))

    def test_paused_toy_and_bedtime_silence_are_trouble(self):
        w = run([f"[button] ignored {DASH} toy is paused",
                 f"[welcome] bedtime window {DASH} staying silent"])
        self.assertEqual(len(w.troubles), 2)
        self.assertTrue(any("PAUSED" in t for t in w.troubles))

    def test_sync_markers_and_the_index_parse_failure(self):
        w = run(["[content-sync] manifest status=200 stories=10 voice=42 games=104",
                 "[content-sync] index written",
                 "[content-sync] voice summary offered=28 already=0 downloaded=28 failed=0 voice_active=28",
                 "[story-select] index parse failed: IncompleteInput (heap=1 psram=2)"])
        self.assertEqual(w.counts["sync_manifest_ok"], 1)
        self.assertEqual(w.counts["sync_index_written"], 1)
        self.assertEqual(len(w.troubles), 1)          # failed=0 is a healthy counter
        self.assertIn("index failed to parse", w.troubles[0])

    def test_real_content_sync_failures_are_trouble(self):
        w = run(["[content-sync] story ulik:offer FAILED (sd_write_failed)",
                 "[content-sync] FAIL (manifest_http)"])
        self.assertEqual(len(w.troubles), 2)

    def test_fw_and_build_come_from_the_boot_poll_line(self):
        w = run(["[ota] boot poll (fw=1.3.5-bench build=bench-20261022 board=areg-s3-n8 partition=app0)"])
        self.assertEqual((w.fw, w.build), ("1.3.5-bench", "bench-20261022"))

    def test_fw_falls_back_to_the_heartbeat_when_wifi_never_joined_the_ota_tick(self):
        w = run(["[heartbeat] status=200 (fw=1.3.3 bedtime=0 paused=0)"])
        self.assertEqual((w.fw, w.build), ("1.3.3", None))
        self.assertEqual(w.counts["heartbeat_ok"], 1)

    def test_unrecognised_lines_still_echo_dimly(self):
        _w, out = run_out(["[alive] ms=5000 heap=1 psram=2 wifi=3 ip=1 rssi=-60 btn=UP",
                           "[button] raw=DOWN"], echo=True)
        self.assertIn("[button] raw=DOWN", out)
        _w, quiet = run_out(["[button] raw=DOWN"], echo=False)
        self.assertNotIn("raw=DOWN", quiet)


class StuckStateTests(unittest.TestCase):
    """The dead-button bug (s_state left != ST_IDLE) makes loop() skip its
    idle branch: AregVoiceMvp.ino:2822-2861 prints `[alive]` ONLY there, so a
    stuck toy goes SILENT. The signature is silence after a non-IDLE
    `[state]`, never `[alive]` lines."""

    ALIVE = "[alive] ms=5000 heap=1 psram=2 wifi=3 ip=1 rssi=-60 btn=UP"

    def ticked(self, stamped, now):
        """Feed (t, line) pairs, then tick once at `now`; return (watcher, output)."""
        w = bw.Watcher(echo=False)
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            for t, line in stamped:
                w.feed(line, t)
            w.tick(now)
        return w, buf.getvalue()

    def test_silence_after_a_non_idle_state_is_the_dead_button_bug(self):
        # a flow returned from PLAYING (3) -> ERROR (4) and never went back to IDLE
        stamped = [(100.0, "[state] 0 -> 3"), (101.0, "[audio] play_end ok=true"),
                   (102.0, "[state] 3 -> 4")]
        w, out = self.ticked(stamped, 102.0 + bw.STUCK_SILENCE_S + 0.5)
        self.assertEqual(w.stuck_events, 1)
        self.assertTrue(any("s_state != ST_IDLE" in t for t in w.troubles))
        self.assertIn("TROUBLE", out)
        # flagged once per episode, however long the silence lasts
        with contextlib.redirect_stdout(io.StringIO()):
            w.tick(10_000.0)
        self.assertEqual(w.stuck_events, 1)
        # a new [state] line is a new episode
        with contextlib.redirect_stdout(io.StringIO()):
            w.feed("[state] 4 -> 3", 20_000.0)
            w.tick(20_000.0 + bw.STUCK_SILENCE_S + 1)
        self.assertEqual(w.stuck_events, 2)

    def test_not_silent_long_enough_is_not_stuck(self):
        stamped = [(100.0, "[state] 0 -> 3"), (101.0, "[audio] play_end ok=true")]
        w, _ = self.ticked(stamped, 101.0 + bw.STUCK_SILENCE_S - 0.5)
        self.assertEqual(w.stuck_events, 0)

    def test_silence_in_idle_is_fine(self):
        stamped = [(100.0, "[state] 0 -> 3"), (101.0, "[state] 3 -> 0")]
        w, _ = self.ticked(stamped, 5_000.0)
        self.assertEqual(w.stuck_events, 0)
        self.assertEqual(w.troubles, [])

    def test_silence_before_any_state_line_is_not_stuck(self):
        w, _ = self.ticked([(100.0, "[boot] AregVoiceMvp starting")], 5_000.0)
        self.assertEqual(w.stuck_events, 0)

    def test_a_story_playing_is_quiet_by_design(self):
        stamped = [(100.0, "[state] 0 -> 3"), (101.0, "[story] SD open: /stories/ulik.mp3 @ 0")]
        w, _ = self.ticked(stamped, 101.0 + 400.0)       # a six-minute story
        self.assertEqual(w.stuck_events, 0)
        # ... but once the audio has ended and the toy is still not IDLE, it is stuck
        with contextlib.redirect_stdout(io.StringIO()):
            w.feed("[story] SD end interrupted=false", 501.0)
            w.tick(501.0 + bw.STUCK_SILENCE_S + 1)
        self.assertEqual(w.stuck_events, 1)

    def test_every_audio_begin_has_a_matching_end_marker(self):
        for begin, end in (("[audio] play_begin bytes=100", "[audio] play_end ok=true"),
                           ("[audio] record_begin", "[audio] record_end bytes=10"),
                           ("[audio] qa_stream_begin url=x", "[audio] qa_stream_end ok=true"),
                           ("[story] stream open: http://x", "[story] stream end interrupted=false"),
                           # early failures print no end line
                           ("[story] SD open: /a.mp3 @ 0", "[story] mp3.begin (SD) failed")):
            with self.subTest(begin=begin):
                w, _ = self.ticked([(1.0, "[state] 0 -> 3"), (2.0, begin)], 9_999.0)
                self.assertEqual(w.stuck_events, 0, "audio in flight is not stuck")
                with contextlib.redirect_stdout(io.StringIO()):
                    w.feed(end, 10_000.0)
                    w.tick(10_000.0 + bw.STUCK_SILENCE_S + 1)
                self.assertEqual(w.stuck_events, 1, "...and silence after it is")

    def test_a_slow_upload_is_not_stuck_until_past_the_upload_timeout(self):
        stamped = [(100.0, "[state] 3 -> 2")]
        w, _ = self.ticked(stamped, 100.0 + 40.0)
        self.assertEqual(w.stuck_events, 0)
        with contextlib.redirect_stdout(io.StringIO()):
            w.tick(100.0 + bw.STUCK_UPLOAD_SILENCE_S + 1)
        self.assertEqual(w.stuck_events, 1)

    def test_a_reboot_clears_the_state(self):
        stamped = [(100.0, "[state] 0 -> 3")] + [(101.0 + i, b) for i, b in enumerate(boot_lines())]
        w, _ = self.ticked(stamped, 5_000.0)
        self.assertEqual(w.stuck_events, 0)

    def test_a_lost_port_is_not_the_toys_silence(self):
        w, _ = self.ticked([(100.0, "[state] 0 -> 3"), (101.0, "[state] 3 -> 4")], 101.0)
        w.note_port_lost()
        with contextlib.redirect_stdout(io.StringIO()):
            w.tick(5_000.0)
        self.assertEqual(w.stuck_events, 0)

    def test_alive_after_a_non_idle_state_is_a_separate_warn_not_the_stuck_verdict(self):
        w, out = run_out(boot_lines() + ["[state] 0 -> 3", "[state] 3 -> 4"] + [self.ALIVE] * 6)
        self.assertEqual(w.stuck_events, 0)
        self.assertEqual(w.troubles, [])
        self.assertEqual(w.alive_nonidle_warnings, 1)        # once per episode
        self.assertIn("unexpected, check the firmware", out)
        self.assertTrue(any("`[alive]` lines after a non-IDLE state" in m
                            for m in w._marker_lines()))

    def test_alive_in_idle_is_fine(self):
        w = run(boot_lines() + ["[state] 0 -> 3", "[state] 3 -> 0"] + [self.ALIVE] * 6)
        self.assertEqual((w.stuck_events, w.alive_nonidle_warnings), (0, 0))
        self.assertEqual(w.troubles, [])

    def test_a_reboot_clears_the_alive_warn_counter(self):
        w = run(["[state] 0 -> 3"] + boot_lines() + [self.ALIVE] * 5)
        self.assertEqual((w.stuck_events, w.alive_nonidle_warnings), (0, 0))

    def test_a_replayed_log_says_the_detector_needs_live_timing(self):
        w = run(boot_lines() + ["[state] 0 -> 3", "[state] 3 -> 4"])        # untimed
        w.tick(10_000.0)                                                     # nothing to measure
        self.assertEqual(w.stuck_events, 0)
        self.assertTrue(any("NOT RUN" in m and "live timing" in m for m in w._marker_lines()))
        text = w.render_evidence(when=dt.datetime(2026, 10, 22, 19, 30),
                                 source="replay of `x.log` (no timestamps)",
                                 raw_log="x.log", raw_sha256="0" * 64, port=None)
        self.assertIn("| R10 | Button alive after every flow | not checked", text)

    def test_a_live_log_with_no_silence_says_so_in_r10(self):
        w = bw.Watcher(echo=False)
        feed_at(w, [(1.0, "[state] 0 -> 3"), (2.0, "[state] 3 -> 0")])
        self.assertFalse(any("NOT RUN" in m for m in w._marker_lines()))
        text = w.render_evidence(when=dt.datetime(2026, 10, 22, 19, 30),
                                 source="live", raw_log="x.log", raw_sha256="0" * 64, port="COM7")
        self.assertIn("| R10 | Button alive after every flow | no stuck-state signature", text)

    def test_the_stuck_verdict_reaches_the_evidence_row(self):
        w, _ = self.ticked([(1.0, "[state] 0 -> 3"), (2.0, "[state] 3 -> 4")], 100.0)
        text = w.render_evidence(when=dt.datetime(2026, 10, 22, 19, 30),
                                 source="live", raw_log="x.log", raw_sha256="0" * 64, port="COM7")
        self.assertIn("| R10 | Button alive after every flow | STUCK-STATE signature seen", text)

    def test_the_live_loop_ticks_on_a_silent_port(self):
        """readline() times out every second with b"" - a dead button prints
        nothing, so the check must run on those empty reads, not on lines."""
        w = bw.Watcher(echo=False)
        first = FakeSerial([b"[state] 0 -> 3\n", b"", b""])      # then KeyboardInterrupt
        times = iter([100.0, 105.0, 120.0])

        class Sink:
            def write(self, s): pass
            def flush(self): pass

        with contextlib.redirect_stdout(io.StringIO()):
            bw.run_live(lambda: first, first, w, Sink(), clock=lambda: next(times),
                        sleep=lambda s: None)
        self.assertEqual(w.stuck_events, 1)


class LatencyTests(unittest.TestCase):
    def test_buffered_line_is_scored_as_before(self):
        w = run(["[latency] qa_release->play_begin_ms=2000"])
        self.assertEqual(w.latencies, [2000])
        self.assertEqual(w.streamed_latencies, 0)

    def test_streaming_first_byte_line_is_scored_too(self):
        # AREG_QA_STREAM_PLAYBACK prints a DIFFERENT key; the original watcher
        # was blind to it, i.e. blind in the very mode it was written for.
        w = run(["[latency] qa_release->first_byte_ms=1800"])
        self.assertEqual(w.latencies, [1800])
        self.assertEqual(w.streamed_latencies, 1)

    def test_the_other_latency_print_is_not_an_answer_sample(self):
        w = run(["[latency] release->play_begin_ms=1234"])
        self.assertEqual(w.latencies, [])

    def test_summary_keeps_its_shape(self):
        w = run(boot_lines() + ["[latency] qa_release->first_byte_ms=1900"] * 5)
        out = captured(w.summary)
        for needle in ("BENCH SUMMARY", "Answer latency, 5 sample(s)", "PASS — streaming is working",
                       "Stories played 0", "No crashes, SD faults or failed turns seen.",
                       "s_state != ST_IDLE bug"):
            self.assertIn(needle, out)

    def test_slow_streamed_samples_do_not_blame_the_flag(self):
        w = run(["[latency] qa_release->first_byte_ms=29518"])
        out = captured(w.summary)
        self.assertIn("too slow even though the answer was streamed", out)
        self.assertNotIn("may not be in the flashed build", out)


class EvidenceTests(unittest.TestCase):
    WHEN = dt.datetime(2026, 10, 22, 19, 30)

    def watcher(self) -> bw.Watcher:
        lines = (boot_lines() + [sel("ulik"), sel("hedgehog-apple")] + boot_lines(1, "POWERON")
                 + [sel("three-piglets"), "[story] finished -- press to play again"]
                 + [QA] * 10)
        w = bw.Watcher(echo=False)
        feed_at(w, [(100.0 + i, ln) for i, ln in enumerate(lines)])
        return w

    def render(self, w) -> str:
        return w.render_evidence(when=self.WHEN, source="live capture on COM7, host-side timestamps",
                                 raw_log="tools/quality-evidence/bench-20261022-1930.log",
                                 raw_sha256="ab" * 32, port="COM7")

    def test_render_is_deterministic(self):
        w = self.watcher()
        self.assertEqual(self.render(w), self.render(w))

    def test_render_contains_every_block(self):
        text = self.render(self.watcher())
        for needle in ("# Bench evidence — 2026-10-22 19:30",
                       "## SHIP.md items this log supports",
                       "| C1 | PASS |", "| A6 | PASS |", "| A4 | NOT YET |", "| C3 | NOT YET |",
                       "| C4 | NOT YET |", "| D3 | NOT YET |",
                       "SHIP C1  longest run of voice chains without a reset: 10 (needs 10)",
                       "power-cycles between selections: 1",
                       "## Answer latency", "## Regression R1–R10", "## Trouble list",
                       "- Session: ", "The under-one-second stop (A4)",
                       "## Welcome / online / games markers", "## Not provable from a serial log",
                       "- Firmware: fw=? build=?", "boot 2: reset_reason=1/POWERON — power-cycle",
                       "sha256 " + "ab" * 32):
            self.assertIn(needle, text)

    def test_a_replayed_log_says_so_in_the_header(self):
        w = run(boot_lines() + [QA])
        text = w.render_evidence(when=self.WHEN, source="replay of `x.log` (no timestamps)",
                                 raw_log="x.log", raw_sha256="0" * 64, port=None)
        self.assertIn("- Timing: none - a replayed log has no timestamps", text)

    def test_evidence_is_never_overwritten(self):
        with tempfile.TemporaryDirectory() as d:
            w = self.watcher()
            raw = Path(d) / "raw.log"
            raw.write_text("x\n", encoding="utf-8")
            kw = dict(evidence_dir=Path(d) / "ev", when=self.WHEN, source="s", raw_log=raw)
            p1 = bw.write_evidence(w, **kw)
            first = p1.read_bytes()
            p2 = bw.write_evidence(w, **kw)
            p3 = bw.write_evidence(w, **kw)
            self.assertEqual(p1.name, "bench-20261022-1930.md")
            self.assertEqual(p2.name, "bench-20261022-1930-2.md")
            self.assertEqual(p3.name, "bench-20261022-1930-3.md")
            self.assertEqual(p1.read_bytes(), first)

    def test_default_evidence_dir_is_under_the_repo_root_not_the_cwd(self):
        self.assertEqual(bw.EVIDENCE_DIR, Path(bw.__file__).resolve().parents[2]
                         / "tools" / "quality-evidence")
        self.assertTrue((bw.REPO_ROOT / "SHIP.md").is_file())

    def test_unique_path_skips_existing_files(self):
        with tempfile.TemporaryDirectory() as d:
            base = Path(d) / "a.log"
            self.assertEqual(bw.unique_path(base), base)
            base.write_text("x")
            self.assertEqual(bw.unique_path(base).name, "a-2.log")


class FakeSerial:
    """Stands in for serial.Serial. `script` items: bytes (a line), an
    Exception instance (raised from readline), or KeyboardInterrupt."""

    opened: list = []

    def __init__(self, script=None):
        self.script = list(script or [])
        self.dtr = True       # pyserial's defaults
        self.rts = True
        self.port = None
        self.baudrate = None
        self.timeout = None
        self.closed = False
        self.at_open = None

    def open(self):
        self.at_open = (self.dtr, self.rts)
        FakeSerial.opened.append(self)

    def readline(self):
        if not self.script:
            raise KeyboardInterrupt
        item = self.script.pop(0)
        if isinstance(item, BaseException):
            raise item
        return item

    def close(self):
        self.closed = True


class FakeSerialModule:
    def __init__(self, script):
        self._script = script

    def Serial(self):  # noqa: N802 - mirrors pyserial
        return FakeSerial(self._script)


class LiveLoopTests(unittest.TestCase):
    def setUp(self):
        FakeSerial.opened = []

    def test_dtr_and_rts_are_low_when_the_port_opens(self):
        ser = bw.open_serial(FakeSerialModule([]), "COM7", 115200)
        self.assertEqual(ser.at_open, (False, False))
        self.assertEqual((ser.port, ser.baudrate), ("COM7", 115200))

    def test_a_vanished_port_is_waited_out_and_reopened(self):
        w = bw.Watcher(echo=False)
        first = FakeSerial([b"[boot] AregVoiceMvp starting\n", OSError("device vanished")])
        second = FakeSerial([b"[boot] AregVoiceMvp starting\n", (QA + "\n").encode()])
        attempts = iter([OSError("not back yet"), second])
        sleeps = []

        def opener():
            nxt = next(attempts)
            if isinstance(nxt, BaseException):
                raise nxt
            return nxt

        class Sink:
            def __init__(self): self.data = []
            def write(self, s): self.data.append(s)
            def flush(self): pass

        sink = Sink()
        with contextlib.redirect_stdout(io.StringIO()) as out:
            bw.run_live(opener, first, w, sink, clock=lambda: 1.0, sleep=sleeps.append)
        self.assertEqual(w.boots, 2)
        self.assertEqual(w.chains, 1)
        self.assertEqual(w.port_losses, 1)
        self.assertEqual(sleeps, [0.5])
        self.assertTrue(first.closed and second.closed)
        self.assertEqual(len(sink.data), 3)
        self.assertIn("serial port is back", out.getvalue())

    def test_main_live_writes_raw_log_and_evidence_and_skips_on_request(self):
        script = [(ln + "\n").encode() for ln in boot_lines() + [sel("ulik"), QA]]
        with tempfile.TemporaryDirectory() as d:
            ev = Path(d) / "ev"
            raw = Path(d) / "raw.log"
            fixed = dt.datetime(2026, 10, 22, 19, 30)
            with contextlib.redirect_stdout(io.StringIO()) as out:
                rc = bw.main(["--port", "COM7", "--log", str(raw), "--evidence-dir", str(ev)],
                             serial_module=FakeSerialModule(script), now=lambda: fixed)
            self.assertEqual(rc, 0)
            self.assertEqual(FakeSerial.opened[0].at_open, (False, False))
            self.assertIn("[boot] AregVoiceMvp starting", raw.read_text(encoding="utf-8"))
            md = ev / "bench-20261022-1930.md"
            self.assertTrue(md.is_file())
            self.assertIn("live capture on COM7", md.read_text(encoding="utf-8"))
            self.assertIn("Evidence saved", out.getvalue())

            # a second run with the same --log must not clobber the first raw log
            first_raw = raw.read_bytes()
            with contextlib.redirect_stdout(io.StringIO()):
                bw.main(["--port", "COM7", "--log", str(raw), "--evidence-dir", str(ev)],
                        serial_module=FakeSerialModule(script), now=lambda: fixed)
            self.assertEqual(raw.read_bytes(), first_raw)
            self.assertTrue((Path(d) / "raw-2.log").is_file())
            self.assertTrue((ev / "bench-20261022-1930-2.md").is_file())

            ev2 = Path(d) / "ev2"
            with contextlib.redirect_stdout(io.StringIO()):
                bw.main(["--port", "COM7", "--log", str(Path(d) / "r3.log"), "--no-evidence",
                         "--evidence-dir", str(ev2)],
                        serial_module=FakeSerialModule(script), now=lambda: fixed)
            self.assertFalse(ev2.exists())

    def test_usage_errors(self):
        with contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(bw.main([]), 2)
            self.assertEqual(bw.main(["--replay", "/nonexistent/x.log"]), 2)


class ReplayTests(unittest.TestCase):
    """The existing logs in tools/quality-evidence keep replaying, and their
    real line shapes land where they should."""

    def replay(self, name: str) -> bw.Watcher:
        path = EVIDENCE / name
        self.assertTrue(path.is_file(), f"{name} is tracked evidence and must exist")
        w = bw.Watcher(echo=False)
        with contextlib.redirect_stdout(io.StringIO()):
            for line in path.read_text(errors="replace").splitlines():
                w.feed(line)
        return w

    def test_button_jumper3_streamed_answer_and_barge_in(self):
        w = self.replay("button-jumper3-20260819.log")
        self.assertEqual(w.latencies, [29518])
        self.assertEqual(w.streamed_latencies, 1)
        self.assertEqual(w.counts["barge_in"], 1)
        self.assertEqual(w.chain_kinds["qa"], 1)
        self.assertEqual(w.selections[0][0], "anban-huri")
        # the saved log shows the real shape: raw edge, cut, listen, answer
        self.assertEqual(w.a4_counts(), (1, 1, 1))
        self.assertEqual(w.check_a4().verdict, "NOT YET")

    def test_answer_reply_verify_reflection_chain_and_full_story(self):
        w = self.replay("answer-reply-verify-20260816.log")
        self.assertEqual(w.chain_kinds["reflection"], 1)
        self.assertEqual(w.a1_complete, 1)
        self.assertEqual(w.check_a1().verdict, "PASS")

    def test_menu_redesign_has_a_real_watchdog_and_the_benign_twdt_line(self):
        w = self.replay("menu-redesign-20260819.log")
        self.assertTrue(any("task watchdog" in t for t in w.troubles))
        self.assertTrue(any(b.num == 6 for b in w.boot_events))
        self.assertTrue(any(t.startswith("unexpected reset: reset_reason=6/TASK_WDT") for t in w.troubles))

    def test_no_stuck_state_false_positive_on_any_saved_log(self):
        for path in sorted(EVIDENCE.glob("*.log")):
            with self.subTest(log=path.name):
                w = bw.Watcher(echo=False)
                with contextlib.redirect_stdout(io.StringIO()):
                    for line in path.read_text(errors="replace").splitlines():
                        w.feed(line)
                self.assertEqual(w.stuck_events, 0)


if __name__ == "__main__":
    unittest.main()

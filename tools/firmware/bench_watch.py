#!/usr/bin/env python3
"""Watch the toy's serial log and score the bench tests automatically.

Written 2026-09-16, extended 2026-10-22 so ONE bench evening produces the
hardware evidence SHIP.md still lacks (A1, A4, A6, C1, C3).
Runs on the machine the toy is plugged into.

You press the buttons and talk to the toy. This reads the serial log, times
the things that matter, and tells you PASS or FAIL instead of making you
count seconds with a stopwatch and read scrolling text.

    python3 tools/firmware/bench_watch.py --port COM7

Stop it with Ctrl+C. It then prints a summary, writes the full raw log, and
writes tools/quality-evidence/bench-<YYYYMMDD-HHMM>.md - the evidence file a
fix session (or SHIP.md's owner) can read without having been there. Skip
the evidence file with --no-evidence.

Needs pyserial:   pip install pyserial
Can also replay a log you captured some other way, with no toy attached:
    python3 tools/firmware/bench_watch.py --replay some-session.log
(a replay writes no evidence file unless you pass --evidence; old logs carry
no timestamps, so A4 and the C3 outage length are "not measurable" there).

WHAT IT MEASURES

  Answer latency   the firmware's own `[latency] qa_release->play_begin_ms`
                   print (buffered path) or `qa_release->first_byte_ms`
                   (streaming path, AREG_QA_STREAM_PLAYBACK). This is the
                   real number, measured on the device.
                   NOTE: its anchor is taken AFTER the 600 ms earcon
                   (AregVoiceMvp.ino:1529), so the gap a child actually
                   perceives is roughly this plus 600 ms. The script says
                   both so nobody quietly compares the wrong pair.
                   Target with streaming on: about 2700 ms perceived.

  Button alive     every flow must end back at idle. A story that finishes
                   and a session that ends are both logged; if the toy
                   keeps printing `[alive]` while its last `[state]` is not
                   IDLE, that is the `s_state != ST_IDLE` bug, and it is
                   the most important thing you can find. The watcher says
                   so the moment it sees it.

  SHIP C1          counts completed voice chains (`[qa] answer played`,
                   `[post] reply N played`, `[chat] turn N ... played`) and
                   the longest run with no reset, panic, task-watchdog or
                   failed chain between them. Needs 10. A boot banner that
                   is not the first boot counts as a reset. The benign
                   `task_wdt: esp_task_wdt_init ... already initialized`
                   line every boot prints is NOT a watchdog trip.

  SHIP A6          the `[story] selected <id> (N eligible)` sequence,
                   adjacent repeats, and the reboots between selections
                   (a power-on / EXT / USB reset is a power-cycle; a crash
                   or software reset is not).

  SHIP A4          counts barge-ins (`[story] SD barge-in` on the SD path,
                   `[story] barge-in:` on the Wi-Fi stream path), whether
                   each was followed by `[audio] record_begin` and then
                   `[qa] answer played` - the "toy responds to what the
                   child said" half. The "stops within one second" half is
                   NOT in the log: `[button] pressed` prints only on the
                   idle path and the barge-in line is emitted at the same
                   instant as mp3.stop(), so no press->cut interval can be
                   read from it. That half is attested by ear, a phone video
                   or a stopwatch, and written down by the owner.

  SHIP C3          a Wi-Fi drop that started on a link which was up
                   (`[wifi] event=disconnected`), the next `event=got_ip`,
                   and a completed chain after it - with no reboot between.
                   `[prov] long Wi-Fi outage - opening BLE provisioning`
                   (after AREG_PROV_FALLBACK_AFTER_MS = 5 min offline) is a
                   WARN: the toy gave up and needs an adult, which is not a
                   recovery.

  SHIP D3          the denominator only: online turns completed and the
                   session wall-clock (first to last line, host clock), so
                   the provider bill can be read "per N turns in M minutes".
                   A replayed log has no clock: the length is unknown.

  Welcome / online / games markers
                   `[welcome] intent=<x>` (WARN on unknown: then Game,
                   Riddle, Curiosity and Calm are unreachable by voice),
                   `[button] hold`, `[chat] turn N played`, `[games]`,
                   `[browse]`, `[menu]` are recognised events, counted.

  Trouble          crashes, SD faults, sync failures, OTA refusals and the
                   canned failure clip are surfaced the moment they appear
                   instead of scrolling past.

SERIAL PORT NOTE   the port is opened with DTR and RTS held LOW. On the
                   S3's native USB-CDC those lines are the reset /
                   download-mode gesture (it cost an evening on 2026-08-17,
                   see watch-serial.ps1). If the port disappears - a
                   power-cycle re-enumerates USB - the watcher waits and
                   reconnects instead of dying, so A6's power-cycle does
                   not end the session.
"""
from __future__ import annotations

import argparse
import datetime as _dt
import hashlib
import re
import sys
import time
from collections import Counter
from dataclasses import dataclass, field
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
EVIDENCE_DIR = REPO_ROOT / "tools" / "quality-evidence"

# --- markers, all taken from the firmware source, not guessed ------------
RE_LATENCY_QA = re.compile(r"\[latency\]\s+qa_release->play_begin_ms=(\d+)")
# With AREG_QA_STREAM_PLAYBACK the SAME stopwatch prints under another key
# (AregVoiceMvp.ino:2175): the stream path stops at the first byte.
RE_LATENCY_QA_STREAM = re.compile(r"\[latency\]\s+qa_release->first_byte_ms=(\d+)")
RE_LATENCY_ANY = re.compile(r"\[latency\]\s+(\S+?)=(\d+)")
# The version banner only prints on the FIRST OTA tick with the link up
# (ota_foundation.cpp:509) — so on a toy that never joins Wi-Fi it never
# appears at all. `[boot] AregVoiceMvp starting` (AregVoiceMvp.ino setup())
# is the line that always prints, so treat that as the boot marker.
RE_BOOT_START = re.compile(r"\[boot\]\s+AregVoiceMvp starting")
RE_BOOT_VER = re.compile(r"\[ota\]\s+boot poll \(fw=(\S+)\s+build=(\S+)")
RE_OTA_RUNNING = re.compile(r"\[ota\]\s+running fw=(\S+)")
RE_HEARTBEAT = re.compile(r"\[heartbeat\]\s+status=(\d+)\s+\(fw=(\S+)")
RE_RESET = re.compile(r"\[boot\]\s+reset_reason=(\d+)/(\S+)")
RE_WIFI = re.compile(r"\[(wifi|net)\].*(connected|ip=|got_ip|failed|disconnect)", re.I)
RE_WIFI_EVENT = re.compile(r"\[wifi\]\s+event=(connected|got_ip|disconnected)")
RE_STORY_SEL = re.compile(r"\[story\]\s+selected\s+(\S+)")
# A real selection prints "(N eligible)"; the failure shape prints
# "... but it did not resolve" and is NOT a selection (A6 must not count it).
RE_STORY_SEL_OK = re.compile(r"\[story\]\s+selected\s+(\S+)\s+\((\d+) eligible\)")
RE_STORY_UNRESOLVED = re.compile(r"\[story\]\s+selected\s+\S+\s+but it did not resolve")
RE_STORY_END = re.compile(r"\[story\]\s+finished")
RE_STORY_RESUME = re.compile(r"\[story\]\s+resuming\s+(\S+)")

# Button / barge-in (AregVoiceMvp.ino:207, 3059, 3068, 3113; audio_io.cpp:434, 644)
RE_BTN_RAW = re.compile(r"\[button\]\s+raw=(DOWN|UP)")
RE_BTN_PRESSED = re.compile(r"\[button\]\s+pressed\b")
RE_BTN_HOLD = re.compile(r"\[button\]\s+hold\b")
RE_BARGE = re.compile(r"\[story\]\s+(?:SD\s+)?barge-in(.*)")
RE_RECORD_BEGIN = re.compile(r"\[audio\]\s+record_begin")
RE_PROV_OUTAGE = re.compile(r"\[prov\]\s+long Wi-Fi outage")
RE_BROWSE = re.compile(r"\[browse\]\s+(\S+)\s+->\s+(\S+)")
RE_CHOSEN = re.compile(r"\[welcome\]\s+playing chosen story\s+(\S+)")

# Welcome / online chat / games (AregVoiceMvp.ino:936-1590, offline_games.cpp)
RE_INTENT = re.compile(r"\[welcome\]\s+intent=(\S*)")
RE_WELCOME = re.compile(r"\[welcome\]\s+(.*)")
RE_MENU = re.compile(r"\[menu\]\s+(.*)")
RE_CHAT_TURN = re.compile(r"\[chat\]\s+turn\s+(\d+)\s+\((\S*)\)\s+played")
RE_CHAT = re.compile(r"\[chat\]\s+(.*)")
RE_GAMES = re.compile(r"\[games\]\s+(.*)")
RE_GAME_REPORT = re.compile(r"\[game-report\]\s+played\s+(\S+)")

# Completed voice chains (the C1 count)
RE_QA_PLAYED = re.compile(r"\[qa\]\s+answer played(?:\s+\((.*?)\))?")
RE_POST_REPLY = re.compile(r"\[post\]\s+reply\s+(\d+)\s+played")
RE_CHAIN_FAIL = re.compile(
    r"\[qa\]\s+(?:upload failed|streamed answer failed)"
    r"|\[post\]\s+reflection upload failed"
    r"|\[chat\]\s+(?:upload failed|decoder error)")

# Faults that end a run of chains. NOT bare "task_wdt": every normal boot
# prints `E (225) task_wdt: esp_task_wdt_init(517): TWDT already initialized`.
RE_FAULT = re.compile(
    r"Guru Meditation|abort\(\) was called|panic'ed|Task watchdog got triggered"
    r"|Interrupt wdt timeout|Brownout detector was triggered"
    r"|StoreProhibited|LoadProhibited")

RE_STATE = re.compile(r"\[state\]\s+(\d+)\s*->\s*(\d+)")
RE_ALIVE = re.compile(r"\[alive\]")
RE_VOLUME = re.compile(r"\[volume\]\s+gain\s+([\d.]+)")
RE_SYNC_MANIFEST = re.compile(r"\[content-sync\]\s+manifest status=(\d+)")
RE_SYNC_INDEX = re.compile(r"\[content-sync\]\s+index written")
RE_SYNC_RESTORED = re.compile(r"\[content-sync\]\s+primary index missing.*restored from backup")

# Anything here is worth stopping for.
TROUBLE = [
    (re.compile(r"\[story\]\s+SD (not mounted|open failed|file is not MP3)"), "SD fault"),
    (re.compile(r"\[story\]\s+mp3\.begin .* failed"), "decoder failed to start"),
    (re.compile(r"\[voice\].*(fail|timeout|unexpected body)", re.I), "voice turn failed"),
    (re.compile(r"\[ota\]\s+REFUSED"), "OTA refused"),
    (re.compile(r"\[ota\]\s+ROLLBACK detected"), "OTA ROLLBACK — the new image did not survive"),
    (re.compile(r"OTA_SIG_CHECK_DISABLED"), "signature check disabled in this image"),
    (re.compile(r"(Guru Meditation|abort\(\)|panic'ed|StoreProhibited|LoadProhibited)"), "CRASH"),
    (re.compile(r"Task watchdog got triggered"), "task watchdog fired — the loop stopped feeding it"),
    (re.compile(r"rst:0x[0-9a-f]+ \(.*(PANIC|WDT).*\)", re.I), "watchdog or panic reset"),
    (re.compile(r"index parse (?:failed|FAILED)"), "SD story index failed to parse (R9)"),
    # The firmware's real failures print in capitals ("FAIL", "FAILED",
    # "UNREADABLE", "TRUNCATED"); lower-case `failed=0` in a summary line is
    # a healthy counter and must not trip this.
    (re.compile(r"\[content-sync\].*(?:\bFAIL(?:ED)?\b|UNREADABLE|TRUNCATED|\bfailed=[1-9])"),
     "content sync problem"),
    (re.compile(r"\[sync\].*(fail|error)", re.I), "content sync problem"),
    (re.compile(r"\[qa\]\s+(?:upload failed|streamed answer failed)"), "in-story question failed"),
    (re.compile(r"\[post\]\s+reflection upload failed"), "after-story answer failed"),
    (re.compile(r"\[chat\]\s+(?:upload failed|decoder error|payload alloc failed)"),
     "online chat turn failed"),
    (re.compile(r"\[games\]\s+armed"),
     "AREG_OFFLINE_GAMES_BENCH is compiled into this build (bench auto-start) — remove it from config.h"),
    (re.compile(r"\[button\]\s+ignored"), "button press ignored: the toy is PAUSED in the dashboard"),
    (re.compile(r"\[welcome\].*staying silent"),
     "welcome flow stayed silent on purpose (toy paused or inside its bedtime window)"),
]

EARCON_MS = 600  # AREG_EARCON_DURATION_MS, config.h
TARGET_PERCEIVED_MS = 2700  # docs/latency-plan.md, streaming on
BASELINE_PERCEIVED_MS = 5700  # same source, streaming off

C1_NEEDS = 10          # SHIP C1: ten in a row
A6_NEEDS_SELECTIONS = 3  # SHIP A6: at least three stories
A4_NEEDS_PRESSES = 5   # the sheet asks for five mid-story presses
C3_MIN_OUTAGE_S = 20.0  # the sheet asks for 30 s; allow slack for the join
STUCK_ALIVE_LINES = 3   # [alive] prints every 5 s; 3 in a row = >10 s stuck

# esp_reset_reason_t numbers: 1 POWERON, 2 EXT, 3 SW, 11 USB (the firmware's
# name table has no entry for 11, so it prints "11/UNKNOWN" on every cable
# reset / port open - benign, and the only reason a USB-bench power-cycle
# can report).
POWER_CYCLE_NUMS = {1, 2, 11}
BENIGN_RESET_NUMS = {1, 2, 3, 11}
BENIGN_RESET_NAMES = {"POWERON", "POWERON_RESET", "SW", "SW_RESET", "USB", "USB_UART_CHIP_PU"}

C_OK, C_BAD, C_WARN, C_DIM, C_OFF = "\033[32m", "\033[31m", "\033[33m", "\033[2m", "\033[0m"
VERDICT_COLOUR = {"PASS": C_OK, "FAIL": C_BAD, "NOT YET": C_WARN}


@dataclass
class Check:
    """One SHIP.md item as this log can speak to it."""
    item: str
    verdict: str            # PASS | FAIL | NOT YET
    headline: str
    notes: list = field(default_factory=list)
    basis: str = ""         # one line for the evidence table


@dataclass
class Boot:
    num: int | None = None   # esp_reset_reason_t number, None until the line arrives
    name: str | None = None

    def kind(self) -> str:
        if self.num is None:
            return "reason not logged"
        if self.num in POWER_CYCLE_NUMS:
            return "power-cycle"
        if self.num == 3:
            return "software reset"
        return "FAULT"

    def label(self) -> str:
        if self.num is None:
            return "reset_reason not logged"
        return f"reset_reason={self.num}/{self.name}"


@dataclass
class Outage:
    t_start: float | None
    t_end: float | None = None
    closed: bool = False      # got_ip seen
    broken: bool = False      # the toy rebooted before the recovery was proven
    chain_after: bool = False  # a chain completed after the reconnect
    prov: bool = False        # BLE provisioning opened: the toy needs an adult


class Watcher:
    def __init__(self, echo: bool = True) -> None:
        self.latencies: list[int] = []
        self.streamed_latencies = 0
        self.stories: list[str] = []
        self.finishes = 0
        self.troubles: list[str] = []
        self.fw = self.build = None
        self.lines = 0
        self.boots = 0
        self.echo = echo
        self.timed = False                  # any line carried a host timestamp
        self.counts: Counter = Counter()    # recognised-event counters

        # boots, in order; a boot is created by its banner (or, when the
        # watcher joined mid-boot, by its reset_reason line)
        self.boot_events: list[Boot] = []

        # SHIP C1
        self.chains = 0
        self.chains_failed = 0
        self.chain_kinds: Counter = Counter()
        self.run = 0                # since the last reset OR failed chain
        self.run_reset_only = 0     # since the last reset only
        self.longest_run = 0
        self.longest_reset_only = 0
        self.run_breaks: list[str] = []

        # SHIP A6 : (id, boot count at the time, log line number)
        self.selections: list[tuple[str, int, int]] = []

        # SHIP A4: {line, browse, recorded, answered} per barge-in
        self.barge_ins: list[dict] = []
        self._pending_barge: dict | None = None

        # SHIP D3 denominator: host clock of the first and last line
        self.t_first: float | None = None
        self.t_last: float | None = None

        # SHIP C3
        self.outages: list[Outage] = []
        self._link_up = False

        # SHIP A1
        self._story_open_epoch: int | None = None
        self.a1_complete = 0

        # button alive
        self.state: int | None = None
        self._alive_nonidle = 0
        self._stuck_flagged = False
        self.stuck_events = 0

        self.port_losses = 0
        self.volume_gains: set = set()
        self.selections_unresolved = 0

    # ------------------------------------------------------------------ feed
    def feed(self, line: str, t: float | None = None) -> None:
        """Score one serial line. `t` is the host's time.monotonic() when the
        line ARRIVED; None for a replayed log (then nothing is timed)."""
        self.lines += 1
        line = line.rstrip("\r\n")
        if not line.strip():
            return
        if t is not None:
            self.timed = True
            if self.t_first is None:
                self.t_first = t
            self.t_last = t

        self._observe(line, t)

        if RE_BOOT_START.search(line):
            self.boots += 1
            self._new_boot(Boot(), f"boot #{self.boots}")
            print(f"\n{C_OK}BOOT{C_OFF}  the toy started (boot #{self.boots})")
            return

        if RE_WIFI.search(line):
            bad = re.search(r"fail|disconnect", line, re.I)
            print(f"{C_WARN if bad else C_OK}WIFI{C_OFF}  {line.strip()}")
            return

        m = RE_BOOT_VER.search(line)
        if m:
            self.fw, self.build = m.group(1), m.group(2)
            print(f"{C_OK}BOOT{C_OFF}  firmware {self.fw}   build {self.build}")
            print(f"{C_DIM}      (check this build string before blaming hardware){C_OFF}")
            return

        m = RE_RESET.search(line)
        if m:
            num, reason = int(m.group(1)), m.group(2)
            self._note_reset_reason(num, reason)
            bad = (reason.upper() not in BENIGN_RESET_NAMES) and (num not in BENIGN_RESET_NUMS)
            colour = C_WARN if bad else C_DIM
            print(f"{colour}RESET{C_OFF} {reason}")
            if bad:
                self.troubles.append(f"unexpected reset: reset_reason={num}/{reason}")
            return

        m = RE_LATENCY_QA.search(line)
        if m:
            self._latency(int(m.group(1)))
            return

        m = RE_LATENCY_QA_STREAM.search(line)
        if m:
            self._latency(int(m.group(1)), streamed=True)
            return

        m = RE_LATENCY_ANY.search(line)
        if m and "play_begin_ms" not in m.group(1):
            print(f"{C_DIM}time  {m.group(1)} = {m.group(2)} ms{C_OFF}")
            return

        m = RE_STORY_SEL.search(line)
        if m:
            self.stories.append(m.group(1))
            print(f"{C_DIM}story {m.group(1)}{C_OFF}")
            self._pending_barge = None
            if RE_STORY_UNRESOLVED.search(line):
                self.selections_unresolved += 1
                self.troubles.append(
                    f"story selected but did not resolve (nothing played): {line.strip()}")
            else:
                ok = RE_STORY_SEL_OK.search(line)
                if ok:
                    self.selections.append((ok.group(1), len(self.boot_events), self.lines))
                    self._story_open_epoch = len(self.boot_events)
            return

        if RE_STORY_END.search(line):
            self.finishes += 1
            if self._story_open_epoch is not None and self._story_open_epoch == len(self.boot_events):
                self.a1_complete += 1
            self._story_open_epoch = None
            self._pending_barge = None
            print(f"{C_OK}IDLE{C_OFF}  story finished — the button should work now")
            return

        for pattern, label in TROUBLE:
            if pattern.search(line):
                self.troubles.append(f"{label}: {line.strip()}")
                print(f"{C_BAD}TROUBLE{C_OFF} {label}")
                print(f"        {line.strip()}")
                return

        if self._recognise(line, t):
            return

        # Everything else is echoed dimly. An earlier version swallowed
        # unrecognised lines, so a toy that was talking perfectly well looked
        # like a dead screen for 214 lines. Silence must never be the default.
        if self.echo:
            print(f"{C_DIM}      {line.strip()}{C_OFF}")

    # ------------------------------------------------- silent side-trackers
    def _observe(self, line: str, t: float | None) -> None:
        """State that must be tracked whatever else the line is. Never prints
        and never consumes the line."""
        m = RE_BTN_RAW.search(line)
        if m:
            if m.group(1) == "DOWN":
                self.counts["raw_down"] += 1
            return

        if RE_RECORD_BEGIN.search(line):
            # The recorder opened right after a barge-in = the toy is listening
            # to the child's question (the first half of "responds to what the
            # child said"). Attaches only while a cut is still pending.
            if self._pending_barge is not None:
                self._pending_barge["recorded"] = True
                self._pending_barge = None
            return

        m = RE_STATE.search(line)
        if m:
            self.state = int(m.group(2))
            self._alive_nonidle = 0
            self._stuck_flagged = False
            return

        if RE_ALIVE.search(line):
            # `[alive]` prints from the idle section of loop(). If the last
            # state transition did not end in IDLE and loop() is running
            # anyway, a flow returned without transition_to(ST_IDLE): the
            # button is dead until a power cycle (the standing "never" rule).
            if self.state not in (None, 0):
                self._alive_nonidle += 1
                if self._alive_nonidle >= STUCK_ALIVE_LINES and not self._stuck_flagged:
                    self._stuck_flagged = True
                    self.stuck_events += 1
                    msg = (f"toy is idling but its last [state] ended in {self.state}, "
                           f"not IDLE (0) - the s_state != ST_IDLE bug: the button is "
                           f"dead until a power cycle")
                    self.troubles.append(msg)
                    print(f"{C_BAD}TROUBLE{C_OFF} {msg}")
            return

        if RE_FAULT.search(line):
            self._break_run("fault: " + line.strip()[:70])
            return

        m = RE_WIFI_EVENT.search(line)
        if m:
            self._wifi_event(m.group(1), t)
            return

        m = RE_OTA_RUNNING.search(line)
        if m and self.fw is None:
            self.fw = m.group(1)
            return

        m = RE_HEARTBEAT.search(line)
        if m:
            self.counts["heartbeat_ok" if m.group(1) == "200" else "heartbeat_bad"] += 1
            if self.fw is None:
                self.fw = m.group(2)
            return

        m = RE_VOLUME.search(line)
        if m:
            self.volume_gains.add(m.group(1))
            return

        m = RE_SYNC_MANIFEST.search(line)
        if m:
            self.counts["sync_manifest_ok" if m.group(1) == "200" else "sync_manifest_bad"] += 1
            return
        if RE_SYNC_INDEX.search(line):
            self.counts["sync_index_written"] += 1
            return
        if RE_SYNC_RESTORED.search(line):
            self.counts["sync_restored_from_prev"] += 1
            return

        if RE_BTN_PRESSED.search(line):
            self.counts["press_idle"] += 1
            return

        if RE_STORY_RESUME.search(line):
            self.counts["story_resume"] += 1
            return

        if RE_CHAIN_FAIL.search(line):
            self.chains_failed += 1
            self.run = 0
            self._break_note("failed chain: " + line.strip()[:70], only_strict=True)

    # ---------------------------------------------------- recognised events
    def _event(self, tag: str, text: str, colour: str = C_OK) -> None:
        print(f"{colour}{tag}{C_OFF}  {text}")

    def _recognise(self, line: str, t: float | None) -> bool:
        """Welcome / online / games / voice-chain markers (item 4). True when
        the line was handled and printed as an event."""
        m = RE_BARGE.search(line)
        if m:
            self._barge_in(m.group(1).strip(), t)
            return True

        if RE_BTN_HOLD.search(line):
            self.counts["hold"] += 1
            self._event("HOLD", "button held 2 s — the welcome menu is opening")
            return True

        m = RE_QA_PLAYED.search(line)
        if m:
            how = (m.group(1) or "").strip() or "no detail"
            for rec in reversed(self.barge_ins):
                if rec["recorded"] and not rec["answered"] and not rec["browse"]:
                    rec["answered"] = True
                break
            self._chain_done("qa", f"in-story answer played ({how})")
            return True

        m = RE_POST_REPLY.search(line)
        if m:
            self._chain_done("reflection", f"after-story reply {m.group(1)} played")
            return True

        m = RE_CHAT_TURN.search(line)
        if m:
            self.counts["chat_turn"] += 1
            self._chain_done("chat", f"online turn {m.group(1)} ({m.group(2)}) played")
            return True

        m = RE_INTENT.search(line)
        if m:
            intent = m.group(1) or "(empty)"
            self.counts["intent:" + intent] += 1
            if intent == "unknown":
                self._event("INTENT", "unknown — the voice answer did not resolve. "
                            "Game, Riddle, Curiosity and Calm are voice-only doors; "
                            "GREEN/RED only reach story/suggest/yes/no.", C_WARN)
            else:
                self._event("INTENT", intent)
            return True

        m = RE_BROWSE.search(line)
        if m:
            self.counts["browse"] += 1
            if self._pending_barge is not None:
                self._pending_barge["browse"] = True   # GREEN/RED cut, not a question
                self._pending_barge = None
            self._story_open_epoch = len(self.boot_events)
            self._event("BROWSE", line.strip().split("] ", 1)[-1], C_DIM)
            return True

        if RE_PROV_OUTAGE.search(line):
            self.counts["prov_outage"] += 1
            for o in reversed(self.outages):
                if not o.closed and not o.broken:
                    o.prov = True
                break
            self._event("PROV", "the toy has been offline long enough that it opened BLE "
                        "provisioning - it gave up on the saved Wi-Fi and needs an adult. "
                        "That is not a recovery (SHIP C3).", C_WARN)
            return True

        m = RE_CHOSEN.search(line)
        if m:
            self.counts["chosen_story"] += 1
            self._story_open_epoch = len(self.boot_events)
            self._event("MENU", f"story chosen from the menu: {m.group(1)}")
            return True

        m = RE_WELCOME.search(line)
        if m:
            self.counts["welcome_lines"] += 1
            text = m.group(1).strip()
            warn = re.search(r"not on card|no ask clip|no cached stories|answer too short|"
                             r"no answer|did not resolve|every mode disabled", text)
            self._event("MENU", text, C_WARN if warn else C_DIM)
            return True

        m = RE_MENU.search(line)
        if m:
            self.counts["menu_lines"] += 1
            self._event("MENU", m.group(1).strip(), C_DIM)
            return True

        m = RE_CHAT.search(line)
        if m:
            text = m.group(1).strip()
            if re.search(r"two silent windows|backend closed the turn|answer too short|"
                         r"payload alloc failed", text):
                self.counts["chat_session_end"] += 1
            self._event("CHAT", text, C_DIM)
            return True

        m = RE_GAME_REPORT.search(line)
        if m:
            self.counts["game_report"] += 1
            self._event("GAME", f"play reported for the dashboard: {m.group(1)}")
            return True

        m = RE_GAMES.search(line)
        if m:
            text = m.group(1).strip()
            game = re.match(r"([a-z][a-z-]*):", text)
            self.counts["games:" + (game.group(1) if game else "other")] += 1
            self._event("GAME", text, C_DIM)
            return True

        m = RE_SYNC_MANIFEST.search(line)
        if m:
            self._event("SYNC", line.strip().split("] ", 1)[-1],
                        C_OK if m.group(1) == "200" else C_WARN)
            return True
        if RE_SYNC_INDEX.search(line) or RE_SYNC_RESTORED.search(line):
            self._event("SYNC", line.strip().split("] ", 1)[-1])
            return True
        return False

    # --------------------------------------------------------- C1 plumbing
    def _chain_done(self, kind: str, text: str) -> None:
        self.chains += 1
        self.chain_kinds[kind] += 1
        self.run += 1
        self.run_reset_only += 1
        self.longest_run = max(self.longest_run, self.run)
        self.longest_reset_only = max(self.longest_reset_only, self.run_reset_only)
        for o in reversed(self.outages):     # C3: first chain after the reconnect
            if o.closed and not o.broken and not o.chain_after:
                o.chain_after = True
            break
        self._event("CHAIN", f"#{self.chains} {text}  {C_DIM}(run {self.run}/{C1_NEEDS}){C_OFF}")

    def _break_note(self, why: str, only_strict: bool = False) -> None:
        if self.run > 0 or self.run_reset_only > 0:
            done = self.run if only_strict else self.run_reset_only
            self.run_breaks.append(f"after {done} chain(s) in the run: {why}")

    def _break_run(self, why: str) -> None:
        """A reset / panic / watchdog: ends the run under both definitions."""
        self._break_note(why)
        self.run = 0
        self.run_reset_only = 0

    def _new_boot(self, boot: Boot, why: str) -> None:
        self.boot_events.append(boot)
        self._break_run(why)
        self._link_up = False
        self.state = None
        self._alive_nonidle = 0
        self._stuck_flagged = False
        self._story_open_epoch = None
        self._pending_barge = None
        for o in self.outages:               # a reboot proves nothing about recovery
            if not o.chain_after:
                o.broken = True

    def _note_reset_reason(self, num: int, name: str) -> None:
        if self.boot_events and self.boot_events[-1].num is None:
            self.boot_events[-1].num, self.boot_events[-1].name = num, name
        else:
            # The watcher joined after the banner; the reason line IS the boot.
            self._new_boot(Boot(num, name), f"boot (reset_reason={num}/{name})")

    # --------------------------------------------------------- C3 plumbing
    def _wifi_event(self, kind: str, t: float | None) -> None:
        if kind == "got_ip":
            self._link_up = True
            for o in reversed(self.outages):
                if not o.closed and not o.broken:
                    o.closed, o.t_end = True, t
                break
        elif kind == "disconnected":
            # Only a drop of a link that WAS up is a mid-session drop; the
            # first connect attempt after every boot also logs disconnects.
            if self._link_up:
                self._link_up = False
                self.outages.append(Outage(t_start=t))

    # --------------------------------------------------------- A4 plumbing
    def _barge_in(self, tail: str, t: float | None) -> None:
        self.counts["barge_in"] += 1
        rec = {"line": self.lines, "browse": False, "recorded": False, "answered": False}
        self.barge_ins.append(rec)
        self._pending_barge = rec
        detail = f"  {C_DIM}{tail.lstrip(': ')}{C_OFF}" if tail else ""
        self._event("CUT", f"story cut (barge-in){detail}", C_DIM)

    def a4_counts(self) -> tuple[int, int, int]:
        """(barge-ins that were a press, of those followed by record_begin,
        of those answered). GREEN/RED browse cuts are not questions."""
        real = [r for r in self.barge_ins if not r["browse"]]
        return (len(real), sum(1 for r in real if r["recorded"]),
                sum(1 for r in real if r["answered"]))

    # --------------------------------------------------------- port events
    def note_port_lost(self) -> None:
        self.port_losses += 1

    # ------------------------------------------------------------- latency
    def _latency(self, ms: int, streamed: bool = False) -> None:
        self.latencies.append(ms)
        if streamed:
            self.streamed_latencies += 1
        perceived = ms + EARCON_MS
        if perceived <= TARGET_PERCEIVED_MS * 1.15:
            tag, colour = "FAST", C_OK
        elif perceived >= BASELINE_PERCEIVED_MS * 0.85:
            tag, colour = "SLOW", C_BAD
        else:
            tag, colour = "MID ", C_WARN
        n = len(self.latencies)
        kind = f" {C_DIM}[streamed: first byte]{C_OFF}" if streamed else ""
        print(f"{colour}{tag}{C_OFF}  answer #{n}: {ms} ms measured, "
              f"~{perceived} ms as the child hears it "
              f"{C_DIM}(target ~{TARGET_PERCEIVED_MS}){C_OFF}{kind}")

    # ============================================================== checks
    def check_c1(self) -> Check:
        n = self.longest_run
        attempted = self.chains + self.chains_failed
        if n >= C1_NEEDS:
            verdict = "PASS"
        elif attempted >= C1_NEEDS:
            verdict = "FAIL"
        else:
            verdict = "NOT YET"
        notes = [f"{self.chains} completed chain(s), {self.chains_failed} failed chain(s), "
                 f"{self.boots} boot banner(s) seen"]
        if self.chain_kinds:
            kinds = ", ".join(f"{k} {v}" for k, v in sorted(self.chain_kinds.items()))
            notes.append(f"completed by kind: {kinds}")
        if self.longest_reset_only > n:
            notes.append(f"a failed chain also ends a run (SHIP C1 says ten IN A ROW): "
                         f"counting resets only, the longest run would be {self.longest_reset_only}")
        for b in self.run_breaks[:8]:
            notes.append(f"run ended {b}")
        if len(self.run_breaks) > 8:
            notes.append(f"... and {len(self.run_breaks) - 8} more")
        if not self.chains:
            notes.append("no completed voice chain seen: press during a story and ask a question, "
                         "or answer the after-story question")
        return Check("C1", verdict,
                     f"SHIP C1  longest run of voice chains without a reset: {n} (needs {C1_NEEDS})",
                     notes, f"longest run {n} of {C1_NEEDS}; {self.chains} chain(s) completed")

    def _boots_between(self, a_epoch: int, b_epoch: int) -> list[Boot]:
        return self.boot_events[a_epoch:b_epoch]

    def check_a6(self) -> Check:
        sel = self.selections
        repeats = [(i, sel[i]) for i in range(1, len(sel)) if sel[i][0] == sel[i - 1][0]]
        cycles = other = 0
        parts: list[str] = []
        for i, (sid, epoch, _line) in enumerate(sel):
            if i:
                for b in self._boots_between(sel[i - 1][1], epoch):
                    if b.kind() == "power-cycle":
                        cycles += 1
                        parts.append("[power-cycle]")
                    else:
                        other += 1
                        parts.append(f"[{b.kind()}]")
            parts.append(sid)
        if repeats:
            verdict = "FAIL"
        elif len(sel) >= A6_NEEDS_SELECTIONS and cycles >= 1:
            verdict = "PASS"
        else:
            verdict = "NOT YET"
        head = (f"SHIP A6  {len(sel)} selections, adjacent repeats: {len(repeats)}, "
                f"power-cycles between selections: {cycles} "
                f"(needs ≥{A6_NEEDS_SELECTIONS} selections + ≥1 power-cycle, 0 repeats)")
        notes = []
        if sel:
            seq = " -> ".join(parts[:60]) + (" -> ..." if len(parts) > 60 else "")
            notes.append(f"sequence: {seq}")
        for i, (sid, _e, line_no) in repeats:
            notes.append(f"REPEAT: {sid} selected twice in a row (log line {line_no}). "
                         "If nothing was heard between the two (a failed start does not advance "
                         "the cursor), read the lines around it before calling it a repeat.")
        if other:
            notes.append(f"{other} other reboot(s) between selections (crash / software reset) "
                         "- NVS persistence is still exercised, but they are not power-cycles")
        if self.selections_unresolved:
            notes.append(f"{self.selections_unresolved} selection(s) did not resolve "
                         "(`... but it did not resolve - using fallback chain`): counted as TROUBLE, "
                         "not as selections")
        side = self.counts["browse"] + self.counts["chosen_story"]
        if side:
            notes.append(f"{side} browse / menu-chosen start(s) are not rotation selections "
                         "and are not scored - A6 is five quick presses")
        if sel and len(sel) < A6_NEEDS_SELECTIONS:
            notes.append(f"only {len(sel)} selection(s): press five times")
        if len(sel) >= A6_NEEDS_SELECTIONS and not cycles and not repeats:
            notes.append("no power-cycle between selections: unplug the toy, plug it in, "
                         "wait for the boot, then press again")
        return Check("A6", verdict, head, notes,
                     f"{len(sel)} selection(s), {len(repeats)} repeat(s), {cycles} power-cycle(s)")

    def check_a4(self) -> Check:
        n, recorded, answered = self.a4_counts()
        head = (f"SHIP A4  {n} barge-ins, {answered} answered; the <1 s stop is attested by "
                f"ear or phone video, not by this log")
        notes = []
        if n:
            notes.append(f"{recorded} of {n} cut(s) were followed by `[audio] record_begin` "
                         f"(the toy listened), {answered} by `[qa] answer played` "
                         "(it answered what the child said)")
        else:
            notes.append("no barge-in seen: press the main button while a story plays")
        if n and n < A4_NEEDS_PRESSES:
            notes.append(f"only {n} barge-in(s): the sheet asks for {A4_NEEDS_PRESSES} "
                         "presses mid-story")
        if self.counts["browse"]:
            notes.append(f"{self.counts['browse']} GREEN/RED browse cut(s) are not counted")
        notes.append("the <1 s half: take a phone video (or use a stopwatch) over "
                     f"{A4_NEEDS_PRESSES} mid-story presses and write the seconds in the "
                     "evidence note - `[button] pressed` prints only when idle and the "
                     "barge-in line is printed at the same instant as the decoder stop, so "
                     "the log holds no press-to-silence interval")
        # The log can only ever speak to the "responds" half, so never PASS.
        return Check("A4", "NOT YET", head, notes,
                     f"{n} barge-in(s), {answered} answered (log half); the <1 s stop needs "
                     "ear / phone video / stopwatch seconds")

    def check_c3(self) -> Check:
        outs = self.outages
        done = [o for o in outs if o.closed and not o.broken and o.chain_after and not o.prov]
        if done:
            o = done[0]
            dur = (o.t_end - o.t_start) if (o.t_start is not None and o.t_end is not None) else None
            if dur is not None and dur < C3_MIN_OUTAGE_S:
                verdict = "NOT YET"
                tail = f"the outage lasted only {dur:.0f} s (the sheet asks for 30 s)"
            else:
                verdict = "PASS"
                tail = (f"outage {dur:.0f} s, then a completed chain with no reboot" if dur is not None
                        else "outage length not measurable from a replayed log")
            head = f"SHIP C3  Wi-Fi drop recovered without a reboot: {len(done)} of {len(outs)} drop(s)"
            return Check("C3", verdict, head, [tail],
                         f"{len(done)} drop(s) recovered; {tail}")
        notes = []
        if not outs:
            notes.append("no mid-session Wi-Fi drop seen: switch the router off for 30 s, "
                         "then on, then press the button")
        else:
            for o in outs:
                if o.prov:
                    notes.append("WARN: the toy opened BLE provisioning during a drop "
                                 "(offline > AREG_PROV_FALLBACK_AFTER_MS) - it needed an adult, "
                                 "which is not a recovery")
                elif o.broken:
                    notes.append("a drop ended in a reboot - that is not a recovery")
                elif not o.closed:
                    notes.append("a drop is still open: no `event=got_ip` after it")
                elif not o.chain_after:
                    notes.append("the link came back but no voice chain completed after it: "
                                 "press the button and ask something")
        return Check("C3", "NOT YET",
                     f"SHIP C3  Wi-Fi drop recovered without a reboot: 0 of {len(outs)} drop(s)",
                     notes, "no recovered mid-session drop")

    def check_a1(self) -> Check:
        replies = self.chain_kinds["reflection"]
        ok = self.a1_complete >= 1
        head = (f"SHIP A1  stories played start to finish with no reset: {self.a1_complete} "
                f"(after-story replies played: {replies})")
        notes = ["the ear half is yours: Armenian only, no English leaking - write the 3-line note"]
        return Check("A1", "PASS" if ok else "NOT YET", head, notes,
                     f"{self.a1_complete} full story(ies), {replies} after-story reply(ies); "
                     "the ear half needs a human note")

    def check_c4(self) -> Check:
        return Check("C4", "NOT YET",
                     "SHIP C4  loudness is not measurable from a serial log",
                     ["phone SPL app at 1 m, two volume-knob settings; write app / distance / "
                      "room / dB(A) in the evidence note"],
                     "needs a phone SPL reading")

    def session_minutes(self) -> float | None:
        if self.t_first is None or self.t_last is None:
            return None
        return (self.t_last - self.t_first) / 60.0

    def session_text(self) -> str:
        m = self.session_minutes()
        return ("unknown (a replayed log has no clock)" if m is None else f"{m:.1f} min")

    def check_d3(self) -> Check:
        n = self.chains
        m = self.session_minutes()
        when = "in an unknown number of minutes (replayed log, no clock)" if m is None \
            else f"in {m:.1f} min"
        head = f"SHIP D3  {n} online turns {when} (the denominator for the provider bill)"
        kinds = ", ".join(f"{k} {v}" for k, v in sorted(self.chain_kinds.items())) or "none"
        notes = [f"turns by kind: {kinds}; {self.chains_failed} failed turn(s) (those may still bill)",
                 "wall-clock runs from the first to the last line the watcher saw, host clock, "
                 "and includes pauses and any serial-port gap",
                 "a story played from the SD card is not an online turn",
                 "the cost itself is not in this log: read the OpenAI / ElevenLabs / Gemini bill "
                 "for the same window and divide"]
        return Check("D3", "NOT YET", head, notes,
                     f"{n} online turn(s) {when}; the bill is read from the providers")

    def checks(self) -> list[Check]:
        return [self.check_c1(), self.check_a6(), self.check_a4(),
                self.check_c3(), self.check_a1(), self.check_c4(), self.check_d3()]

    # ------------------------------------------------------------- summary
    def _latency_block(self) -> list[tuple[str, str]]:
        """(text, level) pairs; level in ok/bad/warn/plain. Shared by the
        console summary and the evidence file."""
        out: list[tuple[str, str]] = []
        if self.latencies:
            xs = sorted(self.latencies)
            mid = xs[len(xs) // 2]
            perceived = mid + EARCON_MS
            out.append((f"Answer latency, {len(xs)} sample(s)", "plain"))
            out.append((f"  fastest  {xs[0]} ms      slowest {xs[-1]} ms", "plain"))
            out.append((f"  median   {mid} ms measured, ~{perceived} ms perceived", "plain"))
            if self.streamed_latencies:
                out.append((f"  {self.streamed_latencies} of these are streamed first-byte readings "
                            f"(qa_release->first_byte_ms)", "plain"))
            if len(xs) < 5:
                out.append(("  fewer than 5 samples — one fast answer is luck", "warn"))
            if perceived <= TARGET_PERCEIVED_MS * 1.15:
                out.append(("  PASS — streaming is working", "ok"))
            elif perceived >= BASELINE_PERCEIVED_MS * 0.85:
                if self.streamed_latencies == len(self.latencies):
                    out.append(("  FAIL — too slow even though the answer was streamed.", "bad"))
                    out.append(("         The flag is in the build; look at the backend (STT, model,", "plain"))
                    out.append(("         TTS time) and the Wi-Fi link, not at config.h.", "plain"))
                else:
                    out.append(("  FAIL — this is the old slow path.", "bad"))
                    out.append(("         AREG_QA_STREAM_PLAYBACK may not be in the flashed build,", "plain"))
                    out.append(("         or StoryQa__StreamAnswerAudio is false on the server.", "plain"))
            else:
                out.append(("  between the two known numbers — worth a closer look", "warn"))
        return out

    def _marker_lines(self) -> list[str]:
        c = self.counts
        lines = []
        row = []
        if c["hold"]:
            row.append(f"hold-to-menu {c['hold']}")
        if c["press_idle"]:
            row.append(f"idle presses {c['press_idle']}")
        if c["browse"]:
            row.append(f"browse {c['browse']}")
        if c["chosen_story"]:
            row.append(f"menu-chosen story {c['chosen_story']}")
        if c["story_resume"]:
            row.append(f"story resumes {c['story_resume']}")
        if row:
            lines.append("   ".join(row))
        intents = sorted((k[len("intent:"):], v) for k, v in c.items() if k.startswith("intent:"))
        if intents:
            lines.append("welcome intents: " + ", ".join(f"{k} {v}" for k, v in intents))
            if c["intent:unknown"]:
                lines.append("  WARN: intent=unknown means the voice answer did not resolve; "
                             "Game/Riddle/Curiosity/Calm cannot be reached by voice")
        if c["chat_turn"] or c["chat_session_end"]:
            lines.append(f"online chat: {c['chat_turn']} turn(s) played, "
                         f"{c['chat_session_end']} session end(s) logged")
        games = sorted((k[len("games:"):], v) for k, v in c.items() if k.startswith("games:"))
        if games or c["game_report"]:
            lines.append("offline-game lines: "
                         + (", ".join(f"{k} {v}" for k, v in games) or "none")
                         + f"   game reports {c['game_report']}")
        if c["sync_manifest_ok"] or c["sync_index_written"] or c["sync_restored_from_prev"]:
            lines.append(f"content sync: manifest 200 x{c['sync_manifest_ok']}, "
                         f"index written x{c['sync_index_written']}, "
                         f"restored from .prev x{c['sync_restored_from_prev']}")
        if c["prov_outage"]:
            lines.append(f"WARN: BLE provisioning opened after a long Wi-Fi outage x{c['prov_outage']}")
        if self.stuck_events:
            lines.append(f"STUCK-STATE signature seen {self.stuck_events} time(s)")
        if self.port_losses:
            lines.append(f"serial port dropped {self.port_losses} time(s) "
                         "(expected on a power-cycle: USB re-enumerates)")
        return lines

    def summary(self) -> None:
        print("\n" + "=" * 60)
        print("  BENCH SUMMARY")
        print("=" * 60)
        print(f"firmware {self.fw or '?'}   build {self.build or '?'}   "
              f"{self.lines} log lines   {self.boots} boot(s)")
        if self.build is None and self.boots:
            print(f"  {C_DIM}no build string: it only prints on the first OTA tick"
                  f" once Wi-Fi is up{C_OFF}")

        block = self._latency_block()
        if block:
            print()
            colours = {"ok": C_OK, "bad": C_BAD, "warn": C_WARN, "plain": ""}
            for text, level in block:
                col = colours[level]
                print(f"{col}{text}{C_OFF if col else ''}")
        else:
            print(f"\n{C_WARN}No answer timings seen.{C_OFF} Ask the toy a question "
                  "during a story: press the button while a story plays.")

        print(f"\nStories played {len(self.stories)}   finished cleanly {self.finishes}")
        if self.stories and not self.finishes:
            print(f"  {C_WARN}a story started but none finished — did you stop early, "
                  f"or did it hang?{C_OFF}")

        print("\nSHIP evidence this log can speak to (SHIP.md itself is yours to tick)")
        for c in self.checks():
            col = VERDICT_COLOUR[c.verdict]
            print(f"{c.headline}   {col}{c.verdict}{C_OFF}")
            for n in c.notes:
                print(f"{C_DIM}         {n}{C_OFF}")

        markers = self._marker_lines()
        if markers:
            print("\nMarkers seen")
            for m in markers:
                print(f"  {m}")

        if self.troubles:
            print(f"\n{C_BAD}{len(self.troubles)} problem(s):{C_OFF}")
            for t in self.troubles[:20]:
                print(f"  - {t}")
            if len(self.troubles) > 20:
                print(f"  ... and {len(self.troubles) - 20} more, see the log file")
        else:
            print(f"\n{C_OK}No crashes, SD faults or failed turns seen.{C_OFF}")

        print("\nIf the button stopped responding after any flow, that is the")
        print("s_state != ST_IDLE bug. Write down exactly what you did first.")
        print("Record results in tools/quality-evidence/, dated.\n")

    # ------------------------------------------------------------ evidence
    def render_evidence(self, *, when: _dt.datetime, source: str, raw_log: str,
                        raw_sha256: str, port: str | None) -> str:
        """The evidence file. Deterministic: same input and same `when` give
        the same bytes (no clock reads, sorted counters)."""
        L: list[str] = []
        add = L.append
        add(f"# Bench evidence — {when.strftime('%Y-%m-%d %H:%M')}")
        add("")
        add("Written by `tools/firmware/bench_watch.py` from a toy's serial log. It is the "
            "script's reading of the log and says PASS only where the log itself proves the "
            "point. Nothing here was typed by hand, and SHIP.md is not edited by this file.")
        add("")
        add(f"- Source: {source}")
        add(f"- Raw log: `{raw_log}` ({self.lines} lines, sha256 {raw_sha256})")
        add(f"- Firmware: fw={self.fw or '?'} build={self.build or '?'}")
        if port:
            add(f"- Port: {port}")
        add("- Timing: " + ("host-side arrival stamps (time.monotonic(), USB CDC latency included)"
                            if self.timed else "none - a replayed log has no timestamps"))
        m = self.session_minutes()
        add("- Session: " + ("unknown (a replayed log has no clock)" if m is None
                             else f"{m:.1f} min, first line to last line on the host clock")
            + f"; {self.chains} online turn(s) completed")
        add(f"- Boots: {len(self.boot_events)} "
            f"({self.boots} banner(s))")
        for i, b in enumerate(self.boot_events, 1):
            add(f"  - boot {i}: {b.label()} — {b.kind()}")
        add("")

        checks = self.checks()
        add("## SHIP.md items this log supports")
        add("")
        add("| Item | Verdict | Basis |")
        add("|---|---|---|")
        for c in checks:
            add(f"| {c.item} | {c.verdict} | {c.basis} |")
        add("")
        add("PASS means the log proves the hardware half. A1's ear half (Armenian only, no "
            "English), A4's under-one-second stop, C4 (loudness) and D3's bill need a human "
            "note or a provider dashboard; FAIL means this log shows a miss.")
        add("")

        for c in checks:
            add(f"## {c.headline.split('  ', 1)[0]} — {c.verdict}")
            add("")
            add(c.headline)
            add("")
            for n in c.notes:
                add(f"- {n}")
            if c.notes:
                add("")

        add("## Answer latency")
        add("")
        block = self._latency_block()
        if block:
            add("```")
            for text, _ in block:
                add(text)
            add("```")
        else:
            add("No answer timings seen.")
        add("")

        add("## Regression R1–R10: what the log shows")
        add("")
        add("| # | Step | What the log shows |")
        add("|---|---|---|")
        c = self.counts
        add(f"| R1 | Power on | boots {len(self.boot_events)}; fw={self.fw or '?'}; "
            f"build={self.build or '?'}; heartbeat 200 x{c['heartbeat_ok']} |")
        add(f"| R2 | Quick press: a story plays to the end | {len(self.selections)} selection(s), "
            f"{self.a1_complete} finished with no reset |")
        n4, rec4, ans4 = self.a4_counts()
        add(f"| R3 | Press mid-story / press again | {n4} barge-in(s), {rec4} recorded, "
            f"{ans4} answered; {c['story_resume']} resume(s) |")
        add(f"| R4 | GREEN / RED during playback | {c['browse']} browse event(s) |")
        add(f"| R5 | After-story flow | {self.chain_kinds['reflection']} after-story reply(ies) played |")
        add(f"| R6 | Question during a story | {self.chain_kinds['qa']} answer(s) played |")
        add(f"| R7 | Hold 2 s, then GREEN | {c['hold']} hold(s); {c['chosen_story']} menu-chosen story(ies) |")
        add(f"| R8 | Knob mid-story | {len(self.volume_gains)} distinct `[volume] gain` value(s) "
            "(by ear: does the loudness follow?) |")
        add(f"| R9 | ~3 min after boot | manifest 200 x{c['sync_manifest_ok']}; "
            f"index written x{c['sync_index_written']} |")
        add(f"| R10 | Button alive after every flow | "
            + ("STUCK-STATE signature seen" if self.stuck_events
               else "no stuck-state signature (only your press proves it)") + " |")
        add("")

        markers = self._marker_lines()
        add("## Welcome / online / games markers")
        add("")
        if markers:
            for m in markers:
                add(f"- {m.strip()}")
        else:
            add("None seen.")
        add("")

        add("## Trouble list")
        add("")
        if self.troubles:
            for t in self.troubles:
                add(f"- {t}")
        else:
            add("No crashes, SD faults or failed turns seen.")
        add("")

        add("## Not provable from a serial log")
        add("")
        add("- Loudness (C4) - phone SPL app reading.")
        add("- The under-one-second stop (A4) - phone video or stopwatch seconds.")
        add("- Cost per hour of play (D3) - the provider bills; the denominator is above.")
        add("- Armenian-only audio and no English leaking (A1) - a human ear.")
        add("- Whether the toy sounds right, clicks, or drops a word - a human ear.")
        add("")
        return "\n".join(L)


# ------------------------------------------------------------------ files
def unique_path(path: Path) -> Path:
    """`path`, or `path` with -2, -3 ... before the suffix. Never an existing file."""
    if not path.exists():
        return path
    for n in range(2, 1000):
        cand = path.with_name(f"{path.stem}-{n}{path.suffix}")
        if not cand.exists():
            return cand
    raise FileExistsError(f"too many files named like {path}")


def open_new(path: Path, encoding: str = "utf-8"):
    """Create a file that does not exist yet; returns (handle, actual_path)."""
    p = unique_path(path)
    while True:
        try:
            return p.open("x", encoding=encoding), p
        except FileExistsError:   # lost a race between exists() and open
            p = unique_path(p)


def sha256_of(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 16), b""):
            h.update(chunk)
    return h.hexdigest()


def write_evidence(w: Watcher, *, evidence_dir: Path, when: _dt.datetime, source: str,
                   raw_log: Path, port: str | None = None) -> Path:
    evidence_dir.mkdir(parents=True, exist_ok=True)
    try:
        raw_name = str(raw_log.relative_to(REPO_ROOT))
    except ValueError:
        raw_name = str(raw_log)
    text = w.render_evidence(when=when, source=source, raw_log=raw_name,
                             raw_sha256=sha256_of(raw_log) if raw_log.is_file() else "unavailable",
                             port=port)
    fh, path = open_new(evidence_dir / f"bench-{when.strftime('%Y%m%d-%H%M')}.md")
    with fh:
        fh.write(text if text.endswith("\n") else text + "\n")
    return path


# ------------------------------------------------------------------ serial
def open_serial(serial_mod, port: str, baud: int):
    """Open with DTR and RTS held LOW. On the S3's native USB-CDC those two
    lines are the reset / download-mode gesture; pyserial's default (assert
    both on open) is what put the toy into download mode on 2026-08-17
    (see watch-serial.ps1). Setting them before open() applies them at open."""
    ser = serial_mod.Serial()
    ser.port = port
    ser.baudrate = baud
    ser.timeout = 1
    ser.dtr = False
    ser.rts = False
    ser.open()
    return ser


def run_live(open_port, first, w: Watcher, fh, *, clock=time.monotonic,
             sleep=time.sleep) -> None:
    """Read until Ctrl+C. A vanished port (power-cycle, USB re-enumeration) is
    waited out and reopened - it must not end the session."""
    ser = first
    announced = False
    try:
        while True:
            if ser is None:
                try:
                    ser = open_port()
                except KeyboardInterrupt:
                    raise
                except Exception:  # noqa: BLE001 - port not back yet
                    if not announced:
                        print(f"{C_WARN}PORT{C_OFF}  serial port is gone (a power-cycle "
                              f"re-enumerates USB) - waiting for it to come back...")
                        announced = True
                    sleep(0.5)
                    continue
                print(f"{C_OK}PORT{C_OFF}  serial port is back - still listening")
                announced = False
            try:
                raw = ser.readline()
            except KeyboardInterrupt:
                raise
            except Exception:  # noqa: BLE001 - SerialException / OSError on unplug
                try:
                    ser.close()
                except Exception:  # noqa: BLE001
                    pass
                ser = None
                w.note_port_lost()
                print(f"\n{C_WARN}PORT{C_OFF}  lost the serial port - waiting for it to come back...")
                announced = True
                continue
            if not raw:
                continue
            t = clock()          # stamp on arrival, before any parsing
            line = raw.decode("utf-8", errors="replace")
            fh.write(line)
            fh.flush()           # a crash must not take the evidence with it
            w.feed(line, t)
    except KeyboardInterrupt:
        pass
    finally:
        if ser is not None:
            try:
                ser.close()
            except Exception:  # noqa: BLE001
                pass


def _harden_stdout() -> None:
    """The log echo can carry Armenian text and the summary uses ≥ and —;
    a redirected Windows stdout (cp1252) must not die on them."""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(errors="replace")  # type: ignore[attr-defined]
        except Exception:  # noqa: BLE001
            pass


def main(argv: list[str] | None = None, *, serial_module=None, now=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--port", help="serial port, e.g. COM7 or /dev/ttyUSB0")
    ap.add_argument("--baud", type=int, default=115200)
    ap.add_argument("--replay", type=Path, help="score a saved log instead of a live toy")
    ap.add_argument("--log", type=Path, help="where to write the raw log "
                                             "(default: bench-<timestamp>.log; an existing "
                                             "file is never overwritten)")
    ap.add_argument("--quiet", action="store_true",
                    help="do not echo lines the scorer does not recognise")
    ap.add_argument("--no-evidence", action="store_true",
                    help="do not write tools/quality-evidence/bench-<date>.md")
    ap.add_argument("--evidence", action="store_true",
                    help="write the evidence file even for --replay (off by default there: "
                         "an old log would be dated today)")
    ap.add_argument("--evidence-dir", type=Path, default=EVIDENCE_DIR,
                    help="where the evidence file goes (default: tools/quality-evidence)")
    args = ap.parse_args(argv)

    _harden_stdout()
    stamp = now() if now else _dt.datetime.now()
    w = Watcher(echo=not args.quiet)

    if args.replay:
        if not args.replay.is_file():
            print(f"no such file: {args.replay}", file=sys.stderr)
            return 2
        for line in args.replay.read_text(errors="replace").splitlines():
            w.feed(line)
        w.summary()
        if args.evidence and not args.no_evidence:
            path = write_evidence(w, evidence_dir=args.evidence_dir, when=stamp,
                                  source=f"replay of `{args.replay.name}` (no timestamps)",
                                  raw_log=args.replay)
            print(f"Evidence saved: {path}")
        return 0

    if not args.port:
        print("give --port (or --replay). Find it with: arduino-cli board list",
              file=sys.stderr)
        return 2

    if serial_module is None:
        try:
            import serial as serial_module  # type: ignore
        except ImportError:
            print("pyserial is missing.  pip install pyserial", file=sys.stderr)
            return 2

    log_path = args.log or Path(f"bench-{stamp.strftime('%Y%m%d-%H%M%S')}.log")

    try:
        ser = open_serial(serial_module, args.port, args.baud)
    except Exception as exc:  # noqa: BLE001 - surface the real reason
        print(f"could not open {args.port}: {exc}", file=sys.stderr)
        print("Is the serial monitor open in another window? Close it and retry.",
              file=sys.stderr)
        return 2

    fh, log_path = open_new(log_path)
    print(f"Listening on {args.port} at {args.baud} (DTR/RTS held low). Raw log -> {log_path}")
    print("Play with the toy. Ctrl+C when you are done.\n")

    with fh:
        run_live(lambda: open_serial(serial_module, args.port, args.baud), ser, w, fh)

    w.summary()
    print(f"Raw log saved: {log_path}")
    if not args.no_evidence:
        path = write_evidence(w, evidence_dir=args.evidence_dir, when=stamp,
                              source=f"live capture on {args.port}, host-side timestamps",
                              raw_log=log_path, port=args.port)
        print(f"Evidence saved: {path}")
        if log_path.resolve().parent != args.evidence_dir.resolve():
            print(f"Copy the raw log next to it so they are committed together: {log_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

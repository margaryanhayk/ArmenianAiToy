#!/usr/bin/env python3
"""Watch the toy's serial log and score the bench tests automatically.

Written 2026-09-16. Runs on the machine the toy is plugged into.

You press the buttons and talk to the toy. This reads the serial log, times
the things that matter, and tells you PASS or FAIL instead of making you
count seconds with a stopwatch and read scrolling text.

    python3 tools/firmware/bench_watch.py --port COM7

Stop it with Ctrl+C. It then prints a summary and writes the full raw log to
a timestamped file, so a failed evening leaves evidence instead of a memory.

Needs pyserial:   pip install pyserial
Can also replay a log you captured some other way, with no toy attached:
    python3 tools/firmware/bench_watch.py --replay some-session.log

WHAT IT MEASURES

  Answer latency   the firmware's own `[latency] qa_release->play_begin_ms`
                   print. This is the real number, measured on the device.
                   NOTE: its anchor is taken AFTER the 600 ms earcon
                   (AregVoiceMvp.ino:1529), so the gap a child actually
                   perceives is roughly this plus 600 ms. The script says
                   both so nobody quietly compares the wrong pair.
                   Target with streaming on: about 2700 ms perceived.

  Button alive     every flow must end back at idle. A story that finishes
                   and a session that ends are both logged; if the toy stops
                   producing them after a session, that is the
                   `s_state != ST_IDLE` bug, and it is the most important
                   thing you can find.

  Trouble          crashes, SD faults, sync failures, OTA refusals and the
                   canned failure clip are surfaced the moment they appear
                   instead of scrolling past.
"""
from __future__ import annotations

import argparse
import datetime as _dt
import re
import sys
from pathlib import Path

# --- markers, all taken from the firmware source, not guessed ------------
RE_LATENCY_QA = re.compile(r"\[latency\]\s+qa_release->play_begin_ms=(\d+)")
RE_LATENCY_ANY = re.compile(r"\[latency\]\s+(\S+?)=(\d+)")
RE_BOOT_VER = re.compile(r"\[ota\]\s+boot poll \(fw=(\S+)\s+build=(\S+)")
RE_RESET = re.compile(r"\[boot\]\s+reset_reason=(\d+)/(\S+)")
RE_STORY_SEL = re.compile(r"\[story\]\s+selected\s+(\S+)")
RE_STORY_END = re.compile(r"\[story\]\s+finished")

# Anything here is worth stopping for.
TROUBLE = [
    (re.compile(r"\[story\]\s+SD (not mounted|open failed|file is not MP3)"), "SD fault"),
    (re.compile(r"\[story\]\s+mp3\.begin .* failed"), "decoder failed to start"),
    (re.compile(r"\[voice\].*(fail|timeout|unexpected body)", re.I), "voice turn failed"),
    (re.compile(r"\[ota\]\s+REFUSED"), "OTA refused"),
    (re.compile(r"\[ota\]\s+ROLLBACK detected"), "OTA ROLLBACK — the new image did not survive"),
    (re.compile(r"OTA_SIG_CHECK_DISABLED"), "signature check disabled in this image"),
    (re.compile(r"(Guru Meditation|abort\(\)|panic'ed|StoreProhibited|LoadProhibited)"), "CRASH"),
    (re.compile(r"rst:0x[0-9a-f]+ \(.*(PANIC|WDT).*\)", re.I), "watchdog or panic reset"),
    (re.compile(r"\[sync\].*(fail|error)", re.I), "content sync problem"),
]

EARCON_MS = 600  # AREG_EARCON_DURATION_MS, config.h
TARGET_PERCEIVED_MS = 2700  # docs/latency-plan.md, streaming on
BASELINE_PERCEIVED_MS = 5700  # same source, streaming off

C_OK, C_BAD, C_WARN, C_DIM, C_OFF = "\033[32m", "\033[31m", "\033[33m", "\033[2m", "\033[0m"


class Watcher:
    def __init__(self) -> None:
        self.latencies: list[int] = []
        self.stories: list[str] = []
        self.finishes = 0
        self.troubles: list[str] = []
        self.fw = self.build = None
        self.lines = 0

    def feed(self, line: str) -> None:
        self.lines += 1
        line = line.rstrip("\r\n")

        m = RE_BOOT_VER.search(line)
        if m:
            self.fw, self.build = m.group(1), m.group(2)
            print(f"{C_OK}BOOT{C_OFF}  firmware {self.fw}   build {self.build}")
            print(f"{C_DIM}      (check this build string before blaming hardware){C_OFF}")
            return

        m = RE_RESET.search(line)
        if m:
            reason = m.group(2)
            bad = reason.upper() not in {"POWERON", "POWERON_RESET", "SW", "SW_RESET", "USB", "USB_UART_CHIP_PU"}
            colour = C_WARN if bad else C_DIM
            print(f"{colour}RESET{C_OFF} {reason}")
            return

        m = RE_LATENCY_QA.search(line)
        if m:
            self._latency(int(m.group(1)))
            return

        m = RE_LATENCY_ANY.search(line)
        if m and "play_begin_ms" not in m.group(1):
            print(f"{C_DIM}time  {m.group(1)} = {m.group(2)} ms{C_OFF}")
            return

        m = RE_STORY_SEL.search(line)
        if m:
            self.stories.append(m.group(1))
            print(f"{C_DIM}story {m.group(1)}{C_OFF}")
            return

        if RE_STORY_END.search(line):
            self.finishes += 1
            print(f"{C_OK}IDLE{C_OFF}  story finished — the button should work now")
            return

        for pattern, label in TROUBLE:
            if pattern.search(line):
                self.troubles.append(f"{label}: {line.strip()}")
                print(f"{C_BAD}TROUBLE{C_OFF} {label}")
                print(f"        {line.strip()}")
                return

    def _latency(self, ms: int) -> None:
        self.latencies.append(ms)
        perceived = ms + EARCON_MS
        if perceived <= TARGET_PERCEIVED_MS * 1.15:
            tag, colour = "FAST", C_OK
        elif perceived >= BASELINE_PERCEIVED_MS * 0.85:
            tag, colour = "SLOW", C_BAD
        else:
            tag, colour = "MID ", C_WARN
        n = len(self.latencies)
        print(f"{colour}{tag}{C_OFF}  answer #{n}: {ms} ms measured, "
              f"~{perceived} ms as the child hears it "
              f"{C_DIM}(target ~{TARGET_PERCEIVED_MS}){C_OFF}")

    def summary(self) -> None:
        print("\n" + "=" * 60)
        print("  BENCH SUMMARY")
        print("=" * 60)
        print(f"firmware {self.fw or '?'}   build {self.build or '?'}   {self.lines} log lines")

        if self.latencies:
            xs = sorted(self.latencies)
            mid = xs[len(xs) // 2]
            perceived = mid + EARCON_MS
            print(f"\nAnswer latency, {len(xs)} sample(s)")
            print(f"  fastest  {xs[0]} ms      slowest {xs[-1]} ms")
            print(f"  median   {mid} ms measured, ~{perceived} ms perceived")
            if len(xs) < 5:
                print(f"  {C_WARN}fewer than 5 samples — one fast answer is luck{C_OFF}")
            if perceived <= TARGET_PERCEIVED_MS * 1.15:
                print(f"  {C_OK}PASS — streaming is working{C_OFF}")
            elif perceived >= BASELINE_PERCEIVED_MS * 0.85:
                print(f"  {C_BAD}FAIL — this is the old slow path.{C_OFF}")
                print("         AREG_QA_STREAM_PLAYBACK may not be in the flashed build,")
                print("         or StoryQa__StreamAnswerAudio is false on the server.")
            else:
                print(f"  {C_WARN}between the two known numbers — worth a closer look{C_OFF}")
        else:
            print(f"\n{C_WARN}No answer timings seen.{C_OFF} Ask the toy a question "
                  "during a story: press the button while a story plays.")

        print(f"\nStories played {len(self.stories)}   finished cleanly {self.finishes}")
        if self.stories and not self.finishes:
            print(f"  {C_WARN}a story started but none finished — did you stop early, "
                  f"or did it hang?{C_OFF}")

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


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--port", help="serial port, e.g. COM7 or /dev/ttyUSB0")
    ap.add_argument("--baud", type=int, default=115200)
    ap.add_argument("--replay", type=Path, help="score a saved log instead of a live toy")
    ap.add_argument("--log", type=Path, help="where to write the raw log "
                                             "(default: bench-<timestamp>.log)")
    args = ap.parse_args()

    w = Watcher()

    if args.replay:
        if not args.replay.is_file():
            print(f"no such file: {args.replay}", file=sys.stderr)
            return 2
        for line in args.replay.read_text(errors="replace").splitlines():
            w.feed(line)
        w.summary()
        return 0

    if not args.port:
        print("give --port (or --replay). Find it with: arduino-cli board list",
              file=sys.stderr)
        return 2

    try:
        import serial  # type: ignore
    except ImportError:
        print("pyserial is missing.  pip install pyserial", file=sys.stderr)
        return 2

    log_path = args.log or Path(
        f"bench-{_dt.datetime.now().strftime('%Y%m%d-%H%M%S')}.log")

    print(f"Listening on {args.port} at {args.baud}. Raw log -> {log_path}")
    print("Play with the toy. Ctrl+C when you are done.\n")

    try:
        ser = serial.Serial(args.port, args.baud, timeout=1)
    except Exception as exc:  # noqa: BLE001 - surface the real reason
        print(f"could not open {args.port}: {exc}", file=sys.stderr)
        print("Is the serial monitor open in another window? Close it and retry.",
              file=sys.stderr)
        return 2

    with ser, log_path.open("w", encoding="utf-8") as fh:
        try:
            while True:
                raw = ser.readline()
                if not raw:
                    continue
                line = raw.decode("utf-8", errors="replace")
                fh.write(line)
                fh.flush()  # a crash must not take the evidence with it
                w.feed(line)
        except KeyboardInterrupt:
            pass

    w.summary()
    print(f"Raw log saved: {log_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

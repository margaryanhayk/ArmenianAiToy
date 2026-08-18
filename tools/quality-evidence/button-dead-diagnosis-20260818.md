# The main button appeared dead — what it actually was, and what is still open

2026-08-18/19, bench session on the owner's toy (COM7, fw 1.3.3).

## Symptom

Pressing the toy's main button did nothing. No story, no menu, no serial
line. An evening had already gone into multimeter continuity checks on
GPIO0 before this session started.

## Finding 1 — the toy was running a bench build that took itself over

The flashed image carried `-DAREG_OFFLINE_GAMES_BENCH`
(`AREG_FW_BUILD "games-bench"`). That build starts a game **on its own, 30 s
after every boot**:

```
[games] armed (pick=1), starting in 22s
[story] SD open: /games/mind-reader/intro.mp3 @ 0
```

`offline_games_tick()` runs in the IDLE branch and the game then owns the
loop for minutes, reading only the GREEN/RED pins (21/47). Every main-button
press in that window was genuinely unpolled. This is the whole reason the
toy "did nothing" and then, minutes later, spoke on its own.

Rebuilt and flashed with **no bench flags**. `AREG_CONTENT_SYNC_BENCH` stays
(it lives in `config.h` and is load-bearing — see its comment). Build id set
back to `hold-to-menu`.

## Finding 2 — the `[menu] ask clip missing` report is stale

The 70 story clips and 42 voice clips are on the card. The menu opens and
speaks:

```
[menu] activity ended — asking what next (chain=1)
[welcome] greeting greet-28
[welcome] ask ask-sgrc
```

Content sync now reports everything current — 10 stories at the re-rendered
versions, 104 game clips, 42 voice clips, `[content-sync] PASS`.

## Finding 3 — the button press never reaches the pin

`button_poll()` now prints the **raw** pin edge before debounce, and
`button_begin()` prints the resting level once. Both are permanent: the
button is the toy's only physical input, and when it looks dead there was
previously no way to tell "the wire is off" from "the firmware dropped it".
The print cannot flood — it fires only on a physical level change.

With that build flashed, across **~16 minutes** of capture in four sessions:

```
[button] raw= lines: 0
```

Not one edge. Meanwhile the toy is healthy throughout — `[alive]` every 5 s,
Wi-Fi associated, `[heartbeat] status=200`, content sync PASS.

Ruled out in the firmware, by reading it:

- `AREG_PIN_BUTTON` is 0; `pinMode(0, INPUT_PULLUP)` is called once in
  `button_begin()` and **no other `pinMode` in the entire sketch touches
  GPIO0** (the only other two are the answer buttons, 21 and 47).
- `button_poll()` is called every pass of the IDLE branch, the same branch
  that emits the `[alive]` line we can see arriving.
- Nothing re-claims the pin: the volume pot is GPIO8, mic 4/5/6, amp
  15/16/7, SD 10/11/12/13, LED 48.

So the firmware is correct and the pin simply never changes level.

## Still open — the leading hypothesis

The onboard **BOOT** button is hardwired to GPIO0 on this dev board, so it
is a free test of the pin that bypasses the owner's wiring entirely. It also
produced no edge. A dead external button cannot explain that; something
holding the pin HIGH can.

**Most likely: GPIO0 is shorted to 3V3** — the 10 kΩ pull-up soldered as a
short, or onto the wrong pad. A pin hard-tied to 3V3 cannot be pulled down
by any button, which matches every observation above.

Decisive measurement (not yet taken): continuity **GPIO0 ↔ 3V3**. A real
10 kΩ resistor will NOT beep on a continuity tester; a beep means a short.

Fallback if the pin is damaged rather than shorted: move the main button to
**GPIO18**, which `docs/hardware/schematic-spec.md` already names as the
production choice precisely because GPIO0 is a strapping pin (a child
holding it through a power-cycle forces download mode). GPIO18 is free in
the current pin map.

## Method note — do not reset the toy to read it

`tools/firmware/watch-serial.ps1` reads the port and **never touches DTR or
RTS**. On the S3's native USB-CDC those lines are the reset / boot-mode
gesture; pulsing them is what put this toy into `DOWNLOAD(USB/UART0)` mode
on 2026-08-17 and cost an evening. Opening the port can still reset the chip
on its own — that is CDC connection state, not the script.

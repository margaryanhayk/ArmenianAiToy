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

---

## Continued 2026-08-19 — it is not the pin

The GPIO0-is-dead hypothesis above was **wrong**, and the way it was
disproved is worth keeping.

**Measured, not assumed:** continuity GPIO0 ↔ 3V3 was **silent**. No short.
So the pin was not being held high by the owner's soldered pull-up, and the
leading hypothesis died.

**Main button moved to GPIO18** (`config.h` + `config.h.example`), which
`docs/hardware/schematic-spec.md` already names as the production pin — the
bench had simply never followed its own spec. GPIO18 collides with nothing:
mic 4/5/6, amp 15/16/7, SD 10/11/12/13, LED 48, answer buttons 21/47, volume
pot 8. Rebuilt, flashed, five minutes of capture with the owner pressing:

```
button edges: 0
```

**Two different pins reporting zero edges is not credible as two dead pins.**
So the next thing to rule out was whether the button code is reached at all —
`button_poll()` lives in the IDLE branch and could in principle be starved by
the state machine, which would look identical from outside.

`[alive]` now carries the raw level, read with a bare `digitalRead` in the
5-second diagnostic tick — outside `button_poll()`, outside the debounce,
outside the state machine. It depends only on the loop running, which the
rest of that same line already proves.

```
[alive] ... rssi=-78 btn=UP
```

Owner held the button 15 s (guaranteed to cross three ticks):

```
DOWN ticks: 0    UP ticks: 7
```

And with a plain jumper touched directly from pin 18 to GND, no button
involved at all:

```
DOWN ticks: 0    UP ticks: 48
```

## Where this leaves it

The pin **reads correctly at rest** (`UP`, on the internal pull-up), on both
pins tried, and the firmware is confirmed to be reading it — so the read path
is sound end to end. What has never once been observed is the level going
LOW, by button or by wire.

Every remaining explanation is on the bench, not in this repo:

- the jumper/button is not landing on the header pins believed (a header
  labelled differently, or a neighbouring row),
- the GND used is not a GND,
- a wire is open (broken core, cold joint) — invisible to the eye,
- the button's two wires are on a leg pair that is internally joined (a
  4-leg tactile has two such pairs; only the DIAGONAL pair switches). Note
  this specific fault would read DOWN permanently, not UP, so it does not
  fit — recorded because it was the first suspicion and it is ruled out by
  the data.

Not yet confirmed for any of the captures above: that the owner's action and
the capture window actually overlapped. Several windows closed while he was
mid-question, and one 300 s capture ended at 34 s when the port dropped
(fixed — `watch-serial.ps1` now reconnects instead of exiting, since the S3
re-enumerates USB on every reset). Only two windows are known to have
contained a deliberate action, and both are among the zero-edge results.

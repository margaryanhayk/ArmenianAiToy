# Bench procurement — what is safe to buy without pre-committing production

Written 2026-08-15 for a same-day shopping window. Companion to
`power-tree.md`, `schematic-spec.md`, `bom.md`, `open-questions.md`.

**The governing fact: nothing buyable in a walk-in shop moves either
production battery option forward.** Both Option A (3×AA) and Option B
(1S Li-ion) require the SAME TPS63802 buck-boost — a 2×3 mm SMD part on
a real PCB (`power-tree.md` §0). So a shop purchase today is a BENCH
POWER question only, and must be judged on bench safety and bench
usefulness, not on production alignment.

Bench as it stands: ESP32-S3-DevKitC-1 (N8R8), INMP441 on I2S 4/5/6,
MAX98357A on 15/16/7, WWZMDiB microSD breakout (its own AMS1117 needs
5 V — `power-tree.md` §4 for why this is the MODULE's fault, not
microSD's), bare 8 Ω speaker, USB powered. Firmware 1.3.3.

---

## 1. Bench power — verdict: USB power bank

| Option | Verdict | Reason |
|---|---|---|
| USB power bank, 5 V ≥2 A | **BUY** | Cell + PCM + charge IC with power path + boost are all inside one sealed, CE/UKCA-marked consumer product. Zero new fire path. Works today. |
| 18650 + TP4056 + buck-boost | **DON'T** | See §1.1 — genuinely unsafe unattended at shop-component quality. |
| 4×AA + buck/boost module | **DON'T** | 4×AA alkaline fresh = 6.4 V, ABOVE the TPS63802's 5.5 V ceiling and above a 5 V buck's useful window; burns cells at ~1 A; buys no production information. |
| LiPo pouch + charger module | **DON'T** | Every TP4056 objection plus a puncture/crease thermal-runaway path on a bare desk. |

Power-bank spec: **5 V, ≥2 A on one port** (2.4 A preferred),
≥5000 mAh, **two output ports** (or one port + a USB-A splitter) so the
SD module's 5 V can be tapped without back-feeding the dev board.

Known gotchas, both real:
- **Auto-shutoff.** Many banks cut off below ~50-75 mA. The firmware
  never sleeps and idles ~70 mA (`power-tree.md` §3) — uncomfortably
  close. Ask for a bank advertising a "low-current / small-device /
  earbud mode". If the toy dies after ~30 s of idle, this is why.
- **Pass-through switching** can drop the rail for 100-200 ms when the
  bank swaps between charging and discharging. Do not run an OTA on a
  bank that is simultaneously charging.

What the power bank does NOT prove: it does not exercise the buck-boost,
does not produce a real runtime figure (M6), does not test the
low-battery ladder or the ADC divider (M5). Those stay lab items.

### 1.1 Why a shop Li-ion setup is refused for an unattended bench

- **Two different modules share the name "TP4056".** The bare one
  (charge IC only) has **no protection whatsoever** — no overdischarge
  cutoff, no overcurrent, no short-circuit limit. The protected one
  carries a DW01A (SOT-23-6) plus an 8205A dual MOSFET beside the
  TP4056 and is labelled B+ / B− / OUT+ / OUT−. In a shop bin they look
  nearly identical.
- **Even the protected module lacks three things this design requires**:
  NTC/temperature sensing (the TP4056 TEMP pin is tied off on
  essentially every module, so the 0-45 °C charge window does not
  exist), reverse-polarity protection on the cell input, and
  power-path / load-sharing. Without load-sharing, running the toy off
  OUT+ while charging means charge termination never fires and the cell
  parks at 4.2 V indefinitely — the swelling failure mode named in
  `power-tree.md` §2 as the reason BQ24074 was specified.
- **Cell reversed into the module**: on the bare version the charge
  FET's body diode conducts and the cell short-circuits through the
  module — trace and cell heat, and an 18650 can vent or flame. On the
  protected version the 8205A blocks reverse *discharge*, but the
  reverse-connection current still has body-diode paths. Neither is a
  designed reverse block; the production answer is a P-FET (Q1
  DMG2301L) plus a **keyed** connector so the mistake is not physically
  possible.
- **Cell provenance.** Cheap loose 18650s are commonly re-wrapped
  salvage with false capacity marks and unknown internal condition, and
  are frequently sold *unprotected* (no PCM in the wrap).

Conclusion: a desk, unattended, charging, beside a children's-toy
prototype is the exact scenario this chain is not adequate for. Refuse
it and use the power bank.

---

## 2. Speaker — verdict: BUY, 8 Ω, bigger, two or three, plus a box

Ask for: **8 Ω, 2-5 W, 50-57 mm (66 mm acceptable), full-frame with a
mounting flange.** Buy 2-3 different ones. Refuse 4 Ω.

Power available from the MAX98357A (BTL, P = Vdd² / 2R):

| Rail | Load | Max output | Peak supply current | vs 3V3/8 Ω |
|---|---|---|---|---|
| 3.3 V | 8 Ω | 0.68 W | 0.41 A | reference |
| 3.3 V | 4 Ω | 1.36 W | 0.83 A | +3.0 dB |
| 5.0 V | 8 Ω | 1.56 W | 0.63 A | +3.6 dB |
| 5.0 V | 4 Ω | 3.13 W | 1.25 A | +6.6 dB |

4 Ω refused for the bench for a bench reason on top of the production
one (`schematic-spec.md` §5): at 3.3 V it pulls 0.83 A peak through a
dev-board LDO with a breakout's ~10 µF of bulk. That is a brownout
generator, and a brownout reads as a firmware crash — it will cost a
day of debugging the wrong layer.

Four loudness/clarity levers, ranked by size of effect on this bench:

1. **Enclosure — biggest.** A bare driver on a desk is acoustically
   shorted: front and rear waves cancel below a few hundred Hz, which
   is the band that makes a voice sound close rather than "phone
   speaker". A sealed 50-100 cm³ box recovers it (`schematic-spec.md`
   §5). Cheap fix: a 60×60×40 mm ABS project box, driver mounted on the
   outside face over a cut hole, closed-cell foam tape as gasket,
   hot-melt on every wire pass.
2. **A more sensitive driver — up to ~+7 dB.** A 57 mm at ~87 dB/W/m
   against a generic 40 mm at ~80 dB. Sensitivity is the one spec a
   walk-in shop will not have, which is exactly why he buys two or
   three and compares by ear.
3. **Amp rail 3.3 → 5 V — +3.6 dB** into 8 Ω.
4. **Bulk capacitance at the amp.** 470-1000 µF, 10 V or 16 V,
   low-ESR/low-impedance, across the MAX98357A VIN/GND. The production
   value is a derived 330 µF (`schematic-spec.md` §2); a breakout ships
   ~10 µF. This is why bass transients sound like distortion today.

**EN 71-1 is not a reason to buy quiet.** The 80 dB category-1 limit
and the 78 dB @ 50 cm design target are enforced by the GAIN resistor
and software gain, both downstream of the driver
(`schematic-spec.md` §3/§4). A more sensitive driver reaches the same
78 dB on LESS electrical power — less current, less distortion, more
headroom. Buying loud is strictly correct.

---

## 3. Volume — BUY the resistors, DON'T buy a pot for the speaker line

**There IS a hardware volume path today: the MAX98357A GAIN pin**, five
fixed steps set by one resistor (`schematic-spec.md` §3):

| GAIN pin | Gain |
|---|---|
| 100 kΩ to VDD | 3 dB |
| direct to VDD | 6 dB |
| floating | 9 dB |
| direct to GND | 12 dB |
| 100 kΩ to GND | 15 dB |

Breakout defaults vary by vendor — read the board before assuming.
Buy: a **through-hole resistor assortment (0.25 W), must include
100 kΩ and 10 kΩ.**

**A pot between amp and speaker: DON'T.** Three independent reasons:
- The output is **bridge-tied** — neither terminal is ground, so a
  3-terminal divider has no reference. As a 2-terminal rheostat it
  divides against 8 Ω, so it must be tens of ohms, and at half rotation
  it dissipates a large fraction of the output: up to ~0.8 W at the
  5 V/8 Ω operating point, in a part usually rated 0.1-0.5 W.
- The amp is **filterless class-D** — what is on the wire is a ~300 kHz
  full-rail PWM square, not audio. Series resistance raises output
  impedance, destroys the driver's electrical damping (Qes rises →
  boomy and loose), and makes attenuation frequency-dependent.
- It raises the impedance of a 300 kHz loop on the longest wires in the
  toy, i.e. it worsens radiated emissions (M9).

**A 10 kΩ linear pot for the FUTURE firmware volume: BUY, ~$1**, but
know it does nothing until firmware exists. It is the settled production
architecture (pot as digital gain, wiper → GPIO8, ADC1 only — ADC2 is
dead while Wi-Fi is up). GPIO8 is free on this bench.
Ask for: **10 kΩ linear panel potentiometer (B10K)**, detented if
available.

---

## 4. Boost / 5 V module — DON'T BUY

The power bank IS the boost. A boost module solves a problem he only has
if he abandons USB, which §1 says he should not do today.

Bench 5 V budget, honest, for sizing whatever supplies it:

| Load | Current |
|---|---|
| ESP32-S3 Wi-Fi TX burst | 355 mA |
| ESP32-S3 active baseline | 55-65 mA |
| MAX98357A into 8 Ω @ 3.3 V | 410 mA peak / ~90 mA speech average |
| microSD write burst | 100 mA (init to 200 mA) |
| INMP441 | 1.6 mA |
| **Coincident worst case** | **~0.9-1.0 A** |

Plus the SD breakout's AMS1117 loss — it is an LDO, so it draws the SD
current at 5 V and burns (5 − 3.3) × I as heat. Size any 5 V source at
**≥2 A**, never 1 A.

**Board trap already on file:** this USB-C DevKitC-1 clone does **not**
expose USB 5 V on a header pin — `5VIN` / J1-21 is input-only and reads
~0.14 V. So the SD module's 5 V must come from a second source with a
shared ground. Buy a **USB-A female breakout board (USB to DIP or to
screw terminals), ~$1**, on the bank's second port.

**Better, optional: replace the SD breakout.** A **3.3 V-only microSD
module** (no regulator IC on the board — if you see a 3-pin SOT-223
regulator and an 8-pin level shifter, that is the wrong kind) removes
the second supply entirely and matches the production design, which runs
the socket on the single 3V3 rail (`schematic-spec.md` §2).

---

## 5. IM69D130 PDM mic — DON'T BUY

- The firmware capture path is **I2S only**; PDM-RX on I2S0 is an
  unbuilt firmware slice with its own bench session
  (`open-questions.md`, Sourcing).
- It is a **3.5 × 2.65 mm LGA bottom-port SMD** part — no walk-in shop
  stocks it, and it cannot be hand-soldered onto a breadboard bench.

Run-1 plan is unchanged: buy remaining INMP441 / ICS-43434 stock at
BOM freeze. If he passes a shop with INMP441 or ICS-43434 breakouts,
**buying 2-3 spares is worthwhile** — the whole Western I2S-mic category
is exiting (TDK PCN-000772, LTB passed 2026-06-15) and a dead bench mic
with no replacement stops all voice work.

---

## Full chain — the recommended bench power tree

```
USB POWER BANK (sealed consumer product; CE/UKCA)
  internal: Li-ion cell → integrated PCM (over/under-voltage, OC, short)
            → charge IC with power path → boost → 5.00 V regulated, ≥2 A
  │
  ├── PORT 1 ── USB cable ──► ESP32-S3-DevKitC-1 USB-C  (5 V in)
  │                            │
  │                          on-board reverse-block diode
  │                            │
  │                          on-board LDO (AMS1117/SGM2212-3.3, ~800 mA-1 A)
  │                            │
  │                          +3V3 (dev board rail)
  │                            ├── ESP32-S3-WROOM-1-N8R8   (3.3 V, 355 mA TX peak)
  │                            ├── INMP441  VDD 3.3 V, L/R HARD-WIRED TO GND
  │                            │            (I2S SCK 4 / WS 5 / SD 6)
  │                            └── MAX98357A VIN — SEE NOTE BELOW
  │
  └── PORT 2 ── USB-A female breakout ──► +5 V , GND
                 │                          (GND common inside the bank;
                 │                           keep the wire short and thick)
                 │
                 ├──► WWZMDiB microSD module VCC = 5 V
                 │      └── its own AMS1117 → 3.3 V at the card
                 │          (CS 10 / SCK 12 / MOSI 11 / MISO 13)
                 │
                 └──► [OPTIONAL BENCH EXPERIMENT] MAX98357A VIN = 5 V
                        + 470-1000 µF low-ESR electrolytic across VIN/GND
                        + the breakout's existing 100 nF / 10 µF
                        │
                        └── BTL out ──► 8 Ω speaker in a sealed 50-100 cm³
                                        gasketed box; leads twisted, routed
                                        away from the mic and the antenna
```

**Amp rail — pick ONE and label it:**
- **VIN = 3.3 V (default, safe):** 0.68 W into 8 Ω, 0.41 A peak. Shares
  the dev-board LDO with the ESP32-S3's 355 mA TX bursts — this is a
  brownout risk on a board with no bulk capacitance and is a real
  candidate cause of any unexplained resets seen today.
- **VIN = 5 V (louder, +3.6 dB):** 1.56 W into 8 Ω, 0.63 A peak, and it
  takes the amp OFF the LDO the MCU is using — usually a net stability
  improvement as well as louder.
  **UNVERIFIED LINK, flagged:** the MAX98357A's BCLK/LRCLK/DIN logic
  thresholds at VDD = 5 V driven from 3.3 V logic. The datasheet
  specifies these as absolute voltages (VIH ~1.4 V), not ratiometric,
  which is why 5 V VDD with a 3.3 V MCU is a widely-used combination —
  but this has not been re-read against the datasheet page today.
  **What settles it:** MAX98357A datasheet, Electrical Characteristics,
  "Input Logic-High Voltage (VIH)" row, confirming an absolute figure
  across the full 2.5-5.5 V VDD range. Until then it is a bench
  experiment, not a design decision.

**Grounds:** one common ground. Both branches return to the power bank,
which is a single ground internally. Do not create a second ground path
through a bench supply as well.

---

## 6. Bench wiring issued 2026-08-15 (buttons / latching switch / pot)

Parts actually bought: tactile buttons (green + red), capacitors, one
latching switch, one potentiometer. Wiring issued against firmware 1.3.3
and the live boot log (`button=0 led=48`, mic 4/5/6, amp 15/16/7, SD
10/12/11/13).

| Net | From | To | Status |
|---|---|---|---|
| BTN_YES | GPIO21 | green tactile → GND | NEW |
| BTN_NO | GPIO47 | red tactile → GND | NEW |
| VOL_WIPER | GPIO8 (ADC1_CH7) | pot wiper (+1 kΩ, 100 nF at pin) | NEW, **no firmware reads it** |
| VOL_TOP | 3V3 | pot end | NEW — **never 5 V**, ADC absmax 3.6 V |
| VOL_BOT | GND | pot other end | NEW |
| SW_5V | USB-A breakout +5 V | latching switch → board 5V pin + SD VCC | NEW, Config B only |

- GPIO21/47 re-confirmed clear: no strapping function, not ADC2-bound,
  untouched by SPI flash (26-32) or octal PSRAM (33-37 on R8). GPIO8 is
  ADC1 — the required half, since ADC2 (GPIO11-20) is dead while Wi-Fi
  is up.
- Buttons: minimum wiring is 2 wires each on the internal ~45 kΩ pull-up
  plus the existing 30 ms software debounce (`answer_buttons.cpp`). The
  100 nF/1 kΩ pair is optional at the bench and, if fitted, follows the
  corrected placement rule in `schematic-spec.md` §2.
- **`answer_buttons.h` requires BOTH `AREG_PIN_BUTTON_YES` and
  `AREG_PIN_BUTTON_NO` defined** (line 23); one alone folds the whole
  module to silent no-ops. And the only callers are `offline_quiz.cpp` /
  `offline_games.cpp`, both behind bench flags — a plain production build
  with the pins defined compiles the driver in and never polls it.
- **No ADC read exists anywhere in the firmware** (verified by grep,
  2026-08-15): no `analogRead`, no `AREG_PIN_VOLUME`. The pot is
  physical readiness only.
- **Latching switch must not sit in a VBUS line that also carries data.**
  With VBUS cut and D±  still driven by a live host, current enters the
  S3 through its USB-pin ESD clamps and part-powers the chip. Switch only
  a power-only path.
- **UNVERIFIED LINK, flagged:** feeding 5 V into J1-21 (`5VIN`) assumes a
  blocking diode between USB VBUS and that pin — consistent with the
  ~0.14 V reading on file (§4) but the diode direction has not been
  measured. **What settles it:** USB plugged, no external supply, measure
  J1-21 to GND; near 0 V (not ~4.7 V) confirms VBUS cannot reach the pin
  and injection there is safe.

**What this pre-commits: nothing.** The power bank, the resistor kit,
the speakers, the box and the USB breakout are bench tools. Both
production battery options still require the same TPS63802 buck-boost,
the same reverse-block P-FET and the same star ground on a real PCB, and
neither is affected by anything on this list.

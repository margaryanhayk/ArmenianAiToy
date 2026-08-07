# Areg power tree — complete chains, both battery options

Status: design specification from the 2026-08-07 four-lens hardware
review. Nothing here is bench-verified on a production PCB yet; the
measurements that settle each open number are in
`open-questions.md`.

**The rule this document exists to enforce: no component without its
chain.** Every option below runs source → protection → regulation →
load, with the voltage at every stage.

---

## 0. Why a regulator is mandatory in EVERY option

The system rail is 3.3 V (ESP32-S3 spec 3.0-3.6 V, ≥0.5 A delivery).
No battery provides that across its life:

| Source | Fresh | Dead | Crosses 3.3 V? |
|---|---|---|---|
| 3×AA alkaline in series | 4.5 V | ~3.0 V (1.0 V/cell cutoff) | YES |
| 3×NiMH | 4.2 V | ~3.0 V | YES |
| 1S Li-ion | 4.2 V | 3.0 V | YES |
| USB | 5.0 V | — | above |

Because every chemistry's discharge curve CROSSES 3.3 V:
- a plain **buck** drops out near ~3.4 V input and throws away the
  last ~12 % of the battery;
- a plain **LDO** (e.g. AMS1117: 1.1-1.3 V dropout) is worse — it
  needs ≥4.4 V input to make 3.3 V and additionally burns
  (Vin−3.3 V)·I as heat;
- therefore the regulator must be a **buck-boost**.

**Selected: TI TPS63802** — VIN 1.3-5.5 V, 3.3 V out at 2 A, 11 µA
quiescent, seamless buck↔boost. Efficiency ~93 % at the 60-200 mA
band the toy lives in; ~88-90 % at the 800 mA worst-case burst.
(The 11 µA Iq is what makes standby viable; a generic 500 µA-Iq
converter would dominate the sleep budget on its own.)

---

## 1. Option A — 3×AA alkaline (run-1 candidate)

```
3×AA (4.5 V fresh → 3.0 V end-of-life)
  │
  ├── keyed battery contacts (compartment: screw-closed — toy-safety requirement)
  │
[Q1] reverse-block P-FET  DMG2301L (Rds ~50 mΩ → drop ~30 mV @0.6 A;
  │                       a Schottky's 300-400 mV would eat 10 % of a
  │                       dying pack's remaining headroom)
  │
[U4] TPS63802 buck-boost ──────────────► +3V3 @ ≤2 A
  │        L = 1.5-2.2 µH shielded 0630; Cout 100 µF effective
  │
  ├─[FB1 ferrite BLM18PG]──► +3V3_D: ESP32-S3 module (22 µF+100 nF at pin,
  │                          EN: 10 kΩ+1 µF), INMP441 (10 Ω+10 µF+100 nF),
  │                          microSD socket (10 µF+100 nF AT the socket)
  │
  └─(no ferrite — transient path)──► MAX98357A (100 nF + 22 µF ceramic
                                     + 330 µF polymer) → 8 Ω speaker
```

- Why no ferrite in the amp branch: the bead would starve exactly
  the 0.4-0.6 A transient the bulk capacitor exists to serve.
- Battery sense: BAT+ →1.0 MΩ→ tap →820 kΩ→ GND, 100 nF at the tap,
  into **GPIO8/ADC1** (never ADC2 — dead while Wi-Fi runs). Divider
  burns 2.3 µA. Curve-fit ADC calibration + one per-unit offset at
  the factory, or the reading is ±100 mV ≈ ±20 % state of charge.
- Runtime (current budget in §3): the modelled day draws
  ≈ 1,010 mAh at the cell → **~1.75 days per set** after Peukert
  derating. Recurring cost to the parent ≈ €150-300/yr.
- What this option BUYS: no UN 38.3, no IEC 62133-2, no lithium
  shipping paperwork, no charge circuit, weeks off the calendar.
- What it COSTS: the recurring cells, 85 g of mass, and the
  compartment-open risk (mitigated by the screwed door the toy rules
  demand anyway).

## 2. Option B — 1S Li-ion 2000 mAh pouch (run-2 target)

```
USB-C VBUS (5 V) ── CC1→5.1 kΩ→GND, CC2→5.1 kΩ→GND (both, separately —
  │                without them a compliant USB-C source supplies NOTHING)
[F1] PTC 1206L050 (0.5 A hold / 1.0 A trip — shorted-VBUS fire path)
[U5] TPD4S014 (VBUS OVP + CC/D± ESD, IEC 61000-4-2 L4)
  │
[U7] BQ24074 charger with POWER PATH ◄──── NTC 10 kΩ (Murata NCP15XH103)
  │      • load sharing: system runs from USB while the cell charges     bonded to the CELL FACE,
  │        (without it: termination never fires → cell parked at 4.2 V   not the PCB — 0-45 °C
  │        forever → swelling; and the cell cycles while plugged in)     charge window
  │      • charge current 500 mA (0.25C): ~4.5 h, cool; 1 A only if
  │        the closed-enclosure thermal soak passes <45 °C cell surface
  │  OUT
  ├──────────────► [U4] TPS63802 ► +3V3 (identical tree as Option A)
  │
 BAT ── keyed JST-ZH ── pack with integrated PCM (DW01A-class:
        4.25 V overcharge / 2.9 V overdischarge / 3 A OC) ── 803860
        pouch 2000 mAh, IEC 62133-2 + UN 38.3 reports FOR THAT MODEL
```

- Firmware low-battery ladder: 3.60 V = refuse OTA/content-sync
  start; 3.45 V = play the "charge me" clip, refuse new stories;
  3.20 V = finish the sentence, deep sleep. Never die mid-story.
- Production fuel gauge: MAX17048 (I²C, 23 µA) replaces the divider
  and the flat-middle guessing.
- One-time cost: ~€11 BOM (§ bom.md) + €6-10k pack certification +
  UN38.3 lead time. Recurring cost: zero.
- EU Battery Reg (EU) 2023/1542 Art. 11 (from 2027): pack must be
  END-USER-replaceable → connectorized pack behind a screw hatch.
  Design it in now; it is free now and a tooling change later.

## 3. Load currents the tree must serve (worst honest numbers)

| Load | Rail | Peak | Note |
|---|---|---|---|
| ESP32-S3 Wi-Fi TX burst | 3V3 | 355 mA | 802.11b @20.5 dBm, datasheet |
| ESP32-S3 active baseline | 3V3 | 55-65 mA | 240 MHz, no power save (as built) |
| MAX98357A into 8 Ω @3.3 V | 3V3 | ~410 mA pk | speech avg ~90 mA |
| microSD write burst | 3V3 | 100 mA | init up to 200 mA |
| INMP441 | 3V3 | 1.6 mA | |
| LED (WS2812) | 3V3/5V | 7-60 mA | draws ~0.7 mA while "OFF" — gate its rail with a P-FET in any battery build |
| **Coincident worst case** | | **~0.9-1.0 A** | TX + bass transient + SD write — this is what sizes U4 at 2 A and the 330 µF bulk |

The modelled day (1 h stories + 20 voice turns + 12 h idle as built)
≈ **1,010 mAh/day at the cell**. Idle is 840 of it — the firmware
never sleeps, so doing NOTHING costs 4.6× the storytelling. The
firmware fixes (WiFi.setSleep = 2×; light-sleep idle = 10-20×) are
worth more than any battery-size change.

## 4. The 5 V question, resolved head-on

The ONLY load that ever wanted 5 V is the amplifier, and only for
headroom:

| Amp rail | Ceiling into 8 Ω | Headroom over the 0.44 W crest peak |
|---|---|---|
| 3.3 V | 0.68 W | 1.9 dB with an 84 dB/W/m driver — too tight |
| 5 V | 1.56 W | 5.5 dB with the same driver |
| 3.3 V + **≥88 dB/W/m driver** | 0.68 W | ≥5.9 dB — **fine** |

**Speaker sensitivity and rail count are ONE decision.** Specify the
driver at ≥88 dB SPL/W/m (verified by measurement, not the vendor
headline) and the entire product is a single 3V3 rail — no boost
converter, no second power domain, less EMI, smaller BOM. If the
chosen driver measures ≤86 dB, a 5 V rail (and its bulk-cap math:
330 µF at 0.625 A peak) comes back. The SD card does NOT want 5 V
and never did — that was the bench breakout's AMS1117 dropout
(1.1-1.3 V) masquerading as a card requirement.

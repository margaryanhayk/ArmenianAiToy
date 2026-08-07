# Areg production schematic specification (rev A draft)

From the 2026-08-07 schematic review. Concrete enough to draw and to
quote. Companion documents: `power-tree.md` (rails), `bom.md`
(parts/prices), `open-questions.md` (what only a lab settles).

## 1. Production pin map (ESP32-S3-WROOM-1-N8R8/N16R8)

| Function | GPIO | Constraint honored |
|---|---|---|
| MIC BCK / WS / SD | 4 / 5 / 6 | ADC1-range pins, no strapping |
| AMP BCK / LRC / DIN | 15 / 16 / 7 | |
| AMP SD_MODE (shutdown/mute) | **17** | new — mute between clips kills class-D idle hiss + 2.4 mA |
| SD CS / MOSI / SCK / MISO | 10 / 11 / 12 / 13 | FSPI IO_MUX-native group — do not move |
| SD card-detect | **9** | firmware can report "card removed" instead of failing silently |
| Volume pot wiper | **8** | **ADC1 only** — ADC2 (GPIO11-20) is unusable while Wi-Fi is active |
| MAIN button | **18** | **moved off GPIO0** (strapping: child holding it through a power-cycle = download mode = dead-looking toy). GPIO0 → 10 kΩ pull-up + factory test pad only |
| YES / NO buttons | 21 / 47 | both clean on N8R8; 47's diff-clock alt-function applies only to OPI-DDR modes not used here |
| LED data | 48 | free on N8R8 (1.8 V caveat is R16V-only) |
| USB D−/D+ | 19 / 20 | receptacle or pogo test pads via ESD array |
| Strapping hygiene | 0, 3, 45, 46 | 10 kΩ pulls (3 has NO internal pull and must never float); all unused |
| Unavailable | 35/36/37 | consumed by octal PSRAM on R8 SKUs — they do not exist on the module |
| Spare → test pads | 1, 2, 14, 38 | |

## 2. Net-by-net components — required vs optional

| Circuit | Components | Verdict |
|---|---|---|
| Module power | 22 µF + 100 nF ≤3 mm from pin 2 | **REQUIRED** (Espressif Fig. 7) |
| Module EN | 10 kΩ to 3V3 + 1 µF to GND | **REQUIRED** — missing = intermittent boot |
| GPIO3 | 10 kΩ to GND | **REQUIRED** — no internal pull |
| GPIO45/46 | 10 kΩ to GND each | **REQUIRED** — strapping determinism |
| Mic VDD | 100 nF (datasheet) + 10 Ω/10 µF RC | 100 nF REQUIRED; RC recommended (mic PSRR is poor at RF and STT accuracy is the product; costs 16 mV) |
| Mic L/R | **hard-wired to GND — no resistor, no jumper** | **REQUIRED** — firmware reads the LEFT slot; floating L/R = noise that looks like working capture (documented failure) |
| Amp VDD | 100 nF ≤2 mm + 22 µF ≤5 mm + **330 µF polymer** ≤15 mm | **REQUIRED**. Derived, not folklore: C = I·Δt/ΔV = 0.625 A × 50 µs (buck settle) / 0.1 V = 313 µF. At 3.3 V/8 Ω the same math gives 220 µF. Derate ceramics for DC bias (a "100 µF" 0805 X5R is ~40 µF at 3.3 V) |
| Amp SD_MODE | bare GPIO17, no resistor | **REQUIRED shape** — internal 100 kΩ pulldown holds shutdown at reset (toy boots silent); 3.3 V drive lands in the "Left" window (>1.4 V) |
| Amp GAIN | ONE 0402, value chosen at the bench | **REQUIRED** — this is the hardware loudness ceiling. Procedure in §4 |
| Speaker leads | ferrite (BLM18PG) + 1 nF C0G to GND, each leg | **footprints REQUIRED, parts DNP-able** — EMC insurance; retrofit after tooling = respin |
| Buttons ×3 | 10 kΩ pull-up, 1 kΩ series, 100 nF | recommended. The 1 kΩ is load-bearing: without it the 100 nF dumps into the GPIO on every press. τ = 1 ms sits under the firmware's 30 ms debounce |
| Volume pot | 10 kΩ lin **detented** (Bourns PTV09A-4020F-B103); wiper →1 kΩ→ GPIO8, 100 nF at pin | knob chosen over buttons/encoder because its ANGLE is the display for a pre-reader; never a pot in the class-D OUTPUT (bridge-tied 300 kHz PWM — see §3) |
| SD socket | 10 kΩ pull-ups on CS, CMD/MOSI, DAT0/MISO **and DAT1, DAT2** | **REQUIRED** — floating DAT1/2 can flip a card out of SPI mode; the classic works-on-bench-fails-in-production item |
| SD VDD | 10 µF + 100 nF AT the socket | **REQUIRED** — 100 mA write bursts, ~200 mA init |
| I2S/SPI series | 33 Ω at the driving end | recommended, not functional at these lengths (<50 mm rule satisfied); they soften the periodic BCLK/LRCLK harmonics on a radiated-emissions scan |
| USB port | USBLC6-2SC6 (D±) + TPD4S014 or SMAJ5.0A (VBUS) + PTC | **REQUIRED** — module ESD rating is a HANDLING rating, not an IEC 61000-4-2 system rating; the user is a child on carpet |
| Inrush | TPS22918 soft-start switch, C_CT for ~5 ms rise | **REQUIRED** once bulk >~100 µF (350 µF here would draw ~1.75 A at plug-in and trip source limits) |
| Reverse block | P-FET DMG2301L in the battery path + keyed connector | **REQUIRED** with any battery |
| LED | WS2812B-2020 on 5 V needs a 3.3→5 V shifter (SN74LVC1T45) — VIH is 3.5 V and the S3 can't guarantee it. On a 3V3-only build: 3 discrete LEDs at 2-3 mA, resistors sized from the VF-vs-IF curve, not the 20 mA headline | one or the other **REQUIRED** — "usually works" is not shippable |

## 3. Volume architecture (three layers)

1. **Hardware ceiling** — the MAX98357A GAIN resistor (5 fixed
   steps: 100 kΩ→VDD=3 dB, VDD=6 dB, float=9 dB, GND=12 dB,
   100 kΩ→GND=15 dB). No firmware bug can exceed it.
2. **Parent maximum** — additive `maxVolume` field on the content
   manifest (delivered/cached exactly like `bedtimeMusicEnabled`).
3. **Child control** — the detented pot as digital gain: sample
   20 Hz, median-of-5, 2-LSB hysteresis, perceptual (log) mapping.

Rejected with reasons: pot in the speaker line (bridge-tied output —
both terminals swing; filterless PWM — the "audio" on the wire is a
300 kHz square; the pot would need ≥1 W rating and wears in the
highest-current path). Digital-pot/I2C volume (no analog node exists
in a MAX98357A design). I2C codec (TLV320DAC3100) is the upgrade
path IF a headphone jack becomes a requirement — volume in the
analog stage costs zero SNR and the jack is a real feature.

The SNR objection to software volume, quantified and dismissed: room
floor 30-35 dBA, target 70 dB → ~40 dB useful range; a 16-bit path
attenuated 30 dB still has 66 dB SNR. The room ate the argument.

## 4. Loudness ceiling procedure (bench, per design not per unit)

1. GAIN=float (9 dB), software gain 1.0. Play 1 kHz at 0 dBFS.
2. Class-2 SLM, on-axis, **50 cm**, A-weighted.
3. Step the GAIN resistor down until **0 dBFS ≤ 78 dB LpA @ 50 cm**.
4. That resistor value becomes a controlled BOM line.
5. Software max set so ordinary narration ≈ 70 dB LpA @ 50 cm.

Legal context (EN 71-1:2011+A3 cl. 4.20, at 50 cm): voice-toy
exposure category 1 = **LpAeq ≤ 80 dB / LpCpeak ≤ 110 dB**.
Close-to-the-ear category would be 60-70 dB — the CATEGORY is a
notified-body call and changes the ceiling by ~15 dB; resolve before
enclosure tooling. US route (ASTM F963) has different numbers.

## 5. Speaker and acoustics

- Driver: **50 mm, 8 Ω, ≥1 W at 50 °C (specify the HOT rating),
  sensitivity ≥86 dB/W/m (≥88 dB to stay 3V3-only — see
  power-tree.md §4), Fs 150-400 Hz, impedance minimum ≥6 Ω across
  the band.** Buy from a vendor who publishes Thiele-Small
  parameters (Dayton CE50MP-8 as reference) — without Fs/Qts/Vas the
  enclosure cannot be designed, only guessed.
- **Sealed chamber, 50-100 cm³, gasketed** (foam/silicone compressed
  30-50 %). Ported rejected: a port is a tuned resonator an ID-drawn
  box will mistune, a hole a child feeds, and an excursion risk.
- **Seal the chamber, not the toy**: every boss and wire pass into
  the speaker chamber gets hot-melt or a grommet. A leaky chamber
  cancels the very band that makes a voice sound close — the most
  common cause of "sounds like a phone speaker", and the hardware
  twin of the "thin, far away" complaint already fought in renders.
- Mic port: Ø0.7-1.0 mm, gasketed to the enclosure, opposite face
  from the speaker, ≥40 mm away — there is no echo cancellation, so
  geometry does that job.
- 4 Ω rejected: doubles peak current (1.25 A vs 0.625 A at 5 V) and
  triples the bulk cap, for loudness the 80 dB limit forbids using.

## 6. Storage

No user-accessible microSD slot (15×11 mm = choking-hazard finding
at 4-7; sockets die on a coin or a backwards card; a removed card is
a silent toy). Rev A: internal microSD in a **push-pull** (friction)
socket — Molex 5031821852 / Hirose DM3D-SF, NOT push-push (spring
eject) — behind the screwed enclosure, card-detect wired to GPIO9.
Zero firmware change. Rev B: soldered eMMC 4 GB (SDMMC 4-bit, ~+$1.25
net) — a deliberate revision with its own bench cycle because the
mount path and content-sync tests change. SPI-NOR (64 MB) rejected:
~13 stories, a ceiling the library already approaches. Card grade:
industrial/pSLC — consumer cards corrupt on power loss mid-write,
and a toy loses power by being switched off mid-story.

## 7. Layout rules that are decisions, not preferences

- Antenna keep-out: no copper, no battery, no speaker magnet. A
  magnet next to the PCB antenna costs real RSSI → Wi-Fi retries →
  355 mA bursts: an RF mistake that presents as a battery problem.
- Star ground at the bulk cap; speaker return NEVER shares copper
  with mic ground (mic ground = most sensitive node; the README's
  top wiring mistake, with higher stakes on battery).
- Twist speaker leads; route away from mic and antenna.
- Module EPAD soldered to the ground plane.
- Firmware rule with a hardware root cause: never write SD while the
  amp is at full output AND Wi-Fi transmits — content sync should
  run with SD_MODE low (mute). Removes the worst-case coincidence.

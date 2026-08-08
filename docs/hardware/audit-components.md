# Areg component audit — everything except the MCU

Date: 2026-08-08 (overnight audit, owner request: "are my current
components good or not, MCU → speaker, all names and parameters").
Scope: power chain, protection, audio in, audio out, speaker,
controls, SD, LED — production BOM **and** the bench modules on the
desk today. The MCU itself is audited by a parallel report.

Method: every claim below is tagged **[DS]** (datasheet/derived
arithmetic), **[WEB]** (verified online during this audit,
2026-08-08), or **[LAB]** (only a measurement settles it — cross-ref
`open-questions.md` M-numbers). Where a number could not be
verified, it says so.

---

## 0. Verdict table (one line per subsystem)

| Subsystem | Part(s) on file | Verdict |
|---|---|---|
| Buck-boost regulator | TPS63802DLAR | **KEEP** — still the right part; runner-ups lose on package or Iq |
| Reverse block | DMG2301L | **KEEP** — verify Rds at VGS −2.5 V from curve (minor) |
| Soft-start switch | TPS22918DBVR | **KEEP** |
| USB protection | USBLC6-2SC6 + TPD4S014 + SMAJ5.0A + PTC | **KEEP, except the PTC** — 1206L050 (0.5 A hold) is under-sized hot; go 1206L**075** |
| Charger (Li-ion opt.) | BQ24074 + NCP15XH103 + MAX17048 | **KEEP** (paper design; M3/M4 gate it) |
| Microphone | INMP441, fallback SPH0645LM4H-B | **REPLACE THE PLAN** — INMP441 dead, ICS-43434 LTB passed, **SPH0645 itself now obsolete**; production path = Infineon IM69D130 (PDM) |
| Amplifier | MAX98357AETE+T | **KEEP** — 3V3/8Ω honestly sufficient; SPL math below, both ways |
| Speaker | "50 mm 8 Ω ≥88 dB/W/m Fs≤400" (ref CE50MP-8) | **RESPEC** — ≥88 dB @ 50 mm is an empty set on the market; CE50MP-8 fails the spec's own T/S rule; widen to 50-70 mm, gate at ≥85 dB **measured in enclosure** |
| Volume pot | Bourns PTV09A-4020F-B103 | **KEEP** (note: 330 µA standing drain) |
| Buttons | "6 mm 160 gf, e.g. B3F-1000" | **KEEP w/ correction** — B3F-**1002** is the 160 gf part; B3F-1000 is ~100 gf |
| SD socket + card | Molex 5031821852 + 8 GB industrial pSLC | **KEEP** |
| Status LED | WS2812B-2020 + SN74LVC1T45 + gate FET | **REPLACE on battery builds** — WS2812B VDD min 3.5 V; no valid rail exists on either battery option. 3 discrete LEDs, delete U7 + Q2 |
| Bench modules | devkit + INMP441 + MAX98357A + WWZMDiB SD + bare 8 Ω | per-module verdicts in §10 |

---

## 1. Regulation — TPS63802 (U4)

**Chain (Option A):** 3×AA 4.5→3.0 V → keyed contacts → Q1 DMG2301L
(−30 mV) → **TPS63802** (L1 2.2 µH 0630, Cout 100 µF eff.) → +3V3 ≤2 A
→ FB1 ferrite → digital loads / no-ferrite branch → amp.
**Chain (Option B):** BQ24074 OUT (3.5-5.0 V) or cell 4.2→3.0 V →
same TPS63802 → same tree.

**Parameter card — TI TPS63802DLAR** [DS, VIN/Iq/2A re-verified WEB]
- Package: 11-pin 2.5×3.0 mm DFN (DLA).
- VIN 1.3-5.5 V (start-up needs >1.8 V — a dead-flat 3×AA at 3.0 V
  starts with margin); VOUT fixed 3.3 V; **2 A output**, buck and
  boost; automatic 4-cycle buck-boost through the VIN≈VOUT crossover
  — exactly where every battery option spends most of its life.
- **Iq 11 µA** operating; efficiency ~93 % in the 60-200 mA band,
  ~88-90 % at the 0.8-1.0 A coincident worst case (power-tree §3).
- Inductor: shopping list's TDK VLS6045EX-2R2M (2.2 µH, Isat ~3.1 A)
  is inside the datasheet's application range — **[LAB/layout] confirm
  against the datasheet L-table for the fixed-3V3 variant before
  layout**; saturation ≥2.5 A is the binding constraint.

**Why it wins:** the only common part that combines 2 A, seamless
buck-boost, AND 11 µA Iq. The Iq matters because standby is the
battery budget (idle is 840 of the 1,010 mAh modelled day).

**Runner-ups:**
- **ADI MAX77827** — Iq **6 µA** (wins that number, verified WEB) but
  loses on **package: WLP, 0.4 mm pitch** — fine-pitch wafer-scale
  assembly on a toy PCB built at a budget assembler is a yield risk
  the 2.5×3 mm DFN doesn't carry. Also single-source ADI pricing.
- **TI TPS63021** — 3A-class, but Iq **25-50 µA** [WEB] = 2-5× the
  TPS63802; loses the standby budget for output current the toy
  never uses.
- **TI TPS63070** — VIN to 16 V (irrelevant here), Iq ~50 µA: same loss.

**Honest caveat:** 11 vs 6 µA is academic while the firmware idles at
~70 mA. The Iq argument becomes real only after light-sleep ships.

## 2. Protection chain

### Q1 — DMG2301L (reverse-battery block, both options)

**Chain:** BAT+ → keyed connector → **DMG2301L** (P-FET, source to
battery, gate to GND, drain to TPS63802 VIN) → regulator.

- VDS −20 V, P-channel, SOT-23. Rds(on): the dossier's "~50 mΩ" is
  the VGS = −4.5 V number; at end-of-life pack (VGS ≈ −3.0 V) expect
  **~60-100 mΩ from the curve → 40-70 mV at 0.7 A** [DS, approximate
  — read the exact curve at order time]. Still 5-10× better than a
  Schottky.
- **Why it wins:** a Schottky (SS34 class) drops 300-400 mV — that is
  10 % of a dying AA pack's remaining headroom and ~0.25 W of heat
  at the 0.7 A burst. The FET's body diode conducts at first plug-in,
  then the channel shorts it out.
- Runner-up: **LM66100 ideal-diode** — cleaner, but $0.35 vs $0.09
  for zero functional gain at these currents.

### U6 — TPS22918 (soft-start / inrush)

**Chain:** source (5 V USB or battery post-Q1) → **TPS22918** (CT cap
sets ~5 ms ramp) → the ~350 µF of downstream bulk.

- 5.5 V max, 2 A, Ron ~55 mΩ, slew set by C_CT. Required because
  350 µF hitting a hard 5 V edge draws amps and trips current-limited
  sources (S2 sim owns the C_CT value). **KEEP.**

### USB port protection (U5a/U5b/D60/F1)

**Chain:** USB-C receptacle → CC1/CC2 **each** 5.1 kΩ to GND →
TPD4S014 (CC/D± protection) → D± → USBLC6-2SC6 → GPIO19/20;
VBUS → F1 PTC → SMAJ5.0A clamp → TPS22918/BQ24074.

- **USBLC6-2SC6** (SOT-23-6): IEC 61000-4-2 ±15 kV air / ±8 kV
  contact, ~3.5 pF line capacitance — fine for the S3's full-speed
  (12 Mbps) USB. Needed because the module's HBM rating is a
  handling rating, not a system rating. **KEEP.**
- **TPD4S014**: CC1/CC2 short-to-VBUS (24 V) protection + ESD. Note
  it protects CC/D±, NOT the VBUS rail itself — so the **SMAJ5.0A is
  not redundant**; it is the VBUS surge clamp (standoff 5 V, clamp
  ~9.2 V, inside BQ24074's 26-30 V abs max / 6.6 V OVP). **KEEP both.**
- **F1 = 1206L050: FINDING — under-specified.** Hold 0.5 A at 23 °C,
  trip 1.0 A, and PTC hold current derates ~25-35 % hot: **at 60 °C
  in a sealed charging toy it holds only ~0.35 A** [DS derating
  curve]. The BQ24074 input at the 500 mA default IS 0.5 A, plus
  leakage — nuisance trips in summer are designed in.
  **Change to 1206L075 (0.75 A hold / 1.5 A trip)**; still trips a
  shorted VBUS fast, never trips the legitimate 500 mA charge. If
  charge current is ever raised to 1 A (after M3), the PTC moves
  again to 1206L110 — tie these two BOM lines together.

### Li-ion option chain (U7 charger group) — paper-correct, KEEP

**Chain:** VBUS 5 V → PTC → TPD4S014 → **BQ24074** (power-path,
ISET = 500 mA, TS ← NCP15XH103 NTC **bonded to the cell face**,
0-45 °C window) → OUT → TPS63802; BAT ← keyed JST-ZH ← PCM-protected
803860 pouch. Fuel gauge **MAX17048** (I²C, ~23 µA) on the cell.

- BQ24074: 6.6 V input OVP, DPPM load-sharing (the feature that
  stops the "cell parked at 4.2 V forever" failure), 10-pin VQFN.
- All four M-gates (M3 thermals, M4 NTC window, M5/M2 gauge/current)
  stand; nothing in this audit changes that design.

## 3. Audio in — the microphone crisis (worse than the dossier says)

The dossier flags INMP441 EOL with SPH0645LM4H-B as the named
fallback. This audit checked the whole field [WEB, 2026-08-08]:

| Part | Interface | SNR | AOP | Sens. | Current | Lifecycle (verified) |
|---|---|---|---|---|---|---|
| INMP441 (current design) | I2S 24-bit | 61 dBA | 120 dB SPL | −26 dBFS | 1.4 mA | **Discontinued** — no longer manufactured; breakout stock only |
| TDK ICS-43434 | I2S 24-bit | 65 dBA | 120 dB SPL | −26 dBFS | 0.49 mA | **TDK PCN-000772 (2026-01-15): LTB 2026-06-15 — ALREADY PASSED — LTS 2027-01-15.** Distributor shelf stock only |
| TDK T3902 | PDM | 64.5 dBA | 120 (126 HP-mode) | — | 0.43 mA | **Same PCN** (listed as MMICT3902) — dead end, do not migrate to it |
| Knowles SPH0645LM4H-B (dossier fallback) | I2S 24-bit | 65 dBA | 120 dB SPL | −26 dBFS | ~0.6 mA | **Obsolete at Digi-Key** (line moved Knowles→Syntiant; -B no longer in production). The dossier's fallback is dead |
| CUI/Same Sky CMM-4030D-261-I2S | I2S 24-bit | 59 dBA | — | −26 dBFS | 0.75-1 mA | **Discontinued** (Same Sky lists it under /discontinued/) |
| MemsSensing MSM261S4030H0 | I2S 24-bit | **57 dBA** | ~140 dB max SPL claim | −26 dBFS | >1 mA | In production (CN), cheap, on LCSC — but −4 dB SNR vs even the INMP441 |
| **Infineon IM69D130** | **PDM** | **69 dBA** | **130 dB SPL** | −36 dBFS | ~1 mA | **ACTIVE** — Infineon product page updated 2026-07; XENSIV line with Infineon longevity practice |

**The finding:** the Western *I2S* MEMS mic category is being exited
wholesale (TDK winding down, Knowles consumer line transferred and
pruned, CUI out). Every I2S drop-in candidate is EOL or a 57 dBA
Chinese part. The only current-production, spec-*superior* device is
PDM.

**Recommendation (two-stage, matches run sizes):**
1. **Run 1 / bench:** keep INMP441 (breakouts on hand + module
   market stock) or buy remaining ICS-43434 distributor stock
   (better: +4 dB SNR, 24-bit, near-drop-in; ships until 2027-01).
   Quantity needed for a 50-500-unit run is trivially available today
   — but buy at BOM-freeze, not at PCB-order.
2. **Production rev A:** **Infineon IM69D130**, PDM.
   - **Parameter card:** SNR 69 dBA (a full 8 dB over INMP441 — this
     is directly STT word-error-rate margin in a room with a fan),
     AOP 130 dB SPL (a shouting child at 10 cm no longer clips the
     front end — INMP441's 120 dB does), THD <1 % at 128 dB SPL,
     sensitivity −36 dBFS (10 dB lower — recover in digital gain;
     noise floor is set by the mic, so nothing is lost), 28 Hz
     roll-off, PDM clock 1-3.3 MHz, LGA bottom-port.
   - **Chain:** +3V3_D → 10 Ω + 10 µF RC + 100 nF at VDD (unchanged
     discipline) → IM69D130 → PDM CLK/DATA on two GPIOs → **ESP32-S3
     I2S0 in PDM-RX mode** (S3 supports PDM RX on I2S0 only — pin
     choice stays flexible via GPIO matrix; I2S1 cannot do PDM).
   - **Cost:** the firmware capture path changes (PDM config +
     CIC/decimation handled by the S3 peripheral; new bring-up bench
     session). That cost is real and is why it must NOT be a
     mid-layout surprise: **decide before footprint freeze** (this
     was already the open-questions instruction; the answer is now
     researched: IM69D130, not SPH0645).
- Fallback-of-the-fallback (if PDM slice can't be funded):
  MSM261S4030H0 keeps the I2S firmware byte-identical but donates
  4 dB of SNR — measurable in STT accuracy; treat as bench-proven
  last resort only.

## 4. Audio out — MAX98357A (U2)

**Chain:** +3V3 (amp branch, **no ferrite** — transient path) →
100 nF (≤2 mm) + 22 µF ceramic (≤5 mm) + 330 µF polymer (≤15 mm) →
**MAX98357A** → BTL OUT+/OUT− → BLM18PG601SN1 + 1 nF C0G per leg
(footprints fitted, parts per M9) → twisted pair → 8 Ω speaker.
Control: DIN/BCLK/LRC ← GPIO7/15/16 via 33 Ω; SD_MODE ← GPIO17
(internal 100 kΩ pulldown = boots muted; 3.3 V drive lands in the
"Left" window >1.4 V); GAIN ← one 0402 chosen by the §4 SPL bench.

**Parameter card — MAX98357AETE+T** [DS]
- TQFN-16, supply 2.5-5.5 V, filterless class-D BTL, ~300 kHz PWM,
  I2S in 8-96 kHz, efficiency ~92 %, THD+N 0.013 % typ @1 kHz,
  quiescent ~2.4 mA on (why SD_MODE muting between clips is on the
  follow-up list), gain 3/6/9/12/15 dB via one resistor.
- **Output power at 3.3 V into 8 Ω** (derived, the honest way):
  BTL unclipped sine ceiling = VDD²/(2·R) = 3.3²/16 = **0.68 W**;
  pushing into 10 % THD clipping reads **~0.85-0.9 W** off the
  datasheet family curves [DS-graph, confirm at M8]. The "~0.9 W"
  number the owner has is the 10 %-THD figure; use 0.68 W for
  headroom math because clipped watts are distortion, not loudness.

**Is 0.68 W honestly enough? — the SPL arithmetic BOTH ways:**

*Way 1 — loudness ceiling vs the law.* With an 88 dB/W/m driver:
SPL@1m at 0.68 W = 88 + 10·log₁₀(0.68) = 86.3 dB → **92.3 dB at
50 cm** (+6 dB, free field). The EN 71-1 procedure then trims the
GAIN resistor until 0 dBFS ≤ 78 dB @ 50 cm — i.e. the hardware must
**throw away ≥14 dB**. Even an 82 dB/W/m driver still ceilings at
86.3 dB @ 50 cm, 8 dB above the legal cap. **Loudness is never the
constraint — the law is.** The amp has power to spare at 3.3 V.

*Way 2 — crest headroom at the legal operating point.* Narration at
70 dB LpA @ 50 cm = 64 dB @ 1 m average. Electrical average with an
88 dB driver = 10^((64−88)/10) = **4 mW**. Speech crest factor
12-15 dB → instantaneous peaks 63-127 mW. Against the 0.68 W clean
ceiling that is **7.3-10.3 dB of headroom: PASS.** With 86 dB
measured: 6.3 mW avg, peaks ≤200 mW, 5.3-8.3 dB headroom: **still
PASS.** With 84 dB: 3.3-6.3 dB — marginal (this reproduces
power-tree §4's warning; the dossier's "1.9 dB" uses a ~16 dB crest
assumption, the conservative end). With 81 dB (hi-fi micro drivers):
0.3-3.3 dB — clips on crests; that is where a 5 V rail returns.

**Conclusion:** 3V3-only stands with any driver that measures
**≥85-86 dB/W/m in the enclosure**. The ≥88 spec line buys ~3 dB of
enclosure/grille-loss margin, which is legitimate padding — but see
§5 for what it does to part availability.

**Why the MAX98357A wins:** only mainstream part that is I2S-in,
filterless, 2.5-5.5 V (rail-agnostic across both battery options),
with a **resistor-strapped gain ladder** — the EN 71-1 loudness
ceiling becomes one controlled 0402 BOM line that no firmware bug
can exceed.
**Runner-up: MAX98360A** — same family, ~40 % cheaper; loses on the
one number that matters here: **gain steps 1 (fixed per ordering
variant) vs 5 (one resistor)** — the SPL-trim procedure would mean
changing part numbers instead of one passive. An I2C codec
(TLV320DAC3100) stays the upgrade path only if a headphone jack ever
becomes a feature.

## 5. Speaker (LS1) — spec vs market, resolved honestly

**Spec on file:** 50 mm, 8 Ω, ≥1 W at 50 °C, sensitivity ≥86
(≥88 for 3V3-only), Fs 150-400 Hz, published T/S, sealed 50-100 cm³.

**Finding 1 — the ≥88 dB/W/m @ 50 mm requirement selects an empty
set.** Market survey [WEB]:

| Real, orderable driver | Size | Z | Sens. (1 W/1 m) | Fs | Power | T/S published? |
|---|---|---|---|---|---|---|
| Dayton CE50MP-8 (BOM reference) | 50 mm | 8 Ω | **NOT PUBLISHED** — neither product page nor spec sheet carries a number | **400 Hz** | 1 W max | No (Fs only) |
| Visaton K 50 | 50 mm | 8 Ω | 83 dB | 500 Hz | 2 W | No |
| Peerless PLS-50N25AL01-08 | 50 mm | 8 Ω | 81 dB | 161 Hz | 20 W | **Yes, full** |
| Dayton DMA45-8 | 45 mm | 8 Ω | 76.9 dB | 150.7 Hz | 10 W RMS | **Yes, full** |
| PUI AS05008PR-A-R | 50 mm | 8 Ω | 84 dB | 550 Hz | 1 W | No |
| Same Sky (CUI) GF0668 | 66 mm sq | 8 Ω | 91 dB @1 W/**0.5 m** = **85 dB @1 m** | 300 Hz | 3 W nom / 5 W max | Partial (Fs, no Qts/Vas) |

Small-driver physics is the reason: at 50 mm, radiating area limits
efficiency; the 83-86 dB cluster IS the market. Vendors quoting
higher numbers do it at 0.5 m or 0.1 m (a 0.1 m rating flatters by
20 dB) — exactly the "vendor headline vs measurement" trap the
dossier already warns about, now confirmed with data.

**Finding 2 — the reference part fails the spec's own rules.**
CE50MP-8: sensitivity unpublished (violates "buy from a vendor who
publishes"), and Fs = 400 Hz sits AT the spec ceiling — in a
50-100 cm³ sealed box, Fc = Fs·√(1+Vas/Vb) > 400 Hz, so the S4 sim
PASS criterion (F3 ≤ 350 Hz) is **unreachable by construction** with
this driver. A boxed Fc of ~500+ Hz is the "sounds like a phone
speaker" failure the dossier itself names. The spec is internally
inconsistent at its boundary: **Fs ≤ 400 free-air cannot deliver
F3 ≤ 350 sealed.** Tighten to **Fs ≤ 300 Hz free-air** (or honestly
relax the sim gate to F3 ≤ 450 Hz and accept thinner narration).

**Resolution (respec, no 5 V rail needed):**
- Mechanical: widen to **50-70 mm** (the enclosure is not yet tooled
  — this is free today and impossible after tooling).
- Sensitivity gate: **≥85 dB/W/m measured on-axis, in the production
  enclosure, 1-4 kHz average** (§4 Way-2 math shows 85-86 measured
  keeps 3V3-only with ≥5 dB crest headroom). Drop the 88 paper
  number; keep the measurement discipline.
- Fs: ≤300 Hz free-air.
- **Candidates to order for M10 (2-3 units each):**
  1. **Same Sky GF0668** — 66 mm, Fs 300 Hz, 85 dB@1m derived, 3 W:
     the only candidate that meets the revised spec on paper.
     Primary.
  2. **Dayton CE50MP-8** — stays as the 50 mm candidate ONLY if the
     enclosure cannot grow; its sensitivity gets measured, not
     believed, and its 400 Hz Fs is accepted as a voice-band
     compromise. Secondary.
  3. **Peerless PLS-50N25AL01-08** — the fidelity option with full
     published T/S (enclosure actually designable); at 81 dB it
     REQUIRES the 5 V amp rail per §4. Order it anyway as the
     control sample for listening tests.
- The owner decision in `open-questions.md` #2 ("≥88 measured →
  3V3") is superseded by this arithmetic: the honest gate is
  **≥85 measured in-enclosure → 3V3-only; ≤84 → 5 V rail returns.**

## 6. Controls

### Volume pot — Bourns PTV09A-4020F-B103. KEEP.
**Chain:** +3V3 → pot ends (10 kΩ) → wiper → 1 kΩ → GPIO8 (**ADC1**
— ADC2 is dead under Wi-Fi) + 100 nF at the pin. 9 mm rotary,
10 kΩ ±20 %, linear, detented, THT. Digital-gain-only (never in the
BTL output — bridge-tied 300 kHz PWM). Sampling per schematic-spec
§3 (20 Hz, median-of-5, hysteresis, log map).
**Note:** 3V3/10 kΩ = **330 µA standing drain**, ~8 mAh/day —
2nd-largest fixed idle load after the LED. Cheap fix if wanted:
power the pot from a GPIO, drive high only around the 20 Hz sample.
Worth one firmware line on any battery build.

### Buttons ×3. KEEP with one part-number correction.
**Chain (each):** +3V3 → 10 kΩ pull-up → switch node → 1 kΩ series →
GPIO18/21/47; 100 nF at the node (τ = 1 ms < 30 ms firmware
debounce). The 1 kΩ is load-bearing — without it the 100 nF dumps
its charge into the GPIO on every press.
**Correction:** the shopping list says "160 gf (e.g. Omron
B3F-1000)" — **B3F-1000 is the ~100 gf (0.98 N) variant; the 160 gf
(1.57 N) part is B3F-1002** [DS — verify at order]. For ages 4-7,
100 gf actuates too easily in a pocket/backpack; 160 gf was the
right spec, so order **B3F-1002**.

## 7. Storage

**Chain:** +3V3_D → 10 µF + 100 nF **at the socket** → Molex
5031821852 push-pull socket → card; CS/MOSI/SCK/MISO =
GPIO10/11/12/13 (IO_MUX-native FSPI, 33 Ω series), 10 kΩ pull-ups on
CS, CMD/MOSI, DAT0/MISO **and DAT1, DAT2** (floating DAT1/2 can flip
a card out of SPI mode); card-detect → GPIO9.

- **Socket:** Molex 5031821852 (or Hirose DM3D-SF) — push-PULL
  friction type, deliberately NOT push-push (spring eject = the
  mechanism a drop triggers). Internal, behind screws — choking-
  hazard finding at 15×11 mm stands. **KEEP.**
- **Card:** 8 GB industrial/pSLC (SanDisk Industrial / ATP) — the
  power-loss-mid-write argument is real (a toy is switched off
  mid-story as a matter of routine); consumer TLC corrupts, pSLC
  doesn't. The ~$1.9 vs ~$0.8 delta is NOT over-spec. **KEEP.**
- The SD-needs-5V myth stays dead: 3.3 V interface; the bench
  symptom was the WWZMDiB breakout's AMS1117 dropout (§10).
- Rev B eMMC plan unchanged by this audit.

## 8. Status LED — FINDING: the current plan is broken on battery

The BOM carries WS2812B-2020 (D1) + SN74LVC1T45 shifter (U7) + Q2
rail-gate FET, with "3 discrete LEDs on a 3V3-only build" as the
alternate. The audit closes the alternative:

- **WS2812B VDD minimum is 3.5 V** [DS]. On Option A there is no
  5 V rail at all (the whole point of §4/§5 is single-3V3), and raw
  pack voltage runs 4.5→3.0 V — below spec for most of the discharge
  curve. On Option B raw cell is 4.2→3.0 V — same violation. The
  shifter fixes VIH, not VDD; **there is no valid rail for a WS2812B
  in either battery build.**
- It also draws ~0.7 mA while dark (why Q2 exists at all).

**Replace with 3 discrete 0603 LEDs** (the states the toy actually
signals: listening / thinking / error — the firmware LED vocabulary
is already deliberately small): GPIO48 + 2 spare-pad GPIOs, resistor
per LED sized from the diode's VF-vs-IF curve at **2-3 mA** (not the
20 mA headline). Green VF ~2.0 V → (3.3−2.0)/2.5 mA ≈ **560 Ω**;
blue/white VF ~2.8 V → (3.3−2.8)/2.5 mA ≈ **200 Ω**; confirm
per-LED datasheet curve. **Deletes U7, Q2, and D1's dark current**
(−$0.31 @1000, −0.7 mA idle). USB-powered/dev builds may keep the
WS2812 footprint as DNP.

## 9. Decoupling / passives (spot-check of derived values — all KEEP)

- Amp bulk 330 µF polymer: C = I·Δt/ΔV = 0.625 A × 50 µs / 0.1 V =
  313 µF — arithmetic checks; polymer (not ceramic) because a
  "100 µF" 0805 X5R is ~40 µF at 3.3 V DC bias. S3 sim still gates
  the final value.
- Module 22 µF + 100 nF at pin 2, EN 10 kΩ + 1 µF: Espressif Fig. 7
  — unchanged, required.
- Mic RC 10 Ω + 10 µF: fc ≈ 1.6 kHz against rail hash, costs
  1.6 mA × 10 Ω = 16 mV — checks.
- Battery divider 1.0 MΩ/820 kΩ + 100 nF at tap: full-scale tap
  2.03 V @ 4.5 V — inside ADC1 range; 2.3 µA burn; source impedance
  is high but the 100 nF reservoir + slow sampling makes it valid.
  M5 still owns accuracy.
- USB-C CC: 5.1 kΩ ×2, one each, never shared — correct per spec.

## 10. Bench modules on the desk today

| Module | Verdict | Why |
|---|---|---|
| ESP32-S3 devkit (USB-C DevKitC-1 clone) | **Fine for bench, never production** | Known quirks documented: 5VIN/J1-21 is input-only (no USB 5 V on headers); MAIN button still on GPIO0 on the bench — already ruled a production blocker (strapping pin), production = GPIO18. MCU itself is the parallel agent's scope |
| INMP441 breakout | **Fine for bench; part must be replaced for production** | The IC is discontinued (§3); breakouts remain purchasable from module stock — keep buying spares for bench work only. Hard-wire L/R to GND exactly as the production rule says; the breakout's floating-L/R failure mode is the documented "noise that looks like working capture" |
| MAX98357A breakout (Adafruit-style) | **Fine for bench; the IC itself carries into production** | Same silicon as U2. The breakout is NOT the production circuit: it lacks the 330 µF bulk (typ. only ~10 µF onboard) and straps GAIN/SD_MODE its own way — do not copy its passives into layout; §4's chain governs |
| WWZMDiB microSD breakout (AMS1117 + level shifter) | **Bench-only with the 5 V feed workaround; MUST NOT carry into production** | Its own AMS1117 (1.1-1.3 V dropout) browns out from 3.3 V — the origin of the SD-5V myth. Production: socket directly on 3V3, 10 µF + 100 nF at socket, no regulator, no shifter |
| Bare 8 Ω speaker | **Fine for bench; production driver is a specified purchase** | No sensitivity/Fs data, no enclosure. Production per §5 respec + M8/M10 |

## 11. Over-specified / under-specified — the money-and-failure list

**Under-specified (will fail):**
1. **PTC 1206L050** — holds ~0.35 A at 60 °C vs a 0.5 A legitimate
   charge draw → nuisance trips. **1206L075.** (§2)
2. **WS2812B on any battery build** — VDD min 3.5 V, no valid rail.
   **3 discrete LEDs.** (§8)
3. **Mic fallback SPH0645LM4H-B** — obsolete; the fallback plan
   itself was dead stock. **IM69D130 path.** (§3)
4. **Speaker spec ≥88 dB @ 50 mm** — empty set; and CE50MP-8's
   Fs = 400 Hz makes the S4 box target unreachable. **Respec §5.**
5. **B3F-1000 vs the 160 gf requirement** — order B3F-1002. (§6)

**Over-specified (money back):**
1. **U7 SN74LVC1T45 + Q2 DMG3415U + WS2812B** — all three deleted by
   the discrete-LED baseline: −$0.31/unit @1000 and one less rail
   gate to route.
2. Nothing else. The parts that look gold-plated survive scrutiny:
   the 330 µF polymer is derived (§9), pSLC is a power-loss
   requirement (§7), the dual VBUS clamps do different jobs (§2),
   4-layer ENIG is the RF/audio floor, and the 33 Ω series packs
   are $0.11 of respin insurance (M9 decides if they're fitted).

**Explicitly verified as right-sized:** TPS63802 at 2 A (coincident
worst case 0.9-1.0 A × margin), 0.68 W amp against the 78 dB legal
ceiling (14 dB to spare), 8 GB card (~13-story ceiling killed SPI-NOR,
8 GB ≈ hundreds of stories + game/voice clip growth).

## 12. Dossier updates made in this same pass

- `open-questions.md` — sourcing section rewritten with the verified
  lifecycle facts (PCN-000772 dates, SPH0645 obsolete, IM69D130
  recommendation); owner decision #2 amended with the ≥85-measured
  gate and the 50-70 mm widening.
- `bom.md` — U3 note, LED lines, PTC note in the Li-ion delta.
- `shopping-list-one-toy.md` — mic fallback line, PTC part number,
  B3F-1002 correction.

**What only the lab still settles (unchanged M-gates):** M8 SPL trim,
M10 enclosure listening, M1/M2 rail sag + real currents, M3/M4
charge thermals/NTC, S3/S4 sims. Nothing in this audit substitutes
for them; it narrows what gets bought before they run.

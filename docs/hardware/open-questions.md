# Areg hardware — open questions and required measurements

What the 2026-08-07 review could NOT settle from datasheets. Each
item names who closes it and what a PASS looks like.

## Owner decisions

1. **Battery chemistry for run 1** — both options are COMPLETE
   designs in `power-tree.md` (both need the same TPS63802
   buck-boost; the chemistry changes protection + charging, never
   the regulator). Trade: AA = zero certification/lead-time, parent
   pays ~€150-300/yr in cells; Li-ion = +$8.65 BOM + €6-10k
   one-time + weeks, zero recurring. Split recommendation on file:
   AA run 1, Li-ion run 2 — but it is a business call.
2. **Speaker sensitivity ↔ rail count** — AMENDED by the 2026-08-08
   component audit (`audit-components.md` §5): ≥88 dB/W/m at 50 mm
   is an empty set on the market (survey on file: real 50 mm drivers
   cluster 77-85 dB/W/m), and the crest-headroom math shows 3V3-only
   survives at **≥85 dB measured in-enclosure**. Revised gate:
   widen the driver to 50-70 mm, Fs ≤300 Hz free-air; ≥85 dB/W/m
   MEASURED in the enclosure → single 3V3 rail; ≤84 → 5 V rail
   returns. Candidates to order for M10: Same Sky GF0668 (primary),
   Dayton CE50MP-8 (50 mm only, sensitivity unpublished — measure),
   Peerless PLS-50N25AL01-08 (fidelity control; 81 dB ⇒ 5 V).
   This still gates the schematic.
3. **EU or not for run 1** — €500-1,500 written pre-assessment from
   a notified body (TÜV/SGS/BV/Intertek) that does BOTH toys and
   RED, asked in writing: (a) does the AI Act's Annex-I toy hook
   make this high-risk? (b) EN 71-1 sound category: voice-toy (80
   dB) or close-to-ear (60-70 dB)? Highest-leverage spend in the
   whole plan; gates enclosure tooling.

## Lab / measurement (cannot be simulated away)

| # | Measurement | PASS |
|---|---|---|
| M1 | Rail sag at the MODULE 3V3 pin during Wi-Fi TX + loud passage + SD write (scope, 20 MHz BW, spring ground) | ≥3.10 V; <100 mVpp ripple; zero brownout resets (reset_reason 9) in 30 min. bootDiag's `rst` field is the field-side canary |
| M2 | Real per-state current (PPK2/Joulescope at the cell, ≥100 kHz): idle / playback / voice turn / OTA / full sync | within ±25 % of power-tree.md §3 — this number sizes the cell; everything else is arithmetic on assumptions |
| M3 | Charge thermals, ENCLOSURE CLOSED, 500 mA and 1 A, 25/35 °C ambient | cell surface <45 °C; touch surface <~48 °C (exact toy-standard limit: ask the lab — deliberately not quoted from memory) |
| M4 | NTC window: assembled toy at −5 °C and +50 °C, plug in | charging DOES NOT START either way, resumes in range. Sign off on the test, never the resistor math |
| M5 | ADC battery-sense accuracy across 3.0-4.2 V on five units | ±30 mV after curve-fit + per-unit offset; worse than ±50 mV → MAX17048 |
| M6 | Runtime: scripted modelled day to cutoff | ≥1.5 days as built; ≥7 days after the firmware sleep work |
| M7 | Drop: 10× 850 mm onto steel, battery installed | no pouch deformation, charges normally |
| M8 | SPL: assembled toy, class-2 SLM, 50 cm, per EN 71-1 method → then the gain-resistor procedure (schematic-spec §4) | 0 dBFS ≤78 dB LpA; then accredited-lab confirmation |
| M9 | EMC pre-scan 30 MHz-1 GHz, with/without the DNP-able ferrites and series resistors | decides what actually ships fitted; the item that most often forces a late respin |
| M10 | Acoustic prototype: real speaker + grille + gasket in 3-5 printed enclosures, approved narration, real bedroom | the ear test. A grille fine on a flat panel can be −6 dB and boxy on a resonant box |

## Simulations to run before layout (inputs specified, no sim run yet)

| # | Sim | PASS |
|---|---|---|
| S1 | Buck-boost transient (vendor SPICE model): 0.1→0.9 A in 1 µs, 2 ms hold, 100 Hz repeat | ≥3.05 V at module pin, ≥2.80 V at SD socket, settle <200 µs, release overshoot ≤3.55 V |
| S2 | Inrush at plug-in: 5 V/0.3 Ω source, 1 ms rise, TPS22918 C_CT sweep, 350 µF total | peak input ≤500 mA, monotonic 3V3 rise (expect ~5 ms) |
| S3 | Amp-branch decoupling impedance vs frequency, DC-bias-derated ceramic models | \|Z\| <100 mΩ from 1 kHz to 1 MHz — the sim that proves 330 µF is enough BEFORE the board exists |
| S4 | Sealed-box response (VituixCAD/Hornresp) with the measured T/S, volumes 30/50/80/120 cm³ | Qtc 0.6-0.9 and F3 ≤350 Hz |
| S5 | Charger thermal (hand θ_JA is acceptable): 1.5 W worst case in the sealed shell | junction <100 °C at 35 °C ambient |

## Sourcing / lifecycle

- **Microphone (RESOLVED as research, 2026-08-08 audit — see
  `audit-components.md` §3).** Verified: INMP441 discontinued;
  TDK PCN-000772 (2026-01-15) EOLs ICS-43434 AND T3902 — **LTB
  2026-06-15 has PASSED**, LTS 2027-01-15 (distributor shelf stock
  only); the previously-named fallback **Knowles SPH0645LM4H-B is
  itself obsolete** (Knowles→Syntiant line pruning); CUI
  CMM-4030D-261-I2S discontinued. The whole Western I2S-mic
  category is exiting. Plan: run 1 = INMP441/ICS-43434 remaining
  stock (buy at BOM-freeze); production rev A = **Infineon
  IM69D130, PDM** (active, SNR 69 dBA, AOP 130 dB SPL — both
  better) via ESP32-S3 I2S0 PDM-RX — a firmware capture-path slice
  with its own bench session, decided BEFORE footprint freeze.
  Last-resort I2S drop-in: MSM261S4030H0 (in production, but
  57 dBA SNR = −4 dB vs INMP441).
- Espressif longevity/PCN pages for the exact WROOM-1 SKU before
  tooling; module allocation lead time 2-6 weeks, occasionally worse.
- EN 71-1:2026 edition exists — confirm clause 4.20 unchanged vs the
  2011+A3 numbers quoted here.

## Standing firmware follow-ups with hardware roots

- WiFi.setSleep(WIFI_PS_MIN_MODEM): one line, ~2× idle battery
  (task #29 — do NOT ship in the same release as an OTA).
- Light-sleep idle: 10-20×, real slice, same OTA caution.
- AMP SD_MODE mute between clips (hiss + 2.4 mA) once GPIO17 exists.
- Content sync with the amp muted (removes the worst-case current
  coincidence).
- Battery telemetry on the heartbeat (additive fields — same shape
  firmwareVersion already uses) + dashboard badge.

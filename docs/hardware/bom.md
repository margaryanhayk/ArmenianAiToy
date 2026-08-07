# Areg electronics BOM (rev A draft)

Distributor list prices at review time (LCSC/JLCPCB tier where
available, Digi-Key/Mouser otherwise). **Treat totals as ±25 %** —
real 100-unit assembler pricing and real 1000-unit quotes both
differ. Battery-option deltas at the end.

| Ref | Part | Package | Qty | @100 | @1000 |
|---|---|---|---|---:|---:|
| U1 | ESP32-S3-WROOM-1-**N16R8** (PCB antenna) | module | 1 | $4.30 | $3.60 |
| U2 | MAX98357AETE+T (class-D I2S amp) | TQFN-16 | 1 | $2.20 | $1.45 |
| U3 | INMP441ACEZ ⚠ EOL risk — see open-questions | LGA | 1 | $2.40 | $1.80 |
| U4 | TPS63802DLAR buck-boost 3V3/2A | — | 1 | $1.60 | $1.35 |
| U5 | USBLC6-2SC6 (USB ESD) | SOT-23-6 | 1 | $0.15 | $0.09 |
| U6 | TPS22918DBVR (soft-start switch) | SOT-23-6 | 1 | $0.35 | $0.22 |
| U7 | SN74LVC1T45DBVR (LED level shifter; DNP on 3V3-only build) | SOT-23-6 | 1 | $0.18 | $0.11 |
| U8 | ESDALC6V1-5M6 (SD ESD; DNP if slot internal) | SOT-23-6 | 1 | $0.16 | $0.10 |
| Q1 | DMG2301L reverse-block P-FET | SOT-23 | 1 | $0.09 | $0.05 |
| Q2 | DMG3415U LED-rail gate (battery builds) | SOT-23 | 1 | $0.08 | $0.05 |
| D1 | WS2812B-2020 (or 3× discrete LED on 3V3-only) | 2020 | 1 | $0.12 | $0.06 |
| L1 | 2.2 µH 2 A shielded | 0630 | 1 | $0.15 | $0.08 |
| C1 | 330 µF 6.3 V low-ESR polymer (amp bulk) | D-case | 1 | $0.42 | $0.26 |
| C2-C6 | 22/10 µF X5R | 0603 | 5 | $0.20 | $0.10 |
| C7-C20 | 100 nF X7R | 0402 | 14 | $0.09 | $0.04 |
| FB1-2 | BLM18PG601SN1 ferrite (speaker; DNP-able) | 0603 | 2 | $0.05 | $0.02 |
| R* | 0402 1 % (pulls, series, straps, divider) | 0402 | ~32 | $0.22 | $0.11 |
| RV1 | Bourns PTV09A-4020F-B103 10 kΩ lin detented | THT | 1 | $0.75 | $0.52 |
| SW1-3 | 6 mm tactile 160 gf (rev B: silicone keypad + carbon domes, ~$0.40-1.20 + $300-800 tooling) | THT | 3 | $0.36 | $0.18 |
| J1 | USB-C receptacle 16-pin (charging builds) | SMD | 1 | $0.30 | $0.16 |
| J2 | Molex 5031821852 microSD **push-pull** | SMD | 1 | $0.85 | $0.55 |
| — | microSD 8 GB **industrial/pSLC** | — | 1 | $1.90 | $1.60 |
| LS1 | Speaker 50 mm 8 Ω ≥1 W@50 °C, ≥86 (target 88) dB/W/m, Fs≤400 Hz, published T/S | — | 1 | $1.20 | $0.75 |
| — | Speaker + mic gaskets | — | 2 | $0.10 | $0.05 |
| PCB | 4-layer 60×50 mm ENIG | — | 1 | $2.50 | $0.90 |
| — | SMT assembly + AOI + test | — | 1 | $6.00 | $2.50 |
| | **Electronics subtotal** | | | **≈ $26.6** | **≈ $16.7** |

## Battery-option deltas

| Option | Adds | Δ @1000 | One-time |
|---|---|---|---|
| A: 3×AA | holder + contacts + screwed door | +$0.40 | — |
| B: Li-ion | 803860 2000 mAh pouch w/ PCM + reports $4.20, BQ24074 $1.60, MAX17048 fuel gauge $1.40, NTC $0.15, USB-C protection (TPD4S014+PTC) $0.70, pack bay/hatch $0.60 | **+$8.65** | UN 38.3 + IEC 62133-2 + EN IEC 62115 ≈ €6-10k, 4-6 weeks |

## Whole-product context (manufacturing lens)

| Scale | Landed cost/unit (electronics + enclosure + assembly + test + packaging + scrap) |
|---|---|
| 50 | ≈ $78 (enclosure printed; certification NOT amortized here) |
| 500 | ≈ $36 (soft tooling) |
| 5000 | ≈ $17 (injection tooling $5-12k separate) |

Cost concentrates: at 50 units → enclosure + NRE; at 500 → tooling +
labour; at 5000 → materials (module+storage+speaker+amp ≈ 55 % of
materials). The invisible line: recurring per-toy CLOUD cost — the
offline SD story cache is the most important cost control in the
product.

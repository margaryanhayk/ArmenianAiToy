# Shopping list — ONE toy (rev A, AA-battery build)

Matches the full schematic (`schematic/areg-schematic-pro.svg`).
Quantities are for one toy plus nothing spare — order ×1.5 for a
prototype run because you WILL burn parts.

## Main components

| Qty | Buy exactly | Purpose |
|---|---|---|
| 1 | **ESP32-S3-WROOM-1-N16R8** module (PCB antenna) | the brain (U1) |
| 1 | **MAX98357AETE+T** | audio amplifier (U2) |
| 1 | **INMP441** MEMS microphone (verify stock — if EOL: **Knowles SPH0645LM4H-B**) | microphone (U3) |
| 1 | **TPS63802DLAR** | 3.3 V buck-boost regulator (U4) |
| 1 | **TPS22918DBVR** | soft-start switch (U6) |
| 1 | **USBLC6-2SC6** | USB ESD protection (U5a) |
| 1 | **TPD4S014DSQR** | USB-C VBUS/CC protection (U5b) |
| 1 | **SN74LVC1T45DBVR** | LED level shifter (U8; skip on 3V3-only LED) |
| 1 | **DMG2301L** P-FET | reverse-battery block (Q1) |
| 1 | **WS2812B-2020** | status LED (D1) |
| 1 | **SMAJ5.0A** TVS diode | USB surge clamp (D60) |
| 1 | **Littelfuse 1206L050/15YR** PTC | USB fuse (F1) |
| 1 | 2.2 µH 2 A shielded inductor, 0630 (e.g. **TDK VLS6045EX-2R2M**) | for U4 (L1) |

## Electro-mechanics

| Qty | Buy exactly | Purpose |
|---|---|---|
| 1 | Speaker **50 mm, 8 Ω, ≥1 W, sensitivity ≥88 dB/W/m** with published Fs ≤400 Hz (reference: **Dayton Audio CE50MP-8**... verify sensitivity!) | LS1 |
| 1 | **Bourns PTV09A-4020F-B103** (10 kΩ linear, DETENTED) + a plastic knob | volume (RV1) |
| 3 | 6 mm tactile switches, ~160 gf (e.g. **Omron B3F-1000**) | main / green / red buttons |
| 1 | **Molex 5031821852** microSD socket (push-PULL) | J2 |
| 1 | microSD card **8 GB industrial/pSLC** (e.g. SanDisk Industrial / ATP) | content storage |
| 1 | USB-C receptacle, 16-pin SMD (e.g. **GCT USB4105-GF-A**) | J1 |
| 1 | **3×AA battery holder** with leads + screws | BT1 |
| 2 | Closed-cell foam / silicone gasket rings (speaker + mic) | acoustics |

## Passives (one strip of each — they cost cents)

| Qty | Value / type | Where |
|---|---|---|
| 2 | 5.1 kΩ 1 % 0402 | R60, R61 — USB-C CC (one EACH, never shared) |
| 10 | 10 kΩ 1 % 0402 | EN, strapping pulls, button pull-ups, SD pull-ups (R1–R5, R44–R48…) |
| 4 | 1 kΩ 0402 | button/pot series (protection into GPIO) |
| 7 | 33 Ω 0402 | I2S + SD series (R21-23, R40-43) |
| 1 | 10 Ω 0402 | mic supply filter (R20) |
| 1 | GAIN resistor — value decided by the SPL bench test (100 kΩ or direct link) | R30 loudness ceiling |
| 2 | Ferrite bead 600 Ω @100 MHz 0603 (**BLM18PG601SN1**) | FB1, FB2 speaker |
| 1 | 330 µF 6.3 V low-ESR **polymer** (D-case) | C32 amp bulk |
| 1 | 100 µF ceramic/polymer | C11 buck output |
| 4 | 22 µF X5R 0603 | C1, C31… |
| 4 | 10 µF X5R 0603 | C10, C20, C40… |
| 15 | 100 nF X7R 0402 | decoupling everywhere |
| 3 | 1 µF X5R 0402 | EN RC, misc |

## For the custom PCB build only

| Qty | Item |
|---|---|
| 1 | 4-layer PCB ~60×50 mm, ENIG finish (JLCPCB/PCBWay, min order is usually 5 boards) |

## NOT needed (myths killed during review)

- ~~AMS1117 regulator~~ — the buck-boost replaces every LDO
- ~~5 V rail / boost converter~~ — gone if the speaker is ≥88 dB/W/m
- ~~SD level-shifter module~~ — SD runs natively on 3.3 V
- ~~coin cell~~ — none anywhere, by design (US toy-safety rule)

## If Li-ion instead of AA (batch-2 option) — ADD:

| Qty | Buy exactly |
|---|---|
| 1 | **803860 LiPo pouch 2000 mAh** WITH protection PCM and IEC 62133-2 + UN 38.3 reports |
| 1 | **BQ24074RGTR** charger with power-path |
| 1 | **MAX17048G+T10** fuel gauge |
| 1 | **NCP15XH103F03RC** 10 kΩ NTC (bonded to the cell face) |
| 1 | JST-ZH keyed connector pair |

## Note for the CURRENT bench toy (devkit)

The devkit build needs only: 2 push buttons + wires to GPIO21/47 and
GND (internal pull-ups — no resistors), and later the volume pot to
GPIO8. Everything else already exists on the bench modules.

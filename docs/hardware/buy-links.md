# Buy links — one-toy shopping list (researched 2026-08-08)

Sourcing pass over `shopping-list-one-toy.md` / `bom.md`. One "best" link
per part; prices/stock are what was actually seen on the linked page or
its distributor listing on the research date — they will drift. Channels
favored: LCSC (cheap, ships to Armenia), Mouser/TME (authorized), AliExpress
for mechanical bits.

## 🚨 RED FLAGS (act before ordering)

1. **INMP441 (U3) — DEAD. EOL since 2018.** TDK InvenSense issued the EOL
   PCN in March 2018; last-time-buy closed September 2018. Every "in-stock"
   bare chip today is old stock or a clone. AliExpress breakout modules are
   fine for the bench, NOT for production.
2. **Knowles SPH0645LM4H-B (the shopping list's named fallback) — ALSO
   OBSOLETE.** DigiKey lists it as no longer manufactured (Knowles' consumer
   MEMS mic line went to Syntiant). The BOM's plan-B is dead too.
3. **TDK ICS-43434 (the natural successor) — IN LAST-TIME-BUY.** PCN-000772
   (2026-01-15); DigiKey's stated LTB date was 2026-06-15 and has passed,
   but DigiKey still showed ~113,041 pcs in stock — buyable today, no
   long-term supply. **Action needed: pick the mic strategy now** — either
   a lifetime buy of ICS-43434 from remaining stock, or switch the design
   to the active Chinese part **MEMSensing MSM261S4030H0R** (I2S, on
   LCSC/JLCPCB, C2840615). There is effectively no Western I2S MEMS mic in
   production anymore.
4. **Dayton Audio CE50MP-8 (LS1) — sensitivity is NOT PUBLISHED anywhere**
   (checked Dayton's product page, the spec-sheet link, Parts Express, and
   SoundImports' full T/S listing). It cannot satisfy "published
   ≥88 dB/W/m". Worse: **no 45–57 mm 8 Ω driver we could find publishes
   ≥88 dB/W/m together with Fs ≤400 Hz and ≥1 W** — the spec combination
   looks physically self-contradictory at this cone size. Closest published
   options:
   - **Visaton K 57 C – 8 Ω** — **87 dB @ 1W/1m** (published), 57 mm, 2 W
     rated / 3 W max, Fs 550 Hz. Best-documented near-miss, well stocked.
   - **PUI Audio AS07708PS-7-R** — **90 dB(A)**, 8 Ω, 4 W, **Fs 250 Hz** —
     meets sensitivity, Fs and power, but is **77 mm** (needs a bigger
     cutout). Soberton WSP-5708 (57 mm, 8 Ω, 3 W, active) publishes
     "100 dB" but without a stated measurement condition — not comparable.
   - Recommendation: re-run the SPL budget at 87 dB (K 57 C) or grow the
     enclosure cutout to 77 mm (PUI). The 5V-rail-not-needed conclusion in
     the shopping list depends on this number.
5. **Molex 5031821852 microSD socket (J2) — DISCONTINUED.** DigiKey: "no
   longer manufactured / stocked once depleted." JLCPCB still lists it
   (C587953) from remaining stock. Active alternatives: **Hirose
   DM3AT-SF-PEJM5** (push-push, huge multi-distributor stock) or Hirose
   **DM3BT-DSF-PEJS** (push-pull — verify before committing footprint), or
   GCT MEM2075-00-140-01-A.
6. **TDK VLS6045EX-2R2M (L1) — part number does not exist.** The real TDK
   part is **VLS6045EX-2R2N** (2.2 µH ±30 %, 5.1 A, 6×6×4.5 mm) or
   **VLS6045EX-2R2N-H**. Same footprint/spec intent; fix the BOM PN.
7. **Bourns PTV09A-4020F-B103 (RV1) — two problems.** (a) DigiKey shows it
   **out of stock / backorder** (lifecycle still Production; RS/other
   channels have it). (b) Per Bourns' PTV09 nomenclature the second digit
   "0" in "40**2**0F" vs "4**0**20F" matters: **-4020F- is the NO-detent
   version; the center-detent part is PTV09A-4220F-B103.** The shopping
   list says "DETENTED" — for a volume knob you almost certainly want the
   plain (no-detent) 4020 anyway; decide, then fix either the note or the PN.
8. Minor: **Omron B3F-1000 is a 100 gf (0.98 N) switch**, not ~160 gf as the
   line item says. The 160 gf part in the same family is **B3F-1020**.
9. Minor: SanDisk industrial 8 GB microSD (SDSDQAF3-008G-I) street price is
   ~$35 single-unit — far above the BOM's $1.90 placeholder. ATP's
   equivalent runs cheaper via Mouser; get a real quote.

## Main components

| Ref | Part | Best link | Price seen | Stock seen | Lifecycle | Notes/alternates |
|---|---|---|---|---|---|---|
| U1 | ESP32-S3-WROOM-1-N16R8 | [LCSC C2913202](https://www.lcsc.com/product-detail/C2913202.html) | ~$3.46–3.78 | In stock (LCSC) | Active | Mouser carries the family too |
| U2 | MAX98357AETE+T | [Mouser](https://www.mouser.com/ProductDetail/Analog-Devices-Maxim-Integrated/MAX98357AETE+T?qs=AAveGqk956HhNpoJjF5x2g%3D%3D) | $2.86 (DigiKey cut tape, qty 1: $3.73) | Mouser 5,771 | **Production** (long lead time flagged; 14-wk factory) | Also [LCSC C910544](https://www.lcsc.com/product-detail/Audio-Power-OpAmps_Maxim-Integrated-MAX98357AETE-T_C910544.html) |
| U3 | INMP441 | — | — | old stock/clones only | **EOL 2018** | See RED FLAG 1–3. Interim: [ICS-43434 @ DigiKey](https://www.digikey.com/en/products/detail/tdk-invensense/ICS-43434/6140298) (~113k pcs, last-time-buy); long-term: [MSM261S4030H0R @ LCSC](https://www.lcsc.com/product-detail/C2840615.html) |
| U4 | TPS63802DLAR | [Mouser](https://eu.mouser.com/ProductDetail/Texas-Instruments/TPS63802DLAR?qs=9r4v7xj2Lnmr2ylyTiX9Mg%3D%3D) | €3.23 (qty 1, Mouser EU) | In stock | Active | Also on TI.com direct |
| U6 | TPS22918DBVR | [LCSC C131941](https://www.lcsc.com/product-detail/C131941.html) | $0.0815 (LCSC bulk); $0.51 Mouser qty 1 | LCSC 11,020 | Active | |
| U5a | USBLC6-2SC6 | [LCSC C7519](https://www.lcsc.com/product-detail/C7519.html) | from $0.0896 | In stock | Active | ST-brand; cheaper clones exist on LCSC |
| U5b | TPD4S014DSQR | [Mouser](https://www.mouser.com/ProductDetail/Texas-Instruments/TPD4S014DSQR?qs=L4ss%2FyqpMWT8UftxLwIb4g%3D%3D) | ~$0.68+ (distributor range) | Mouser 28,700 | Active | |
| U8 | SN74LVC1T45DBVR | [LCSC C7843](https://lcsc.com/product-detail/74-Series_TI_SN74LVC1T45DBVR_SN74LVC1T45DBVR_C7843.html) | from $0.0355 | In stock | Active | DNP on 3V3-only LED build |
| Q1 | DMG2301L-7 | [LCSC C102619](https://www.lcsc.com/product-detail/MOSFET_Diodes-Incorporated-DMG2301L-7_Diodes-Incorporated-DMG2301L-7_C102619.html) | $0.0412–0.0694 | In stock | Active | |
| D1 | WS2812B-2020 | [LCSC C965555](https://www.lcsc.com/product-detail/C965555.html) | $0.055–0.092 | 35,115 | Active | |
| D60 | SMAJ5.0A (Littelfuse) | [LCSC C83329](https://lcsc.com/product-detail/esd-and-surge-protection-tvs-esd_littelfuse-smaj5-0a_C83329.html) | from $0.046 | In stock | Active | Cheaper non-brand SMAJ5.0A from $0.017 on LCSC |
| F1 | Littelfuse 1206L050/15YR | [Mouser](https://www.mouser.co.za/ProductDetail/Littelfuse/1206L050-15YR?qs=PWhpLWeW8wdxWp9etyyAeA%3D%3D) | — | Mouser 41,805 | Active/Production | Also [LCSC C151162](https://www.lcsc.com/product-detail/Surface-Mount-Fuses_Littelfuse_1206L050-15YR_500mA-15V-Self-recovery_C151162.html) |
| L1 | ~~VLS6045EX-2R2M~~ → **VLS6045EX-2R2N** | [TME](https://www.tme.com/us/en-us/details/vls6045ex-2r2n/inductors/tdk/) | — | In stock (TME/DigiKey/Newark) | Active | **BOM PN wrong — "M" suffix doesn't exist** (RED FLAG 6). 2.2 µH, 5.1 A, 6×6×4.5 mm |

## Electro-mechanics

| Ref | Part | Best link | Price seen | Stock seen | Lifecycle | Notes/alternates |
|---|---|---|---|---|---|---|
| LS1 | Dayton CE50MP-8 (reference) | [SoundImports](https://www.soundimports.eu/en/dayton-audio-ce50mp-8.html) | €3.95 ($1.99 MSRP) | 5 in stock (SoundImports) | Active | **Sensitivity NOT published — fails spec** (RED FLAG 4). Alternates: [Visaton K 57 C – 8 Ω](https://www.visaton.de/en/products/drivers/fullrange-systems/k-57-c-8-ohm) 87 dB@1W/1m, Fs 550 Hz; [PUI AS07708PS-7-R](https://puiaudio.com/product/speakers-and-receivers/AS07708PS-7-R) 90 dB(A), Fs 250 Hz, 77 mm |
| RV1 | Bourns PTV09A-4020F-B103 | [DigiKey](https://www.digikey.com/en/products/detail/bourns-inc/PTV09A-4020F-B103/3534181) | — | **DigiKey 0 (backorder)**; RS has stock | Production | See RED FLAG 7 (detent PN mixup). Knob: any 6 mm D-shaft knob on AliExpress |
| SW1-3 | Omron B3F-1000 | [LCSC C93157](https://www.lcsc.com/product-detail/Tactile-Switches_OMRON_B3F-1000_B3F-1000_C93157.html) | from $0.0747 | In stock | Active | **100 gf, not 160 gf** — use B3F-1020 for 160 gf (RED FLAG 8) |
| J2 | Molex 5031821852 | [JLCPCB C587953](https://jlcpcb.com/partdetail/MOLEX-5031821852/C587953) (residual stock) | — | residual only | **DISCONTINUED** | Alternative (active): [Hirose DM3AT-SF-PEJM5 @ DigiKey](https://www.digikey.com/en/products/detail/hirose-electric-co-ltd/DM3AT-SF-PEJM5/2533565); push-pull option DM3BT-DSF-PEJS — verify (RED FLAG 5) |
| — | microSD 8 GB industrial | [SanDisk SDSDQAF3-008G-I @ Mouser](https://www.mouser.com/ProductDetail/SanDisk/SDSDQAF3-008G-I?qs=1mbolxNpo8dZV83dHCEirA%3D%3D) | ~$35 street (bulkmemorycards) | In stock | Active | Far above BOM's $1.90 placeholder (RED FLAG 9); price ATP AF8GUD3A via Mouser as alternative |
| J1 | GCT USB4105-GF-A | [TME](https://www.tme.com/us/en-us/details/usb4105-gf-a/usb-ieee1394-connectors/gct/) | $0.49–0.75 (TME tiers) | **TME 36,582** | Active | Also Mouser/DigiKey/Future (4,800) |
| BT1 | 3×AA holder w/ leads | [AliExpress search](https://www.aliexpress.com/w/wholesale-3xAA-battery-holder-with-leads-cover-switch.html) | ~$1–2 | plentiful | commodity | Pick one with screw lugs + covered case |
| — | Speaker + mic gasket rings | [AliExpress search](https://www.aliexpress.com/w/wholesale-speaker-foam-gasket-ring-50mm.html) | cents | plentiful | commodity | Closed-cell foam / silicone, cut-to-size also fine |

## Passives (one link per value)

| Value / type | Best link | Price seen | Notes |
|---|---|---|---|
| 5.1 kΩ 1 % 0402 | [LCSC C25905](https://www.lcsc.com/product-detail/C25905.html) | <$0.01 | UNI-ROYAL 0402WGF5101TCE (JLC basic) |
| 10 kΩ 1 % 0402 | [LCSC C25744](https://www.lcsc.com/product-detail/C25744.html) | <$0.01 | 0402WGF1002TCE |
| 1 kΩ 0402 | [LCSC C11702](https://www.lcsc.com/product-detail/C11702.html) | <$0.01 | 0402WGF1001TCE |
| 33 Ω 0402 | [LCSC search](https://www.lcsc.com/search?q=0402%2033ohm%201%25) | <$0.01 | any 1 % strip |
| 10 Ω 0402 | [LCSC search](https://www.lcsc.com/search?q=0402%2010ohm%201%25) | <$0.01 | any 1 % strip |
| 100 kΩ 0402 (GAIN option) | [LCSC C25741](https://www.lcsc.com/product-detail/C25741.html) | <$0.01 | 0402WGF1003TCE; value pending SPL bench test |
| Ferrite BLM18PG601SN1D 0603 | [LCSC C1017](https://www.lcsc.com/product-detail/C1017.html) | ~$0.01 | Murata, JLC basic |
| 330 µF 6.3 V polymer D-case | [Panasonic 6TPE330ML @ LCSC C79113](https://lcsc.com/product-detail/Tantalum-Capacitors_PANASONIC_6TPE330ML_330uF-337-20-6-3V_C79113.html) | from $0.53 | In stock; 6TPE330MAP (C79112) from $0.35 |
| 100 µF (buck output) | [LCSC search](https://www.lcsc.com/search?q=100uF%206.3V%201206) | ~$0.05 | ceramic 1206/1210 or small polymer |
| 22 µF X5R 0603 | [LCSC search](https://www.lcsc.com/search?q=22uF%200603%20X5R) | ~$0.01 | |
| 10 µF X5R 0603 | [LCSC C19702](https://www.lcsc.com/product-detail/C19702.html) | ~$0.01 | Samsung CL10A106KP8NNNC |
| 100 nF X7R 0402 | [LCSC C1525](https://www.lcsc.com/product-detail/C1525.html) | <$0.01 | Samsung CL05B104KO5NNNC (JLC basic) |
| 1 µF X5R 0402 | [LCSC C52923](https://www.lcsc.com/product-detail/C52923.html) | <$0.01 | Samsung CL05A105KA5NQNC |

## Li-ion add-on (batch-2 option)

| Ref | Part | Best link | Price seen | Stock seen | Lifecycle | Notes/alternates |
|---|---|---|---|---|---|---|
| — | 803860 LiPo 2000 mAh w/ PCM + certs | [PKCELL LP803860](https://www.batterypkcell.com/lp803860-lithium-polymer-battery-product/) | ~$4–8 (Alibaba MOQ pricing) | factory order | Active | PKCELL lists UL/IEC 62133/UN 38.3/CE/RoHS for LP803860. Singles: [Rokland](https://store.rokland.com/products/pkcell-flat-3-7v-2000mah-rechargeable-lithium-polymer-803860-battery-with-jst-type-ph-2-0-plug). **Get the actual test reports in writing with the PO** |
| — | BQ24074RGTR | [Mouser](https://www.mouser.com/ProductDetail/Texas-Instruments/BQ24074RGTR?qs=ZV%2Fxhq4oszp2Nll7fIx5wg%3D%3D) | $2.46 | Mouser 3,122 | Active | |
| — | MAX17048G+T10 | [Mouser](https://www.mouser.com/ProductDetail/Analog-Devices-Maxim-Integrated/MAX17048G%2bT10?qs=D7PJwyCwLAoGnnn8jEPRBQ%3D%3D) | — | Mouser 8,140 | **Production** (19-wk lead flagged) | Also LCSC C2682616 |
| — | NCP15XH103F03RC | [LCSC C77131](https://www.lcsc.com/product-detail/C77131.html) | from $0.0144 | In stock | Active | TME $0.08 qty 1 |
| — | JST-ZH pair | Header: [B2B-ZR-SM4-TF @ LCSC C265284](https://www.lcsc.com/product-detail/Wire-To-Board-Connector_JST-B2B-ZR-SM4-TF-LF-SN_C265284.html) · Housing: [ZHR-2 @ LCSC C160375](https://www.lcsc.com/product-detail/Rectangular-Connectors-Housings_JST-ZHR-2_C160375.html) | $0.09 / $0.011 | In stock | Active | Note: PKCELL packs ship with JST-PH 2.0 by default — order the pack with ZH or change the BOM to PH |

## Research notes

- Prices/stock captured 2026-08-08 from the linked pages and distributor
  search listings; treat as a snapshot.
- Mouser/DigiKey product pages block automated fetches, so some "price
  seen" cells are from their listings quoted in aggregate search results
  rather than the raw page; the links are direct.
- The mic situation (RED FLAGS 1–3) is the single most urgent decision:
  it affects the U3 footprint on the rev-A PCB.

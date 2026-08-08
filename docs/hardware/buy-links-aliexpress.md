# Buy links — AliExpress / Amazon (prototype stage)

Sourced 2026-08-08 by web search against live listings. Owner constraint:
AliExpress (ships direct to Armenia) preferred, Amazon secondary — no
Mouser/DigiKey/LCSC accounts. Breakout modules are the intended form at
this stage; the production PCB run is JLCPCB-assembled and JLCPCB sources
the bare chips themselves.

AliExpress item links churn (sellers delist weekly). Every direct link
below was verified present in search results on the date above; where a
link dies, the search URL in the Note column finds the same item — these
are commodity modules with dozens of identical sellers.

## What you genuinely CANNOT get on AliExpress/Amazon

These engineering-BOM parts have no acceptable module/listing on either
platform. None of them blocks the prototype — the devkit + breakouts
already cover their function, and **JLCPCB assembly sources every bare
IC/exact-PN below when the production PCB is ordered**:

- **Bare production ICs** — TPS63802DLAR, TPS22918DBVR, USBLC6-2SC6,
  TPD4S014DSQR, SN74LVC1T45DBVR, DMG2301L, BQ24074RGTR, MAX17048G+T10,
  NCP15XH103F03RC. Singles of genuine parts are distributor territory;
  AliExpress "bare IC" listings are a counterfeit lottery. → JLCPCB parts
  library at assembly time. (The TPS63802 as a *module* IS buyable — see
  item 4.)
- **Exact-PN passives/protection** — Littelfuse 1206L075 PTC, TDK
  VLS6045EX-2R2N inductor, BLM18PG601SN1 ferrite beads, the D-case 330 µF
  polymer cap. Generic equivalents exist on AliExpress but the audited
  PNs do not. → JLCPCB; generics are fine on the breadboard.
- **Bourns PTV09A-4220F-B103 (detented 10 kΩ pot)** — detented rotary
  *potentiometers* are effectively absent from AliExpress (detents there
  mean encoders or center-detent only). → prototype with a smooth B10K
  (item 6); the detented Bourns goes on the JLCPCB BOM.
- **Hirose DM3AT SD socket, GCT USB4105-GF-A receptacle** — bare SMD
  connectors; the breakout modules (items 8, 10) replace them at bench
  stage. → JLCPCB.
- **Same Sky GF0668 / Dayton CE50MP-8 / Peerless PLS-50** — verified NOT
  on Amazon (GF0668 is DigiKey/Arrow only; Amazon carries the smaller
  Dayton CE40P-8 but not CE50MP-8 — that one is Parts Express). → bench
  with a generic AliExpress 50 mm 8 Ω speaker (item 5); the named
  candidates are an M10 distributor order anyway, gated on the
  in-enclosure SPL measurement.
- **Industrial/pSLC microSD (SanDisk Industrial, ATP)** — distributor
  part. → consumer SanDisk Ultra from the official AliExpress SanDisk
  store (item 9) is fine for prototype.
- **Certified LiPo (IEC 62133-2 + UN 38.3 test reports)** — AliExpress
  hobby sellers ship uncertified pouches with no paperwork; cells WITH
  reports are Alibaba/manufacturer procurement (e.g. the Alibaba 803860
  listing that explicitly advertises both reports). Fine for a supervised
  bench, mandatory paperwork before any child-facing build.

## Buy table

| # | Item | Where | Link | Price seen | Note |
|---|------|-------|------|------------|------|
| 1 | ESP32-S3 DevKitC-1 (N16R8) | AliExpress | https://www.aliexpress.com/item/1005003819366900.html | ~$12–22 (variant-priced) | Multi-variant listing (N8/N8R2/N8R8/N16R8) — pick **N16R8** or N8R8. Alt: https://www.aliexpress.com/item/1005006240070551.html . Search: https://www.aliexpress.com/w/wholesale-esp32-s3-devkitc-1-n16r8.html |
| 2 | MAX98357A amp breakout | AliExpress | https://www.aliexpress.com/item/1005004840960248.html | $1.85 | Also Amazon 2-pack ~$8: https://www.amazon.com/dp/B0DPJRLMDJ |
| 3 | INMP441 mic breakout | AliExpress | https://www.aliexpress.com/item/32962426410.html | $1.61 | Multi-pack option: https://www.aliexpress.com/item/1005001605597206.html . Chip is EOL but fine for bench (per BOM note). |
| 4 | TPS63802 buck-boost 3.3 V module | AliExpress | https://www.aliexpress.com/item/1005008914857619.html | ~$2–4 | **Real TPS63802 modules DO exist** ("XL63802", output selectable 3.3/4.2/5 V — set 3.3 V). Alt: https://www.aliexpress.us/item/3256806076109931.html . Fallback if both die: TPS63020 module (2 A, very common): https://www.aliexpress.us/item/3256803978683395.html |
| 5 | Speaker ~50 mm 8 Ω ≥1 W | AliExpress | https://www.aliexpress.com/w/wholesale-50mm-full-range-speaker.html | ~$1–3 | Search URL (listings churn); top results verified to contain 50 mm 8 Ω 2 W full-range. GF0668 / CE50MP-8 NOT on Amazon — see CANNOT list. Buy 2–3 different ones and measure SPL in the enclosure. |
| 6 | 10 kΩ pot + knob | AliExpress | https://www.aliexpress.us/item/3256802840715108.html | ~$2–3 /5 pcs | B10K, 6 mm knurled shaft, nuts/washers included — **smooth, not detented** (detented pots aren't on AliExpress; see CANNOT list). Knobs: https://www.aliexpress.com/item/1005001768715069.html |
| 7 | 6 mm tactile buttons | AliExpress | https://www.aliexpress.com/item/1005005477286330.html | ~$2–4 | 180-pc 6×6 kit. Genuine Omron B3F series (Japan) also on AliExpress: https://www.aliexpress.us/item/3256805031306920.html — check the variant menu for B3F-1002. Amazon kit: https://www.amazon.com/dp/B07DG3VGZT |
| 8 | microSD SPI breakout, plain 3.3 V | AliExpress | https://www.aliexpress.us/item/3256810111205156.html | <$1 | Listing explicitly "3.3V SPI for ESP32", headers unsoldered — the small 6-pin board with only pull-up resistors, **no AMS1117, no level shifter** (the blue regulator module is the one to avoid — it's what browned out on the bench). Amazon 10-pack: https://www.amazon.com/dp/B0DRWPJ1T5 |
| 9 | microSD 8–16 GB name brand | AliExpress | https://www.aliexpress.com/store/1102960672 | ~$4–8 | Official **SanDisk Digital Store** on AliExpress — buy Ultra 16 GB (or 32 GB) from there, not a third-party seller (fake-capacity cards are endemic outside brand stores). |
| 10 | USB-C breakout, 16-pin + 5.1 kΩ CC | AliExpress | https://www.aliexpress.us/item/3256807162617616.html | ~$1–3 | 16-pin female with dual 5.1 k CC resistors, 1/5/10-pc packs. Amazon alt: https://www.amazon.com/dp/B0GR8SNSCQ |
| 11 | 3×AA holder, switch + leads | AliExpress | https://www.aliexpress.us/item/3256807670148660.html | ~$1–2 | diymore 1–4-slot with ON/OFF switch — pick the 3-slot (4.5 V). Amazon 3-pack: https://www.amazon.com/dp/B079KTLMFW (~$7) |
| 12 | WS2812B breakout / LED assortment | AliExpress | https://www.aliexpress.us/item/2251832732689860.html | ~$1–3 | WS2812B board modules (1/8/12/16-LED) — USB-powered dev only (VDD min 3.5 V, no valid battery rail per BOM). Discrete-LED baseline comes free with the resistor kit + any 0603/5 mm LED kit: https://www.aliexpress.com/w/wholesale-led-assortment-kit.html |
| 13 | Resistor + capacitor assortment | AliExpress | https://www.aliexpress.com/item/1005003742844212.html | $21.42 | Mixed R+C sample book covering **0402 AND 0603** (0201–1206, 170 values). Cheaper 0603-only resistor book $17.25: https://www.aliexpress.com/item/32323748301.html ; R+C combo $29.98: https://www.aliexpress.com/item/32887863086.html . For breadboard stage a through-hole kit also works. |
| 14 | SMAJ5.0A TVS diodes | AliExpress | https://www.aliexpress.com/item/1849300642.html | ~$3–5 /100 pcs | 100-pc lot, DO-214AC unidirectional. 20-pc assortment (5.0A/6.8A/10A/…): https://www.aliexpress.com/item/1005004906452289.html |
| 15 | JST-ZH / JST-PH connector kit | AliExpress | https://www.aliexpress.com/item/4000898605030.html | ~$1–3 /10 sets | One listing covers SH 1.0 / ZH 1.5 / PH 2.0 / XH 2.54, pre-wired pairs. Alt: https://www.aliexpress.com/item/32807634326.html |
| 16 | LiPo 803860 2000 mAh + charger | AliExpress | https://www.aliexpress.com/w/wholesale-803860-battery.html | ~$5–10 cell; ~$0.5–1 charger | Search URL for the cell (single listings churn fast; verify "with protection board/PCM" in the listing). Charger, TP4056 **Type-C with protection**: https://www.aliexpress.com/item/1005006936016105.html . Certified cells = Alibaba (see CANNOT list). Note: lithium pouches sometimes ship only by slow surface line to AM. |

## Amazon → Armenia

Amazon.com does ship **eligible** items directly to Armenia via the
AmazonGlobal Export program (Armenia is on the export-country list), but
eligibility is per-item — a large share of electronics, third-party-seller
items, and anything with a lithium battery is excluded from export, and
customs/duty is collected at checkout or on arrival. That is why
Armenians overwhelmingly use forwarders: **Onex** and **Globbing** both
run US/EU warehouses with an Armenian pickup-point network (Onex
advertises 764 pickup points + free home delivery) — you order to the
forwarder's US address and they fly it to Yerevan. Practical rule for
this BOM: AliExpress direct for everything; Amazon-only items go through
Onex/Globbing unless the listing explicitly shows "Ships to Armenia".

Sources: [Amazon — AmazonGlobal Export Countries](https://www.amazon.com/gp/help/customer/display.html?nodeId=GCBBSZMUXA6U2P8R),
[Amazon — International Shipping](https://www.amazon.com/gp/help/customer/display.html?nodeId=GJF6884LHHZ5ELD4),
[Does Amazon Ship to Armenia (2026 guide)](https://joyofcreating.org/does-amazon-ship-to-armenia),
[Onex — Shipping from Amazon to Armenia](https://onex.am/en/shops/usa/Amazon)

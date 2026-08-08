# JLCPCB PCBA readiness — rev-A BOM

Researched 2026-08-09, against `areg-reva.net` (89 nets, 100 symbols) and
`generate_schematic.py`. Purpose: find every part that would fail, surprise,
or overcharge at checkout **while a footprint change is still cheap**.

Scope: 47 unique BOM lines — **35 SMT lines / 74 placements**, **6 THT parts /
4 lines**, 12 test pads (artwork, no BOM line), and 8 DNP positions that are
not ordered for run 1 (U8, C10, R15, R16, R38, R39, C22, C23).

---

## ⛔ BLOCKERS — what would stop an order placed today

| # | Item | Why it blocks | What to do |
|---|---|---|---|
| **B1** | **RV1 — Bourns PTV09A-4220F-B103** | **Not in the LCSC/JLCPCB catalogue at any tier.** The closest sibling PTV09A-4020F-B103 (`C5848782`) is **out of stock** *and* is the **no-detent** variant, so it is not a functional substitute either. | JLCPCB cannot fit this part. Hand-solder in Armenia (fine at 5 boards), or consign, or respec to a detented control JLC stocks. **Decision needed before the 100-board run, not before layout** — the THT footprint stays either way. |
| **B2** | **U5 — TPD4S014DSQR (`C202244`)** | **Stock 96 units** (JLCPCB) / 78 (LCSC). Enough for 5 boards, **not enough for 100.** Every other TPD4S family member (`TPD4S010DQAR`, `TPD4S311YBFR`, `TPD4S1394DQLR`, `TPD4S009DBVRG4`, `TPD4S009DCKRG4`) shows **0 stock and none is WSON-10 2×2** — there is no same-footprint drop-in. | Order 5 now; before the 100-run either buy/consign a reel, or split the function (keep USBLC6 for ESD + a separate OVP part). **This is the one that could force a footprint change later**, so decide deliberately. |
| **B3** | **U3 — ICS-43434 (`C5656610`)** | **Past its last-time-buy.** TDK PCN-000772: LTB **2026-06-15 (≈2 months ago)**, last-time-ship 2027-01-15. Stock 5,448 (LCSC) is all there will ever be. LCSC shows no EOL warning — it is silently selling through. | Known risk, already anticipated: the board carries the **IM69D130 alternate footprint** (U8, DNP). Buy ICS-43434 stock now for near-term builds. ⚠️ The obvious I2S successor **SPH0645LM4H-B (`C19190864`), which IS footprint-compatible (identical 3.50×2.65×0.98 mm 6-pad), is currently OUT OF STOCK at LCSC**; ICS-43432 (`C574021`) is a *different* 7-pad 3×4 mm package; MSM261S4030H0R (`C2840615`) is **top-ported** — an enclosure change, not a footprint swap. |

**Nothing else blocks an order.** In particular the two things the brief
expected to be blockers are **not**: JLCPCB *does* place through-hole parts
(see § THT), and the ESP32-S3-WROOM-1-N8R8 *is* stocked (`C2913201`).

---

## Full readiness table

Tier key: **Basic** and **Preferred** both cost **$0** loading. Only
**Extended** costs $3 per unique part number. Tier is from the JLCPCB
Basic+Preferred library dump (2,004 parts, snapshot **2026-08-07**) — a part
absent from that list is Extended by definition.

Stock/price are **LCSC/JLCPCB figures as seen on 2026-08-09** and will drift.

### Active ICs, modules, connectors

| Ref | Part | LCSC | Tier | Stock | Verdict | Note |
|---|---|---|---|---|---|---|
| U1 | ESP32-S3-WROOM-1-**N8R8** | **C2913201** | Extended | 6,525 | **OK** | Schematic carried no LCSC; the `C2913202` in the generator comment is the **N16R8**. $4.99@5 / $3.63@100. **All 28 WROOM-1 variants are Extended** — a variant swap saves no fee. Consider N16R8 `C2913202` anyway: **31,925 stock** vs 6,525, +$0.16, same footprint, double flash. |
| U2 | MAX98357AETE+T | **C910544** ✅ | Extended | 12,967 | **OK** | Guess correct. $1.34@5 / $0.90@100. Cheaper than the Mouser price in `buy-links.md`. |
| U3 | ICS-43434 | **C5656610** | Extended | 5,448 | **BLOCKER (B3)** | Past LTB. $3.34@5 / $2.78@100. |
| U4 | TPS63802DLAR | **C2845237** | Extended | 3,365 | **OK** | Was unknown in the schematic. VSON-10(2×3). $1.03@5 / $0.68@100. |
| U5 | TPD4S014DSQR | **C202244** | Extended | **96** | **BLOCKER (B2)** | $2.15@5 / $1.45@100. |
| U6 | USBLC6-2SC6 | **C7519** ✅ | Extended | 30,686 | **OK** | Guess correct. **Extended, not Basic** — contrary to common belief. $0.18@5 / $0.14@100. Clone alternates `C2687116` / `C2827654` are ~4× cheaper but also Extended; keep genuine ST on a child's USB port. |
| U7 | TPS22918DBVR | **C131941** ✅ | Extended | 30,168 | **OK** | Guess correct. $0.22@5 / $0.18@100. |
| Q1 | DMG2301L-7 | **C102619** ✅ | Extended | 9,419 | **SUBSTITUTE** | Guess correct, but → **`C15127` AO3401A, SOT-23, Basic, 631k stock**, P-ch −30 V / −4 A / Vgs(th) 0.9 V. Same footprint, same role (reverse block + USB-presence mux). **Saves $3.** |
| J1 | GCT USB4105-GF-A | **C3020560** | Extended | 8,294 | **OK** | $1.07@5 / $0.67@100. Cheaper alt `C165948` (HRO TYPE-C-31-M-12, 446k stock, $0.12@100) is *reported* footprint-compatible but on a single unsourced claim — **do not swap without overlaying the drawings**. ⚠️ LCSC/JLCPCB list USB4105-GF-A as "12P" while GCT's datasheet says 16 contacts; our footprint is the 16P. Confirm at cart time. |
| J3 | Hirose DM3AT-SF-PEJM5 | **C114218** | Extended | 10,349 | **OK** | $1.44@5 / $0.93@100. **No microSD socket exists at Basic/Preferred** — the whole category is Extended. Cheap alternates (`C91145` TF-01A etc.) could **not** be confirmed DM3AT-footprint-compatible; staying on DM3AT is the low-risk call. |

### Discretes, magnetics, protection

| Ref | Part | LCSC | Tier | Stock | Verdict | Note |
|---|---|---|---|---|---|---|
| L1 | 2.2 µH VLS6045EX-2R2N | — | — | — | **SUBSTITUTE** | **TDK VLS6045EX is not stocked by JLCPCB.** ✅ **Footprint verified from the KiCad file**: `L_TDK_VLS6045EX_VLS6045AF` descr = *"6x6x4.5mm"*, pads 1.9×5.1 mm at ±2.1 mm — a **6×6 mm square land**, not 6.0×4.5. So the standard "6045" family drops in. Best pick **`C2849533` DMBJ PNLS6045-2R2M** — 2.2 µH, **8.8 A**, 18.4 mΩ, 6.0×6.0×4.5 mm, 1,290 stock, **$0.05@100**. Alt `C36500` Sunlord SWPA6045S2R2NT (4.6 A/7.4 A sat). Verify the vendor pad drawing against the 1.9×5.1 @ ±2.1 land before fab. |
| F1 | Littelfuse 1206L075 | **C207036** | Extended | 5,355 | **OK** | Exact part stocked (750 mA hold / 1.5 A trip / 13.2 V). $0.17@5 / $0.12@100. Schematic had no LCSC. **No Basic 1206 PPTC exists.** Budget alt `C49318367` (YTL, same spec, ~⅓ price, still Extended). |
| D4 | SMAJ5.0A | **C83329** ✅ | Extended | 23,160 | **SUBSTITUTE** | Guess correct (Littelfuse, DO-214AC, unidirectional, 5 V standoff, 9.2 V clamp). → **`C19077523` SMAJ5.0A, DO-214AC, Preferred (no fee), 99,067 stock**, identical ratings. **Saves $3.** |
| FB1 | Ferrite 0603 (mic supply) | ~~C1017~~ → **C1002** | Basic | 790,004 | **SUBSTITUTE** | ⚠️ **The schematic's `C1017` is wrong.** C1017 = Sunlord GZ2012D601TF, **0805** — right impedance, **wrong package** for our 0603 footprint. Murata BLM18PG601SN1D has no JLC listing. Use **`C1002`** (Sunlord GZ1608D601TF, 0603, 600 Ω@100 MHz, 200 mA, Basic). Fine here — mic branch draws ~1 mA. |
| FB2, FB3 | Ferrite 0603 (speaker legs) | ~~C1017~~ → **C14709** | Basic | 2,693,209 | **SUBSTITUTE** | Same wrong-package problem **plus a current problem**: FB2/FB3 sit in series with the class-D speaker output (~0.3 A rms, ~0.4 A peak into 8 Ω), so C1002's **200 mA** rating is under-spec. Use **`C14709` Murata BLM18PG121SN1D — 0603, 120 Ω@100 MHz, 2 A, 50 mΩ, Basic**. Lower impedance is also the *correct* choice on a power/audio leg. No footprint change. |
| C21 | 330 µF 6.3 V polymer | **C79113** ✅ | Extended | 1,192 | **OK** | Guess correct: Panasonic 6TPE330ML (POSCAP), **7.3×4.3 mm = EIA-7343 D case** ✅, 25 mΩ ESR. $0.87@5 / $0.63@100. Stock is thin for a 100-run — check before ordering. |
| D1 | LED 0603 red | **C2286** | **Basic** | 6,877,321 | **OK** | KT-0603R. |
| D2 | LED 0603 blue | **C2288** | Extended | 257,900 | **OK** | KT-0603B, 469 nm. |
| D3 | LED 0603 green | **C12624** | Extended | in stock | **OK** | KT-0603G, 525 nm. **No Basic 0603 green or blue exists** (normal — InGaN dies). Costs 2 × $3. Only if you don't care about colour semantics: red/white(`C2290`)/yellow(`C89811`) are all fee-free. |

### Passives — all fee-free after the 0603 moves

Every 0402 guess in the schematic verified **correct** for value and package.

| Value | Qty | LCSC | Pkg | Tier | Verdict |
|---|---|---|---|---|---|
| 5.1 k | 2 | **C25905** ✅ | 0402 | Basic | OK |
| 100 k | 1 | **C25741** ✅ | 0402 | Basic | OK |
| 10 k | 14 | **C25744** ✅ | 0402 | Basic | OK |
| 1 k | 4 | **C11702** ✅ | 0402 | Basic | OK |
| 10 R | 1 | **C25077** | 0402 | Basic | OK (was unassigned) |
| 200 R | 1 | **C25087** | 0402 | Basic | OK (was unassigned) |
| **0 R** | 3 | C17168 (0402) → **C21189 (0603)** | — | Basic | **SUBSTITUTE** — 0402 part forces a **1,367-pc / $9.02** minimum buy. 0603 costs $0.16. |
| **33 R** | 6 | C25105 (0402) → **C23140 (0603)** | — | Basic | **SUBSTITUTE** — 0402 forces **2,996 pc / $8.99**. 0603 costs $0.16. |
| **560 R** | 2 | **C23204** | **0603** | Basic | **SUBSTITUTE** — no fee-free 0402 exists at this value. |
| **91 k** | 1 | **C23265** | **0603** | Preferred | **SUBSTITUTE** — no fee-free 0402 exists. (Or accept 100 k `C25741`, −9 %.) |
| **511 k** | 1 | **C2930114** | **0603** | Preferred | **SUBSTITUTE** — no fee-free 0402 exists; nearest Basic 0402 values are 200 k / 1 M. ⚠️ This entry has blank PCBA min-qty fields — confirm it is orderable for assembly. |
| 100 nF | 9 | **C1525** ✅ | 0402 | Basic | OK — ⚠️ **it is 16 V, not 50 V** (46 M stock, cheapest in the library). Fine on a 3.3 V rail. If 50 V was deliberate use `C307331` (Basic, 28 M, ~2×). |
| 1 µF | 1 | **C52923** ✅ | 0402 | Basic | OK |
| 4.7 nF | 1 | **C1538** | 0402 | Basic | OK (was unassigned) |
| 10 µF | 3 | **C19702** ✅ | 0603 | Basic | OK |
| 22 µF | 3 | **C59461** | 0603 | Basic | **OK — no footprint change needed.** The feared "22 µF only in 0805" does not apply. |
| 100 µF | 1 | **C15008** | 1206 | Basic | OK — ⚠️ **only 6.3 V exists**. Meets ≥6.3 V with zero margin, and X5R at this density loses well over half its value under 3.3 V DC bias. Size the bulk accordingly. |
| 1 nF **C0G** | 2 | — | 0402 | — | **DNP — not ordered.** No fee-free C0G ≥330 pF exists in any package; 0402 C0G tops out at 100 pF. If ever populated: X7R `C1523` (Basic) or pay $3. |

### Through-hole — JLCPCB CAN place these

| Ref | Part | LCSC | Tier | Stock | Verdict | Note |
|---|---|---|---|---|---|---|
| SW1–3 | Omron B3F-1002 | **C87036** | Extended | 160 | **OK (wave-soldered)** | Part page carries `Assembly Type: Wave Soldering`. $0.17@5 / $0.12@100. ⚠️ **160 units is thin for a 100-board run** (needs 300). Optional swap: **`C318884` TS-1187A-B-A-B — SMD-4P 5.1×5.1 mm, 1.6 N (= the same 160 gf), Basic, 1.23 M stock** — removes the $3 fee *and* the THT charge, but is a footprint change (do it now or never). |
| J2, J4 | JST B2B-PH-K-S | **C131337** | Extended | 413,320 | **OK (wave-soldered)** | `Assembly Type: Wave Soldering`. $0.035@5 / $0.025@100. |
| RV1 | Bourns PTV09A-4220F-B103 | **not in catalogue** | — | — | **HAND-SOLDER (B1)** | See blockers. |
| TP1–12 | Test pads | n/a | — | — | **OK** | Bare copper artwork — no BOM line, no C-number, no placement cost. Correct as drawn. |

**The THT assumption in the brief is out of date.** JLCPCB's capabilities page
states plainly: *"JLCPCB supports Through-Hole Technology (THT) component
assembly and mixed-technology (SMD + THT) PCB assembly within a single PCBA
order… available under both the Economic and Standard PCBA."* Cost is
**$3.50 hand-soldering labour per order + $0.0173 per joint** (both quoted),
plus one extra build day. Caveat: JLCPCB's own copy is internally inconsistent
— the capabilities page says *wave soldering*, the FAQ calls the same charge a
*hand-soldering* fee. The published fee model is what is billed; which process
runs is ambiguous. Unused through-holes are explicitly kept clear of solder so
the pot can be fitted later.

**So only ONE part (RV1) must be hand-soldered in Armenia — not five.**

---

## Extended-part count

| Scenario | Extended lines | Loading fee |
|---|---|---|
| **As drawn today** | **18** | **$54** |
| After the two zero-risk swaps (Q1→AO3401A, D4→`C19077523`) | **16** | **$48** |
| Also swapping LED colours and buttons to Basic parts | **13** | **$39** |

The 18 as-drawn: U1, U2, U3, U4, U5, U6, U7, Q1, J1, J3, L1, F1, C21, D2, D3,
D4, SW1–3 (one line), J2/J4 (one line). RV1 is not counted — it cannot be
ordered at all.

Note the fee is **per unique part number per order**, not per board — it is a
fixed $48–54 whether you build 5 boards or 100.

---

## Cost estimate

Every line is labelled **[Q]** quoted from a JLCPCB published page, **[L]**
read off an LCSC/JLCPCB part page on 2026-08-09, or **[E]** my estimate.
**No estimate below is a quote.** The only way to get a real number is to
upload Gerbers + BOM + CPL to JLCPCB's cart.

### Fee schedule used (Economic PCBA)

| Item | Rate | Source |
|---|---|---|
| Assembly setup fee | $8.00 | **[Q]** |
| Stencil | $1.50 | **[Q]** |
| SMT assembly | $0.0016 / joint | **[Q]** |
| Extended part loading | $3.00 / unique part | **[Q]** |
| THT | $3.50 / order + $0.0173 / joint | **[Q]** |
| 4-layer board charge | $70.60 / m² | **[Q]** |
| FR-4 PCB floor | "From $2.00 / 5 pcs" | **[Q]** |

Board = 60 × 50 mm = **0.003 m²**. Joints: **270 SMT/board** **[E]** (256
net-connected nodes counted from the netlist + 12 no-connect pins + shields
and thermal pads) and **10 THT joints/board** for the buttons and JST headers
(RV1's 3 excluded — hand-soldered locally).

### 5 assembled boards

| Line | Arithmetic | Cost |
|---|---|---|
| Bare PCB, 4-layer | 0.015 m² × $70.60 = $1.06 → below the $2 floor **[Q]** | $2.00 |
| ENIG surcharge | JLCPCB publishes no base ENIG rate (only a >30 %-coverage adder of $0.8992/m²/1% **[Q]**) | **[E]** $12.00 |
| 4-layer engineering / e-test above the ≤50×50 promo band | | **[E]** $3.00 |
| Assembly setup | **[Q]** | $8.00 |
| Stencil | **[Q]** | $1.50 |
| SMT assembly | 5 × 270 = 1,350 joints × $0.0016 **[Q]** | $2.16 |
| **Extended part fees** | 18 × $3.00 **[Q]** | **$54.00** |
| THT (buttons + JST) | $3.50 + (5 × 10 × $0.0173) **[Q]** | $4.37 |
| Components | sum of per-line max(need, JLC minimum buy) **[L]** | $94.88 |
| Shipping to Armenia (DHL/FedEx, ~0.3 kg) | no quote obtainable without a cart | **[E]** $32.00 |
| | | |
| **TOTAL — 5 boards** | | **≈ $214** |
| | | **≈ $42.80 / board** |

### 100 assembled boards

| Line | Arithmetic | Cost |
|---|---|---|
| Bare PCB, 4-layer | 0.3 m² × $70.60 **[Q rate]** | $21.18 |
| ENIG surcharge | | **[E]** $30.00 |
| Engineering / e-test | | **[E]** $10.00 |
| Assembly setup | **[Q]** | $8.00 |
| Stencil | **[Q]** | $1.50 |
| SMT assembly | 100 × 270 = 27,000 joints × $0.0016 **[Q]** | $43.20 |
| **Extended part fees** | 18 × $3.00 — **flat, not per board** **[Q]** | **$54.00** |
| THT | $3.50 + (100 × 10 × $0.0173) **[Q]** | $20.80 |
| Components | at qty-100 price breaks **[L]** | $1,353.20 |
| Shipping to Armenia (~3 kg) | | **[E]** $90.00 |
| | | |
| **TOTAL — 100 boards** | | **≈ $1,632** |
| | | **≈ $16.32 / board** |

### Not included in either total

- **RV1 pot** — bought from a Western distributor (~$1–2 each) and hand-soldered
  in Armenia. 100 boards = 100 hand-solder operations; cost that as labour.
- **Armenian import VAT/duty** **[E]** — typically 20 % on the declared value:
  ≈ **$43** on the 5-board order, ≈ **$326** on the 100-board order. JLCPCB
  states plainly that duties are the importer's responsibility and it cannot
  estimate them. Confirm the actual Armenian rate before budgeting.
- **Speaker, microSD card, 3×AA holder, enclosure, knob** — all off-board.
- **Offset:** JLCPCB advertises **up to $60 in new-user sign-up coupons** **[Q]**,
  which could cover a large share of the first 5-board order.

### Where the money actually goes

At 5 boards, **the $54 of Extended fees is 25 % of the order** and the setup +
stencil + fees together ($63.50) exceed the entire PCB fabrication cost. At
100 boards, components are **83 %** of the total and every fixed fee is noise.
The two zero-risk swaps below are worth ~11 % of a 5-board order and ~0.4 % of
a 100-board one — which tells you they are a prototype-economics decision, not
a production one.

---

## What to change in the schematic before layout

Ordered by cost of fixing it later.

1. **Move 14 positions from 0402 to 0603** — 0 R (R4, R13, R14), 33 R (R23–25,
   R35–37), 560 R (R41, R43), 91 k (R6), 511 k (R5). Three of those values have
   **no fee-free 0402 part at all**, and the 0 R / 33 R 0402 parts force
   **~$18 of minimum-buy** that is worse than the fee they avoid. In 0603 every
   one is Basic or Preferred with a normal 20-piece minimum. **This is the
   single change that is cheap now and expensive after layout.**
2. **Fix the ferrite beads (a real error, not an optimisation).** `C1017` is an
   **0805** part sitting on a 0603 footprint. Split the three positions by job:
   **FB1 → `C1002`** (0603, 600 Ω, 200 mA — mic branch, ~1 mA) and
   **FB2/FB3 → `C14709`** (0603, 120 Ω, **2 A** — they carry class-D speaker
   current, where a 200 mA bead is under-rated). Both Basic, both 0603, no
   footprint change.
3. **Set L1 to `C2849533`** (DMBJ PNLS6045-2R2M, 2.2 µH, 8.8 A). The existing
   6×6 mm land is correct — verified from the KiCad footprint, not assumed.
   Still gated on the Phase-2 datasheet check of 2.2 µH vs TI's typical 1.5 µH.
4. **Two free swaps, no downside:** Q1 → **`C15127` AO3401A** (Basic),
   D4 → **`C19077523` SMAJ5.0A** (Preferred). Saves $6 and two feeder loads.
5. **Fill in the six missing LCSC numbers** the schematic never carried:
   U1 `C2913201`, U4 `C2845237`, U5 `C202244`, U3 `C5656610`, F1 `C207036`,
   L1 `C2849533`. Correct the generator comment that implies `C2913202` is the
   N8R8 — it is the N16R8.
6. **Decide U1 variant now.** N16R8 (`C2913202`) is +$0.16 for **31,925 stock
   vs 6,525** and double the flash, same footprint and tier. Given the OTA
   image is already ~1.26 MB against a 3 MB slot, the extra flash is not needed
   — but the 5× stock buffer is worth having.
7. **Optional, footprint-changing, decide before layout or never:**
   SW1–3 → **`C318884`** (SMD 5.1×5.1 mm, same 1.6 N force, Basic, 1.2 M stock)
   removes a $3 fee, the THT charge, and the thin 160-unit stock on `C87036`.
8. **Optional cost lever:** if the layout can fit **≤50 × 50 mm**, the board
   drops into JLCPCB's $2 4-layer price band. Whether that is reachable with a
   25.5 × 18 mm module plus the SD socket is a layout question, not a BOM one.
9. **Record, don't change:** C1525 is 16 V (fine at 3.3 V); C15008 is the only
   100 µF 1206 and is 6.3 V with heavy DC-bias derating — if the buck output
   genuinely needs 100 µF of *effective* capacitance, add margin.

---

## Method and reliability

**One trap worth recording.** `jlcpcb.com/parts/componentSearch?searchTxt=…`
is client-rendered and returns **"Search results (0 Found)"** to any fetcher,
whatever you search for. A control test with `MAX98357A` and `C1525` — both
definitively stocked — returned "0 Found" for each. Two of the four research
passes initially produced false "not stocked" conclusions from it, including
for the JST header and the tactile switch, which are in fact both stocked
*and* placeable. Every "not available" claim in this document rests on a
direct `partdetail`/LCSC page or on absence from the library dump, never on
that endpoint.

Sources: [JLCPCB assembly price](https://jlcpcb.com/help/article/pcb-assembly-price) ·
[PCB assembly FAQs](https://jlcpcb.com/help/article/pcb-assembly-faqs) ·
[assembly capabilities](https://jlcpcb.com/capabilities/pcb-assembly-capabilities) ·
[4-layer price announcement](https://jlcpcb.com/news/discount-on-quality-4-layer-pcbs) ·
[extra-charge schedule](https://jlcpcb.com/help/article/in-what-cases-will-there-be-charged-extra) ·
[Basic vs Preferred](https://jlccnc.com/help/answers/detail/837-What-is-the-practical-difference-between-Basic-and-Preferred) ·
[JLCPCB Basic/Preferred library dump](https://lrks.github.io/jlcpcb-economic-parts/) (2,004 parts, 2026-08-07) ·
LCSC product pages per part.

**Not verified, carry into the cart step:** exact tier for `C3020560` and
`C79113` (both almost certainly Extended); JLCPCB-side stock for parts where
only LCSC stock was readable; the `C2930114` blank PCBA-minimum fields; the
DM3AT vs TF-01A footprint question; GCT USB4105's 12P-vs-16P attribute
discrepancy; and the vendor pad drawing for `C2849533` against our 1.9 × 5.1
@ ±2.1 mm land.

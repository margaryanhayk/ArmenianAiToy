# Rev-A PCB — Phase 2 (board layout) — PLACED + DRC-CLEAN, ROUTING 99 % DONE

Date: 2026-08-10. Contract: `rev-a-design-inputs.md` § "Layout rules".
Phase 1 (schematic): `phase1-status.md`. Nothing has been committed.

**Read this first.** The board is fully PLACED, the placement satisfies every
measurable rule in the contract, and DRC reports **zero errors**. It is
**not finished**: 11 pad-pairs are still ratsnest and three power traces are
narrower than their current calls for. Every one is listed below by
ref/pin/net. **Do not generate Gerbers from this file yet.**

## Verification gate

| Gate | Result |
|---|---|
| `kicad-cli pcb drc` (all severities) | **0 errors**, 16 warnings (each justified below) |
| Unconnected items | **11** — listed individually in § Remaining ratsnest |
| `kicad-cli pcb drc --schematic-parity` | **0** (see § Parity — it was 157 until the flag was actually used) |
| `kicad-cli sch erc` after the schematic fix below | **0 errors**, 1 pre-existing justified warning |
| Courtyard-overlap + off-board check (polygon-exact, in `generate_pcb.py`) | 0 overlaps, nothing off-board |
| Contract distances, measured on the board | all PASS — table below |
| Renders inspected by eye | `out/render-top.png`, `out/render-bottom.png` |

## The board

| | |
|---|---|
| Outline | **74 × 62 mm** (target was ~60 × 50 — reason below) |
| Stackup | 4 layers: **F.Cu signal / In1.Cu GND / In2.Cu +3V3 / B.Cu signal**, 1.6 mm |
| Footprints | **100 / 100** from `areg-reva.net`, all on the TOP side (single-sided assembly) |
| Copper | **824 track segments, 1,347 mm total; 340 vias** (0.6 mm pad / 0.3 mm drill) |
| Track split | F.Cu 522 / B.Cu 302 |
| Widths used | 0.50 mm ×116, 0.40 ×10, 0.30 ×24, 0.25 ×2, 0.20 ×382, 0.12 ×290 |
| Design rules | 0.15 mm clearance · 0.11 mm min track · 0.20 mm min drill · 0.15 mm hole clearance · 0.25 mm copper-to-edge |

### Why 74 × 62 and not 60 × 50

60 × 50 mm is 3,000 mm². The parts that cannot shrink — a 25.5 × 18 mm
module, a 15.8 × 17.8 mm SD socket, a 14.5 × 13.8 mm pot, a 10.7 × 9.0 mm
USB-C, three 8.8 × 6.8 mm through-hole buttons, a 6 × 6 mm inductor, an
8.9 × 4.9 mm polymer cap — total roughly 1,200 mm² of courtyard before a
single 0402 is placed, and **everything is on one side**, because the
JLCPCB cost model in `jlcpcb-readiness.md` assumes single-sided assembly.
At 74 × 62 = 4,588 mm² coverage is ~40 %, which is what left room to route.

The cost of the growth, stated rather than hidden: at JLCPCB's $70.60/m²
4-layer rate this is **+$0.11 per board** (+$11 over a 100-board run). The
≤ 50 × 50 mm price band was unreachable either way. If the enclosure later
demands 60 × 50, the honest answers are double-sided assembly or dropping
the pot — not tighter routing.

## Placement — the contract rules, measured on the board

All asserted in `generate_pcb.py::check_contract()`; the generator refuses
to emit a board if any fails.

| Rule (source) | Required | Measured |
|---|---|---|
| Module decoupling C6 from U1 pin 2 (spec §2) | ≤ 3 mm | **2.64 mm** |
| Module decoupling C7 from U1 pin 2 | ≤ 3 mm | **2.77 mm** |
| Amp C19 from U2 VDD | ≤ 2 mm | **1.98 mm** |
| Amp C20 from U2 VDD | ≤ 5 mm | **4.28 mm** |
| Amp C21 (330 µF polymer) from U2 VDD | ≤ 15 mm | **8.04 mm** |
| SD C11 / C12 at the socket | at the socket | **3.98 / 3.95 mm** from J3 VDD |
| Mic U3 → speaker connector J4 (rule 4) | ≥ 40 mm | **65.5 mm** |
| Mic U8 (PDM alternate) → J4 | ≥ 40 mm | **65.8 mm** |
| Antenna keep-out (rule 1) | no copper, all layers | rule area at **x −21.1 … −0.1 mm** — entirely off the board |
| Amp I2S BCLK run (spec §2) | < 50 mm | 48.9 mm |
| SD CLK run | < 50 mm | 38.3 mm |
| Mic BCLK run | < 50 mm | 12.0 mm |

### Antenna keep-out (rule 1)

U1 is rotated 90° with its antenna overhanging the **left** board edge by
6 mm. The outline is cut exactly on the module's own antenna/body boundary,
so the stock footprint's 48 × 21 mm rule area lands entirely at x < 0 — off
the board. There is therefore no copper, no battery and no speaker magnet
inside it *by construction*, and the check is an assertion rather than a
review note. J2 (battery) is 30 mm away; J4 (speaker) is at the opposite
edge. Confirmed visually in `out/render-top.png`.

### Star ground and the speaker return (rule 2)

Realised by placement plus a solid plane, not by splitting copper:

* In1.Cu is one uninterrupted GND plane. The amp's supply-return loop
  (C19/C20/C21 → U2 GND + EP) is local and sits ~55 mm from the mic.
* **The speaker return never shares ground copper with the mic — because a
  bridge-tied class-D output has no ground return at all.** SPK_P and SPK_N
  are both driven; neither is ground. The contract's rule is satisfied in
  its strongest form, not merely approximated.
* The two speaker legs leave U2 through FB2/FB3 and run parallel, 2.5 mm
  apart, straight to J4 at the board edge — a tight loop. Ferrite and 1 nF
  footprints are present on both legs as rule 6 requires.
* Zones connect to pads SOLID, not with thermal spokes: on a plane feeding
  a 2 A buck-boost and a class-D amp, spokes are series impedance in the
  return path. RV1 — the one hand-soldered part, per `jlcpcb-readiness.md`
  B1 — keeps thermal relief through a per-pad override.

## Routing

`route_pcb.py` is a router written for this job, because KiCad ships none:
A* on a 0.1 mm grid over F.Cu and B.Cu, octile moves, per-width obstacle
masks, and rip-up-and-reroute across four full passes (each pass rebuilds
from the placed state, promotes the previous pass's failures to the front
of the queue, and the best-scoring pass is kept).

| | |
|---|---|
| Signal nets fully routed | **71** |
| Partially routed | **1** — USB_DM_C |
| Not routed | **2** — MIC_SCK_I2S, VBUS_F |
| GND pads tied to the plane by their own via | 99 of 104 (rest reach it through the F.Cu pour) |
| +3V3 pads tied to the plane | **31 of 31** |
| GND stitching vias | 150 |

### Remaining ratsnest — all 11, by ref/pin/net

| # | Net | Pads still open | Why |
|---|---|---|---|
| 1 | **VBUS_F** | `F1.2 ↔ D4.1` | USB power rail. Its four loads (F1, D4, U5, U6) sit in the most congested 15 mm of the board, and U5 is a 0.4 mm-pitch WSON. |
| 2 | **VBUS_F** | `D4.1 ↔ U5.9` | as above |
| 3 | **VBUS_F** | `U5.9 ↔ U5.10` | the two VBUS_F pins of U5 are on a 0.4 mm pitch |
| 4 | **VBUS_F** | `U5.10 ↔ U6.5` | as above |
| 5 | **USB_DM_C** | `J1.A7 ↔ J1.B7` | the USB-C receptacle interleaves D+ and D− pads (37.25 DM / 37.75 DP / 38.25 DM / 38.75 DP); joining the two DM pads means crossing a DP pad |
| 6 | **USB_DM_C** | `J1.A7 ↔ existing USB_DM_C track` | same cause |
| 7 | **MIC_SCK_I2S** | `R13.2 ↔ U3.4` | 3.7 mm apart, but the mic block (U3, U8, C9, C10, R13–R16, FB1, R7, C5) fills a 14 × 12 mm pocket; both pads are individually reachable, so this is pure congestion, not geometry |
| 8 | **GND** | `U5.3 ↔ F.Cu GND pour` | 0.4 mm-pitch pin the pour cannot reach between its neighbours |
| 9–11 | **GND** | F.Cu pour island ↔ In1.Cu plane (×3) | three small F.Cu pour islands with no stitching via inside them. They hold no component pads — floating copper, not a broken connection — but they should be stitched or removed |

### Power traces narrower than their current calls for

Widths are limited by the **pad**, not the design rule: a TPS63802 DLA pin
is 0.28 mm wide and a MAX98357A TQFN pin 0.25 mm, so no trace leaving them
can be wider whatever copper is free further out. Capacity, IPC-2221
external, 35 µm copper, 10 °C rise: 0.50 mm → 1.4 A · 0.40 → 1.1 A ·
0.30 → 0.72 A · 0.25 → 0.61 A · 0.20 → 0.44 A · 0.12 → 0.38 A.

| Net | Got | Wanted | Verdict |
|---|---|---|---|
| **VSYS** | 0.12 mm | 0.50 mm | **DEFECT — must be fixed.** System rail from battery/USB into the buck; up to ~1 A. 0.12 mm carries 0.38 A at 10 °C rise. |
| **VBUS_PROT** | 0.12 mm | 0.40 mm | **DEFECT — must be fixed.** Behind the PTC, so up to 0.75 A hold / 1.5 A trip. |
| AMP_OUTN | 0.12 mm | 0.25 mm | Marginal. Speaker peak 0.41 A, rms ~0.29 A vs 0.38 A capacity. Widen with AMP_OUTP (already 0.25 mm) when the escape is hand-routed. |
| SW1 / SW2 | 0.30 mm | 0.30 mm | OK — this is the TPS63802 pad width, the physical maximum |
| SPK_P / SPK_N | 0.50 mm | 0.50 mm | OK |
| VBAT | 0.50 mm | 0.50 mm | OK |
| VBUS_C | 0.40 mm | 0.40 mm | OK |
| AMP_OUTP | 0.25 mm | 0.25 mm | OK — TQFN pad width |
| FB63802 | 0.20 mm | 0.20 mm | OK — buck feedback, signal-level |

### Planes

* **In1.Cu — solid GND** across the board.
* **In2.Cu — solid +3V3** across the board. An earlier revision carved
  VSYS / VBUS_PROT / VBAT islands out of In2; that was **removed** because
  those rails interleave with +3V3 in the same 10 mm of the bottom-right
  corner (C1/C2 are VSYS, C3/C4 immediately beside them are +3V3), so any
  island large enough to serve VSYS also swallowed +3V3 pads and **split
  the +3V3 plane into two unconnected islands** — i.e. a severed main rail.
  That is why VSYS and VBUS_PROT are now thin traces: the trade was a
  correct plane against two under-width traces, and a severed 3V3 rail is
  the worse of the two. Both are on the fix list.
* F.Cu and B.Cu carry GND pours tied to In1 by 150 stitching vias plus one
  via at or beside almost every GND pad.

## DRC

```
violations        16   (0 errors, 16 warnings)
  silk_over_copper 11  warning
  silk_overlap      5  warning
unconnected_items 11   (listed above)
schematic_parity   0
```

Full report: `out/drc.rpt` / `out/drc-final.json`.

### Parity — a check that had not actually run

Earlier passes of this work reported "parity 0". That was wrong, and worth
recording rather than quietly correcting: the JSON report contains a
`schematic_parity` key whether or not the check ran, and it is empty unless
`--schematic-parity` is passed. Running it properly gave **157 issues**:

* 112 × `footprint_symbol_mismatch` — `pcbnew.FootprintLoad()` returns a
  footprint whose id is the bare name (`C_0603_1608Metric`) rather than the
  library-qualified id the symbol declares
  (`Capacitor_SMD:C_0603_1608Metric`). Same footprint, unqualified id.
* 45 × `footprint_symbol_field_mismatch` — the symbols' `LCSC` fields were
  never copied onto the footprints. **Not cosmetic**: the JLCPCB BOM/CPL
  export reads the part number off the footprint, so a fab upload from this
  board would have gone out with 45 blank part numbers.

Both are fixed by `fix_metadata.py`, which edits metadata only (track, via
and zone counts are byte-identical before and after: 824 / 340 / 4). The
copied fields are set invisible — leaving them visible put an LCSC number on
the silkscreen of every 0402 and took the silk warnings from 16 to 304.

The last 12 were `'Exclude from bill of materials' settings differ` on
TP1–TP12. Fixed at the source instead of on the board: test pads are bare
copper artwork with no part number and no placement cost
(`jlcpcb-readiness.md` § Through-hole), so `generate_schematic.py` now marks
them out-of-BOM and the footprint attribute matches. Schematic, symbol
library and netlist regenerated; ERC re-run: **0 errors, 1 warning** (the
same MAX98357A EP pin-type warning phase 1 documented).

Six parts still have no LCSC number *in the schematic* — U1, U3, U4, U5, F1,
L1 — exactly the six `jlcpcb-readiness.md` item 5 lists. They are now
visibly missing on the board too, which is the honest state.

### Why each warning stands

| Warning | Count | Why not fixed |
|---|---|---|
| `silk_over_copper` | 11 | Reference designators printed over a pad or via do not transfer. Deleting them makes the board unserviceable for rework; at this density not every refdes can have clear space. A silkscreen tidy-up is on the human list. |
| `silk_overlap` | 5 | Adjacent 0402 designators touch. Legibility only, no electrical effect. |

Refdes text is 0.8 mm rather than KiCad's 1.0 mm default — 1.0 mm does not
fit between 0402s at this density and JLCPCB silkscreens 0.8 mm reliably.
`m_MinSilkTextHeight` was set to match, so this is a recorded decision, not
100 suppressed warnings.

### Two design-rule numbers to confirm with the fab

* **Minimum drill 0.20 mm** — forced by the ESP32-S3-WROOM-1 footprint's own
  EPAD thermal vias. JLCPCB's published minimum via hole is 0.20 mm, so this
  should be standard; confirm at cart time.
* **Hole clearance 0.15 mm** — the tightest real instances are 0.194 mm and
  they are *inside vendor footprints* (GCT USB4105 and Infineon PG-LLGA-5:
  pad to that footprint's own NPTH). Editing a vendor land pattern to
  satisfy a house rule is the worse trade, so the rule matches the vendors.

## The schematic defect this phase found

**The TPS63802's exposed thermal pad had no net.** The `Areg:TPS63802DLA`
symbol written in phase 1 declared pins 1–10 only. The footprint
`Package_SON:Texas_S-PVSON-N10` has an eleventh pad — the 1.65 × 2.40 mm
PowerPAD — which was therefore left floating.

That pad is the main GND return **and** the only heat path for a 2 A
buck-boost. Floating it is a thermal fault and a ground-return fault, not a
cosmetic one; every board of the run would have shipped with it.

ERC could not have caught this: ERC checks pins that exist, and this pin was
never declared. It surfaced only because the layout generator found a pad
with no net to route. Fixed at source in `generate_schematic.py` (pin 11
`PAD`, type `passive`, mapped to GND); `areg-reva.kicad_sch`,
`Areg.kicad_sym` and `areg-reva.net` regenerated; ERC re-run: **0 errors,
1 warning** — the same pre-existing MAX98357A EP pin-type warning phase 1
already documented. No new warning introduced.

### Every exposed / mechanical pad on the board, audited by script

| Part | Exposed pad | Net | Lands on |
|---|---|---|---|
| U1 ESP32-S3-WROOM-1 | pad 41, 3.90 × 3.90 mm | **GND** | F.Cu + the footprint's own 0.20 mm thermal vias into In1 |
| U2 MAX98357A | pad 17, 1.60 × 1.60 mm | **GND** | F.Cu + via in pad |
| U4 TPS63802 | pad 11, 1.65 × 2.40 mm | **GND** | F.Cu + via in pad — *this is the one that was missing* |
| U5 TPD4S014 | pad 11, 0.90 × 1.50 mm | **GND** | F.Cu + via in pad |
| U7 TPS22918 | none — SOT-23-6 has no exposed pad | n/a | n/a |
| U6 USBLC6-2SC6 | none — SOT-23-6 | n/a | n/a |
| U3 ICS-43434 / U8 IM69D130 | none — LGA, ground is a numbered pin | n/a | n/a |

**Exactly one copper pad on the whole board still has no net: `RV1` pad
`MP`** — the Bourns PTV09A's two 2.2 mm plated mounting posts. They are
mechanical anchors, not a thermal pad, and the symbol has no pin for them.
This is a decision, not an oversight: left as-is they are two plated holes
of floating copper. Tying them to GND is the usual choice — it grounds the
pot's metal frame, which is the part a child touches through the knob — but
it also puts GND on enclosure-facing hardware, so it is an owner call.

## Renders — inspected, not merely produced

| File | What it shows |
|---|---|
| `out/render-top.png` | 3D top. Antenna overhanging the left edge with no board under it; USB-C mating face at the bottom edge (body overhanging, as its "PCB Edge" marker requires — a receptacle recessed behind the edge cannot be mated); SD socket opening at the right edge; mic far left, speaker connector far right; no footprint overlaps; nothing crossing the outline. |
| `out/render-bottom.png` | 3D bottom. B.Cu routing, GND pour, stitching-via field, through-hole pads — and **no components**, so single-sided assembly is preserved. |
| `out/layer-fcu.png`, `layer-in1cu.png`, `layer-in2cu.png`, `layer-bcu.png` | 2D per-layer copper drawn from the board file. `kicad-cli pcb render` only ever shows the two outer layers, so these are the only way to see whether the GND and +3V3 planes are solid. |

## What a human must still do before Gerbers

1. **Widen VSYS and VBUS_PROT** from 0.12 mm to 0.5 / 0.4 mm, and
   **route VBUS_F** (4 open pads). These are the current-carrying defects.
2. **Close the other 7 open pads** — USB_DM_C (2), MIC_SCK_I2S (1), U5.3
   GND (1), three F.Cu pour islands. All are inside the three tightest
   packages on the board (0.5 mm-pitch MAX98357A, 0.4 mm-pitch TPD4S014,
   interleaved USB-C D±) or in the mic pocket; an interactive router with
   push-and-shove closes them in minutes.
3. **Widen AMP_OUTN** to match AMP_OUTP (0.25 mm) once its escape is
   hand-routed.
4. **Decide RV1's mounting posts** — GND or floating (above).
5. **Silkscreen tidy-up** so every part can be identified for rework.
6. **Mounting holes: there are none.** Deliberately not invented — their
   positions are an enclosure decision, and arbitrary screws would look
   settled when they are not.
7. **Phase-1 inductor gate still open**: confirm 2.2 µH against the
   TPS63802 datasheet's inductor-selection table at our load (TI's typical
   application is 1.5 µH). BOM line, not topology.
8. **Confirm the two fab numbers** (0.20 mm drill, 0.15 mm hole clearance).
9. **Fill in the six missing LCSC part numbers** (U1, U3, U4, U5, F1, L1) in
   `generate_schematic.py`, then re-run `generate_schematic.py` and
   `fix_metadata.py`. Without them the JLCPCB BOM has six blank lines.
10. **`jlcpcb-readiness.md` blockers B1/B2/B3 are untouched by this phase** —
   the pot is not in JLCPCB's catalogue, TPD4S014 stock covers 5 boards not
   100, and the ICS-43434 is past last-time-buy (the board already carries
   the IM69D130 alternate footprint for that one).

## Files

| File | Role |
|---|---|
| `generate_pcb.py` | placement, outline, stackup, design rules, zones — and the contract assertions; rebuilds the placed board from the netlist |
| `route_pcb.py` | the maze router (A*, per-width clearance model, rip-up-and-reroute) |
| `netlist.py` | shared reader for `areg-reva.net` + `fp-lib-table` |
| `fix_metadata.py` | qualifies footprint ids and copies the LCSC fields from the netlist onto the board (metadata only — never touches copper) |
| `report_pcb.py` | independent read-back of the finished board |
| `plot_layers.py` | per-copper-layer PNGs (dump under KiCad Python, draw under system Python) |
| `areg-reva.kicad_pcb` | the board |
| `out/drc.json`, `out/drc.rpt` | DRC reports |
| `out/route.log` | the routing run this file describes |
| `out/render-*.png`, `out/layer-*.png` | renders |

Regenerate:

```
KiCad10\bin\python.exe generate_pcb.py      # place + assert + zones
KiCad10\bin\python.exe route_pcb.py         # route (~6 min, 4 passes)
KiCad10in\python.exe fix_metadata.py   # ids + LCSC fields back on
kicad-cli pcb drc --refill-zones --save-board -o out/drc.json areg-reva.kicad_pcb
kicad-cli pcb drc --schematic-parity -o out/drc-final.json areg-reva.kicad_pcb
```

**Pass `--schematic-parity` explicitly.** The report's parity section is
present but empty without it, which reads exactly like a clean result.

`generate_pcb.py` **wipes all routing** — it rebuilds from the netlist.
Always run `route_pcb.py` after it.

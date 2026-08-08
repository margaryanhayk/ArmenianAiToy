# Rev-A PCB — Phase 1 (schematic capture) — COMPLETE

Date: 2026-08-08. Contract: `rev-a-design-inputs.md`.

## Verification gate — all three passed

| Gate | Result |
|---|---|
| `kicad-cli sch erc` | **0 errors**, 1 justified warning (below) |
| `kicad-cli sch export netlist` + net spot-check | 89 nets; every load-bearing net present with correct node counts; 13 singletons are exactly the pins explicitly marked no-connect |
| Visual render (SVG → PNG, inspected) | `out/sch-fit.png` — all 12 sections legible, nothing overlapping or cut off |

## The one remaining ERC warning — justified, not fixed

```
[pin_to_pin]: Pins of type Unspecified and Power input are connected
  U2 Pin 17 [PAD, Unspecified]  +  #PWR079 Pin 1 [Power input]
```

U2 is the MAX98357A; pin 17 is its exposed thermal pad, which **must** be
soldered to GND (datasheet: the EP is the thermal and electrical ground
path). KiCad's stock symbol types that pad `Unspecified`, so tying it to a
GND power symbol trips the pin-type matrix. Suppressing it would mean
either mistyping the pad or leaving the EP floating — both worse than the
warning. Left visible deliberately.

## Authoring method

`generate_schematic.py` emits `areg-reva.kicad_sch` from the pin map and
net list. Connectivity is by **global labels + power symbols placed exactly
on pin endpoints** — no wire geometry — which is why the cosmetic `SCALE`
constant can spread the sheet without touching a single net.

The generator **refuses to emit** if any pin of any placed symbol is
neither netted nor explicitly `NC`. That check is the reason the netlist
came out clean on the first ERC-passing run: unconnected pins are a
compile error here, not a review finding.

## Fixes applied during bring-up (each one cost a real debugging round)

1. **Missing custom symbol** — `Areg:TPS22918DBV` was referenced but never
   defined. Created from TI SLVSDV1 (SOT-23-6: 1 VIN, 2 GND, 3 ON, 4 CT,
   5 QOD, 6 VOUT).
2. **201 × `endpoint_off_grid`** — symbol origins were placed on integer
   mm while library pins sit on 1.27 mm multiples, so every pin endpoint
   landed off-grid. Fixed by snapping placements in `place()`.
3. **Power symbols missing from `lib_symbols`** — `power:GND` / `power:+3V3`
   were registered lazily *during* the instance loop, i.e. after the
   library block had already been serialized. KiCad then refuses to load
   the file at all. Now pre-registered.
4. **Sub-symbol renaming — MY BUG, reverted.** Renaming unit sub-symbols to
   `<lib_id>_0_1` looked like the fix for `lib_symbol_issues`; it actually
   makes KiCad reject the file outright. Sub-units keep their bare
   `<name>_0_1` names. The real cause of those warnings was (3) + the
   missing library tables.
5. **No library tables** — this KiCad is an *extracted* install (no admin
   rights on this machine), so `KICAD10_*_DIR` are unset and every
   footprint link failed. Project-local `sym-lib-table` / `fp-lib-table`
   now point at the extracted tree; `Areg.kicad_sym` is emitted by the
   generator itself so the on-disk library can never drift from the
   definitions embedded in the schematic.
6. **Two wrong footprint names** — `WSON-10-1EP_2x3mm...` → the real
   `Texas_DSQ0010A_WSON-10-1EP_2x2mm_P0.4mm_EP0.9x1.5mm` (TPD4S014**DSQ**R),
   and an invented inductor footprint → the stock
   `L_TDK_VLS6045EX_VLS6045AF`, which is exactly our part family.
7. **`VBUS_F` undriven** — the net between the PTC (passive) and the
   TPD4S014's VBUS power-inputs had no driver. Added `#FLG05` PWR_FLAG.

## Deviation from the contract, recorded

The predecessor draft specified a 0.47 µH Coilcraft XFL4015 inductor; the
contract's audited part is the **TDK VLS6045EX-2R2N (2.2 µH / 5.1 A)**, and
that is what is now in the schematic. **PHASE-2 GATE:** confirm 2.2 µH
against the TPS63802 datasheet inductor-selection table at our load before
fab — TI's typical application is 1.5 µH. If the datasheet disagrees, the
BOM line changes, not the schematic topology.

## Files

| File | Role |
|---|---|
| `generate_schematic.py` | source of truth — regenerates everything |
| `areg-reva.kicad_sch` | the schematic (105 symbols) |
| `Areg.kicad_sym` | project symbol library (TPS63802DLA, TPS22918DBV) |
| `areg-reva.kicad_pro`, `sym-lib-table`, `fp-lib-table` | project + library config |
| `areg-reva.net` | exported netlist (89 nets) |
| `erc.rpt` | ERC report |
| `out/areg-reva.svg`, `out/sch-fit.png` | human-readable renders |

## Next — Phase 2 (board layout)

Placement + routing per the contract's layout rules (antenna keep-out, star
ground, decoupling distances), DRC + JLCPCB DFM, then Gerbers/BOM/CPL.
Nothing is ordered without the owner seeing a board render first.

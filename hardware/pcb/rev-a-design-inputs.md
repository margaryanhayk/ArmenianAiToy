# Rev-A PCB — design inputs (the layout contract)

Started 2026-08-08 on owner approval ("Yes" to step 2). This file is the
single authoritative input for the KiCad capture + layout. It merges
`docs/hardware/schematic-spec.md` (net-by-net, pin map) with every
overnight-audit correction (`audit-mcu.md`, `audit-components.md`,
`buy-links.md`). Where this file and older docs disagree, THIS file wins.

## Fixed decisions (from the audits — do not re-litigate in layout)

| Decision | Value | Source |
|---|---|---|
| Module | ESP32-S3-WROOM-1-**N8R8**, PCB antenna | audit-mcu §2 |
| MAIN button GPIO | **18** (GPIO0 = 10 kΩ pull-up + factory test pad only) | task #30, spec §1 |
| Mic strategy | **DUAL FOOTPRINT**: ICS-43434 (I2S, run-1 bridge stock) AND Infineon IM69D130 (PDM, production). Only one populated per build. PDM CLK/DATA share GPIO4/6 wiring via DNP 0 Ω selects | audit-components §3 |
| PTC | Littelfuse **1206L075** | audit-components §2 |
| Buttons | Omron **B3F-1002** (160 gf) ×3 | buy-links flag 8 |
| Volume pot | Bourns **PTV09A-4220F-B103** (detented) | buy-links flag 7 |
| SD socket | **Hirose DM3AT-SF-PEJM5** (push-push, active; Molex 5031821852 is discontinued). Spec §6 asked push-pull — overridden by availability; the socket sits behind a screwed enclosure so eject style is minor | buy-links flag 5 |
| Buck-boost inductor | TDK **VLS6045EX-2R2N** | buy-links flag 6 |
| Status LED | **3 discrete 0603 LEDs** + resistors (no WS2812B, no SN74LVC1T45 on battery build) | audit-components §8 |
| Speaker connector | 2-pin JST-PH keyed — board must accept ANY 8 Ω driver; speaker choice gated on M10 bench SPL test | audit-components §5 |
| Battery | 3×AA holder off-board via keyed 2-pin JST-PH + DMG2301L reverse block. Li-ion (BQ24074/MAX17048/NTC) is batch-2 — leave an UNPOPULATED charger section only if it does not grow the board; else omit | power-tree |

## Pin map (final, from spec §1 — all constraints re-verified in audit-mcu §3)

GPIO4/5/6 mic (I2S BCK/WS/SD; PDM alt via 0Ω), 15/16/7 amp BCK/LRC/DIN,
17 amp SD_MODE, 10/11/12/13 SD-SPI, 9 SD card-detect, 8 volume (ADC1),
18 MAIN, 21 YES, 47 NO, 48 LED, 19/20 USB D∓, straps 0/3/45/46 pulled
per spec §2, spares 1/2/14/38 → test pads, TXD0/RXD0 (43/44) → UART
test pads (audit-mcu gap fix), GPIO39-42 JTAG → test pads if room.

Layout note (audit-mcu §3): GPIO15/16 are XTAL_32K pins — amp clocks
there forecloses a future RTC crystal; accepted for rev A, recorded.

## Layout rules (from audit-mcu §5 + spec)

1. Antenna keep-out: module antenna end overhangs board edge or sits over
   a copper-free zone ALL layers; no battery/speaker magnet/metal within
   the keep-out; pull Espressif Hardware Design Guidelines for the mm
   figures before final placement.
2. Star ground: speaker return and mic ground meet only at the star
   point; class-D loop (amp → speaker connector) short and away from mic.
3. Decoupling placement distances are REQUIREMENTS (spec §2): 22µF+100nF
   ≤3 mm from module pin 2; amp 100nF ≤2 mm, 22µF ≤5 mm, 330µF ≤15 mm;
   SD caps AT the socket.
4. Mic: bottom-port hole in board, gasket ring keep-out on solder side,
   ≥40 mm from speaker connector, opposite board edge.
5. 4-layer, ~60×50 mm target, ENIG (JLCPCB assembled; impedance control
   not required at these speeds).
6. Ferrite + 1nF C0G footprints on each speaker leg (DNP allowed).
7. All JLCPCB-assembly parts chosen from LCSC catalog (basic parts
   preferred); BOM carries LCSC "C" numbers (start from buy-links.md).

## Deliverables

1. `areg-reva.kicad_pro/.kicad_sch/.kicad_pcb` in this directory
2. DRC-clean + JLCPCB DFM-clean
3. `fab/` — Gerbers, drill, BOM.csv (LCSC PNs), CPL (pick-and-place)
4. A rendered board preview PNG the owner can look at

## Status log

- 2026-08-08: project started; KiCad 10 installing; this contract written.
- 2026-08-08: **Phase 1 (schematic capture) COMPLETE** — ERC 0 errors,
  netlist verified (89 nets), render inspected. See `phase1-status.md`.
  Two contract items resolved during capture: SD socket footprint uses the
  Hirose DM3AT stock footprint; inductor is the contract's VLS6045EX-2R2N
  with a phase-2 datasheet gate on the 2.2 µH value.

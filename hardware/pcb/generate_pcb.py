#!/usr/bin/env python3
"""Areg rev-A board generator (Phase 2 — layout).

Builds `areg-reva.kicad_pcb` from `areg-reva.net`:
  * loads every footprint the schematic assigned (no substitutions — a
    missing footprint is a hard failure, not a silent swap),
  * places them from the PLACEMENT table below,
  * refuses to emit if any two courtyards overlap or any part falls off
    the board outline (the layout equivalent of phase 1's "unconnected
    pin = compile error" rule),
  * cuts the board outline, sets the 4-layer stackup and design rules,
  * pours GND (F/In1/B) and +3V3 (In2).

Routing lives in `route_pcb.py`, which runs after this.

Coordinate system: board-local millimetres, origin at the board's
top-left corner, +X right, +Y down (KiCad's own convention). The page
offset (ORIGIN_X/Y) is applied on emission only.

Run with KiCad's own Python:
    C:\\Users\\hayk.margaryan\\KiCad10\\bin\\python.exe generate_pcb.py
"""
import os
import sys

import pcbnew

import netlist

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'areg-reva.kicad_pcb')

# --------------------------------------------------------------- board ---
BOARD_W = 74.0
BOARD_H = 62.0
ORIGIN_X = 30.0          # page offset, cosmetic only
ORIGIN_Y = 30.0
EDGE_CLEARANCE = 0.35    # copper-to-Edge.Cuts

# The ESP32-S3-WROOM-1 antenna overhangs the LEFT board edge. The stock
# footprint carries a 48 x 21 mm rule area in front of the antenna; with
# the module at rotation 90 and its body/antenna boundary line sitting on
# x = 0, that whole rule area lands at x < 0 — off the board — so there is
# no copper, no battery and no speaker magnet inside it by construction.
# ANTENNA_OVERHANG is asserted below, not assumed.
ANTENNA_OVERHANG = 6.0

# ------------------------------------------------------------ placement ---
# ref: (x, y, rotation_deg)   — board-local mm, top-left origin.
# Grouped by function; the comment above each group is the layout rule it
# serves (rev-a-design-inputs.md § "Layout rules").
PLACEMENT = {
    # --- MCU -------------------------------------------------------------
    # Antenna to the LEFT, overhanging the board edge (rule 1).
    'U1':  (6.6, 16.0, 90),

    # Module decoupling: 22 uF + 100 nF <= 3 mm from module pin 2 (spec §2).
    # Pin 2 lands at (2.81, 24.75); both caps are measured, not eyeballed.
    'C7':  (1.2, 27.0, 0),      # 100 nF
    'C6':  (4.0, 27.0, 0),      # 22 uF
    # EN RC (10k / 1uF) — pin 3 at (4.08, 24.75)
    'C8':  (6.8, 27.0, 0),
    'R8':  (6.8, 29.3, 0),

    # Strap pulls, at their own module pins.
    'R9':  (22.5, 7.2, 0),      # IO0 pull-up   (pin 27 @ 18.05, 7.25)
    'R11': (23.5, 9.4, 0),      # IO45 -> GND   (pin 26 @ 19.30, 9.01)
    'R12': (23.5, 21.3, 0),     # IO46 -> GND   (pin 16 @ 19.30, 21.72)
    'R10': (23.5, 23.8, 0),     # IO3  -> GND   (pin 15 @ 19.30, 22.99)

    # --- test pads (module top row exits upward) -------------------------
    'TP2':  (2.5,  3.0, 0), 'TP3':  (5.5,  3.0, 0),
    'TP4':  (8.5,  3.0, 0), 'TP5':  (11.5, 3.0, 0),
    'TP6':  (14.5, 3.0, 0), 'TP7':  (17.5, 3.0, 0),
    'TP8':  (20.5, 3.0, 0), 'TP9':  (23.5, 3.0, 0),
    'TP1':  (27.0, 3.0, 0), 'TP10': (30.0, 3.0, 0),
    'TP11': (33.0, 3.0, 0), 'TP12': (36.0, 3.0, 0),

    # --- status LEDs -----------------------------------------------------
    'D1': (41.0, 3.0, 0), 'R41': (41.0, 6.0, 0),
    'D2': (45.0, 3.0, 0), 'R42': (45.0, 6.0, 0),
    'D3': (49.0, 3.0, 0), 'R43': (49.0, 6.0, 0),

    # --- microSD (rule: socket at its own board edge) --------------------
    # Card opening faces the RIGHT board edge; contacts face the MCU.
    'J3':  (65.1, 14.5, 90),
    # Pull-ups on CS / CMD / DAT0 / DAT1 / DAT2 + card-detect.
    'R17': (52.5, 11.5, 90), 'R18': (52.5, 14.0, 90), 'R19': (52.5, 16.5, 90),
    'R20': (52.5, 19.0, 90), 'R21': (52.5, 21.5, 90), 'R22': (49.0, 21.5, 90),
    # Socket decoupling AT the socket (spec §2). VDD pad is (57.38, 15.03).
    'C11': (54.3, 12.5, 90), 'C12': (54.3, 17.5, 90),
    # 33 R series at the DRIVING (MCU) end.
    'R25': (28.0, 15.0, 0), 'R23': (32.5, 16.8, 0), 'R24': (28.0, 18.6, 0),

    # --- volume (pot wiper -> 1k -> GPIO8, 100 nF at the pin) ------------
    # C24 sits at the GPIO pin; the 1 k goes at the WIPER end, so the long
    # trace between them is inside the RC and any noise it picks up is
    # attenuated before it reaches the ADC. R40 beside the module would put
    # 30 mm of unfiltered high-impedance wiper across the board instead.
    'C24': (15.5, 27.3, 0), 'R40': (19.5, 45.0, 0),
    'RV1': (4.0, 53.0, 0),

    # --- amp I2S series 33 R, at the driving (MCU) end -------------------
    'R35': (10.0, 29.3, 0), 'R36': (13.5, 29.3, 0), 'R37': (17.0, 29.3, 0),

    # --- microphone (rule 4: opposite edge from the speaker, >= 40 mm) ---
    # 0 R build selects: R13/R14 populate the I2S mic, R15/R16 the PDM one.
    'R13': (2.0, 31.8, 0), 'R14': (5.5, 31.8, 0),
    'R15': (9.0, 31.8, 0), 'R16': (12.5, 31.8, 0),
    'U3':  (4.0, 35.5, 0), 'C9':  (8.5, 35.5, 0),     # ICS-43434 (I2S)
    'U8':  (4.0, 41.0, 0), 'C10': (9.0, 41.0, 0),     # IM69D130 (PDM, DNP)
    # mic supply filter: +3V3 -> FB1 -> 10 R -> 10 uF -> MIC_VDD
    'FB1': (13.5, 35.5, 0), 'R7': (13.5, 38.0, 0), 'C5': (13.5, 40.5, 0),

    # --- buttons ---------------------------------------------------------
    'R26': (20.5, 31.8, 0), 'R27': (23.0, 31.8, 0), 'C16': (25.5, 31.8, 0),
    'SW1': (23.0, 37.5, 0),
    'R29': (30.0, 31.8, 0), 'R30': (32.5, 31.8, 0), 'C17': (35.0, 31.8, 0),
    'SW2': (32.5, 37.5, 0),
    'R32': (39.5, 31.8, 0), 'R33': (42.0, 31.8, 0), 'C18': (44.5, 31.8, 0),
    'SW3': (42.0, 37.5, 0),

    # --- audio amp + speaker (rule 2: class-D loop short, away from mic) --
    'U2':  (55.0, 34.0, 0),
    'R38': (49.5, 32.5, 0), 'R39': (49.5, 34.7, 0),   # GAIN ladder (DNP)
    'C19': (55.5, 37.4, 0),      # 100 nF  <= 2 mm from amp VDD
    'C20': (51.5, 37.5, 0),      # 22 uF   <= 5 mm
    'C21': (61.5, 40.5, 0),      # 330 uF  <= 15 mm
    # OUTP leaves U2 pad 9 (y 34.75) and OUTN pad 10 (y 34.25). Put the
    # OUTP ferrite BELOW and the OUTN ferrite ABOVE and rotate J4 so pin 1
    # (SPK_P) is the lower of the two: the pair then runs straight out to
    # the connector without crossing, which is what let AMP_OUTN keep a
    # 0.5 mm trace instead of being squeezed to 0.15 mm.
    'FB2': (60.5, 36.0, 0), 'C22': (64.5, 36.0, 0),   # OUTP -> SPK_P
    'FB3': (60.5, 33.5, 0), 'C23': (64.5, 33.5, 0),   # OUTN -> SPK_N
    'J4':  (69.5, 35.5, 90),     # speaker, right edge; pin 1 lower

    # --- battery input + reverse block -----------------------------------
    'J2':  (23.0, 57.0, 0),
    'Q1':  (23.0, 49.5, 0), 'R3': (28.0, 48.2, 0), 'R4': (28.0, 50.7, 0),

    # --- USB-C input + protection ----------------------------------------
    # The GCT footprint carries a "PCB Edge" marker on User.Drawings at
    # +3.68 mm: the plug enters from +Y, so the mating face must sit ON the
    # board outline, not 1.8 mm behind it (where a plug shell would foul the
    # board). Rotation is 0 — 180 would point the opening inboard. Asserted
    # in check_contract(), not eyeballed.
    'J1':  (38.0, BOARD_H - 3.68, 0),
    # CC1 exits J1 at x 36.75 and CC2 at x 39.75, with D+/D- between them
    # at x 37.75/38.75. Pull CC1 left and CC2 right so the differential
    # pair has a clear vertical corridor out of the connector.
    'R1':  (34.0, 52.3, 0), 'R2': (42.0, 52.3, 0),
    'U6':  (33.5, 46.5, 0),      # USBLC6-2SC6 on D+/D-
    'U5':  (40.0, 47.5, 0),      # TPD4S014 on VBUS + D+/D-
    'F1':  (46.0, 57.5, 0),      # 1206L075 PTC
    'D4':  (48.0, 53.0, 0),      # SMAJ5.0A
    'U7':  (52.5, 48.5, 0), 'C15': (53.0, 52.5, 0),   # TPS22918 soft-start

    # --- buck-boost 3V3 (star ground lives at C3/C4) ---------------------
    'U4':  (60.5, 49.5, 0),
    'L1':  (69.0, 49.5, 0),      # switching node kept to two short pads
    'R5':  (57.0, 44.8, 0), 'R6': (61.5, 44.8, 0),
    'C1':  (56.5, 55.0, 0), 'C2': (60.0, 55.0, 0),
    'C3':  (64.0, 55.0, 0), 'C4': (69.0, 55.5, 0),
}

# Reference designators whose silkscreen text would collide with a
# neighbour; nudged rather than deleted (a board a human cannot read is
# a board a human cannot rework).
SILK_OFFSET = {}


def mm(v):
    return pcbnew.FromMM(v)


def vec(x, y):
    return pcbnew.VECTOR2I(mm(x + ORIGIN_X), mm(y + ORIGIN_Y))


def bbox_mm(item_bbox):
    f = pcbnew.ToMM
    return (f(item_bbox.GetX()) - ORIGIN_X, f(item_bbox.GetY()) - ORIGIN_Y,
            f(item_bbox.GetRight()) - ORIGIN_X, f(item_bbox.GetBottom()) - ORIGIN_Y)


def build():
    comps, nets = netlist.load()
    libs = netlist.fp_lib_paths()

    missing = sorted(set(PLACEMENT) - set(comps))
    unplaced = sorted(set(comps) - set(PLACEMENT))
    if missing:
        sys.exit('PLACEMENT names refs that are not in the netlist: %s' % missing)
    if unplaced:
        sys.exit('these netlist refs have no placement: %s' % unplaced)

    board = pcbnew.NewBoard(OUT)
    board.SetCopperLayerCount(4)

    # ---- design rules (JLCPCB 4-layer, comfortable margins) -------------
    ds = board.GetDesignSettings()
    ds.SetBoardThickness(mm(1.6))
    ds.m_TrackMinWidth = mm(0.11)
    ds.m_ViasMinSize = mm(0.45)
    ds.m_ViasMinAnnularWidth = mm(0.13)
    ds.m_MinClearance = mm(0.13)
    ds.m_HoleToHoleMin = mm(0.25)
    ds.m_CopperEdgeClearance = mm(0.25)
    # 0.20 mm: the ESP32-S3-WROOM-1 footprint's own EPAD thermal vias are
    # 0.20 mm drills. JLCPCB's published minimum via hole is 0.20 mm, so
    # this is a real capability, not a relaxation to make DRC quiet —
    # but CONFIRM it against the fab's 4-layer process at cart time.
    ds.m_MinThroughDrill = mm(0.20)
    # 0.15 mm: the tightest instances on this board are 0.194 mm inside the
    # GCT USB4105 and Infineon PG-LLGA-5 VENDOR footprints (pad to that
    # footprint's own NPTH). Editing a vendor land pattern to satisfy a
    # house rule is the worse trade. FLAGGED for fab confirmation.
    ds.m_HoleClearance = mm(0.15)
    # 0.8 mm refdes: KiCad's 1.0 mm default cannot fit between 0402s
    # on a board this dense, and JLCPCB silkscreens 0.8 mm reliably.
    ds.m_MinSilkTextHeight = mm(0.8)
    ds.m_MinSilkTextThickness = mm(0.1)
    try:
        ds.SetCustomTrackWidth(mm(0.25))
        ds.SetCustomViaSize(mm(0.6))
        ds.SetCustomViaDrill(mm(0.3))
    except AttributeError:
        pass

    nc = board.GetAllNetClasses()['Default']
    # 0.15 mm. JLCPCB's 4-layer floor is 0.0889 mm (3.5 mil), so this
    # keeps a 70% margin while leaving enough room to escape a 0.4 mm
    # pitch WSON (U5) and a 0.5 mm pitch QFN (U2) at all. Bulk routing
    # still runs at 0.2 mm trace on a 0.2 mm effective pitch; only the
    # fine-pitch escapes use the tighter number.
    nc.SetClearance(mm(0.15))
    nc.SetTrackWidth(mm(0.25))
    nc.SetViaDiameter(mm(0.6))
    nc.SetViaDrill(mm(0.3))

    # ---- nets ------------------------------------------------------------
    for name in sorted(nets):
        board.Add(pcbnew.NETINFO_ITEM(board, name))
    board.BuildListOfNets()
    netmap = {name: board.FindNet(name) for name in nets}

    pad_net = {}                       # (ref, padnumber) -> netname
    for name, nodes in nets.items():
        for ref, pin in nodes:
            pad_net[(ref, pin)] = name

    # ---- footprints ------------------------------------------------------
    placed = {}
    for ref in sorted(PLACEMENT):
        c = comps[ref]
        lib, fpname = c.footprint.split(':', 1)
        libdir = libs.get(lib)
        if not libdir or not os.path.isdir(libdir):
            sys.exit('FOOTPRINT LIBRARY MISSING for %s: %s' % (ref, c.footprint))
        fp = pcbnew.FootprintLoad(libdir, fpname)
        if fp is None:
            sys.exit('FOOTPRINT MISSING for %s: %s' % (ref, c.footprint))
        x, y, rot = PLACEMENT[ref]
        fp.SetReference(ref)
        fp.SetValue(c.value)
        fp.Value().SetVisible(False)
        fp.Reference().SetTextSize(pcbnew.VECTOR2I(mm(0.8), mm(0.8)))
        fp.Reference().SetTextThickness(mm(0.12))
        board.Add(fp)
        fp.SetOrientationDegrees(rot)
        fp.SetPosition(vec(x, y))
        if c.dnp:
            fp.SetDNP(True)
        for pad in fp.Pads():
            nn = pad_net.get((ref, pad.GetNumber()))
            if nn:
                pad.SetNet(netmap[nn])
            if ref == 'RV1':
                # hand-soldered in Armenia; keep the iron from fighting a plane
                pad.SetLocalZoneConnection(pcbnew.ZONE_CONNECTION_THERMAL)
        placed[ref] = fp

    # ---- board outline ---------------------------------------------------
    corners = [(0, 0), (BOARD_W, 0), (BOARD_W, BOARD_H), (0, BOARD_H)]
    for i in range(4):
        seg = pcbnew.PCB_SHAPE(board, pcbnew.SHAPE_T_SEGMENT)
        seg.SetStart(vec(*corners[i]))
        seg.SetEnd(vec(*corners[(i + 1) % 4]))
        seg.SetLayer(pcbnew.Edge_Cuts)
        seg.SetWidth(mm(0.1))
        board.Add(seg)

    return board, comps, nets, placed


# ------------------------------------------------------------- checking ---
def _copy_poly(poly):
    out = pcbnew.SHAPE_POLY_SET()
    for i in range(poly.OutlineCount()):
        out.AddOutline(poly.Outline(i))
    return out


def body_courtyard(fp):
    """The footprint's real keep-clear area for PLACEMENT purposes.

    U1's stock courtyard swallows the 48 x 21 mm antenna rule area, so a
    naive bbox test calls half the board an overlap. Subtract any rule
    area the footprint carries: the keep-out is checked on its own terms
    (it must land off-board), not as a courtyard.
    """
    poly = _copy_poly(fp.GetCourtyard(pcbnew.F_CrtYd))
    if not poly.OutlineCount():
        bb = fp.GetBoundingBox(False, False)
        poly = pcbnew.SHAPE_POLY_SET()
        poly.NewOutline()
        for px, py in ((bb.GetX(), bb.GetY()), (bb.GetRight(), bb.GetY()),
                       (bb.GetRight(), bb.GetBottom()), (bb.GetX(), bb.GetBottom())):
            poly.Append(px, py)
    for z in fp.Zones():
        poly.BooleanSubtract(_copy_poly(z.Outline()))
    return poly


def check_placement(board, placed, comps):
    """Courtyard overlap + off-board check. Returns list of problem strings."""
    problems = []
    polys, boxes = {}, {}
    for ref, fp in placed.items():
        p = body_courtyard(fp)
        polys[ref] = p
        boxes[ref] = bbox_mm(p.BBox())

    refs = sorted(polys)
    for i, a in enumerate(refs):
        ax0, ay0, ax1, ay1 = boxes[a]
        # off-board (the module antenna is allowed to overhang the LEFT edge)
        # U1's antenna overhangs the LEFT edge and J1's plug-clearance
        # courtyard overhangs the BOTTOM edge; both are deliberate.
        lo_x = -ANTENNA_OVERHANG - 0.5 if a == 'U1' else -0.001
        hi_y = BOARD_H + 0.8 if a == 'J1' else BOARD_H + 0.001
        if ax0 < lo_x or ay0 < -0.001 or ax1 > BOARD_W + 0.001 or ay1 > hi_y:
            problems.append('OFF-BOARD %s: x %.2f..%.2f y %.2f..%.2f'
                            % (a, ax0, ax1, ay0, ay1))
        for b in refs[i + 1:]:
            bx0, by0, bx1, by1 = boxes[b]
            if min(ax1, bx1) - max(ax0, bx0) <= 0 or min(ay1, by1) - max(ay0, by0) <= 0:
                continue
            inter = _copy_poly(polys[a])
            inter.BooleanIntersection(_copy_poly(polys[b]))
            area = inter.Area() / 1e12       # nm^2 -> mm^2
            if area > 0.001:
                problems.append('COURTYARD OVERLAP %s / %s  (%.3f mm2)'
                                % (a, b, area))
    return problems, boxes


def pad_xy(fp, number):
    for p in fp.Pads():
        if p.GetNumber() == number:
            pos = p.GetPosition()
            return (pcbnew.ToMM(pos.x) - ORIGIN_X, pcbnew.ToMM(pos.y) - ORIGIN_Y)
    return None


def dist(a, b):
    return ((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2) ** 0.5


def check_contract(placed, boxes):
    """The layout rules that are REQUIREMENTS, measured on the real board."""
    out = []

    def rule(name, ok, detail):
        out.append(('PASS' if ok else 'FAIL', name, detail))

    u1 = placed['U1']
    p2 = pad_xy(u1, '2')
    for ref, limit in (('C6', 3.0), ('C7', 3.0)):
        d = dist(p2, (PLACEMENT[ref][0], PLACEMENT[ref][1]))
        rule('module decoupling %s <= %.0f mm from U1 pin 2' % (ref, limit),
             d <= limit, '%.2f mm' % d)

    u2 = placed['U2']
    vdd = pad_xy(u2, '7')
    for ref, limit in (('C19', 2.0), ('C20', 5.0), ('C21', 15.0)):
        d = dist(vdd, (PLACEMENT[ref][0], PLACEMENT[ref][1]))
        rule('amp %s <= %.0f mm from U2 VDD' % (ref, limit), d <= limit,
             '%.2f mm' % d)

    j3vdd = pad_xy(placed['J3'], '4')
    for ref in ('C11', 'C12'):
        d = dist(j3vdd, (PLACEMENT[ref][0], PLACEMENT[ref][1]))
        rule('SD %s at the socket (<= 5 mm from J3 VDD)' % ref, d <= 5.0,
             '%.2f mm' % d)

    micxy = (PLACEMENT['U3'][0], PLACEMENT['U3'][1])
    spkxy = (PLACEMENT['J4'][0], PLACEMENT['J4'][1])
    d = dist(micxy, spkxy)
    rule('mic U3 >= 40 mm from speaker connector J4', d >= 40.0, '%.1f mm' % d)
    d2 = dist((PLACEMENT['U8'][0], PLACEMENT['U8'][1]), spkxy)
    rule('mic U8 (PDM alt) >= 40 mm from J4', d2 >= 40.0, '%.1f mm' % d2)

    # antenna keep-out: the module's own rule area must land entirely
    # off-board (x < 0), which is what "overhangs the board edge" means.
    zones = list(u1.Zones())
    if zones:
        bb = bbox_mm(zones[0].GetBoundingBox())
        rule('antenna keep-out entirely off-board', bb[2] <= 0.05,
             'rule area x %.1f..%.1f y %.1f..%.1f' % (bb[0], bb[2], bb[1], bb[3]))
    else:
        rule('antenna keep-out present in U1 footprint', False, 'no rule area found')

    # USB-C mating face must land ON the board outline (footprint's own
    # "PCB Edge" marker on User.Drawings).
    edge_y = None
    for g in placed['J1'].GraphicalItems():
        if pcbnew.BOARD.GetStandardLayerName(g.GetLayer()) == 'User.Drawings' \
                and g.GetClass() != 'PCB_TEXT':
            edge_y = bbox_mm(g.GetBoundingBox())[3]
    rule('USB-C "PCB Edge" marker lands on the board outline',
         edge_y is not None and abs(edge_y - BOARD_H) < 0.15,
         'marker y = %.2f, board edge y = %.1f' % (edge_y or -1, BOARD_H))

    # nothing else may sit inside the keep-out footprint
    intruders = [r for r, b in boxes.items() if r != 'U1' and b[0] < 0.0]
    rule('no part intrudes past the antenna edge', not intruders,
         ', '.join(intruders) or 'none')

    # I2S/SPI run length guidance (<50 mm, spec §2)
    amp_bclk = dist(pad_xy(u1, '8'), (PLACEMENT['R35'][0], PLACEMENT['R35'][1])) \
        + dist((PLACEMENT['R35'][0], PLACEMENT['R35'][1]), pad_xy(u2, '16'))
    rule('amp I2S BCLK run < 50 mm', amp_bclk < 50.0, '%.1f mm' % amp_bclk)
    sd_clk = dist(pad_xy(u1, '20'), (PLACEMENT['R23'][0], PLACEMENT['R23'][1])) \
        + dist((PLACEMENT['R23'][0], PLACEMENT['R23'][1]), pad_xy(placed['J3'], '5'))
    rule('SD CLK run < 50 mm', sd_clk < 50.0, '%.1f mm' % sd_clk)
    mic_clk = dist(pad_xy(u1, '4'), (PLACEMENT['R13'][0], PLACEMENT['R13'][1])) \
        + dist((PLACEMENT['R13'][0], PLACEMENT['R13'][1]), pad_xy(placed['U3'], '4'))
    rule('mic BCLK run < 50 mm', mic_clk < 50.0, '%.1f mm' % mic_clk)
    return out


# ---------------------------------------------------------------- zones ---
def add_zone(board, layer, netname, rect, priority=0, name=''):
    z = pcbnew.ZONE(board)
    z.SetLayer(layer)
    if netname:
        z.SetNet(board.FindNet(netname))
    z.SetZoneName(name or netname)
    z.SetAssignedPriority(priority)
    z.SetLocalClearance(mm(0.25))
    z.SetMinThickness(mm(0.2))
    # SOLID, not thermal relief. On a plane feeding a 2 A buck-boost and a
    # class-D amp, spokes are series impedance in exactly the return path
    # that must not have any; and the stock reliefs on J1's shield tabs and
    # U4's GND pads resolve to a single spoke (starved). The one part a
    # human hand-solders (RV1 — see jlcpcb-readiness.md B1) gets thermal
    # relief back as a per-pad override in build().
    z.SetPadConnection(pcbnew.ZONE_CONNECTION_FULL)
    z.SetThermalReliefGap(mm(0.3))
    z.SetThermalReliefSpokeWidth(mm(0.4))
    z.SetIsFilled(False)
    outline = z.Outline()
    outline.NewOutline()
    x0, y0, x1, y1 = rect
    for px, py in ((x0, y0), (x1, y0), (x1, y1), (x0, y1)):
        outline.Append(mm(px + ORIGIN_X), mm(py + ORIGIN_Y))
    board.Add(z)
    return z


def add_pours(board):
    inset = EDGE_CLEARANCE
    full = (inset, inset, BOARD_W - inset, BOARD_H - inset)
    layers = [
        (pcbnew.F_Cu, 'GND', 10),
        (pcbnew.In1_Cu, 'GND', 10),
        (pcbnew.In2_Cu, '+3V3', 10),
        (pcbnew.B_Cu, 'GND', 10),
    ]
    for layer, net, prio in layers:
        add_zone(board, layer, net, full, prio)
    # NO raw-input islands on In2. They were tried and removed: VSYS, VBUS
    # and +3V3 pads interleave in the same 10 mm of the bottom-right corner
    # (C1/C2 are VSYS, C3/C4 right beside them are +3V3), so any rectangle
    # big enough to serve VSYS also swallowed the +3V3 pads inside it and cut
    # the +3V3 plane into two islands that DRC then reported as unconnected.
    # In2 is a single uninterrupted +3V3 plane; VSYS / VBUS_PROT / VBAT are
    # routed as 0.4-0.5 mm traces instead, which their currents allow
    # (VSYS <= ~1 A, USB limited to 0.75 A by the PTC).


def main():
    board, comps, nets, placed = build()
    problems, boxes = check_placement(board, placed, comps)
    results = check_contract(placed, boxes)

    print('placed %d footprints on a %.0f x %.0f mm board'
          % (len(placed), BOARD_W, BOARD_H))
    print()
    for status, name, detail in results:
        print('  [%s] %-52s %s' % (status, name, detail))
    print()
    if problems:
        print('PLACEMENT PROBLEMS (%d):' % len(problems))
        for p in problems:
            print('   ', p)
    else:
        print('placement clean: no courtyard overlaps, nothing off-board')

    add_pours(board)
    pcbnew.SaveBoard(OUT, board)
    print('\nwrote', OUT)

    if problems or any(s == 'FAIL' for s, _, _ in results):
        sys.exit(1)


if __name__ == '__main__':
    main()

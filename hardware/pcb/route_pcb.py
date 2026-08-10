#!/usr/bin/env python3
"""Areg rev-A board router (Phase 2).

KiCad ships no autorouter, so this is one: a grid maze router (A* with
octile moves and a via cost) over the two signal layers of the board
`generate_pcb.py` produced. In1.Cu is a solid GND plane and In2.Cu
carries +3V3 / VSYS / VBUS_PROT / VBAT planes, so those nets are joined
by dropping a via beside each pad rather than by drawing traces.

What it does NOT do: rip-up-and-retry, push-and-shove, length matching,
differential-pair coupling, or teardrops. Anything it cannot route is
left as ratsnest and REPORTED — a silent failure here would be a board
that looks finished and is not.

Run with KiCad's own Python, after generate_pcb.py:
    C:\\Users\\hayk.margaryan\\KiCad10\\bin\\python.exe route_pcb.py
"""
import heapq
import math
import os
import sys

import numpy as np
import pcbnew

import generate_pcb as G

HERE = os.path.dirname(os.path.abspath(__file__))
BOARD_FILE = G.OUT

# ------------------------------------------------------------ geometry ---
GRID = 0.1                      # mm per cell
NX = int(round(G.BOARD_W / GRID)) + 1
NY = int(round(G.BOARD_H / GRID)) + 1
LAYERS = [pcbnew.F_Cu, pcbnew.B_Cu]

CLEAR = 0.15                    # mm, copper-to-copper — matches the netclass
FINE_CLEAR = 0.15               # clearance never relaxes; the TRACE narrows
TW_SIGNAL = 0.2
TW_FINE = 0.12                  # >= JLCPCB's 0.0889 mm 4-layer floor
TW_POWER = 0.5
VIA_D = 0.6
VIA_DRILL = 0.3

# Nets whose connectivity is provided by a plane, not by traces.
PLANE_NETS = {
    'GND': pcbnew.In1_Cu,
    '+3V3': pcbnew.In2_Cu,
}

# Nets that carry real current, with the width each one can actually have.
#
# The ceiling is the PAD, not the trace rule: TPS63802 (DLA) pins are 0.28 mm
# wide and MAX98357A (TQFN) pins 0.25 mm, so no trace leaving them can be
# wider than that however much copper is free further out. Capacity, IPC-2221
# external, 35 um copper, 10 C rise:
#   0.50 mm -> 1.4 A    0.40 mm -> 1.1 A    0.30 mm -> 0.72 A
#   0.25 mm -> 0.61 A   0.20 mm -> 0.44 A
# Against the loads: speaker peak 0.41 A at 3.3 V into 8 R; USB limited by the
# 1206L075 PTC (0.75 A hold / 1.5 A trip); TPS63802 inductor peak ~1.2-1.5 A
# for tens of microseconds, on a run of ~2 mm.
NET_WIDTH = {
    'SW1': 0.30, 'SW2': 0.30,         # buck switching node -> L1 (pad-limited)
    'SPK_P': 0.50, 'SPK_N': 0.50,     # speaker legs, ferrite -> connector
    'AMP_OUTP': 0.25, 'AMP_OUTN': 0.25,   # class-D out (TQFN pad-limited)
    'VBUS_C': 0.40, 'VBUS_F': 0.40,   # USB input, PTC-limited
    'MIC_VDD': 0.30, '3V3_MIC_F': 0.30,
    'VSYS': 0.50, 'VBAT': 0.50,       # battery / system rail, ~1 A worst case
    'VBUS_PROT': 0.40,                # behind the PTC, so <= 0.75 A hold
}
POWER_NETS = set(NET_WIDTH)

# Routed first: the short critical loops before the long hauls take the
# easy channels.
# Current-carrying nets are routed BEFORE everything else and keep that
# place across every rip-up pass. A signal that necks to 0.12 mm is fine;
# the USB input behind a 0.75 A PTC is not, and letting CC1/CC2 grab the
# channel above J1 first is exactly how VBUS ended up at 0.12 mm.
# Order matters more than any other knob in this router. FB63802 is the
# buck-boost's feedback divider — a regulator with no feedback is not a
# regulator, so it goes before even the switching node. Then the current
# carriers, then the three nets whose pads are only enterable by a 0.12 mm
# trace (they have no alternative; everything else does).
ALWAYS_FIRST = ['FB63802',
                'SW1', 'SW2', 'VSYS', 'VBAT', 'VBUS_PROT', 'VBUS_C', 'VBUS_F',
                'SPK_P', 'SPK_N', 'AMP_OUTP', 'AMP_OUTN',
                'MIC_VDD', '3V3_MIC_F',
                'MIC_SCK_I2S', 'USB_DP_C', 'USB_DM_C']

PRIORITY_NETS = ALWAYS_FIRST + [
    'FB63802', 'ESP_EN', 'VOL_ADC', 'RV1_W',
    'AMP_DIN', 'AMP_BCLK', 'AMP_LRC', 'AMP_SD_MODE', 'AMP_GAIN',
    'USB_DM_C', 'USB_DP_C', 'USB_DM', 'USB_DP', 'CC1', 'CC2']

# Every width the router may draw. A rect set is rasterised per width so a
# 0.25 mm trace is not held to a 0.5 mm trace's keep-clear — which is what
# stopped 0.30 mm leaving a 0.28 mm-wide TPS63802 pin.
WIDTHS = (0.12, 0.20, 0.25, 0.30, 0.40, 0.50)

ORTHO = 10
DIAG = 14
VIA_COST = 55
MAX_EXPAND = 1500000


def to_cell(x, y):
    return int(round(x / GRID)), int(round(y / GRID))


def to_mm(ix, iy):
    return ix * GRID, iy * GRID


def board_xy(item_pos):
    return (pcbnew.ToMM(item_pos.x) - G.ORIGIN_X,
            pcbnew.ToMM(item_pos.y) - G.ORIGIN_Y)


class Router:
    """Obstacle bookkeeping + A* over a two-signal-layer grid.

    Masks are rebuilt PER NET rather than carved open around the current
    net's pads: carving a whole inflated box open also opens the clearance
    zone of whatever different-net pad sits 0.5 mm away, which is exactly
    how an 0402 pair shorts. ~330 slice writes per net costs nothing.
    """

    def __init__(self, board):
        self.board = board
        self.pad_rects = []
        self.base = np.zeros((2, NX, NY), dtype=bool)
        self.base_via = np.zeros((NX, NY), dtype=bool)
        # One obstacle mask PER TRACK WIDTH. A committed 0.50 mm route has
        # to keep 0.40 mm from the centre of a future 0.50 mm route but only
        # 0.21 mm from a 0.12 mm one; reserving the worst case for everybody
        # blanketed the single 0.1 mm corridor into a 0.5 mm-pitch QFN pin
        # and made three nets look unroutable when they are not.
        self.track = {w: np.zeros((2, NX, NY), dtype=bool) for w in WIDTHS}
        self.track_via = np.zeros((NX, NY), dtype=bool)
        # Snapshot of `track` as it stood when the current net started. The
        # earlier "track AND NOT current-net" trick was wrong: a cell marked
        # by BOTH this net and an older one got un-blocked, and a GND via
        # then landed on top of a B.Cu signal trace (two real shorts).
        self.others = {w: np.zeros((2, NX, NY), dtype=bool) for w in WIDTHS}
        self.others_via = np.zeros((NX, NY), dtype=bool)
        # Holes are hard obstacles for OTHER HOLES no matter whose net they
        # belong to: you cannot drill a via on top of a same-net PTH pad, and
        # two vias of one net still need 0.25 mm between their drills. This
        # mask is never masked off by net.
        self.hole_via = np.zeros((NX, NY), dtype=bool)
        self._build_obstacles()

    # ---------------------------------------------------------- masks ---
    def _rect(self, x0, y0, x1, y1, pad, core=False, fine=False):
        """Cells whose CENTRE falls inside the (inflated) rectangle.

        floor/ceil bounds looked safe and were not: they over-block by up
        to a whole cell, which on a 0.4 mm-pitch WSON removes the only
        0.1 mm-wide corridor a pin has and makes the part look unroutable.
        Half a grid step is added back into the inflation so the result
        stays conservative rather than becoming optimistic.
        """
        if core:
            pass
        elif fine:
            # fine-pitch escapes are routed ORTHOGONALLY (see astar's
            # allow_diag), so only the rasterisation half-step is needed
            pad = pad + GRID / 2.0
        else:
            # + GRID/(2*sqrt2): a 45 deg step between two legal cell centres
            # can clip the corner of an obstacle by that much. Leaving it out
            # is what produced 0.11 mm gaps against a 0.15 mm rule.
            pad = pad + GRID / 2.0 + GRID / (2.0 * math.sqrt(2.0))
        ix0 = max(0, int(math.ceil((x0 - pad) / GRID - 1e-9)))
        iy0 = max(0, int(math.ceil((y0 - pad) / GRID - 1e-9)))
        ix1 = min(NX - 1, int(math.floor((x1 + pad) / GRID + 1e-9)))
        iy1 = min(NY - 1, int(math.floor((y1 + pad) / GRID + 1e-9)))
        if ix1 < ix0 or iy1 < iy0:
            return None
        return slice(ix0, ix1 + 1), slice(iy0, iy1 + 1)

    def _build_obstacles(self):
        margin = G.EDGE_CLEARANCE + TW_POWER / 2
        m = int(math.ceil(margin / GRID))
        self.base[:, :m, :] = True
        self.base[:, -m:, :] = True
        self.base[:, :, :m] = True
        self.base[:, :, -m:] = True
        mv = int(math.ceil((G.EDGE_CLEARANCE + VIA_D / 2) / GRID))
        self.base_via[:mv, :] = True
        self.base_via[-mv:, :] = True
        self.base_via[:, :mv] = True
        self.base_via[:, -mv:] = True

        for fp in self.board.GetFootprints():
            for pad in fp.Pads():
                pos = board_xy(pad.GetPosition())
                x0, y0, x1, y1 = G.bbox_mm(pad.GetBoundingBox())
                through = pad.GetAttribute() in (pcbnew.PAD_ATTRIB_PTH,
                                                 pcbnew.PAD_ATTRIB_NPTH)
                if through:
                    dr = pad.GetDrillSize()
                    hx, hy = pcbnew.ToMM(dr.x) / 2.0, pcbnew.ToMM(dr.y) / 2.0
                    x0 = min(x0, pos[0] - hx)
                    x1 = max(x1, pos[0] + hx)
                    y0 = min(y0, pos[1] - hy)
                    y1 = max(y1, pos[1] + hy)
                onf = pad.IsOnLayer(pcbnew.F_Cu) or through
                onb = pad.IsOnLayer(pcbnew.B_Cu) or through
                # Inflation is per TRACK WIDTH, not one fat number: a
                # 0.45 mm halo round every pad seals a 0.5 mm-pitch QFN and
                # an SD socket's 1.1 mm contact row, and the router then
                # reports "not routable" for a board that routes fine.
                # Paste-only apertures (a QFN exposed pad is usually drawn
                # as a copper pad plus a grid of paste sub-pads) carry no
                # copper and no net. Treating them as obstacles walled off
                # the middle of every exposed pad on the board — which is
                # precisely where a thermal via belongs.
                if not onf and not onb:
                    continue
                rects = {w: self._rect(x0, y0, x1, y1, CLEAR + w / 2,
                                       fine=(w != TW_SIGNAL))
                         for w in WIDTHS}
                r_sig = rects[TW_SIGNAL]
                rv = self._rect(x0, y0, x1, y1, CLEAR + VIA_D / 2)
                shp = pad.GetShape()
                round_ish = shp in (pcbnew.PAD_SHAPE_CIRCLE,
                                    pcbnew.PAD_SHAPE_OVAL)
                hw = (x1 - x0) / 2.0
                hh = (y1 - y0) / 2.0
                shrink = (0.30 * min(hw, hh)) if round_ish else 0.02
                core = self._rect(x0, y0, x1, y1, -shrink, core=True)
                if r_sig is None:
                    continue
                sz = pad.GetSize()
                big = min(pcbnew.ToMM(sz.x), pcbnew.ToMM(sz.y)) >= 0.8
                if through and rv:
                    self.hole_via[rv[0], rv[1]] = True
                self.pad_rects.append(
                    dict(net=pad.GetNetname(), onf=onf, onb=onb,
                         rects={w: (r or r_sig) for w, r in rects.items()},
                         rv=rv or r_sig, core=core or r_sig, pos=pos,
                         through=through, big=big))

        # rule areas carried in by a footprint (the antenna keep-out)
        for fp in self.board.GetFootprints():
            for z in fp.Zones():
                bb = G.bbox_mm(z.GetBoundingBox())
                # a keep-out excludes the ITEM, not its centre line
                r = self._rect(bb[0], bb[1], bb[2], bb[3], TW_POWER / 2)
                rv = self._rect(bb[0], bb[1], bb[2], bb[3], VIA_D / 2)
                if r:
                    self.base[:, r[0], r[1]] = True
                if rv:
                    self.base_via[rv[0], rv[1]] = True

    def masks_for(self, netname, width=TW_SIGNAL):
        key = min((w for w in WIDTHS if w >= width - 1e-9), default=TW_POWER)
        b = self.base | self.others[key]
        v = self.base_via | self.others_via | self.hole_via
        for p in self.pad_rects:
            if p['net'] == netname:
                continue
            sx, sy = p['rects'][key]
            if p['onf']:
                b[0][sx, sy] = True
            if p['onb']:
                b[1][sx, sy] = True
            sx, sy = p['rv']
            v[sx, sy] = True
        return b, v

    def pad_cells(self, netname, pos, blocked):
        """Grid cells lying on the real copper of the pad at `pos`."""
        out = set()
        for p in self.pad_rects:
            if p['net'] != netname:
                continue
            if abs(p['pos'][0] - pos[0]) > 1e-6 or abs(p['pos'][1] - pos[1]) > 1e-6:
                continue
            sx, sy = p['core']
            for gx in range(sx.start, sx.stop):
                for gy in range(sy.start, sy.stop):
                    if p['onf'] and not blocked[0, gx, gy]:
                        out.add((0, gx, gy))
                    if p['onb'] and not blocked[1, gx, gy]:
                        out.add((1, gx, gy))
        if not out:
            # fall back to the pad's centre cell — but only on layers the
            # pad actually has copper on. Offering both layers let A* "reach"
            # an F.Cu-only pad from B.Cu, which is a connection that does not
            # exist outside the router's head.
            cx, cy = to_cell(*pos)
            layers = set()
            for p in self.pad_rects:
                if p['net'] == netname and abs(p['pos'][0] - pos[0]) < 1e-6 \
                        and abs(p['pos'][1] - pos[1]) < 1e-6:
                    if p['onf']:
                        layers.add(0)
                    if p['onb']:
                        layers.add(1)
            for lyr in sorted(layers):
                if 0 <= cx < NX and 0 <= cy < NY and not blocked[lyr, cx, cy]:
                    out.add((lyr, cx, cy))
        return out

    def start_net(self):
        for w in WIDTHS:
            self.others[w][:] = self.track[w]
        self.others_via[:] = self.track_via

    def mark_route(self, cells, width):
        slack = GRID / 2.0 + GRID / (2.0 * math.sqrt(2.0))
        pads = {w: int(math.ceil((CLEAR + width / 2 + w / 2 + slack) / GRID))
                for w in WIDTHS}
        vpad = int(math.ceil((CLEAR + width / 2 + VIA_D / 2 + slack) / GRID))
        for layer, ix, iy in cells:
            for w in WIDTHS:
                pad = pads[w]
                x0, x1 = max(0, ix - pad), min(NX - 1, ix + pad)
                y0, y1 = max(0, iy - pad), min(NY - 1, iy + pad)
                self.track[w][layer, x0:x1 + 1, y0:y1 + 1] = True
            x0, x1 = max(0, ix - vpad), min(NX - 1, ix + vpad)
            y0, y1 = max(0, iy - vpad), min(NY - 1, iy + vpad)
            self.track_via[x0:x1 + 1, y0:y1 + 1] = True

    def mark_via(self, ix, iy):
        slack = GRID / 2.0 + GRID / (2.0 * math.sqrt(2.0))
        for w in WIDTHS:
            pad = int(math.ceil((CLEAR + VIA_D / 2 + w / 2 + slack) / GRID))
            x0, x1 = max(0, ix - pad), min(NX - 1, ix + pad)
            y0, y1 = max(0, iy - pad), min(NY - 1, iy + pad)
            self.track[w][:, x0:x1 + 1, y0:y1 + 1] = True
        vpad = int(math.ceil((CLEAR + VIA_D) / GRID))
        x0, x1 = max(0, ix - vpad), min(NX - 1, ix + vpad)
        y0, y1 = max(0, iy - vpad), min(NY - 1, iy + vpad)
        self.track_via[x0:x1 + 1, y0:y1 + 1] = True
        self.hole_via[x0:x1 + 1, y0:y1 + 1] = True

    # ------------------------------------------------------------ A* ---
    def astar(self, blocked, via_block, sources, targets, target_pts,
              allow_via, allow_diag=True):
        tset = set(targets)
        if not tset or not sources:
            return None
        INF = float('inf')
        dist, prev, heap = {}, {}, []

        diag_ok = allow_diag

        def h(layer, ix, iy):
            best = INF
            for tx, ty in target_pts:
                dx, dy = abs(ix - tx), abs(iy - ty)
                best = min(best, ORTHO * (dx + dy)
                           - (2 * ORTHO - DIAG) * min(dx, dy)
                           if diag_ok else ORTHO * (dx + dy))
            return best

        for s in sources:
            if blocked[s[0], s[1], s[2]]:
                continue
            dist[s] = 0
            heapq.heappush(heap, (h(*s), 0, s))
        expanded = 0
        while heap:
            f, g, node = heapq.heappop(heap)
            if g > dist.get(node, INF):
                continue
            if node in tset:
                path = [node]
                while node in prev:
                    node = prev[node]
                    path.append(node)
                path.reverse()
                return path
            expanded += 1
            if expanded > MAX_EXPAND:
                return None
            layer, ix, iy = node
            moves = ((1, 0, ORTHO), (-1, 0, ORTHO), (0, 1, ORTHO),
                     (0, -1, ORTHO))
            if allow_diag:
                moves = moves + ((1, 1, DIAG), (1, -1, DIAG),
                                 (-1, 1, DIAG), (-1, -1, DIAG))
            for dx, dy, c in moves:
                nx_, ny_ = ix + dx, iy + dy
                if nx_ < 0 or ny_ < 0 or nx_ >= NX or ny_ >= NY:
                    continue
                if blocked[layer, nx_, ny_]:
                    continue
                if dx and dy:
                    if blocked[layer, ix + dx, iy] or blocked[layer, ix, iy + dy]:
                        continue
                nb = (layer, nx_, ny_)
                ng = g + c
                if ng < dist.get(nb, INF):
                    dist[nb] = ng
                    prev[nb] = node
                    heapq.heappush(heap, (ng + h(*nb), ng, nb))
            if allow_via and not via_block[ix, iy]:
                other = 1 - layer
                if not blocked[other, ix, iy]:
                    nb = (other, ix, iy)
                    ng = g + VIA_COST
                    if ng < dist.get(nb, INF):
                        dist[nb] = ng
                        prev[nb] = node
                        heapq.heappush(heap, (ng + h(*nb), ng, nb))
        return None


# ------------------------------------------------------------ emission ---
def add_track(board, net, layer, p0, p1, width):
    if p0 == p1:
        return 0
    t = pcbnew.PCB_TRACK(board)
    t.SetStart(G.vec(*p0))
    t.SetEnd(G.vec(*p1))
    t.SetWidth(G.mm(width))
    t.SetLayer(LAYERS[layer])
    t.SetNet(net)
    board.Add(t)
    return 1


def add_via(board, net, x, y):
    v = pcbnew.PCB_VIA(board)
    v.SetPosition(G.vec(x, y))
    v.SetViaType(pcbnew.VIATYPE_THROUGH)
    v.SetLayerPair(pcbnew.F_Cu, pcbnew.B_Cu)
    v.SetWidth(G.mm(VIA_D))
    v.SetDrill(G.mm(VIA_DRILL))
    v.SetNet(net)
    board.Add(v)
    return 1


def emit_path(board, net, path, width, anchor_start=None, anchor_end=None):
    # anchor_* are accepted and IGNORED: routes now start and end on cells
    # that lie inside the pad copper, so no straight unchecked stub is
    # needed to close the last 0.1 mm.
    """Cell path -> collinear PCB_TRACK runs + PCB_VIA at layer changes.

    anchor_start/anchor_end are the exact pad centres; a stub from the pad
    centre to the first grid cell is what stops every route ending 0.1 mm
    short of its pad and showing up as a dangling track.
    """
    tracks = vias = 0
    if len(path) == 1:
        layer, ix, iy = path[0]
        p0 = to_mm(ix, iy)
        return add_track(board, net, layer, p0,
                         (p0[0] + GRID / 2.0, p0[1]), width), 0
    i = 0
    while i < len(path) - 1:
        layer, ix, iy = path[i]
        if path[i + 1][0] != layer:
            vias += add_via(board, net, *to_mm(ix, iy))
            i += 1
            continue
        dx, dy = path[i + 1][1] - ix, path[i + 1][2] - iy
        j = i + 1
        while (j + 1 < len(path) and path[j + 1][0] == layer
               and path[j + 1][1] - path[j][1] == dx
               and path[j + 1][2] - path[j][2] == dy):
            j += 1
        tracks += add_track(board, net, layer, to_mm(ix, iy),
                            to_mm(path[j][1], path[j][2]), width)
        i = j
    return tracks, vias


def plane_via(board, router, net, pad):
    """Drop a via beside a pad so it reaches its plane."""
    netname = net.GetNetname()
    px, py = board_xy(pad.GetPosition())
    cx, cy = to_cell(px, py)
    layer = 0 if pad.IsOnLayer(pcbnew.F_Cu) else 1
    width = NET_WIDTH.get(netname, TW_POWER if netname in PLANE_NETS
                          else TW_SIGNAL)
    blocked, via_block = router.masks_for(netname, width)
    spot = None
    # A pad big enough to swallow a via gets one IN the pad — the normal
    # answer for a QFN/VSON thermal pad, and the only one that gives U2's
    # and U4's exposed GND pads a real path to the plane.
    big = any(p['big'] and abs(p['pos'][0] - px) < 1e-6
              and abs(p['pos'][1] - py) < 1e-6
              for p in router.pad_rects if p['net'] == netname)
    if (big and 0 <= cx < NX and 0 <= cy < NY and not via_block[cx, cy]
            and not blocked[0, cx, cy] and not blocked[1, cx, cy]):
        v = add_via(board, net, *to_mm(cx, cy))
        router.mark_via(cx, cy)
        return True, 0, v
    for r in range(1, 32):
        for dx in range(-r, r + 1):
            for dy in range(-r, r + 1):
                if max(abs(dx), abs(dy)) != r:
                    continue
                ix, iy = cx + dx, cy + dy
                if not (0 <= ix < NX and 0 <= iy < NY):
                    continue
                # a THROUGH via lands on both signal layers; checking only
                # the pad's own layer is how a GND stitch ends up sitting on
                # top of a B.Cu speaker trace
                if (via_block[ix, iy] or blocked[0, ix, iy]
                        or blocked[1, ix, iy]):
                    continue
                spot = (ix, iy)
                break
            if spot:
                break
        if spot:
            break
    if not spot:
        return False, 0, 0
    ix, iy = spot
    path = None
    for w in (width, TW_SIGNAL, TW_FINE):
        if w > width:
            continue
        blocked, via_block = router.masks_for(netname, w)
        src = router.pad_cells(netname, (px, py), blocked)
        path = router.astar(blocked, via_block, src, {(layer, ix, iy)},
                            [(ix, iy)], allow_via=False,
                            allow_diag=(w == TW_SIGNAL))
        if path is not None:
            width = w
            break
    if path is None:
        return False, 0, 0
    t, v = emit_path(board, net, path, width, anchor_start=(px, py))
    v += add_via(board, net, *to_mm(ix, iy))
    mark_path(router, path, width)
    router.mark_via(ix, iy)
    return True, t, v


def mst_order(points):
    """Prim order: returns the sequence in which to attach the points."""
    n = len(points)
    order = [0]
    inside = {0}
    while len(inside) < n:
        best = None
        for i in inside:
            for j in range(n):
                if j in inside:
                    continue
                d = ((points[i][0] - points[j][0]) ** 2
                     + (points[i][1] - points[j][1]) ** 2)
                if best is None or d < best[0]:
                    best = (d, j)
        order.append(best[1])
        inside.add(best[1])
    return order


def mst_length(points):
    n = len(points)
    if n < 2:
        return 0.0
    inside, total = {0}, 0.0
    while len(inside) < n:
        best = None
        for i in inside:
            for j in range(n):
                if j in inside:
                    continue
                d = ((points[i][0] - points[j][0]) ** 2
                     + (points[i][1] - points[j][1]) ** 2)
                if best is None or d < best[0]:
                    best = (d, j)
        total += math.sqrt(best[0])
        inside.add(best[1])
    return total


def route_net(board, router, netname, pts, width, report, commit=True):
    """Maze-route one net's MST.

    Nothing is drawn until EVERY connection of the net succeeds. That is
    what makes a retry at a different width honest: a half-drawn net left
    on the board before falling back would have to be ripped up, and a
    router with no rip-up would instead just accumulate garbage.
    Returns ('full'|'partial'|'none', paths).
    """
    net = board.FindNet(netname)
    router.start_net()
    order = mst_order(pts)
    blocked, via_block = router.masks_for(netname, width)
    merged = router.pad_cells(netname, pts[order[0]], blocked)
    paths = []
    done = 1
    for k in order[1:]:
        tgt = router.pad_cells(netname, pts[k], blocked)
        src = {c for c in merged if not blocked[c[0], c[1], c[2]]}
        path = router.astar(blocked, via_block, src, tgt,
                            [to_cell(*pts[k])], allow_via=True,
                            allow_diag=(width == TW_SIGNAL))
        if path is None:
            break
        paths.append((path, pts[k]))
        merged |= set(path)
        merged |= tgt
        done += 1
    status = 'full' if done == len(order) else ('partial' if done > 1 else 'none')
    if commit and paths:
        commit_paths(board, router, net, paths, width, report)
    return status, paths


def commit_paths(board, router, net, paths, width, report):
    for path, _endpt in paths:
        t, v = emit_path(board, net, path, width)
        report[0] += t
        report[1] += v
        mark_path(router, path, width)


def mark_path(router, path, width):
    """Mark a committed route as an obstacle.

    The layer changes inside the path are VIAS, and a via needs a bigger
    keep-clear than the track that carries it: marking only the track
    radius let two vias on different nets sit 0.70 mm apart when they need
    0.75 mm, which DRC reported as a 0.10 mm clearance violation.
    """
    router.mark_route(path, width)
    for i in range(len(path) - 1):
        if path[i + 1][0] != path[i][0]:
            router.mark_via(path[i][1], path[i][2])


def do_plane(board, router, netname, pads_by_net, report):
    """Tie every pad of a plane net to its plane.

    First choice is a via at (or beside) the pad. Where the package is too
    tight for one — U4's 0.5 mm pitch GND pins, U5's 0.4 mm pitch — fall
    back to a maze route into same-net copper that already reached the
    plane. Leaving those to 'the pour will find them' is how a buck-boost
    ends up with its ground return through a 0.28 mm zone finger.
    """
    net = board.FindNet(netname)
    if net is None:
        return (0, 0, [])
    router.start_net()
    ok = fail = 0
    fails = []
    tied = []                        # positions of pads that reached the plane
    late = []
    for ref, pad in pads_by_net.get(netname, []):
        pos = board_xy(pad.GetPosition())
        if pad.GetAttribute() in (pcbnew.PAD_ATTRIB_PTH, pcbnew.PAD_ATTRIB_NPTH):
            ok += 1                  # a through pad already crosses every layer
            tied.append(pos)
            continue
        good, t, v = plane_via(board, router, net, pad)
        report[0] += t
        report[1] += v
        if good:
            ok += 1
            tied.append(pos)
        else:
            late.append((ref, pad, pos))

    for ref, pad, pos in late:
        done = False
        for width in (TW_SIGNAL, TW_FINE):
            blocked, via_block = router.masks_for(netname, width)
            src = router.pad_cells(netname, pos, blocked)
            tgt = set()
            tpts = []
            for tp in tied:
                cells = router.pad_cells(netname, tp, blocked)
                if cells:
                    tgt |= cells
                    tpts.append(to_cell(*tp))
            if not src or not tgt:
                continue
            path = router.astar(blocked, via_block, src, tgt, tpts,
                                allow_via=False,
                                allow_diag=(width == TW_SIGNAL))
            if path is None:
                continue
            t, v = emit_path(board, net, path, width, anchor_start=pos)
            report[0] += t
            report[1] += v
            mark_path(router, path, width)
            tied.append(pos)
            ok += 1
            done = True
            break
        if not done:
            fail += 1
            fails.append('%s.%s' % (ref, pad.GetNumber()))
    return (ok, fail, fails)


def route_board(promote, verbose=False):
    """One complete routing attempt on a FRESH copy of the placed board.

    `promote` is an ordered list of nets to route before anything else.
    Returns (board, stats) — nothing is written to disk here, so a failed
    attempt costs only time.
    """
    board = pcbnew.LoadBoard(BOARD_FILE)
    router = Router(board)

    pads_by_net = {}
    for fp in board.GetFootprints():
        for pad in fp.Pads():
            n = pad.GetNetname()
            if n:
                pads_by_net.setdefault(n, []).append((fp.GetReference(), pad))

    report = [0, 0]
    routed, partial, failed = [], [], []
    rank = {n: i for i, n in enumerate(promote)}

    signal = []
    for netname, pads in pads_by_net.items():
        if netname in PLANE_NETS or netname.startswith('unconnected-'):
            continue
        if len(pads) < 2:
            continue
        pts = [board_xy(p.GetPosition()) for _r, p in pads]
        signal.append((rank.get(netname, len(promote)), mst_length(pts),
                       netname, pts))
    # Within the same rank, a net whose pads can only be entered by a
    # 0.12 mm trace has no alternative and must pick its channel before a
    # net that can be drawn six different ways.
    flex = {}
    for _r, _l, netname, pts in signal:
        best = 0.0
        for w in sorted(WIDTHS, reverse=True):
            blocked, _v = router.masks_for(netname, w)
            if all(router.pad_cells(netname, pt, blocked) for pt in pts):
                best = w
                break
        flex[netname] = best
    signal.sort(key=lambda t: (t[0], flex[t[2]], t[1]))
    hard = [e for e in signal if e[0] < len(promote)]
    rest = [e for e in signal if e[0] >= len(promote)]

    def run(entries):
        for prio, total, netname, pts in entries:
            width = NET_WIDTH.get(netname, TW_SIGNAL)
            widths = sorted({w for w in (width, 0.3, TW_SIGNAL, TW_FINE)
                             if w <= width}, reverse=True)
            net = board.FindNet(netname)
            chosen = fallback = None
            for w in widths:
                st, paths = route_net(board, router, netname, pts, w, report,
                                      commit=False)
                if st == 'full':
                    chosen = (w, paths)
                    break
                if st == 'partial' and fallback is None:
                    fallback = (w, paths)
            if chosen is not None:
                router.start_net()
                commit_paths(board, router, net, chosen[1], chosen[0], report)
                routed.append((netname, chosen[0], width))
            elif fallback is not None:
                router.start_net()
                commit_paths(board, router, net, fallback[1], fallback[0],
                             report)
                partial.append(netname)
            else:
                failed.append(netname)

    run(hard)

    plane_report = {}
    plane_report['+3V3'] = do_plane(board, router, '+3V3', pads_by_net, report)
    run(rest)
    plane_report['GND'] = do_plane(board, router, 'GND', pads_by_net, report)

    stitched = 0
    gnd = board.FindNet('GND')
    router.start_net()
    blocked, via_block = router.masks_for('GND')
    step = int(round(4.0 / GRID))
    for gx in range(int(4 / GRID), NX, step):
        for gy in range(int(4 / GRID), NY, step):
            if via_block[gx, gy] or blocked[0, gx, gy] or blocked[1, gx, gy]:
                continue
            report[1] += add_via(board, gnd, *to_mm(gx, gy))
            router.mark_via(gx, gy)
            blocked, via_block = router.masks_for('GND')
            stitched += 1

    stats = dict(routed=routed, partial=partial, failed=failed,
                 planes=plane_report, stitched=stitched,
                 tracks=report[0], vias=report[1])
    return board, stats


def score(stats):
    """Rank attempts. Order of badness:
      1. connections that were not made at all
      2. a CURRENT-carrying net forced below 0.2 mm (a thermal defect)
      3. plane pads left for the pour to find
      4. signal nets necked to 0.12 mm (cosmetic at these currents)
    """
    unrouted = len(stats['failed']) + len(stats['partial'])
    thin_power = sum(1 for n, got, want in stats['routed']
                     if n in NET_WIDTH and got < 0.2)
    plane_fail = sum(f for _ok, f, _l in stats['planes'].values())
    necked = sum(1 for n, got, want in stats['routed'] if got < want)
    return (unrouted, thin_power, plane_fail, necked)


def main():
    promote = list(PRIORITY_NETS)
    best = None
    seen_bad = []
    for attempt in range(1, 5):
        board, stats = route_board(promote)
        sc = score(stats)
        print('attempt %d: %d unrouted, %d partial, %d thin-power, '
              '%d plane unplaced, %d necked'
              % (attempt, len(stats['failed']), len(stats['partial']),
                 sc[1], sc[2], sc[3]))
        if best is None or sc < best[0]:
            best = (sc, board, stats)
        if not stats['failed'] and not stats['partial']:
            break
        # rip-up-and-reroute: whatever could not get through goes to the
        # front of the queue next time, before the easy nets take the
        # channels it needed. The board is rebuilt from scratch each pass,
        # so this is a real retry and not an accumulation of debris.
        newly = [n for n in stats['failed'] + stats['partial']
                 if n not in ALWAYS_FIRST]
        newly += [n for n, got, want in stats['routed']
                  if n in NET_WIDTH and got < 0.2 and n not in ALWAYS_FIRST]
        for n in newly:
            if n not in seen_bad:
                seen_bad.append(n)
        rest = [n for n in promote
                if n not in ALWAYS_FIRST and n not in seen_bad]
        # rotate the tail each pass so a net that keeps losing the same
        # channel eventually gets to pick first
        rest = rest[attempt % max(len(rest), 1):] + rest[:attempt % max(len(rest), 1)]
        promote = ALWAYS_FIRST + seen_bad + rest

    sc, board, stats = best
    pcbnew.ZONE_FILLER(board).Fill(board.Zones())
    pcbnew.SaveBoard(BOARD_FILE, board)

    print('=' * 66)
    print('plane-connected nets (one via per SMD pad):')
    for k in ('GND', '+3V3'):
        ok, fail, fails = stats['planes'][k]
        note = ('  no via: ' + ', '.join(fails)) if fails else ''
        print('   %-12s %3d pads tied, %d unplaced%s' % (k, ok, fail, note))
    print()
    print('GND stitching vias: %d' % stats['stitched'])
    necked = [(n, got, want) for n, got, want in stats['routed'] if got < want]
    print('maze-routed nets: %d fully, %d partial, %d not routed'
          % (len(stats['routed']), len(stats['partial']), len(stats['failed'])))
    if stats['partial']:
        print('   PARTIAL: %s' % ', '.join(sorted(stats['partial'])))
    if stats['failed']:
        print('   NOT ROUTED: %s' % ', '.join(sorted(stats['failed'])))
    if necked:
        print('   necked down (pad pitch, not a rule change):')
        for n, got, want in sorted(necked):
            print('      %-14s %.2f mm (wanted %.2f)' % (n, got, want))
    print()
    print('tracks: %d   vias: %d' % (stats['tracks'], stats['vias']))
    print('wrote', BOARD_FILE)


if __name__ == '__main__':
    main()

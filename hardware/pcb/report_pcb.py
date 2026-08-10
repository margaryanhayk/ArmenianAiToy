#!/usr/bin/env python3
"""Independent read-back of the finished board.

Deliberately does NOT reuse the router's own bookkeeping: it reloads
`areg-reva.kicad_pcb` and measures what is actually in the file, so the
phase-2 status note quotes the board rather than the program that wrote
it. Run with KiCad's own Python.
"""
import collections
import os

import pcbnew

import generate_pcb as G
import netlist

BOARD_FILE = G.OUT


def main():
    board = pcbnew.LoadBoard(BOARD_FILE)
    comps, nets = netlist.load()

    fps = list(board.GetFootprints())
    print('board file      : %s' % os.path.basename(BOARD_FILE))
    print('copper layers   : %d' % board.GetCopperLayerCount())
    names = [board.GetLayerName(l) for l in
             (pcbnew.F_Cu, pcbnew.In1_Cu, pcbnew.In2_Cu, pcbnew.B_Cu)]
    print('stackup         : %s' % ' / '.join(names))

    bb = board.GetBoardEdgesBoundingBox()
    print('board outline   : %.2f x %.2f mm'
          % (pcbnew.ToMM(bb.GetWidth()), pcbnew.ToMM(bb.GetHeight())))
    print('footprints      : %d placed (netlist has %d)' % (len(fps), len(comps)))
    dnp = [f.GetReference() for f in fps if f.IsDNP()]
    print('  DNP (not fitted for run 1): %s' % ', '.join(sorted(dnp)))

    tracks = [t for t in board.GetTracks() if t.GetClass() == 'PCB_TRACK']
    vias = [t for t in board.GetTracks() if t.GetClass() == 'PCB_VIA']
    per_layer = collections.Counter(board.GetLayerName(t.GetLayer())
                                    for t in tracks)
    total_len = sum(pcbnew.ToMM(t.GetLength()) for t in tracks)
    print('tracks          : %d segments, %.0f mm total  (%s)'
          % (len(tracks), total_len,
             ', '.join('%s %d' % kv for kv in sorted(per_layer.items()))))
    widths = collections.Counter(round(pcbnew.ToMM(t.GetWidth()), 3)
                                 for t in tracks)
    print('  widths        : %s'
          % ', '.join('%.2f mm x%d' % (w, n) for w, n in sorted(widths.items())))
    print('vias            : %d (%.2f mm pad / %.2f mm drill)'
          % (len(vias),
             pcbnew.ToMM(vias[0].GetWidth()) if vias else 0,
             pcbnew.ToMM(vias[0].GetDrill()) if vias else 0))

    zones = list(board.Zones())
    print('zones           : %d' % len(zones))
    for z in zones:
        print('   %-12s on %-9s  filled area %.1f mm2'
              % (z.GetNetname() or '(none)',
                 board.GetLayerName(z.GetLayer()),
                 z.GetFilledArea() / 1e12 if hasattr(z, 'GetFilledArea')
                 else -1))

    # connectivity read straight from the board's own engine
    board.BuildConnectivity()
    conn = board.GetConnectivity()
    ratsnest = collections.Counter()
    try:
        for i in range(1, board.GetNetCount()):
            net = board.FindNet(i)
            if net is None:
                continue
            n = conn.GetUnconnectedCount(True) if False else None
    except Exception:
        pass
    print('unrouted pairs  : %d (board connectivity engine)'
          % conn.GetUnconnectedCount(True))

    # the two contract distances that matter most, measured on the board
    def pad_pos(ref, num):
        fp = board.FindFootprintByReference(ref)
        for p in fp.Pads():
            if p.GetNumber() == num:
                return (pcbnew.ToMM(p.GetPosition().x),
                        pcbnew.ToMM(p.GetPosition().y))
        return None

    def d(a, b):
        return ((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2) ** 0.5

    print()
    print('measured on the placed board:')
    print('  U1 pin2 -> C6   %.2f mm   (rule: <= 3 mm)'
          % d(pad_pos('U1', '2'), pad_pos('C6', '1')))
    print('  U1 pin2 -> C7   %.2f mm   (rule: <= 3 mm)'
          % d(pad_pos('U1', '2'), pad_pos('C7', '1')))
    print('  U2 VDD  -> C19  %.2f mm   (rule: <= 2 mm)'
          % d(pad_pos('U2', '7'), pad_pos('C19', '1')))
    print('  U2 VDD  -> C20  %.2f mm   (rule: <= 5 mm)'
          % d(pad_pos('U2', '7'), pad_pos('C20', '1')))
    print('  U2 VDD  -> C21  %.2f mm   (rule: <= 15 mm)'
          % d(pad_pos('U2', '7'), pad_pos('C21', '1')))
    print('  J3 VDD  -> C11  %.2f mm   (rule: at the socket)'
          % d(pad_pos('J3', '4'), pad_pos('C11', '1')))
    print('  U3 mic  -> J4   %.1f mm   (rule: >= 40 mm)'
          % d(pad_pos('U3', '5'), pad_pos('J4', '1')))
    print('  U8 mic  -> J4   %.1f mm   (rule: >= 40 mm)'
          % d(pad_pos('U8', '2'), pad_pos('J4', '1')))
    print('  U4 SW1  -> L1   %.2f mm   (switching node, keep short)'
          % d(pad_pos('U4', '9'), pad_pos('L1', '1')))

    u1 = board.FindFootprintByReference('U1')
    for z in u1.Zones():
        b2 = G.bbox_mm(z.GetBoundingBox())
        print('  antenna keep-out x %.1f..%.1f mm (board starts at x = 0)'
              % (b2[0], b2[2]))


if __name__ == '__main__':
    main()

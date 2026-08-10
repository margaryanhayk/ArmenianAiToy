#!/usr/bin/env python3
"""Put the library nickname and the LCSC part number back on every footprint.

`pcbnew.FootprintLoad()` returns a footprint whose FPID carries only the
bare name ("C_0603_1608Metric"), not the library-qualified id the symbol
declares ("Capacitor_SMD:C_0603_1608Metric"), and it carries none of the
symbol's custom fields. Both show up in `kicad-cli pcb drc
--schematic-parity` as warnings, and the missing LCSC field is a real
problem beyond tidiness: the JLCPCB BOM/CPL export reads it off the
footprint.

Runs on the ROUTED board in place — it touches metadata only, so no track,
via or zone is disturbed.
"""
import os
import sys

import pcbnew

import generate_pcb as G
import netlist


def apply(board, comps):
    fixed_fpid = fixed_field = 0
    for fp in board.GetFootprints():
        ref = fp.GetReference()
        c = comps.get(ref)
        if c is None:
            continue
        if fp.GetFPIDAsString() != c.footprint:
            fp.SetFPIDAsString(c.footprint)
            fixed_fpid += 1
        for name, value in sorted(c.fields.items()):
            if name in ('Footprint', 'Datasheet', 'Description') or not value:
                continue
            if not fp.HasField(name) or fp.GetFieldText(name) != value:
                fp.SetField(name, value)
                fixed_field += 1
            # a copied field defaults to VISIBLE, which puts the LCSC part
            # number on the silkscreen of every 0402 on the board
            fld = fp.GetField(name)
            if fld.IsVisible():
                fld.SetVisible(False)
        # test pads are bare copper artwork, excluded from the BOM in the
        # schematic; the footprint attribute has to say the same or parity
        # reports a mismatch on all twelve
        if c.ref.startswith("TP"):
            fp.SetExcludedFromBOM(True)
    return fixed_fpid, fixed_field


def main():
    comps, _nets = netlist.load()
    board = pcbnew.LoadBoard(G.OUT)
    a, b = apply(board, comps)
    pcbnew.SaveBoard(G.OUT, board)
    print('footprint ids qualified : %d' % a)
    print('fields copied from netlist: %d' % b)
    print('wrote', G.OUT)


if __name__ == '__main__':
    main()

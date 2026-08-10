#!/usr/bin/env python3
"""Dump the finished board's geometry to JSON (KiCad Python), then draw one
PNG per copper layer (system Python + matplotlib).

`kicad-cli pcb render` only ever shows the two OUTER layers, so it cannot
answer "is the GND plane actually solid" or "did the +3V3 plane get split
in half". This does.

    KiCad10\\bin\\python.exe plot_layers.py dump
    python plot_layers.py draw
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DUMP = os.path.join(HERE, 'out', 'layers.json')
OUTDIR = os.path.join(HERE, 'out')

LAYER_NAMES = ['F.Cu', 'In1.Cu', 'In2.Cu', 'B.Cu']


def dump():
    import pcbnew
    import generate_pcb as G

    board = pcbnew.LoadBoard(G.OUT)
    ids = {'F.Cu': pcbnew.F_Cu, 'In1.Cu': pcbnew.In1_Cu,
           'In2.Cu': pcbnew.In2_Cu, 'B.Cu': pcbnew.B_Cu}
    f = pcbnew.ToMM
    data = {'tracks': [], 'vias': [], 'pads': [], 'zones': [], 'edges': [],
            'refs': []}

    for t in board.GetTracks():
        if t.GetClass() == 'PCB_VIA':
            p = t.GetPosition()
            data['vias'].append([f(p.x), f(p.y), f(t.GetWidth(pcbnew.F_Cu)),
                                 t.GetNetname()])
        else:
            s, e = t.GetStart(), t.GetEnd()
            data['tracks'].append([f(s.x), f(s.y), f(e.x), f(e.y),
                                   f(t.GetWidth()),
                                   board.GetLayerName(t.GetLayer()),
                                   t.GetNetname()])

    for fp in board.GetFootprints():
        p = fp.GetPosition()
        data['refs'].append([f(p.x), f(p.y), fp.GetReference()])
        for pad in fp.Pads():
            pp = pad.GetPosition()
            sz = pad.GetSize()
            lay = []
            for nm, lid in ids.items():
                if pad.IsOnLayer(lid):
                    lay.append(nm)
            data['pads'].append([f(pp.x), f(pp.y), f(sz.x), f(sz.y),
                                 pad.GetOrientationDegrees(), lay,
                                 pad.GetNetname()])

    for z in board.Zones():
        lname = board.GetLayerName(z.GetLayer())
        poly = z.GetFilledPolysList(z.GetLayer())
        outs = []
        for i in range(poly.OutlineCount()):
            o = poly.Outline(i)
            pts = [[f(o.CPoint(k).x), f(o.CPoint(k).y)]
                   for k in range(o.PointCount())]
            holes = []
            for hidx in range(poly.HoleCount(i)):
                h = poly.Hole(i, hidx)
                holes.append([[f(h.CPoint(k).x), f(h.CPoint(k).y)]
                              for k in range(h.PointCount())])
            outs.append({'pts': pts, 'holes': holes})
        data['zones'].append({'layer': lname, 'net': z.GetNetname(),
                              'outlines': outs})

    for d in board.GetDrawings():
        if d.GetLayer() == pcbnew.Edge_Cuts and d.GetClass() == 'PCB_SHAPE':
            s, e = d.GetStart(), d.GetEnd()
            data['edges'].append([f(s.x), f(s.y), f(e.x), f(e.y)])

    os.makedirs(OUTDIR, exist_ok=True)
    with open(DUMP, 'w') as fh:
        json.dump(data, fh)
    print('dumped %d tracks, %d vias, %d pads, %d zones'
          % (len(data['tracks']), len(data['vias']), len(data['pads']),
             len(data['zones'])))


def draw():
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    from matplotlib.patches import Rectangle, Circle, Polygon

    data = json.load(open(DUMP))
    xs = [e[0] for e in data['edges']] + [e[2] for e in data['edges']]
    ys = [e[1] for e in data['edges']] + [e[3] for e in data['edges']]
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)

    for layer in LAYER_NAMES:
        fig, ax = plt.subplots(figsize=(13, 11), dpi=130)
        ax.set_facecolor('#0e1117')
        for z in data['zones']:
            if z['layer'] != layer:
                continue
            col = '#2f6f4f' if z['net'] == 'GND' else '#7a4a2a'
            for o in z['outlines']:
                ax.add_patch(Polygon(o['pts'], closed=True, facecolor=col,
                                     edgecolor='none', alpha=0.85, zorder=1))
                for h in o['holes']:
                    ax.add_patch(Polygon(h, closed=True, facecolor='#0e1117',
                                         edgecolor='none', zorder=2))
        for px, py, sx, sy, rot, lays, net in data['pads']:
            if layer not in lays:
                continue
            ax.add_patch(Rectangle((px - sx / 2, py - sy / 2), sx, sy,
                                   angle=-rot, rotation_point='center',
                                   facecolor='#d8b44a', edgecolor='none',
                                   zorder=4))
        for sx, sy, ex, ey, w, lay, net in data['tracks']:
            if lay != layer:
                continue
            ax.plot([sx, ex], [sy, ey], linewidth=max(w * 3.2, 0.6),
                    color='#e05a3a', solid_capstyle='round', zorder=5)
        for vx, vy, vw, net in data['vias']:
            ax.add_patch(Circle((vx, vy), vw / 2, facecolor='#8fd3ff',
                                edgecolor='none', zorder=6))
        for ex0, ey0, ex1, ey1 in data['edges']:
            ax.plot([ex0, ex1], [ey0, ey1], color='#f0f0f0', linewidth=1.4,
                    zorder=8)
        ax.set_xlim(x0 - 8, x1 + 3)
        ax.set_ylim(y1 + 3, y0 - 8)
        ax.set_aspect('equal')
        ax.set_title('%s   —   Areg rev-A' % layer, color='#e8e8e8')
        ax.tick_params(colors='#888888', labelsize=7)
        for sp in ax.spines.values():
            sp.set_color('#333333')
        out = os.path.join(OUTDIR, 'layer-%s.png'
                           % layer.replace('.', '').lower())
        fig.savefig(out, facecolor='#0e1117', bbox_inches='tight')
        plt.close(fig)
        print('wrote', out)


if __name__ == '__main__':
    if len(sys.argv) > 1 and sys.argv[1] == 'draw':
        draw()
    else:
        dump()

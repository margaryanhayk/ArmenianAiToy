#!/usr/bin/env python3
"""Shared netlist reader for the Areg rev-A board generator.

Parses `areg-reva.net` (KiCad s-expression netlist exported in phase 1) into
plain Python so the layout generator never has to re-state the connectivity
the schematic already owns. If the schematic changes, this changes with it.
"""
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
NET_FILE = os.path.join(HERE, 'areg-reva.net')
FP_LIB_TABLE = os.path.join(HERE, 'fp-lib-table')


# --------------------------------------------------------------- s-expr ---
def tokenize(text):
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c in ' \t\r\n':
            i += 1
        elif c in '()':
            yield c
            i += 1
        elif c == '"':
            j, out = i + 1, []
            while j < n:
                if text[j] == '\\' and j + 1 < n:
                    out.append(text[j + 1])
                    j += 2
                elif text[j] == '"':
                    break
                else:
                    out.append(text[j])
                    j += 1
            yield ('str', ''.join(out))
            i = j + 1
        else:
            j = i
            while j < n and text[j] not in ' \t\r\n()"':
                j += 1
            yield ('sym', text[i:j])
            i = j


def parse(text):
    stack, root = [], None
    for t in tokenize(text):
        if t == '(':
            node = []
            if stack:
                stack[-1].append(node)
            stack.append(node)
        elif t == ')':
            root = stack.pop()
        else:
            stack[-1].append(t)
    return root


def tag(node):
    return node[0][1] if node and isinstance(node[0], tuple) else None


def kids(node, name):
    return [x for x in node if isinstance(x, list) and tag(x) == name]


def kid(node, name):
    r = kids(node, name)
    return r[0] if r else None


def val(node, name, default=''):
    k = kid(node, name)
    if not k or len(k) < 2:
        return default
    return k[1][1]


# --------------------------------------------------------------- netlist ---
class Comp:
    __slots__ = ('ref', 'value', 'footprint', 'dnp', 'fields')

    def __init__(self, ref, value, footprint, dnp, fields):
        self.ref, self.value, self.footprint = ref, value, footprint
        self.dnp, self.fields = dnp, fields

    def __repr__(self):
        return f'<{self.ref} {self.value} {self.footprint}>'


def load(path=NET_FILE):
    """-> (comps: {ref: Comp}, nets: {netname: [(ref, pin), ...]})"""
    root = parse(open(path, encoding='utf-8').read())
    comps, nets = {}, {}

    cblock = kid(root, 'components')
    for c in kids(cblock, 'comp'):
        ref = val(c, 'ref')
        fields = {}
        fblock = kid(c, 'fields')
        if fblock:
            for f in kids(fblock, 'field'):
                nm = val(f, 'name')
                # (field (name "LCSC") "C19702")
                fv = ''
                for x in f:
                    if isinstance(x, tuple) and x[0] == 'str':
                        fv = x[1]
                fields[nm] = fv
        props = {val(p, 'name'): val(p, 'value') for p in kids(c, 'property')}
        dnp = 'dnp' in props or val(c, 'dnp') == 'yes'
        comps[ref] = Comp(ref, val(c, 'value'), val(c, 'footprint'), dnp, fields)

    nblock = kid(root, 'nets')
    for n in kids(nblock, 'net'):
        name = val(n, 'name')
        nodes = []
        for nd in kids(n, 'node'):
            nodes.append((val(nd, 'ref'), val(nd, 'pin')))
        nets[name] = nodes
    return comps, nets


# ------------------------------------------------------- fp-lib-table ---
def fp_lib_paths(path=FP_LIB_TABLE):
    """-> {nickname: absolute .pretty directory}"""
    text = open(path, encoding='utf-8').read()
    out = {}
    for m in re.finditer(r'\(lib \(name "([^"]+)"\).*?\(uri "([^"]+)"\)', text, re.S):
        nick, uri = m.group(1), m.group(2)
        uri = uri.replace('${KIPRJMOD}', HERE)
        out[nick] = uri
    return out


if __name__ == '__main__':
    comps, nets = load()
    print(f'{len(comps)} components, {len(nets)} nets')
    missing = [c.ref for c in comps.values() if not c.footprint]
    print('components with no footprint:', missing or 'none')
    libs = fp_lib_paths()
    print(f'{len(libs)} footprint libraries')

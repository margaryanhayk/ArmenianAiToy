#!/usr/bin/env python3
"""Areg rev-A production schematic generator (Phase 1 — schematic only).

Emits areg-reva.kicad_sch (KiCad s-expression, v8-compatible format that
KiCad 10 reads/migrates transparently) from the pin map and net list in
hardware/pcb/rev-a-design-inputs.md + docs/hardware/schematic-spec.md.

Authoring method: every symbol instance is placed at a fixed grid position;
connectivity is by GLOBAL LABELS and power symbols placed exactly on pin
endpoints (net-label style, no crossing wires). The script REFUSES to emit
if any pin of any placed symbol is neither netted nor explicitly NC'd.

Stock symbols are extracted (and `extends` chains flattened) from the
KiCad 10 share libraries; TPS63802DLA is project-local (created from the
TI datasheet SLVSEU9D — see NOTES.md).
"""
import re, sys, uuid, copy, os

KICAD_SYMS = r"C:\Users\hayk.margaryan\KiCad10\share\kicad\symbols"
OUT_DIR = os.path.dirname(os.path.abspath(__file__))

# ---------------------------------------------------------------- s-expr ---
class S(list):
    """An s-expression node: first element is the tag (str), rest are
    str atoms or S nodes. Quoted atoms are kept as ('"', value) tuples."""
    pass

def tokenize(text):
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c in ' \t\r\n':
            i += 1
        elif c == '(' or c == ')':
            yield c; i += 1
        elif c == '"':
            j = i + 1; out = []
            while j < n:
                if text[j] == '\\' and j + 1 < n:
                    out.append(text[j:j+2]); j += 2
                elif text[j] == '"':
                    break
                else:
                    out.append(text[j]); j += 1
            yield ('"', ''.join(out)); i = j + 1
        else:
            j = i
            while j < n and text[j] not in ' \t\r\n()"':
                j += 1
            yield text[i:j]; i = j

def parse(text):
    tokens = tokenize(text)
    stack = []
    root = None
    for t in tokens:
        if t == '(':
            node = S()
            if stack:
                stack[-1].append(node)
            stack.append(node)
        elif t == ')':
            root = stack.pop()
        else:
            stack[-1].append(t)
    return root

def serialize(node, indent=0):
    pad = '\t' * indent
    parts = []
    simple = all(not isinstance(x, S) for x in node)
    def atom(a):
        if isinstance(a, tuple):
            return '"%s"' % a[1]
        return a
    if simple:
        return pad + '(' + ' '.join(atom(a) for a in node) + ')'
    head = [atom(a) for a in node if not isinstance(a, S)]
    # keep leading atoms on the first line
    out = pad + '(' + ' '.join(head)
    for x in node:
        if isinstance(x, S):
            out += '\n' + serialize(x, indent + 1)
    out += '\n' + pad + ')'
    return out

def q(s):
    return ('"', s)

def find_all(node, tag):
    return [x for x in node if isinstance(x, S) and x and x[0] == tag]

def find_one(node, tag):
    r = find_all(node, tag)
    return r[0] if r else None

def sym_name(node):
    # (symbol "NAME" ...)
    return node[1][1] if isinstance(node[1], tuple) else node[1]

# ------------------------------------------------------- symbol library ---
_lib_cache = {}
def load_lib(libname):
    if libname not in _lib_cache:
        path = os.path.join(KICAD_SYMS, libname + '.kicad_sym')
        with open(path, encoding='utf-8') as f:
            _lib_cache[libname] = parse(f.read())
    return _lib_cache[libname]

def get_symbol_raw(libname, name):
    lib = load_lib(libname)
    for s in find_all(lib, 'symbol'):
        if sym_name(s) == name:
            return s
    raise KeyError(f'{libname}:{name} not found')

def rename_symbol(node, old, new):
    """Rename a symbol def and its unit sub-symbols old_* -> new_*."""
    node[1] = q(new)
    for sub in find_all(node, 'symbol'):
        sn = sym_name(sub)
        if sn.startswith(old + '_'):
            sub[1] = q(new + sn[len(old):])

def flatten_symbol(libname, name):
    """Resolve `extends` chains: returns a standalone symbol def named
    `name` with the parent's graphics/pins and the child's properties."""
    node = copy.deepcopy(get_symbol_raw(libname, name))
    ext = find_one(node, 'extends')
    if not ext:
        return node
    parent_name = ext[1][1]
    parent = flatten_symbol(libname, parent_name)
    # child property overrides applied onto a copy of the parent
    result = copy.deepcopy(parent)
    rename_symbol(result, parent_name, name)
    child_props = {p[1][1]: p for p in find_all(node, 'property')}
    kept = S([x for x in result if not (isinstance(x, S) and x and x[0] == 'property'
                                        and x[1][1] in child_props)])
    # insert child properties after the header atoms/settings, before sub-symbols
    out = S()
    inserted = False
    for x in kept:
        if isinstance(x, S) and x[0] == 'symbol' and not inserted:
            for p in child_props.values():
                out.append(copy.deepcopy(p))
            inserted = True
        out.append(x)
    if not inserted:
        for p in child_props.values():
            out.append(copy.deepcopy(p))
    out[0:0] = []
    result2 = S(out)
    return result2

def get_pins(symdef):
    """Return {number: (x, y, angle, name, etype)} from a flattened def."""
    pins = {}
    for sub in find_all(symdef, 'symbol'):
        for p in find_all(sub, 'pin'):
            at = find_one(p, 'at')
            x, y, ang = float(at[1]), float(at[2]), float(at[3]) if len(at) > 3 else 0.0
            nm = find_one(p, 'name')[1][1]
            num = find_one(p, 'number')[1][1]
            etype = p[1]
            pins[num] = (x, y, ang, nm, etype)
    return pins

# --------------------------------------------------- custom project lib ---
# TPS63802DLA — created from TI datasheet SLVSEU9D (rev D, Jan 2021),
# Table 7-1: 1 EN, 2 MODE, 3 AGND, 4 FB, 5 PG, 6 VOUT, 7 L2, 8 GND, 9 L1,
# 10 VIN. Package: 10-pin VSON-HR (DLA), 2.5 x 3.0 mm.
TPS63802_SYM = '''
(symbol "TPS63802DLA" (exclude_from_sim no) (in_bom yes) (on_board yes)
  (property "Reference" "U" (at -7.62 11.43 0) (effects (font (size 1.27 1.27))))
  (property "Value" "TPS63802DLA" (at 0 -11.43 0) (effects (font (size 1.27 1.27))))
  (property "Footprint" "Package_SON:Texas_S-PVSON-N10" (at 0 -13.97 0) (effects (font (size 1.27 1.27)) hide))
  (property "Datasheet" "https://www.ti.com/lit/ds/symlink/tps63802.pdf" (at 0 0 0) (effects (font (size 1.27 1.27)) hide))
  (property "ki_description" "2-A high-efficiency low-Iq buck-boost converter, VIN 1.3-5.5V, VSON-HR-10 (DLA)" (at 0 0 0) (effects (font (size 1.27 1.27)) hide))
  (symbol "TPS63802DLA_0_1"
    (rectangle (start -8.89 10.16) (end 8.89 -10.16)
      (stroke (width 0.254) (type default)) (fill (type background)))
  )
  (symbol "TPS63802DLA_1_1"
    (pin power_in line (at -11.43 7.62 0) (length 2.54)
      (name "VIN" (effects (font (size 1.27 1.27)))) (number "10" (effects (font (size 1.27 1.27)))))
    (pin input line (at -11.43 2.54 0) (length 2.54)
      (name "EN" (effects (font (size 1.27 1.27)))) (number "1" (effects (font (size 1.27 1.27)))))
    (pin input line (at -11.43 -2.54 0) (length 2.54)
      (name "MODE" (effects (font (size 1.27 1.27)))) (number "2" (effects (font (size 1.27 1.27)))))
    (pin passive line (at 11.43 5.08 180) (length 2.54)
      (name "L1" (effects (font (size 1.27 1.27)))) (number "9" (effects (font (size 1.27 1.27)))))
    (pin passive line (at 11.43 2.54 180) (length 2.54)
      (name "L2" (effects (font (size 1.27 1.27)))) (number "7" (effects (font (size 1.27 1.27)))))
    (pin power_out line (at 11.43 7.62 180) (length 2.54)
      (name "VOUT" (effects (font (size 1.27 1.27)))) (number "6" (effects (font (size 1.27 1.27)))))
    (pin input line (at 11.43 -2.54 180) (length 2.54)
      (name "FB" (effects (font (size 1.27 1.27)))) (number "4" (effects (font (size 1.27 1.27)))))
    (pin open_collector line (at 11.43 -5.08 180) (length 2.54)
      (name "PG" (effects (font (size 1.27 1.27)))) (number "5" (effects (font (size 1.27 1.27)))))
    (pin power_in line (at -11.43 -7.62 0) (length 2.54)
      (name "AGND" (effects (font (size 1.27 1.27)))) (number "3" (effects (font (size 1.27 1.27)))))
    (pin power_in line (at 11.43 -7.62 180) (length 2.54)
      (name "GND" (effects (font (size 1.27 1.27)))) (number "8" (effects (font (size 1.27 1.27)))))
  )
)
'''

# TPS22918DBV — TI load switch, SOT-23-6 (DBV). Pinout from TI datasheet
# SLVSDV1 (TPS22918, 5.5-V 2-A load switch with adjustable rise time),
# Pin Configuration: 1 VIN, 2 GND, 3 ON, 4 CT, 5 QOD, 6 VOUT.
TPS22918_SYM = '''
(symbol "TPS22918DBV" (exclude_from_sim no) (in_bom yes) (on_board yes)
  (property "Reference" "U" (at -7.62 8.89 0) (effects (font (size 1.27 1.27))))
  (property "Value" "TPS22918DBV" (at 0 -8.89 0) (effects (font (size 1.27 1.27))))
  (property "Footprint" "Package_TO_SOT_SMD:SOT-23-6" (at 0 -11.43 0) (effects (font (size 1.27 1.27)) hide))
  (property "Datasheet" "https://www.ti.com/lit/ds/symlink/tps22918.pdf" (at 0 0 0) (effects (font (size 1.27 1.27)) hide))
  (property "ki_description" "5.5V 2A load switch, adjustable rise time (CT), quick output discharge, SOT-23-6" (at 0 0 0) (effects (font (size 1.27 1.27)) hide))
  (symbol "TPS22918DBV_0_1"
    (rectangle (start -7.62 7.62) (end 7.62 -7.62)
      (stroke (width 0.254) (type default)) (fill (type background)))
  )
  (symbol "TPS22918DBV_1_1"
    (pin power_in line (at -10.16 5.08 0) (length 2.54)
      (name "VIN" (effects (font (size 1.27 1.27)))) (number "1" (effects (font (size 1.27 1.27)))))
    (pin input line (at -10.16 0 0) (length 2.54)
      (name "ON" (effects (font (size 1.27 1.27)))) (number "3" (effects (font (size 1.27 1.27)))))
    (pin passive line (at -10.16 -5.08 0) (length 2.54)
      (name "CT" (effects (font (size 1.27 1.27)))) (number "4" (effects (font (size 1.27 1.27)))))
    (pin power_out line (at 10.16 5.08 180) (length 2.54)
      (name "VOUT" (effects (font (size 1.27 1.27)))) (number "6" (effects (font (size 1.27 1.27)))))
    (pin passive line (at 10.16 0 180) (length 2.54)
      (name "QOD" (effects (font (size 1.27 1.27)))) (number "5" (effects (font (size 1.27 1.27)))))
    (pin power_in line (at 0 -10.16 90) (length 2.54)
      (name "GND" (effects (font (size 1.27 1.27)))) (number "2" (effects (font (size 1.27 1.27)))))
  )
)
'''


def get_symdef(lib_id):
    libname, name = lib_id.split(':')
    if libname == 'Areg':
        if name == 'TPS63802DLA':
            return parse(TPS63802_SYM)
        if name == 'TPS22918DBV':
            return parse(TPS22918_SYM)
        raise KeyError(lib_id)
    return flatten_symbol(libname, name)

# ------------------------------------------------------------ the design ---
NC = object()          # sentinel: explicit no-connect

GRID = 1.27            # mm — KiCad's schematic connectivity grid

# The design below was laid out on a compact coordinate grid; at 1:1 the
# symbols collide. Connectivity here is by GLOBAL LABELS and power symbols
# placed on pin endpoints — there is no wire geometry — so scaling every
# placement is safe and purely cosmetic. Page grows to A1 to match.
SCALE = 1.5

def snap(v):
    return round(round(v * SCALE / GRID) * GRID, 4)

class Design:
    def __init__(self):
        self.instances = []          # dicts
        self.texts = []              # (text, x, y, size)
        self.symdefs = {}            # lib_id -> flattened def
        self.pin_geo = {}            # lib_id -> {num: (x,y,ang,name,etype)}
        self.by_ref = {}

    def add_text(self, text, x, y, size=3.0):
        self.texts.append((text, snap(x), snap(y), size))

    def place(self, ref, lib_id, x, y, value, footprint='', lcsc='', dnp=False,
              fields=None):
        # Symbol pins sit on 1.27 mm multiples in library coords, so the
        # instance ORIGIN must also be a 1.27 multiple or every pin endpoint
        # lands off-grid (KiCad ERC: endpoint_off_grid, 201 of them).
        x, y = snap(x), snap(y)
        if lib_id not in self.symdefs:
            d = get_symdef(lib_id)
            self.symdefs[lib_id] = d
            self.pin_geo[lib_id] = get_pins(d)
        inst = dict(ref=ref, lib_id=lib_id, x=x, y=y, value=value,
                    footprint=footprint, lcsc=lcsc, dnp=dnp,
                    fields=fields or {}, nets={})
        self.instances.append(inst)
        self.by_ref[ref] = inst
        return inst

    def net(self, ref, pin, netname):
        self.by_ref[ref]['nets'][str(pin)] = netname

    def nets(self, ref, mapping):
        for pin, netname in mapping.items():
            self.net(ref, pin, netname)

d = Design()

# ======================= SECTION: POWER INPUT (USB-C + battery) ==========
d.add_text('USB-C INPUT + PROTECTION', 25, 22)
d.place('J1', 'Connector:USB_C_Receptacle_USB2.0_16P', 40, 60,
        'USB4105-GF-A',
        'Connector_USB:USB_C_Receptacle_GCT_USB4105-xx-A_16P_TopMnt_Horizontal')
d.nets('J1', {
    'A1': 'GND', 'A12': 'GND', 'B1': 'GND', 'B12': 'GND',
    'A4': 'VBUS_C', 'A9': 'VBUS_C', 'B4': 'VBUS_C', 'B9': 'VBUS_C',
    'A5': 'CC1', 'B5': 'CC2',
    'A6': 'USB_DP_C', 'B6': 'USB_DP_C',
    'A7': 'USB_DM_C', 'B7': 'USB_DM_C',
    'A8': NC, 'B8': NC,
    'SH': 'GND',
})
d.place('R1', 'Device:R', 70, 95, '5.1k', 'Resistor_SMD:R_0402_1005Metric', 'C25905')
d.nets('R1', {'1': 'CC1', '2': 'GND'})
d.place('R2', 'Device:R', 80, 95, '5.1k', 'Resistor_SMD:R_0402_1005Metric', 'C25905')
d.nets('R2', {'1': 'CC2', '2': 'GND'})

d.place('F1', 'Device:Polyfuse', 95, 40, '1206L075',
        'Fuse:Fuse_1206_3216Metric')     # LCSC pending: buy-links has 1206L050 only
d.nets('F1', {'1': 'VBUS_C', '2': 'VBUS_F'})
d.place('D4', 'Diode:SMAJ5.0A', 95, 60, 'SMAJ5.0A', 'Diode_SMD:D_SMA', 'C83329')
d.nets('D4', {'1': 'VBUS_F', '2': 'GND'})    # unidirectional: cathode to VBUS_F (layout note)

d.place('U5', 'Power_Protection:TPD4S014', 125, 60, 'TPD4S014DSQR',
        'Package_DFN_QFN:Texas_DSQ0010A_WSON-10-1EP_2x2mm_P0.4mm_EP0.9x1.5mm')
d.nets('U5', {
    '9': 'VBUS_F', '10': 'VBUS_F',
    '1': 'VBUS_PROT', '2': 'VBUS_PROT',
    '3': 'GND',              # /EN low = enabled
    '4': NC,                 # /ACK open-drain, unused
    '5': NC,                 # ID (no ID pin on type-C)
    '6': 'USB_DM_C', '7': 'USB_DP_C',
    '8': 'GND', '11': 'GND',
})
d.place('U6', 'Power_Protection:USBLC6-2SC6', 125, 95, 'USBLC6-2SC6',
        'Package_TO_SOT_SMD:SOT-23-6', 'C7519')
d.nets('U6', {
    '1': 'USB_DM_C', '6': 'USB_DM',
    '3': 'USB_DP_C', '4': 'USB_DP',
    '5': 'VBUS_F', '2': 'GND',
})

d.add_text('SOFT-START (USB) + BATTERY INPUT', 165, 22)
d.place('U7', 'Areg:TPS22918DBV', 190, 45, 'TPS22918DBVR',
        'Package_TO_SOT_SMD:SOT-23-6', 'C131941')
d.nets('U7', {
    '1': 'VBUS_PROT', '2': 'GND',
    '3': 'VBUS_PROT',        # ON tied to VIN: enabled when USB present
    '4': 'U7_CT', '5': NC,   # QOD unused
    '6': 'VSYS',
})
d.place('C15', 'Device:C', 205, 60, '4.7nF',
        'Capacitor_SMD:C_0402_1005Metric')   # ~5 ms rise; S2 sim owns final value
d.nets('C15', {'1': 'U7_CT', '2': 'GND'})

d.place('J2', 'Connector_Generic:Conn_01x02', 170, 80, 'JST-PH-2 (BATT 3xAA)',
        'Connector_JST:JST_PH_B2B-PH-K_1x02_P2.00mm_Vertical')
d.nets('J2', {'1': 'VBAT', '2': 'GND'})
d.place('Q1', 'Transistor_FET:DMG2301L', 195, 80, 'DMG2301L-7',
        'Package_TO_SOT_SMD:SOT-23', 'C102619')
# reverse block + auto power-mux: S=battery, D=VSYS, G=VBUS_PROT (USB
# presence turns the battery path OFF; 100k holds it ON otherwise)
d.nets('Q1', {'1': 'Q1_G', '2': 'VBAT', '3': 'VSYS'})
d.place('R3', 'Device:R', 215, 90, '100k', 'Resistor_SMD:R_0402_1005Metric', 'C25741')
d.nets('R3', {'1': 'Q1_G', '2': 'GND'})
d.place('R4', 'Device:R', 225, 90, '0R', 'Resistor_SMD:R_0402_1005Metric')
d.nets('R4', {'1': 'Q1_G', '2': 'VBUS_PROT'})

# ======================= SECTION: BUCK-BOOST =============================
d.add_text('BUCK-BOOST 3V3 (TPS63802, PFM mode)', 260, 22)
d.place('U4', 'Areg:TPS63802DLA', 295, 50, 'TPS63802DLAR',
        'Package_SON:Texas_S-PVSON-N10')     # verify footprint vs DLA0010A in phase 2
d.nets('U4', {
    '10': 'VSYS', '1': 'VSYS',      # EN = VIN
    '2': 'GND',                     # MODE low = PFM (11 uA Iq)
    '9': 'SW1', '7': 'SW2',
    '6': '+3V3', '4': 'FB63802', '5': NC,   # PG open-drain unused
    '3': 'GND', '8': 'GND',
})
# Contract part (rev-a-design-inputs.md): TDK VLS6045EX-2R2N, 2.2 uH / 5.1 A.
# PHASE-2 GATE: confirm value against the TPS63802 datasheet inductor-selection
# table before fab — TI's typical application is 1.5 uH; 2.2 uH is the audited
# BOM choice and must be shown acceptable at our load, or the BOM changes.
d.place('L1', 'Device:L', 295, 30, '2.2uH VLS6045EX-2R2N',
        'Inductor_SMD:L_TDK_VLS6045EX_VLS6045AF')
d.nets('L1', {'1': 'SW1', '2': 'SW2'})
d.place('C1', 'Device:C', 265, 70, '10uF', 'Capacitor_SMD:C_0603_1608Metric', 'C19702')
d.nets('C1', {'1': 'VSYS', '2': 'GND'})
d.place('C2', 'Device:C', 275, 70, '100nF', 'Capacitor_SMD:C_0402_1005Metric', 'C1525')
d.nets('C2', {'1': 'VSYS', '2': 'GND'})
d.place('R5', 'Device:R', 320, 55, '511k', 'Resistor_SMD:R_0402_1005Metric')
d.nets('R5', {'1': '+3V3', '2': 'FB63802'})
d.place('R6', 'Device:R', 320, 75, '91k', 'Resistor_SMD:R_0402_1005Metric')
d.nets('R6', {'1': 'FB63802', '2': 'GND'})
d.place('C3', 'Device:C', 335, 70, '22uF', 'Capacitor_SMD:C_0603_1608Metric')
d.nets('C3', {'1': '+3V3', '2': 'GND'})
d.place('C4', 'Device:C', 345, 70, '100uF', 'Capacitor_SMD:C_1206_3216Metric')
d.nets('C4', {'1': '+3V3', '2': 'GND'})

# mic supply filter branch: +3V3 -> FB1 -> 3V3_MIC_F -> 10R -> MIC_VDD
d.add_text('MIC SUPPLY FILTER', 355, 22)
d.place('FB1', 'Device:FerriteBead', 365, 40, 'BLM18PG601SN1D',
        'Inductor_SMD:L_0603_1608Metric', 'C1017')
d.nets('FB1', {'1': '+3V3', '2': '3V3_MIC_F'})
d.place('R7', 'Device:R', 365, 60, '10R', 'Resistor_SMD:R_0402_1005Metric')
d.nets('R7', {'1': '3V3_MIC_F', '2': 'MIC_VDD'})
d.place('C5', 'Device:C', 375, 75, '10uF', 'Capacitor_SMD:C_0603_1608Metric', 'C19702')
d.nets('C5', {'1': 'MIC_VDD', '2': 'GND'})

# ======================= SECTION: ESP32 MODULE ===========================
d.add_text('ESP32-S3-WROOM-1 (N8R8)', 430, 22)
d.place('U1', 'RF_Module:ESP32-S3-WROOM-1', 470, 90, 'ESP32-S3-WROOM-1-N8R8',
        'RF_Module:ESP32-S3-WROOM-1')   # LCSC: N16R8 alt = C2913202 (see NOTES)
d.nets('U1', {
    '1': 'GND', '40': 'GND', '41': 'GND',
    '2': '+3V3',
    '3': 'ESP_EN',
    '4': 'MIC_CLK',          # I2S BCK / PDM CLK (0R select)
    '5': 'MIC_WS',
    '6': 'MIC_DATA',         # I2S SD / PDM DATA (0R select)
    '7': 'AMP_DIN_M',        # via 33R
    '8': 'AMP_BCLK_M',       # IO15, via 33R
    '9': 'AMP_LRC_M',        # IO16, via 33R
    '10': 'AMP_SD_MODE',     # IO17, bare
    '11': 'BTN_MAIN',        # IO18
    '12': 'VOL_ADC',         # IO8, ADC1
    '13': 'USB_DM', '14': 'USB_DP',
    '15': 'STRAP_IO3',       # 10k to GND
    '16': 'STRAP_IO46',      # 10k to GND
    '17': 'SD_CD',           # IO9
    '18': 'SD_CS',           # IO10
    '19': 'SD_MOSI_M',       # IO11, via 33R
    '20': 'SD_CLK_M',        # IO12, via 33R
    '21': 'SD_MISO_M',       # IO13, via 33R
    '22': 'LED2_CTL',        # IO14 (spare re-purposed, audit-components §8)
    '23': 'BTN_YES',         # IO21
    '24': 'BTN_NO',          # IO47
    '25': 'LED1_CTL',        # IO48
    '26': 'STRAP_IO45',      # 10k to GND
    '27': 'STRAP_IO0',       # 10k pull-up + factory test pad
    '28': NC, '29': NC, '30': NC,   # IO35/36/37 absent on R8 (octal PSRAM)
    '31': 'LED3_CTL',        # IO38 (spare re-purposed)
    '32': 'TP_IO39', '33': 'TP_IO40', '34': 'TP_IO41', '35': 'TP_IO42',  # JTAG pads
    '36': 'TP_RXD0', '37': 'TP_TXD0',
    '38': 'TP_IO2', '39': 'TP_IO1',
})
# module decoupling at pin 2 + EN RC + strap pulls
d.place('C6', 'Device:C', 425, 150, '22uF', 'Capacitor_SMD:C_0603_1608Metric')
d.nets('C6', {'1': '+3V3', '2': 'GND'})
d.place('C7', 'Device:C', 435, 150, '100nF', 'Capacitor_SMD:C_0402_1005Metric', 'C1525')
d.nets('C7', {'1': '+3V3', '2': 'GND'})
d.place('R8', 'Device:R', 450, 150, '10k', 'Resistor_SMD:R_0402_1005Metric', 'C25744')
d.nets('R8', {'1': '+3V3', '2': 'ESP_EN'})
d.place('C8', 'Device:C', 460, 150, '1uF', 'Capacitor_SMD:C_0402_1005Metric', 'C52923')
d.nets('C8', {'1': 'ESP_EN', '2': 'GND'})
d.place('R9', 'Device:R', 475, 150, '10k', 'Resistor_SMD:R_0402_1005Metric', 'C25744')
d.nets('R9', {'1': '+3V3', '2': 'STRAP_IO0'})
d.place('R10', 'Device:R', 485, 150, '10k', 'Resistor_SMD:R_0402_1005Metric', 'C25744')
d.nets('R10', {'1': 'STRAP_IO3', '2': 'GND'})
d.place('R11', 'Device:R', 495, 150, '10k', 'Resistor_SMD:R_0402_1005Metric', 'C25744')
d.nets('R11', {'1': 'STRAP_IO45', '2': 'GND'})
d.place('R12', 'Device:R', 505, 150, '10k', 'Resistor_SMD:R_0402_1005Metric', 'C25744')
d.nets('R12', {'1': 'STRAP_IO46', '2': 'GND'})

# ======================= SECTION: MICROPHONES (dual footprint) ===========
d.add_text('MIC — DUAL FOOTPRINT: ICS-43434 (I2S, run 1) / IM69D130 (PDM, prod)', 25, 185)
d.place('U3', 'Sensor_Audio:ICS-43434', 50, 215, 'ICS-43434',
        'Sensor_Audio:InvenSense_ICS-43434-6_3.5x2.65mm')
d.nets('U3', {
    '4': 'MIC_SCK_I2S', '1': 'MIC_WS', '6': 'MIC_SD_I2S',
    '2': 'GND',            # L/R hard-wired to GND — REQUIRED (left slot)
    '5': 'MIC_VDD', '3': 'GND',
})
d.place('C9', 'Device:C', 65, 230, '100nF', 'Capacitor_SMD:C_0402_1005Metric', 'C1525')
d.nets('C9', {'1': 'MIC_VDD', '2': 'GND'})
d.place('U8', 'Sensor_Audio:IM69D130', 105, 215, 'IM69D130',
        'Sensor_Audio:Infineon_PG-LLGA-5-1', dnp=True)
d.nets('U8', {
    '3': 'MIC_CLK_PDM', '1': 'MIC_DATA_PDM',
    '4': 'GND',            # SELECT to GND
    '2': 'MIC_VDD', '5': 'GND',
})
d.place('C10', 'Device:C', 120, 230, '100nF', 'Capacitor_SMD:C_0402_1005Metric',
        'C1525', dnp=True)
d.nets('C10', {'1': 'MIC_VDD', '2': 'GND'})
# 0R selects: only ONE mic populated per build
d.place('R13', 'Device:R', 50, 245, '0R', 'Resistor_SMD:R_0402_1005Metric')
d.nets('R13', {'1': 'MIC_CLK', '2': 'MIC_SCK_I2S'})
d.place('R14', 'Device:R', 60, 245, '0R', 'Resistor_SMD:R_0402_1005Metric')
d.nets('R14', {'1': 'MIC_DATA', '2': 'MIC_SD_I2S'})
d.place('R15', 'Device:R', 105, 245, '0R', 'Resistor_SMD:R_0402_1005Metric', dnp=True)
d.nets('R15', {'1': 'MIC_CLK', '2': 'MIC_CLK_PDM'})
d.place('R16', 'Device:R', 115, 245, '0R', 'Resistor_SMD:R_0402_1005Metric', dnp=True)
d.nets('R16', {'1': 'MIC_DATA', '2': 'MIC_DATA_PDM'})

# ======================= SECTION: SD CARD ================================
d.add_text('microSD (internal, Hirose DM3AT push-push)', 160, 185)
d.place('J3', 'Connector:Micro_SD_Card_Det_Hirose_DM3AT', 200, 220,
        'DM3AT-SF-PEJM5', 'Connector_Card:microSD_HC_Hirose_DM3AT-SF-PEJM5')
d.nets('J3', {
    '2': 'SD_CS',       # DAT3/CD
    '3': 'SD_CMD_C',    # CMD  (= MOSI, card side of 33R)
    '4': '+3V3',
    '5': 'SD_CLK_C',
    '6': 'GND',
    '7': 'SD_DAT0_C',   # DAT0 (= MISO, card side of 33R)
    '1': 'SD_DAT2',
    '8': 'SD_DAT1',
    '9': 'GND',         # DET_B
    '10': 'SD_CD',      # DET_A: closes to DET_B on insert -> GPIO9 low
    'SH': 'GND',
})
# five pull-ups
for i, (ref, net_) in enumerate([('R17', 'SD_CS'), ('R18', 'SD_CMD_C'),
                                 ('R19', 'SD_DAT0_C'), ('R20', 'SD_DAT1'),
                                 ('R21', 'SD_DAT2')]):
    d.place(ref, 'Device:R', 235 + i * 8, 210, '10k',
            'Resistor_SMD:R_0402_1005Metric', 'C25744')
    d.nets(ref, {'1': '+3V3', '2': net_})
# card-detect pull-up
d.place('R22', 'Device:R', 275, 210, '10k', 'Resistor_SMD:R_0402_1005Metric', 'C25744')
d.nets('R22', {'1': '+3V3', '2': 'SD_CD'})
# series 33R (driving end)
d.place('R23', 'Device:R', 235, 245, '33R', 'Resistor_SMD:R_0402_1005Metric')
d.nets('R23', {'1': 'SD_CLK_M', '2': 'SD_CLK_C'})
d.place('R24', 'Device:R', 245, 245, '33R', 'Resistor_SMD:R_0402_1005Metric')
d.nets('R24', {'1': 'SD_MOSI_M', '2': 'SD_CMD_C'})
d.place('R25', 'Device:R', 255, 245, '33R', 'Resistor_SMD:R_0402_1005Metric')
d.nets('R25', {'1': 'SD_MISO_M', '2': 'SD_DAT0_C'})
# socket decoupling AT the socket
d.place('C11', 'Device:C', 290, 220, '10uF', 'Capacitor_SMD:C_0603_1608Metric', 'C19702')
d.nets('C11', {'1': '+3V3', '2': 'GND'})
d.place('C12', 'Device:C', 300, 220, '100nF', 'Capacitor_SMD:C_0402_1005Metric', 'C1525')
d.nets('C12', {'1': '+3V3', '2': 'GND'})

# ======================= SECTION: BUTTONS ================================
d.add_text('BUTTONS (B3F-1002, 160 gf)', 320, 185)
btns = [('MAIN', 'BTN_MAIN', 325), ('YES', 'BTN_YES', 355), ('NO', 'BTN_NO', 385)]
for i, (nm, net_, x) in enumerate(btns):
    rp, rs, cc, sw = f'R{26+i*2}', f'R{27+i*2}', f'C{13 if i==0 else 15+i}', f'SW{1+i}'
    node = f'{net_}_N'
    d.place(f'R{26+i*3}', 'Device:R', x, 205, '10k',
            'Resistor_SMD:R_0402_1005Metric', 'C25744')
    d.nets(f'R{26+i*3}', {'1': '+3V3', '2': node})
    d.place(f'R{27+i*3}', 'Device:R', x + 10, 205, '1k',
            'Resistor_SMD:R_0402_1005Metric', 'C11702')
    d.nets(f'R{27+i*3}', {'1': node, '2': net_})
    d.place(f'C{16+i}', 'Device:C', x + 20, 205, '100nF',
            'Capacitor_SMD:C_0402_1005Metric', 'C1525')
    d.nets(f'C{16+i}', {'1': node, '2': 'GND'})
    d.place(f'SW{1+i}', 'Switch:SW_Push', x + 5, 225, f'B3F-1002 {nm}',
            'Button_Switch_THT:SW_TH_Tactile_Omron_B3F-100x')
    d.nets(f'SW{1+i}', {'1': node, '2': 'GND'})

# ======================= SECTION: AMPLIFIER + SPEAKER ====================
d.add_text('AUDIO AMP MAX98357A + SPEAKER (8R, JST-PH)', 420, 185)
d.place('U2', 'Audio:MAX98357A', 455, 220, 'MAX98357AETE+T',
        'Package_DFN_QFN:TQFN-16-1EP_3x3mm_P0.5mm_EP1.6x1.6mm', 'C910544')
d.nets('U2', {
    '1': 'AMP_DIN', '16': 'AMP_BCLK', '14': 'AMP_LRC',
    '4': 'AMP_SD_MODE',      # bare GPIO17; internal 100k pulldown = boots muted
    '2': 'AMP_GAIN',
    '7': '+3V3', '8': '+3V3',
    '3': 'GND', '11': 'GND', '15': 'GND', '17': 'GND',
    '9': 'AMP_OUTP', '10': 'AMP_OUTN',
    '5': NC, '6': NC, '12': NC, '13': NC,
})
# I2S series 33R at the driving (MCU) end
d.place('R35', 'Device:R', 425, 205, '33R', 'Resistor_SMD:R_0402_1005Metric')
d.nets('R35', {'1': 'AMP_BCLK_M', '2': 'AMP_BCLK'})
d.place('R36', 'Device:R', 435, 205, '33R', 'Resistor_SMD:R_0402_1005Metric')
d.nets('R36', {'1': 'AMP_LRC_M', '2': 'AMP_LRC'})
d.place('R37', 'Device:R', 445, 205, '33R', 'Resistor_SMD:R_0402_1005Metric')
d.nets('R37', {'1': 'AMP_DIN_M', '2': 'AMP_DIN'})
# GAIN ladder: one 0402 chosen at the SPL bench (spec §4); both DNP = 9 dB float
d.place('R38', 'Device:R', 480, 205, 'DNP-bench(GAIN->3V3)',
        'Resistor_SMD:R_0402_1005Metric', dnp=True)
d.nets('R38', {'1': '+3V3', '2': 'AMP_GAIN'})
d.place('R39', 'Device:R', 490, 205, 'DNP-bench(GAIN->GND)',
        'Resistor_SMD:R_0402_1005Metric', dnp=True)
d.nets('R39', {'1': 'AMP_GAIN', '2': 'GND'})
# amp supply decoupling (derived: 330uF = 0.625A x 50us / 0.1V, DC-bias derated)
d.place('C19', 'Device:C', 425, 240, '100nF', 'Capacitor_SMD:C_0402_1005Metric', 'C1525')
d.nets('C19', {'1': '+3V3', '2': 'GND'})
d.place('C20', 'Device:C', 435, 240, '22uF', 'Capacitor_SMD:C_0603_1608Metric')
d.nets('C20', {'1': '+3V3', '2': 'GND'})
d.place('C21', 'Device:C_Polarized', 445, 240, '330uF 6.3V polymer',
        'Capacitor_Tantalum_SMD:CP_EIA-7343-31_Kemet-D', 'C79113')
d.nets('C21', {'1': '+3V3', '2': 'GND'})
# speaker legs: ferrite + 1nF C0G each (EMC insurance; parts DNP-able, M9 decides)
d.place('FB2', 'Device:FerriteBead', 495, 220, 'BLM18PG601SN1D',
        'Inductor_SMD:L_0603_1608Metric', 'C1017')
d.nets('FB2', {'1': 'AMP_OUTP', '2': 'SPK_P'})
d.place('FB3', 'Device:FerriteBead', 505, 220, 'BLM18PG601SN1D',
        'Inductor_SMD:L_0603_1608Metric', 'C1017')
d.nets('FB3', {'1': 'AMP_OUTN', '2': 'SPK_N'})
d.place('C22', 'Device:C', 495, 245, '1nF C0G', 'Capacitor_SMD:C_0402_1005Metric',
        dnp=True)
d.nets('C22', {'1': 'SPK_P', '2': 'GND'})
d.place('C23', 'Device:C', 505, 245, '1nF C0G', 'Capacitor_SMD:C_0402_1005Metric',
        dnp=True)
d.nets('C23', {'1': 'SPK_N', '2': 'GND'})
d.place('J4', 'Connector_Generic:Conn_01x02', 525, 220, 'JST-PH-2 (SPK 8R 50mm)',
        'Connector_JST:JST_PH_B2B-PH-K_1x02_P2.00mm_Vertical')
d.nets('J4', {'1': 'SPK_P', '2': 'SPK_N'})

# ======================= SECTION: VOLUME POT + LEDS ======================
d.add_text('VOLUME (detented pot -> ADC1) + STATUS LEDS', 25, 285)
d.place('RV1', 'Device:R_Potentiometer', 45, 310, 'PTV09A-4220F-B103 10k',
        'Potentiometer_THT:Potentiometer_Bourns_PTV09A-1_Single_Vertical')
d.nets('RV1', {'1': '+3V3', '2': 'RV1_W', '3': 'GND'})
d.place('R40', 'Device:R', 65, 310, '1k', 'Resistor_SMD:R_0402_1005Metric', 'C11702')
d.nets('R40', {'1': 'RV1_W', '2': 'VOL_ADC'})
d.place('C24', 'Device:C', 80, 315, '100nF', 'Capacitor_SMD:C_0402_1005Metric', 'C1525')
d.nets('C24', {'1': 'VOL_ADC', '2': 'GND'})
leds = [('LED1_CTL', 'green  listening', '560R', 105),
        ('LED2_CTL', 'blue  thinking', '200R', 130),
        ('LED3_CTL', 'red  error', '560R', 155)]
for i, (net_, nm, rv, x) in enumerate(leds):
    d.place(f'R{41+i}', 'Device:R', x, 305, rv, 'Resistor_SMD:R_0402_1005Metric')
    d.nets(f'R{41+i}', {'1': net_, '2': f'LED{1+i}_A'})
    d.place(f'D{1+i}', 'Device:LED', x, 320, f'LED 0603 {nm}',
            'LED_SMD:LED_0603_1608Metric')
    d.nets(f'D{1+i}', {'2': f'LED{1+i}_A', '1': 'GND'})

# ======================= SECTION: TEST PADS ==============================
d.add_text('TEST PADS (straps, spares, UART, JTAG)', 200, 285)
tps = [('TP1', 'STRAP_IO0', 'IO0/BOOT'), ('TP2', 'TP_IO1', 'IO1'),
       ('TP3', 'TP_IO2', 'IO2'), ('TP4', 'TP_TXD0', 'TXD0'),
       ('TP5', 'TP_RXD0', 'RXD0'), ('TP6', 'TP_IO39', 'IO39/MTCK'),
       ('TP7', 'TP_IO40', 'IO40/MTDO'), ('TP8', 'TP_IO41', 'IO41/MTDI'),
       ('TP9', 'TP_IO42', 'IO42/MTMS'), ('TP10', 'ESP_EN', 'EN'),
       ('TP11', 'GND', 'GND'), ('TP12', '+3V3', '3V3')]
for i, (ref, net_, nm) in enumerate(tps):
    x = 205 + (i % 6) * 20
    y = 305 + (i // 6) * 20
    d.place(ref, 'Connector:TestPoint', x, y, nm,
            'TestPoint:TestPoint_Pad_1.5x1.5mm')
    d.nets(ref, {'1': net_})

# ======================= SECTION: POWER FLAGS ============================
d.add_text('PWR_FLAG anchors', 360, 285)
flags = [('#FLG01', 'GND'), ('#FLG02', 'VBUS_C'), ('#FLG03', 'MIC_VDD'),
         ('#FLG04', 'VBAT'),
         # VBUS_F sits between the PTC (passive) and the TPD4S014 VBUS power
         # inputs — without a flag ERC calls the protection IC undriven.
         ('#FLG05', 'VBUS_F')]
for i, (ref, net_) in enumerate(flags):
    d.place(ref, 'power:PWR_FLAG', 365 + i * 15, 310, 'PWR_FLAG')
    d.nets(ref, {'1': net_})

# ============================================================ emission ---
def new_uuid():
    return str(uuid.uuid4())

ROOT_UUID = '7a2e9f10-0000-4000-8000-000000000001'

def label_angle(pin_ang):
    # outward direction = pin angle + 180 (lib coords; up stays up on screen)
    return (pin_ang + 180) % 360

def emit():
    body = []
    lib_syms = []
    # Power symbols are instantiated lazily inside the instance loop below,
    # i.e. AFTER lib_symbols would have been serialized — register them up
    # front or the file references lib_ids it never defines (KiCad then
    # refuses to load the schematic outright).
    for _pwr in ('power:GND', 'power:+3V3'):
        if _pwr not in d.symdefs:
            _dd = get_symdef(_pwr)
            d.symdefs[_pwr] = _dd
            d.pin_geo[_pwr] = get_pins(_dd)
    # lib_symbols with lib-id names
    for lib_id, sd in d.symdefs.items():
        sd2 = copy.deepcopy(sd)
        # Only the top-level def takes the lib_id; sub-units KEEP their bare
        # "<name>_<unit>_<style>" names. Renaming them makes KiCad refuse to
        # load the file entirely (verified 2026-08-08).
        sd2[1] = q(lib_id)
        lib_syms.append(sd2)

    inst_txt = []
    label_txt = []
    nc_txt = []
    errors = []
    pwr_count = [0]

    for inst in d.instances:
        lib_id = inst['lib_id']
        pins = d.pin_geo[lib_id]
        x0, y0 = inst['x'], inst['y']
        # check completeness
        for num in pins:
            if num not in inst['nets']:
                errors.append(f"{inst['ref']} ({lib_id}) pin {num} "
                              f"[{pins[num][3]}] has no net and no NC")
        for num, target in inst['nets'].items():
            if num not in pins:
                errors.append(f"{inst['ref']} ({lib_id}) maps unknown pin {num}")
        # symbol instance
        props = [
            ('Reference', inst['ref'], x0, y0 - 8, False),
            ('Value', inst['value'], x0, y0 + 8, False),
            ('Footprint', inst['footprint'], x0, y0 + 10, True),
            ('Datasheet', '', x0, y0 + 12, True),
        ]
        if inst['lcsc']:
            props.append(('LCSC', inst['lcsc'], x0, y0 + 14, True))
        for k, v in inst['fields'].items():
            props.append((k, v, x0, y0 + 16, True))
        ptxt = []
        for name, val, px, py, hide in props:
            h = ' (hide yes)' if hide else ''
            ptxt.append(
                f'\t\t(property "{name}" "{esc(val)}" (at {px:g} {py:g} 0)\n'
                f'\t\t\t(effects (font (size 1.27 1.27)){h})\n\t\t)')
        pintxt = []
        for num in pins:
            pintxt.append(f'\t\t(pin "{num}" (uuid "{new_uuid()}"))')
        dnp = 'yes' if inst['dnp'] else 'no'
        in_bom = 'no' if inst['ref'].startswith('#') else 'yes'
        inst_txt.append(
            f'\t(symbol (lib_id "{lib_id}") (at {x0:g} {y0:g} 0) (unit 1)\n'
            f'\t\t(exclude_from_sim no) (in_bom {in_bom}) (on_board yes) '
            f'(dnp {dnp}) (fields_autoplaced yes)\n'
            f'\t\t(uuid "{new_uuid()}")\n'
            + '\n'.join(ptxt) + '\n'
            + '\n'.join(pintxt) + '\n'
            f'\t\t(instances (project "areg-reva" (path "/{ROOT_UUID}" '
            f'(reference "{inst["ref"]}") (unit 1))))\n'
            f'\t)')
        # connectivity
        for num, target in inst['nets'].items():
            if num not in pins:
                continue
            px, py, pang, pname, ptype = pins[num]
            gx, gy = x0 + px, y0 - py
            if target is NC:
                nc_txt.append(f'\t(no_connect (at {gx:g} {gy:g}) (uuid "{new_uuid()}"))')
                continue
            if target == 'GND' and not inst['ref'].startswith('#'):
                pwr_count[0] += 1
                inst_txt.append(power_symbol('power:GND', 'GND', gx, gy, pwr_count[0]))
                continue
            if target == '+3V3' and not inst['ref'].startswith('#'):
                pwr_count[0] += 1
                inst_txt.append(power_symbol('power:+3V3', '+3V3', gx, gy, pwr_count[0]))
                continue
            ang = label_angle(pang)
            just = {0: 'left', 90: 'left', 180: 'right', 270: 'right'}[ang]
            label_txt.append(
                f'\t(global_label "{target}" (shape passive) (at {gx:g} {gy:g} {ang:g}) '
                f'(fields_autoplaced yes)\n'
                f'\t\t(effects (font (size 1.27 1.27)) (justify {just}))\n'
                f'\t\t(uuid "{new_uuid()}")\n'
                f'\t\t(property "Intersheetrefs" "${{INTERSHEET_REFS}}" (at {gx:g} {gy:g} 0)\n'
                f'\t\t\t(effects (font (size 1.27 1.27)) (hide yes))\n\t\t)\n'
                f'\t)')

    if errors:
        for e in errors:
            print('ERROR:', e, file=sys.stderr)
        sys.exit(1)

    text_txt = []
    for t, x, y, size in d.texts:
        text_txt.append(
            f'\t(text "{esc(t)}" (exclude_from_sim no) (at {x:g} {y:g} 0)\n'
            f'\t\t(effects (font (size {size:g} {size:g}) (bold yes)) (justify left bottom))\n'
            f'\t\t(uuid "{new_uuid()}")\n\t)')

    libsym_txt = '\n'.join(serialize(sspec, 2) for sspec in lib_syms)
    out = (
        '(kicad_sch (version 20231120) (generator "areg_generate_schematic")\n'
        f'\t(uuid "{ROOT_UUID}")\n'
        '\t(paper "A1")\n'
        '\t(title_block\n'
        '\t\t(title "Areg AI toy - rev A production schematic")\n'
        '\t\t(date "2026-08-08")\n'
        '\t\t(rev "A")\n'
        '\t\t(company "ArmenianAiToy")\n'
        '\t\t(comment 1 "Contract: hardware/pcb/rev-a-design-inputs.md")\n'
        '\t\t(comment 2 "Net detail: docs/hardware/schematic-spec.md")\n'
        '\t)\n'
        '\t(lib_symbols\n' + libsym_txt + '\n\t)\n'
        + '\n'.join(text_txt) + '\n'
        + '\n'.join(nc_txt) + '\n'
        + '\n'.join(label_txt) + '\n'
        + '\n'.join(inst_txt) + '\n'
        '\t(sheet_instances (path "/" (page "1")))\n'
        ')\n'
    )
    return out

def esc(sx):
    return sx.replace('\\', '\\\\').replace('"', '\\"')

_pwr_defs_added = set()
def power_symbol(lib_id, netname, gx, gy, idx):
    if lib_id not in d.symdefs:
        dd = get_symdef(lib_id)
        d.symdefs[lib_id] = dd
        d.pin_geo[lib_id] = get_pins(dd)
    ref = f'#PWR{idx:03d}'
    return (
        f'\t(symbol (lib_id "{lib_id}") (at {gx:g} {gy:g} 0) (unit 1)\n'
        f'\t\t(exclude_from_sim no) (in_bom no) (on_board yes) (dnp no) (fields_autoplaced yes)\n'
        f'\t\t(uuid "{new_uuid()}")\n'
        f'\t\t(property "Reference" "{ref}" (at {gx:g} {gy + 5:g} 0)\n'
        f'\t\t\t(effects (font (size 1.27 1.27)) (hide yes))\n\t\t)\n'
        f'\t\t(property "Value" "{netname}" (at {gx:g} {gy + 3.5:g} 0)\n'
        f'\t\t\t(effects (font (size 1.27 1.27)))\n\t\t)\n'
        f'\t\t(property "Footprint" "" (at {gx:g} {gy:g} 0)\n'
        f'\t\t\t(effects (font (size 1.27 1.27)) (hide yes))\n\t\t)\n'
        f'\t\t(property "Datasheet" "" (at {gx:g} {gy:g} 0)\n'
        f'\t\t\t(effects (font (size 1.27 1.27)) (hide yes))\n\t\t)\n'
        f'\t\t(pin "1" (uuid "{new_uuid()}"))\n'
        f'\t\t(instances (project "areg-reva" (path "/{ROOT_UUID}" '
        f'(reference "{ref}") (unit 1))))\n'
        f'\t)')

def write_project_symbol_lib():
    """Emit Areg.kicad_sym from the same strings the schematic embeds, so the
    project library and the in-file definitions can never drift. Without a
    real library on disk (+ its sym-lib-table row) KiCad warns
    'configuration does not include the symbol library Areg' on every
    instance of a custom part."""
    body = TPS63802_SYM.strip() + '\n' + TPS22918_SYM.strip()
    out = ('(kicad_symbol_lib (version 20231120) (generator "areg_generate_schematic")\n'
           + body + '\n)\n')
    p = os.path.join(OUT_DIR, 'Areg.kicad_sym')
    with open(p, 'w', encoding='utf-8', newline='\n') as f:
        f.write(out)
    return p


if __name__ == '__main__':
    txt = emit()
    outp = os.path.join(OUT_DIR, 'areg-reva.kicad_sch')
    with open(outp, 'w', encoding='utf-8', newline='\n') as f:
        f.write(txt)
    libp = write_project_symbol_lib()
    print('wrote', outp, len(d.instances), 'symbols')
    print('wrote', libp)

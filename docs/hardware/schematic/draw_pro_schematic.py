# Areg toy — professional one-sheet schematic, hand-placed SVG.
# Discipline: orthogonal wires only, net labels instead of long wires,
# every coordinate chosen, no text collisions.
import io

W, H = 2680, 1780
S = []  # svg fragments

def line(x1, y1, x2, y2, w=2.2, color='black'):
    S.append(f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{color}" stroke-width="{w}"/>')

def text(x, y, t, size=15, anchor='middle', bold=False, color='black'):
    wgt = ' font-weight="bold"' if bold else ''
    for i, ln in enumerate(t.split('\n')):
        S.append(f'<text x="{x}" y="{y + i*(size+3)}" font-size="{size}" font-family="Arial,Helvetica,sans-serif" text-anchor="{anchor}"{wgt} fill="{color}">{ln}</text>')

def dot(x, y, r=4):
    S.append(f'<circle cx="{x}" cy="{y}" r="{r}" fill="black"/>')

def gnd(x, y):
    line(x, y, x, y+12); line(x-14, y+12, x+14, y+12)
    line(x-9, y+18, x+9, y+18); line(x-4, y+24, x+4, y+24)

def vdd(x, y, name='+3V3'):
    line(x, y, x, y-14); line(x-10, y-14, x+10, y-14)
    text(x, y-20, name, 14, bold=True)

def res_h(x, y, label, value):  # horizontal IEC resistor, 56 wide, centered at x..x+56
    S.append(f'<rect x="{x}" y="{y-9}" width="56" height="18" fill="white" stroke="black" stroke-width="2"/>')
    text(x+28, y-14, label, 13); text(x+28, y+26, value, 13)

def res_v(x, y, label, value):  # vertical resistor, 56 tall, from y to y+56
    S.append(f'<rect x="{x-9}" y="{y}" width="18" height="56" fill="white" stroke="black" stroke-width="2"/>')
    text(x+16, y+24, label, 13, 'start'); text(x+16, y+40, value, 13, 'start')

def cap_v(x, y, label):  # vertical capacitor at y..y+18
    line(x, y, x, y+6); line(x-11, y+6, x+11, y+6)
    line(x-11, y+12, x+11, y+12); line(x, y+12, x, y+18)
    text(x+15, y+13, label, 13, 'start')

def netflag(x, y, name, direction='right'):
    # net label: small open arrow + name
    if direction == 'right':
        S.append(f'<path d="M {x} {y-8} L {x+14} {y} L {x} {y+8}" fill="none" stroke="black" stroke-width="1.8"/>')
        text(x+20, y+5, name, 14, 'start', bold=True)
    else:
        S.append(f'<path d="M {x} {y-8} L {x-14} {y} L {x} {y+8}" fill="none" stroke="black" stroke-width="1.8"/>')
        text(x-20, y+5, name, 14, 'end', bold=True)

def icbox(x, y, w, h, title, lpins, rpins, stub=26):
    """lpins/rpins: list of (pinname, netlabel-or-None). Returns dict name->(x,y)."""
    S.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="white" stroke="black" stroke-width="2.6"/>')
    text(x+w/2, y+22, title, 15, bold=True)
    pos = {}
    n = len(lpins)
    for i, (pn, net) in enumerate(lpins):
        py = y + 48 + i * ((h-60)/max(n-1,1) if n > 1 else 0)
        line(x-stub, py, x, py)
        text(x+6, py+5, pn, 13, 'start')
        pos[pn] = (x-stub, py)
        if net:
            netflag(x-stub, py, net, 'left')
    n = len(rpins)
    for i, (pn, net) in enumerate(rpins):
        py = y + 48 + i * ((h-60)/max(n-1,1) if n > 1 else 0)
        line(x+w, py, x+w+stub, py)
        text(x+w-6, py+5, pn, 13, 'end')
        pos[pn] = (x+w+stub, py)
        if net:
            netflag(x+w+stub, py, net, 'right')
    return pos

def frame(x, y, w, h, title):
    S.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="none" stroke="#777" stroke-width="1.6" stroke-dasharray="7 5"/>')
    S.append(f'<rect x="{x}" y="{y-26}" width="{len(title)*11+20}" height="26" fill="#eee" stroke="#777" stroke-width="1.2"/>')
    text(x+10, y-7, title, 16, 'start', bold=True)

# ==================================================================
S.append(f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}">')
S.append(f'<rect width="{W}" height="{H}" fill="white"/>')
text(W/2, 44, 'AREG TOY — FULL ELECTRICAL SCHEMATIC (rev A)', 30, bold=True)
text(W/2, 74, 'Single +3V3 rail  ·  net labels connect same-named points  ·  docs/hardware/schematic-spec.md carries the derivations', 16)

# ---------------- A. POWER (top-left) ----------------
frame(60, 130, 760, 300, 'A · POWER — battery to +3V3')
# battery
bx, by = 110, 260
S.append(f'<rect x="{bx-14}" y="{by-40}" width="28" height="80" fill="white" stroke="black" stroke-width="2.4"/>')
line(bx-24, by-52, bx+24, by-52); line(bx-12, by-64, bx+12, by-64)
text(bx, by+64, 'BT1\n3×AA (4.5→3.0 V)', 14)
line(bx, by-64, bx, by-80); line(bx, by-80, bx+70, by-80)
# Q1 P-FET (simplified two-terminal block for readability)
S.append(f'<rect x="{bx+70}" y="{by-98}" width="120" height="36" fill="white" stroke="black" stroke-width="2.2"/>')
text(bx+130, by-75, 'Q1 DMG2301L', 13); text(bx+130, by-104, 'reverse block (P-FET)', 13)
line(bx+190, by-80, bx+250, by-80); dot(bx+250, by-80)
cap_v(bx+250, by-72, 'C10 10µF'); line(bx+250, by-80, bx+250, by-72); gnd(bx+250, by-54+18)
line(bx+250, by-80, bx+310, by-80)
# U4
u4 = icbox(bx+310, by-140, 190, 120, 'U4 TPS63802', [('VIN', None)], [('VOUT', None)])
text(bx+405, by-95, 'buck-boost\n3.3 V · 2 A', 13)
# inductor on top
line(bx+360, by-140, bx+360, by-168); line(bx+450, by-140, bx+450, by-168)
S.append(f'<path d="M {bx+360} {by-168} A 15 15 0 0 1 {bx+390} {by-168} A 15 15 0 0 1 {bx+420} {by-168} A 15 15 0 0 1 {bx+450} {by-168}" fill="none" stroke="black" stroke-width="2.2"/>')
text(bx+405, by-182, 'L1 1.5 µH', 13)
line(bx+405, by-20, bx+405, by-4); gnd(bx+405, by-4)   # U4 GND
# VIN wire
line(u4['VIN'][0], u4['VIN'][1], bx+310-26, by-80)
# VOUT rail
ox, oy = u4['VOUT']
line(ox, oy, ox+40, oy); dot(ox+40, oy)
cap_v(ox+40, oy, 'C11 100µF'); gnd(ox+40, oy+18)
line(ox+40, oy, ox+110, oy)
vdd(ox+110, oy); text(ox+150, oy+5, '→ every +3V3 flag on this sheet', 13, 'start')

# ---------------- B. USB-C (bottom-left) ----------------
frame(60, 500, 760, 330, 'B · USB-C input and protection (charging builds)')
j1 = icbox(120, 560, 150, 210, 'J1 USB-C', [], [('VBUS', None), ('CC1', None), ('CC2', None), ('GND', None)])
x, y = j1['GND']; line(x, y, x+14, y); gnd(x+14, y)
x, y = j1['CC1']; res_h(x+8, y, 'R60', '5.1 kΩ'); line(x+64, y, x+80, y); gnd(x+80, y)
x, y = j1['CC2']; res_h(x+8, y, 'R61', '5.1 kΩ'); line(x+64, y, x+80, y); gnd(x+80, y)
text(x+150, y+8, '(two separate resistors —\nnever one shared)', 13, 'start')
x, y = j1['VBUS']
S.append(f'<rect x="{x+10}" y="{y-9}" width="64" height="18" fill="white" stroke="black" stroke-width="2"/>')
text(x+42, y-14, 'F1 PTC 0.5 A', 13)
line(x+74, y, x+120, y); dot(x+120, y)
line(x+120, y, x+120, y+34)
S.append(f'<path d="M {x+108} {y+34} L {x+132} {y+34} L {x+120} {y+56} Z" fill="none" stroke="black" stroke-width="2"/>')
line(x+108, y+56, x+132, y+56); gnd(x+120, y+58)
text(x+150, y+44, 'D60 SMAJ5.0A + U5 TPD4S014\n(surge + CC/D± ESD)', 13, 'start')
line(x+120, y, x+180, y)
u6 = icbox(x+180, y-40, 170, 80, 'U6 TPS22918', [('IN', None)], [('OUT', None)])
text(x+265, y+22, 'soft-start switch', 13)
ox2, oy2 = u6['OUT']; netflag(ox2, oy2, 'VSYS  → U4 VIN (or BQ24074 on Li-ion)', 'right')

# ---------------- C. MCU (center) ----------------
frame(900, 130, 820, 700, 'C · ESP32-S3 MCU')
u1 = icbox(1030, 190, 560, 580, 'U1  ESP32-S3-WROOM-1-N16R8   (PCB antenna — keep-out: no copper / magnet / battery)',
    [('EN', None), ('IO0', None), ('IO3', None), ('IO45', None), ('IO46', None),
     ('IO18', 'BTN_MAIN'), ('IO21', 'BTN_YES'), ('IO47', 'BTN_NO'), ('IO8', 'VOL_ADC'), ('IO48', 'LED_DIN')],
    [('IO4', 'MIC_SCK'), ('IO5', 'MIC_WS'), ('IO6', 'MIC_SD'),
     ('IO15', 'AMP_BCLK'), ('IO16', 'AMP_LRC'), ('IO7', 'AMP_DIN'), ('IO17', 'AMP_MUTE'),
     ('IO10', 'SD_CS'), ('IO11', 'SD_MOSI'), ('IO12', 'SD_SCK'), ('IO13', 'SD_MISO'), ('IO9', 'SD_CD')])
# 3V3 + decoupling at top
line(1310, 190, 1310, 160); vdd(1310, 160)
line(1310, 175, 1390, 175); cap_v(1390, 175, 'C1 22µF + C2 100nF  (≤3 mm from pin 2)'); gnd(1390, 193)
# GND at bottom
line(1310, 770, 1310, 795); gnd(1310, 795)
# EN + straps
x, y = u1['EN']; res_h(x-70, y, 'R1', '10 kΩ'); line(x-70, y, x-86, y); vdd(x-86, y)
cap_v(x-8, y+6, 'C3 1µF'); line(x-8, y, x-8, y+6); gnd(x-8, y+24)
x, y = u1['IO0']; res_h(x-70, y, 'R2', '10 kΩ'); line(x-70, y, x-86, y); vdd(x-86, y); text(x-140, y+5, 'test pad only', 12, 'end')
for pin, rn in [('IO3','R3'), ('IO45','R4'), ('IO46','R5')]:
    x, y = u1[pin]; res_h(x-70, y, rn, '10 kΩ'); line(x-70, y, x-86, y); gnd(x-86, y)

# ---------------- D. AUDIO (right) ----------------
frame(1800, 130, 820, 700, 'D · AUDIO')
mic = icbox(1900, 190, 260, 190, 'U3 INMP441 (mic)',
    [('SCK', 'MIC_SCK'), ('WS', 'MIC_WS'), ('SD', 'MIC_SD'), ('L/R', None)], [])
x, y = mic['L/R']; line(x, y, x-14, y); gnd(x-14, y); text(1900, 405, 'L/R hard-wired to GND (required)', 13, 'start')
# mic supply
line(2030, 190, 2030, 168); dot(2030, 168)
res_h(2030-98, 168, 'R20', '10 Ω'); line(2030-98, 168, 2030-114, 168); vdd(2030-114, 168)
cap_v(2140, 168, 'C20 10µF + C21 100nF'); line(2030, 168, 2140, 168); gnd(2140, 186)
text(1900, 425, '(alt part if EOL: Knowles SPH0645LM4H-B)', 13, 'start')

amp = icbox(1900, 440, 260, 240, 'U2 MAX98357A (amp)',
    [('BCLK', 'AMP_BCLK'), ('LRC', 'AMP_LRC'), ('DIN', 'AMP_DIN'), ('SD_MODE', 'AMP_MUTE'), ('GAIN', None)],
    [('OUT+', None), ('OUT-', None)])
# amp supply
line(2030, 440, 2030, 416); dot(2030, 416)
line(2030, 416, 1980, 416); vdd(1980, 416)
cap_v(2140, 416, 'C30 100nF + C31 22µF + C32 330µF'); line(2030, 416, 2140, 416); gnd(2140, 434)
x, y = amp['GAIN']; res_h(x-70, y, 'R30', 'gain-set'); line(x-70, y, x-86, y)
text(x-92, y+5, 'value from SPL test:\n0 dBFS ≤ 78 dB @ 50 cm', 12, 'end')
# speaker
xp, yp = amp['OUT+']; xn, yn = amp['OUT-']
S.append(f'<rect x="{xp+30}" y="{yp-9}" width="44" height="18" fill="white" stroke="black" stroke-width="2"/>'); text(xp+52, yp-14, 'FB1', 12)
S.append(f'<rect x="{xn+30}" y="{yn-9}" width="44" height="18" fill="white" stroke="black" stroke-width="2"/>'); text(xn+52, yn+26, 'FB2', 12)
line(xp+74, yp, xp+120, yp); line(xn+74, yn, xn+120, yn)
sx = xp+120; sy = (yp+yn)/2
line(sx, yp, sx, yn)
S.append(f'<rect x="{sx}" y="{sy-18}" width="20" height="36" fill="white" stroke="black" stroke-width="2.2"/>')
S.append(f'<path d="M {sx+20} {sy-18} L {sx+52} {sy-40} L {sx+52} {sy+40} L {sx+20} {sy+18} Z" fill="none" stroke="black" stroke-width="2.2"/>')
text(sx+30, sy+64, 'LS1  50 mm · 8 Ω · ≥88 dB/W/m\nsealed 50–100 cm³ chamber', 13, 'start')

# ---------------- E. microSD (bottom center) ----------------
frame(900, 900, 820, 330, 'E · microSD (internal socket — child cannot reach)')
j2 = icbox(1030, 960, 300, 230, 'J2 microSD — Molex 5031821852 (push-pull)',
    [('CS', 'SD_CS'), ('CMD/MOSI', 'SD_MOSI'), ('CLK', 'SD_SCK'), ('DAT0/MISO', 'SD_MISO'), ('CD', 'SD_CD')], [])
line(1180, 960, 1180, 936); vdd(1180, 936)
cap_v(1320, 940, 'C40 10µF + 100nF at the socket'); line(1180, 942, 1320, 942); gnd(1320, 958)
line(1180, 1190, 1180, 1212); gnd(1180, 1212)
text(1400, 1030, 'R40–R43: 33 Ω series in each signal line', 14, 'start')
text(1400, 1055, 'R44–R48: 10 kΩ → +3V3 pull-ups on', 14, 'start')
text(1400, 1080, 'CS, CMD, DAT0, DAT1, DAT2', 14, 'start')
text(1400, 1105, '(floating DAT1/2 can flip the card out of SPI mode)', 13, 'start')
text(1400, 1150, 'Card: 8 GB industrial/pSLC — consumer cards\ncorrupt on power-loss mid-write', 13, 'start')

# ---------------- F. CONTROLS (bottom left) ----------------
frame(60, 900, 760, 460, 'F · CONTROLS')
yb = 960
for label, net in [('SW1  MAIN', 'BTN_MAIN'), ('SW2  YES (green)', 'BTN_YES'), ('SW3  NO (red)', 'BTN_NO')]:
    vdd(120, yb)
    res_h(120, yb, '', '10 kΩ'); dot(196, yb)
    res_h(216, yb, '', '1 kΩ'); line(196, yb, 216, yb)
    line(272, yb, 300, yb); netflag(300, yb, net, 'right')
    line(196, yb, 196, yb+22); cap_v(196, yb+22, '100nF'); gnd(196, yb+40)
    # button hangs off the junction, clear of the cap
    line(196, yb, 196, yb-26); line(196, yb-26, 300, yb-26)
    S.append(f'<line x1="{300}" y1="{yb-26}" x2="{330}" y2="{yb-40}" stroke="black" stroke-width="2.2"/>')
    line(330, yb-26, 344, yb-26); gnd(344, yb-26)
    text(370, yb-22, label, 13, 'start')
    yb += 110
# volume pot
vdd(560, 960)
S.append(f'<rect x="{551}" y="{970}" width="18" height="70" fill="white" stroke="black" stroke-width="2"/>')
line(560, 960, 560, 970); line(560, 1040, 560, 1058); gnd(560, 1058)
S.append(f'<path d="M {569} {1005} L 594 1005" stroke="black" stroke-width="2" fill="none"/>')
S.append(f'<path d="M 588 999 L 594 1005 L 588 1011" stroke="black" stroke-width="2" fill="none"/>')
text(560, 1090, 'RV1 10 kΩ lin DETENTED\nBourns PTV09A-4020F-B103', 13)
res_h(600, 1005, 'R', '1 kΩ'); dot(676, 1005); line(656, 1005, 676, 1005)
line(676, 1005, 700, 1005); netflag(700, 1005, 'VOL_ADC', 'right')
line(676, 1005, 676, 1027); cap_v(676, 1027, '100nF'); gnd(676, 1045)
# LED
vdd(560, 1180)
S.append(f'<rect x="{520}" y="{1190}" width="150" height="70" fill="white" stroke="black" stroke-width="2.2"/>')
text(595, 1215, 'D1 WS2812B', 14, bold=True); text(595, 1235, 'status LED', 13)
line(560, 1180, 560, 1190)
line(520, 1225, 480, 1225); netflag(480, 1225, 'LED_DIN', 'left')
line(595, 1260, 595, 1280); gnd(595, 1280)
text(690, 1225, 'via U8 74LVC1T45 shifter\non 5 V builds only', 13, 'start')

# ---------------- Notes ----------------
frame(1800, 900, 820, 460, 'G · DESIGN RULES (why, in one line each)')
notes = [
 'Buck-boost is mandatory: every battery chemistry crosses 3.3 V while discharging.',
 'No 5 V rail anywhere IF speaker measures ≥88 dB/W/m — sensitivity and rail are one decision.',
 'The old "SD needs 5 V" was the hobby module\'s own regulator dropout — SD is a 3.3 V part.',
 'Main button on IO18, never IO0: a child holding IO0 through power-on = download mode = "dead toy".',
 'R30 (amp GAIN) is the hardware loudness ceiling — set by measurement, EN 71-1 limit 80 dB @ 50 cm.',
 'Star ground: speaker return never shares copper with mic ground.',
 'Antenna keep-out: no copper, no battery, no speaker magnet near the module antenna.',
 'ADC1 only for the volume pot — ADC2 pins are dead while Wi-Fi is active.',
 'No coin cell anywhere, by design (US toy-safety rule 16 CFR 1263).',
 'Net labels (▷ NAME) connect all same-named points — this is how EDA schematics avoid wire spaghetti.',
]
for i, n in enumerate(notes):
    text(1830, 950 + i*40, '•  ' + n, 14, 'start')

S.append('</svg>')
io.open(r'C:\Users\HAYK~1.MAR\AppData\Local\Temp\claude\C--Users-hayk-margaryan-Documents-Projects-ArmenianAiToy\f2e961ff-3dfc-4bdc-8e81-98b085ccee3b\scratchpad\areg-schematic-pro.svg', 'w', encoding='utf-8').write('\n'.join(S))
print('pro sheet written,', len(S), 'elements')

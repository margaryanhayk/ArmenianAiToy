# Areg toy — production schematic rev A, drawn with schemdraw.
# Five sheets as separate SVGs: power, audio, MCU+SD, controls, USB.
import schemdraw
import schemdraw.elements as elm

OUT = r'C:\Users\HAYK~1.MAR\AppData\Local\Temp\claude\C--Users-hayk-margaryan-Documents-Projects-ArmenianAiToy\f2e961ff-3dfc-4bdc-8e81-98b085ccee3b\scratchpad'

schemdraw.config(fontsize=11)

# ------------------------------------------------------------------
# SHEET 1 — POWER: battery -> protection -> buck-boost -> 3V3 rail
# ------------------------------------------------------------------
with schemdraw.Drawing(file=OUT + r'\sheet1-power.svg', show=False) as d:
    d += elm.Label().label('AREG TOY — SHEET 1/5 — POWER TREE (single 3V3 rail)', loc='top').at((0, 8))

    bat = d.add(elm.BatteryCell().up().at((0, 0)).label('BT1\n3×AA (4.5→3.0 V)\nor 1S Li-ion (4.2→3.0 V)', loc='left'))
    d += elm.Line().up().length(1)
    d += elm.Line().right().length(2)
    # reverse-block P-FET
    q = d.add(elm.PFet().anchor('source').theta(0).label('Q1\nDMG2301L\nreverse block', loc='top'))
    d += elm.Line().at(q.drain).right().length(2)
    dot_in = d.add(elm.Dot())
    d += elm.Capacitor().down().length(2.2).label('C10\n10 µF', loc='bottom')
    d += elm.Ground()
    d += elm.Line().at(dot_in.start).right().length(1.5)

    # Buck-boost as an IC box
    u4 = d.add(elm.Ic(pins=[elm.IcPin(name='VIN', side='left'),
                            elm.IcPin(name='GND', side='bottom'),
                            elm.IcPin(name='L1', side='top'),
                            elm.IcPin(name='L2', side='top'),
                            elm.IcPin(name='VOUT', side='right'),
                            elm.IcPin(name='EN', side='left')],
                      edgepadW=1.2, edgepadH=.8,
                      label='U4\nTPS63802\nbuck-boost 2 A'))
    d += elm.Inductor2().at(u4.L1).to(u4.L2).label('L1  1.5 µH', loc='top')
    d += elm.Ground().at(u4.GND)
    d += elm.Line().at(u4.EN).left().length(.6).label('to VIN (always on)', loc='left')

    d += elm.Line().at(u4.VOUT).right().length(1.5)
    outdot = d.add(elm.Dot())
    d += elm.Capacitor().down().length(2.2).label('C11\n100 µF', loc='bottom')
    d += elm.Ground()
    d += elm.Line().at(outdot.start).right().length(1.5)
    d += elm.Dot()
    rail = d.add(elm.Line().right().length(1))
    d += elm.Label().label('+3V3  (one rail: MCU, mic, SD, amp*)', loc='right')

    d += elm.Label().at((0, -4.5)).label(
        '* Amp on 3V3 requires speaker ≥88 dB/W/m (power-tree.md §4).\n'
        'Rule: every chemistry crosses 3.3 V during discharge → buck-boost is mandatory;\n'
        'a plain buck drops out below ~3.4 V in, an LDO (AMS1117, 1.1 V dropout) needs ≥4.4 V in.',
        loc='right', halign='left')

# ------------------------------------------------------------------
# SHEET 2 — AUDIO: INMP441 mic + MAX98357A amp + speaker
# ------------------------------------------------------------------
with schemdraw.Drawing(file=OUT + r'\sheet2-audio.svg', show=False) as d:
    d += elm.Label().label('AREG TOY — SHEET 2/5 — AUDIO', loc='top').at((0, 10))

    # Mic supply RC
    d += elm.Label().at((0, 8)).label('+3V3', loc='left')
    d += elm.Line().at((0, 8)).right().length(.8)
    d += elm.Resistor().right().label('R20  10 Ω', loc='top')
    micv = d.add(elm.Dot())
    d += elm.Capacitor().down().length(1.8).label('C20 10 µF\n∥ C21 100 nF', loc='bottom')
    d += elm.Ground()

    u3 = d.add(elm.Ic(pins=[elm.IcPin(name='VDD', side='top'),
                            elm.IcPin(name='SCK', side='left'),
                            elm.IcPin(name='WS', side='left'),
                            elm.IcPin(name='SD', side='right'),
                            elm.IcPin(name='L/R', side='bottom'),
                            elm.IcPin(name='GND', side='bottom')],
                      edgepadW=1.1, edgepadH=.7,
                      label='U3\nINMP441\n(EOL risk — alt:\nSPH0645LM4H)')
             .anchor('VDD').at((micv.start[0], micv.start[1] - 0)).down())
    d += elm.Line().at(u3.VDD).to(micv.start)
    d += elm.Ground().at(u3.GND)
    d += elm.Line().at(u3.__getattr__('L/R')).down().length(.5)
    d += elm.Ground().label('MUST tie to GND\n(floating = fake audio)', loc='right')
    d += elm.Resistor().at(u3.SCK).left().length(2.5).label('R21 33Ω', loc='top')
    d += elm.Label().label('GPIO4', loc='left')
    d += elm.Resistor().at(u3.WS).left().length(2.5).label('R22 33Ω', loc='top')
    d += elm.Label().label('GPIO5', loc='left')
    d += elm.Resistor().at(u3.SD).right().length(2.5).label('R23 33Ω', loc='top')
    d += elm.Label().label('GPIO6', loc='right')

    # Amp
    u2 = d.add(elm.Ic(pins=[elm.IcPin(name='VDD', side='top'),
                            elm.IcPin(name='BCLK', side='left'),
                            elm.IcPin(name='LRC', side='left'),
                            elm.IcPin(name='DIN', side='left'),
                            elm.IcPin(name='SD_MODE', side='left'),
                            elm.IcPin(name='GAIN', side='bottom'),
                            elm.IcPin(name='OUT+', side='right'),
                            elm.IcPin(name='OUT-', side='right'),
                            elm.IcPin(name='GND', side='bottom')],
                      edgepadW=1.3, edgepadH=.9,
                      label='U2\nMAX98357A\nclass-D')
             .at((9, 2)))
    d += elm.Line().at(u2.VDD).up().length(.8)
    ampv = d.add(elm.Dot())
    d += elm.Label().label('+3V3', loc='top')
    d += elm.Capacitor().at(ampv.start).right().length(1.6).label('C30 100nF + C31 22µF\n+ C32 330 µF polymer', loc='right')
    d += elm.Ground()
    d += elm.Ground().at(u2.GND)
    d += elm.Label().at(u2.BCLK).label('GPIO15 ', loc='left')
    d += elm.Label().at(u2.LRC).label('GPIO16 ', loc='left')
    d += elm.Label().at(u2.DIN).label('GPIO7 ', loc='left')
    d += elm.Label().at(u2.SD_MODE).label('GPIO17 (mute) ', loc='left')
    d += elm.Resistor().at(u2.GAIN).down().length(1.6).label('R30 = loudness ceiling\n(value set by SPL test:\n0 dBFS ≤ 78 dB @ 50 cm)', loc='bottom')
    d += elm.Line().down().length(.3)
    d += elm.Label().label('to VDD or GND per test', loc='bottom')

    d += elm.Line().at(u2.__getattr__('OUT+')).right().length(.8)
    fb1 = d.add(elm.Inductor2(n=2).right().label('FB1 ferrite', loc='top'))
    d += elm.Line().at(u2.__getattr__('OUT-')).right().length(.8)
    fb2 = d.add(elm.Inductor2(n=2).right().label('FB2 ferrite', loc='bottom'))
    spk = d.add(elm.Speaker().at((fb1.end[0] + 1.2, (fb1.end[1] + fb2.end[1]) / 2)).right()
                .label('LS1  50 mm 8 Ω\n≥88 dB/W/m, sealed 50–100 cm³', loc='right'))
    d += elm.Line().at(fb1.end).to(spk.in1)
    d += elm.Line().at(fb2.end).to(spk.in2)

# ------------------------------------------------------------------
# SHEET 3 — MCU + microSD
# ------------------------------------------------------------------
with schemdraw.Drawing(file=OUT + r'\sheet3-mcu-sd.svg', show=False) as d:
    d += elm.Label().label('AREG TOY — SHEET 3/5 — ESP32-S3 + microSD', loc='top').at((0, 12))

    u1 = d.add(elm.Ic(pins=[elm.IcPin(name='3V3', side='top'),
                            elm.IcPin(name='EN', side='left'),
                            elm.IcPin(name='IO0', side='left'),
                            elm.IcPin(name='IO3', side='left'),
                            elm.IcPin(name='IO45', side='left'),
                            elm.IcPin(name='IO46', side='left'),
                            elm.IcPin(name='IO10_CS', side='right'),
                            elm.IcPin(name='IO11_MOSI', side='right'),
                            elm.IcPin(name='IO12_SCK', side='right'),
                            elm.IcPin(name='IO13_MISO', side='right'),
                            elm.IcPin(name='IO9_CD', side='right'),
                            elm.IcPin(name='GND', side='bottom')],
                      edgepadW=1.6, edgepadH=1.0,
                      label='U1\nESP32-S3-WROOM-1\nN16R8 (PCB antenna)\nantenna keep-out:\nno copper/magnet/battery')
             .at((3, 4)))
    d += elm.Line().at(u1.__getattr__('3V3')).up().length(.7)
    v = d.add(elm.Dot())
    d += elm.Label().label('+3V3', loc='top')
    d += elm.Capacitor().at(v.start).right().length(1.6).label('C1 22 µF + C2 100 nF\n≤3 mm from pin (Espressif Fig.7)', loc='right')
    d += elm.Ground()
    d += elm.Ground().at(u1.GND)

    # EN RC
    d += elm.Resistor().at(u1.EN).left().length(2).label('R1 10 kΩ', loc='top')
    d += elm.Label().label('+3V3', loc='left')
    d += elm.Capacitor().at(u1.EN).down().length(1.6).label('C3 1 µF', loc='bottom')
    d += elm.Ground()
    # straps
    d += elm.Resistor().at(u1.IO0).left().length(2).label('R2 10 kΩ→3V3\nIO0 = TEST PAD ONLY\n(button moved to IO18!)', loc='left')
    d += elm.Resistor().at(u1.IO3).left().length(2).label('R3 10 kΩ→GND\n(no internal pull!)', loc='left')
    d += elm.Resistor().at(u1.IO45).left().length(2).label('R4 10 kΩ→GND', loc='left')
    d += elm.Resistor().at(u1.IO46).left().length(2).label('R5 10 kΩ→GND', loc='left')

    # SD socket
    j2 = d.add(elm.Ic(pins=[elm.IcPin(name='VDD', side='top'),
                            elm.IcPin(name='CS_DAT3', side='left'),
                            elm.IcPin(name='CMD_MOSI', side='left'),
                            elm.IcPin(name='CLK', side='left'),
                            elm.IcPin(name='DAT0_MISO', side='left'),
                            elm.IcPin(name='DAT1', side='bottom'),
                            elm.IcPin(name='DAT2', side='bottom'),
                            elm.IcPin(name='CD', side='left'),
                            elm.IcPin(name='GND', side='bottom')],
                      edgepadW=1.4, edgepadH=.9,
                      label='J2  microSD (INTERNAL,\npush-PULL socket, 3V3 —\nno 5 V, no level shifter:\nthe 5 V myth was the\nbreakout\'s own LDO)')
             .at((13, 4)))
    d += elm.Line().at(j2.VDD).up().length(.7)
    sv = d.add(elm.Dot())
    d += elm.Label().label('+3V3', loc='top')
    d += elm.Capacitor().at(sv.start).right().length(1.6).label('C40 10 µF + 100 nF\nAT the socket', loc='right')
    d += elm.Ground()
    d += elm.Ground().at(j2.GND)
    for (mcupin, sdpin, r) in [('IO10_CS', 'CS_DAT3', 'R40'), ('IO11_MOSI', 'CMD_MOSI', 'R41'),
                               ('IO12_SCK', 'CLK', 'R42'), ('IO13_MISO', 'DAT0_MISO', 'R43')]:
        a = u1.__getattr__(mcupin); b = j2.__getattr__(sdpin)
        d += elm.Resistor().at(a).to(b).label(f'{r} 33 Ω', loc='top', fontsize=9)
    d += elm.Line().at(u1.IO9_CD).to(j2.CD).label('card detect', loc='bottom', fontsize=9)
    d += elm.Label().at((13, .4)).label('10 kΩ pull-ups→3V3 on CS, CMD, DAT0, DAT1, DAT2\n(floating DAT1/2 can flip the card out of SPI mode)', loc='right', halign='left')

# ------------------------------------------------------------------
# SHEET 4 — CONTROLS: buttons, volume pot, LED
# ------------------------------------------------------------------
with schemdraw.Drawing(file=OUT + r'\sheet4-controls.svg', show=False) as d:
    d += elm.Label().label('AREG TOY — SHEET 4/5 — CONTROLS', loc='top').at((0, 9))
    y = 7
    for name, gpio in [('MAIN', 'GPIO18'), ('YES (green)', 'GPIO21'), ('NO (red)', 'GPIO47')]:
        d += elm.Label().at((0, y)).label('+3V3', loc='left')
        d += elm.Resistor().at((0, y)).right().length(1.8).label('10 kΩ', loc='top', fontsize=9)
        n = d.add(elm.Dot())
        d += elm.Resistor().at(n.start).right().length(1.8).label('1 kΩ', loc='top', fontsize=9)
        d += elm.Line().right().length(.6)
        d += elm.Label().label(gpio, loc='right')
        d += elm.Capacitor().at(n.start).down().length(1.2).label('100 nF', loc='left', fontsize=9)
        d += elm.Ground()
        sw = d.add(elm.Button().at((n.start[0] + 1.0, n.start[1] - 0)).down().length(1.2)
                   .label(f'SW {name}', loc='right', fontsize=9))
        d += elm.Ground()
        y -= 3

    # Volume pot
    d += elm.Label().at((9, 7)).label('+3V3', loc='top')
    pot = d.add(elm.Potentiometer().at((9, 7)).down().length(2.4)
                .label('RV1 10 kΩ lin DETENTED\nBourns PTV09A-4020F-B103\n(knob angle = the display)', loc='bottom'))
    d += elm.Ground().at(pot.end)
    d += elm.Resistor().at(pot.tap).right().length(1.8).label('1 kΩ', loc='top', fontsize=9)
    w = d.add(elm.Dot())
    d += elm.Line().right().length(.6)
    d += elm.Label().label('GPIO8 (ADC1 —\nADC2 dead with Wi-Fi)', loc='right')
    d += elm.Capacitor().at(w.start).down().length(1.2).label('100 nF', loc='left', fontsize=9)
    d += elm.Ground()

    # LED
    d += elm.Label().at((9, 1.2)).label('LED: WS2812B on 5 V needs SN74LVC1T45 level shifter (VIH=3.5 V > ESP VOH);\non a 3V3-only build use 3 discrete LEDs at 2–3 mA (size R from the VF curve).', loc='right', halign='left')

# ------------------------------------------------------------------
# SHEET 5 — USB-C input & protection (charging builds)
# ------------------------------------------------------------------
with schemdraw.Drawing(file=OUT + r'\sheet5-usb.svg', show=False) as d:
    d += elm.Label().label('AREG TOY — SHEET 5/5 — USB-C INPUT AND PROTECTION', loc='top').at((0, 8))
    j1 = d.add(elm.Ic(pins=[elm.IcPin(name='VBUS', side='right'),
                            elm.IcPin(name='CC1', side='right'),
                            elm.IcPin(name='CC2', side='right'),
                            elm.IcPin(name='D+', side='right'),
                            elm.IcPin(name='D-', side='right'),
                            elm.IcPin(name='GND', side='bottom')],
                      edgepadW=1.2, edgepadH=.8,
                      label='J1\nUSB-C\nreceptacle').at((0, 3)))
    d += elm.Ground().at(j1.GND)
    d += elm.Resistor().at(j1.CC1).right().length(2).label('R60 5.1 kΩ', loc='top', fontsize=9)
    d += elm.Ground()
    d += elm.Resistor().at(j1.CC2).right().length(2).label('R61 5.1 kΩ  (SEPARATE —\nnever share one resistor)', loc='bottom', fontsize=9)
    d += elm.Ground()
    d += elm.Line().at(j1.VBUS).right().length(1.2)
    f1 = d.add(elm.Fuse().right().label('F1 PTC 0.5 A\n1206L050', loc='top', fontsize=9))
    d += elm.Line().right().length(.8)
    tvs = d.add(elm.Dot())
    d += elm.Zener().at(tvs.start).down().length(1.8).label('D60\nSMAJ5.0A +\nTPD4S014 array', loc='right', fontsize=9)
    d += elm.Ground()
    d += elm.Line().at(tvs.start).right().length(1.2)
    sw = d.add(elm.Ic(pins=[elm.IcPin(name='IN', side='left'),
                            elm.IcPin(name='OUT', side='right'),
                            elm.IcPin(name='CT', side='bottom'),
                            elm.IcPin(name='GND', side='bottom')],
                     edgepadW=1.0, edgepadH=.6,
                     label='U6 TPS22918\nsoft-start switch\n(350 µF bulk would pull\n1.75 A inrush without it)'))
    d += elm.Ground().at(sw.GND)
    d += elm.Capacitor().at(sw.CT).down().length(1.2).label('C60 sets 5 ms rise', loc='bottom', fontsize=9)
    d += elm.Ground()
    d += elm.Line().at(sw.OUT).right().length(1.2)
    d += elm.Label().label('→ charger BQ24074 (Li-ion builds)\n→ or buck-boost VIN (sheet 1)', loc='right')
    d += elm.Label().at((0, -2.5)).label('D+/D− → USBLC6-2SC6 ESD array → GPIO19/20 (or factory pogo pads only).', loc='right', halign='left')

print('5 sheets drawn')

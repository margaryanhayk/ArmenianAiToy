# Areg toy — ONE full schematic sheet, all sections, readable labels.
import schemdraw
import schemdraw.elements as elm

OUT = r'C:\Users\HAYK~1.MAR\AppData\Local\Temp\claude\C--Users-hayk-margaryan-Documents-Projects-ArmenianAiToy\f2e961ff-3dfc-4bdc-8e81-98b085ccee3b\scratchpad'
schemdraw.config(fontsize=13)

d = schemdraw.Drawing(file=OUT + r'\areg-full-schematic.svg', show=False)

d += elm.Label().at((14, 25)).label('AREG TOY — FULL SCHEMATIC (rev A)   one 3.3 V rail', fontsize=20)

# ============ SECTION A: POWER (top-left) ============
d += elm.Label().at((-4, 22.5)).label('A. POWER', fontsize=16, halign='left')
bat = d.add(elm.BatteryCell().up().at((-4, 19)).label('BT1\n3×AA holder\n(4.5→3.0 V)', loc='left', fontsize=12))
d += elm.Line().up().length(0.8)
d += elm.Line().right().length(1.6)
q = d.add(elm.PFet().anchor('source').theta(0).label('Q1 DMG2301L\nreverse block', loc='top', fontsize=12))
d += elm.Line().at(q.drain).right().length(1.4)
din = d.add(elm.Dot())
d += elm.Capacitor().down().length(1.8).label('C10 10µF', loc='bottom', fontsize=12)
d += elm.Ground()
d += elm.Line().at(din.start).right().length(1.2)
u4 = d.add(elm.Ic(pins=[elm.IcPin(name='VIN', side='left'),
                        elm.IcPin(name='GND', side='bottom'),
                        elm.IcPin(name='L1', side='top'),
                        elm.IcPin(name='L2', side='top'),
                        elm.IcPin(name='VOUT', side='right')],
                  edgepadW=1.1, edgepadH=.7,
                  label='U4 TPS63802\nbuck-boost 3.3V 2A'))
d += elm.Inductor2().at(u4.L1).to(u4.L2).label('L1 1.5µH', loc='top', fontsize=12)
d += elm.Ground().at(u4.GND)
d += elm.Line().at(u4.VOUT).right().length(1.2)
od = d.add(elm.Dot())
d += elm.Capacitor().down().length(1.8).label('C11 100µF', loc='bottom', fontsize=12)
d += elm.Ground()
d += elm.Line().at(od.start).right().length(1.4)
d += elm.Label().label('+3V3', loc='right', fontsize=14)

# ============ SECTION B: USB-C (bottom-left) ============
d += elm.Label().at((-4, 13)).label('B. USB-C (charging option)', fontsize=16, halign='left')
j1 = d.add(elm.Ic(pins=[elm.IcPin(name='VBUS', side='right'),
                        elm.IcPin(name='CC1', side='right'),
                        elm.IcPin(name='CC2', side='right'),
                        elm.IcPin(name='GND', side='bottom')],
                  edgepadW=1.0, edgepadH=.6,
                  label='J1 USB-C').at((-4, 9)))
d += elm.Ground().at(j1.GND)
d += elm.Resistor().at(j1.CC1).right().length(1.8).label('R60 5.1kΩ', loc='top', fontsize=12)
d += elm.Ground()
d += elm.Resistor().at(j1.CC2).right().length(1.8).label('R61 5.1kΩ', loc='bottom', fontsize=12)
d += elm.Ground()
d += elm.Line().at(j1.VBUS).right().length(1.0)
f1 = d.add(elm.Fuse().right().label('F1 PTC 0.5A', loc='top', fontsize=12))
d += elm.Line().right().length(0.6)
tv = d.add(elm.Dot())
d += elm.Zener().at(tv.start).down().length(1.6).label('D60 SMAJ5.0A\n+U5 TPD4S014', loc='right', fontsize=11)
d += elm.Ground()
d += elm.Line().at(tv.start).right().length(1.0)
u6 = d.add(elm.Ic(pins=[elm.IcPin(name='IN', side='left'),
                        elm.IcPin(name='OUT', side='right'),
                        elm.IcPin(name='GND', side='bottom')],
                  edgepadW=.9, edgepadH=.5,
                  label='U6 TPS22918\nsoft-start'))
d += elm.Ground().at(u6.GND)
d += elm.Line().at(u6.OUT).right().length(1.0)
d += elm.Label().label('to U7 BQ24074 charger\n(Li-ion build) or U4 VIN', loc='right', fontsize=11)

# ============ SECTION C: MCU + SD (center) ============
d += elm.Label().at((8, 22.5)).label('C. ESP32-S3 + microSD', fontsize=16, halign='left')
u1 = d.add(elm.Ic(pins=[elm.IcPin(name='3V3', side='top'),
                        elm.IcPin(name='EN', side='left'),
                        elm.IcPin(name='IO0', side='left'),
                        elm.IcPin(name='IO3', side='left'),
                        elm.IcPin(name='IO45', side='left'),
                        elm.IcPin(name='IO46', side='left'),
                        elm.IcPin(name='IO4', side='right'),
                        elm.IcPin(name='IO5', side='right'),
                        elm.IcPin(name='IO6', side='right'),
                        elm.IcPin(name='IO15', side='right'),
                        elm.IcPin(name='IO16', side='right'),
                        elm.IcPin(name='IO7', side='right'),
                        elm.IcPin(name='IO17', side='right'),
                        elm.IcPin(name='IO10', side='bottom'),
                        elm.IcPin(name='IO11', side='bottom'),
                        elm.IcPin(name='IO12', side='bottom'),
                        elm.IcPin(name='IO13', side='bottom'),
                        elm.IcPin(name='IO9', side='bottom'),
                        elm.IcPin(name='IO18', side='left'),
                        elm.IcPin(name='IO21', side='left'),
                        elm.IcPin(name='IO47', side='left'),
                        elm.IcPin(name='IO8', side='left'),
                        elm.IcPin(name='IO48', side='left'),
                        elm.IcPin(name='GND', side='bottom')],
                  edgepadW=2.0, edgepadH=1.4, pinspacing=1.05,
                  label='U1\nESP32-S3-WROOM-1-N16R8\n(PCB antenna — keep-out:\nno copper / magnet / battery)')
         .at((10, 15)))
d += elm.Line().at(u1.__getattr__('3V3')).up().length(.6)
mv = d.add(elm.Dot())
d += elm.Label().label('+3V3', loc='top', fontsize=13)
d += elm.Capacitor().at(mv.start).right().length(1.4).label('C1 22µF + C2 100nF\n≤3mm from pin', loc='right', fontsize=11)
d += elm.Ground()
d += elm.Ground().at(u1.GND)
d += elm.Resistor().at(u1.EN).left().length(1.6).label('R1 10kΩ→3V3', loc='top', fontsize=11)
d += elm.Capacitor().at(u1.EN).down().length(1.3).label('C3 1µF', loc='bottom', fontsize=11)
d += elm.Ground()
d += elm.Resistor().at(u1.IO0).left().length(1.6).label('R2 10kΩ→3V3 (test pad)', loc='top', fontsize=11)
d += elm.Resistor().at(u1.IO3).left().length(1.6).label('R3 10kΩ→GND', loc='top', fontsize=11)
d += elm.Resistor().at(u1.IO45).left().length(1.6).label('R4 10kΩ→GND', loc='top', fontsize=11)
d += elm.Resistor().at(u1.IO46).left().length(1.6).label('R5 10kΩ→GND', loc='top', fontsize=11)

# SD socket below MCU
j2 = d.add(elm.Ic(pins=[elm.IcPin(name='VDD', side='right'),
                        elm.IcPin(name='CS', side='top'),
                        elm.IcPin(name='CMD', side='top'),
                        elm.IcPin(name='CLK', side='top'),
                        elm.IcPin(name='DAT0', side='top'),
                        elm.IcPin(name='CD', side='top'),
                        elm.IcPin(name='GND', side='bottom')],
                  edgepadW=1.6, edgepadH=.7, pinspacing=1.0,
                  label='J2 microSD (internal,\npush-pull socket, 3V3)\nMolex 5031821852')
         .at((9.2, 5.2)))
d += elm.Ground().at(j2.GND)
d += elm.Line().at(j2.VDD).right().length(.8)
sv = d.add(elm.Dot())
d += elm.Label().label('+3V3', loc='right', fontsize=12)
d += elm.Capacitor().at(sv.start).down().length(1.4).label('C40 10µF+100nF', loc='bottom', fontsize=11)
d += elm.Ground()
for mp, sp, rn in [('IO10','CS','R40'),('IO11','CMD','R41'),('IO12','CLK','R42'),('IO13','DAT0','R43')]:
    d += elm.Resistor().at(u1.__getattr__(mp)).to(j2.__getattr__(sp)).label(f'{rn} 33Ω', loc='top', fontsize=10)
d += elm.Line().at(u1.IO9).to(j2.CD)
d += elm.Label().at((13.5, 2.6)).label('R44–R48: 10kΩ→3V3 pull-ups on\nCS, CMD, DAT0, DAT1, DAT2', fontsize=11, halign='left')

# ============ SECTION D: AUDIO (right) ============
d += elm.Label().at((22, 22.5)).label('D. AUDIO', fontsize=16, halign='left')
u3 = d.add(elm.Ic(pins=[elm.IcPin(name='VDD', side='top'),
                        elm.IcPin(name='SCK', side='left'),
                        elm.IcPin(name='WS', side='left'),
                        elm.IcPin(name='SDO', side='left'),
                        elm.IcPin(name='LR', side='bottom'),
                        elm.IcPin(name='GND', side='bottom')],
                  edgepadW=1.1, edgepadH=.7,
                  label='U3 INMP441 mic\n(alt: SPH0645LM4H)')
         .at((24, 18.5)))
d += elm.Resistor().at(u3.VDD).up().length(1.3).label('R20 10Ω→3V3', loc='top', fontsize=11)
d += elm.Capacitor().at(u3.VDD).right().length(1.4).label('C20 10µF+100nF', loc='right', fontsize=11)
d += elm.Ground()
d += elm.Ground().at(u3.GND)
d += elm.Line().at(u3.LR).down().length(.4)
d += elm.Ground().label('L/R→GND (required)', loc='right', fontsize=11)
d += elm.Line().at(u3.SCK).left().length(1.4).label('←GPIO4', loc='left', fontsize=12)
d += elm.Line().at(u3.WS).left().length(1.4).label('←GPIO5', loc='left', fontsize=12)
d += elm.Line().at(u3.SDO).left().length(1.4).label('→GPIO6', loc='left', fontsize=12)

u2 = d.add(elm.Ic(pins=[elm.IcPin(name='VDD', side='top'),
                        elm.IcPin(name='BCLK', side='left'),
                        elm.IcPin(name='LRC', side='left'),
                        elm.IcPin(name='DIN', side='left'),
                        elm.IcPin(name='SDM', side='left'),
                        elm.IcPin(name='GAIN', side='bottom'),
                        elm.IcPin(name='OUTP', side='right'),
                        elm.IcPin(name='OUTN', side='right'),
                        elm.IcPin(name='GND', side='bottom')],
                  edgepadW=1.2, edgepadH=.8,
                  label='U2 MAX98357A\nclass-D amp')
         .at((24, 12)))
d += elm.Line().at(u2.VDD).up().length(.6)
av = d.add(elm.Dot())
d += elm.Label().label('+3V3', loc='top', fontsize=13)
d += elm.Capacitor().at(av.start).right().length(1.4).label('C30 100nF + C31 22µF\n+ C32 330µF polymer', loc='right', fontsize=11)
d += elm.Ground()
d += elm.Ground().at(u2.GND)
d += elm.Line().at(u2.BCLK).left().length(1.2).label('←GPIO15', loc='left', fontsize=12)
d += elm.Line().at(u2.LRC).left().length(1.2).label('←GPIO16', loc='left', fontsize=12)
d += elm.Line().at(u2.DIN).left().length(1.2).label('←GPIO7', loc='left', fontsize=12)
d += elm.Line().at(u2.SDM).left().length(1.2).label('←GPIO17 mute', loc='left', fontsize=12)
d += elm.Resistor().at(u2.GAIN).down().length(1.4).label('R30 gain-set =\nloudness ceiling', loc='bottom', fontsize=11)
d += elm.Line().at(u2.OUTP).right().length(.6)
fb1 = d.add(elm.Inductor2(n=2).right().label('FB1', loc='top', fontsize=11))
d += elm.Line().at(u2.OUTN).right().length(.6)
fb2 = d.add(elm.Inductor2(n=2).right().label('FB2', loc='bottom', fontsize=11))
spk = d.add(elm.Speaker().at((fb1.end[0] + 1.0, (fb1.end[1] + fb2.end[1]) / 2)).right()
            .label('LS1 50mm 8Ω\n≥88dB/W/m\nsealed chamber', loc='right', fontsize=12))
d += elm.Line().at(fb1.end).to(spk.in1)
d += elm.Line().at(fb2.end).to(spk.in2)

# ============ SECTION E: CONTROLS (bottom center-left) ============
d += elm.Label().at((-4, 5.5)).label('E. CONTROLS', fontsize=16, halign='left')
y = 3.5
for name, gpio in [('SW1 MAIN', 'GPIO18'), ('SW2 YES/green', 'GPIO21'), ('SW3 NO/red', 'GPIO47')]:
    d += elm.Label().at((-4, y)).label('+3V3', loc='left', fontsize=11)
    d += elm.Resistor().at((-4, y)).right().length(1.5).label('10kΩ', loc='top', fontsize=10)
    n = d.add(elm.Dot())
    d += elm.Resistor().at(n.start).right().length(1.5).label('1kΩ', loc='top', fontsize=10)
    d += elm.Label().label(gpio, loc='right', fontsize=12)
    d += elm.Capacitor().at(n.start).down().length(1.0).label('100nF', loc='left', fontsize=10)
    d += elm.Ground()
    b = d.add(elm.Button().at((n.start[0] + .8, n.start[1])).down().length(1.0).label(name, loc='right', fontsize=11))
    d += elm.Ground()
    y -= 2.6

d += elm.Label().at((3.5, 5.5 - 2)).label('+3V3', loc='top', fontsize=11)
pot = d.add(elm.Potentiometer().at((3.5, 3.5)).down().length(2.0)
            .label('RV1 10kΩ detented\nBourns PTV09A', loc='bottom', fontsize=11))
d += elm.Ground().at(pot.end)
d += elm.Resistor().at(pot.tap).right().length(1.5).label('1kΩ', loc='top', fontsize=10)
w = d.add(elm.Dot())
d += elm.Label().label('GPIO8 (ADC1)', loc='right', fontsize=12)
d += elm.Capacitor().at(w.start).down().length(1.0).label('100nF', loc='left', fontsize=10)
d += elm.Ground()

led = d.add(elm.Ic(pins=[elm.IcPin(name='VDD', side='top'),
                         elm.IcPin(name='DIN', side='left'),
                         elm.IcPin(name='GND', side='bottom')],
                   edgepadW=.9, edgepadH=.5,
                   label='D1 WS2812B LED\n(+U8 74LVC1T45\nshifter on 5V builds)')
          .at((3.5, -2.2)))
d += elm.Line().at(led.DIN).left().length(1.2).label('←GPIO48', loc='left', fontsize=12)
d += elm.Ground().at(led.GND)
d += elm.Label().at(led.VDD).label('+3V3/5V', loc='top', fontsize=11)

d.save(OUT + r'\areg-full-schematic.svg')
print('full sheet drawn')

# Generate Falstad CircuitJS shareable links for the four Areg sub-circuit sims.
# Recipe verified in docs/hardware/sim-research.md:
#   url = https://www.falstad.com/circuit/circuitjs.html?ctz=
#         + LZString.compressToEncodedURIComponent(circuitText)
from lzstring import LZString

lz = LZString()
BASE = 'https://www.falstad.com/circuit/circuitjs.html?ctz='

circuits = {}

# ------------------------------------------------------------------
# 1. BOOST REGULATOR CONCEPT — 3.0 V tired battery -> ~3.6 V rail.
# Open-loop boost: battery -> L -> switching NMOS (50 kHz gate) -> diode -> C -> load.
# Demonstrates the "battery below 3.3 V still powers the 3.3 V rail" claim.
# Timestep 5e-8 for 50 kHz switching.
circuits['power-boost'] = r'''$ 1 5e-8 10.20027730826997 50 5 43 5e-11
v 112 304 112 128 0 0 40 3 0 0 0.5
w 112 128 208 128 0
l 208 128 320 128 0 0.000022 0
f 336 224 336 128 8 1.5 0.02
R 272 224 240 224 0 2 50000 2.5 2.5 0 0.25
w 272 224 336 224 0
w 352 128 352 176 0
g 352 176 352 192 0
w 112 304 112 352 0
g 112 352 112 368 0
w 320 128 320 80 0
d 320 80 464 80 2 default
c 464 80 464 224 0 0.0001 3.5
w 464 224 464 256 0
g 464 256 464 272 0
r 560 80 560 224 0 20
w 464 80 560 80 0
w 560 224 560 256 0
g 560 256 560 272 0
p 624 80 624 224 0 0 0
w 560 80 624 80 0
w 624 224 624 256 0
g 624 256 624 272 0
x 96 96 260 99 4 14 3.0\sV\stired\sbattery
x 400 56 520 59 4 14 diode\s->\s3V3\srail
x 500 290 640 293 4 14 load\s(toy\selectronics)
'''

# ------------------------------------------------------------------
# 2. CONTROLS — button with 10k pull-up + 100nF, and 10k volume pot -> 1k -> 100nF (ADC).
circuits['controls'] = r'''$ 1 0.000005 10.20027730826997 50 5 43 5e-11
R 112 96 112 64 0 0 40 3.3 0 0 0.5
r 112 96 112 192 0 10000
s 112 192 112 288 0 1 false
g 112 288 112 320 0
c 208 192 208 288 0 1e-7 0
w 112 192 208 192 0
g 208 288 208 320 0
p 288 192 288 288 0 0 0
w 208 192 288 192 0
g 288 288 288 320 0
x 60 48 260 51 4 14 BUTTON\s->\sGPIO18\s(press\ss\sto\stest)
R 448 96 448 64 0 0 40 3.3 0 0 0.5
174 448 96 448 224 0 10000 0.5 Volume
w 464 160 512 160 0
r 512 160 608 160 0 1000
c 608 160 608 256 0 1e-7 0
g 608 256 608 288 0
p 688 160 688 256 0 0 0
w 608 160 688 160 0
g 688 256 688 288 0
w 448 224 448 256 0
g 448 256 448 288 0
x 420 48 660 51 4 14 VOLUME\sKNOB\s->\sADC\s(GPIO8)
'''

# ------------------------------------------------------------------
# 3. SPEAKER DRIVE — amp output (1 kHz, 3.2 Vpp square-ish -> use AC) through
# ferrite beads into the 8-ohm speaker (voice coil = 8R + 40uH).
circuits['speaker'] = r'''$ 1 0.000005 10.20027730826997 50 5 43 5e-11
v 112 288 112 128 0 1 1000 1.6 0 0 0.5
l 176 128 272 128 0 1e-6 0
w 112 128 176 128 0
r 336 128 336 224 0 8
l 336 224 336 288 0 0.00004 0
l 176 288 272 288 0 1e-6 0
w 112 288 176 288 0
w 272 288 336 288 0
w 272 128 336 128 0
c 400 128 400 288 0 1e-7 0
w 336 128 400 128 0
w 336 288 400 288 0
g 112 288 112 336 0
p 464 128 464 288 0 0 0
w 400 128 464 128 0
w 400 288 464 288 0
x 60 96 300 99 4 14 MAX98357A\soutput\s(1\skHz\stone)
x 300 336 470 339 4 14 8-ohm\sspeaker\s(coil\smodel)
x 150 160 240 163 4 12 FB1\sferrite
x 150 320 240 323 4 12 FB2\sferrite
'''

# ------------------------------------------------------------------
# 4. USB SURGE PROTECTION — 12 V surge pulse through PTC, TVS clamps the rail.
# Load sees a clamped voltage instead of the surge. Zener legacy form = 5.6 V TVS stand-in.
circuits['usb-protection'] = r'''$ 1 0.000005 10.20027730826997 50 12 43 5e-11
v 112 304 112 128 0 2 200 3.5 8.5 0 0.5
r 112 128 240 128 0 0.5
z 240 304 240 128 1 0.805904783 5.6
w 112 304 240 304 0
r 336 128 336 304 0 100
w 240 128 336 128 0
w 240 304 336 304 0
g 240 304 240 352 0
p 432 128 432 304 0 0 0
w 336 128 432 128 0
w 336 304 432 304 0
x 48 96 340 99 4 14 USB\sinput:\s5\sV\snormally,\s12\sV\ssurge\spulses
x 148 160 220 163 4 12 F1\sPTC
x 250 200 330 203 4 12 D60\sTVS\sclamp
x 340 340 480 343 4 14 protected\selectronics
'''

import json, os
OUT = os.path.dirname(os.path.abspath(__file__))
links = {}
for name, text in circuits.items():
    text = text.replace('\r\n', '\n')
    ctz = lz.compressToEncodedURIComponent(text)
    links[name] = BASE + ctz
    # roundtrip check
    back = lz.decompressFromEncodedURIComponent(ctz)
    assert back == text, f'roundtrip failed for {name}'
    with open(os.path.join(OUT, f'falstad-{name}.txt'), 'w', encoding='utf-8') as f:
        f.write(text)
    print(name, len(links[name]), 'chars')
with open(os.path.join(OUT, 'falstad-links.json'), 'w', encoding='utf-8') as f:
    json.dump(links, f, indent=1)
print('all roundtrips ok')

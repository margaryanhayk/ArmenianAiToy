# Areg toy — numerical simulations of the two load-bearing analog decisions.
# 1) Battery discharge vs regulator topology: WHY buck-boost, not LDO/buck.
# 2) Loudness budget: amp power at 3.3 V into 8 ohm vs speaker sensitivity
#    vs the EN 71-1 80 dB @ 50 cm toy limit.
# Outputs two PNGs for visual verification before shipping.
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

OUT = r'C:\Users\HAYK~1.MAR\AppData\Local\Temp\claude\C--Users-hayk-margaryan-Documents-Projects-ArmenianAiToy\f2e961ff-3dfc-4bdc-8e81-98b085ccee3b\scratchpad'

# ------------------------------------------------------------------
# 1) POWER: 3xAA alkaline discharge curve (per-cell, typical 250 mA load)
#    Piecewise-linear approximation of an alkaline AA under moderate load:
#    1.55 V fresh -> 1.2 V at 50% -> 1.0 V at 90% -> 0.8 V empty.
soc = np.linspace(0, 1, 500)  # state of discharge 0=fresh 1=empty
cell = np.interp(soc, [0.0, 0.15, 0.5, 0.8, 0.95, 1.0],
                       [1.55, 1.40, 1.23, 1.10, 0.95, 0.80])
pack = 3 * cell
# P-FET reverse-block drop: DMG2301L Rds(on) ~40 mOhm at 4.5V Vgs, 250 mA -> ~10 mV. Negligible but honest:
pack_after_fet = pack - 0.25 * 0.04

# Regulator behaviors (output voltage the toy actually sees):
# LDO (e.g. AMS1117-3.3: dropout ~1.1 V typ at load!)
ldo_out = np.where(pack_after_fet >= 3.3 + 1.1, 3.3, np.maximum(pack_after_fet - 1.1, 0))
# "Low-dropout" 300 mV LDO (best case realistic):
ldo2_out = np.where(pack_after_fet >= 3.3 + 0.3, 3.3, np.maximum(pack_after_fet - 0.3, 0))
# Pure buck (needs Vin > Vout + margin ~0.2):
buck_out = np.where(pack_after_fet >= 3.5, 3.3, 0)  # collapses when it can't regulate
# Buck-boost TPS63802 (Vin 1.3-5.5 V, seamless):
bb_out = np.where(pack_after_fet >= 1.3, 3.3, 0)

# ESP32-S3 brownout threshold ~2.8 V (WiFi TX needs solid 3.0+):
brownout = 3.0

fig, ax = plt.subplots(figsize=(9.5, 5.6), dpi=130)
ax.plot(soc*100, pack_after_fet, color='#888', lw=2, label='3×AA pack voltage (after P-FET)')
ax.plot(soc*100, bb_out, color='#1a7f37', lw=3, label='TPS63802 buck-boost output (CHOSEN)')
ax.plot(soc*100, ldo2_out, color='#d97706', lw=2, ls='--', label='best-case 0.3 V-dropout LDO')
ax.plot(soc*100, ldo_out, color='#b91c1c', lw=2, ls='--', label='AMS1117 LDO (hobby module)')
ax.axhline(brownout, color='#b91c1c', lw=1, ls=':', alpha=0.8)
ax.text(1, brownout+0.04, 'ESP32-S3 needs ≥3.0 V for stable Wi-Fi TX', fontsize=9, color='#b91c1c')

# usable-life shading for buck-boost (until pack < 1.3V ~ never with alkaline) vs LDO
# find where each output first drops below 3.0
def life_pct(out):
    bad = np.where(out < brownout)[0]
    return 100.0 if len(bad) == 0 else soc[bad[0]]*100
ax.annotate(f'buck-boost: usable to {life_pct(bb_out):.0f}% of pack energy',
            xy=(70, 3.3), xytext=(38, 3.62), fontsize=10, color='#1a7f37',
            arrowprops=dict(arrowstyle='->', color='#1a7f37'))
lp = life_pct(ldo_out)
ax.annotate(f'AMS1117 dies at {lp:.0f}% — throws away {100-lp:.0f}% of the batteries',
            xy=(lp, brownout), xytext=(8, 2.35), fontsize=10, color='#b91c1c',
            arrowprops=dict(arrowstyle='->', color='#b91c1c'))
ax.set_xlabel('battery energy used, %')
ax.set_ylabel('voltage, V')
ax.set_title('Why the toy needs a buck-boost: 3×AA crosses 3.3 V while discharging')
ax.set_xlim(0, 100); ax.set_ylim(1.8, 4.8)
ax.grid(alpha=0.25)
ax.legend(loc='upper right', fontsize=9)
fig.tight_layout()
fig.savefig(f'{OUT}\\sim-power.png')
print('power life: buck-boost', life_pct(bb_out), '% | 0.3V LDO', life_pct(ldo2_out), '% | AMS1117', life_pct(ldo_out), '%')

# ------------------------------------------------------------------
# 2) LOUDNESS: MAX98357A at 3.3 V into 8 ohm.
#    Max undistorted: Vout_pk ≈ 3.3 V rail (bridge-tied load doubles swing but
#    the part is a class-D BTL: P_max(3.3V, 8ohm) ≈ 0.9 W per datasheet table.
P_max = 0.9  # W, datasheet 3.3V/8ohm ~10% THD; ~0.6-0.7W at 1% THD
P_1pct = 0.65
sens = np.array([84, 86, 88, 90])  # dB/W/m speaker candidates
dist = 0.5  # EN 71-1 measurement distance 50 cm
en71_limit = 80.0

fig2, ax2 = plt.subplots(figsize=(9.5, 5.6), dpi=130)
powers = np.linspace(0.01, 1.0, 400)
for s, c in zip(sens, ['#b91c1c', '#d97706', '#1a7f37', '#2563eb']):
    spl_50cm = s + 10*np.log10(powers) - 20*np.log10(dist/1.0)
    ax2.plot(powers, spl_50cm, lw=2.4, color=c, label=f'{s} dB/W/m speaker')
ax2.axhline(en71_limit, color='black', lw=1.6, ls=':')
ax2.text(0.012, en71_limit+0.4, 'EN 71-1 toy limit: 80 dB @ 50 cm  (R30 gain resistor caps BELOW this)', fontsize=9)
ax2.axvline(P_max, color='#666', lw=1.2, ls='--'); ax2.text(P_max*1.02, 63.0, 'MAX98357A max\n0.9 W @ 3.3 V/8 Ω', fontsize=8.5, ha='left')
ax2.axvline(P_1pct, color='#aaa', lw=1.2, ls='--'); ax2.text(P_1pct*0.97, 66.8, '0.65 W\n(clean, 1% THD)', fontsize=8.5, ha='right')
ax2.set_xscale('log')
ax2.set_xlabel('amplifier power into speaker, W (log scale)')
ax2.set_ylabel('loudness at 50 cm, dB SPL')
ax2.set_title('Loudness budget: 3.3 V amp + speaker sensitivity vs the EN 71-1 toy limit')
ax2.set_xlim(0.01, 1.0); ax2.set_ylim(62, 92)
ax2.grid(alpha=0.25, which='both')
ax2.legend(loc='upper left', fontsize=9)
fig2.tight_layout()
fig2.savefig(f'{OUT}\\sim-audio.png')

for s in sens:
    spl = s + 10*np.log10(P_1pct) + 6.02  # at 50 cm: -20log10(0.5)=+6.02
    print(f'{s} dB/W/m: clean max at 50cm = {spl:.1f} dB SPL')

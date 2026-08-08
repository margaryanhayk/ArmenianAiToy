# Microcontroller audit — ESP32-S3-WROOM-1/1U (overnight slice, owner request)

Scope: the MCU decision and pin allocation only. Companion docs:
`schematic-spec.md` (full pin map + net-by-net), `power-tree.md` (rails),
`bom.md` (parts/prices), `open-questions.md` (what a lab still has to
settle). Primary source for every ESP32-S3-WROOM-1/1U number below:
`esp32-s3-wroom-1_wroom-1u_datasheet_en.pdf` (v1.4), re-extracted and
grepped directly for this audit — table/section cited inline. Numbers
for competing chips (C6/P4/i.MX RT/RP2350) are **not** from that
datasheet — they're general vendor-spec knowledge, flagged as such, and
should be re-checked against the vendors' current datasheets before
they inform a purchasing decision.

**Bottom line up front:** ESP32-S3 stays the right chip. N8R8 has
~5.3 MB (67%) of its 8 MB sitting idle today — N16R8 is not yet earned
by usage. The pin map in `schematic-spec.md` is correct on every
constraint it claims to honor; two things it does not mention are worth
fixing before layout (GPIO15/16 quietly forecloses ever adding an RTC
crystal; GPIO39-42 and the UART0 pins TXD0/RXD0 are simply absent from
the table, not marked spare or NC). No pin in the map is illegal.

---

## 1. Is ESP32-S3 still the right chip?

Judged on: simultaneous I2S in+out, TLS+PSRAM headroom, Arduino
ecosystem maturity (this team has a working, field-OTA'd Arduino
firmware today), price, supply.

| Chip | Verdict | One-line why |
|---|---|---|
| **ESP32-S3** (current) | **Keep** | Dual I2S+DMA blocks (mic + amp run independently, no bit-banging), Octal PSRAM headroom the firmware is already using for TLS+BLE+audio buffers, native USB, mature Arduino/ESP-IDF core this project has already shipped OTA on. |
| ESP32-C6 | Reject | **No PSRAM interface at all** (Table 1/2 comparison for C6-family modules has no Rn suffix option) — this project's own field notes already show RAM is the tight resource even *with* Octal PSRAM (157-227 KB free after adding voice/games clip tables); dropping PSRAM entirely is a firmware-architecture regression, not a swap. Also only 1 I2S block vs the S3's 2. |
| ESP32-P4(+C6) | Reject | P4 has camera/display/AI horsepower this toy doesn't use, but **no integrated Wi-Fi/BT radio** — needs a second chip (C6/C5) as an SDIO/SPI companion, doubling the BOM, the firmware surface, and the certification scope for zero product benefit (the toy proxies AI to the cloud; it doesn't need on-device vision or a display). |
| NXP i.MX RT (RT1060/RT600-class) | Reject | Real Cortex-M7 DSP/audio parts, but **no integrated Wi-Fi/BT** — needs an external combo module *and* a full MCUXpresso/SDK firmware rewrite, discarding the entire Arduino OTA/BLE-provisioning/content-sync codebase this project already has bench- and field-proven. Not compute-bound work that would justify the M7. |
| RP2350(+CYW43439, "Pico 2 W"-class) | Reject | Wi-Fi is a bolted-on SPI companion chip (no native radio), **no hardware I2S+DMA block** (PIO can bit-bang one stream, doing simultaneous full-duplex mic+speaker on PIO is a real firmware risk this team hasn't built), and no PSRAM expansion path — same RAM-headroom problem as the C6, worse ecosystem maturity for audio+TLS+OTA on Arduino specifically. |

Nothing here reopens the MCU decision. The two live decisions are SKU
(§2) and antenna variant, and WROOM-1 (PCB antenna, not -1U) is
already the right call for a handheld toy with no room for an external
antenna connector, matching the manufacturing review's earlier
conclusion (see agent memory `project-areg-toy-manufacturing-review`).

---

## 2. N16R8 vs N8R8 — what does the extra 8 MB actually buy?

Both are legitimate Octal-PSRAM SKUs (Datasheet Table 1: `N8R8` = 8 MB
Quad-SPI flash / 8 MB Octal-SPI PSRAM; `N16R8` = 16 MB Quad-SPI flash /
8 MB Octal-SPI PSRAM — **PSRAM is identical between them**, only flash
changes). `bom.md` currently prices in **N16R8** ($4.30@100 /
$3.60@1000); the flash size is not a datasheet fact, so treat that
delta as a BOM-doc/market estimate, not a spec.

**Current partition table** (`esp32/AregVoiceMvp/partitions.csv`, 8 MB
target, comments verified against the file):

```
nvs        0x9000    0x5000    (  20 KB)
otadata    0xe000    0x2000    (   8 KB)
app0       0x10000   0x300000  (3.00 MB)  <- OTA slot A
app1       0x310000  0x300000  (3.00 MB)  <- OTA slot B
spiffs     0x610000  0x1E0000  (1.875 MB)
coredump   0x7F0000  0x10000   (  64 KB)
                                --------
                                8.00 MB total, byte-exact
```

Production firmware is **1,264,539 B** (per CLAUDE.md's OTA section) =
**42%** of one 3 MB app slot, leaving **1.735 MB free per slot** (2×
= 3.47 MB across both slots). The `spiffs` partition is **1.875 MB and
currently unmounted** — a repo-wide grep for `SPIFFS`/`LittleFS` across
every `.cpp`/`.h`/`.ino` in `esp32/AregVoiceMvp/` returns zero hits.
Story/game/music/voice content lives on the SD card via the
`content_sync.cpp` pipeline (4 ContentSync namespaces, 143 files per
CLAUDE.md), not on flash, so this partition is dead space by design,
not by oversight.

**Arithmetic**: of the 8 MB N8R8 offers today, **~5.35 MB (67%) is
currently idle** (3.47 MB of OTA-slot headroom + the entire 1.875 MB
unused spiffs region). N16R8 would double an already two-thirds-empty
resource.

**Recommendation: stay on N8R8** unless one of these becomes real:
- A **flash-only, no-SD lower-tier SKU** is wanted (removes the SD
  BOM line — J2 $0.55 + card $1.60 + pull-ups + ESD ≈ $2.75/unit per
  `bom.md` — in exchange for a one-time module cost delta). That's
  the one scenario where 16 MB is actually needed: today's SD library
  is 143 files across stories/games/music/voice; a meaningful subset
  of that on flash instead of SD would consume real space, unlike the
  currently-idle spiffs partition.
- Firmware footprint roughly doubles from its current 42%-of-3MB
  trajectory — no evidence of that yet across the whole owner batch
  history in CLAUDE.md (OTA, BLE provisioning, 4 content namespaces,
  offline games, story pauses, welcome flow all landed inside the
  existing slot with headroom to spare).
- A 3rd OTA slot or a bigger firmware safety margin is explicitly
  wanted for its own sake — a business call, not an engineering
  requirement visible in the current numbers.

If `bom.md`'s current N16R8 line is a placeholder rather than a
decision, this audit's recommendation is to correct it to N8R8 and
bank the market-rate delta (~$0.20-0.70/unit @1000, BOM-doc estimate,
not a datasheet fact) until one of the triggers above actually fires.

---

## 3. Pin-map verification (`schematic-spec.md` §1, full pin-by-pin)

Cross-checked against Datasheet Table 3 (Pin Definitions, all 41
physical pins), Table 4/6/7/8 (strapping), and the ADC1/ADC2 channel
list. **Every pin in the production map is legal for N8R8 and N16R8
alike** (both are Octal-PSRAM SKUs with identical PSRAM-reserved pins;
neither is the R16V SKU that shifts GPIO47/48 to 1.8 V). Two real gaps
found — neither is a wiring error, both are worth closing before
layout.

### Strapping pins (IO0/IO3/IO45/IO46) — correct

| Pin | Datasheet role (Table 4/6/7/8) | Schematic-spec treatment | Verdict |
|---|---|---|---|
| GPIO0 | Boot mode select (weak pull-UP default) | Moved off as MAIN button → 10 kΩ pull-up + factory test pad only | Correct — this was the manufacturing review's headline finding and it's been fixed in the current map. |
| GPIO3 | JTAG signal source, **no internal pull at all** | 10 kΩ to GND, "must never float" | Correct and matches the datasheet's own warning verbatim (§4.4: "This pin does not have any internal pull resistors ... cannot be in a high impedance state"). |
| GPIO45 | VDD_SPI voltage select (weak pull-DOWN default) | 10 kΩ to GND | Correct. |
| GPIO46 | Boot mode + ROM print control (weak pull-DOWN default) | 10 kΩ to GND | Correct. |

### ADC1 vs ADC2-during-Wi-Fi (volume pot on IO8)

ADC1 = GPIO1-10 (10 channels, CH0-CH9); ADC2 = GPIO11-20 (10 channels,
CH0-CH9) — confirmed directly off Table 3's per-pin function column
(e.g. `IO8 ... ADC1_CH7`, `IO11 ... ADC2_CH0` ... `IO20 ... ADC2_CH9`).
The volume pot wiper is on **GPIO8 = ADC1_CH7** — inside the immune
range. This is the correct choice: ADC2 is unusable while Wi-Fi is
active (general ESP-IDF/SoC-errata caveat, not a line item in this
module datasheet, but well-established and already in this agent's
own reference memory), and the pot needs to work continuously,
including mid-Wi-Fi-call. Confirmed no other analog input in the
design lands in GPIO11-20.

### Octal PSRAM reserved pins (IO35/36/37) — correctly excluded

Table 3 footnote b, verbatim: *"For modules with Octal SPI PSRAM, i.e.,
modules embedded with ESP32-S3R8 or ESP32-S3R16V, pins IO35, IO36, and
IO37 are connected to the Octal SPI PSRAM and are not available for
other uses."* **This applies identically to N8R8 and N16R8** — both
are R8-class PSRAM. The schematic-spec's "Unavailable: 35/36/37" line
is correct and the map does not attempt to use them.

### IO18 MAIN button — the "USB-JTAG-adjacent" question, resolved

Checked directly against Table 3: `IO18 ... RTC_GPIO18, GPIO18, U1RXD,
ADC2_CH7, CLK_OUT3`. **No JTAG association whatsoever.** The four
actual JTAG signals (Table 3, §4.4) are hardwired to specific pins:
`MTCK=GPIO39, MTDO=GPIO40, MTDI=GPIO41, MTMS=GPIO42` — fourteen pin
positions away from GPIO18. Separately, the on-chip USB-Serial-JTAG
controller shares the USB OTG **internal-PHY** pins (GPIO19/20, i.e.
USB_D-/D+) when using the internal PHY — again, not GPIO18. (There is
a documented **external-PHY** alternate mux that puts USB signals on
GPIO21/38/39-42, but that only applies if an external USB PHY chip is
wired in, which this design does not do — GPIO21 is free by that same
logic, see below.) **Verdict: IO18 is clean.** Its `ADC2_CH7` alt
function is irrelevant here since the button is read digitally, not
through the ADC — the ADC2/Wi-Fi conflict only bites analog reads.

### IO21/IO47 (YES/NO buttons) — correct, IO21 is the cleanest pin on the module

`IO21 ... RTC_GPIO21, GPIO21` — **no other alternate function listed
at all**, confirmed against Table 3 (the only asterisk is the
external-USB-PHY footnote discussed above, which doesn't apply here).
`IO47 ... SPICLK_P_DIFF, GPIO47, SUBSPICLK_P_DIFF` — footnote c only
fires "for modules embedded with ESP32-S3**R16V**" (i.e. the
16 MB-flash/16 MB-PSRAM SKU). **N16R8 is not R16V** — R16V is a
distinct ordering code (`N16R16VA`) for the 16 MB Octal-PSRAM variant,
not the 8 MB Octal-PSRAM N16R8 this project is evaluating. So on both
N8R8 and N16R8, GPIO47 stays a normal 3.3 V-domain GPIO. Correct as
specified.

### IO48 (LED) — correct, same R16V-only caveat

Same footnote-c logic as IO47: 1.8 V only applies to R16V (`N16R16VA`),
not to N8R8/N16R8. Correct.

### I2S flexibility — confirmed unrestricted

Both I2S controllers route through the GPIO Matrix (any pin, no fixed
IO_MUX requirement) and run independently with dedicated DMA per
Section 5.2 — mic (BCK=4/WS=5/SD=6) and amp (BCK=15/LRC=16/DIN=7) can
run full-duplex simultaneously with zero contention. This is also the
direct answer to Q1's "simultaneous I2S in+out" requirement — it's not
a stretch for this chip, it's two independent hardware blocks.

**One thing worth flagging that the map doesn't mention**: `IO15 ...
ADC2_CH4, XTAL_32K_P` and `IO16 ... ADC2_CH5, XTAL_32K_N` — these are
the chip's dedicated 32.768 kHz RTC-crystal pins. Assigning them to
AMP BCK/LRC is *fine today* (no external RTC crystal is planned — the
toy explicitly has no wall clock per CLAUDE.md, bedtime windows are
evaluated server-side and pushed via heartbeat) but it **permanently
forecloses ever adding a real-time clock crystal** without moving the
amp's BCK/LRC lines in a future revision. Worth a one-line note in
`schematic-spec.md` so a future revision doesn't rediscover this by
routing an RTC crystal into the amp's clock lines.

### SD-SPI pins — correct, and one subtlety worth naming

CS=10/SCK=12/MOSI=11/MISO=13 are exactly the native FSPI (SPI2)
IO_MUX group per Table 3 (`FSPICS0/FSPICLK/FSPID/FSPIQ` on those four
pins respectively) — correctly called out as "do not move." Card-detect
on GPIO9 reuses the **fifth** member of that same native group
(`IO9 ... FSPIHD, SUBSPIHD`) as a plain digital input. That's safe
*because* the SD card is run in 3-wire SPI mode (CS/CLK/D/Q only) —
HD/WP are QSPI-only signals this design never activates — but it does
mean GPIO9 is not truly "spare," it's "borrowed from the SPI2 group
and safe only as long as SD stays in 1-bit SPI mode." Worth a one-line
comment in the schematic for whoever revisits this for a future
SDMMC 4-bit migration (CLAUDE.md notes this as a deliberate Rev-B
option).

### GPIO48/47/21 — see above (all correct)

### Two gaps: pins present on the module but absent from the pin-map table

Cross-referencing every one of the 41 physical pins in Table 3 against
`schematic-spec.md` §1 turns up six pins that are neither assigned nor
listed in the "Spare → test pads" row (1, 2, 14, 38):

| Pins | Datasheet identity | Gap |
|---|---|---|
| GPIO39, 40, 41, 42 | JTAG (MTCK/MTDO/MTDI/MTMS) | Not in the map at all — not wired, not spare, not NC. If a hardware debug/JTAG path is ever wanted for bring-up or factory test, these are the *only* four pins that carry it; if it's deliberately not wanted, they should still be listed (even as "NC, no test pad") so a future revision doesn't wire something else onto them by accident. |
| TXD0, RXD0 (module pins 37/36 = GPIO43/44, UART0) | Default ROM/boot UART console (used by `UART Download Boot` and default ROM message printing per §4.3) | Also absent from the table. Production plan already leans on OTA + native USB-CDC (GPIO19/20) for reflash, so UART0 may be intentionally unused — but a cheap 2-pin UART pogo pad is the traditional low-cost factory-test/recovery path and costs nothing to add now versus a respin later. |

Neither gap is a defect in what's already there — everything claimed
in the table checks out. It's an omission: 6 of the module's 41
physical pins have no disposition at all.

### Bonus datasheet find, worth logging for the product's environmental spec

Table 1's header note (not previously in this agent's memory):
*"For R8 and R16V series modules with Octal SPI PSRAM, if the PSRAM
ECC function is enabled, the maximum ambient temperature can be
improved to 85 °C, while the usable size of PSRAM will be reduced by
1/16."* Default ambient rating for N8R8/N16R8 is **-40~65 °C**
(narrower than the non-PSRAM/R2 SKUs' -40~85 °C). A toy that might sit
in a hot car or near a sunny window should have this on the table when
the enclosure thermal review happens — either accept 65 °C as the
ceiling, or trade ~0.5 MB of the 8 MB PSRAM (8 MB → 7.5 MB usable) for
the wider 85 °C ECC-enabled range. Not currently referenced anywhere
in the hardware dossier.

---

## 4. Peak current — does the TPS63802 (2 A) budget still hold?

Worst-case coincidence: Wi-Fi TX burst + both I2S paths running + an
SD write landing in the same instant. `power-tree.md` §3 already
states "~0.9-1.0 A coincident worst case, sizes U4 at 2 A" — this
audit re-derives it against the datasheet's own numbers rather than
taking that on faith:

| Load | Peak | Source |
|---|---|---|
| ESP32-S3 module, Wi-Fi TX (802.11b, 1 Mbps, @20.5 dBm) | **355 mA** | Datasheet Table 12 — this figure is the module's *total* supply current during that TX condition (CPU+radio+everything), not an incremental delta, and is explicitly rated "at a 100% duty cycle" (§6.4.1 header) — i.e. this is the sustained-continuous worst case, not an instantaneous spike, so it's already conservative for this project's bursty (heartbeat/HTTP-call) traffic pattern. |
| MAX98357A into 8 Ω @ 3.3 V | ~410 mA pk (speech avg ~90 mA) | Amp IC's own datasheet, via `power-tree.md` §4 — not a WROOM-1 figure, cited here for the sum only. |
| microSD write burst / init | 100 mA / up to 200 mA | SD Association general figures, via `power-tree.md` §3 — not a WROOM-1 figure. |
| INMP441 mic | 1.6 mA | Negligible, included for completeness. |
| **Sum (module TX + amp peak + SD init + mic)** | **355 + 410 + 200 + 1.6 ≈ 967 mA** | Matches `power-tree.md`'s "~0.9-1.0 A" independently. |

Against TPS63802's 2 A continuous rating at 3.3 V out, that's a
**~2.1× margin**. Two reasons the real margin is better than the raw
ratio suggests:

1. **Boost-mode derating is not the concern here.** TPS63802's output
   current capability derates as Vin drops below Vout (deep-boost
   corner). But both chosen chemistries (3×AA: 4.5 V→3.0 V; 1S
   Li-ion: 4.2 V→3.0 V, per `power-tree.md` §1/§2) never go below
   ~3.0 V input against a 3.3 V output — that's a shallow boost ratio
   the whole way down, nowhere near the part's 1.3 V Vin floor where
   real derating shows up.
2. **Firmware already avoids the coincidence by policy**, not just by
   margin: `schematic-spec.md` §7 states "never write SD while the
   amp is at full output AND Wi-Fi transmits — content sync should
   run with SD_MODE low (mute)." So the 967 mA figure is a
   conservative worst-case bound the design doesn't actually plan to
   hit simultaneously in normal operation.

**Verdict: the 2 A budget holds**, with margin to spare even before
crediting the firmware-level mitigation. This is unchanged by the
N8R8-vs-N16R8 question in §2 — flash size has no bearing on the power
tree.

Caveat carried over from `open-questions.md` M1/M2 (not re-litigated
here): this is arithmetic on datasheet+component-datasheet numbers,
not a scope measurement. The real rail-sag and per-state-current
measurements are still open lab items, not settled by this audit.

---

## 5. Antenna keep-out and module placement (PCB antenna variant)

**Datasheet-honesty note first**: this module datasheet does **not**
give a numeric keepout dimension. Figure 3 (Pin Layout) shows a
"Keepout Zone" label graphically at one end of the module, and
§11.2 states verbatim: *"If module-on-board design is adopted,
attention should be paid while positioning the module on the base
board. The interference of the base board on the module's antenna
performance should be minimized. For details ... please refer to
**ESP32-S3 Hardware Design Guidelines > Section Positioning a Module
on a Base Board**"* — a separate document not available to this
agent. The datasheet also confirms, directly relevant to the
WROOM-1-vs-WROOM-1U choice already made: *"the [WROOM-1U] has no
antenna keepout zone"* (Note A under Figure 3) — because -1U's
external U.FL antenna has no on-module PCB antenna to protect at all.
**Action item: pull the actual numeric keepout dimension from the
Hardware Design Guidelines doc before laying out the first PCB** —
this audit cannot certify a specific millimeter value from its
canonical source.

With that gap named, the three rules a first PCB must not break are
standard RF-layout practice for any Espressif PCB-antenna module, and
match what `schematic-spec.md` §7 already commits to:

1. **No copper under or near the antenna** — no ground pour, no
   trace, on any layer, in the keepout zone at the antenna end of the
   module. A copper fill "just to tie down a stray net" is the classic
   way this gets broken by accident during autorouting cleanup.
2. **No metal mass near the antenna** — battery, speaker magnet, or
   any large metal enclosure feature. `schematic-spec.md` already
   ties this to a concrete cost: a magnet near the PCB antenna
   degrades RSSI, which drives Wi-Fi retries, which repeatedly
   re-triggers the **355 mA TX peak** from §4 above — an RF layout
   mistake that shows up on the bench looking like a battery/power
   problem, not an antenna problem.
3. **The antenna end of the module must overhang or sit at the edge
   of the host PCB**, oriented away from the enclosure's other RF-
   hostile features (speaker leads, battery leads) — not buried in
   the interior of the board where the ground plane and other traces
   surround it on all sides.

---

## Summary of corrections/flags for `schematic-spec.md`

None of these are wiring errors — the map is correct on every claim it
makes. Recommended additions before the first PCB:

1. Note that GPIO15/16 (amp BCK/LRC) are the chip's XTAL_32K_P/N pins
   — fine today (no RTC crystal planned), but forecloses one later.
2. Note GPIO9 (card-detect) is the fifth member of the native FSPI
   group (FSPIHD) — safe only because SD runs in 3-wire SPI, not QSPI.
3. Add GPIO39-42 (JTAG) and TXD0/RXD0 (UART0, module pins 36/37) to
   the table explicitly — even if the disposition is "NC, unused."
4. Confirm the numeric antenna keepout dimension from the Hardware
   Design Guidelines doc (not in this datasheet) before layout.
5. Log the R8/R16V PSRAM-ECC 85 °C/7.5 MB tradeoff against the
   enclosure thermal review.

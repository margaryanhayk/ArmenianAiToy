# Overnight hardware report — 2026-08-08

You asked: check every component from the microcontroller to the speaker,
give names and parameters, a clean circuit, working simulations, and buy
links. Here is everything, in plain words. The deep engineering behind
every claim is in the files named at the end.

---

## 1. Verdict on your components — the short table

| Component | Verdict | In plain words |
|---|---|---|
| **ESP32-S3 chip** | ✅ KEEP | Still the best choice. Every alternative loses: cheaper chips can't run our audio + memory, stronger chips have no Wi-Fi and would force a full firmware rewrite. |
| Chip memory size | 🔁 SMALLER IS FINE | Buy **N8R8** (8 MB), not N16R8 — 67% of the 8 MB sits unused today. Same speed, same everything, less money. |
| **Microphone INMP441** | ⚠️ MUST CHANGE for production | It stopped being manufactured in **2018** — everything sold now is old stock or clones. Its backup is ALSO dead, and the successor just closed orders. New production mic: **Infineon IM69D130** (better in every number: clearer signal, handles louder rooms, actively manufactured). Your bench mic keeps working — this is about the real toy. |
| **Amplifier MAX98357A** | ✅ KEEP | Actively made, in stock. At 3.3 V it gives 0.65 W clean — and the simulation shows that is MORE than enough (see § 3). |
| **Speaker** | ⚠️ SPEC CHANGED | The old requirement ("≥88 dB at 50 mm") turned out to describe a speaker that **does not exist anywhere on the market**. New honest requirement: 50–70 mm, ≥85 dB **measured in our enclosure**. Three real candidates ordered for the bench test. |
| **Regulator TPS63802** | ✅ KEEP | Simulation confirms: it uses 100% of the batteries' energy; the hobby-module regulator would kill the toy with 79% of the energy still in the batteries. |
| **USB protection set** | ✅ KEEP (1 change) | The fuse changes to a slightly bigger one (1206L075) — the old one would trip by itself on hot days while charging. |
| **Status LED WS2812B** | ❌ REMOVED on battery | It legally needs 3.5 V minimum — our battery toy never has that voltage. Three simple LEDs instead: cheaper, and two chips get deleted. |
| **Buttons** | 🔁 PART NUMBER FIX | B3F-**1002** (the firm 160-gram press). The old number was the too-light 100-gram version. |
| **Volume knob** | 🔁 PART NUMBER FIX | PTV09A-**4220F**-B103 — the old number was the version WITHOUT click-steps. |
| **microSD socket** | 🔁 CHANGED | The Molex socket is discontinued. New: **Hirose DM3AT-SF-PEJM5** — actively made, huge stock. |
| **Regulator's coil** | 🔁 TYPO FIX | Real part is VLS6045EX-2R2**N** — the "-2R2M" on the old list doesn't exist. |

Bottom line: **the architecture is right** — chip, amplifier, regulator,
protection all survived a hostile audit. What changed is one strategic part
(the microphone), one honest spec (the speaker), and five part numbers
that would have wasted an order.

## 2. Working simulations — open these on your phone

Each link opens a live, moving simulation in your browser. Yellow dots =
electric current flowing. No app, no account. You can drag sliders and
click switches.

1. **Why the toy needs the special regulator** — a tired 3.0 V battery still
   producing a full 3.3 V rail:
   https://www.falstad.com/circuit/circuitjs.html?ctz=CQAgjCCsCmC0Ac4AMA6ATEpaDs2DMS8aAbAJynZRJQgAseUcYYAUAG7hhogG2fddE1arWoNhIVJBYB3fuDSIMiQZJYAbEMoWI8GHZMkpMmNNyQsAZjzzEtaPnlsGVKSEawsASluzczfA7UAYbckCbBblpRwujScjj+DjZ2IRZyeJACijxZ4Nh2FgDmuQIFpeCk5rLyvLV5xfUCXDzEQjV61KqdIPDCLAAmPPp9dMR8o9wD0JYAhgCu6gAuLADGYxMi4-Z8sSYQeG41tNshJ4GQhSwl51qXG77VAE5QxNSjl8HJwekPH2+9fpyT47V7Be6NEFoe5QvxqAAOIGIyVGyMC30MvxBqJRQKRyRCaLuVxKROhdjJcIsAA8QKQ7PStADyHRwI5jAAdADOADVuUsAJZPaADbkAI1mSyW0CeAE8WLTRNQYfpIKRWWA+AMBQB7abc2AAPm5eB5eG5T1mAvUCqowVI1HG9oYfE1IHUOtmoq5AAoljrZdzoOpoKslk8dQA7AWrLkAShYQA
2. **The button and the volume knob** — exactly as they'll be wired to the
   chip (press "s" to press the button; drag the Volume slider):
   https://www.falstad.com/circuit/circuitjs.html?ctz=CQAgjCAMB0l3BWcMBMcUHYMGZIA4UA2ATmIxAUgpABZsKBTAWjDACgAlcMFEYw7r0I0oomlWzR6VGdARsAToL4DWvMMV5Uw8SGwDOyjep4gUePKIgAzAIYAbfQzYBzZectqQ2NFDYBjM3xwTSDLDytmcj0AdyNQtE9QvTdEswsw7189AAd0pN4IiJk-OLTjfJCtV0qijJ8ZNgAPEEIqGnC2iggRMBEAIQBVABVhgHkAOQAdfSYAPhmAcQAFAEkxsDwZgAochQZ9fRmj-QAXAHsZ04PTgEpOWg6VR8thUXaJKXeoOTYwDBENCe-BeZhQIm0uh+SAAaud7ABXAC2zjiNDeYC6CFMmMaSmx6i6bU8XUhcACrWCuMp4QQAm0UT8bmJZjpNMquVaGWphAyKDZJVi7J53NJNV5tIEEo5bDRTxQ4NB-PpNSBkqVGT0LRoviehCxPXAIhhYwAMoMALIAURmAGkJmN+jN5jMAIIAEQAwjsVus8PcgA
3. **The speaker circuit** — the amplifier's 1 kHz tone flowing through the
   noise filters into the 8-ohm speaker:
   https://www.falstad.com/circuit/circuitjs.html?ctz=CQAgjCAMB0l3BWcMBMcUHYMGZIA4UA2ATmIxAUgpABZsKBTAWjDACgA3cMFEFPPN149BVCGHjhohKLJgI2AG3AYZIvhmH9ZYZjMhsA7kPDawq06LYAnENmxrt9mShQ1ZeJXYd833lwJysPDuBsrmAYKYvPyi4HpQRiaxKpGJxtF8gc5ZVhmalv6FBgDGtHCFNBUpYszkBsY56lVi2g1FKS25iQDmyYGsvDkGAA60hO7NE91Us0ldU5Nt89WBNNM1bAAeIIRUxDK4+8S04O4AsgCCABrEeNgIGJcAOgDOAPYArgAuIz9vAAowG8ANYACQAXm9vu8AHYMACU2zsFRyNAwVHsJ0m7jwTHeAAsALZvV4jBgAQxBDGsgJK7wAloo3kT3gATBiKJE7MCUcB7PhVfn0JYgABiACFga8AGY06wM74MZG8zFoQVqkWmcUSlBvOXWBVKthAA
4. **USB charging protection** — a 12 V surge hits the port; the protection
   parts absorb it and the toy's electronics never see it:
   https://www.falstad.com/circuit/circuitjs.html?ctz=CQAgjCAMB0l3BWcMBMcUHYMGZIA4UA2ATmIxAUnBRABZsKBTAWjDACgA3cMG3WnjV54oIGmirZoSPNNEwE7AE6DqIlLSrD509gC8xmkP0NaUIiDDyQExSLQx4GCaIXYB3VSY2T7UZcbYhGqBwSZacB6mIdhBIZBRPsZ+sWF+CQDm0d5G2Ag0CQAOdNhC5iV86fJRqSH0ZSIJnrUm9ckCCQAedCLEYUakdOACAKoAygBCADoAzgCWAHaFAK4ALgBcswizAGqzCwD2SgC2AIYANucAngA0s7y7szPLShmMsyvnM4wz7N1gtAshCoKDQ4EIDAEvBAADEwLMAAoAFQAwn8xJQxHBAiDIJDqCAACLA2ZInZjWYAY3Op2OhXR2FyRkBknoQwBIEKSgOq0YlN5ABNZoxznzVtyFnNKb8gA

Two computed charts are also committed beside this file
(`sim/sim-power.png`, `sim/sim-audio.png`):

- **The battery chart** — proof that the regulator choice decides whether
  the toy dies with 79% of the battery still full or uses 100%.
- **The loudness chart** — an honest discovery: EVERY candidate speaker
  reaches the children's legal loudness limit (80 dB at 50 cm) with power
  to spare. Speaker sensitivity is really about **battery life** (a more
  sensitive speaker needs fewer watts for the same loudness), not about
  being loud enough. The gain resistor R30 is what pins the maximum
  loudness below the legal line.

## 3. Where to buy — every part, with live stock checked tonight

Full table with one link per part, the price seen, and the stock seen:
**`docs/hardware/buy-links.md`**. Headline: 28 of 31 items are purchasable
right now. The 3 that aren't as-originally-specified all have named,
in-stock substitutes (mic, SD socket, coil — see § 1).

## 4. What only the lab can settle (unchanged list, one addition)

- Speaker bench test (the ≥85 dB measured gate) — candidates are named in
  the shopping list, test procedure in `audit-components.md` § 5.
- AA vs rechargeable battery — still your decision; both circuits are fully
  specified either way.
- One layout note found tonight: the amplifier's clock pins (GPIO15/16)
  are also the chip's only clock-crystal pins — fine today, but it quietly
  blocks a future bedtime-clock feature; noted in `audit-mcu.md` § 3 for
  the PCB day.

## The deep files (full engineering, numbers, sources)

| File | What's inside |
|---|---|
| `audit-mcu.md` | Chip-vs-chip comparison, memory arithmetic, every pin checked |
| `audit-components.md` | Every part's parameter card, runner-ups, SPL math both ways |
| `buy-links.md` | Live links + prices + stock + lifecycle warnings |
| `sim-research.md` | How the simulation links are generated (reproducible) |
| `shopping-list-one-toy.md` | The corrected buy list — this is the one to order from |
| `sim/` | The simulation sources + the two computed charts |

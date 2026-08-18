# Areg — industrial design and visual construction (rev A)

**Written 2026-08-18.** The launch-readiness roadmap has carried one line
under hardware since it was written: *"Enclosure, battery, power management.
No case… A bare devkit is not a toy."* Every other hardware surface has a
document — power tree, schematic spec, BOM, component audit, PCB layout
contract. The shape of the object a child actually holds has none. This is
that document.

Claims are tagged the way `audit-components.md` tags them:
**[FIXED]** already decided and paid for somewhere else in the repo (changing
it costs money or a re-render), **[DERIVED]** arithmetic or a direct
consequence of a [FIXED] item, **[PROPOSED]** my recommendation, open to the
owner, **[OPEN]** a decision only the owner or a lab can close.

---

## 0. The finding that matters most

**The shape is already ~70% decided, and nobody wrote it down.** Between the
schematic spec, the component audit and 92 rendered Armenian audio clips, the
enclosure has been given a speaker diameter, a sealed chamber volume, a
minimum mic-to-speaker distance, a screwed-access rule, a button count, and —
this is the one that surprises — **two button colours that can no longer be
changed without re-rendering audio.**

So this is not a blank page. Most of what follows is reading the constraints
that already exist back out, and the design that fits them.

Second finding, in § 3: the product's own front page promises a **soft toy**
while every acoustic requirement on file describes a **rigid sealed box**.
That is not a contradiction to be discovered during tooling.

---

## 1. What the electronics already decided about the shape

| Constraint | Value | Source | Consequence for the shell |
|---|---|---|---|
| Speaker | 50–70 mm driver, 8 Ω, Fs ≤300–400 Hz | schematic-spec §5, open-questions #2 | A front face at least ~75 mm across before any bezel |
| Speaker chamber | **Sealed, 50–100 cm³, gasketed. Ported rejected** | schematic-spec §5 | A rigid, closed, screwed volume — not stuffing, not fabric |
| Chamber sealing | Every boss and wire pass hot-melted or grommeted | schematic-spec §5 | Seal the *chamber*, not the toy. This is a moulding rule, not an assembly wish |
| Mic port | Ø0.7–1.0 mm, gasketed, **opposite face from the speaker, ≥40 mm away** | schematic-spec §5 | There is no echo cancellation. Geometry is the AEC. Constrains where the mic hole can be to within a few cm |
| PCB | 4-layer, ~60×50 mm | rev-a-design-inputs | An internal bay ≥ 66×56 mm with 12 mm of headroom |
| Antenna | Keep-out all layers; no battery, no speaker magnet, no metal in it | rev-a-design-inputs, schematic-spec §7 | The module edge must face a plastic wall with nothing behind it. **A magnet next to the antenna reads as a battery fault, not an RF fault** |
| Buttons | 3 × tactile, 160 gf (MAIN GPIO18, YES 21, NO 47) | rev-a-design-inputs | Three actuators, and 160 gf is light enough for a 4-year-old |
| Volume | Detented THT potentiometer | BOM RV1 | A physical knob or wheel with click detents — not a soft key |
| Status light | **3 discrete LEDs**, 2–3 mA (WS2812B is unusable on battery: VDD min 3.5 V) | audit-components §8 | One diffused window, three colours, no rainbow |
| microSD | Internal, **no user-accessible slot** (15×11 mm = choking hazard at 4–7) | schematic-spec §6 | Behind screws. A removed card is a silent toy |
| Battery | 3×AA off-board holder (run 1) | power-tree §1, rev-a-design-inputs | A back bay ~60×34×18 mm and a screw-secured door |
| Loudness | 0 dBFS ≤ **78 dB LpA at 50 cm** | schematic-spec §4 | Grille open area cannot be used to "get it louder" — the ceiling is legal, not acoustic |
| Drop | 10 × 850 mm onto steel, battery installed | open-questions M7 | Corner radii, rib design, and a compliant outer layer |

**[DERIVED]** Working the chamber arithmetic: a 50 mm driver in a cylindrical
chamber Ø60 mm needs ~28 mm of depth for 80 cm³ — deep and awkward. The same
80 cm³ as a wide shallow cavity Ø85 × 14 mm is easy and puts the driver in the
middle of a broad front face. **The acoustics want a disc, not a cube.**

---

## 2. What the shipped Armenian content already fixed — and cannot un-fix

This is the constraint most likely to be broken by someone drawing a nice
enclosure without reading `backend/content/offline-games/game-clips.json`.

- **YES is green («կանաչ»). NO is red («կարմիր»).** [FIXED] Not a suggestion:
  it is spoken in the mind-reader intro, in all 16 guess clips, in the replay
  prompt and in the buzzer game. Changing a button colour now means re-rendering
  a large part of a 92-clip library and running a fresh listen test.
- **The toy names colours, never positions.** [FIXED] It never says "the left
  one", so left/right is genuinely free — but see § 5 for why the spoken order
  should still decide it.
- **The toy addresses players by colour, never by name, and never announces a
  loser.** [FIXED, CLAUDE.md] Two-player buzzer implies two children reach the
  two answer buttons *at the same time* — that sets a minimum spacing, and it
  means the toy is played on a floor or a table between two children, not held.
- **A quick press starts a story; a 2 s hold opens the menu.** [FIXED,
  firmware 1.3.3] The MAIN button is held down by a small hand for two seconds.
  It must be big, it must not be sharp, and it must not be reachable by
  accident when the toy is squeezed.
- **The mic is off in every offline game.** [FIXED] The honesty rule forbids
  the toy claiming to have heard anything. Nothing in the shell should suggest
  "always listening" — no ring of holes that reads as a microphone array.

---

## 3. The contradiction to settle before anything is tooled

`wwwroot/index.html` tells every parent, in three languages: *"Areg is a **soft
toy** that tells your child a story…"*

Every acoustic requirement on file describes the opposite object: a rigid,
gasketed, sealed 50–100 cm³ chamber whose leaks are the documented cause of
"sounds like a phone speaker" — *the hardware twin of the "thin, far away"
complaint already fought in renders* (schematic-spec §5).

Both are right. They are not the same layer.

**[PROPOSED] Two-layer construction, and it is the whole design:**

1. **The core** — a rigid, sealed, screwed acoustic pod. Holds the driver, its
   chamber, the PCB, the battery bay, every control, every opening. It is the
   toy, electrically and acoustically. It never gets washed.
2. **The shell** — a removable, machine-washable soft cover the core drops
   into. It is the toy, emotionally. It carries no electronics, no fasteners
   a child can swallow, and no cords.

What this buys, in order of how much it matters:

- A child's toy that goes in a bed **will** need washing. A sealed pod with a
  removable cover is the only version of that which does not destroy the
  electronics.
- The soft layer is free drop protection for M7 (10 × 850 mm).
- The seal argument survives intact, because the fabric is not part of it.
- The family can be sold a second cover.

**The cost, stated honestly:** fabric over a speaker grille is high-frequency
loss, and fabric over a Ø0.8 mm mic port is a dead microphone. So the shell
must have a **bound, permanently open die-cut window at the sun face and an
open crown at the mic** — the cover frames the face, it never crosses it. And
**M10 (the acoustic prototype gate) must be run with the fabric fitted**, not
on a bare printed shell, or it measures an object that will never be sold.

---

## 4. The form: a sun

*Areg* means sun. The mark already exists — gold disc, deep violet ring, eight
rays, warm vellum ground (`wwwroot/icons/areg-512.png`). The acoustics
independently asked for a wide shallow disc (§ 1). Those two agree, which is
rare enough to take.

**[PROPOSED]** A disc that stands on its own lower edge, tilted back ~12° so
the face aims at a seated child's head rather than at the ceiling.

```
Core outer:      Ø 110 mm × 55 mm deep
With soft shell: Ø 128 mm × 68 mm  (~9 mm of shell + foam per side)
Mass, assembled: ~310 g  (3×AA 69 g + core ~120 g + driver ~40 g
                          + PCB/wiring ~20 g + shell/foam ~60 g)
```

310 g is carryable by a four-year-old and heavy enough not to feel disposable.

### Section, front to back

| # | Layer | Depth | Note |
|---|---|---|---|
| 1 | Grille face + gasket | 3.0 mm | Sun-face pattern; open area sized for 78 dB, not for maximum |
| 2 | **Sealed chamber** | 15.0 mm | Ø85 cavity ≈ **80 cm³**, mid of the 50–100 cm³ window |
| 3 | Chamber back wall | 2.5 mm | Ribbed; every pass-through grommeted or hot-melted |
| 4 | Electronics bay | 12.0 mm | PCB flat, module edge to the antenna wall |
| 5 | Battery bay | 18.0 mm | 3×AA holder, contacts keyed |
| 6 | Back door | 3.0 mm | 4 captive screws |
| | **Total** | **53.5 mm** | drawn at 55 mm |

The antenna wall is the **rim segment behind the module edge** — plastic, no
copper, no battery, no driver magnet inside the keep-out. On a disc this is
easy: the battery sits deep and central, the magnet sits forward and central,
the module points out at the rim.

### Where the microphone goes, and why it is not settled

The rule is *opposite face, ≥40 mm*. On a disc, a mic on the literal back
faces away from the child, which is worse than the rule was trying to prevent.

**[PROPOSED]** Mic at the **top crown of the rim**, facing up: ~55 mm of path
from the driver centre, a 90° axis change, and the sealed chamber wall as a
structure-borne barrier between them.

**[OPEN — gated by measurement]** This is *near* the rule, not inside it. It
must be proven at M10 with real narration playing, because **barge-in** —
interrupting a story to ask a question — is the feature that actually depends
on it. If it fails, the fallback is a mic boom in a moulded ray at the top,
buying another 15 mm. That is a tooling-time decision, so it must be answered
before the tool is cut, not after.

---

## 5. Control layout — the front face

```
              ·  ·  ·        ← eight moulded rays (upper five visible)
           ·           ·
        ┌───────────────────┐          top crown ── mic port Ø0.8 mm, gasketed
        │    ╭─────────╮    │
        │   │  SUN FACE │   │          the grille. Fixed, sealed, not a button
        │   │   Ø 85    │   │
        │    ╰─────────╯    │
        │         ●         │          one diffused light window (3 LEDs)
        │   ◯     ⬢     ▣   │          green YES │ gold MAIN │ red NO
        └───────────────────┘
             rim right ── volume wheel, detented, ~4 mm proud
             rim lower-back ── power slide  ·  USB-C behind a flap
```

- **MAIN** is the largest, gold, dead centre of the control band, ~26 mm across
  and domed. It is held for two seconds by a small hand, so it is the one that
  gets the generous radius and the light 160 gf switch under it.
- **The sun face is not a button.** Pressing on a sealed chamber's front wall
  is how the seal dies. The face is fixed; the hand goes to the band below it.
- **Green sits left of red** [PROPOSED], because the toy always speaks them in
  that order — «Հա՝ կանաչ, ոչ՝ կարմիր» — and a child who cannot yet read still
  maps a spoken sequence onto left-to-right. Costs nothing; only available now.
- **Colour is not the only difference between YES and NO, and that is
  deliberate** [PROPOSED]. Red–green is the common colour-blindness axis, and
  ~1 in 12 boys is affected. Since the audio can only say a colour, the shell
  has to carry the redundancy: **YES is a circle with a raised dot, NO is a
  hexagon with a raised bar.** Shape and texture are free at tooling time and
  impossible to add afterwards. This is the single change here I would fight
  for hardest.
- **The two answer buttons are exempt from the brand palette.** The manuscript
  greens and pomegranate reds (§ 6) are beautiful and, as signal colours for a
  four-year-old, ambiguous. Answer buttons are saturated and unmistakable:
  green ≈ `#3FA34D`, red ≈ `#D7382E`. Everything else on the toy obeys the
  palette.
- **One light, three states.** The production build has three discrete LEDs
  behind a single diffuser — green (listening), amber (thinking), red (fault).
  **[OPEN — firmware]** The bench firmware has a five-state colour vocabulary
  on the devkit's RGB pixel (idle / recording / upload / playing / error). On
  battery hardware two of those states have nowhere to go. The mapping needs
  deciding by the person who knows what a parent needs to see, not by whoever
  is wiring the LEDs.

---

## 6. Materials, colour, finish

| Part | Spec | Why |
|---|---|---|
| Core shell | PC/ABS, 2.5 mm nominal wall, matte texture | Sealed-chamber stiffness and drop; matte hides a toy's life |
| Chamber gasket | Silicone or PORON, compressed 30–50 % | schematic-spec §5, verbatim |
| Grille | Moulded slots + acoustically transparent mesh behind | Slots < 5 mm so nothing is posted into the chamber |
| Buttons | TPE over-mould, 160 gf domes | Soft to a fingertip, keeps the 160 gf switch feel |
| Soft shell | OEKO-TEX knit, 3 mm foam backing, machine wash 30° | Wash without touching the electronics |
| Shell closure | Interior hook-and-loop or a covered zip on the back | No cord, no bead, no snap a child can free |
| Screws | 4 captive, Torx T6 or Ph1, back door only | Battery and card behind a tool |

Palette, from the mark and `docs/story-cover-prompts.md` — the toy should
belong to the same world as its own story covers:

- Sun face / MAIN: **gold `#C6952F`**
- Ring around the face: **deep violet `#4A2E7F`** (the mark's ring)
- Body / shell: **warm vellum `#EFE4CE`**
- Accents, rays: **lapis `#2A4A8F`**, **pomegranate `#7E2547`**
- Answer buttons: signal green / signal red only (see § 5)

No PVC, no phthalates, no coin cell anywhere in the product, no cord longer
than 220 mm, no magnet a child can free — and none near the antenna anyway.

---

## 7. The power switch that does not exist

**[OPEN — real gap, found writing this.]** There is no on/off control anywhere
in the design. The only switch in the whole file set is the one moulded into an
AliExpress AA holder — an internal bench part.

The toy idles at ~70 mA. A set of AA cells is **~1.75 days** (power-tree §1).
As drawn, a parent whose child left the toy on has one option: a screwdriver.

Three ways out, and the choice is the owner's:

1. **Recessed slide switch on the lower rim** — a few cents, one opening,
   reachable by an adult thumbnail and hard for a child to flick by accident.
   *Recommended.*
2. **A firmware sleep state** with a long-press wake. Zero BOM cost, but M6
   already says ≥7 days runtime *depends on the sleep work landing*, and it is
   not landed.
3. **Accept it** — the toy always runs, and cells are consumable.

Whatever is chosen, it must be chosen before the tool: an opening is a mould
feature.

---

## 8. What is different if the owner says no

Briefly, because these are alternatives, not recommendations.

- **A cube or a "creature" with limbs.** Costs the chamber the shallow disc
  gave it for free; the driver has to go somewhere deep, or the chamber shrinks
  below 50 cm³ and the voice thins out. Limbs are also drop-test failures and
  small-parts findings.
- **A fully rigid toy, no soft shell.** Cheaper, one part fewer, but the front
  page has to stop saying "soft toy", and there is no washing story.
- **Fully plush, electronics in a sewn pod.** The sealed chamber becomes
  extremely hard; this is the version that sounds like a phone in a sock.
- **Screen or lights beyond the one window.** Out of product scope; a screenless
  toy is a stated position, not an omission (`docs/screenless-story-selection-design.md`).

---

## 9. Open decisions — owner

1. **Soft shell or rigid only** (§ 3). Everything else in this document is
   downstream of it, including whether the front page's "soft toy" is true.
2. **Form: the sun disc, or something else** (§ 4). If something else, the
   chamber arithmetic in § 1 is where it must start.
3. **Power switch: slide, sleep, or accept** (§ 7).
4. **Shape/texture redundancy on the answer buttons** (§ 5) — free now,
   impossible after tooling.
5. **LED state vocabulary mapped from five to three** (§ 5) — firmware, but a
   product decision.
6. **Cover art direction for the soft shell** — plain vellum, or the manuscript
   illumination language of the story covers. This one is not urgent, and it is
   the only genuinely aesthetic question in the list.

## 10. Gates that must close before a tool is cut

| Gate | What it is | Where it already lives |
|---|---|---|
| M8 | SPL, assembled toy, class-2 SLM at 50 cm, EN 71-1 method | open-questions M8 |
| M10 | Acoustic prototype in 3–5 printed enclosures, real bedroom — **amended here: run it with the soft shell fitted** | open-questions M10 |
| — | **Mic/speaker isolation with narration playing**, proving barge-in works at the crown position (§ 4) | new; belongs with M10 |
| M7 | Drop, 10 × 850 mm, battery installed — with the shell, since it is load-bearing for this | open-questions M7 |
| EU/US | EN 71-1 sound category (voice-toy 80 dB vs close-to-ear 60–70 dB) — **a 15 dB swing that gates enclosure tooling** | open-questions, owner decision 3 |
| Speaker | Driver choice, T/S published, sensitivity measured in-enclosure | open-questions #2 |

The category question is the one to spend money on first. It is a written
pre-assessment from a notified body, it is already costed at €500–1,500 in
`open-questions.md`, and its answer changes the grille, the chamber and the
gain resistor at the same time.

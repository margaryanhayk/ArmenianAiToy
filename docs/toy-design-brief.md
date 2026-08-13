# Areg — toy design brief (physical form, brand, packaging)

Written 2026-08-13. This is the document an industrial designer, a
packaging designer, or a factory works from — and the single place the
owner's open look-decisions live. It was written because the repo had
**no enclosure spec, no CAD, no materials decision, no packaging design,
and no visual identity applied to the physical object** — the roadmap's
own words: *"A bare devkit is not a toy"*
(`launch-readiness-roadmap.md`). Everything below is either sourced from
an existing document (cited) or explicitly marked **undecided**. Nothing
here invents a decision.

How to read it:

- **§2–3 are FIXED** — electrical and safety constraints already
  designed and reviewed. A concept that violates them is wrong, not
  bold.
- **§4–5 are DIRECTION** — the established brand language, extended to
  the object as a recommended starting point, not a decision.
- **§6 is OPEN** — the numbered decisions only the owner can make.
  The brief is complete when §6 is empty.

---

## 1. What the object is

A screenless Armenian-speaking storyteller for children aged 4–7. The
whole child-facing interface is **sound, one main button, a volume
knob, and an LED** (`screenless-story-selection-design.md`). The toy
works offline from an internal SD story cache; Wi-Fi is for sync and
voice turns. It lives in a child's room, including at bedtime — the
product's tone rules (calm, warm, never a chatbot) apply to the object
as much as to the voice.

One product decision shapes everything visual: **the toy has no name to
the child.** The system prompt forbids it from stating or accepting a
name; "Areg" is the parent- and brand-facing name only. So the object
itself must work as a *thing with a voice inside*, not as a named
character demanding a face — whether it gets a face at all is owner
decision §6.1, deliberately not made here.

## 2. Hard constraints — the object around the board

### 2.1 The rev-A PCB (source: `hardware/pcb/phase2-status.md`, placement in `hardware/pcb/generate_pcb.py`)

- **74 × 62 mm rectangle, 1.6 mm, 4 layers. All 100 components on the
  TOP side** — the bottom is copper only, so the board can sit close
  to a flat internal surface on its underside.
- **There are NO mounting holes — deliberately.** The board file says
  it plainly: *"their positions are an enclosure decision, and
  arbitrary screws would look settled when they are not."* The
  designer's first deliverable back to the electronics is a boss/screw
  plan (§6.9).
- Edge map, board-local, top-left origin (things a case must line up
  with):

  | Feature | Where | Enclosure consequence |
  |---|---|---|
  | Wi-Fi antenna (U1) | **overhangs the LEFT edge by 6 mm** | keep-out: no copper, no battery, no speaker magnet, no metal near it — *"an RF mistake that presents as a battery problem"* (`schematic-spec.md` §7) |
  | USB-C (J1) | **BOTTOM edge**, mating face on the outline, body overhanging | plug enters from below/behind; the receptacle cannot be recessed behind the case wall without a redesign |
  | microSD socket (J3) | card opening faces the **RIGHT edge** | internal-only (§2.3) — the case must NOT expose it |
  | Speaker connector (J4, JST-PH) | **RIGHT edge**, mid-height | speaker chamber is on the right side of the board, wires short |
  | Battery connector (J2, JST-PH) | near the **BOTTOM edge**, left of centre | 30 mm from the antenna keep-out; battery bay goes low/central, never left |
  | Microphone (U3/U8) | **FAR LEFT**, lower half — measured **65.5 mm** from J4 | mic port on the opposite face from the speaker (§2.2) |
  | 3 tactile buttons (SW1–3) | a row across the **middle**, 9.5 mm pitch | see §3.1 — which are child-facing is undecided |
  | Volume pot (RV1, Bourns PTV09A) | **bottom-left corner** | shaft needs a knob through the case; its two plated mounting posts are electrically undecided (§6.7) |
  | 3 × 0603 LEDs (D1–3, red/blue/green) | a row along the **TOP edge**, 4 mm pitch | need a window / light pipe / diffuser — none specified today |
  | 12 factory test pads | top edge | factory fixture access; not child-facing |

- If the enclosure later demands a smaller board (~60 × 50), the
  recorded honest answers are *"double-sided assembly or dropping the
  pot — not tighter routing"* (`phase2-status.md`).

### 2.2 Acoustics (source: `docs/hardware/schematic-spec.md` §5, `open-questions.md`)

- **Sealed speaker chamber, 50–100 cm³, gasketed** (foam/silicone
  compressed 30–50 %). Ported enclosures are **rejected**, with
  reasons that bind the ID: *"a port is a tuned resonator an ID-drawn
  box will mistune, a hole a child feeds, and an excursion risk."*
- *"Seal the chamber, not the toy"* — every boss and wire pass into
  the chamber gets hot-melt or a grommet. A leaky chamber is the
  documented cause of "sounds like a phone speaker".
- **Driver size is UNDECIDED (§6.3) and locks the front face**: the
  spec was widened from 50 mm to **50–70 mm** (a 77 mm candidate has
  been floated in `buy-links.md`) because ≥88 dB/W/m at 50 mm turned
  out to be an empty set on the market. The audit's exact words:
  *"the enclosure is not yet tooled — this is free today and
  impossible after tooling"* (`audit-components.md`).
- **Speaker sensitivity and rail count are one decision**
  (`power-tree.md` §4): ≥85 dB/W/m *measured in the enclosure* keeps
  the whole product single-rail 3V3. The enclosure prototype is part
  of that measurement (M10), not downstream of it.
- **Mic port: Ø0.7–1.0 mm, gasketed to the enclosure, on the opposite
  face from the speaker, ≥40 mm away** — *"there is no echo
  cancellation, so geometry does that job."*
- **A speaker grille is required and does not exist.** Measurement M10
  (`open-questions.md`) demands *"real speaker + grille + gasket in
  3–5 printed enclosures"* and warns *"a grille fine on a flat panel
  can be −6 dB and boxy on a resonant box."* No hole pattern, open
  area, or geometry has ever been drawn.

### 2.3 Safety and regulatory (sources cited per line)

- **Screw-closed enclosure** — the SD card is internal-only:
  *"15×11 mm = choking-hazard finding at 4–7; … a removed card is a
  silent toy"* (`schematic-spec.md` §6).
- **Screwed battery door** (Option A, 3×AA): *"compartment:
  screw-closed — toy-safety requirement"* (`power-tree.md` §1). Adds
  **85 g of mass**. Option B (run 2, 1S Li-ion) requires an
  **end-user-replaceable connectorized pack behind a screw hatch**
  (EU Battery Reg 2023/1542 Art. 11, from 2027): *"design it in now;
  it is free now and a tooling change later"* (`power-tree.md` §2).
  Chemistry for run 1 is owner decision §6.8.
- **Touch surface < ~48 °C** with the enclosure closed while charging
  (measurement M3 — exact toy-standard limit to be confirmed with the
  lab, deliberately not quoted from memory).
- **Drop: 10 × 850 mm onto steel, battery installed** (M7).
- **EN 71-1 sound category GATES the tooling** (`open-questions.md`
  owner decision 3): voice-toy (LpAeq ≤ 80 dB @ 50 cm) vs
  close-to-ear (60–70 dB) is a notified-body call worth ~15 dB of
  ceiling — the €500–1,500 written pre-assessment is *"the
  highest-leverage spend in the whole plan"* and must land **before
  enclosure tooling**. The 2026 edition of EN 71-1 exists; clause
  4.20 must be re-confirmed against the 2011+A3 numbers quoted.
- No small removable parts, no holes a child feeds (the rejected
  speaker port doubles as the general rule), nothing that pinches at
  the battery door or knob.

### 2.4 Cost ladder (source: `docs/hardware/bom.md`)

| Scale | Landed cost/unit incl. enclosure | Enclosure method |
|---|---|---|
| 50 | ≈ $78 | 3D printed |
| 500 | ≈ $36 | soft tooling |
| 5000 | ≈ $17 | injection tooling ($5–12k separate) |

*"Cost concentrates: at 50 units → enclosure + NRE."* The first run's
biggest cost line IS this brief's subject.

## 3. Ergonomics for ages 4–7

### 3.1 The button

The child's entire agency is one button: tap to choose, hold for
"surprise me", press to interrupt (`screenless-story-selection-design.md`
— which names the design rule: don't *"overload the single button into
an unlearnable gesture soup"*). On the board this is a bare **6 mm
tactile switch** (160 gf) in a row of three at 9.5 mm pitch; what a
four-year-old actually presses — cap size, travel feel, color,
placement on the case — is unspecified. The BOM already hints at the
production answer: *"rev B: silicone keypad + carbon domes"*
(`bom.md`), i.e. a soft-touch surface, which interacts with the
material decision (§6.5). The pin map also gives YES / NO buttons
(GPIO 21/47) — **which of the three switches are child-facing and
which are parent/factory is undecided** and shapes the face of the toy.
(Bench-only note: the current dev-kit button on GPIO0 is a strapping
pin and is already moved to GPIO18 in the production map — the case
never exposes a boot-mode trap to a child.)

### 3.2 The knob

The volume pot was *chosen over buttons/encoder because its ANGLE is
the display for a pre-reader* (`schematic-spec.md` §2) — the one
genuinely ID-driven electrical decision already on record. The case
therefore needs a knob whose rotational position is visible/feelable
(detented, 10 kΩ Bourns PTV09A, bottom-left of the board). Its metal
frame's grounding is owner decision §6.7 because the knob is *"the
part a child touches"*.

### 3.3 The light

One LED vocabulary already exists and the case must let it read:
**IDLE = slow soft blue breathe; CHOOSE = amber pulse ("press now")**
(`screenless-story-selection-design.md`). The board carries three
discrete 0603 LEDs (red / blue / green) on the top edge; no window,
diffuser, or light pipe is specified. For a bedtime object the light
must be gentle — a point-source LED glaring in a dark room is a
product defect, not a detail.

### 3.4 Mass and hands

3×AA adds 85 g (`power-tree.md`); total product mass is not yet
budgeted. Held-vs-placed is undecided with the form: a 66–77 mm
driver pushes toward a placed/table object; the one-button barge-in
pattern assumes a child can reach the button mid-story either way.

## 4. Brand visuals → the object

### 4.1 The established language

The product already has a developed identity — entirely on screens,
never on the object:

- **The Armenian manuscript palette** — *"lapis and cinnabar and gold
  on vellum, rather than the generic blue-on-white it replaces"*
  (`mobile/AregParent/src/theme.ts`; tokens defined in
  `wwwroot/parent.html`). Key tokens: lapis brand `#25417D`, vellum
  page `#F7F1E3`, warm gold band `#F6E7C4`, cinnabar danger
  `#A32D22`, pomegranate deep `#7E2547`, foliage green ok `#2C5233`.
- **The Toros Roslin cover style** (`docs/story-cover-prompts.md`) —
  flat manuscript illumination inside a rounded-arch frame, with its
  own (slightly different) prompt palette: lapis `#2A4A8F`, cinnabar
  `#C2432B`, gold leaf `#C6952F`, pomegranate `#7E2547`, foliage
  `#46664B`, vellum `#EFE4CE`. And the guardrail that carries to the
  object: **"Keep them calm. … No bared teeth, no fear, no dark
  menace."**
- **The Areg sun mark** — a flat amber sun disc with twelve
  rounded-cap rays ("Areg" = sun). It exists only as raster PNGs, in
  **two divergent variants** (the web icon adds an off-palette indigo
  ring and a gradient background; the mobile icon is flat on cream).

### 4.2 Recommended starting direction (direction, not decision)

Extend the manuscript language to the object: a **warm vellum/cream
body** (never gadget-white, never toy-primary-red), with **lapis
and/or gold accents** on the child-facing controls (button, knob), and
the **sun mark** as the object's one graphic — e.g. the speaker grille
pattern is the obvious candidate to *be* the sun (rays as acoustic
openings), which would make the toy's largest necessary hole its
identity instead of a compromise. Calm over cute; decorative over
mascot-like — consistent with the nameless-narrator decision until
§6.1 says otherwise. This direction is cheap to reject and exists so
the designer does not start from nothing.

### 4.3 Brand asset gaps (work items, each its own small slice — not done in this brief)

1. **No vector master for the sun mark** — before anything is printed
   or molded, the mark needs an SVG source of truth.
2. **The two icon variants disagree** (indigo ring + gradient vs flat
   cream) — pin one.
3. **Two adjacent-but-different palettes** (CSS tokens vs cover-prompt
   palette) — the physical object and packaging must pin ONE set of
   inks; recommend the cover-prompt set for print (it was written for
   pigment, the tokens for screens).
4. `manifest.webmanifest` carries stale pre-palette colors
   (`#f6f4fb` background) — off-brand on the one surface a parent
   installs to a home screen.
5. The marketing page logo is a literal `🧸` emoji placeholder
   (`wwwroot/index.html`) — the only "physical toy" image the product
   shows anyone today, and it's a teddy bear the product is not.

## 5. Packaging & box

Nothing has been designed; the roadmap names it once as part of the
manufacturing gate: *"Factory NVS burn station, claim-code printing,
QR on the box"* (`launch-readiness-roadmap.md`). What the box must do:

- **Carry the pairing QR / claim code** — and agree with the toy. The
  claim code is deliberately **not consumed** and works for the toy's
  whole life (second parent, re-pairing after unlink), and the QR is
  *printed on the toy itself* (CLAUDE.md § Consumer platform). So the
  box QR and the toy QR are the same mark, the box is not the only
  copy, and losing the box must not orphan the toy. Placement on the
  toy: somewhere a parent finds and a child ignores — battery-door
  interior is the natural candidate (undecided).
- **Support the real unboxing flow**: scan → parent app → claim →
  BLE Wi-Fi provisioning. The first minute is a parent with a phone;
  the box copy should walk exactly that path and nothing else.
- **Regulatory content**: CE mark, age grade (4–7; and the standard
  not-under-3 warning is NOT applicable only if the toy passes as
  suitable — a notified-body/lab question alongside §2.3), battery
  chemistry warnings per the §6.8 outcome, EN 71 / RED references,
  importer/manufacturer address.
- **The name nuance**: "Areg" appears on the box as the brand — the
  toy never says it. Box copy should introduce the *storyteller
  inside* without promising a named friend; the same
  companion-boundary rules that govern greetings (no "I'll be waiting
  for you") govern packaging copy.
- **Language**: the parent surface rule is trilingual (hy/en/ru on
  the dashboard); the box should follow the same rule, Armenian
  first.
- **Style**: the manuscript language (§4) — a box that looks like an
  illuminated book cover is the identity doing its job; the eleven
  story covers already prove the style works in print-like form.

## 6. Open owner decisions

The brief is done when this table is empty. None of these are made in
this document.

| # | Decision | What it locks | Already on record |
|---|---|---|---|
| 1 | **Character vs object** — does the toy get a face/mascot, or stay an abstract "thing with a voice"? | The entire form language; also whether packaging shows a character | Toy is nameless to the child (system prompt); owner scoped character design OUT of this brief |
| 2 | **NFC card/figurine fork** (Yoto/Tonies pattern) — reserve enclosure space + a tap zone now, or not | Front-face layout, internal space for a PN532 reader (~$3), the whole Phase-2 UX | Flagged as *"the real product"* and *"the single biggest UX jump"* in `screenless-story-selection-design.md`; absent from rev A |
| 3 | **Speaker driver size** (50 / 66 / 77 mm) | Front face diameter, chamber volume, and the 3V3-vs-5V rail question | Candidates named for M10 in `open-questions.md`; *"free today, impossible after tooling"* |
| 4 | **EN 71-1 sound category + EU run-1** — commission the notified-body pre-assessment | The loudness ceiling (~15 dB swing) and the tooling go-date | `open-questions.md` owner decision 3; *"gates enclosure tooling"* |
| 5 | **Material system** — rigid plastic (ABS/PC/PP), silicone overmold, or fabric/plush shell | Tooling cost, cleanability (saliva-resistance at 4–7), the rev-B silicone keypad path, drop behavior | Only hint on record: rev-B *"silicone keypad + carbon domes"* (`bom.md`) |
| 6 | **Colorway** — accept/adjust the vellum-body + lapis/gold direction (§4.2), and pin ONE print palette (§4.3.3) | Every printed and molded surface | Two candidate palettes exist; neither pinned for print |
| 7 | **RV1 mounting-post grounding** — GND (grounds the metal frame a child touches through the knob) or floating | A PCB edit + an enclosure-facing-hardware stance | Explicitly left as *"an owner call"* in `phase2-status.md` |
| 8 | **Battery chemistry for run 1** — 3×AA (screwed door, €150–300/yr cells) vs Li-ion (screw hatch, +$8.65 BOM + €6–10k certs) | The door/hatch, mass, charging port role | `open-questions.md` owner decision 1; split recommendation on file: AA run 1, Li-ion run 2 |
| 9 | **Mounting-hole positions** — the designer proposes, the PCB adopts | Closes the deliberate hole in `phase2-status.md`; unblocks Gerbers | *"Deliberately not invented"* — waiting on exactly this brief's successor |

## 7. Out of scope of this brief (deliberate)

No CAD, no renders, no concept art, no character design, no mascot, no
tooling engagement, no packaging artwork. This document collects what
is fixed and what is open so that the next step — concepts, or a
designer engagement — starts from the true state instead of
rediscovering it. The brand-asset gaps in §4.3 are recorded here but
fixed in their own slices.

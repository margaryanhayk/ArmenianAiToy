# Writer prompt v3 — bounded story arc & stop condition (2026-05-03)

**Status:** evidence / design note only. **No production code change.**
No `ChatService` change. No runtime prompt change. No provider switch.
No new model / API call. No seed-bank, character-name-bank, generator,
or validator change.

**Companion files:**
- [`./writer-prompt-tightening-notes-20260503.md`](./writer-prompt-tightening-notes-20260503.md) — v2 rule proposal (A–G).
- [`./writer-prompt-v2-first-capture-20260503.md`](./writer-prompt-v2-first-capture-20260503.md) — v2 capture package; Plan A's slot is the source of the v2 evidence below.
- [`./plan-to-story-four-profile-capture-20260501.md`](./plan-to-story-four-profile-capture-20260501.md) — v1 four-profile baseline.

---

## 1. Purpose

The first manual writer-prompt-**v2** capture (Plan A / age-4-simple
in Claude.app) confirmed that v2 fixes most of the per-turn issues
v1 surfaced — opener variety, choice format, and moralizing
dialogue all improved — **but exposed a new, more serious
issue**: v2 has no story-session boundary. The model keeps
producing new choices long after the small problem is solved, so
the session drifts into open-ended chat shape rather than the
short, focused fairy-tale shape Areg needs.

This note proposes **writer prompt v3**: keep every v2 rule, and
add a bounded-arc + stop-condition rule set so each Story-mode
session is a self-contained 3-turn arc that ends softly without a
trailing choice block.

This is **not** a production change request and **not** a runtime
provider decision. The deliverable is this file. A subsequent
slice may produce a v3 capture package (parallel to the v2 one)
once these rules settle.

---

## 2. Evidence

### Source

- The Plan A / age-4-simple #17 v2 capture in Claude.app (manual
  test, 2026-05-03). The capture slot is at
  [`./writer-prompt-v2-first-capture-20260503.md`](./writer-prompt-v2-first-capture-20260503.md)
  § 6A and the rule set being tested is at
  [`./writer-prompt-tightening-notes-20260503.md`](./writer-prompt-tightening-notes-20260503.md)
  § 3 (rules A–G).

### What v2 fixed (compared to v1)

- **Opener.** v2 did **not** start with `Մի անգամ` /
  `Մի անգամ, շատ վաղուց`. Rule A held.
- **Choice format.** Many turns landed `Ա: ` / `Բ: ` exactly as
  rule B specifies (vs v1's drift into `Ա)`, `🌿`, `Ա․`, `Ա.`).
- **Armenian quality** generally good — natural age-4 spoken
  register, warm tatik-narrator framing, no translated-feel
  phrasing.
- **Warmth for age 4** mostly good.

### What v2 did **not** fix

- **Unbounded continuation.** The story kept producing new
  choices across many turns (≥ 5 captured continuations with no
  natural stop). After the main arc — arrival → inspection →
  small gift → arագիլ flying home — landed cleanly, the writer
  kept extending: peach offering → child sleeping → dream
  sequence → tatik return → hug → peach-sharing → **still**
  followed by a fresh choice block.
- **No bounded ending / stop condition.** No turn was a
  "closing turn" without a choice block. The story never
  signalled "this is the end" — it always invited one more
  choice.
- **Claude.app duplicated opening sentence-pair artefact still
  appears** on every continuation. Unchanged from v1; treated
  as Claude.app UI-side rendering bug pending API confirmation
  (per `writer-prompt-tightening-notes-20260503.md` § 3 rule F
  / § 5).

### Net assessment

v2 is **better per-turn** than v1 (rules A / B / C / E hold
well in the sampled output). v2 is **worse end-to-end** than
v1, because v1 captures were short by hand-stop convention and
v2 captures expose what happens when the writer is asked to
keep going: the model has no model of "this story ends
here." v3's job is to give it that model.

---

## 3. Problem statement

For a *spoken toy* like Areg, an unbounded story-mode session
fails on multiple axes simultaneously:

1. **Child attention.** A 4-year-old listener has a working
   attention budget of roughly one fairy-tale arc per session.
   Past that, the listener disengages and the session stops
   being a story experience at all.
2. **Session length predictability.** Parents need to be able
   to estimate "this is a short story before bed" vs "this is
   open-ended play." An unbounded story collapses both modes
   into one and removes that signal.
3. **TTS cost and latency.** Every additional turn is another
   STT input and another TTS render. A 12-turn unbounded story
   is ~4× the cost of a 3-turn bounded one with no quality
   improvement past turn 3 — usually a quality *regression*,
   because the writer drifts into recap and ornament once the
   plan's smallProblem is solved.
4. **Story arc focus.** The Story Director architecture's
   plan-then-render pipeline is built to ground a story on
   *one* small problem and *one* resolution. An unbounded
   continuation has no plan beyond turn 1 — every subsequent
   turn is improvised, which defeats the determinism the plan
   layer is meant to deliver.
5. **Audit and summarization.** The parent dashboard surfaces
   per-conversation summaries and per-day counts. A bounded
   story has a clean "1 conversation" semantic; an unbounded
   session is a single multi-turn conversation that
   semantically contains many sub-stories, which is harder
   to label and review.
6. **Mode boundary clarity.** Areg's safety posture relies on
   the five modes (Story / Game / Riddle / Curiosity / Calm)
   being distinct. Story without a closure rule eventually
   becomes free-form chat-shape — exactly the posture
   `MODES.md` says Areg must never have.

The fix is to make the writer prompt itself enforce the arc.
The rest of this note is that fix.

---

## 4. Proposed v3 rule: bounded story arc

A Story-mode session is a **fixed-shape 3-turn arc** by
default. The writer is told the turn index, the selected child
choice (for turns 2 and 3), and the maximum turn count, so its
output shape is deterministic per turn:

### Turn 1 — opening

- Introduce the **scene**, **hero**, **smallProblem**, and
  **magicalObject** from the plan.
- Honour all v2 rules (no `Մի անգամ` opener; no moralizing;
  age-profile pacing; etc.).
- **End with exactly two choices** in `Ա: ` / `Բ: ` format.
- Do **not** resolve the small problem in turn 1.

### Turn 2 — middle

- The **first sentence** directly performs the child's
  selected choice (rule F from v2).
- Move the story toward solving `smallProblem` — the chosen
  action either resolves it OR sets up turn 3's resolution.
- **End with exactly two choices only if the small problem
  is not yet resolved.** If turn 2's chosen action *did*
  resolve the problem, treat the turn as closure (skip the
  choice block, follow the closure rule § 5 below).
- Default expectation is *not yet resolved* — this lets the
  child experience two choices and gives turn 3 something
  to land.

### Turn 3 — closure

- The **first sentence** directly performs the child's
  selected turn-2 choice.
- **Resolve `smallProblem`** within this turn.
- Add warm closure: a soft sensory beat, a small
  acknowledgement, a quiet tail. The plan's
  `resolutionStyle` shapes the resolution's flavour.
- **No choice block.** Turn 3 ends in narration, not a
  question.
- Optionally end with the formula `Վերջ։` on its own line,
  or with a natural last sentence and no formula. Either
  is fine; the rule is "no `Ա:` / `Բ:` after this turn."

The 3-turn budget is the **default**. A future slice may
introduce a generator-side `--max-turns N` flag if research
shows certain plans want 4 turns; v3 wires the value in via
a `{{MAX_TURNS}}` placeholder so the prompt stays generic.

---

## 5. Closure rule

The writer must treat any of these as **closure** and **not**
emit another choice block:

- The plan's `smallProblem` is solved (the resolution beat the
  plan was designed around has landed).
- The hero is **sleeping, resting, or peaceful** at the end of
  the turn.
- A **gift-sharing, hug, dream, homecoming, or goodbye** beat
  has just happened.
- The current turn index equals `{{MAX_TURNS}}` regardless of
  state — closure is compulsory at the budget edge even if
  the writer feels the arc could go on.

Closure ends **softly**: no "and then?" hook, no cliffhanger, no
"Areg-asks-the-child-something" question. The last sentence
either narrates the hero settling, or the world quieting, or
ends with a single `Վերջ։` line.

The closure rule **overrides** the per-turn choice-block
requirement from v2 rule B. Rule B's *exact format* still
applies on turns 1 and 2; rule B does not apply on the closing
turn because there *is* no choice block on the closing turn.

---

## 6. Optional continuation escape hatch

If the child explicitly says `շարունակիր` (or a close synonym:
`մի ուրիշ պատմություն`, `ևս մի հեքիաթ`, `արի շարունակենք`)
*after* a turn that closed the story per § 5, treat that as a
**new mini-story request**, not as continuation of the closed
arc.

- The new mini-story uses a **fresh plan** (the generator
  produces another plan; the same hero / friend may continue
  if the plan happens to draw them, but that is coincidence,
  not enforced continuity).
- The new mini-story is **its own 3-turn bounded arc** under
  the same v3 rules.
- Areg does **not** infer "continue" from the child's choice
  block on turn 2 — the choice block is already part of the
  open arc; only an *explicit* `շարունակիր`-shaped request
  starts a fresh arc.

This is **not** an open-ended continuation. There is no rule
that says "Areg keeps going as long as the child asks." A
parent-tunable session-level cap (e.g. "max 3 mini-stories
per session") is a future slice and out of scope for v3
prompt design.

---

## 7. Choice budget rule

Reinforces § 4 + § 5 from the writer's point of view, mostly
for compactness in the prompt:

- **Max 2 choice-bearing turns** per mini-story (turn 1, turn 2).
- **Final turn has no choices.**
- **Never offer "ask for another story"** or "walk around
  more" or "what should we do now?" as a default choice unless
  the parent/session explicitly allows open-ended mode (out of
  scope for v3 prompt; would be a parent-flag concept).
- Choices on turn 1 / turn 2 must always be **plot-grounded**
  (place / magicalObject / smallProblem-related), per v2
  rule G + the existing `validate-story-plan.js` choice
  grounding check.

---

## 8. Writer prompt v3 draft block

Eastern Armenian instructions, same posture as v2's prompt,
with three new placeholders:

- `{{PLAN_JSON}}` — verbatim plan JSON.
- `{{AGE_PROFILE_RULES}}` — pre-rendered per-profile pacing +
  register block (same as v2).
- `{{TURN_INDEX}}` — integer `1`, `2`, or `3` (or however high
  `{{MAX_TURNS}}` allows).
- `{{SELECTED_CHOICE}}` — for turn ≥ 2 only: the literal
  string `Ա` or `Բ` and the chosen-choice phrase pulled from
  the plan (`plan.choiceA` or `plan.choiceB`). On turn 1 this
  placeholder renders to the literal string `(none — opening turn)`.
- `{{MAX_TURNS}}` — integer `3` by default. The prompt
  references it so a future `--max-turns 4` knob does not
  require prompt rewrites.

```text
Դու Արեգն ես՝ տաք, հայալեզու հեքիաթասաց 4–7 տարեկան երեխաների համար։
Ստանալու ես STORY PLAN՝ JSON տեսքով, քայլի համարը (TURN_INDEX) և
երեխայի ընտրած գործողությունը (SELECTED_CHOICE)։ Քո խնդիրն է գրել
ՄԵԿ կարճ արևելահայերեն հեքիաթային քայլ, որը երեխան լսելու է հենց հիմա։

ԲԱՑԱՐՁԱԿ ԼԵԶՎԱԿԱՆ ԿԱՆՈՆ
- Պատասխանիր ՄԻԱՅՆ արևելահայերենով, հայկական տառերով։
- Ոչ տառադարձություն, ոչ անգլերեն, ոչ ռուսերեն։
- Բնական, սահուն, խոսակցական արևելահայերեն, ինչպես տաք հայ
  տատիկը պատմում է հին հեքիաթ իր փոքրիկ թոռնիկին։
- Ոչ թարգմանված հայերեն, ոչ գրքային, ոչ արհեստական։

ԲԱՑՄԱՆ ԿԱՆՈՆ (A — v2)
- Մի՛ սկսիր «Մի անգամ», «Մի անգամ, շատ վաղուց», «Մի գեղեցիկ օր»
  կամ «Մի գեղեցիկ առավոտ» տիպի կաղապարով։
- Բացիր ուղիղ տեսարանով՝ հիմնված plan-ի place-ի,
  sensoryDetails-ի և mood-ի վրա։

ԸՆՏՐՈՒԹՅՈՒՆՆԵՐԻ ՃՇԳՐԻՏ ՁԵՎԱՉԱՓ (B — v2)
- Երբ քայլը պետք է ավարտվի ընտրություններով (տես «ՍԱՀՄԱՆԱՓԱԿ ԱՐԿ»),
  ընտրությունների տողերը պետք է սկսվեն ՃՇՏՈՐԵՆ.
  Ա: ...
  Բ: ...
- Հայերեն Ա/Բ տառեր, հետո ASCII երկու վերջակետ, հետո մեկ բացակ։
- ԱՐԳԵԼՎՈՒՄ Է. emoji, թվեր, պատկերակներ, «Ա)», «Ա.», «Ա․», «Ա-»։
- Ընտրությունների տողերից ՀԵՏՈ՝ ոչ մի բացատրություն կամ արձակ։
- Ընտրությունների իմաստը պետք է ՊԱՀՊԱՆԻ plan-ի choiceA-ի և
  choiceB-ի գործողությունների իմաստը։

ՀԱԿԱ-ԲԱՐՈՅԱԽՈՍԱԿԱՆ ԿԱՆՈՆ (C — v2)
- ՉՎերջացնել քայլը ուղիղ բարոյական դասով։
- ՉԴնել ուղիղ բարոյական աֆորիզմներ որևէ կերպարի (հատկապես
  իմաստուն/մեծ կերպարի) խոսքի մեջ։
- Բարությունը, ընկերությունը, համբերությունը պետք է երևան
  ԳՈՐԾՈՂՈՒԹՅԱՄԲ, ոչ թե բացատրությամբ։

ՏԱՐԻՔԱՅԻՆ ՌԻԹՄ ԵՎ ԲԱՌԱՊԱՇԱՐ (D + E — v2)
{{AGE_PROFILE_RULES}}

ՇԱՐՈՒՆԱԿՈՒԹՅԱՆ ԿԱՆՈՆ (F — v2)
- Քայլ 2-ի և քայլ 3-ի ԱՌԱՋԻՆ նախադասությունը պետք է ՈՒՂԻՂ
  կատարի երեխայի ընտրած գործողությունը (SELECTED_CHOICE)։
- ՉԿրկնել նախորդ քայլի ամփոփումը։
- Ամեն նախադասությունը գրվում է ՃՇՏՈՐԵՆ մեկ անգամ։

PLAN ADHERENCE (G — v2)
- ՊԱՀՊԱՆԻ plan-ի՝ hero, friendOrGuide, place, magicalObject,
  smallProblem, goal, mood-ը։ Կարող ես հղկել բառերը, բայց ՉՓՈԽԵՍ
  որևէ հիմնական ատոմը։

ՍԱՀՄԱՆԱՓԱԿ ԱՐԿ (NEW — v3 § 4)
- Ընդհանուր քայլերի առավելագույն թիվը MAX_TURNS = {{MAX_TURNS}}
  (լռելյայն 3)։
- ԸՆԹԱՑԻԿ ՔԱՅԼԸ՝ TURN_INDEX = {{TURN_INDEX}}.
- ԸՆՏՐՎԱԾ ԳՈՐԾՈՂՈՒԹՅՈՒՆ (քայլ 2-ից սկսած)՝ {{SELECTED_CHOICE}}.

  Քայլ 1 (TURN_INDEX = 1):
  - Ներկայացնել տեսարանը, հերոսին, plan.smallProblem-ը, plan.magicalObject-ը։
  - ՉԼուծել smallProblem-ը այս քայլում։
  - Ավարտել ՃՇՏՈՐԵՆ երկու ընտրությամբ՝ Ա: / Բ: ձևաչափով։

  Քայլ 2 (TURN_INDEX = 2):
  - Առաջին նախադասությամբ ՈՒՂԻՂ կատարել SELECTED_CHOICE-ը։
  - Շարժվել smallProblem-ի լուծման ուղղությամբ։
  - Եթե smallProblem-ը դեռ ՉԻ լուծվել՝ ավարտել երկու ընտրությամբ։
    Եթե smallProblem-ը արդեն լուծվել է այս քայլում՝ կիրառել
    «ՓԱԿՄԱՆ ԿԱՆՈՆ» (ստորև) և չավելացնել ընտրություններ։

  Քայլ 3 (TURN_INDEX = 3) — ՓԱԿՈՒՄ:
  - Առաջին նախադասությամբ ՈՒՂԻՂ կատարել SELECTED_CHOICE-ը։
  - ԼՈՒԾԵԼ smallProblem-ը այս քայլում։
  - Ավելացնել տաք, փակիչ վերջ՝ plan.resolutionStyle-ի ոճով։
  - ՉԱՎԵԼԱՑՆԵԼ ընտրություններ։
  - Կարող ես ավարտել «Վերջ։» տողով կամ բնական վերջին նախադասությամբ։

ՓԱԿՄԱՆ ԿԱՆՈՆ (NEW — v3 § 5)
Հետևյալ պայմաններից ՈՐԵՎԷ ՄԵԿԸ բավարար է, որպեսզի այս քայլը
դիտվի որպես ՓԱԿՈՒՄ՝ առանց նոր ընտրությունների.
- plan.smallProblem-ը լուծված է։
- հերոսը քնում/հանգստանում է/խաղաղ է։
- քայլում տեղի է ունեցել նվեր-տալ, գրկախառնում, երազ, տուն-
  վերադարձ, կամ բարի-մնա մոմենտ։
- TURN_INDEX == MAX_TURNS (անպայման փակում, անկախ վիճակից)։

ՓԱԿՈՒՄԸ լինում է ՄԵՂՄ. ոչ «և հետո...», ոչ ցատկող-հարց,
ոչ կախարդական մնացորդ։ Վերջին նախադասությունը կամ պատմողական է,
կամ կազմված է մեկ «Վերջ։» տողից։

ԸՆՏՐՈՒԹՅՈՒՆՆԵՐԻ ԲՅՈՒՋԵ (NEW — v3 § 7)
- ԱՌԱՎԵԼԱԳՈՒՅՆ 2 քայլ ընտրություններով (քայլ 1 և քայլ 2)։
- Վերջին քայլը (քայլ 3 կամ ավելի վաղ՝ եթե ՓԱԿՈՒՄ է առաջացել)՝
  ԱՌԱՆՑ ընտրությունների։
- ՄԻ՛ առաջարկիր «եկ ուրիշ տեղ գնանք», «եկ ուրիշ պատմություն»,
  «ի՞նչ անենք հիմա» տիպի ընտրություններ որպես լռելյայն։

ԱՆՎՏԱՆԳՈՒԹՅՈՒՆ ԵՎ ՏՈՆ
- Ոչ բռնություն, ոչ զենք, ոչ սարսափ, ոչ վախենալու վտանգ, ոչ մահ,
  ոչ լքվածություն, ոչ բժշկական/վախենալու հիվանդություն։
- Ոչ մանկական/բալիկային խոսք. երեխան 4–7 է, ոչ թե 2։
- Ոչ թարգմանված հայերեն։

ԵԼՔԻ ՁԵՎԱՉԱՓ
- Քայլ 1 և քայլ 2 (չի-փակում)՝
  1. Հեքիաթի մարմինը (հայերեն արձակ)։
  2. Մեկ դատարկ տող։
  3. Ընտրությունները՝ «Ա: », «Բ: » նախածանցներով։
- Քայլ 3 կամ ՈՐԵՎԷ ՓԱԿՄԱՆ քայլ՝ ՄԻԱՅՆ հեքիաթի մարմինը (հնարավոր է
  «Վերջ։» տողով)։ Ոչ մի ընտրություն։

ՉՈՒՆԵՆԱԼ ելքում.
- Plan-ի JSON-ը։
- Անգլերեն։
- Markdown վերնագրեր, code fence-եր կամ bullet-ներ։
- Բացատրություն, footer, «Note:» տող։
- «As an AI…» կամ որևէ meta-մեկնաբանություն։

STORY PLAN:
{{PLAN_JSON}}

TURN_INDEX: {{TURN_INDEX}}
SELECTED_CHOICE: {{SELECTED_CHOICE}}
MAX_TURNS: {{MAX_TURNS}}
```

Notes on the prompt:

- The prompt is **per-turn**; the capture flow runs it three
  times against the same plan with `TURN_INDEX = 1, 2, 3` and
  the matching `SELECTED_CHOICE`. The prompt does not ask the
  model to manage state across turns — that is the operator's
  job.
- `SELECTED_CHOICE` for turn 1 is rendered as `(none — opening
  turn)` so the placeholder is never empty in the prompt body
  (an empty placeholder reads as a prompt bug to the model).
- The `ՇԱՐՈՒՆԱԿՈՒԹՅԱՆ ԿԱՆՈՆ` block was tightened to scope rule
  F to turns 2 and 3 explicitly; v2's wording said "every
  continuation," which is technically the same set but less
  unambiguous to the model.

---

## 9. Recommended next test

Run a single Claude.app **v3** capture against Plan A /
age-4-simple #17 (the same plan v2 was tested on). Capture
shape:

1. **Turn 1** with `TURN_INDEX = 1`,
   `SELECTED_CHOICE = (none — opening turn)`,
   `MAX_TURNS = 3`.
2. Child picks `Ա` (the inspection-template choice — same
   choice the v1 capture exercised first).
3. **Turn 2** with `TURN_INDEX = 2`,
   `SELECTED_CHOICE = Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին`.
4. Child picks `Բ` (the alternate-arc choice — exercises both
   sides).
5. **Turn 3** with `TURN_INDEX = 3`,
   `SELECTED_CHOICE = Բ: <whatever turn-2 emitted as Բ>`.

Acceptance criteria:

- Turn 1 ends with `Ա: ` / `Բ: ` choice block.
- Turn 2 ends with `Ա: ` / `Բ: ` choice block (problem not yet
  resolved is the default).
- **Turn 3 has NO choice block.** This is the load-bearing v3
  check.
- Turn 3 either ends in a natural last sentence or in a `Վերջ։`
  line.
- After turn 3 the child saying "ևս մի հեքիաթ" / "շարունակիր"
  starts a fresh mini-story (not relevant for the first
  capture, but worth confirming as a follow-up).
- v2's per-turn rules (A / B / C / D / E / F / G) all still
  hold.

If turn 3 emits a choice block anyway, the rule wording in §
8's `ՍԱՀՄԱՆԱՓԱԿ ԱՐԿ` block needs hardening — likely toward
"the model is FORBIDDEN from emitting `Ա:` or `Բ:` on the
closing turn" with an explicit format example showing
narrative-only output.

A second test on Plan D / age-7-richer #6 follows once Plan A
clears, mirroring the v2 capture-package strategy.

---

## 10. Decision

Recommendation:

1. **Stop running more app captures with v2.** v2's
   per-turn quality is already understood; further v2 captures
   would only re-confirm the unbounded-continuation problem at
   capture cost.
2. **Move to v3 manual capture** with the prompt block in § 8
   and the test plan in § 9.
3. **No production / runtime changes yet.** ChatService,
   `system-prompt.txt`, and the production model selection
   stay as they are. v3 lives in the StoryModelBakeoff capture
   flow only.
4. **API comparison still load-bearing later.** The
   duplicate-sentence-pair artefact is still unresolved (v2
   showed it; v3 does nothing to address it because the
   hypothesis is UI-side). The next slice after a clean v3
   manual capture is an API run of the same v3 prompt against
   the same two plans (Plan A + Plan D).
5. **Generator does NOT need a `--max-turns` flag yet.** The
   `{{MAX_TURNS}}` placeholder is filled at capture time
   (operator types `3` into the prompt). If experimentation
   shows certain plans want 4 or 5 turns, a future slice can
   wire the flag into `generate-story-plan.js` and the
   capture pipeline; today it is one number in the prompt.

The v3 capture-package slice (parallel to
`writer-prompt-v2-first-capture-20260503.md`) is the natural
next step but is **not** scheduled by this note.

---

## 11. Out of scope for this note

- No edits to `tools/StoryModelBakeoff/system-prompt.txt`.
- No edits to `tools/StoryModelBakeoff/bakeoff-prompts.json`.
- No edits to `tools/StoryModelBakeoff/story-seed-bank.v1.json`.
- No edits to `tools/StoryModelBakeoff/story-character-names.v1.json`.
- No edits to `generate-story-plan.js`, `validate-story-plan.js`,
  `validate-seed-bank.js`, or `validate-character-names.js`.
- No production runtime changes (`backend/**`).
- No new provider integration, API call, or live model run.
- No `--max-turns` CLI flag (would be a future generator
  slice if research shows it's needed).
- No multi-mini-story session cap design — that is a
  parent-flag concern and a separate slice from this writer-
  prompt rule set.
- No change to the `validate-story-plan.js` choice-bearing-
  turn invariants — the validator does not score per-turn
  rendered output, only the plan; v3's bounded-arc rule is a
  *prompt-time* contract, not a *plan-time* one.

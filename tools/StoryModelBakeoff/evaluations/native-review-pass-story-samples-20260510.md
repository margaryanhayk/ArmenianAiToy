# Native-review pass — applying the checklist to the five story-brain samples — 2026-05-10

**Status:** consolidated review only. No code change, no paid API
call, no backend run, no Claude API use, no production change,
no ChatService touch, no provider switch implied or authorized
by this document. Applies the freshly-pushed native review
checklist to the five evidence samples already in the matrix,
re-stating per-sample findings in a single comparable shape so a
future native-Armenian reviewer or a production-integration
design doc has one place to start from.

**Grounding rule.** Every finding below is grounded in the cited
evaluator note for that sample. This pass does not introduce new
raw-output claims; it consolidates and re-classifies under the
checklist's vocabulary. A fresh full native pass by a native
Eastern-Armenian reviewer remains a separate slice and is
explicitly listed in § 7 below.

**Source files:**
- Checklist (commit `e254e9d`):
  `tools/StoryModelBakeoff/evaluations/native-armenian-story-review-checklist-20260510.md`
- Claude manual hedgehog rubric (commit `16537e5`):
  `tools/StoryModelBakeoff/evaluations/claude-app-manual-sample-aregb-rubric-20260510.md`
- OpenAI v3.2.3 mp1 PA evidence (commit `e73975b`):
  `tools/StoryModelBakeoff/evaluations/openai-v3-2-3-smoke-mp1-20260510.md`
- Claude.app Plan A controlled result (commit `d80318d`):
  `tools/StoryModelBakeoff/evaluations/claude-app-plan-a-controlled-capture-result-20260510.md`
- Claude.app Plan D controlled result (commit `471bbf6`):
  `tools/StoryModelBakeoff/evaluations/claude-app-plan-d-controlled-capture-result-20260510.md`
- Claude.app Home/Play controlled result (commit `8c944e5`):
  `tools/StoryModelBakeoff/evaluations/claude-app-home-play-controlled-capture-result-20260510.md`
- Story-brain findings summary (commit `db9292f`):
  `tools/StoryModelBakeoff/evaluations/story-brain-findings-summary-20260510.md`

**Filename date** uses local Yerevan `2026-05-10` for batch
consistency.

---

## 1. Sample 1 — Claude manual hedgehog

- **Source:**
  `tools/StoryModelBakeoff/evaluations/claude-app-manual-sample-aregb-rubric-20260510.md`
- **Scenario:** Forest / hedgehog (Փնջիկ) / golden-leaf / lake →
  small frog (Կլկլիկ) with a silver bell that has lost its
  voice. Two-turn capture only; choice block is consumer-app
  shaped (emoji + `Հիմա ի՞նչ անի…`), not the Areg 9-label
  envelope.
- **Capture context:** consumer-app under Claude's own default
  system prompt and decoding — *not* the Areg system prompt,
  *not* a controlled scenario brief, *not* routed through the
  bake-off runner.

- **Review verdict (per § 2 of the checklist):**
  **PASS WITH SMALL EDITS**.
  Pass on Armenian naturalness, fairy-tale feeling, warmth,
  continuation coherence, safety, choice physicality. Small
  edits needed before this could be a *production* sample —
  length must be brought under the 3–5-sentence-per-turn cap;
  the choice block must be re-shaped to the Areg parser
  envelope; the emoji prefixes have to be stripped.

- **Say-aloud decision:**
  **YES, with slight length control** (matches the original
  rubric line). The native-ear quality is the highest of any
  sample in this set; the only blockers to "say aloud as-is"
  are pacing and parser-format, not Armenian quality.

- **Top 3 Armenian / native-ear issues:**
  1. **Spoken pacing.** Both turns run well past Areg's
     `3–5 short sentences` production directive — too long for
     a 4-year-old's attention span and the toy's spoken
     budget. *Tag: `pacing`.*
  2. **Slightly literary / book-page register.** Lines like
     `որտեղից լսվում էր ինչ-որ մեկի մեղմ-մեղմ երգը` read
     more "page-of-a-book" than "told around a quiet
     bedside." Some vocabulary leans older (`ստորոտում`,
     `ադամանդների պես շողշողուն`, `ճյուղերի արանքից`).
     *Tag: `register/style`.*
  3. **Choice block format is non-parser-compatible.**
     `Հիմա ի՞նչ անի Փնջիկը.` with emoji-prefixed Armenian
     options (`🌿 lake` / `🌟 oak` / `💧 dew drop` / `🍂 leaf`)
     does not match the Areg tail-block parser shape
     (`---\nCHOICE_A:\n…\nCHOICE_B:\n…`). *Tag: `parser/format`.*

- **Top 3 strengths:**
  1. **Premium fairy-tale ceiling.** Opening
     *"Բարձր լեռների ստորոտում, որտեղ առվակը զրուցում էր
     քարերի հետ"* lands classical storyteller register on
     the first line. Personifying the brook talking to the
     stones is the kind of imagery Areg's persona wants.
  2. **Concrete sensory imagery.** Golden leaf with dew like
     diamonds; dry leaves rustling underfoot; the smell of
     forest strawberries and wet moss; a small silver bell on
     a white stone. 1–2 vivid, child-graspable details per
     turn.
  3. **Choices are physical and meaningful.** Both turns end
     with two concrete physical actions ("go down to the
     lake" / "climb to the oak"; "drip dew into the bell" /
     "wrap the leaf around it"). Not opinion polling, not
     metacognition.

- **Issue classification:**
  `pacing`, `register/style`, `parser/format`. *No* `morphology`
  or `semantic/anatomy` or `story coherence` issues flagged in
  the source rubric.

- **Recommended action:**
  Keep as the *ceiling signal* for what an Anthropic-tier
  model can sound like in Armenian fairy-tale register, but do
  **not** derive runtime decisions from it — the capture is
  uncontrolled and consumer-app, not API-under-Areg-prompt.

---

## 2. Sample 2 — OpenAI v3.2.3 mp1 PA

- **Source:**
  `tools/StoryModelBakeoff/evaluations/openai-v3-2-3-smoke-mp1-20260510.md`
- **Scenario:** PA — puppy (`Բուռ`) / apple orchard
  (`Խնձորենու այգի`) / dew-drop golden leaf / stork. Three
  turns, A → A path (per the bake-off scenario). System prompt:
  `system-prompt-v3-2.txt` at commit `919dee5` (v3.2.3 R2 +
  opener tightening). Provider / model: `openai` / `gpt-4o`.
  Run via the StoryModelBakeoff runner; one paid OpenAI API
  call.

- **Review verdict:**
  **BORDERLINE**.
  Hard tally 9/10 PASS on the structural rules — best
  envelope of any v3.2-era run. But four borderline / coined
  Armenian tokens spread across all three turns plus the
  recurring mid-paragraph `Մի օր,` opener slip means the
  spoken output would carry detectable "model-Armenian"
  texture to a native ear. Not safe to ship; not so broken
  that a v3.3 prompt slice can't address it.

- **Say-aloud decision:**
  **NO — borderline** (matches the original axis line).

- **Top 3 Armenian / native-ear issues:**
  1. **Four borderline / coined morphology tokens across three
     turns** — `բոցերում էր` (T1; standard is `բոցավառվել`),
     `փայլացնում էին` (T1; standard causative is
     `փայլեցնում`), `ցուցանի` (T2; `ցուցան` is not standard
     Eastern Armenian), `անթել` (T3; standard for "ember /
     glow" is `անթեղ`). Verb-from-noun, alt-causative, and
     coined / mis-selected nouns. R2's named-exemplar
     defense catches the previously-named families but not
     this next layer. *Tag: `morphology/fake Armenian`.*
  2. **`բնավ խումբախումբ խաղալու ժամանակ` is semantically
     incoherent.** `բնավ` means "never / at all" (a negation
     intensifier); `խումբախումբ` means "in groups." A single
     dog playing "in groups, ever" is not a meaningful
     Armenian phrase. *Tag: `semantic/anatomy`* (verb-noun
     semantic-fit class, not body-part class).
  3. **Mid-paragraph `Մի օր,` opener slip in T1 sentence 6.**
     `Մի օր, բնավ խումբախումբ խաղալու ժամանակ, շնիկը լսեց…`
     — the exact pattern v3.2.3's opener rule was meant to
     suppress. First-sentence guard works; mid-paragraph
     guard does not on this sample. *Tag: `register/style`.*

- **Top 3 strengths:**
  1. **Best-yet structural envelope.** Hard-tally 9/10; T3
     ≈ 90 words in 70–100 (mid-band, +20 above floor, second
     consecutive mid-band landing); all turns in target
     word-band for the first time across v3.2-era runs.
  2. **Choice / closure / place-stem all clean.** Both choice
     blocks reproduced byte-for-byte; T3 closes on `Վերջ։`
     on its own line; place-stem `Խնձորենու այգում` preserved
     across all T1 mentions.
  3. **No English / Latin / meta leakage; safety PASS.** Zero
     English words, zero narrator brackets, zero meta
     commentary; age-appropriate throughout.

- **Issue classification:**
  `morphology/fake Armenian`, `semantic/anatomy` (verb-noun fit
  class), `register/style` (opener slip). *No* `pacing` or
  `parser/format` issues; `story coherence` is mostly clean
  with a small T2-dog-speaks-wisdom → T3-dog-passively-stands
  character-role discontinuity noted in the source file.

- **Recommended action:**
  Do **not** burn another paid mp1 / mp2 on a v3.2.x
  exemplar-list extension — the named-exemplar approach has
  hit a clear ceiling on the morphology axis. A future v3.3
  design needs to shift from *"list bad tokens"* to a
  *structural rule* ("only use participles / verbs / nouns
  whose 3rd-person past form you can name aloud first") and
  probably a *positive whitelist* of safe stems. Document
  first; spend later.

---

## 3. Sample 3 — Claude.app Plan A controlled

- **Source:**
  `tools/StoryModelBakeoff/evaluations/claude-app-plan-a-controlled-capture-result-20260510.md`
- **Scenario:** PA matched to the OpenAI PA bake-off run —
  same hero (puppy `Բուռ`), same friend/guide (old dog
  `Ղազար`), same place (apple orchard), same magical object
  (golden dew-drop leaf), same small problem (stork seeking
  rest). Three turns. **Fixed path A → B** (corrected from
  the earlier A → A draft of the capture prompt).
- **Capture context:** controlled prompt (Areg persona, PA
  scenario brief, fixed A → B, 9-label format) pasted into a
  fresh Claude.app chat; non-interactive single-shot;
  consumer-app, **not** API.

- **Review verdict:**
  **PASS WITH SMALL EDITS**.
  Format envelope perfect; morphology clean; safe and warm;
  age-appropriate. Three small register-side cleanups
  (generic opener, animal-sound mismatch, lesson-style
  closer) would make it production-shaped. Native reviewer
  pass still pending.

- **Say-aloud decision:**
  **YES, but not ideal** (matches the original rubric line).

- **Top 3 Armenian / native-ear issues:**
  1. **Generic `Մի անգամ…` opener on T1 sentence 1.** Exactly
     the opener pattern Areg's style is moving *away* from
     (it's the fairy-tale equivalent of "Once upon a time").
     The capture prompt did not explicitly forbid it — a
     weakness of the prompt as written, not necessarily of
     Claude. *Tag: `register/style`.*
  2. **`Բուռը բարձրաձայն կաչկաչ է անում` is semantically
     awkward for a dog.** `կաչկաչ` ("squawking / chirping")
     fits a bird, not a dog. A native ear flags this as
     off-register. Not a morphology fabrication; an
     animal-sound idiomatic mismatch. *Tag: `semantic/anatomy`*
     (animal-action class).
  3. **Final moral line drifts toward a lesson.**
     `«Տեսա՞ր, Բուռ, երբ բարությամբ ես դիպչում, ամեն ինչ
     տեղն է ընկնում»` reads as an explicit moral statement
     — closer to a teacher's wrap-up than Areg's preferred
     "warm storyteller who trusts the listener to feel the
     meaning" register. Borderline, not a hard fail.
     *Tag: `register/style`* (moralizing-closer class).

- **Top 3 strengths:**
  1. **Excellent format compliance.** All 9 labeled fields
     produced in order, each on its own labeled line. No
     prefatory text, no trailing meta, no markdown bolding,
     no emoji. Parser-friendly out of the box.
  2. **Correct fixed path A → B with direct execution.** T2
     first sentence directly executes T1 `CHOICE_A`; T3
     first sentence directly executes T2 `CHOICE_B`. No
     drift, no implicit pivot.
  3. **No obvious fake Armenian morphology.** Best-effort
     scan surfaced no clearly fabricated stems or coined
     participles — a meaningful contrast with the OpenAI
     v3.2.3 mp1 PA run's four borderline tokens on the same
     scenario family.

- **Issue classification:**
  `register/style` (×2: opener pattern + moralizing closer),
  `semantic/anatomy` (animal-sound mismatch). *No*
  `morphology/fake Armenian`, `pacing`, `parser/format`, or
  `story coherence` issues flagged in the source rubric.

- **Recommended action:**
  Tighten any future controlled Claude capture prompt to
  explicitly forbid `Մի անգամ` / `Մի օր` / `Կար ու չկար`
  openers and to add a "no aphorism / no lesson-style closer"
  rule. Already partially present in the Home/Play capture
  prompt; back-port to PA / PD when those captures are
  re-run. No paid run needed yet.

---

## 4. Sample 4 — Claude.app Plan D controlled

- **Source:**
  `tools/StoryModelBakeoff/evaluations/claude-app-plan-d-controlled-capture-result-20260510.md`
- **Scenario:** PD — lizard hero (`մողես`) / frightened
  duckling friend (`բադիկ`) / old bridge (`Հին կամուրջ`) /
  sleeping key / snow-not-melting small problem / patience-
  style resolution / gently-wintery mood. Three turns. Fixed
  path A → B.
- **Capture context:** controlled prompt (Areg persona, PD
  scenario brief, A → B, schwa-not-drop + no-aphorism PD care
  notes, 9-label format) pasted into a fresh Claude.app chat;
  non-interactive single-shot; consumer-app, **not** API.

- **Review verdict:**
  **BORDERLINE**.
  Strong atmospheric ceiling on the fairy-tale-feeling axis
  and a clean schwa-not-drop on `Հին կամուրջի` (defeated the
  OpenAI side at v3.2.1 mp2). But two animal-anatomy
  mismatches, one off-register water-sound verb, an
  ungrammatical middle clause, and a T3 that drifts past the
  age-4–7 register band combine into "close, but not safe to
  ship."

- **Say-aloud decision:**
  **NO, but close** (matches the original rubric line).

- **Top 3 Armenian / native-ear issues:**
  1. **Animal-anatomy mismatches.** Duckling described as
     `ականջները ճկված` — ducks do not have visible external
     ears; this reads anatomically off to a native ear that
     visualizes the scene. Lizard offered `թև` in T1
     CHOICE_B (`Մողեսը թևը դնի բադիկի թևի վրա…`) — `թև`
     means "wing / arm"; lizards have `ոտք` / `թաթ`, not
     wings. The single most decision-relevant nature error
     in the matrix. *Tag: `semantic/anatomy`* (body-part class).
  2. **`կռկռում է` for a stream / water voice.** `կռկռալ`
     is a bird / duck / chicken sound, not water; the
     intended sense is closer to `գլգլալ` / `քրքրալ` /
     `շշնջալ` for a small awakening stream. *Tag:
     `semantic/anatomy`* (animal-action / verb-noun
     semantic-fit class).
  3. **Ungrammatical middle clause:** `Բանալին, ոչ ոք չէ
     նայել, պտտվեց մի կես պտույտ…`. The parenthetical `ոչ ոք
     չէ նայել` is not grammatical Eastern Armenian (should
     be `ոչ ոք չէր նայում` or `առանց որ որևէ մեկը նայեր`);
     the construction reads like an attempted translation
     of a literary English sentence rather than native
     Armenian. *Tag: `story coherence`* (grammar/structure
     within the narrative).

- **Top 3 strengths:**
  1. **Strong winter fairy-tale mood.** Opening line
     `Հին կամուրջի տակ, որտեղ ձյունը դրել էր իր սպիտակ
     վերմակը` lands the gently-wintery register on the first
     sentence; the atmospheric ceiling is the highest of the
     three controlled Claude captures.
  2. **Schwa-not-drop on `Հին կամուրջի` PASS.** The PD R3
     care note that defeated OpenAI at v3.2.1 mp2 is honored
     cleanly — `հին կամուրջ` letters preserved, no `Կամրջի…`
     drop.
  3. **Cleanly avoids the patience-aphorism trap.** The
     resolution comes through the characters' silent action
     and inaction (`«Հետո — ոչ ոք ոչինչ չանեց, ոչ ոք ոչ մի
     բառ չասաց»`) rather than through an explicit aphorism
     like `«համբերությունը հաղթում է»`. Showing patience,
     not naming it.

- **Issue classification:**
  `semantic/anatomy` (×2: body-part + animal-action), `story
  coherence` (ungrammatical middle clause), with a secondary
  `register/style` note on the T3 over-literary register
  (`որովհետև ջրի ձայնն ինքն էր ասում ամեն ինչ`) and a borderline
  `pacing` / TTS-handling concern on the `տտ, տտ, տտ` sound
  effect. *No* `morphology/fake Armenian` or `parser/format`
  issues.

- **Recommended action:**
  In any future Claude capture prompt, add explicit per-
  scenario animal-anatomy reminders (ducks have `կտուց` /
  `թևիկներ`, not external ears; lizards have `ոտք` / `թաթ`,
  not `թև`; water doesn't `կռկռում`) and tighten the literary-
  density allowance for T3 closers. No paid run needed yet.

---

## 5. Sample 5 — Claude.app Home/Play controlled

- **Source:**
  `tools/StoryModelBakeoff/evaluations/claude-app-home-play-controlled-capture-result-20260510.md`
- **Scenario:** PE — Նարե (≈ 5 y.o.) in her bedroom in the
  evening / doll Մոմո (sleepy, ready for bed) / missing little
  pillow (`Մոմոյի փոքրիկ բարձը կորել է`). Three turns. Fixed
  path A → B. Deliberately un-magical and deliberately
  familiar.
- **Capture context:** controlled prompt (Areg persona, PE
  child-natural home/play scenario brief, A → B, 9-label
  format, anti-magical + anti-poetic register rules) pasted
  into a fresh Claude.app chat; non-interactive single-shot;
  consumer-app, **not** API.

- **Review verdict:**
  **PASS WITH SMALL EDITS**.
  Format envelope perfect; morphology clean; semantics clean
  (no animal hero by design); concrete physical choices; safe
  + warm. One minor T2 story-state slip is the only flagged
  issue. The cleanest practical say-aloud controlled sample
  in the matrix.

- **Say-aloud decision:**
  **YES** (matches the original rubric line). Subject to the
  outstanding native-Armenian review pass.

- **Top 3 Armenian / native-ear issues:**
  1. **T2 story-state contradiction.** `Նարեն ելավ ու Մոմոյին
     դրեց բարձի կողքին, ասես Մոմոն սպասում էր` — placing Մոմո
     "next to the pillow" is confusing because the pillow has
     **not** been found yet at this point. Likely intended
     sense is "next to the *spot* where the pillow should
     go" or "next to her own pillow." Not fake Armenian; a
     story-logic micro-glitch. *Tag: `story coherence`.*
  2. **Slightly plain.** Less memorable than the magical
     hedgehog sample — the deliberate tradeoff of pinning the
     register to *everyday* and stripping magical
     affordances. Worth naming as a register tradeoff, not a
     defect. *Tag: `register/style`* (intentional, low-severity).
  3. **No third decision-relevant issue surfaced in the
     source evaluator note.** The Home/Play capture is the
     only sample in this matrix where the operator rubric
     could not name a third top weakness. Worth noting in
     this consolidated pass — the gap between "two minor
     issues" and "three top issues" is part of what makes
     Home/Play the cleanest practical sample so far. *Tag:
     `n/a`.*

- **Top 3 strengths:**
  1. **Clean practical say-aloud everyday Armenian.**
     Tangible home vocabulary (`լամպ`, `մահճակալ`, `ծածկոց`,
     `դարակ`, `գորգ`, `բարձ`, `գիրք`, `փափուկ խաղալիքներ`)
     — visible, age-anchored, none of the literary density
     that PD slipped into.
  2. **Format compliance perfect; A → B path correct; safe +
     warm for ages 4–7.** All 9 labeled fields in order;
     direct execution of T1 `CHOICE_A` in T2 and of T2
     `CHOICE_B` in T3; no fear, no violence, no medical
     register; no moralizing closer (Մոմո settles without
     a `«տեսնում ե՞ս, Մոմո…»` lesson tail).
  3. **No fake Armenian; no animal-anatomy mismatch (no
     animal hero by design); concrete physical choices.**
     The PD failure-mode surface is structurally absent
     because the scenario avoids it; the PA failure-mode
     surface (opener + moralizing closer) is also absent
     because the prompt explicitly forbade those moves.

- **Issue classification:**
  `story coherence` (T2 pillow slip), `register/style`
  (intentional plain register tradeoff). *No*
  `morphology/fake Armenian`, `semantic/anatomy`, `pacing`,
  or `parser/format` issues flagged in the source evaluator
  note.

- **Recommended action:**
  Hand the raw block to a native Eastern-Armenian reviewer
  for the full § 1–9 checklist pass; consider an updated
  Home/Play capture prompt rule along the lines of *"if the
  missing object has not yet been found, do not describe the
  hero placing other things 'next to' it."* If the native
  reviewer confirms PASS, this becomes the strongest single
  controlled candidate to anchor any future production-
  integration design discussion — still as one row in the
  matrix, not as a decision input.

---

## 6. Overall conclusion

Restating the per-sample findings in one line each:

| # | Sample | Verdict | Say-aloud | Dominant issue tag |
|---|---|---|---|---|
| 1 | Claude manual hedgehog (uncontrolled) | PASS WITH SMALL EDITS | YES, with length control | `pacing` + `parser/format` |
| 2 | OpenAI v3.2.3 mp1 PA | BORDERLINE | NO — borderline | `morphology/fake Armenian` |
| 3 | Claude.app Plan A controlled | PASS WITH SMALL EDITS | YES, but not ideal | `register/style` |
| 4 | Claude.app Plan D controlled | BORDERLINE | NO, but close | `semantic/anatomy` |
| 5 | Claude.app Home/Play controlled | PASS WITH SMALL EDITS | **YES** | `story coherence` (minor T2 slip) |

- **Claude Home/Play is the cleanest practical say-aloud
  controlled sample so far.** Single minor `story coherence`
  slip; no fake Armenian; no animal-anatomy mismatch; no
  literary-register drift; format clean; safe + warm; choices
  concrete and physical. The PE row of the matrix is the
  strongest controlled-and-parser-ready candidate.
- **Claude manual hedgehog remains the best magical / fairy-
  tale ceiling signal**, but is **not fully controlled and
  not parser-ready.** Captured under the consumer-app's own
  system prompt; choice-block shape is emoji-prefixed and not
  the Areg 9-label envelope; single capture, single scenario.
  Useful as a ceiling, not as decision evidence.
- **Claude Plan D shows the semantic / anatomy risk.** Even
  when Armenian morphology is clean and the atmospheric mood
  is strong, the model can still get an animal's anatomy
  wrong, use a bird-sound verb for a stream, or fold an
  ungrammatical middle clause into a literary sentence — all
  things a native ear catches instantly. Any future
  enchanted-bridge / animal-hero scenario needs an explicit
  anatomy reminder in the capture prompt.
- **OpenAI v3.2.3 remains blocked by fake / borderline
  Armenian morphology.** The structural envelope is the
  strongest of any v3.2-era run, but the named-exemplar R2
  defense has hit a clear ceiling — each round suppresses the
  named family and the next family appears. A v3.3 structural
  rule (3rd-person-past-form anchor, positive whitelist of
  safe stems) is required before another paid mp1 / mp2 is
  warranted.
- **No provider switch.** Neither side has cleared the
  comparison-plan thresholds (§ 6 of
  `controlled-claude-openai-comparison-plan-20260510.md`).
  Claude is promising-not-proven; OpenAI is
  structurally-strong-but-Armenian-risky. ChatService still
  routes to OpenAI in production.
- **No ChatService change.** No runtime configuration change,
  no production system-prompt change, no parser adaptation,
  no NuGet add, no tests touched.

---

## 7. Next safe step

Strict order — do not parallelize without explicit GO at each
step.

1. **Hand the five samples to a native Eastern-Armenian
   reviewer** with this document and the checklist as the
   input pair. The reviewer fills the per-axis 0–5 scores
   and the per-sample PASS / PASS WITH SMALL EDITS /
   BORDERLINE / FAIL verdict on each row, quoting the exact
   Armenian phrases that drive any non-PASS finding. This is
   the actual native-review pass; the document you're
   reading is the consolidated pre-pass scaffolding.
2. **Then, exactly one of the following two — not both:**
   - **(2a) one matched OpenAI Home/Play mp1 run** under the
     v3.2.3 prompt (or v3.3 if drafted by then), so the
     matrix has a PE row for both providers. Single scenario,
     `--max-prompts 1`, explicit GO required, no
     `--allow-full-set`. Native-review-pass the OpenAI PE
     output under the same checklist. This is the "close the
     matrix" path.
   - **(2b) a small production-integration design document
     — document only, no code.** Earliest possible scope of
     the document: a Claude-API-based adapter behind a
     feature flag, with parser-adaptation for the Claude
     tail-block format, with all scenarios in the matrix
     (PA + PD + PE + a Calm-mode capture) reviewed by a
     native speaker. The integration design is a *document*
     preceded by review evidence; no code change before the
     document, no code change after the document without a
     second explicit GO. This is the "start sketching the
     adapter shape" path.
3. **Pick which of 2a / 2b first based on the native review
   in step 1.** If the native reviewer confirms Home/Play
   PASS, the matched OpenAI PE run is the more informative
   next step (closes a matrix row). If the native reviewer
   downgrades Home/Play to BORDERLINE or FAIL, the
   integration design doc is the higher-value next step
   (re-derives requirements from the latest evidence
   without a paid call).
4. **Do not parallelize 2a and 2b.** A paid call running in
   parallel with an integration design that hasn't seen its
   result is a wasted call.
5. **Do not touch production runtime.** No ChatService
   change, no provider config change, no production system-
   prompt change, no parser adaptation, no NuGet add, no
   tests touched — until the native review has landed and at
   least 2a *or* 2b has been completed and reviewed.
6. **No paid Claude API call** in this slice or the next.
   Claude API enters the picture only inside step 2b's
   integration design doc as a *future* component, and only
   actually runs after a second explicit GO from Hayk on a
   later slice.

---

## 8. No secrets included

This file contains no API key, no token, no bearer credential,
no parent JWT, no device API key, no private endpoint, no
Anthropic account identifier, no OpenAI account identifier, no
Claude.app session identifier. All quoted Armenian phrases are
pulled verbatim from the cited per-capture evaluator notes.

---

## 9. Scope guard

Authoring this review pass touched no production / runtime
files: `ChatService`, backend code, frontend, `appsettings*.json`,
`*.csproj`, tests, seed bank, name bank, story-plan generator,
validator, runtime system prompts (production sha
`54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
unchanged), speech / TTS / STT — all unchanged. No paid API call
was made; no backend was started; no provider configuration was
touched; Claude API was not used. The only artifact is this
markdown under `tools/StoryModelBakeoff/evaluations/`.

This document does not authorize a provider switch, a code
change, a paid run, or any production action. It is a
consolidated review pass intended to scaffold the actual
native-Armenian review and the next decision-relevant step
(matched OpenAI PE run *or* integration design document).

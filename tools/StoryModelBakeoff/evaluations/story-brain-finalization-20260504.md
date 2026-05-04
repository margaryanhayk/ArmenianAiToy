# Story-brain finalization (2026-05-04)

**Status:** documentation / evidence consolidation only. **No
production code change.** No `ChatService` change. No runtime
prompt change. No provider switch. No live model / API call.
No edits to seed bank, character name bank, generator, or
validators. The deliverable is this file.

This is the single status document for the story-brain
research arc as of `019177c`. It consolidates what's
working, what's still risky, what was newly surfaced in the
v3.1 Plan A capture, and what the next safe repo slices are.
Speech / TTS / STT / parent-dashboard / backend work is out
of scope here — story-brain only.

**Companion files (chronological, most recent first):**
- [`./writer-prompt-v3-1-plan-a-capture-20260504.md`](./writer-prompt-v3-1-plan-a-capture-20260504.md) — v3.1 Plan A capture (filled, all gates green on this single sample).
- [`./writer-prompt-v3-1-hardening-notes-20260504.md`](./writer-prompt-v3-1-hardening-notes-20260504.md) — v3.1 rule changes (A–E) and the C14 / C15 / C16 gate definitions.
- [`./writer-prompt-v3-plan-a-capture-20260503.md`](./writer-prompt-v3-plan-a-capture-20260503.md) — v3 Plan A capture (C9 PASS / C8b / C13 / C14 FAIL).
- [`./writer-prompt-v3-bounded-arc-notes-20260503.md`](./writer-prompt-v3-bounded-arc-notes-20260503.md) — v3 bounded-arc design.
- [`./writer-prompt-v2-first-capture-20260503.md`](./writer-prompt-v2-first-capture-20260503.md) — v2 capture package.
- [`./writer-prompt-tightening-notes-20260503.md`](./writer-prompt-tightening-notes-20260503.md) — v2 rule proposal.
- [`./plan-to-story-four-profile-capture-20260501.md`](./plan-to-story-four-profile-capture-20260501.md) — v1 four-profile (age 4 / 5 / 6 / 7) capture across Plans A / B / C / D.
- [`./character-name-wiring-plan-20260503.md`](./character-name-wiring-plan-20260503.md) — generator opt-in `--with-names` design.
- [`./character-name-native-review-20260503.md`](./character-name-native-review-20260503.md) — name-bank review checklist (47 animals, awaiting Hayk).

---

## 1. Where we are now

- `main == origin/main == 019177c`. The latest pushed
  evidence is the v3.1 Plan A capture fill.
- **Story-brain focus only.** Speech, TTS, STT,
  parent-dashboard, audio cleanup, runtime cost, and
  backend work are **not** the concern of this slice.
- The Story Director research pipeline exists as
  *research tooling*, not production runtime:
  - **Phase 1**: hand-edited `story-seed-bank.v1.json`
    (47 animals + places + magical objects + sensory
    details + story-control attributes + age tone
    profiles + guardrails) with a pure-Node validator.
  - **Phase 2**: `generate-story-plan.js` emits 17-field
    plans pinned by `--age-profile` (age-4-simple /
    age-5-balanced / age-6-story-rich / age-7-richer),
    with optional `--with-names` opt-in for the
    character name bank.
  - **Phase 3a**: `validate-story-plan.js` (Plan Gate)
    enforces seed-bank membership, hero/friend
    distinctness, hardAvoidCreatures /
    forbiddenTonePatterns leak detection, banned choice
    phrases, choice grounding, and choice-type
    consistency. Optional name-field validation is
    half-state-permissive.
  - **Phase 3b**: writer prompt is the rendering layer.
    Currently iterated through v1 → v2 → v3 → v3.1.
  - **Phase 4**: quality gate (rubric + per-rule
    pass/fail) is operator-driven via the capture
    packages.
- **Claude.app evidence suggests** better Armenian
  fairy-tale quality than the OpenAI free-form baseline
  observed in the 2026-04 weak-baseline review (referenced
  via `tools/story-quality-evidence-20260425.md` in the
  working tree, intentionally not committed).
- **v3.1 Plan A capture** (`019177c`) **passed
  C9 / C14 / C15 / C16 / C13** on a single Claude.app
  run. C3 (no duplicated-sentence-trio) also PASSED on
  this run, which is unexpected and worth confirming
  via API rather than relying on it.

This is **not** a runtime statement. ChatService,
`system-prompt.txt`, and the production OpenAI provider
selection are unchanged from before this research arc began.

---

## 2. What works

- **Story Director hypothesis looks promising.** The
  seed bank → plan → Plan Gate → writer prompt pipeline
  produces more focused stories than the v0 free-form
  baseline. The plan-conditioning is doing real work
  in shaping coherence.
- **120-plan age-profile review.** An earlier slice
  generated 30 plans per age profile (120 total) and
  rated them through the Plan Gate plus a manual review
  axis. Result: **103 strong, 17 acceptable, 0 weak,
  0 reject**. This is the empirical floor under the
  generator + Plan Gate combination.
- **Claude.app plan-conditioned renders are usable
  across ages 4–7.** The v1 four-profile capture
  produced rubric scores in the 4.0–4.5/5 range across
  Armenian naturalness, fairy-tale feeling, and warmth.
  Rubric weak spot was choice format and per-turn issues
  (opener variety, moralizing, length), all of which v2
  / v3 / v3.1 progressively addressed.
- **v3.1 Plan A capture fixed every v3 failure on this
  one sample**:
  - **C14** (no meta-output) — v3 leaked
    `Շարունակեց հեքիաթը՝ ստեղծելով կախարդական պահ և
    նոր ընտրանքներ։` twice on Turn 2; v3.1 emitted
    none.
  - **C15** (Turn 2 BREAK-GLASS byte-for-byte) — v3
    invented `Ա: հետևել դեղին թիթեռին` /
    `Բ: քայլել փոքրիկ արահետով`; v3.1 emitted
    `Ա: ուղեկցել արագիլին մինչև երկնքի եզրը` /
    `Բ: մնալ այգում և նայել, թե ինչպես է արագիլը
    թռչում տուն` exactly as required.
  - **C13** (Turn 3 closure 70–100 w) — v3 ran
    ~155 w; v3.1 closed at ~75 w.
  - **C16** (Turn 1 first sentence includes
    `խնձորենու այգ`) — v3.1 opened with
    `Խնձորենու այգում`.
  - **C9** (final turn no choice block) carried over
    from v3's success and held under the heavier
    v3.1 instruction load.
- **Armenian quality is much better than the old weak
  baseline.** Eastern Armenian register is natural; no
  obvious calques, no Russified syntax, no Western
  Armenian forms in the captured outputs.
- **Child warmth and fairy-tale feeling are
  promising.** Tatik narrator-frame, native fauna
  pairings, magical-object grounding all land warmly
  for the age-4 target.

---

## 3. What is still bad / risky

The list is intentionally long. Story-brain MVP is **not**
done; this section names the open risks faithfully.

- **Free-form one-shot story generation is unstable.**
  Without the plan + writer prompt structure, output
  drifts on opener, format, length, and moralizing.
  The Story Director pipeline is the discipline that
  fixes this; the discipline is not yet wired into
  production runtime.
- **OpenAI / Areg baseline can produce artificial
  Armenian.** The April-2026 weak-baseline notes
  (uncommitted) record translated-feeling phrases,
  bookish syntax, and overuse of formulaic openings.
  Production runtime today still uses this baseline
  surface.
- **Claude.app evidence is ceiling evidence, not
  runtime / API evidence.** Every capture in this trail
  is a Claude.app paste — operator-driven, not
  reproducible from code, not under decoding control.
  An API run is the only way to confirm any of this for
  runtime-decision purposes.
- **Claude.app duplicated-sentence-pair artefact has
  appeared on every continuation across v1 / v2 / v3.**
  v3.1 Plan A did NOT show it. **Treat that as
  suggestive, not conclusive.** The artefact is
  presumed UI-side (stream-vs-final collision) but the
  hypothesis is unconfirmed without API evidence.
- **Models overuse formulaic openings unless blocked.**
  v1 captures opened with `Մի անգամ` half the time;
  v2 / v3 / v3.1 had to forbid the formula explicitly.
  Without the rule, drift returns.
- **Choice format drifts unless strictly enforced.**
  v1 captures showed four distinct choice prefixes
  (`Ա)`, emoji bullets, `Ա․`, `Ա.`); only v3.1's
  byte-for-byte BREAK-GLASS rule held the format
  uniformly on the captured sample.
- **Models may moralize through wise-character
  dialogue.** Tatik aphorisms (`Ամենահամեղ խնձորը նա է,
  որ կիսում ես սիրելիի հետ`) and wise-cat aphorisms
  (`Համբերատար սիրտը գիտի...`) appeared in v1 captures.
  v2's anti-moralizing rule held; without it, drift
  returns.
- **Story can become endless without bounded-arc
  rules.** v2 had no stop condition and produced
  unbounded continuations (peach → sleep → dream →
  homecoming → hug → peach-share → still a choice
  block). v3's 3-turn arc + closure rule is what
  brought this under control. Without the rule, drift
  returns.
- **Turn 2 can invent choices unless the exact-choice
  contract is strong.** v3 had the prose instruction
  but the model still invented choices. Only v3.1's
  positive + negative example pair held it.
- **Meta-output can leak unless explicitly blocked.**
  v3 did not block it; v3.1's anti-meta rule did.
- **Continuations can exceed spoken-toy length
  budget.** v3's 70–110 closure budget was overshot at
  ~155 w; v3.1's tightened 70–100 budget plus "no new
  micro-events" guard held.
- **Sensory details can clash with mood.** v1 Plan C
  surfaced winter-mood paired with bee-buzz sensory
  detail. The seed bank does not enforce sensory ↔
  mood coherence; the Plan Gate does not check it
  either.
- **Some generated plans still need native Armenian
  review.** The 17 "acceptable" plans from the
  120-plan review have specific weakness notes that
  Hayk's native ear should overrule or accept.
- **Character name bank needs Hayk native-ear cleanup
  before evidence use.** The 47-animal nickname-style
  bank (`c492beb`) has known repetition heaviness
  (`Թաթո` / `Փետուրո` × 7 each) and several flagged
  names that the
  [`./character-name-native-review-20260503.md`](./character-name-native-review-20260503.md)
  checklist is staged to address.
- **Named-plan generation is opt-in (`--with-names`)
  and should not yet feed production.** No v1 / v2 /
  v3 / v3.1 capture uses named plans. The name-bank
  review must complete first before named plans can
  be evidence-relevant.
- **API comparison is still missing.** No live
  Claude API run, no live OpenAI API run, no live
  Gemini run. Provider environment variables are
  configured in the bake-off CLI but no live run has
  fired in this slice.
- **Production integration must wait.** Every
  decision in this trail has been "no runtime change."
  That posture is the only defensible one until the
  API and multi-sample blockers are cleared.

---

## 4. Newly discovered concrete issue — plan-side spatial coherence

The v3.1 Plan A capture surfaced one new defect that **is
not** a writer-prompt failure:

- The plan-generated `plan.choiceB` for Plan A was
  `գնալ դեպի խնձորենու այգի` ("go to the apple
  orchard").
- The story opens IN the apple orchard
  (`Խնձորենու այգում արևի տաք շողը...`).
- "Go to the place we're already in" is semantically
  vacuous as a child choice — there is no movement, no
  arc, no consequence to the action.
- The writer obeyed the plan correctly: the v3.1 Turn 1
  prompt's BREAK-GLASS block forced the choice
  byte-for-byte, and the writer copied it exactly. C6
  PASSED. The defect did not enter at the writer layer.

**Where the defect lives.** `generate-story-plan.js`'s
`placeActions(place)` helper emits these unconditional
templates on every place:

```
"գնալ դեպի <place>"
"քայլել դեպի <place>"
"իջնել դեպի <place>"        (water/low-spot only)
"բարձրանալ դեպի <place>"     (high-spot only)
```

There is no check that `place` is the *current scene's*
location — which it always is, because `plan.place` is the
scene by construction. So the "go to <place>" pattern is
*always* spatially vacuous on Turn 1 if it gets selected as
the place-grounded choice.

**The defect affects more than Plan A.** Plan D
(age-7-richer #6, the v1 / v2 four-profile capture
companion) carries the same shape: `place = երազային
բացատ`, `choiceA = գնալ դեպի երազային բացատ`. If Turn 1
opens in the dreamy meadow (which it did in v1 / v2 / v3
captures), Plan D's choiceA has the same defect Plan A's
choiceB has.

**Suggested fix (out of scope for this slice).** Three
candidate directions, in increasing order of work:

1. **Sub-location templates.** Swap `"գնալ դեպի <place>"`
   to a sub-location pattern when the place IS the scene
   — `"գնալ դեպի այգու եզրը"`, `"գնալ դեպի կամրջի տակը"`,
   etc. Requires a small per-place sub-location table or
   a heuristic suffix list (`-ի եզրը`, `-ի խորը`,
   `-ի տակը`, `-ի կենտրոնը`).
2. **Switch the place choice to a scene-element approach
   action.** When the place IS the scene, emit a choice
   that approaches another atom of the plan instead —
   `"մոտենալ արագիլին"`, `"նայել հեռվում"`. Requires the
   generator to introspect the plan beyond `place`.
3. **Both.** Use sub-location templates as primary, fall
   back to scene-element approach when no sub-location
   inflects naturally.

**Not in scope for this finalization document.** This is a
generator-side fix that lives in `generate-story-plan.js`
and the Plan Gate (which would need a matching rule). § 8
slice A names this as the recommended next slice.

---

## 5. Blockers before production integration

Concrete gates that must clear before any conversation
about wiring story-brain into runtime:

1. **v3.1 must be tested on more samples.** A single
   Plan A capture is one data point. Plan D
   (age-7-richer #6) is the load-bearing
   second-sample case — it stress-tests the rich-tone
   register that exposed v1 / v2 / v3's worst length
   overshoots, and it carries the spatial-vacuity issue
   on its own choiceA. v3.1 against Plan D, before
   anything else.
2. **API run must confirm no Claude.app artefacts.**
   The duplicated-sentence-pair artefact has been a
   constant on Claude.app continuations except in the
   single v3.1 Plan A run. The artefact must either
   (a) reproduce on the API path — confirming it is a
   model issue and v3.1 needs further hardening — or
   (b) NOT reproduce on the API path — confirming the
   UI hypothesis. Either outcome is informative; the
   current "we hope it's UI-side" is not enough.
3. **Parser / format rules must be stable.** Choice
   parsing (`Ա: ` / `Բ: ` exact prefix) must work
   deterministically across every sample. Today the
   contract is enforced *in the prompt*; the runtime
   must either keep that prompt contract or build a
   tolerant parser that accepts the four observed
   styles (`Ա: `, `Ա)`, `Ա.`, `Ա․`).
4. **Exact-choice handling must be machine-checkable.**
   When the plan ships a choice block, the writer's
   output must match it byte-for-byte (C15 contract).
   A post-hoc validator (or a normalizer) must enforce
   this in production, not just the prompt.
5. **Plan generator must avoid spatially-vacuous
   choices.** § 4 above. Until the fix lands, every
   plan has up to a 50% chance of carrying a "go to the
   place we're already in" choice on Turn 1.
6. **Plan / name banks need native Armenian cleanup.**
   The
   [`./character-name-native-review-20260503.md`](./character-name-native-review-20260503.md)
   checklist is staged but not executed. The seed bank
   has no equivalent native-review pass committed; the
   17 "acceptable" plans from the 120-plan review have
   weakness notes that should feed Hayk's review.
7. **Safety / parent / audit expectations must be
   preserved.** The repo's existing safety posture
   (dual moderation, mode boundaries, parent dashboard
   visibility, audit events) is unchanged by this
   research arc. Any production wiring must preserve
   these contracts; no shortcut.
8. **Runtime cost / latency / retry behaviour must
   be evaluated.** v3.1's per-turn prompts are ~30%
   longer than v3's. Three turns per story = three API
   calls per story session. Both axes (per-call cost,
   per-session cost) need a budget calibration before
   any wiring decision.
9. **Production ChatService integration design must be
   reviewed separately.** A separate design slice
   (slice E in § 8) maps how story-brain plugs into
   ChatService's existing orchestration: where the
   plan is generated, where the BREAK-GLASS choices
   are surfaced to the writer prompt, how `Ա` / `Բ` are
   parsed back from the model's reply, how the bounded
   arc is tracked across turns. None of this is
   designed yet.

---

## 6. Non-blocking polish items

Real but lower-priority. Won't block MVP; should be
folded into the natural revision cycle:

- **Native read-aloud polish** for slightly written /
  bookish phrases (e.g. `սպիտակ թևերը՝ կախ` reads more
  literary than spoken; a tatik would say something
  warmer). v3.1 Plan A scored 4 / 5 on Armenian
  naturalness — there's a half-point to recover with
  Hayk's ear.
- **Better sensory-detail balance.** Two
  sensoryDetails per plan is the current shape; some
  pair clashes with the mood (Plan C v1: winter mood
  + bee-buzz). A coherence rule in the Plan Gate
  could catch this.
- **More varied child-facing choice question
  phrasing.** The choice-prelude line ("Շնիկը մտածում
  է, թե ինչ անի հիմա" vs "Տատիկը մեղմ ժպտաց ու
  սպասեց...") is invented by the writer; some samples
  feel templated. Worth flagging in the writer prompt
  as "vary the choice-prelude" if it persists.
- **Optional richer age-7 style.** The age-7-richer
  ageToneProfile allows light poetry; v3.1 Plan A is
  age-4-simple, so this hasn't been stress-tested under
  v3.1 yet. A Plan D capture covers this.
- **Stronger local Armenian nature / object palette.**
  Seed bank palettes are good but lean toward
  generic-fairy-tale objects (silver bell, golden
  thread). More distinctly local Armenian elements
  (`թոնրի տաք հաց`, `ծիրանի ծառ ծաղկած`,
  `հայկական խաչքար մամռոտ` for older ages) could
  deepen the "feels like Armenia" texture.
- **Avoiding repeated tatik / wise-guide pattern
  overuse.** Plans A and D both happen to use elder-
  guide framings; a 30-plan corpus would show whether
  the seed bank's `relationshipTypes` palette is
  drawn from evenly. If elder-guide dominates, that's
  a palette-balance issue not a writer issue.

---

## 7. Decision: continue Story Director or not?

**Verdict:**

- **YES, continue Story Director.** The empirical
  evidence (4.0–4.5 / 5 rubric scores on Claude.app
  v1 / v2 / v3 / v3.1, all four hardening gates green
  on v3.1 Plan A) is strong enough to keep the
  research arc alive.
- **DO NOT switch runtime provider yet.** OpenAI
  remains the production provider. Claude is a
  research candidate, not a commitment.
- **DO NOT integrate into production yet.** Every
  blocker in § 5 must clear first.
- **Treat Claude-style output as the quality target
  and Claude.app as research evidence**, not as proof
  that production runtime would deliver the same.
- **v3.1 is the preferred writer-prompt candidate
  for further evidence and API testing**, not for
  production. The v3.1 hardening rules (A–E + new
  gates C14 / C15 / C16) hold against Plan A on a
  single Claude.app run; they remain unproven on
  Plan D, on age-5 / age-6 plans, on multi-run
  variance, and on the API path.

The single-sample caveat is the constant. Treat it as
load-bearing.

---

## 8. Next 3–5 safe repo slices

Recommended order, with the explicit dependency graph:

### Slice A — fix generator spatial-choice defect

**File scope:** `tools/StoryModelBakeoff/generate-story-plan.js`,
optionally `tools/StoryModelBakeoff/validate-story-plan.js`,
optionally `tools/StoryModelBakeoff/README.md`.

**Goal:** prevent the generator from emitting "go to
<plan.place>" choices on Turn 1 when `plan.place` is the
scene's setting — which is always. Replace with sub-
location or scene-element approach actions per § 4
suggestions.

**Why first:** the defect affects every plan, not just
Plan A. Plan D's `choiceA` has the same shape; running
Plan D v3.1 capture (slice B) before this fix would
carry the same noise into the v3.1 evidence. Fix-first
is cleaner.

**Tool-only.** No production change. No backend touch.

### Slice B — Plan D age-7-richer v3.1 capture

**File scope:** new
`tools/StoryModelBakeoff/evaluations/writer-prompt-v3-1-plan-d-capture-20260505.md`
(or the appropriate date), parallel to the Plan A
capture package.

**Goal:** stress-test v3.1 against the rich-tone age-7
plan that exposed v1 / v2 / v3's worst length overshoots
and abstract drift. Score the same C9 / C13 / C14 / C15
/ C16 gates plus the standard rubric.

**Depends on slice A** — should be captured against a
generator that no longer emits spatially-vacuous
choices, OR if running before slice A lands, must note
the spatial defect explicitly in the capture (since
Plan D's existing committed JSON predates the fix).

### Slice C — validator/check for spatially-vacuous place choices

**File scope:**
`tools/StoryModelBakeoff/validate-story-plan.js`,
optionally `tools/StoryModelBakeoff/README.md`.

**Goal:** even after slice A makes the generator
*produce* better choices, the Plan Gate should
*reject* any plan that does emit a "go to current
scene's place" choice — defense in depth. A hand-edited
research plan or a regression in the generator
shouldn't sneak past.

**Could be combined with slice A.** Splitting it
matches the existing "fix the producer, harden the
consumer" pattern in this repo.

### Slice D — API comparison run

**File scope:** new evidence file under
`tools/StoryModelBakeoff/evaluations/`; the bake-off
CLI itself (`tools/StoryModelBakeoff/Program.cs`)
already supports `--run --provider claude --i-understand-live-cost`
with `ANTHROPIC_API_KEY` env-var.

**Goal:** run the v3.1 prompts (Plan A and Plan D)
against the **Claude API** and the **OpenAI API** on
the same plans, with the same prompts, and compare
outputs head-to-head. Specifically resolves:

- C3 (duplicated-sentence-pair) — UI-side or
  model-side?
- per-call cost / latency budget
- whether OpenAI's API output under v3.1 prompts
  approaches Claude.app quality (which would mean no
  provider switch is needed)

**Gated on operator-side API key provisioning.**
`ANTHROPIC_API_KEY` and `OPENAI_API_KEY` must be set;
the bake-off CLI's pre-execution plan + Ctrl-C protocol
handles cost discipline.

### Slice E — production-integration design doc

**File scope:** new design document. **Markdown only.
No code change.**

**Goal:** map how the Story Director pipeline plugs
into ChatService's existing orchestration:

- where the plan is generated (per-conversation? per-
  turn? cached?);
- where the writer prompt is rendered (replacing
  `system-prompt.txt`? alongside it?);
- how `Ա` / `Բ` parsing feeds back into `ChoiceNormalizer`
  / `TailBlockParser`;
- how the bounded 3-turn arc is tracked across HTTP
  requests (the existing 30-min in-memory choice
  handoff dictionary is the obvious analogue);
- what the moderation contract looks like
  (input + output dual-moderation must still hold);
- what the safety contract looks like
  (mode boundaries from `MODES.md` must still hold);
- what the parent-dashboard contract looks like
  (`Today summary` / audio replay / audit events
  must continue to work).

**Only written, never executed, until slices A–D pass.**

---

## 9. What NOT to do yet

Hard "no" list:

- **Do not switch runtime provider.** OpenAI stays.
- **Do not edit `ChatService`.** Frozen.
- **Do not replace current production prompts.**
  `system-prompt.txt` and `appsettings.json`'s
  `OpenAI:Model` selection are unchanged.
- **Do not connect Story Director to production.**
  The pipeline lives in `tools/StoryModelBakeoff/`,
  not in `backend/`.
- **Do not touch TTS / STT / speech path.** Out of
  scope.
- **Do not rely on Claude.app artefact behaviour as
  API truth.** Specifically: do not assume the C3
  PASS on v3.1 Plan A means the model has stopped
  emitting duplicated sentences. The artefact has been
  consistent on every prior continuation; one PASS is
  not a fix.
- **Do not overfit to one Plan A sample.** Every
  decision based on a single capture is provisional.
  Plan D is the next must-do.
- **Do not use `--with-names` plans in production.**
  The character name bank is unreviewed; named-plan
  generation is opt-in research-only.

---

## 10. Definition of Done for story-brain MVP

Story-brain is "MVP done" when **every** item below
holds, on **multi-sample evidence** (not a single
Plan A run):

1. **Validated plan generation with no spatially-
   vacuous choices.** Slice A + slice C land. The
   Plan Gate rejects "go to current scene's place"
   choices.
2. **Native-reviewed seed / name / palette quality.**
   Hayk's native review on
   `story-character-names.v1.json` and
   `story-seed-bank.v1.json` palettes is signed off.
3. **Writer prompt produces natural Eastern Armenian
   across age-4 / age-5 / age-6 / age-7 samples.**
   v3.1 (or successor) holds C1–C16 across at least
   one capture per age profile, with rubric scores
   ≥ 4 / 5 on Armenian naturalness, Eastern Armenian
   correctness, fairy-tale feeling, and warmth.
4. **Bounded 3-turn arc works consistently.** C9
   PASS rate is 100% across the multi-age sample.
5. **Exact choices are stable and parseable.** C6
   (Turn 1 plan choices) and C15 (Turn 2 BREAK-GLASS)
   PASS on every sample. The runtime parser handles
   the rendered output deterministically.
6. **No meta-output.** C14 PASS on every sample.
7. **No duplicate / repeated first-sentence artefact
   in API path.** C3 confirmed via API run, not just
   Claude.app.
8. **Length budgets suitable for spoken toy.**
   C7 / C8c / C13 PASS on every sample. Closure
   budget tightened for spoken pacing where needed.
9. **Child-safe, warm, non-moralizing, fairy-tale
   tone.** C2 PASS on every sample. Safety axis of
   the rubric: PASS.
10. **Parent / audit / safety expectations
    preserved.** Production safety contracts
    (`MODES.md`, dual moderation, parent dashboard
    visibility, audit events) demonstrably unbroken
    under the proposed integration.
11. **Production-integration design reviewed before
    any code wiring.** Slice E delivered, Hayk
    reviewed, dependencies on ChatService
    architecture mapped, risks named.

Until **all eleven** hold, story-brain is research,
not production.

---

## 11. Out of scope for this note

- No edits to `tools/StoryModelBakeoff/system-prompt.txt`.
- No edits to `tools/StoryModelBakeoff/bakeoff-prompts.json`.
- No edits to `tools/StoryModelBakeoff/story-seed-bank.v1.json`.
- No edits to `tools/StoryModelBakeoff/story-character-names.v1.json`.
- No edits to `generate-story-plan.js`,
  `validate-story-plan.js`, `validate-seed-bank.js`,
  or `validate-character-names.js`.
- No edits to existing v1 / v2 / v3 / v3.1 capture
  files. They stand as the historical evidence record
  for the iteration arc.
- No edits to `backend/**`, `parent.html`, frontend,
  tests, `appsettings.json`, or any `.csproj`.
- No production runtime changes.
- No new provider integration, API call, or live
  model run.
- No commitment that v3.1 is the final writer prompt
  — it is the **preferred candidate** as of `019177c`,
  pending Plan D and API evidence.

# Story Voice MVP — Phase 2 evaluation (2026-04-25)

Evaluator pass over the **fresh-conversation** evidence set
(`tools/story-quality-evidence-fresh-20260425.md`). The contaminated
single-session capture (`tools/story-quality-evidence-20260425.md`)
is deliberately ignored for scoring. Per project guardrails:
**no code changes, no prompt changes, no commits, no new backend
calls were made for this evaluation.**

Rubric source: `backend/.claude/agents/areg-story-evaluator.md`
augmented with the user-requested 10-dimension framing for this
slice. Mode-spec source of truth: `.claude/MODES.md` § Story Mode.

## 1. Executive summary

Across 10 fresh first-turn Story openings the system is producing
output that is **structurally compliant but qualitatively weak**.
HTTP shape, mode classification, safety flag, and choice-block
emission all behave correctly; the existing `StoryBenchmark`
metrics (`StartOk`, `ChoiceOk`, `ContOk`, recap overlap) would
report a clean run on this batch. They miss the actual problem.

The actual problem is **choice/body decoupling**. In **7 of 10
fresh openings** at least one choice — usually both — introduces
a noun the child has not heard in the preceding 3–5 sentences
(boxes, stones, rivers, poppies, eggs, a swallow, a beetle). The
choice block reads like it was generated independently of the
body it follows. For a screenless toy that speaks the choices
aloud, this is the dominant usability defect: the child is asked
to pick between two actions about objects that don't exist in
the story they just heard.

Secondary problems:

- **Invented or implausible Armenian nouns** in roughly half the
  cases (`Խոսկանիներ`, `Թիթեռինք`, `սակավաձորներ`, the typo
  `քին` for `քար`, the awkward `ոտքերը հպվել պահեստին`).
- **Monoculture story shape**: a "shiny mysterious object" trope
  appears in 6 of 10 openings — recurring across stones, boxes,
  glowing things — regardless of the prompt's actual subject.
- **One folklore-adjacent opening** (`ջրային աստվածուհի` / "water
  goddess" deep in "Armenian mountains" — case 01). This violates
  the explicit "Armenian folklore integration is postponed —
  do NOT add it" guardrail in `CLAUDE.md`.

Average overall score across the 10 cases is **2.5 / 5** on the
"would I let a child hear this?" axis. No case scored higher than
4. The single best case (06) is the only one where the choices
clearly reference the body. The two weakest (02 and 08) combine
fabricated Armenian with surreal or fourth-wall-breaking choices.

**Verdict: not shippable as Story Voice MVP today.** The fix
direction is clear and small enough to execute in one slice — see
§ 7 / § 8.

## 2. Per-case scoring table

Score scale 1–5 per dimension, per the evaluator rubric. Heuristic
flags from the evidence file are repeated for cross-reference.
Dimension keys map to the user-supplied list:

```
ARM    Armenian naturalness
CHILD  Child-friendliness for ages 4–7
WARM   Story warmth / magic
CLAR   Clarity
CHQ    Choice quality (in isolation)
C/B    Choice/body connection
OPEN   Open-ended continuation shape
NOBABY Avoidance of fake baby-talk
NOMOR  Avoidance of moral/closed ending
LET    Overall "would I let a child hear this?"
```

| #  | ARM | CHILD | WARM | CLAR | CHQ | C/B | OPEN | NOBABY | NOMOR | LET | Verdict     |
|----|-----|-------|------|------|-----|-----|------|--------|-------|-----|-------------|
| 01 | 3   | 2     | 3    | 3    | 2   | 1   | 4    | 4      | 5     | 2   | FAIL        |
| 02 | 2   | 2     | 3    | 3    | 3   | 1   | 3    | 4      | 5     | 2   | FAIL        |
| 03 | 3   | 4     | 4    | 4    | 2   | 1   | 4    | 5      | 5     | 3   | WEAK PASS   |
| 04 | 4   | 5     | 4    | 4    | 3   | 1   | 5    | 5      | 5     | 3   | WEAK PASS   |
| 05 | 2   | 3     | 4    | 3    | 2   | 1   | 4    | 5      | 5     | 2   | FAIL        |
| 06 | 4   | 3     | 4    | 5    | 4   | 5   | 5    | 5      | 5     | 4   | PASS        |
| 07 | 3   | 3     | 4    | 3    | 2   | 1   | 4    | 5      | 5     | 2   | FAIL        |
| 08 | 2   | 3     | 3    | 2    | 1   | 1   | 3    | 5      | 5     | 2   | FAIL        |
| 09 | 3   | 4     | 4    | 3    | 3   | 2   | 4    | 5      | 5     | 3   | WEAK PASS   |
| 10 | 2   | 3     | 4    | 2    | 2   | 2   | 4    | 4      | 5     | 2   | FAIL        |

**Averages**

| Dimension | Avg |
|-----------|-----|
| ARM       | 2.8 |
| CHILD     | 3.2 |
| WARM      | 3.7 |
| CLAR      | 3.2 |
| CHQ       | 2.4 |
| C/B       | **1.6** |
| OPEN      | 4.0 |
| NOBABY    | 4.7 |
| NOMOR     | 5.0 |
| LET       | **2.5** |

**Verdict distribution**: 1 PASS · 3 WEAK PASS · 6 FAIL.

The Choice/Body axis (1.6) and the overall LET axis (2.5) are the
two failing rows. NOMOR (5.0) and NOBABY (4.7) confirm that the
two failure modes the evidence file *suspected* (closed endings,
baby-talk register) are not actually problems on this batch — they
are well-controlled. The real damage is downstream of "story body
is mostly fine, choices are not."

### Brief per-case rationale

- **01** — `ջրային աստվածուհի` is folklore-adjacent and
  explicitly out of scope. The phrase
  `սառած հեղեղի պես քնքուշ շորեր` ("tender clothes like a frozen
  torrent") is incoherent imagery. Choices are stones with no
  referent.
- **02** — `սակավաձորներ` is not a real Armenian noun. Body is 3
  sentences (at the floor of MODES.md's 3–5 rule). Choices are
  about an unmentioned `տուփ`.
- **03** — Best body of the batch in tone (a bear-and-rabbit
  meeting about mushrooms) is undermined by an ASCII backtick
  used as a comma, the plural `ունեին` for a singular subject,
  and choices that swap mushrooms for an off-stage `մոշի տուփ`
  and `ծիծեռնակ`.
- **04** — Cleanest Armenian in the batch. Body has the box, the
  hook is genuine ("what's inside?"). Then the choices throw
  the box away and ask about a river that never appeared.
- **05** — `Խոսկանիներ` (singers?) is invented. Stone in choices
  is unmotivated; `քարը կրծելով` ("by gnawing the stone") is
  both decoupled and oddly phrased for a snail protagonist.
- **06** — The only case where both choices refer to the body.
  Marked down on CHILD because `Կարապետ` is an adult-male given
  name attached to a "փոքրիկ" character — jarring for 4–7. But
  the choice/body coupling works.
- **07** — `բացիկներ` (postcards?) under a girl in a decorated
  house is an unusual image. Choices invoke a poppy never named.
- **08** — Closing sentence is incoherent (`ոտքերը սկսեցին հպվել
  փոքրիկ պահեստին` ≈ "her feet started to touch the small
  storage"). Choice A is surreal (`Կարդա ձվի բառերը` — "read
  the egg's words"). Choice B (`Հեքիաթ հայտնիր ծնողներին`) is a
  fourth-wall break — the *child* is asked to tell the story to
  parents, not the protagonist to do anything.
- **09** — Typo `քին` (should be `քար`) inside a sentence about
  a shiny object. ChoiceB partially refers to the body's birds;
  ChoiceA is decoupled.
- **10** — `Թիթեռինք` (an invented diminutive — natural Armenian
  is `թիթեռնիկ`). `մոր միստիկ ծառի տակ` ("under mother's mystical
  tree") leaves "whose mother?" unresolved. ChoiceA introduces a
  beetle (`Բզեզ`) that has no presence in the body.

## 3. Top 3 recurring failure modes

### 3.1 Choice block decoupled from story body — **dominant**

7 of 10 cases (01, 02, 03, 04, 05, 07, 08, with 09 and 10 partial).
Both choices in cases 01, 02, 04, 05, 07, 08 introduce a noun never
named in the body. The pattern is so repetitive that it has the
shape of a generation-time defect, not a sampling artifact.

**Likely root cause** (hypothesis, not confirmed by reading
`StoryChoiceInstruction`): the choice-emission step is being
conditioned on the prompt and prior-turn context but is not
reliably grounding on the body it just produced. The choices that
emerge tend to draw from a fixed set of "story tropes" the model
has been primed on (boxes, stones, rivers, hidden things) rather
than from the entities the child just heard.

### 3.2 Implausible / invented Armenian nouns and word forms

Visible in cases 02 (`սակավաձորներ`), 05 (`Խոսկանիներ`), 09 (`քին`
typo), 10 (`Թիթեռինք`), 08 (`ոտքերը հպվել պահեստին`). These are
not natural Eastern Armenian — they read like the model
attempting Armenian morphology and producing forms a native
speaker would not. This is a layer-2 problem (model output
quality in a low-resource language), and prompt-only mitigation
will only partially help.

### 3.3 Monoculture story shape — "shiny mysterious object" trope

Cases 01 (stone), 03 (apple → mushrooms but choices invoke a
box), 04 (box), 06 (stone), 07 (glowing box), 09 (shiny stone).
The same skeletal shape ("small protagonist walks somewhere →
discovers a shiny / hidden / mysterious object → wonder beat")
recurs regardless of the prompt's actual subject (rabbit,
butterfly, magical, farm, adventure all collapse into this).

The StoryBenchmark `prompts.json` includes `T29: "tell me a story
about a small box with a surprise"` — the trope may have leaked
into the system prompt's exemplars or be an unintended bias from
the benchmark prompt set. Worth checking whether
`StoryChoiceInstruction` has an example that anchors the model on
this shape.

## 4. Best 2 cases

### Case 06 — `պատմիր կախարդական հեքիաթ` — **PASS**

The only case where both choices clearly refer to the body
(stone is named in the body, hike is implied by the protagonist
walking the forest path). Body imagery is concrete and warm. The
last sentence is an in-prose hook (`Ի՞նչ անել հիմա՝ կարո՞ղ է
լինել մի կախարդական բան։`) rather than a narrator question.
The single weakness is the protagonist name `Կարապետ` (an
adult-male first name) attached to a small character.

### Case 04 — `պատմիր հեքիաթ նապաստակի մասին` — **WEAK PASS**

Cleanest Armenian in the batch. 4 short sentences, simple
syntax, age-appropriate vocabulary. Genuine wonder hook ("what's
in the box?"). The reason this isn't a full PASS: the choices
forget the box and ask about a river that doesn't exist in the
body. If the choices had been "Բացենք տուփը" / "Թողնենք տուփը
հանգիստ" this would be the strongest case.

## 5. Worst 2 cases

### Case 08 — `պատմիր հեքիաթ ֆերմայի մասին` — **FAIL**

Triple failure: (a) the body's closing sentence
`Լյուսինիկան ուրախացավ, և ոտքերը սկսեցին հպվել փոքրիկ պահեստին`
is incoherent — feet "starting to touch" a storage room is not
a sensible image for a child; (b) ChoiceA `Կարդա ձվի բառերը`
("read the egg's words") is surreal; (c) ChoiceB `Հեքիաթ
հայտնիր ծնողներին` breaks the fourth wall — instead of the
character doing something inside the story, the *child* is told
to reveal the story to parents. This case fails on naturalness,
clarity, choice quality, and choice/body simultaneously.

### Case 02 — `Ես ուզում եմ հեքիաթ լսել` — **FAIL**

The body uses the invented noun `սակավաձորներ`, and the only 3
sentences barely hit the MODES.md floor. Both choices then
reference an unmentioned `տուփ` ("box"), with ChoiceB asking the
child to call mommy about a box that does not exist. The
"call mommy" framing additionally pulls the child out of the
story and into a household-instruction register.

## 6. Shippability verdict

**No — not shippable as the Story Voice MVP at the current state.**

Three reasons, in order of severity:

1. **Choice/body decoupling at 70% incidence.** This is the core
   value of the toy turn — the child decides what happens next.
   When 7 of 10 first turns ask the child to choose between
   actions about things that were never in the story they just
   heard, the interaction is structurally broken. On voice,
   where the child cannot scroll back to re-read, this is a
   first-impression killer.
2. **Folklore-adjacent imagery breaches an explicit guardrail.**
   Case 01's `ջրային աստվածուհի` / "water goddess in Armenian
   mountains" sits inside the postponed-folklore zone. One case
   in ten with that framing is enough to fail an Armenian-parent
   review and to violate the engineering guardrail in CLAUDE.md.
3. **Invented Armenian nouns reach the wire.** A children's
   product whose entire premise is "warm, natural Armenian" must
   not ship words that don't exist. The current batch contains at
   least four (`սակավաձորներ`, `Խոսկանիներ`, `Թիթեռինք`, the
   `քին` typo), plus several morphologically off-form
   constructions.

Counterweights worth naming so this verdict isn't melodramatic:

- The structural plumbing (HTTP, mode classification, safety
  flag, choice-block emission, story-session id allocation) is
  working cleanly. None of the six fail-to-render or fail-to-route
  failure modes the existing tests guard against fired.
- No moral-ending or baby-talk failures occurred (NOMOR 5.0,
  NOBABY 4.7 averages).
- Case 06 demonstrates the system *can* produce a coupled
  choice block within the existing prompt — so the fix is
  unlikely to require a model swap or pipeline split.

The product can become shippable in one focused slice (see § 8)
without any architecture redesign, model swap, or new
abstraction.

## 7. Recommended next fix category

**Primary: A — Prompt / rules.**

The most reliably-observed defect (choice/body decoupling) and
the most product-damaging defect (folklore violation) are both
the kind of failure that responds well to a tightened explicit
rule in `StoryChoiceInstruction` — specifically a "ground both
choices in nouns or actions present in your own preceding 3–5
sentences" rule, plus a re-issue of the existing
"no folklore / no pagan / no goddess / no spirit" guardrail
inside the system prompt body itself.

Reasons A is the right first move, not B/C/D/E:

- **Smallest, cheapest first slice.** A single string-constant
  edit + benchmark re-run fits the project's "minimal C# change"
  guardrail.
- **Clear measurement.** The existing choice-relatedness
  heuristic in the Phase 1B driver script gives a quantitative
  before/after on the same 10 prompts in seconds. A target of
  "≥ 8 of 10 first-turns with at least one choice token sharing
  a 4-prefix with a body token" is concrete and easy to verify.
- **Preserves Story-mode invariants.** No change to `ChatService`
  orchestration, no change to `TailBlockParser`, no new code
  path. Same prompt → tighter constraint → same downstream pipeline.

**Secondary, only if A doesn't move the dial after one re-eval:
B — Choice generation / fallback.** If a tightened prompt rule
fails to lift choice/body coupling above ~70% on a re-run, the
next escalation is a runtime quality gate at the choice level —
detect zero-overlap choices and either re-prompt the choice
block or fall back to a body-anchored canned pattern. This is
exactly the shape of the existing `ResponseQualityGate.CheckRetry`
in the codebase, so the abstraction is already there.

Categories C / D / E rejected for now:

- **C (Runtime quality gate beyond choices)** is over-engineered
  for what is fundamentally a prompt issue. The body itself is
  largely OK; what fails is the linkage between body and choices.
- **D (Reviewer / repair loop)** introduces a second LLM hop per
  turn — expensive on a voice product where latency is already
  variable (6–23 s in this batch). Don't pay this cost until A
  and B have been tried.
- **E (Model change)** is the largest possible blast radius.
  Current model is producing acceptable bodies; the failure is
  not "the model can't do this" but "the model isn't being told
  to ground choices in the body it just wrote." Model change
  would be an admission of failure on prompt and gate work that
  hasn't been attempted yet.

## 8. Minimal next implementation slice

**Single-file edit to `StoryChoiceInstruction` in
`backend/src/ArmenianAiToy.Application/Services/ChatService.cs`.**

Two additive bullet points inside the existing instruction
constant, no removals, no restructuring of the prompt:

1. A grounding rule for choices, e.g.
   *"Both CHOICE_A and CHOICE_B must be actions involving a
   character, object, or place that was named in your own
   preceding 3–5 Armenian sentences. If your story body did not
   contain such an entity, do not invent one in the choices —
   instead phrase both choices around the protagonist's next
   physical action in the same scene."*
2. An explicit folklore / mythology block, e.g.
   *"Do not use Armenian folklore characters, gods, goddesses,
   spirits, or mythological figures. Protagonists are children,
   small animals (rabbit, bear cub, snail, butterfly, kitten),
   or simple wholesome characters. No `աստված`/`աստվածուհի`/
   `հրեշտակ`/`ոգի`."*

**Validation plan** (no new code required):

- Re-run the existing Phase 1B driver
  (`/c/Python314/python /tmp/story-evidence/run_fresh.py`) — this
  re-registers 10 fresh devices and re-issues the same 10
  prompts.
- Compare the resulting `choices_related_to_text.ratio` averages
  against this evaluation's baseline (currently A 0.12, B 0.11
  across the batch). Target: average ≥ 0.50 on at least one
  choice per case, and zero cases where both choices are 0.0.
- Re-eyeball case 01 specifically for the folklore vocabulary
  list — should be empty.
- Run the existing `dotnet test` suite to confirm no regression
  in `StoryPromptContentTests`, `ChoiceHandoffTests`,
  `ChoiceDiversityTests`. None of these should change behavior;
  if they fail, the prompt edit accidentally removed a tested
  invariant and needs to revert / narrow.

**Out of scope for this slice** (deliberately): the
implausible-noun problem (failure mode 3.2). Native Armenian
naturalness at the model layer is not addressable by a prompt
edit alone; addressing it belongs to a separate slice that
should be scoped only after the choice/body fix has been measured.

**Estimated diff size**: < 30 lines in one file, plus zero test
changes if the existing test corpus continues to pass.

## 9. What NOT to do next

These options would burn time without addressing the dominant
defect, or would regress the working invariants. Listed so the
next session doesn't drift into them:

- **Do NOT refresh the StoryBenchmark baseline.** The existing
  baseline metrics (`StartOk`, `ChoiceOk`, `WeakCases`,
  `RecapOverlap`) would all stay at 29/29 on the fresh batch
  evaluated here — they don't measure choice/body coupling,
  Armenian naturalness, or folklore violations. Re-baselining
  would lock in a false-green signal. *If* StoryBenchmark is
  ever extended, that's a separate scoped slice with its own
  approval.
- **Do NOT add a second LLM call** (reviewer / repair loop /
  re-prompt-on-fail) before trying the prompt-rule slice. That
  doubles latency on a voice path where the bench already shows
  6–23 s end-to-end variance.
- **Do NOT switch model.** GPT-4o is not the bottleneck here —
  case 06 proves the existing model can produce a coupled
  choice block under the current prompt. Switching to another
  model now would lose that signal.
- **Do NOT add a multi-step pipeline** (separate body / choices
  / cleanup generations). Same reason — the `StoryChoiceInstruction`
  hasn't been told to ground choices in body yet, so we don't
  know if simpler conditioning fixes it.
- **Do NOT add Armenian folklore characters** as a partial fix
  to the "bland tropes" complaint. The product doc explicitly
  defers folklore. The case-01 violation is an existing leak,
  not a feature request.
- **Do NOT touch `ChatService` orchestration** (label consumption,
  story-memory injection, tail-block parsing). None of these are
  involved in the dominant failure mode.
- **Do NOT touch `ChoiceNormalizer`** or the choice-handoff path.
  The choice block is being *generated* badly, not parsed badly.
- **Do NOT touch `ModeDetector` or any mode-routing logic.** All
  10 cases routed to Story correctly.
- **Do NOT add audio / hardware / firmware work** in response to
  this finding. The defect is text-side; audio synthesis is
  faithfully reading whatever we put in front of it.
- **Do NOT add new dependencies, packages, or infrastructure.**
  None are needed for the recommended slice.
- **Do NOT broaden the prompt edit** to also try to fix
  Armenian naturalness, story shape monoculture, or pacing in
  the same patch. One axis at a time so the re-run gives a
  clean signal.
- **Do NOT ship the current state to a child as a pilot.** A
  toy that systematically asks the child to choose between two
  things they never heard about will train the wrong
  expectations of the product on the very first session.

---

**Inputs used**: `tools/story-quality-evidence-fresh-20260425.md`
only. **Inputs deliberately ignored**:
`tools/story-quality-evidence-20260425.md` (contaminated by
shared session state). **No new backend calls were made for this
evaluation.**

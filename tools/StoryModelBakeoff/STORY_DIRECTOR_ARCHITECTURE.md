# Story Director Architecture (design)

**Status:** design / evidence-only. No production code changes.
No runtime model switch. No prompt rewrite in `ChatService`. No
seed-bank JSON yet. No provider integrations beyond what F1.2
already shipped.

**Companion files:**
- [`README.md`](./README.md) — tool usage and slice status.
- [`API_VS_APP_BAKEOFF_PLAN.md`](./API_VS_APP_BAKEOFF_PLAN.md) —
  comparison plan and the decision rules that gate any runtime
  switch.
- [`SAMPLE_CAPTURE_TEMPLATE.md`](./SAMPLE_CAPTURE_TEMPLATE.md) —
  the form used for new evidence captures.
- [`samples/`](./samples) and [`evaluations/`](./evaluations) —
  manual / API evidence captured to date.

---

## 1. Problem statement

Areg's current production path is:

> child message → one free-form OpenAI Chat Completions call → final story

This one-call shape is structurally fragile, and we have evidence
on all three providers that prompt tuning alone is not enough to
fix it.

Concrete observed weaknesses (see
[`evaluations/openai-api-current-areg-baseline-story-20260501.eval.md`](./evaluations/openai-api-current-areg-baseline-story-20260501.eval.md)
for the load-bearing OpenAI capture):

- **Artificial Armenian phrasing.** Constructions like
  «շուրջը պտտվելով մոտեցավ» or «թե արդյոք իմանալու» appear in raw
  output. Reads translation-shaped rather than as native Armenian.
- **Weak fairy-tale feeling.** The OpenAI capture used `ռնգեղջյուր`
  (rhinoceros) and `հավիկ` (chicken) — exotic / zoo-flavored
  pairing rather than the Armenian fairy-tale palette
  (`ոզնի`, `նապաստակ`, `աղվես`, `գայլ`, `արջ`).
- **Weak choices.** Both options can be variants of the same
  micro-action (e.g. «we approach» / «we look») without contrasting
  the next narrative beat.
- **Single-call brittleness.** When the model lands a bad opening
  metaphor or wrong palette, the whole turn ships unchanged. There
  is no second pass, no plan check, no fallback.

The Claude / Gemini consumer-app samples
([`samples/claude-manual-pnjik-golden-leaf-20260501.md`](./samples/claude-manual-pnjik-golden-leaf-20260501.md),
[`samples/gemini-manual-mlavik-sunbeam-20260501.md`](./samples/gemini-manual-mlavik-sunbeam-20260501.md))
show a higher quality ceiling on the same prompt. But per the
architectural note in those evaluations, app output is **ceiling
evidence, not runtime evidence** — it conflates model quality
with the consumer app's hidden default system prompt and tier-1
routing. We cannot infer "switch to Claude" from app evidence alone.

The implication is structural rather than provider-shaped: the
problem is the *one-call* shape, not which weights are on the
other end of the call.

## 2. Core principle

> **The model is the writer, but Areg must be the story director.**

A model writes excellent paragraphs. Areg must decide the **story
shape** — hero, place, magical object, friend, problem, choices —
*before* the writer is asked to render the prose. Asking one free-
form call to make all of those decisions AND render the prose AND
produce a clean choice block AND match the Armenian fairy-tale
register is too many dimensions for a single inference to land
reliably.

The Story Director architecture splits the decision-making from
the writing. The director decides the story; the writer writes
the chosen story; a quality gate sanity-checks the output before
Areg speaks it.

This is provider-agnostic by design. OpenAI, Claude, and Gemini
should each be evaluated *inside* the same director pipeline,
with the same plan, the same gates, and the same fallback. That
is the only honest way to compare them on Areg's actual job.

## 3. Architecture

```
                    ┌─────────────────────────┐
                    │   Story Seed Bank       │   ← curated palettes
                    │   (heroes, places,      │     (Armenian-flavored)
                    │    objects, problems)   │
                    └────────────┬────────────┘
                                 │ random / weighted draw
                                 ▼
              ┌──────────────────────────────────────┐
              │   Story Plan generation              │   ← model call #1
              │   (model picks one combination,      │     (small, structured)
              │    proposes choiceA / choiceB)       │
              └────────────┬─────────────────────────┘
                           │ JSON plan
                           ▼
              ┌──────────────────────────────────────┐
              │   Plan Gate (rule-based)             │   ← deterministic checks
              │   reject if generic / exotic /       │     against the seed bank
              │   choices not concrete / no problem  │     and structural rules
              └────────────┬─────────────────────────┘
                           │ approved plan
                           ▼
              ┌──────────────────────────────────────┐
              │   Story generation from plan         │   ← model call #2
              │   (writer renders Armenian prose,    │     (constrained: must use
              │    bound to the approved plan)       │      the planned hero/place
              │                                      │      /object/problem/choices)
              └────────────┬─────────────────────────┘
                           │ raw story text
                           ▼
              ┌──────────────────────────────────────┐
              │   Quality Gate (rule-based)          │   ← deterministic + light-LLM
              │   reject if translated-shape /       │     checks on the rendered
              │   too short / too long / moralizing  │     output
              │   / choices not grounded / footer    │
              └────────┬─────────────────────────┬───┘
                       │                         │
                       │ pass                    │ reject
                       ▼                         ▼
              ┌──────────────────┐    ┌──────────────────────────┐
              │   Areg speaks    │    │   Rewrite (1–2 attempts) │
              └──────────────────┘    │   then Fallback to       │
                                      │   curated/template story │
                                      └──────────────────────────┘
```

Six stages. Each stage is replaceable, instrumentable, and
testable on its own:

- **Seed Bank** is data, not model. Editable by hand.
- **Plan generation** is a small structured-output model call. The
  model proposes a plan picking from (or extending) the seed bank.
  Output is JSON; failures are caught before any prose-render
  spend.
- **Plan Gate** is rule-based code, no model. Cheap. Runs
  deterministically against the seed bank constraints.
- **Story generation** is the prose-render model call. It receives
  the *approved plan* as part of the system prompt, not a free-
  form ask. The writer's job is purely to render the planned
  story in Armenian fairy-tale voice.
- **Quality Gate** is mostly rule-based with optional light-LLM
  checks (e.g. naturalness probe). Runs deterministically; the
  light-LLM check is opt-in and always after the cheap rules.
- **Rewrite / Fallback** preserves the child-facing contract: a
  failed gate never ships; rewrite up to N=2; if still failing,
  serve a curated story from a template fallback bank.

## 4. Seed Bank concept

> **Note: this section describes only what a future seed bank
> WOULD contain. No JSON file is created in this slice. The
> actual seed-bank JSON lands in Phase 1 of the roadmap (§ 10),
> as its own commit, after this design has been reviewed.**

A seed bank is a small set of curated, Armenian-friendly palettes
that the plan generator draws from. Its job is to keep the story
inside the Armenian fairy-tale register without restricting the
writer's voice. Indicative content:

- **Animals (heroes / friends):**
  `ոզնի`, `նապաստակ`, `սկյուռ`, `աղվես`, `արջուկ`, `գորտուկ`,
  `ծիտիկ`. (Local fauna; resists the rhinoceros / penguin /
  giraffe drift we see in single-call output.)
- **Places:** `ծեր ընկուզենի`, `անտառի արահետ`, `կապույտ լճակ`,
  `մամռոտ քար`, `փոքրիկ ջրաղաց`, `մեղվաբույն`. (Native landscape
  imagery; gives the writer a concrete grounding without spending
  prose-render budget on world-building.)
- **Magical objects:** `ոսկե տերև`, `արծաթե զանգակ`, `քնած բանալի`,
  `փոքրիկ լապտեր`, `խոսող կաղին`. (Small, sensory, Armenian-
  textured. Avoid plastic / sci-fi / generic Western fantasy
  artifacts.)
- **Small problems:** `զանգակը կորցրել է ձայնը`, `լապտերը մարել է`,
  `ընկերը մոլորվել է`, `ծաղիկը չի բացվում`, `կամուրջը քնած է`.
  (A small mystery is the engine of a 4–5 sentence fairy-tale
  turn. Without one, the writer drifts toward exposition or
  moralizing.)

The seed bank is **data**, deliberately not code. It must be
trivially editable by a non-engineer (Hayk's daughter's nursery
school teacher, a translator, a folklorist). The plan generator
draws from it; the plan gate validates against it.

The plan generator may be allowed to *extend* the seed bank with
new entries (subject to Plan Gate review), but only inside the
same register — adding a new local plant or a new local sensory
detail, never adding `dragon` or `astronaut`.

## 5. Story Plan shape

Indicative JSON shape (illustrative only; not a committed schema):

```json
{
  "hero":          { "kind": "ոզնի", "name": "Փնջիկ" },
  "place":         "ծեր ընկուզենու արմատների տակ",
  "magicalObject": "ոսկե տերև",
  "friendOrGuide": { "kind": "գորտուկ", "name": "Կլկլիկ" },
  "smallProblem":  "զանգակը կորցրել է ձայնը",
  "sensoryDetails": [
    "ցողի կաթիլներ արևի տակ",
    "թաց մամուռի հոտ",
    "սպիտակ քարի վրա նստած մի փոքր կերպար"
  ],
  "choiceA": "Թող ցողի կաթիլը կաթեցնի զանգակի մեջ",
  "choiceB": "Թող ոսկե տերևը փաթաթի զանգակի շուրջը"
}
```

Notes on shape:

- `hero`, `friendOrGuide` carry both a `kind` (palette item) and an
  optional `name` so the writer can address them naturally.
- `place` is a phrase, not a single noun — it gives the writer
  enough to render an opening sentence without inventing.
- `magicalObject` and `smallProblem` are paired implicitly: the
  small problem typically involves the magical object, which is
  why both rejection of the plan AND grounding of the choices key
  off this pair.
- `sensoryDetails` is a list, deliberately ≤ 3, deliberately
  short. Gives the writer texture without cluttering the prompt.
- `choiceA` / `choiceB` are pre-decided at plan time, NOT made up
  by the writer. The writer must end on these two options
  verbatim or via a grammatical paraphrase the gate accepts.

## 6. Plan Gate rules

The Plan Gate is the cheap reject point. It runs *before* the
expensive prose-render call.

Reject the plan if any of the following holds:

- **Hero / place / object are generic or non-Areg-like.** Anything
  outside the seed bank, OR a seed-bank entry the gate's freshness
  rule says was used too recently for this device, fails. Generic
  English-fantasy fillers (`dragon`, `unicorn`, `princess`) fail
  even if the model returned them in Armenian.
- **Animal palette is too random or too exotic.** A plan that
  pairs `ռնգեղջյուր` (rhinoceros), `պինգվին` (penguin), `օձ`
  (snake) etc. fails. The gate enforces "Armenian-flavored fauna"
  by allowlist.
- **Choices are not concrete.** A choice that's a feeling
  (`Թող մտածի...`) or an abstraction (`Թող փորձի լավը`) fails. The
  choice must propose an *action on the world*, not a mental
  state.
- **Choices are too similar.** If `choiceA` and `choiceB` differ
  only in surface verb (both "approach", both "look") and lead to
  the same next beat, fail. The gate uses a small diff heuristic
  on choice tokens + a "different next-state" signal.
- **No small mystery / problem exists.** A plan whose
  `smallProblem` is empty / vague / "everyone is happy" fails.
  Without an engine, the writer will pad with exposition or land
  a moralism.
- **Plan is moral lecture instead of story action.** A plan whose
  `smallProblem` is "the hero learns to share" or "the hero
  learns to be kind" fails. Lessons are out-of-band; the story
  must contain action that *implies* values, not lectures.

A failed plan retries plan generation up to N=2 times before
falling through to the template-fallback bank.

## 7. Quality Gate rules

Runs *after* the prose-render call. Same fail-fast discipline.
Reject the rendered story if:

- **Armenian sounds translated / artificial.** Heuristic flags
  for translation-shaped constructions
  («շուրջը պտտվելով», `արդյոք` + nominalised infinitive,
  English-syntax word order). A small native-grammar probe is
  acceptable; a full LLM judge is NOT (see § 9).
- **Story is too short or too long.** Outside the
  "3–5 short sentences" production directive. Length-wise this is
  observable from char/sentence count.
- **Weak fairy-tale feeling.** Mostly captured by the Plan Gate
  upstream — the seed bank carries register. The Quality Gate
  catches the residual cases where the writer drifted *off* a
  good plan: an exotic intrusion, a flat exposition opening,
  a name like "Bobby" instead of "Փնջիկ".
- **Over-moralizing.** Surface flag for sentences that explicitly
  state a moral (`երբ բարի ես լինում, ...`). Lessons must be
  shown, not stated.
- **Too babyish.** Diminutive piling
  (`թաթիկ` + `քիթիկ` + `փորիկ` + `սուլոց` in one paragraph) fails.
  Areg has age-aware tone; child age 7 should not sound like age 3.
- **Choices not grounded.** A rendered choice that doesn't reference
  the planned `magicalObject` / `place` / `friendOrGuide` fails.
  The gate verifies the choice text shares lexical anchors with
  the rendered story.
- **Continuation ignores selected choice.** For continuation turns,
  the rendered story must reference the chosen option's noun /
  action. The gate verifies this textually before the turn ships.
- **Recap instead of action.** A continuation that opens with a
  re-statement of the previous turn's events ("Փնջիկն իջել էր
  լճակի մոտ...") fails. Continuations must advance, not summarize.
- **Formatting / footer issue.** Any "As an AI..." footer, any
  loose `---` choice marker that `TailBlockParser` failed to strip,
  any English-language artifact — fails.

## 8. Rewrite / Fallback

The contract for the child is: **a failed gate never ships.**

- **N=2 rewrite attempts max.** First attempt re-prompts the writer
  with a specific failure reason (e.g. "the previous attempt was
  flagged as recap; advance the story without summarizing the
  previous turn"). Second attempt re-prompts with stricter
  constraints. Beyond two, stop spending model budget on the same
  plan.
- **Fallback to curated / template story.** A small bank of
  pre-written, native-edited Armenian template stories (one per
  seed-bank `smallProblem`, parameterised on `hero` / `place` /
  `magicalObject`). When the gate fails after rewrites, fall
  through to a template, fill the parameters, and ship that.
- **Curated fallback is editorial-grade.** It must read aloud
  cleanly without further edits. The whole point of fallback is
  that an off-day from the model never produces an off-day for the
  child.
- **Logging is mandatory but PII-free.** Every rewrite or fallback
  fires structured logs of the *kind* of failure
  (`flagged_translated`, `flagged_exotic_palette`,
  `flagged_recap`) but never logs the failed prose itself. This
  matches the audit-event PII discipline already in place
  elsewhere in the project.

## 9. Provider independence

This design is deliberately written without naming a runtime
provider.

- OpenAI, Claude, Gemini (and any future Armenian-local provider)
  are tested **inside the same Story Director pipeline**. Same
  plan generator, same Plan Gate, same writer prompt, same
  Quality Gate, same fallback.
- Provider-switching alone is not the fix. The OpenAI baseline
  failed on plan-quality grounds (rhinoceros, weak choices), not
  raw model fluency. The same model, with a Plan Gate filtering
  the rhinoceros out, would have produced a different story.
  Until we know how each provider performs *with the gates in
  place*, the bake-off comparison is not yet honest.
- The Quality Gate must NOT be implemented as "ask Claude to
  judge OpenAI output", because we'd be adding the Anthropic
  provider's voice as a tiebreaker on the OpenAI output's
  Armenian register. The gate's heuristic / native-grammar layer
  is provider-free; the optional light-LLM probe, if added, must
  be the SAME provider as the writer (or no provider at all).
- Provider choice is a **post-engine** decision. After the engine
  exists, after each provider has been measured inside it, after
  the seed bank has stabilised, then a runtime switch can be
  considered. Not before.

## 10. Safe implementation roadmap

Each phase is its own commit. No phase is required for the next
slice; the slowest, most cautious order wins for a child-facing
toy.

| Phase | Scope | Touches production? |
|---|---|---|
| **Phase 0** | This document. Design only. | No |
| **Phase 1** | Tool-only seed bank JSON under `tools/StoryModelBakeoff/seed-bank.json`. Hand-curated palette content; no code reads it yet. | No |
| **Phase 2** | StoryModelBakeoff plan-generator experiment. Adds a `--mode plan` flag to the bake-off; calls one provider, asks for a JSON plan against the seed bank, prints the plan; no prose render yet. Operator runs locally. | No |
| **Phase 3** | Offline Quality Gate scorer in StoryModelBakeoff. Pure-rule heuristics on captured prose (pulled from the existing `samples/`, `evaluations/`, F1.2 Claude API runs). No model call required for the gate itself in this phase. | No |
| **Phase 4** | Combined-pipeline bake-off: seed → plan → plan-gate → render → quality-gate → rewrite → fallback. Runs against OpenAI / Claude / Gemini. Still tooling-side; no production change. | No |
| **Phase 5** | ONLY then: a separate, approved architecture slice that integrates the engine into production `ChatService`. Has its own risk plan, migration path, parent-facing privacy review, audit-event additions, and rollback. | Yes |

**Phase 5 is intentionally far away.** It depends on every prior
phase being stable and re-runnable, and on the bake-off evidence
showing the engine measurably improves story quality across at
least two providers. Going to Phase 5 with weak earlier evidence
would replace one fragile single-call pipeline with a more
fragile multi-stage one.

---

## What this document deliberately does NOT do

- **Create the seed bank JSON.** Phase 1 owns that, as its own
  commit, after this design is reviewed.
- **Define the plan generator's exact prompt.** Phase 2.
- **Define the Quality Gate's exact heuristic implementations.**
  Phase 3.
- **Recommend a runtime provider.** Provider choice is a Phase-5+
  decision, gated by the rules in
  [`API_VS_APP_BAKEOFF_PLAN.md`](./API_VS_APP_BAKEOFF_PLAN.md) § 6.
- **Touch `ChatService`, the production system prompt,
  moderation, audit, parent dashboard, firmware, benchmarks, or
  the bake-off prompt set.** None of those are in scope for any
  phase before Phase 5.

These are reasonable to do later; each is a separately approved
slice with its own scope.

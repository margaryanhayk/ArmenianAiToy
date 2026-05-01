# Plan-to-Story experiment — 7 plans, 2026-05-01

**Status:** evidence / experiment design only. No production code
changes. No runtime model switch. No `ChatService` change. No
provider integration beyond what F1.2 already shipped.

**Companion files:**
- [`story-plan-generator-review-20260501.md`](./story-plan-generator-review-20260501.md) — the 30-plan review whose Findings + Recommendation lead into this experiment.
- [`story-plan-generator-review-20260501.plans.json`](./story-plan-generator-review-20260501.plans.json) — the load-bearing 30-plan JSON; the 7 plans below are exact slices of it (1-indexed).
- [`../STORY_DIRECTOR_ARCHITECTURE.md`](../STORY_DIRECTOR_ARCHITECTURE.md) — the multi-stage director pipeline this experiment tests.
- [`../SAMPLE_CAPTURE_TEMPLATE.md`](../SAMPLE_CAPTURE_TEMPLATE.md) — the form for capturing each model output.
- [`../samples/openai-api-current-areg-baseline-story-20260501.md`](../samples/openai-api-current-areg-baseline-story-20260501.md) — the free-form OpenAI baseline this experiment compares against.

---

## 1. Purpose

This experiment tests F1's central hypothesis:

> **Does Story Director-style generation (approved plan + writer call)
> produce better Areg story output than the current free-form
> single-call generation?**

The input is an approved Story Plan (one of the 7 selected below).
The expected output is a **natural Eastern Armenian Areg story turn
with two choices**, rendered by a writer model that is constrained
to honour the plan's hero / place / magicalObject / smallProblem /
choices.

This is **not production runtime**. The plan-to-story experiment
runs offline through the bake-off tooling (or, for ceiling
references, through consumer apps). It does **not** imply a
runtime provider switch — that decision is gated by the rules in
[`../API_VS_APP_BAKEOFF_PLAN.md`](../API_VS_APP_BAKEOFF_PLAN.md)
§ 6.

## 2. Selected plans (5 strong + 2 acceptable)

Plans #14 and #19 are the chosen "acceptable" cases because they
are exactly the two plans the 30-plan review flagged for
**recurring template polish patterns** (palm-size mismatch on
`քնքուշ բարձիկ`; mechanical-feel of "bring wreath to light"). If
a writer can lift these into good Armenian prose, the polish
patterns are downstream-fixable as predicted; if it cannot, the
seed bank or generator templates need work first.

### Plan #8 — strong

```json
{
  "hero": "լորիկ",
  "place": "քարայրի մուտք",
  "magicalObject": "լուսավոր քար",
  "friendOrGuide": "բու",
  "smallProblem": "ընկերը մոլորվել է",
  "sensoryDetails": [
    "արծաթե զանգակի թեթև զնգոց",
    "փափուկ խոտերի հպում"
  ],
  "choiceA": "պահել լուսավոր քարը ափի մեջ",
  "choiceB": "քայլել դեպի քարայրի մուտք"
}
```

**Why selected:** the strongest pairing in the batch — glowing-
stone-in-palm + cave entrance is tightly atmospheric and the
small problem ("a friend got lost") is universally fairy-tale.
Tests whether a writer can carry that atmosphere into prose.

### Plan #9 — strong

```json
{
  "hero": "ճպուռ",
  "place": "քարավանատուն",
  "magicalObject": "կավե փոքրիկ կուժ",
  "friendOrGuide": "բու",
  "smallProblem": "արևը թաքնվել է մեծ ամպի հետևում",
  "sensoryDetails": [
    "մառախուղի թաց հպում",
    "թոնիրի տաք հացի հոտ"
  ],
  "choiceA": "տանել կավե փոքրիկ կուժը ընկերոջ մոտ",
  "choiceB": "քայլել դեպի քարավանատուն"
}
```

**Why selected:** uniquely Armenian-medieval (caravanserai +
clay jug + fresh-bread oven smell). Tests whether a writer
preserves that culturally-specific texture rather than smoothing
it into a generic "old building" scene.

### Plan #12 — strong

```json
{
  "hero": "ծիտիկ",
  "place": "լուսնի արահետ",
  "magicalObject": "անմահական խնձոր",
  "friendOrGuide": "թիթեռ",
  "smallProblem": "քայլող քարը կորցրել է իր ուղին",
  "sensoryDetails": [
    "արևի տաք շող",
    "քնած ծաղիկների հոտ"
  ],
  "choiceA": "քայլել դեպի լուսնի արահետ",
  "choiceB": "վերցնել անմահական խնձոր"
}
```

**Why selected:** classical Sasna-Tsrer-flavor pairing
("immortality apple" + "moon path"). Tests whether the writer
can match that classical Armenian fairy-tale register without
sliding into Western-fantasy archetypes (princess / quest / etc).

### Plan #21 — strong

```json
{
  "hero": "ծղրիդ",
  "place": "ցորենի ոսկե արտ",
  "magicalObject": "ցողի կաթիլներով տերև",
  "friendOrGuide": "արջ",
  "smallProblem": "մեղուն կորցրել է ծաղկի ճանապարհը",
  "sensoryDetails": [
    "դարբնոցի կրակի տաքություն",
    "քամու շշուկ տերևների մեջ"
  ],
  "choiceA": "պահել ցողի կաթիլներով տերևը ափի մեջ",
  "choiceB": "քայլել դեպի ցորենի ոսկե արտ"
}
```

**Why selected:** "unlikely friends" cricket + bear is an Armenian
fairy-tale staple, and the golden wheat field places the scene in
a Caucasus-rural register. Tests whether the writer can keep the
scale mismatch warm rather than absurd.

### Plan #30 — strong

```json
{
  "hero": "գառնուկ",
  "place": "ծիրանենու տակ",
  "magicalObject": "լուսավոր քար",
  "friendOrGuide": "ճնճղուկ",
  "smallProblem": "գորտուկը մոռացել է ցատկելու երգը",
  "sensoryDetails": [
    "քնած ծաղիկների հոտ",
    "թաց մամուռի բույր"
  ],
  "choiceA": "քայլել դեպի ծիրանենու տակ",
  "choiceB": "տանել լուսավոր քարը ընկերոջ մոտ"
}
```

**Why selected:** "frog forgot its jumping song" is the most
charming small-problem in the 30-plan batch, paired with the
quintessential Armenian apricot-tree opening. Tests whether the
writer can render a problem this poetic without flattening it.

### Plan #14 — acceptable (template-stretch case)

```json
{
  "hero": "իշուկ",
  "place": "քամու երգող սարը",
  "magicalObject": "քնքուշ բարձիկ",
  "friendOrGuide": "այծիկ",
  "smallProblem": "տերևները մոռացել են իրենց ծառի տեղը",
  "sensoryDetails": [
    "մեղմ առվակի ձայն",
    "դարբնոցի կրակի տաքություն"
  ],
  "choiceA": "քայլել դեպի քամու երգող սարը",
  "choiceB": "պահել քնքուշ բարձիկը ափի մեջ"
}
```

**Why selected:** the review flagged "pillow in palm" as a
**palm-size mismatch** on the `պահել X-ը ափի մեջ` template —
a pillow doesn't fit in a palm. If the writer can paraphrase
the choice ("hug the gentle pillow", "carry the gentle pillow")
without losing the plan's intent, the polish nudge is
downstream-fixable. If it can't, the generator's template
needs a size-class tag. **This is the load-bearing acceptable
case for the polish-pattern hypothesis.**

### Plan #19 — acceptable (template-mechanical case)

```json
{
  "hero": "ծիտիկ",
  "place": "հին դարբնոց",
  "magicalObject": "դաշտային ծաղկեպսակ",
  "friendOrGuide": "ճնճղուկ",
  "smallProblem": "մեղվաբույնի դուռը կպել է",
  "sensoryDetails": [
    "թարմ խնձորի ճարճատյուն",
    "գարնան անձրևի թաց հոտ"
  ],
  "choiceA": "քայլել դեպի հին դարբնոց",
  "choiceB": "մոտեցնել դաշտային ծաղկեպսակը լույսին"
}
```

**Why selected:** the review flagged "bring the wreath close to the
light" as the most **mechanical** rendering in the batch — there
is no inspection-natural reason to bring a flower wreath toward
the light. If the writer rephrases this into a coherent action
(e.g. "lay the wreath where the morning light reaches") without
breaking plan adherence, the polish nudge is downstream-fixable.
This is the load-bearing acceptable case for the inspection-
template polish.

## 3. Writer prompt template

The writer prompt is in **English** because the production Areg
system prompt is in English (English instructions are followed
more reliably). Output target is **Eastern Armenian**. Copy this
verbatim and substitute `{{plan_json}}` with the JSON object of
one selected plan.

````text
You are Areg, a warm Armenian storyteller for children aged 4–7.
You will receive a STORY PLAN as JSON. Your job is to render the
plan into one short Eastern Armenian story turn that the child
will hear next.

ABSOLUTE LANGUAGE RULE
- Respond ONLY in Eastern Armenian, written in Armenian script.
- No transliteration, no English, no Russian.
- Use natural, fluent, spoken Eastern Armenian — the way a warm
  Armenian grandparent would tell a fairy tale to a small child.
- Do NOT use literal translations from English. Do NOT produce
  awkward, bookish, or machine-like phrasing.

PLAN ADHERENCE RULE
- You MAY polish wording, soften phrasing, and add small connective
  detail — that is your job as the writer.
- You MUST NOT replace the plan's hero, friendOrGuide, place,
  magicalObject, or smallProblem with anything else.
- The two final choices MUST preserve the meaning of the plan's
  choiceA and choiceB. You may rephrase them for grammar and
  warmth, but they must point to the same actions.
- The two sensoryDetails should appear naturally in the prose
  (you may rephrase them; do not replace them with unrelated
  imagery).

LENGTH AND PACING
- 120–180 Armenian words for the story body (NOT counting the
  two choices).
- Several short, clear sentences. Soft cadence, not bookish.
- Concrete sensory writing. No abstract emotional summary.

SAFETY AND TONE — ABSOLUTE
- No violence, no weapons, no horror, no scary danger, no death,
  no abandonment, no medical or scary illness.
- No moral lecture. Show through action, never state a moral.
- Not babyish. The child is 4–7, not 2.
- No translated-feeling Armenian.

OUTPUT FORMAT
Output ONLY:
1. The story body (Armenian prose).
2. A blank line.
3. Two short concrete choices, one per line, prefixed with
   "Ա: " and "Բ: " in that order.

Do NOT output:
- The plan JSON.
- Any English.
- Any markdown headings, code fences, or bullets.
- Any explanation, footer, or "Note:" line.
- The phrase "As an AI…" or any meta-comment.

STORY PLAN:
{{plan_json}}
````

## 4. Expected output contract

Each rendered story turn must contain exactly:

| Field | Definition |
|---|---|
| **Story body** | 120–180 Armenian words. Several short sentences. Concrete sensory writing. References the plan's hero, friendOrGuide, place, magicalObject, smallProblem. Touches both `sensoryDetails` items. |
| **Choice A** | Single line, `Ա: <Armenian phrase>`. Preserves the meaning of the plan's `choiceA`. May be rephrased for warmth / grammar. |
| **Choice B** | Single line, `Բ: <Armenian phrase>`. Preserves the meaning of the plan's `choiceB`. May be rephrased for warmth / grammar. |

**Not required in this manual experiment:**

- No machine footer (no `---\nCHOICE_A: ...\nCHOICE_B: ...` tail block — that's the production `TailBlockParser` shape, irrelevant when humans inspect raw output).
- No JSON wrapping. The writer outputs plain Armenian text.
- No metadata. The capturer (per `../SAMPLE_CAPTURE_TEMPLATE.md`) supplies metadata around the raw text.

A future tooling slice can ask the writer for tail-blocks or JSON; for the manual round in this experiment, plain Armenian text + the two prefix-marked choices is the expected shape.

## 5. Scoring rubric

Reuses the Areg rubric verbatim from
[`../README.md`](../README.md) and [`../SAMPLE_CAPTURE_TEMPLATE.md`](../SAMPLE_CAPTURE_TEMPLATE.md),
with one experiment-specific addition: **plan adherence**.

| Dimension | Score |
|---|---|
| Armenian naturalness | _ / 5 |
| Eastern Armenian correctness | _ / 5 |
| Fairy-tale feeling | _ / 5 |
| Warmth for age 4–7 | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| **Plan adherence** (does the prose use the plan's hero / friend / place / magicalObject / smallProblem and preserve choice meaning?) | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |
| Notes | free text |

A plan-to-story rendering that scores **≤ 2 / 5 on plan adherence** is treated as failing the experiment outright, regardless of how good the prose reads — the whole point of the director architecture is to constrain the writer.

## 6. Failure tags

Tick every tag that applies. Multi-tagging encouraged.

- [ ] translated / artificial Armenian
- [ ] weak fairy-tale feeling
- [ ] too short
- [ ] too long
- [ ] over-moralizing
- [ ] too babyish
- [ ] scary or too intense
- [ ] **ignored plan** (writer wrote a different story)
- [ ] **replaced hero / place / object / problem** (writer swapped a planned element)
- [ ] **choices changed too much** (semantic meaning of choiceA / choiceB lost)
- [ ] choices too similar to each other
- [ ] continuation-style recap instead of action
- [ ] unnatural Armenian phrase
- [ ] formatting issue (footer, English leak, missing `Ա:` / `Բ:` prefix, etc.)

The bolded three are **director-specific** failure modes — they
are the ones whose presence falsifies the F1 hypothesis (a writer
that ignores the plan provides no evidence that the director
architecture works).

## 7. Comparison rule

The plan-to-story output must be compared **against the existing
free-form OpenAI baseline**
(`../samples/openai-api-current-areg-baseline-story-20260501.md`),
NOT against the consumer-app Claude / Gemini samples. App samples
are ceiling-reference evidence per
[`../API_VS_APP_BAKEOFF_PLAN.md`](../API_VS_APP_BAKEOFF_PLAN.md)
§ 1; using them as a runtime-decision baseline conflates "model
ceiling" with "our integration quality" again.

The decision tree:

1. **If plan-to-story OUTPUT (any provider) clearly improves on
   the OpenAI free-form baseline on the rubric**, the director
   architecture is producing real signal. Tune the OpenAI prompt
   + parameters with the director's plan input *before*
   considering a runtime provider switch
   (`API_VS_APP_BAKEOFF_PLAN.md` § 6, rule 5).
2. **If OpenAI plan-to-story still fails BUT Claude API plan-to-
   story succeeds on the SAME plan and SAME writer prompt**, the
   provider test becomes meaningfully stronger evidence — same
   plan, same instructions, model is the only variable.
3. **If neither improves on the baseline**, the director
   architecture is not the answer (or the seed bank / plan shape
   needs more iteration). Do NOT proceed to runtime integration.

In all three cases, **no runtime change is gated by this single
experiment.** The decision rules in `API_VS_APP_BAKEOFF_PLAN.md`
§ 6 (≥ 10 case set, ≥ 2 reviewer passes, separate architecture
slice for any switch) still hold.

## 8. Next actions

In approximate order. Each is its own commit; none implies a
runtime change.

1. **Manual ceiling references first (no spend).** Pick 2–3 of
   the 7 plans and run them through the **Claude consumer app**
   and the **Gemini consumer app** with the writer prompt above.
   Capture each output using `SAMPLE_CAPTURE_TEMPLATE.md`. These
   are not runtime evidence; they bound the model ceiling.
2. **API runs when keys are available.** When `ANTHROPIC_API_KEY`
   is present in the environment, run the same plans through the
   F1.2 bake-off (`--run --provider claude
   --i-understand-live-cost --max-prompts <N>`) — but with the
   writer prompt rewired to take a plan rather than the current
   free-form `Պատմիր հեքիաթ` set. (That rewiring is its own tiny
   tooling slice; do NOT modify F1.2 in this evidence-only
   commit.) Run the same plans through the production OpenAI
   stack (one chat call per plan, manual capture). When
   `GEMINI_API_KEY` is added later, repeat for Gemini.
3. **Capture every run.** One file under `samples/` and one under
   `evaluations/` per (plan × provider) combination. Use
   `SAMPLE_CAPTURE_TEMPLATE.md`'s sections 1–4 for the raw
   sample, sections 5–8 for the rubric + decision.
4. **Hayk's native Armenian review.** This is the load-bearing
   step. Agent-drafted scores on the Armenian-naturalness
   dimensions are not enough for a runtime decision. The native-
   ear pass should at minimum cover the OpenAI free-form baseline
   AND every API plan-to-story output captured under (2).
5. **Aggregate findings.** Once at least one full provider has
   been run on all 7 plans (5 strong + 2 acceptable), summarize
   the rubric averages and the acceptable-vs-strong gap into a
   single follow-up evaluation file. The decision rules in
   `API_VS_APP_BAKEOFF_PLAN.md` § 6 trigger from THAT aggregate,
   not from individual plan-to-story samples.

---

## What this experiment deliberately does NOT do

- Touch `ChatService`, `appsettings.json`'s `SystemPrompt`, the
  production moderation pipeline, the audit feed, the parent
  dashboard, the firmware, the bake-off prompt set, the
  bake-off CSPROJ, the seed bank, or the generator script.
- Pre-pick a winning provider.
- Substitute agent scores for native-ear review.
- Imply a runtime switch path.

Each is reasonable to consider after the manual round produces
data. None is in scope for this evidence file.

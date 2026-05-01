# Plan-to-Story — first capture package (2 plans, 2026-05-01)

**Status:** evidence / capture preparation only. No production code
changes. No `ChatService` change. No provider integration. No
runtime model switch.

**Companion files:**
- [`plan-to-story-experiment-20260501.md`](./plan-to-story-experiment-20260501.md) — the experiment design (writer prompt template, output contract, rubric, failure tags, comparison rule, decision tree).
- [`story-plan-generator-review-20260501.md`](./story-plan-generator-review-20260501.md) — the 30-plan review that selected these two plans.
- [`story-plan-generator-review-20260501.plans.json`](./story-plan-generator-review-20260501.plans.json) — the load-bearing 30-plan JSON. Plan #8 and Plan #14 below are exact 1-indexed slices of it.
- [`../SAMPLE_CAPTURE_TEMPLATE.md`](../SAMPLE_CAPTURE_TEMPLATE.md) — the canonical capture template; this file is a focused two-plan instance of it.

---

## 1. Purpose

First small manual / consumer-app capture for the Story Director
plan-to-story experiment. Two plans only:

- **Plan #8** — strong (the highest-rated plan in the 30-plan review).
- **Plan #14** — acceptable / borderline (a load-bearing
  template-stretch case: the seed bank's `քնքուշ բարձիկ` triggers
  the palm-size-mismatch polish pattern flagged in the review).

The point is to compare apples-to-apples: same writer prompt,
same plan, multiple providers. Strong-plan and acceptable-plan
side-by-side tells us whether the writer can lift a borderline
plan into good Armenian prose, or whether weak plans yield weak
prose regardless of model.

This is **not production runtime**. App outputs are
**ceiling / reference evidence** — they show a model's possible
ceiling under the provider's hidden default prompt and tier-1
routing, not what our integration would deliver. They cannot
drive a runtime provider switch. (See
[`../API_VS_APP_BAKEOFF_PLAN.md`](../API_VS_APP_BAKEOFF_PLAN.md)
§ 1 for the architectural distinction.)

## 2. Exact selected plans

### Plan #8 (strong) — verbatim from the 30-plan JSON

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

### Plan #14 (acceptable / borderline) — verbatim from the 30-plan JSON

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

## 3. Copy-paste writer prompt — **Plan #8**

Paste this entire block (without the outer code-fence markers
shown for display) into Claude.app / Gemini.app / ChatGPT.app /
or the F1.2 bake-off when API is run later. The prompt embeds the
plan JSON inline; no `{{plan_json}}` substitution needed.

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
````

## 4. Copy-paste writer prompt — **Plan #14**

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
````

---

## 5. Capture slots

Each slot is a placeholder. Paste the model's response **verbatim**
into the "Raw output" code block. Strip nothing. The "Normalized
Areg output" block is the same prose minus any machine footers /
"As an AI" lines / wrapping markdown the writer might leak; if no
normalisation was needed, write `(no normalization applied — raw
output is what Areg would say)`.

`<TODO>` markers are explicit fill-in points. Do not delete them
until the slot is filled.

### 5.1 Plan #8 — Claude consumer app

| Field | Value |
|---|---|
| Provider | claude |
| Source | app (claude.ai) |
| Model label | `<TODO: e.g. "Claude Sonnet 4.6 (app default 2026-05-01)">` |
| Exact API model id | `(n/a — app session)` |
| Captured (UTC) | `<TODO: ISO-8601, e.g. 2026-05-01T19:00:00Z>` |
| Reviewer | `<TODO: Hayk>` |
| Decoding | `(unobservable — app default)` |
| System prompt | `(unobservable — app default + the writer prompt above)` |

**Raw output**

```
<TODO: paste Claude.app response verbatim>
```

**Normalized Areg output**

```
<TODO: same as raw, OR note "(no normalization applied)">
```

**Notes**

- `<TODO: free-form review text>`

**Rubric (manual scoring)**

| Dimension | Score |
|---|---|
| Armenian naturalness | _ / 5 |
| Eastern Armenian correctness | _ / 5 |
| Fairy-tale feeling | _ / 5 |
| Warmth for age 4–7 | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| **Plan adherence** | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

### 5.2 Plan #8 — Gemini consumer app

| Field | Value |
|---|---|
| Provider | gemini |
| Source | app (gemini.google.com) |
| Model label | `<TODO>` |
| Exact API model id | `(n/a — app session)` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `(unobservable — app default)` |
| System prompt | `(unobservable — app default + the writer prompt above)` |

**Raw output**

```
<TODO>
```

**Normalized Areg output**

```
<TODO>
```

**Notes**

- `<TODO>`

**Rubric**

| Dimension | Score |
|---|---|
| Armenian naturalness | _ / 5 |
| Eastern Armenian correctness | _ / 5 |
| Fairy-tale feeling | _ / 5 |
| Warmth for age 4–7 | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

### 5.3 Plan #8 — ChatGPT / OpenAI consumer app

| Field | Value |
|---|---|
| Provider | openai |
| Source | app (chatgpt.com) |
| Model label | `<TODO>` |
| Exact API model id | `(n/a — app session)` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `(unobservable — app default)` |
| System prompt | `(unobservable — app default + the writer prompt above)` |

**Raw output**

```
<TODO>
```

**Normalized Areg output**

```
<TODO>
```

**Notes**

- `<TODO>`

**Rubric**

| Dimension | Score |
|---|---|
| Armenian naturalness | _ / 5 |
| Eastern Armenian correctness | _ / 5 |
| Fairy-tale feeling | _ / 5 |
| Warmth for age 4–7 | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

### 5.4 Plan #8 — API output (later)

| Field | Value |
|---|---|
| Provider | `<TODO: openai \| claude \| gemini>` |
| Source | api |
| Model label | `<TODO>` |
| Exact API model id | `<TODO: e.g. claude-opus-4-7, gpt-4o, gemini-2.5-pro>` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `<TODO: temperature=…, max_tokens=…>` |
| System prompt | `<TODO: writer prompt above (sha256 …)>` |

**Raw output**

```
<TODO>
```

**Normalized Areg output**

```
<TODO>
```

**Notes**

- `<TODO>`

**Rubric**

| Dimension | Score |
|---|---|
| Armenian naturalness | _ / 5 |
| Eastern Armenian correctness | _ / 5 |
| Fairy-tale feeling | _ / 5 |
| Warmth for age 4–7 | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

---

### 5.5 Plan #14 — Claude consumer app

| Field | Value |
|---|---|
| Provider | claude |
| Source | app (claude.ai) |
| Model label | `<TODO>` |
| Exact API model id | `(n/a — app session)` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `(unobservable — app default)` |
| System prompt | `(unobservable — app default + the writer prompt above)` |

**Raw output**

```
<TODO>
```

**Normalized Areg output**

```
<TODO>
```

**Notes**

- `<TODO: did the writer keep "քնքուշ բարձիկ" or paraphrase it? scale-fix?>`

**Rubric**

| Dimension | Score |
|---|---|
| Armenian naturalness | _ / 5 |
| Eastern Armenian correctness | _ / 5 |
| Fairy-tale feeling | _ / 5 |
| Warmth for age 4–7 | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

### 5.6 Plan #14 — Gemini consumer app

| Field | Value |
|---|---|
| Provider | gemini |
| Source | app (gemini.google.com) |
| Model label | `<TODO>` |
| Exact API model id | `(n/a — app session)` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `(unobservable — app default)` |
| System prompt | `(unobservable — app default + the writer prompt above)` |

**Raw output**

```
<TODO>
```

**Normalized Areg output**

```
<TODO>
```

**Notes**

- `<TODO>`

**Rubric**

| Dimension | Score |
|---|---|
| Armenian naturalness | _ / 5 |
| Eastern Armenian correctness | _ / 5 |
| Fairy-tale feeling | _ / 5 |
| Warmth for age 4–7 | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

### 5.7 Plan #14 — ChatGPT / OpenAI consumer app

| Field | Value |
|---|---|
| Provider | openai |
| Source | app (chatgpt.com) |
| Model label | `<TODO>` |
| Exact API model id | `(n/a — app session)` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `(unobservable — app default)` |
| System prompt | `(unobservable — app default + the writer prompt above)` |

**Raw output**

```
<TODO>
```

**Normalized Areg output**

```
<TODO>
```

**Notes**

- `<TODO>`

**Rubric**

| Dimension | Score |
|---|---|
| Armenian naturalness | _ / 5 |
| Eastern Armenian correctness | _ / 5 |
| Fairy-tale feeling | _ / 5 |
| Warmth for age 4–7 | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

### 5.8 Plan #14 — API output (later)

| Field | Value |
|---|---|
| Provider | `<TODO: openai \| claude \| gemini>` |
| Source | api |
| Model label | `<TODO>` |
| Exact API model id | `<TODO>` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `<TODO>` |
| System prompt | `<TODO>` |

**Raw output**

```
<TODO>
```

**Normalized Areg output**

```
<TODO>
```

**Notes**

- `<TODO>`

**Rubric**

| Dimension | Score |
|---|---|
| Armenian naturalness | _ / 5 |
| Eastern Armenian correctness | _ / 5 |
| Fairy-tale feeling | _ / 5 |
| Warmth for age 4–7 | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

---

## 7. Reminder

> **Read this before scoring.** Every slot above is structurally
> identical so the comparison stays honest.
>
> - **Do not judge provider runtime from app-only output.**
>   App sessions use the provider's hidden default system prompt
>   and tier-1 routing. Quality observed in the app is the
>   *upper bound* of what the model's weights can produce, not
>   the lower bound of what our integration would deliver.
> - **We are first testing whether plan-conditioned writing
>   improves story quality**, not which provider wins. The
>   per-slot rubric scores feed into that question; the
>   provider-vs-provider question is a separate slice.
> - **Hayk's native Armenian review is required before any
>   runtime decision.** Agent-drafted scores cover what is
>   structurally observable (length, pacing, plan adherence,
>   safety) but cannot judge Eastern-Armenian-correctness or
>   warm-fairy-tale-feeling at the level a native ear can.
> - **The decision rules in
>   [`../API_VS_APP_BAKEOFF_PLAN.md`](../API_VS_APP_BAKEOFF_PLAN.md)
>   § 6 still hold:** no runtime switch from 1–2 samples; no
>   runtime switch from app-only samples; production switch is a
>   separate architecture slice.

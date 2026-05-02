# Plan-to-Story — four-profile capture (1 plan per age, 2026-05-01)

**Status:** evidence / capture preparation only. No production code
changes. No `ChatService` change. No model / API call. No runtime
model switch.

**Companion files:**
- [`plan-to-story-render-candidates-20260501.md`](./plan-to-story-render-candidates-20260501.md) — the 12 render candidates from which these 4 are exact slices.
- [`story-plan-age-profile-review-20260501.md`](./story-plan-age-profile-review-20260501.md) — the 120-plan batch review whose top-3 picks per profile produced this short list.
- [`generated-plans-age-4-simple-20260501.json`](./generated-plans-age-4-simple-20260501.json), [`-age-5-balanced-`](./generated-plans-age-5-balanced-20260501.json), [`-age-6-story-rich-`](./generated-plans-age-6-story-rich-20260501.json), [`-age-7-richer-`](./generated-plans-age-7-richer-20260501.json) — the four 30-plan source batches.
- [`../SAMPLE_CAPTURE_TEMPLATE.md`](../SAMPLE_CAPTURE_TEMPLATE.md) — the canonical capture form; this file is a focused four-plan instance of it.

---

## 1. Purpose

First focused render capture across all four age profiles. One
**strong** plan per profile (the top pick from each profile's
top-3 in the 120-plan review).

The point is to test the **age-profile-driven Story Director
output** end-to-end, apples-to-apples: same plan shape, same
writer-prompt skeleton, varied only by the inlined plan + its
`ageToneProfile`. Each row shows the writer how to scale
sentence length, word choice, and word count without changing
the underlying story palette.

This is **manual / consumer-app capture first**. App outputs are
**ceiling / reference evidence**, not runtime-switch evidence.
They show what the model's weights can produce under the
provider's hidden default prompt; they do NOT prove our API
integration would deliver the same. (See
[`../API_VS_APP_BAKEOFF_PLAN.md`](../API_VS_APP_BAKEOFF_PLAN.md)
§ 1.) API runs come later, with the same plans and the same
writer prompts.

## 2. Selected plans

### Plan A — age-4-simple #17 (strong)

**Why selected:** native Armenian fauna (շնիկ + շուն), apple-
orchard place, dewdrop leaf object that fits the inspection
template cleanly. Among the simplest combinations in the
top-3, ideal for testing whether a writer can stay terse and
warm at the age-4 target word count.

```json
{
  "hero": "շնիկ",
  "heroTrait": "հնարամիտ",
  "friendOrGuide": "շուն",
  "relationship": "տատիկը պատմում է հին պատմություն",
  "place": "խնձորենու այգի",
  "mood": "հիշատակային ու տաք",
  "magicalObject": "ցողի կաթիլներով տերև",
  "smallProblem": "արագիլը չի գտնում հանգստանալու տեղը",
  "conflictType": "աստղն ընկել է մի անհայտ տեղ",
  "goal": "գտնել տան ճանապարհը",
  "resolutionStyle": "լուծումը գալիս է փոքրիկ նվեր մատուցելուց",
  "sensoryDetails": [
    "հասած դեղձի թավշյա մաշկ",
    "արևի տաք շող"
  ],
  "ageToneProfile": {
    "label": "age-4-simple",
    "ageRange": "4-5",
    "sentenceStyle": "կարճ և պարզ նախադասություններ",
    "wordChoice": "շատ պարզ, առանց բարդ փոխաբերությունների",
    "targetWords": "90-130"
  },
  "choiceAType": "փորձել մեղմ գործողություն",
  "choiceBType": "գնալ դեպի վայր",
  "choiceA": "մոտեցնել ցողի կաթիլներով տերևը լույսին",
  "choiceB": "գնալ դեպի խնձորենու այգի"
}
```

### Plan B — age-5-balanced #3 (strong)

**Why selected:** cricket + dragonfly + old bridge + rosy
pearl. Small heroes, safe place, palm-friendly object, and the
inspection template fits the pearl perfectly. Tests whether
the writer can hold the balanced tone (slightly richer
sentences, small metaphors) without sliding into babyish
diminutives.

```json
{
  "hero": "ծղրիդ",
  "heroTrait": "անշտապ",
  "friendOrGuide": "ճպուռ",
  "relationship": "հերոսը մխիթարում է վախեցած կերպարին",
  "place": "հին կամուրջ",
  "mood": "քնքուշ ու հանգիստ",
  "magicalObject": "վարդագույն մարգարիտ",
  "smallProblem": "գորտուկը մոռացել է ցատկելու երգը",
  "conflictType": "ինչ-որ բան լռել է",
  "goal": "վերադարձնել տերևները իրենց ծառին",
  "resolutionStyle": "լուծումը գալիս է ընկերոջ հետ միասին փորձելուց",
  "sensoryDetails": [
    "նոր թխված գաթայի բույր",
    "մեղմ առվակի ձայն"
  ],
  "ageToneProfile": {
    "label": "age-5-balanced",
    "ageRange": "5-6",
    "sentenceStyle": "կարճից միջին երկարության նախադասություններ",
    "wordChoice": "պարզ, փոքր փոխաբերություններով",
    "targetWords": "120-160"
  },
  "choiceAType": "գնալ դեպի վայր",
  "choiceBType": "փորձել մեղմ գործողություն",
  "choiceA": "քայլել դեպի հին կամուրջ",
  "choiceB": "մոտեցնել վարդագույն մարգարիտը լույսին"
}
```

### Plan C — age-6-story-rich #20 (strong)

**Why selected:** bird + ant in the apple orchard with a silver
thin twig. Native fauna + native place + shiny object aligning
with the inspection template. Tests whether the writer can hold
the story-rich tone (medium sentences, narrative rhythm) on a
gentle small problem ("the spring stopped speaking") without
reaching for melodrama.

```json
{
  "hero": "ծիտիկ",
  "heroTrait": "համարձակ",
  "friendOrGuide": "մրջյուն",
  "relationship": "հերոսը կիսում է ուտելիքը մի փոքրիկի հետ",
  "place": "խնձորենու այգի",
  "mood": "ձմեռային մեղմ",
  "magicalObject": "արծաթե բարակ ճյուղ",
  "smallProblem": "աղբյուրը դադարել է խոսել",
  "conflictType": "լույսը թաքնվել է",
  "goal": "գտնել մոլորված ընկերոջը",
  "resolutionStyle": "լուծումը գալիս է մեղմ ձայնով խոսելուց",
  "sensoryDetails": [
    "մեղվի մեղմ բզզոց",
    "թոնիրի տաք հացի հոտ"
  ],
  "ageToneProfile": {
    "label": "age-6-story-rich",
    "ageRange": "6-7",
    "sentenceStyle": "միջին երկարության նախադասություններ, թեթև պատմողական ռիթմ",
    "wordChoice": "պարզ բառապաշար, պատմողական մթնոլորտ",
    "targetWords": "150-200"
  },
  "choiceAType": "գնալ դեպի վայր",
  "choiceBType": "փորձել մեղմ գործողություն",
  "choiceA": "գնալ դեպի խնձորենու այգի",
  "choiceB": "մոտեցնել արծաթե բարակ ճյուղը լույսին"
}
```

### Plan D — age-7-richer #6 (strong)

**Why selected:** swallow + cat in a dreamy meadow with a
moondust bag. The richest pairing in the top-3 — openable
mythical object + carrier-bird hero + classic Armenian
fairy-tale "send greeting to a faraway friend" goal. Tests
whether the writer can lean into light poetry at the age-7
target word count without breaking child-safe register.

```json
{
  "hero": "ծիծեռնակ",
  "heroTrait": "համբերատար",
  "friendOrGuide": "կատու",
  "relationship": "մեծ իմաստուն կերպարը խորհուրդ է տալիս",
  "place": "երազային բացատ",
  "mood": "ջերմ ու մեղմ",
  "magicalObject": "լուսնի փոշիով լի տոպրակ",
  "smallProblem": "ծառը չի կարող արթնանալ",
  "conflictType": "ինչ-որ բան լռել է",
  "goal": "ուղարկել ողջույն հեռավոր ընկերոջը",
  "resolutionStyle": "լուծումը գալիս է ճիշտ ճանապարհն ընտրելուց",
  "sensoryDetails": [
    "չոր տերևների խշշոց",
    "թաց մամուռի բույր"
  ],
  "ageToneProfile": {
    "label": "age-7-richer",
    "ageRange": "7+",
    "sentenceStyle": "մի փոքր ավելի երկար նախադասություններ, թեթև բանաստեղծականություն",
    "wordChoice": "պարզ բառեր, բայց ավելի հարուստ մթնոլորտով",
    "targetWords": "180-250"
  },
  "choiceAType": "գնալ դեպի վայր",
  "choiceBType": "փորձել մեղմ գործողություն",
  "choiceA": "գնալ դեպի երազային բացատ",
  "choiceB": "մոտեցնել լուսնի փոշիով լի տոպրակը լույսին"
}
```

## 3. Ready-to-paste writer prompts

The four prompts share a single skeleton. Each block below has
the full prompt text inlined with that plan's JSON, ready to
paste into Claude.app / Gemini.app / ChatGPT.app or into the
F1.2 bake-off when the API run lands.

### 3A. Writer prompt — Plan A (age-4-simple)

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
- You MUST NOT replace the plan's hero, heroTrait, friendOrGuide,
  relationship, place, mood, magicalObject, smallProblem,
  conflictType, goal, or resolutionStyle. The mood and goal must
  visibly shape the prose.
- The two final choices MUST preserve the meaning of the plan's
  choiceA and choiceB. You may rephrase them for grammar and
  warmth, but they must point to the same actions.
- The two sensoryDetails should appear naturally in the prose
  (you may rephrase them; do not replace them with unrelated
  imagery).

LENGTH AND PACING (age-4-simple — pinned by ageToneProfile)
- Story body: 90–130 Armenian words (NOT counting the two choices).
- Short and simple sentences.
- Word choice: very simple, no complex metaphors.
- Concrete sensory writing. No abstract emotional summary.

SAFETY AND TONE — ABSOLUTE
- No violence, no weapons, no horror, no scary danger, no death,
  no abandonment, no medical or scary illness.
- No moral lecture. Show through action, never state a moral.
- Not babyish. The child is 4–5, not 2.
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
  "hero": "շնիկ",
  "heroTrait": "հնարամիտ",
  "friendOrGuide": "շուն",
  "relationship": "տատիկը պատմում է հին պատմություն",
  "place": "խնձորենու այգի",
  "mood": "հիշատակային ու տաք",
  "magicalObject": "ցողի կաթիլներով տերև",
  "smallProblem": "արագիլը չի գտնում հանգստանալու տեղը",
  "conflictType": "աստղն ընկել է մի անհայտ տեղ",
  "goal": "գտնել տան ճանապարհը",
  "resolutionStyle": "լուծումը գալիս է փոքրիկ նվեր մատուցելուց",
  "sensoryDetails": [
    "հասած դեղձի թավշյա մաշկ",
    "արևի տաք շող"
  ],
  "ageToneProfile": {
    "label": "age-4-simple",
    "ageRange": "4-5",
    "sentenceStyle": "կարճ և պարզ նախադասություններ",
    "wordChoice": "շատ պարզ, առանց բարդ փոխաբերությունների",
    "targetWords": "90-130"
  },
  "choiceAType": "փորձել մեղմ գործողություն",
  "choiceBType": "գնալ դեպի վայր",
  "choiceA": "մոտեցնել ցողի կաթիլներով տերևը լույսին",
  "choiceB": "գնալ դեպի խնձորենու այգի"
}
````

### 3B. Writer prompt — Plan B (age-5-balanced)

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
- You MUST NOT replace the plan's hero, heroTrait, friendOrGuide,
  relationship, place, mood, magicalObject, smallProblem,
  conflictType, goal, or resolutionStyle. The mood and goal must
  visibly shape the prose.
- The two final choices MUST preserve the meaning of the plan's
  choiceA and choiceB. You may rephrase them for grammar and
  warmth, but they must point to the same actions.
- The two sensoryDetails should appear naturally in the prose
  (you may rephrase them; do not replace them with unrelated
  imagery).

LENGTH AND PACING (age-5-balanced — pinned by ageToneProfile)
- Story body: 120–160 Armenian words (NOT counting the two choices).
- Short-to-medium sentences.
- Word choice: simple with small metaphors allowed.
- Concrete sensory writing. No abstract emotional summary.

SAFETY AND TONE — ABSOLUTE
- No violence, no weapons, no horror, no scary danger, no death,
  no abandonment, no medical or scary illness.
- No moral lecture. Show through action, never state a moral.
- Not babyish. The child is 5–6, not 2.
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
  "hero": "ծղրիդ",
  "heroTrait": "անշտապ",
  "friendOrGuide": "ճպուռ",
  "relationship": "հերոսը մխիթարում է վախեցած կերպարին",
  "place": "հին կամուրջ",
  "mood": "քնքուշ ու հանգիստ",
  "magicalObject": "վարդագույն մարգարիտ",
  "smallProblem": "գորտուկը մոռացել է ցատկելու երգը",
  "conflictType": "ինչ-որ բան լռել է",
  "goal": "վերադարձնել տերևները իրենց ծառին",
  "resolutionStyle": "լուծումը գալիս է ընկերոջ հետ միասին փորձելուց",
  "sensoryDetails": [
    "նոր թխված գաթայի բույր",
    "մեղմ առվակի ձայն"
  ],
  "ageToneProfile": {
    "label": "age-5-balanced",
    "ageRange": "5-6",
    "sentenceStyle": "կարճից միջին երկարության նախադասություններ",
    "wordChoice": "պարզ, փոքր փոխաբերություններով",
    "targetWords": "120-160"
  },
  "choiceAType": "գնալ դեպի վայր",
  "choiceBType": "փորձել մեղմ գործողություն",
  "choiceA": "քայլել դեպի հին կամուրջ",
  "choiceB": "մոտեցնել վարդագույն մարգարիտը լույսին"
}
````

### 3C. Writer prompt — Plan C (age-6-story-rich)

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
- You MUST NOT replace the plan's hero, heroTrait, friendOrGuide,
  relationship, place, mood, magicalObject, smallProblem,
  conflictType, goal, or resolutionStyle. The mood and goal must
  visibly shape the prose.
- The two final choices MUST preserve the meaning of the plan's
  choiceA and choiceB. You may rephrase them for grammar and
  warmth, but they must point to the same actions.
- The two sensoryDetails should appear naturally in the prose
  (you may rephrase them; do not replace them with unrelated
  imagery).

LENGTH AND PACING (age-6-story-rich — pinned by ageToneProfile)
- Story body: 150–200 Armenian words (NOT counting the two choices).
- Medium sentences with a light narrative rhythm.
- Word choice: simple vocabulary in a story-telling register.
- Concrete sensory writing. No abstract emotional summary.

SAFETY AND TONE — ABSOLUTE
- No violence, no weapons, no horror, no scary danger, no death,
  no abandonment, no medical or scary illness.
- No moral lecture. Show through action, never state a moral.
- Not babyish. The child is 6–7, not 2.
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
  "hero": "ծիտիկ",
  "heroTrait": "համարձակ",
  "friendOrGuide": "մրջյուն",
  "relationship": "հերոսը կիսում է ուտելիքը մի փոքրիկի հետ",
  "place": "խնձորենու այգի",
  "mood": "ձմեռային մեղմ",
  "magicalObject": "արծաթե բարակ ճյուղ",
  "smallProblem": "աղբյուրը դադարել է խոսել",
  "conflictType": "լույսը թաքնվել է",
  "goal": "գտնել մոլորված ընկերոջը",
  "resolutionStyle": "լուծումը գալիս է մեղմ ձայնով խոսելուց",
  "sensoryDetails": [
    "մեղվի մեղմ բզզոց",
    "թոնիրի տաք հացի հոտ"
  ],
  "ageToneProfile": {
    "label": "age-6-story-rich",
    "ageRange": "6-7",
    "sentenceStyle": "միջին երկարության նախադասություններ, թեթև պատմողական ռիթմ",
    "wordChoice": "պարզ բառապաշար, պատմողական մթնոլորտ",
    "targetWords": "150-200"
  },
  "choiceAType": "գնալ դեպի վայր",
  "choiceBType": "փորձել մեղմ գործողություն",
  "choiceA": "գնալ դեպի խնձորենու այգի",
  "choiceB": "մոտեցնել արծաթե բարակ ճյուղը լույսին"
}
````

### 3D. Writer prompt — Plan D (age-7-richer)

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
- You MUST NOT replace the plan's hero, heroTrait, friendOrGuide,
  relationship, place, mood, magicalObject, smallProblem,
  conflictType, goal, or resolutionStyle. The mood and goal must
  visibly shape the prose.
- The two final choices MUST preserve the meaning of the plan's
  choiceA and choiceB. You may rephrase them for grammar and
  warmth, but they must point to the same actions.
- The two sensoryDetails should appear naturally in the prose
  (you may rephrase them; do not replace them with unrelated
  imagery).

LENGTH AND PACING (age-7-richer — pinned by ageToneProfile)
- Story body: 180–250 Armenian words (NOT counting the two choices).
- Slightly longer sentences with a light poetic touch.
- Word choice: simple words used in a richer atmospheric register.
- Concrete sensory writing. No abstract emotional summary.

SAFETY AND TONE — ABSOLUTE
- No violence, no weapons, no horror, no scary danger, no death,
  no abandonment, no medical or scary illness.
- No moral lecture. Show through action, never state a moral.
- Not babyish. The child is 7+, but still a child.
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
  "hero": "ծիծեռնակ",
  "heroTrait": "համբերատար",
  "friendOrGuide": "կատու",
  "relationship": "մեծ իմաստուն կերպարը խորհուրդ է տալիս",
  "place": "երազային բացատ",
  "mood": "ջերմ ու մեղմ",
  "magicalObject": "լուսնի փոշիով լի տոպրակ",
  "smallProblem": "ծառը չի կարող արթնանալ",
  "conflictType": "ինչ-որ բան լռել է",
  "goal": "ուղարկել ողջույն հեռավոր ընկերոջը",
  "resolutionStyle": "լուծումը գալիս է ճիշտ ճանապարհն ընտրելուց",
  "sensoryDetails": [
    "չոր տերևների խշշոց",
    "թաց մամուռի բույր"
  ],
  "ageToneProfile": {
    "label": "age-7-richer",
    "ageRange": "7+",
    "sentenceStyle": "մի փոքր ավելի երկար նախադասություններ, թեթև բանաստեղծականություն",
    "wordChoice": "պարզ բառեր, բայց ավելի հարուստ մթնոլորտով",
    "targetWords": "180-250"
  },
  "choiceAType": "գնալ դեպի վայր",
  "choiceBType": "փորձել մեղմ գործողություն",
  "choiceA": "գնալ դեպի երազային բացատ",
  "choiceB": "մոտեցնել լուսնի փոշիով լի տոպրակը լույսին"
}
````

---

## 4. Capture slots

Each slot is a placeholder. Paste the model's response **verbatim**
into the "Raw output" code block. Strip nothing. The "Normalized
Areg output" block is the same prose minus any machine footers /
"As an AI" lines / wrapping markdown the writer might leak; if no
normalisation was needed, write `(no normalization applied — raw
output is what Areg would say)`.

`<TODO>` markers are explicit fill-in points. Do not delete them
until the slot is filled.

The compact rubric block under each slot is the same nine-row
Areg rubric used elsewhere, with two experiment-specific
additions: **age-profile fit** (does the prose match the
selected ageToneProfile's sentence style, word choice, and
target word count?) and **plan adherence** (carried over from
`plan-to-story-experiment-20260501.md`).

### 4A. Plan A (age-4-simple) — Claude consumer app

| Field | Value |
|---|---|
| Provider | claude |
| Source | app (claude.ai) |
| Model label | `<TODO>` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `(unobservable — app default)` |

**Raw output**

```
<TODO>
```

**Normalized Areg output**

```
<TODO>
```

**Notes**

- `<TODO: did the writer keep the age-4-simple sentence style? word count in 90–130 range?>`

**Rubric**

| Dimension | Score |
|---|---|
| Armenian naturalness | _ / 5 |
| Eastern Armenian correctness | _ / 5 |
| Fairy-tale feeling | _ / 5 |
| Warmth for age 4–5 | _ / 5 |
| Age-profile fit (age-4-simple) | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

### 4A. Plan A (age-4-simple) — Gemini consumer app

| Field | Value |
|---|---|
| Provider | gemini |
| Source | app (gemini.google.com) |
| Model label | `<TODO>` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `(unobservable — app default)` |

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
| Warmth for age 4–5 | _ / 5 |
| Age-profile fit (age-4-simple) | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

### 4A. Plan A (age-4-simple) — ChatGPT / OpenAI consumer app

| Field | Value |
|---|---|
| Provider | openai |
| Source | app (chatgpt.com) |
| Model label | `<TODO>` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `(unobservable — app default)` |

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
| Warmth for age 4–5 | _ / 5 |
| Age-profile fit (age-4-simple) | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

### 4A. Plan A (age-4-simple) — API output (later)

| Field | Value |
|---|---|
| Provider | `<TODO: openai \| claude \| gemini>` |
| Source | api |
| Exact API model id | `<TODO>` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `<TODO: temperature=…, max_tokens=…>` |
| System prompt | `<TODO: writer prompt 3A above (sha256 …)>` |

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
| Warmth for age 4–5 | _ / 5 |
| Age-profile fit (age-4-simple) | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

---

### 4B. Plan B (age-5-balanced) — Claude consumer app

| Field | Value |
|---|---|
| Provider | claude |
| Source | app (claude.ai) |
| Model label | `<TODO>` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `(unobservable — app default)` |

**Raw output**

```
<TODO>
```

**Normalized Areg output**

```
<TODO>
```

**Notes**

- `<TODO: did the writer keep the age-5-balanced sentence style? word count in 120–160 range?>`

**Rubric**

| Dimension | Score |
|---|---|
| Armenian naturalness | _ / 5 |
| Eastern Armenian correctness | _ / 5 |
| Fairy-tale feeling | _ / 5 |
| Warmth for age 5–6 | _ / 5 |
| Age-profile fit (age-5-balanced) | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

### 4B. Plan B (age-5-balanced) — Gemini consumer app

| Field | Value |
|---|---|
| Provider | gemini |
| Source | app (gemini.google.com) |
| Model label | `<TODO>` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `(unobservable — app default)` |

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
| Warmth for age 5–6 | _ / 5 |
| Age-profile fit (age-5-balanced) | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

### 4B. Plan B (age-5-balanced) — ChatGPT / OpenAI consumer app

| Field | Value |
|---|---|
| Provider | openai |
| Source | app (chatgpt.com) |
| Model label | `<TODO>` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `(unobservable — app default)` |

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
| Warmth for age 5–6 | _ / 5 |
| Age-profile fit (age-5-balanced) | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

### 4B. Plan B (age-5-balanced) — API output (later)

| Field | Value |
|---|---|
| Provider | `<TODO: openai \| claude \| gemini>` |
| Source | api |
| Exact API model id | `<TODO>` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `<TODO>` |
| System prompt | `<TODO: writer prompt 3B above (sha256 …)>` |

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
| Warmth for age 5–6 | _ / 5 |
| Age-profile fit (age-5-balanced) | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

---

### 4C. Plan C (age-6-story-rich) — Claude consumer app

| Field | Value |
|---|---|
| Provider | claude |
| Source | app (claude.ai) |
| Model label | `<TODO>` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `(unobservable — app default)` |

**Raw output**

```
<TODO>
```

**Normalized Areg output**

```
<TODO>
```

**Notes**

- `<TODO: did the writer keep the age-6-story-rich rhythm? word count in 150–200 range?>`

**Rubric**

| Dimension | Score |
|---|---|
| Armenian naturalness | _ / 5 |
| Eastern Armenian correctness | _ / 5 |
| Fairy-tale feeling | _ / 5 |
| Warmth for age 6–7 | _ / 5 |
| Age-profile fit (age-6-story-rich) | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

### 4C. Plan C (age-6-story-rich) — Gemini consumer app

| Field | Value |
|---|---|
| Provider | gemini |
| Source | app (gemini.google.com) |
| Model label | `<TODO>` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `(unobservable — app default)` |

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
| Warmth for age 6–7 | _ / 5 |
| Age-profile fit (age-6-story-rich) | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

### 4C. Plan C (age-6-story-rich) — ChatGPT / OpenAI consumer app

| Field | Value |
|---|---|
| Provider | openai |
| Source | app (chatgpt.com) |
| Model label | `<TODO>` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `(unobservable — app default)` |

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
| Warmth for age 6–7 | _ / 5 |
| Age-profile fit (age-6-story-rich) | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

### 4C. Plan C (age-6-story-rich) — API output (later)

| Field | Value |
|---|---|
| Provider | `<TODO: openai \| claude \| gemini>` |
| Source | api |
| Exact API model id | `<TODO>` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `<TODO>` |
| System prompt | `<TODO: writer prompt 3C above (sha256 …)>` |

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
| Warmth for age 6–7 | _ / 5 |
| Age-profile fit (age-6-story-rich) | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

---

### 4D. Plan D (age-7-richer) — Claude consumer app

| Field | Value |
|---|---|
| Provider | claude |
| Source | app (claude.ai) |
| Model label | `<TODO>` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `(unobservable — app default)` |

**Raw output**

```
<TODO>
```

**Normalized Areg output**

```
<TODO>
```

**Notes**

- `<TODO: did the writer hold light-poetic register without breaking child-safe? word count in 180–250 range?>`

**Rubric**

| Dimension | Score |
|---|---|
| Armenian naturalness | _ / 5 |
| Eastern Armenian correctness | _ / 5 |
| Fairy-tale feeling | _ / 5 |
| Warmth for age 7+ | _ / 5 |
| Age-profile fit (age-7-richer) | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

### 4D. Plan D (age-7-richer) — Gemini consumer app

| Field | Value |
|---|---|
| Provider | gemini |
| Source | app (gemini.google.com) |
| Model label | `<TODO>` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `(unobservable — app default)` |

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
| Warmth for age 7+ | _ / 5 |
| Age-profile fit (age-7-richer) | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

### 4D. Plan D (age-7-richer) — ChatGPT / OpenAI consumer app

| Field | Value |
|---|---|
| Provider | openai |
| Source | app (chatgpt.com) |
| Model label | `<TODO>` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `(unobservable — app default)` |

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
| Warmth for age 7+ | _ / 5 |
| Age-profile fit (age-7-richer) | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

### 4D. Plan D (age-7-richer) — API output (later)

| Field | Value |
|---|---|
| Provider | `<TODO: openai \| claude \| gemini>` |
| Source | api |
| Exact API model id | `<TODO>` |
| Captured (UTC) | `<TODO>` |
| Reviewer | `<TODO>` |
| Decoding | `<TODO>` |
| System prompt | `<TODO: writer prompt 3D above (sha256 …)>` |

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
| Warmth for age 7+ | _ / 5 |
| Age-profile fit (age-7-richer) | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

---

## 5. Reminder

> **Read this before scoring any slot.**
>
> - **Do not infer a runtime provider decision from app-only
>   output.** App sessions use the provider's hidden default
>   system prompt and tier-1 routing. Whatever quality you
>   observe is the *upper bound* of what those weights can
>   produce, not the lower bound of what our integration would
>   deliver.
> - **Compare app outputs as ceiling / reference only.** They
>   tell you whether each provider's underlying model *can*
>   render Areg's age-profile-shaped Armenian, not whether *our*
>   integration will.
> - **API outputs come later, with the same plans and the same
>   writer prompts.** That's the apples-to-apples comparison
>   that feeds the decision rules in
>   [`../API_VS_APP_BAKEOFF_PLAN.md`](../API_VS_APP_BAKEOFF_PLAN.md)
>   § 6.
> - **Hayk's native Armenian review is required before any
>   runtime decision.** Agent-drafted scores can flag obvious
>   translation-shape but cannot vouch for native fluency or
>   case-marking idiom.
> - **Plan adherence ≤ 2 / 5 fails the experiment outright.**
>   The director hypothesis stands or falls on whether writers
>   honour the plan, not on prose quality alone.

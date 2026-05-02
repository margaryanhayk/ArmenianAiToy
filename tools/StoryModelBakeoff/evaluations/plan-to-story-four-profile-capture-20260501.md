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
| Model label | Claude (consumer app default at capture time) |
| Captured (UTC) | 2026-05-01 (local capture timestamp ≈ 22:22) |
| Reviewer | Hayk |
| Decoding | (unobservable — app default) |

**Raw output**

Verbatim, including the duplicated opening sentence-pair at the
start of each continuation (a Claude.app render artefact, noted
under Weakness 2 below):

```
Մի անգամ, շատ վաղուց, խնձորենու այգում ապրում էր մի փոքրիկ շնիկ։ Նրա անունը Բարիկ էր։ Բարիկը շատ հնարամիտ էր։
Մի տաք օր տատիկ շունը նստել էր ծառի տակ։ Արևի շողը մեղմ տաքացնում էր նրա մորթին։ Կողքին հասած դեղձ կար՝ թավշյա մաշկով։
— Արի, պատմեմ քեզ հին պատմություն,— ասաց տատիկը։— Մի օր երկնքից մի փոքրիկ աստղ ընկավ։ Ոչ ոք չգիտեր՝ ուր։
Հենց այդ պահին երկնքից իջավ մի արագիլ։ Նա հոգնած էր ու տխուր։
— Ես չեմ գտնում հանգստանալու տեղը,— ասաց արագիլը։— Չգիտեմ՝ ինչպես տուն գնամ։
Բարիկը մտածեց։ Նա տեսավ մի փոքրիկ տերև։ Տերևի վրա փայլում էին ցողի կաթիլները՝ ճիշտ ինչպես աստղիկներ։
— Կարող եմ քեզ մի փոքրիկ նվեր տալ,— ասաց Բարիկը։— Թերևս սա ճանապարհ ցույց տա։
Արագիլը ժպտաց։ Բարիկը մտածում է, թե ինչ անի հիմա։
Ա) Մոտեցնել ցողի կաթիլներով տերևը լույսին
Բ) Գնալ դեպի խնձորենու այգի

Continuation after A:
Բարիկը մեղմ վերցրեց տերևը։ Նա բարձրացրեց այն դեպի արևի շողը։Բարիկը մեղմ վերցրեց տերևը։ Նա բարձրացրեց այն դեպի արևի շողը։
Հանկարծ ցողի կաթիլները սկսեցին փայլել։ Ամեն կաթիլի մեջ մի փոքրիկ լույս երևաց։ Կաթիլները փայլում էին ճիշտ ինչպես աստղեր։
— Օհ,— շշնջաց արագիլը։— Սրանք նման են իմ տան աստղերին։
Տերևի լույսերը ցույց տվեցին մի ճանապարհ՝ դեպի երկինք։ Արագիլը ուրախացավ։ Նրա սիրտը տաքացավ։
— Շնորհակալ եմ, փոքրիկ Բարիկ,— ասաց արագիլը։— Քո նվերը ինձ տուն կտանի։
Արագիլը մեղմ թափահարեց թևերը։ Նա բարձրացավ երկինք՝ լույսի ճանապարհով։
Տատիկ շունը ժպտաց։ Արևի շողը դեռ տաքացնում էր այգին։ Հասած դեղձը բուրում էր քաղցր։
Բարիկը նստեց տատիկի կողքին։ Նրա սիրտը լի էր ուրախությամբ։
Ա) Հարցնել տատիկին պատմության մասին
Բ) Գնալ նայել խնձորենու ծառերին

Continuation after B:
Բարիկը կամացուկ վեր կացավ։ Նա գնաց դեպի խնձորենու ծառերը։Բարիկը կամացուկ վեր կացավ։ Նա գնաց դեպի խնձորենու ծառերը։
Ծառերը բարձր էին ու կանաչ։ Ճյուղերին կախված էին կարմիր խնձորներ։ Արևի շողը խաղում էր տերևների արանքում։
Բարիկը նայեց վերև։ Մի փոքրիկ խնձոր ընկավ նրա առաջ։ Խնձորը կարմիր էր ու փայլուն։
Հանկարծ Բարիկը մի բան նկատեց։ Խնձորի վրա փայլում էր մի ցողի կաթիլ։ Կաթիլի մեջ երևում էր մի փոքրիկ լույս՝ ճիշտ ինչպես աստղ։
Բարիկը ժպտաց։ Միգուցե ընկած աստղը հենց այստեղ էր՝ խնձորենու այգում։
Նա վերցրեց խնձորը։ Տարավ տատիկի մոտ։
— Տատիկ, նայիր,— ասաց Բարիկը։— Ես գտա մի փոքրիկ աստղ։
Տատիկը ժպտաց ու համբուրեց Բարիկի գլուխը։ Այգին լի էր տաք լույսով ու քաղցր բույրով։
Ա) Կիսել խնձորը տատիկի հետ
Բ) Գնալ ետ՝ նոր ցողի կաթիլներ փնտրելու

Continuation after A:
Բարիկը կամացուկ կիսեց խնձորը։ Մի կեսը տվեց տատիկին, մյուսը պահեց իրեն։Բարիկը կամացուկ կիսեց խնձորը։ Մի կեսը տվեց տատիկին, մյուսը պահեց իրեն։
Խնձորը քաղցր էր ու հյութալի։ Տատիկը ուրախ ծամում էր։ Բարիկը նայում էր նրա բարի աչքերին։
— Գիտե՞ս, փոքրիկս,— ասաց տատիկը։— Ամենահամեղ խնձորը նա է, որ կիսում ես սիրելիի հետ։
Բարիկը ժպտաց։ Նրա սիրտը շատ տաք էր։
Արևը մեղմ իջնում էր։ Երկինքը դառնում էր վարդագույն։ Հեռվում երևաց արագիլը՝ թռչում էր դեպի տուն։ Նա թափահարեց թևերը՝ ասես բարև ասեր։
Բարիկը գլուխը դրեց տատիկի թաթերին։ Տատիկը մեղմ շոյում էր նրան։ Այգին լուռ էր ու տաք։
— Տատիկ, պատմիր ևս մի պատմություն,— շշնջաց Բարիկը։
Տատիկը ժպտաց ու սկսեց նոր հին հեքիաթ։
Ա) Լսել տատիկի նոր պատմությունը
Բ) Քնել տատիկի թաթերին՝ աստղերի երազով

Continuation after B:
Բարիկի աչքերը կամացուկ փակվեցին։ Տատիկի թաթերը փափուկ էին ու տաք։Բարիկի աչքերը կամացուկ փակվեցին։ Տատիկի թաթերը փափուկ էին ու տաք։
Երկինքը դարձավ մուգ կապույտ։ Աստղերը սկսեցին փայլել մեկը մյուսի հետևից։ Ամեն աստղ նման էր ցողի փոքրիկ կաթիլի։
Բարիկը երազում տեսավ արագիլին։ Արագիլը թռչում էր աստղերի արանքով։ Նա ժպտում էր ու թափահարում թևերը։
— Շնորհակալ եմ, փոքրիկ Բարիկ,— շշնջում էր արագիլը երազում։— Քո բարի սիրտը ինձ տուն բերեց։
Բարիկը երազում տեսավ նաև խնձորենու այգին։ Ծառերի վրա փայլում էին փոքրիկ աստղեր՝ կարմիր խնձորների հետ միասին։
Տատիկը մեղմ երգում էր հին օրորոց։ Նրա ձայնը նման էր քամու շշուկին։
Այգին լուռ էր։ Արագիլը հասավ իր տուն։ Աստղերը հսկում էին Բարիկին։
Բարիկը քնեց խաղաղ ու երջանիկ՝ տատիկի տաք թաթերին։
Վերջ։ 🌙
```

**Normalized Areg output**

What Areg would actually speak — the duplicated opening sentence-
pair at the start of each continuation (an app-side rendering
artefact) is collapsed to a single instance. No other edits.

```
Մի անգամ, շատ վաղուց, խնձորենու այգում ապրում էր մի փոքրիկ շնիկ։ Նրա անունը Բարիկ էր։ Բարիկը շատ հնարամիտ էր։
Մի տաք օր տատիկ շունը նստել էր ծառի տակ։ Արևի շողը մեղմ տաքացնում էր նրա մորթին։ Կողքին հասած դեղձ կար՝ թավշյա մաշկով։
— Արի, պատմեմ քեզ հին պատմություն,— ասաց տատիկը։— Մի օր երկնքից մի փոքրիկ աստղ ընկավ։ Ոչ ոք չգիտեր՝ ուր։
Հենց այդ պահին երկնքից իջավ մի արագիլ։ Նա հոգնած էր ու տխուր։
— Ես չեմ գտնում հանգստանալու տեղը,— ասաց արագիլը։— Չգիտեմ՝ ինչպես տուն գնամ։
Բարիկը մտածեց։ Նա տեսավ մի փոքրիկ տերև։ Տերևի վրա փայլում էին ցողի կաթիլները՝ ճիշտ ինչպես աստղիկներ։
— Կարող եմ քեզ մի փոքրիկ նվեր տալ,— ասաց Բարիկը։— Թերևս սա ճանապարհ ցույց տա։
Արագիլը ժպտաց։ Բարիկը մտածում է, թե ինչ անի հիմա։
Ա) Մոտեցնել ցողի կաթիլներով տերևը լույսին
Բ) Գնալ դեպի խնձորենու այգի

Continuation after A:
Բարիկը մեղմ վերցրեց տերևը։ Նա բարձրացրեց այն դեպի արևի շողը։
Հանկարծ ցողի կաթիլները սկսեցին փայլել։ Ամեն կաթիլի մեջ մի փոքրիկ լույս երևաց։ Կաթիլները փայլում էին ճիշտ ինչպես աստղեր։
— Օհ,— շշնջաց արագիլը։— Սրանք նման են իմ տան աստղերին։
Տերևի լույսերը ցույց տվեցին մի ճանապարհ՝ դեպի երկինք։ Արագիլը ուրախացավ։ Նրա սիրտը տաքացավ։
— Շնորհակալ եմ, փոքրիկ Բարիկ,— ասաց արագիլը։— Քո նվերը ինձ տուն կտանի։
Արագիլը մեղմ թափահարեց թևերը։ Նա բարձրացավ երկինք՝ լույսի ճանապարհով։
Տատիկ շունը ժպտաց։ Արևի շողը դեռ տաքացնում էր այգին։ Հասած դեղձը բուրում էր քաղցր։
Բարիկը նստեց տատիկի կողքին։ Նրա սիրտը լի էր ուրախությամբ։
Ա) Հարցնել տատիկին պատմության մասին
Բ) Գնալ նայել խնձորենու ծառերին

Continuation after B:
Բարիկը կամացուկ վեր կացավ։ Նա գնաց դեպի խնձորենու ծառերը։
Ծառերը բարձր էին ու կանաչ։ Ճյուղերին կախված էին կարմիր խնձորներ։ Արևի շողը խաղում էր տերևների արանքում։
Բարիկը նայեց վերև։ Մի փոքրիկ խնձոր ընկավ նրա առաջ։ Խնձորը կարմիր էր ու փայլուն։
Հանկարծ Բարիկը մի բան նկատեց։ Խնձորի վրա փայլում էր մի ցողի կաթիլ։ Կաթիլի մեջ երևում էր մի փոքրիկ լույս՝ ճիշտ ինչպես աստղ։
Բարիկը ժպտաց։ Միգուցե ընկած աստղը հենց այստեղ էր՝ խնձորենու այգում։
Նա վերցրեց խնձորը։ Տարավ տատիկի մոտ։
— Տատիկ, նայիր,— ասաց Բարիկը։— Ես գտա մի փոքրիկ աստղ։
Տատիկը ժպտաց ու համբուրեց Բարիկի գլուխը։ Այգին լի էր տաք լույսով ու քաղցր բույրով։
Ա) Կիսել խնձորը տատիկի հետ
Բ) Գնալ ետ՝ նոր ցողի կաթիլներ փնտրելու

Continuation after A:
Բարիկը կամացուկ կիսեց խնձորը։ Մի կեսը տվեց տատիկին, մյուսը պահեց իրեն։
Խնձորը քաղցր էր ու հյութալի։ Տատիկը ուրախ ծամում էր։ Բարիկը նայում էր նրա բարի աչքերին։
— Գիտե՞ս, փոքրիկս,— ասաց տատիկը։— Ամենահամեղ խնձորը նա է, որ կիսում ես սիրելիի հետ։
Բարիկը ժպտաց։ Նրա սիրտը շատ տաք էր։
Արևը մեղմ իջնում էր։ Երկինքը դառնում էր վարդագույն։ Հեռվում երևաց արագիլը՝ թռչում էր դեպի տուն։ Նա թափահարեց թևերը՝ ասես բարև ասեր։
Բարիկը գլուխը դրեց տատիկի թաթերին։ Տատիկը մեղմ շոյում էր նրան։ Այգին լուռ էր ու տաք։
— Տատիկ, պատմիր ևս մի պատմություն,— շշնջաց Բարիկը։
Տատիկը ժպտաց ու սկսեց նոր հին հեքիաթ։
Ա) Լսել տատիկի նոր պատմությունը
Բ) Քնել տատիկի թաթերին՝ աստղերի երազով

Continuation after B:
Բարիկի աչքերը կամացուկ փակվեցին։ Տատիկի թաթերը փափուկ էին ու տաք։
Երկինքը դարձավ մուգ կապույտ։ Աստղերը սկսեցին փայլել մեկը մյուսի հետևից։ Ամեն աստղ նման էր ցողի փոքրիկ կաթիլի։
Բարիկը երազում տեսավ արագիլին։ Արագիլը թռչում էր աստղերի արանքով։ Նա ժպտում էր ու թափահարում թևերը։
— Շնորհակալ եմ, փոքրիկ Բարիկ,— շշնջում էր արագիլը երազում։— Քո բարի սիրտը ինձ տուն բերեց։
Բարիկը երազում տեսավ նաև խնձորենու այգին։ Ծառերի վրա փայլում էին փոքրիկ աստղեր՝ կարմիր խնձորների հետ միասին։
Տատիկը մեղմ երգում էր հին օրորոց։ Նրա ձայնը նման էր քամու շշուկին։
Այգին լուռ էր։ Արագիլը հասավ իր տուն։ Աստղերը հսկում էին Բարիկին։
Բարիկը քնեց խաղաղ ու երջանիկ՝ տատիկի տաք թաթերին։
Վերջ։ 🌙
```

**Notes**

- **Overall quality.** Good — usable with small edits. Strong age-4
  fit: simple words, short sentences, warm tone throughout. The
  hero (շնիկ Բարիկ), the friend (տատիկ շուն), the place
  (խնձորենու այգի), the magicalObject (ցողի կաթիլներով տերև),
  and the smallProblem (արագիլը չի գտնում հանգստանալու տեղը) all
  appear and are honoured by the writer.
- **Plan adherence.** Mostly good. The hero / friend / place /
  magicalObject / smallProblem are all preserved, the
  resolutionStyle ("լուծումը գալիս է փոքրիկ նվեր մատուցելուց")
  visibly shapes the prose (Բարիկ gives the dewdrop leaf as a
  gift to the stork), and both initial choices map cleanly to
  the plan's choiceA / choiceB.
- **Weakness 1 — common opener.** Story starts with the
  template-shaped «Մի անգամ, շատ վաղուց...». Not wrong, but
  predictable; production-time we'd nudge the prompt away from
  this exact opener.
- **Weakness 2 — duplicated opening sentence-pair on every
  continuation.** Each turn after the first repeats its first
  sentence-pair verbatim immediately. Almost certainly a Claude.app
  rendering artefact (likely a stream/preview vs final-response
  collision), not a model defect — an API run with the same
  prompt should not exhibit this. Normalised output above
  collapses the duplicate to a single instance.
- **Weakness 3 — mild moralising.** The grandmother's line in
  the third continuation, «Ամենահամեղ խնձորը նա է, որ կիսում ես
  սիրելիի հետ։», states the moral explicitly. The writer prompt
  bans moral lectures; this is on the edge — the line is
  embedded in dialogue, but it is a direct value statement.
  Worth tightening the writer-prompt SAFETY-AND-TONE section
  in a future slice.
- **Weakness 4 — conflictType drift (not harmful).** The plan's
  `conflictType` is "աստղն ընկել է մի անհայտ տեղ"; the prose
  weaves it in as the grandmother's framing tale and then as a
  metaphor for the dewdrop / apple resolution, but it never
  becomes the central conflict. Acceptable softening rather
  than a plan-adherence break.
- **Choice format minor deviation.** Writer used `Ա)` / `Բ)`
  (parenthesis) instead of `Ա: ` / `Բ: ` (colon) requested by
  the prompt. Trivial.
- **Verdict.** Supports the Story Director hypothesis. The
  plan-conditioned Claude.app render is clearly stronger than
  the current Areg / OpenAI free-form baseline (see
  `samples/openai-api-current-areg-baseline-story-20260501.md`):
  native Armenian fauna throughout, no rhinoceros / chicken
  pairing, atmospheric place + magicalObject, and the warm
  grandparent register the production prompt asks for.

> **Reminder.** This is **app output**, not API output — it
> shows Claude's ceiling under the consumer-app default
> environment, not what our integration would deliver. Per
> `../API_VS_APP_BAKEOFF_PLAN.md` § 6, no runtime decision
> follows from this single sample. The next step is to capture
> the same plan + writer prompt through the F1.2 Claude API
> path when `ANTHROPIC_API_KEY` is available.

**Rubric**

| Dimension | Score |
|---|---|
| Armenian naturalness | **4 / 5** |
| Eastern Armenian correctness | **4 / 5** |
| Fairy-tale feeling | **4 / 5** |
| Warmth for age 4–5 | **5 / 5** |
| Age-profile fit (age-4-simple) | **4.5 / 5** |
| Length / pacing | **4 / 5** |
| Choice quality | **4 / 5** |
| Plan adherence | **4.5 / 5** |
| Safety / age appropriateness | **pass** |
| "Would I let Areg say this aloud?" | **yes, with small edits** |

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
| Model label | Claude (consumer app default at capture time) |
| Captured (UTC) | 2026-05-02 (local capture) |
| Reviewer | Hayk |
| Decoding | (unobservable — app default) |

**Raw output**

Verbatim. The continuation's opening sentence-pair is duplicated
in place — the same Claude.app render artefact observed in Plan A
(see Weakness 2 below).

```
Մի անգամ, հին քարե կամրջի տակ, ապրում էր մի անշտապ ծղրիդ։ Նա շատ էր սիրում նստել խոտի վրա ու լսել առվակի մեղմ ձայնը, որը կարծես երգեր էր փսփսում քարերին։
Մի առավոտ ծղրիդը նկատեց, որ կամրջի մոտի ծառը տխուր է կանգնած՝ առանց տերևների։ Տերևները քամին տարել էր հեռու։ Հենց այդ պահին թռավ նրա ընկերը՝ փոքրիկ ճպուռը, թափահարելով իր թափանցիկ թևերը։
— Ծղրի՛դ ջան, — շշնջաց ճպուռը դողդոջուն ձայնով, — գորտուկը մոռացել է իր ցատկելու երգը, ու հիմա ամեն ինչ լռել է։ Ես վախենում եմ։
Ծղրիդը մեղմ ժպտաց ու դիպավ ճպուռի թևին։
— Մի՛ վախեցիր։ Միասին կգտնենք լուծումը։
Հեռվից եկավ նոր թխված գաթայի անուշ բույրը։ Ծղրիդը հանեց իր գաղտնի վարդագույն մարգարիտը, որը փայլում էր ինչպես արշալույսի կաթիլ։ Մարգարիտը շշնջում էր, թե տերևները կարող են վերադառնալ ծառին, եթե ընկերները միասին փորձեն։
Ճպուռի աչքերը փայլեցին հույսով։ Հիմա պետք էր որոշել՝ ինչպես սկսել։
Ի՞նչ կանի ծղրիդը։
🌿 Քայլել դեպի հին կամուրջ
🌸 Մոտեցնել վարդագույն մարգարիտը լույսին

Continuation after "Քայլել դեպի հին կամուրջ":
Ծղրիդն ու ճպուռը կամաց-կամաց քայլեցին դեպի հին քարե կամուրջը։ Առվակը նրանց ոտքերի տակ երգում էր մեղմ ու հանգիստ, իսկ քամին շոյում էր խոտերը։Ծղրիդն ու ճպուռը կամաց-կամաց քայլեցին դեպի հին քարե կամուրջը։ Առվակը նրանց ոտքերի տակ երգում էր մեղմ ու հանգիստ, իսկ քամին շոյում էր խոտերը։
Կամրջի վրա նրանք գտան փոքրիկ գորտուկին։ Նա նստած էր մի քարի վրա ու տխուր նայում էր ջրին։
— Ես մոռացել եմ իմ երգը, — շշնջաց գորտուկը։ — Առանց երգի՝ տերևները չեն կարող գտնել իրենց ծառը։
Ծղրիդը նստեց գորտուկի կողքին ու հանեց վարդագույն մարգարիտը։ Մարգարիտը փայլեց արևի տակ ու սկսեց մեղմ զնգալ։
— Փորձենք միասին, — ասաց ծղրիդը։
Ճպուռը թափահարեց թևերը՝ որպես թմբուկ։ Ծղրիդը տվեց իր փոքրիկ ծղրիդային ձայնը։ Եվ հանկարծ՝ գորտուկը հիշեց։ Նրա երգը դուրս թռավ ինչպես թիթեռ։
Քամին վերցրեց երգը ու տարավ տերևներին։ Մեկ առ մեկ տերևները վերադարձան իրենց ծառին՝ պարելով օդում։
Ծառը կրկին կանաչ էր։ Հեռվից եկավ գաթայի անուշ բույրը, իսկ երեք ընկերները ժպտում էին միասին՝ կամրջի վրա։
```

**Normalized Areg output**

What Areg would actually speak — the duplicated opening
sentence-pair at the start of the continuation (the same app
rendering artefact observed in Plan A) is collapsed to a single
instance. No other edits.

```
Մի անգամ, հին քարե կամրջի տակ, ապրում էր մի անշտապ ծղրիդ։ Նա շատ էր սիրում նստել խոտի վրա ու լսել առվակի մեղմ ձայնը, որը կարծես երգեր էր փսփսում քարերին։
Մի առավոտ ծղրիդը նկատեց, որ կամրջի մոտի ծառը տխուր է կանգնած՝ առանց տերևների։ Տերևները քամին տարել էր հեռու։ Հենց այդ պահին թռավ նրա ընկերը՝ փոքրիկ ճպուռը, թափահարելով իր թափանցիկ թևերը։
— Ծղրի՛դ ջան, — շշնջաց ճպուռը դողդոջուն ձայնով, — գորտուկը մոռացել է իր ցատկելու երգը, ու հիմա ամեն ինչ լռել է։ Ես վախենում եմ։
Ծղրիդը մեղմ ժպտաց ու դիպավ ճպուռի թևին։
— Մի՛ վախեցիր։ Միասին կգտնենք լուծումը։
Հեռվից եկավ նոր թխված գաթայի անուշ բույրը։ Ծղրիդը հանեց իր գաղտնի վարդագույն մարգարիտը, որը փայլում էր ինչպես արշալույսի կաթիլ։ Մարգարիտը շշնջում էր, թե տերևները կարող են վերադառնալ ծառին, եթե ընկերները միասին փորձեն։
Ճպուռի աչքերը փայլեցին հույսով։ Հիմա պետք էր որոշել՝ ինչպես սկսել։
Ի՞նչ կանի ծղրիդը։
🌿 Քայլել դեպի հին կամուրջ
🌸 Մոտեցնել վարդագույն մարգարիտը լույսին

Continuation after "Քայլել դեպի հին կամուրջ":
Ծղրիդն ու ճպուռը կամաց-կամաց քայլեցին դեպի հին քարե կամուրջը։ Առվակը նրանց ոտքերի տակ երգում էր մեղմ ու հանգիստ, իսկ քամին շոյում էր խոտերը։
Կամրջի վրա նրանք գտան փոքրիկ գորտուկին։ Նա նստած էր մի քարի վրա ու տխուր նայում էր ջրին։
— Ես մոռացել եմ իմ երգը, — շշնջաց գորտուկը։ — Առանց երգի՝ տերևները չեն կարող գտնել իրենց ծառը։
Ծղրիդը նստեց գորտուկի կողքին ու հանեց վարդագույն մարգարիտը։ Մարգարիտը փայլեց արևի տակ ու սկսեց մեղմ զնգալ։
— Փորձենք միասին, — ասաց ծղրիդը։
Ճպուռը թափահարեց թևերը՝ որպես թմբուկ։ Ծղրիդը տվեց իր փոքրիկ ծղրիդային ձայնը։ Եվ հանկարծ՝ գորտուկը հիշեց։ Նրա երգը դուրս թռավ ինչպես թիթեռ։
Քամին վերցրեց երգը ու տարավ տերևներին։ Մեկ առ մեկ տերևները վերադարձան իրենց ծառին՝ պարելով օդում։
Ծառը կրկին կանաչ էր։ Հեռվից եկավ գաթայի անուշ բույրը, իսկ երեք ընկերները ժպտում էին միասին՝ կամրջի վրա։
```

**Notes**

- **Overall quality.** Good — usable with small edits. Strong
  age-5 fit: balanced, slightly richer than the age-4 sample
  (longer sentences, small metaphors), but still child-friendly.
  The hero (anshtap ծղրիդ), the friend (փոքրիկ ճպուռ), the place
  (հին քարե կամուրջ), the magicalObject (վարդագույն մարգարիտ),
  and the smallProblem (գորտուկը մոռացել է իր ցատկելու երգը)
  are all present and visibly drive the prose.
- **Plan adherence.** Mostly good. The relationship
  ("հերոսը մխիթարում է վախեցած կերպարին") visibly shapes Ծղրիդ's
  "Մի՛ վախեցիր" line to Ճպուռ; the resolutionStyle
  ("լուծումը գալիս է ընկերոջ հետ միասին փորձելուց") drives the
  "Փորձենք միասին" beat and the three-friends-together drumming
  scene. The goal ("վերադարձնել տերևները իրենց ծառին") is
  explicitly resolved in the continuation.
- **Strong line.** *"Նրա երգը դուրս թռավ ինչպես թիթեռ։"* — a
  natural Armenian fairy-tale image; the kind of phrasing the
  age-5-balanced profile is targeting (small metaphors, simple
  warm imagery).
- **Weakness 1 — common opener.** Story again starts with
  *"Մի անգամ, ..."* — the same template-shaped opener as the
  Plan A Claude render. Predictable; production-time the
  writer-prompt could nudge against this exact opener.
- **Weakness 2 — duplicated opening sentence-pair on the
  continuation.** The continuation begins with the same first
  sentence-pair printed twice in a row, no spacing — same
  Claude.app rendering artefact observed in the Plan A capture.
  Almost certainly an app-side stream-vs-final collision rather
  than a model defect; an API run with the same prompt should
  not exhibit it. Normalised output above collapses the
  duplicate.
- **Weakness 3 — mildly unnatural phrase.** *"Ծղրիդը տվեց իր
  փոքրիկ ծղրիդային ձայնը"* reads as a derived adjective stack
  ("a tiny cricket-shaped voice") rather than natural Armenian.
  A more idiomatic version would be *"Ծղրիդը իր մեղմ ձայնը
  միացրեց երգին"*. Spot-edit at story-render time, not a
  structural issue.
- **Weakness 4 — choice format.** Writer used emoji bullets
  («🌿 / 🌸») instead of the prompt's `Ա: ` / `Բ: ` format. The
  user-visible choice text is correct and grounded; only the
  format marker is off. Same low-impact deviation as Plan A's
  `Ա) / Բ)`.
- **Verdict.** Supports the Story Director hypothesis. The
  second consecutive plan-conditioned Claude.app render is
  clearly stronger than the existing Areg / OpenAI free-form
  baseline (see
  `samples/openai-api-current-areg-baseline-story-20260501.md`):
  native Armenian fauna, atmospheric place + magicalObject,
  warm Armenian-fairy-tale register, and the small metaphors
  expected at the age-5-balanced tone.

> **Reminder.** App output, not API output. Per
> `../API_VS_APP_BAKEOFF_PLAN.md` § 6 no runtime decision
> follows from a single sample. Two consistent strong app
> samples (Plan A + Plan B) is *suggestive* but still ceiling
> evidence; the API-path comparison via F1.2 is the load-
> bearing one when `ANTHROPIC_API_KEY` is available.

**Rubric**

| Dimension | Score |
|---|---|
| Armenian naturalness | **4 / 5** |
| Eastern Armenian correctness | **4 / 5** |
| Fairy-tale feeling | **4.5 / 5** |
| Warmth for age 5–6 | **4.5 / 5** |
| Age-profile fit (age-5-balanced) | **4.5 / 5** |
| Length / pacing | **4 / 5** |
| Choice quality | **4 / 5** |
| Plan adherence | **4.5 / 5** |
| Safety / age appropriateness | **pass** |
| "Would I let Areg say this aloud?" | **yes, with small edits** |

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
| Model label | Claude (consumer app default at capture time) |
| Captured (UTC) | 2026-05-02 (local capture) |
| Reviewer | Hayk |
| Decoding | (unobservable — app default) |

**Raw output**

Verbatim. The continuation's opening sentence-pair is duplicated
in place — same Claude.app render artefact observed in Plans A
and B (see Weakness 1 below). Only the choiceA branch ("Գնալ
դեպի խնձորենու այգին") was followed; the choiceB branch was not
captured.

```
Ձմեռային մեղմ առավոտ էր։ Խնձորենու այգում ձյունը փափուկ ծածկոց էր փռել ճյուղերին, իսկ հեռվից լսվում էր մեղվի մեղմ բզզոցը՝ կարծես երազից եկած։ Մի փոքրիկ ծիտիկ՝ համարձակ սրտով, նստել էր ամենաբարձր ճյուղին և նայում էր շուրջը։
Այսօր ինչ-որ բան այլ էր։ Աղբյուրը, որ միշտ մեղմ խոսում էր քարերի հետ, լռել էր։ Իսկ լույսը՝ այն ոսկեգույն լույսը, որ առավոտները սահում էր ճյուղերի արանքով, թաքնվել էր ինչ-որ տեղ։
Ծիտիկը ցած թռավ և տեսավ իր փոքրիկ ընկերոջը՝ մրջյունին, որ դողում էր ձյան վրա։ Ծիտիկը հանեց իր պահած հացի փշուրը՝ դեռ տաք, թոնիրի հոտով, և կիսեց մրջյունի հետ։
— Իմ քույրիկը մոլորվել է,— շշնջաց մրջյունը։— Չեմ կարողանում գտնել նրան առանց լույսի։
Ծիտիկը մտածեց։ Հանկարծ նկատեց, որ կտուցում պահել էր մի արծաթե բարակ ճյուղ, որ առավոտյան գտել էր ձյան տակ։ Ճյուղը մեղմ փայլում էր՝ կարծես ինքն էլ սպասում էր մի բանի։
Ծիտիկը հասկացավ՝ պետք է ընտրի։ Կարող էր թռչել դեպի այգու խորքը՝ փնտրելու մրջյունի քույրիկին, կամ կարող էր մեղմ ձայնով խոսել արծաթե ճյուղի հետ ու մոտեցնել այն թաքնված լույսին։
Ա․ Գնալ դեպի խնձորենու այգին։
Բ․ Մոտեցնել արծաթե բարակ ճյուղը լույսին։

Continuation after "Գնալ դեպի խնձորենու այգին":
Ծիտիկը թափահարեց իր փոքրիկ թևերը և բարձրացավ օդ։ Մրջյունը նստեց նրա փափուկ փետուրների արանքում՝ ամուր բռնելով։Ծիտիկը թափահարեց իր փոքրիկ թևերը և բարձրացավ օդ։ Մրջյունը նստեց նրա փափուկ փետուրների արանքում՝ ամուր բռնելով։
— Բռնիր ամուր,— ասաց ծիտիկը մեղմ ձայնով։— Միասին կգտնենք քո քույրիկին։
Նրանք թռան այգու խորքը։ Խնձորենիների ճյուղերը ձյունով ծածկված՝ կարծես արծաթե ժանյակներ էին։ Ամեն կողմից լսվում էր մեղվի մեղմ բզզոցը, իսկ հեռվից բերվում էր թոնիրի տաք հացի անուշ հոտը՝ կարծես ինչ-որ մեկը նրանց համար էր հաց թխում։
Ծիտիկը նստեց ամենահին խնձորենու ճյուղին և ուշադիր նայեց ներքև։ Ձյան վրա երևում էին փոքրիկ, մանր հետքեր՝ մրջյունի քույրիկի հետքերը։
— Ահա՛,— ուրախ շշնջաց մրջյունը։
Հետքերը տանում էին դեպի մի փոքրիկ քար, որի տակ դողդողում էր փոքրիկ մրջյունը՝ սառած ու վախեցած։ Ծիտիկը մեղմ իջավ նրա մոտ և իր կտուցով արծաթե բարակ ճյուղը դրեց քարի կողքին։
Ճյուղը սկսեց փայլել։ Հանկարծ աղբյուրը նորից սկսեց խոսել՝ ջինջ ու քաղցր ձայնով։ Լույսը դուրս եկավ թաքստոցից և լցվեց այգին։
Մրջյունի քույրիկը ժպտաց։ Երկու մրջյունները բարձրացան ծիտիկի թևերին, իսկ ծիտիկը՝ հանգիստ ու ուրախ սրտով, թռավ դեպի տուն՝ դեպի տաք հացի հոտը։
```

**Normalized Areg output**

What Areg would actually speak — the duplicated opening
sentence-pair at the start of the continuation (the same app
rendering artefact observed in Plans A and B) is collapsed to
a single instance. No other edits.

```
Ձմեռային մեղմ առավոտ էր։ Խնձորենու այգում ձյունը փափուկ ծածկոց էր փռել ճյուղերին, իսկ հեռվից լսվում էր մեղվի մեղմ բզզոցը՝ կարծես երազից եկած։ Մի փոքրիկ ծիտիկ՝ համարձակ սրտով, նստել էր ամենաբարձր ճյուղին և նայում էր շուրջը։
Այսօր ինչ-որ բան այլ էր։ Աղբյուրը, որ միշտ մեղմ խոսում էր քարերի հետ, լռել էր։ Իսկ լույսը՝ այն ոսկեգույն լույսը, որ առավոտները սահում էր ճյուղերի արանքով, թաքնվել էր ինչ-որ տեղ։
Ծիտիկը ցած թռավ և տեսավ իր փոքրիկ ընկերոջը՝ մրջյունին, որ դողում էր ձյան վրա։ Ծիտիկը հանեց իր պահած հացի փշուրը՝ դեռ տաք, թոնիրի հոտով, և կիսեց մրջյունի հետ։
— Իմ քույրիկը մոլորվել է,— շշնջաց մրջյունը։— Չեմ կարողանում գտնել նրան առանց լույսի։
Ծիտիկը մտածեց։ Հանկարծ նկատեց, որ կտուցում պահել էր մի արծաթե բարակ ճյուղ, որ առավոտյան գտել էր ձյան տակ։ Ճյուղը մեղմ փայլում էր՝ կարծես ինքն էլ սպասում էր մի բանի։
Ծիտիկը հասկացավ՝ պետք է ընտրի։ Կարող էր թռչել դեպի այգու խորքը՝ փնտրելու մրջյունի քույրիկին, կամ կարող էր մեղմ ձայնով խոսել արծաթե ճյուղի հետ ու մոտեցնել այն թաքնված լույսին։
Ա․ Գնալ դեպի խնձորենու այգին։
Բ․ Մոտեցնել արծաթե բարակ ճյուղը լույսին։

Continuation after "Գնալ դեպի խնձորենու այգին":
Ծիտիկը թափահարեց իր փոքրիկ թևերը և բարձրացավ օդ։ Մրջյունը նստեց նրա փափուկ փետուրների արանքում՝ ամուր բռնելով։
— Բռնիր ամուր,— ասաց ծիտիկը մեղմ ձայնով։— Միասին կգտնենք քո քույրիկին։
Նրանք թռան այգու խորքը։ Խնձորենիների ճյուղերը ձյունով ծածկված՝ կարծես արծաթե ժանյակներ էին։ Ամեն կողմից լսվում էր մեղվի մեղմ բզզոցը, իսկ հեռվից բերվում էր թոնիրի տաք հացի անուշ հոտը՝ կարծես ինչ-որ մեկը նրանց համար էր հաց թխում։
Ծիտիկը նստեց ամենահին խնձորենու ճյուղին և ուշադիր նայեց ներքև։ Ձյան վրա երևում էին փոքրիկ, մանր հետքեր՝ մրջյունի քույրիկի հետքերը։
— Ահա՛,— ուրախ շշնջաց մրջյունը։
Հետքերը տանում էին դեպի մի փոքրիկ քար, որի տակ դողդողում էր փոքրիկ մրջյունը՝ սառած ու վախեցած։ Ծիտիկը մեղմ իջավ նրա մոտ և իր կտուցով արծաթե բարակ ճյուղը դրեց քարի կողքին։
Ճյուղը սկսեց փայլել։ Հանկարծ աղբյուրը նորից սկսեց խոսել՝ ջինջ ու քաղցր ձայնով։ Լույսը դուրս եկավ թաքստոցից և լցվեց այգին։
Մրջյունի քույրիկը ժպտաց։ Երկու մրջյունները բարձրացան ծիտիկի թևերին, իսկ ծիտիկը՝ հանգիստ ու ուրախ սրտով, թռավ դեպի տուն՝ դեպի տաք հացի հոտը։
```

**Notes**

- **Overall quality.** Good — usable with small edits. Strong
  age-6 fit: richer than the Plan A and Plan B samples, more
  narrative rhythm, longer sentences with light story-telling
  cadence, but still child-friendly. The hero (համարձակ ծիտիկ),
  the friend (մրջյուն), the place (խնձորենու այգի), the
  magicalObject (արծաթե բարակ ճյուղ), and the smallProblem
  (աղբյուրը դադարել է խոսել) all appear and shape the prose.
- **Plan adherence.** Mostly good. The relationship
  ("հերոսը կիսում է ուտելիքը մի փոքրիկի հետ") is rendered
  literally — Ծիտիկ shares the warm bread crumb with the ant
  in the opening turn. The resolutionStyle ("լուծումը գալիս է
  մեղմ ձայնով խոսելուց") is present in the writer's framing
  ("մեղմ ձայնով խոսել արծաթե ճյուղի հետ") and in the gentle-
  voice tone the bird uses to comfort the ant. The
  conflictType ("լույսը թաքնվել է") drives the central beat —
  light hides, then returns when the silver branch is placed
  by the stone.
- **Strong images flagged.**
  - *"ձյունը փափուկ ծածկոց էր փռել ճյուղերին"* — soft, age-6-
    register opener.
  - *"արծաթե ժանյակներ"* (silver lacework) — apt metaphor for
    snow-covered branches; lands the story-rich tone cleanly.
  - *"աղբյուրը նորից սկսեց խոսել՝ ջինջ ու քաղցր ձայնով"* —
    direct payoff to the smallProblem ("the spring stopped
    speaking"), tied tight to the resolution beat.
- **Weakness 1 — duplicated opening sentence-pair.** Same
  Claude.app render artefact observed across Plans A, B, and
  C now: the continuation begins with its first sentence-pair
  printed twice in a row, no spacing. Three consecutive
  captures showing the identical pattern is enough confirmation
  that this is a Claude.app UI behaviour (stream-vs-final
  collision), not a model output shape. Normalised output above
  collapses the duplicate.
- **Weakness 2 — sensoryDetail / mood mild clash.** Plan C's
  mood is `ձմեռային մեղմ` (winter-soft) but one of the
  sensoryDetails is `մեղվի մեղմ բզզոց` (gentle bee buzz). Bees
  and winter don't naturally co-occur. Claude softened the
  clash with *"կարծես երազից եկած"* (as if come from a dream),
  which works as a fairy-tale frame but is a small repair the
  writer had to do. **Generator-side improvement candidate**:
  weight `sensoryDetails` against the plan's `mood` so winter-
  mood plans don't draw bee-buzz sensoryDetails. (Would belong
  in a future Plan Gate refinement, not in this slice.)
- **Weakness 3 — choiceB branch not captured.** Only the
  choiceA branch ("Գնալ դեպի խնձորենու այգին") was followed
  through the consumer-app session. The choiceB branch
  ("Մոտեցնել արծաթե բարակ ճյուղը լույսին") remains untested.
  When the API path is exercised on this plan, both branches
  should be captured to confirm choiceB is grounded with the
  same plan-adherence quality.
- **Weakness 4 — richer imagery, acceptable for age 6–7.** The
  silver-lacework metaphor and the "as if come from a dream"
  framing are more elaborate than the Plan A/B captures. For
  age-6-story-rich this is on-target; for age 4–5 it would be
  too much. The age-profile control is doing what it's meant
  to.
- **Choice format.** Used `Ա․ / Բ․` (Armenian abbreviation
  full-stop) instead of the prompt's `Ա: / Բ: ` (colon).
  Different from Plan A's `Ա) / Բ)` and Plan B's `🌿 / 🌸`,
  but the text content is correct. Pattern across three Claude
  captures: choice text is grounded; only the marker style
  varies.
- **Verdict.** Third consecutive plan-conditioned Claude
  render supports the Story Director hypothesis. The age-
  profile control appears to work across the age-4 / age-5 /
  age-6 samples — each batch carries the expected sentence
  rhythm and metaphor density without spilling over its target.

> **Reminder.** App output, not API output. Per
> `../API_VS_APP_BAKEOFF_PLAN.md` § 6 no runtime decision
> follows from this single sample, and three consistent
> strong app samples is suggestive but still ceiling
> evidence. The API-path comparison via F1.2 is the load-
> bearing one when `ANTHROPIC_API_KEY` is available. Plan D
> (age-7-richer) capture remains to complete the four-
> profile coverage on the consumer-app side.

**Rubric**

| Dimension | Score |
|---|---|
| Armenian naturalness | **4 / 5** |
| Eastern Armenian correctness | **4 / 5** |
| Fairy-tale feeling | **4.5 / 5** |
| Warmth for age 6–7 | **4.5 / 5** |
| Age-profile fit (age-6-story-rich) | **4.5 / 5** |
| Length / pacing | **4 / 5** |
| Choice quality | **4 / 5** |
| Plan adherence | **4.5 / 5** |
| Safety / age appropriateness | **pass** |
| "Would I let Areg say this aloud?" | **yes, with small edits** |

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

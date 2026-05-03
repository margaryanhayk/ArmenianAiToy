# Writer prompt v2 — first capture package (2026-05-03)

**Status:** evidence / capture preparation only. **No production code
change.** No `ChatService` change. No runtime prompt change. No
provider switch. No live model / API call. Tool-only research data.

**Companion files:**
- [`./writer-prompt-tightening-notes-20260503.md`](./writer-prompt-tightening-notes-20260503.md) — the rule proposal (A–G) this capture tests.
- [`./plan-to-story-four-profile-capture-20260501.md`](./plan-to-story-four-profile-capture-20260501.md) — the four-profile v1 capture this v2 set is a focused follow-up of.
- [`./generated-plans-age-4-simple-20260501.json`](./generated-plans-age-4-simple-20260501.json), [`./generated-plans-age-7-richer-20260501.json`](./generated-plans-age-7-richer-20260501.json) — source plan banks for Plan A / Plan D.

---

## 1. Purpose

Manually capture writer-prompt-**v2** outputs against the two plans
that exposed the most useful issues in the v1 four-profile capture,
and confirm — without any API call yet — that v2 fixes:

- the repeated `Մի անգամ...` opener (rule A);
- choice marker drift (rule B — exact `Ա: ` / `Բ: `);
- moralizing / value-statement dialogue, especially in elder-character
  voices (rule C);
- age-7 continuation length overshoot (rule D);
- duplicated continuation sentence-pair, *if* the artefact is
  model-side rather than Claude.app-UI-side (rule F).

This is **app capture only**. Claude.app outputs remain ceiling /
reference evidence per `API_VS_APP_BAKEOFF_PLAN.md` § 1; the API
comparison is the load-bearing follow-up — the duplicate-sentence-
pair question only resolves over the API path.

The two plans are reused **verbatim** from the v1 four-profile
capture so v1 / v2 are apples-to-apples — only the writer prompt
changes.

---

## 2. Selected plans

### Plan A — age-4-simple #17

**Why selected:** carried the `Մի անգամ, շատ վաղուց` opener and the
shared-apple moralizing aphorism (`Ամենահամեղ խնձորը նա է, որ
կիսում ես սիրելիի հետ`) in v1. The simplest combination in the v1
top-3, ideal for stress-testing whether v2's anti-formula opener
+ anti-aphorism rules survive contact with a tatik-narrator setup.

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

### Plan D — age-7-richer #6

**Why selected:** carried the longest continuations (overshooting
the seed bank's `180-250` ceiling), abstract / poetic drift, and
the wise-cat moralizing dialogue in v1. The plan that v2's age-7
continuation budget (130–180) and rule C (anti-moralizing in
elder-character voice) most need to land on cleanly.

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

`relationship` carries the "wise elder" framing — the writer model
will likely cast `կատու` as wise. Rule C is what keeps that
framing from collapsing into aphorisms in the cat's mouth.

---

## 3. Writer prompt v2 rules summary

Distilled from
[`writer-prompt-tightening-notes-20260503.md`](./writer-prompt-tightening-notes-20260503.md)
§ 3. Each rule is a separate scoring axis under § 7 below.

| Rule | What it enforces |
|---|---|
| **A** | No default `Մի անգամ` / `Մի անգամ, շատ վաղուց` / `Մի գեղեցիկ օր` / `Մի առավոտ` opener. Open in a concrete scene grounded in `place` + `sensoryDetails` + `mood`. |
| **B** | Exact choice prefixes `Ա: ` and `Բ: ` (Armenian Ա/Բ + ASCII colon + single ASCII space). No emoji, no `Ա)`, no `Ա.`, no `Ա․`. No prose after the second choice line. |
| **C** | No direct moral / value-statement at the end of a turn. No moral aphorisms in elder-character (tatik / wise cat / owl / fish) dialogue. Show through action. |
| **D** | Per-turn budgets: age-4 initial 90–130, continuation 70–110; age-7 initial 180–230, continuation 130–180. |
| **E** | Register: age-4 short and concrete; age-7 light poetry allowed but no adult-literary aphorisms and no abstract emotional summary. |
| **F** | Continuation's first sentence directly performs the chosen action. No recap. No duplicate sentence within a turn. |
| **G** | Plan adherence: hero, friendOrGuide, place, magicalObject, smallProblem, goal, mood, choiceA / choiceB meanings preserved. |

---

## 4. Ready-to-paste Claude.app prompt — Plan A (age-4-simple)

Copy everything between the fences (inclusive of nothing outside them)
into Claude.app for the **initial** turn. After Areg responds, paste
the child's choice (`Ա` or `Բ`) and Claude continues.

```text
Դու Արեգն ես՝ տաք, հայալեզու հեքիաթասաց 4–7 տարեկան երեխաների համար։
Ստանալու ես STORY PLAN՝ JSON տեսքով։ Քո խնդիրն է այն վերածել մեկ
կարճ արևելահայերեն հեքիաթային քայլի, որը երեխան լսելու է հենց հիմա։

ԲԱՑԱՐՁԱԿ ԼԵԶՎԱԿԱՆ ԿԱՆՈՆ
- Պատասխանիր ՄԻԱՅՆ արևելահայերենով, հայկական տառերով։
- Ոչ տառադարձություն, ոչ անգլերեն, ոչ ռուսերեն։
- Բնական, սահուն, խոսակցական արևելահայերեն, ինչպես տաք հայ
  տատիկը պատմում է հին հեքիաթ իր փոքրիկ թոռնիկին։
- Ոչ թարգմանված հայերեն, ոչ գրքային, ոչ արհեստական։

ԲԱՑՄԱՆ ԿԱՆՈՆ (A)
- Մի՛ սկսիր «Մի անգամ», «Մի անգամ, շատ վաղուց», «Մի գեղեցիկ օր»
  կամ «Մի գեղեցիկ առավոտ» տիպի կաղապարով։
- Բացիր ուղիղ տեսարանով՝ հիմնված plan-ի place-ի,
  sensoryDetails-ի և mood-ի վրա։
- Ավանդական բացման բանաձևերը («Լինում է, չի լինում...»,
  «Կար ու չկար...») թույլատրվում են ՄԻԱՅՆ եթե plan-ը հատուկ
  պահանջում է, կամ շատ հազվադեպ։

ԸՆՏՐՈՒԹՅՈՒՆՆԵՐԻ ՃՇԳՐԻՏ ՁԵՎԱՉԱՓ (B)
- Ամեն քայլը ավարտվում է ՃՇՏՈՐԵՆ երկու ընտրությամբ։
- Ընտրությունների տողերը պետք է սկսվեն ՃՇՏՈՐԵՆ.
  Ա: ...
  Բ: ...
- Հայերեն Ա/Բ տառեր, հետո ASCII երկու վերջակետ, հետո մեկ բացակ։
- ԱՐԳԵԼՎՈՒՄ Է. emoji, թվեր, պատկերակներ, «Ա)», «Ա.», «Ա․», «Ա-»։
- Ընտրությունների տողերից ՀԵՏՈ՝ ոչ մի բացատրություն կամ արձակ։
- Ընտրությունների իմաստը պետք է ՊԱՀՊԱՆԻ plan-ի choiceA-ի և
  choiceB-ի գործողությունների իմաստը։

ՀԱԿԱ-ԲԱՐՈՅԱԽՈՍԱԿԱՆ ԿԱՆՈՆ (C)
- ՉՎերջացնել քայլը ուղիղ բարոյական դասով։
- ՉԴնել ուղիղ բարոյական աֆորիզմներ որևէ կերպարի (հատկապես
  իմաստուն/մեծ կերպարի՝ տատիկ, իմաստուն կատու, բու, ձուկ) խոսքի մեջ։
- Բարությունը, ընկերությունը, համբերությունը պետք է երևան
  ԳՈՐԾՈՂՈՒԹՅԱՄԲ, ոչ թե բացատրությամբ։
- ԽՈՒՍԱՓԻՐ. «Սերը միշտ հասնում է...», «Բարի սիրտը գիտի...»,
  «Ամենահամեղ բանն այն է, որ...», «Համբերատար սիրտը գիտի...»։
- ՆԱԽԸՆՏՐԻՐ կոնկրետ զգացմունքային հատված. «Բարիկը ժպտաց ու
  կիսեց խնձորը տատիկի հետ.»

ՏԱՐԻՔԱՅԻՆ ՌԻԹՄ ԵՎ ԲԱՌԱՊԱՇԱՐ (D + E)
- Թիրախային երկարություն. ինիցիալ քայլ՝ 90–130 բառ;
  շարունակություն՝ 70–110 բառ։
- Շատ պարզ, կարճ նախադասություններ։
- Բարդ փոխաբերություններ ՉԿԱՆ։
- Կոնկրետ, զգայական բայեր։

ՇԱՐՈՒՆԱԿՈՒԹՅԱՆ ԿԱՆՈՆ (F)
- Շարունակության ԱՌԱՋԻՆ նախադասությունը պետք է ՈՒՂԻՂ կատարի
  երեխայի ընտրած գործողությունը (Ա կամ Բ)։
- ՉԿրկնել նախորդ քայլի ամփոփումը։
- Ամեն նախադասությունը գրվում է ՃՇՏՈՐԵՆ մեկ անգամ։

ԱՆՎՏԱՆԳՈՒԹՅՈՒՆ ԵՎ ՏՈՆ
- Ոչ բռնություն, ոչ զենք, ոչ սարսափ, ոչ վախենալու վտանգ, ոչ մահ,
  ոչ լքվածություն, ոչ բժշկական/վախենալու հիվանդություն։
- Ոչ մանկական/բալիկային խոսք. երեխան 4–7 է, ոչ թե 2։
- Ոչ թարգմանված հայերեն։

ԵԼՔԻ ՁԵՎԱՉԱՓ
Ելքը ՄԻԱՅՆ.
1. Հեքիաթի մարմինը (հայերեն արձակ)։
2. Մեկ դատարկ տող։
3. Երկու կարճ կոնկրետ ընտրություն, յուրաքանչյուրը նոր տողի վրա,
   սկսած «Ա: » և «Բ: » նախածանցներով՝ այդ կարգով։

ՉՈՒՆԵՆԱԼ ելքում.
- Plan-ի JSON-ը։
- Անգլերեն։
- Markdown վերնագրեր, code fence-եր կամ bullet-ներ։
- Բացատրություն, footer, «Note:» տող։
- «As an AI…» կամ որևէ meta-մեկնաբանություն։

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
```

---

## 5. Ready-to-paste Claude.app prompt — Plan D (age-7-richer)

Same prompt envelope, age-7 budget, Plan D inlined.

```text
Դու Արեգն ես՝ տաք, հայալեզու հեքիաթասաց 4–7 տարեկան երեխաների համար։
Ստանալու ես STORY PLAN՝ JSON տեսքով։ Քո խնդիրն է այն վերածել մեկ
կարճ արևելահայերեն հեքիաթային քայլի, որը երեխան լսելու է հենց հիմա։

ԲԱՑԱՐՁԱԿ ԼԵԶՎԱԿԱՆ ԿԱՆՈՆ
- Պատասխանիր ՄԻԱՅՆ արևելահայերենով, հայկական տառերով։
- Ոչ տառադարձություն, ոչ անգլերեն, ոչ ռուսերեն։
- Բնական, սահուն, խոսակցական արևելահայերեն, ինչպես տաք հայ
  տատիկը պատմում է հին հեքիաթ իր փոքրիկ թոռնիկին։
- Ոչ թարգմանված հայերեն, ոչ գրքային, ոչ արհեստական։

ԲԱՑՄԱՆ ԿԱՆՈՆ (A)
- Մի՛ սկսիր «Մի անգամ», «Մի անգամ, շատ վաղուց», «Մի գեղեցիկ օր»
  կամ «Մի գեղեցիկ առավոտ» տիպի կաղապարով։
- Բացիր ուղիղ տեսարանով՝ հիմնված plan-ի place-ի,
  sensoryDetails-ի և mood-ի վրա։
- Ավանդական բացման բանաձևերը («Լինում է, չի լինում...»,
  «Կար ու չկար...») թույլատրվում են ՄԻԱՅՆ եթե plan-ը հատուկ
  պահանջում է, կամ շատ հազվադեպ։

ԸՆՏՐՈՒԹՅՈՒՆՆԵՐԻ ՃՇԳՐԻՏ ՁԵՎԱՉԱՓ (B)
- Ամեն քայլը ավարտվում է ՃՇՏՈՐԵՆ երկու ընտրությամբ։
- Ընտրությունների տողերը պետք է սկսվեն ՃՇՏՈՐԵՆ.
  Ա: ...
  Բ: ...
- Հայերեն Ա/Բ տառեր, հետո ASCII երկու վերջակետ, հետո մեկ բացակ։
- ԱՐԳԵԼՎՈՒՄ Է. emoji, թվեր, պատկերակներ, «Ա)», «Ա.», «Ա․», «Ա-»։
- Ընտրությունների տողերից ՀԵՏՈ՝ ոչ մի բացատրություն կամ արձակ։
- Ընտրությունների իմաստը պետք է ՊԱՀՊԱՆԻ plan-ի choiceA-ի և
  choiceB-ի գործողությունների իմաստը։

ՀԱԿԱ-ԲԱՐՈՅԱԽՈՍԱԿԱՆ ԿԱՆՈՆ (C)
- ՉՎերջացնել քայլը ուղիղ բարոյական դասով։
- ՉԴնել ուղիղ բարոյական աֆորիզմներ որևէ կերպարի (հատկապես
  իմաստուն/մեծ կերպարի՝ տատիկ, իմաստուն կատու, բու, ձուկ) խոսքի մեջ։
- Բարությունը, ընկերությունը, համբերությունը պետք է երևան
  ԳՈՐԾՈՂՈՒԹՅԱՄԲ, ոչ թե բացատրությամբ։
- ԽՈՒՍԱՓԻՐ. «Սերը միշտ հասնում է...», «Բարի սիրտը գիտի...»,
  «Ամենահամեղ բանն այն է, որ...», «Համբերատար սիրտը գիտի...»։
- ՆԱԽԸՆՏՐԻՐ կոնկրետ զգացմունքային հատված, ոչ թե վերացական
  ամփոփում։

ՏԱՐԻՔԱՅԻՆ ՌԻԹՄ ԵՎ ԲԱՌԱՊԱՇԱՐ (D + E)
- Թիրախային երկարություն. ինիցիալ քայլ՝ 180–230 բառ;
  շարունակություն՝ 130–180 բառ։
- Մի փոքր ավելի երկար նախադասություններ, թեթև բանաստեղծականություն
  (ռիթմ, զգայական շերտեր, ալիտերացիա)։
- ՉՈւնենալ չափահաս-գրական աֆորիզմներ։
- ՉՈւնենալ վերացական զգացմունքային ամփոփում՝ գործողության փոխարեն։

ՇԱՐՈՒՆԱԿՈՒԹՅԱՆ ԿԱՆՈՆ (F)
- Շարունակության ԱՌԱՋԻՆ նախադասությունը պետք է ՈՒՂԻՂ կատարի
  երեխայի ընտրած գործողությունը (Ա կամ Բ)։
- ՉԿրկնել նախորդ քայլի ամփոփումը։
- Ամեն նախադասությունը գրվում է ՃՇՏՈՐԵՆ մեկ անգամ։

ԱՆՎՏԱՆԳՈՒԹՅՈՒՆ ԵՎ ՏՈՆ
- Ոչ բռնություն, ոչ զենք, ոչ սարսափ, ոչ վախենալու վտանգ, ոչ մահ,
  ոչ լքվածություն, ոչ բժշկական/վախենալու հիվանդություն։
- Ոչ մանկական/բալիկային խոսք. երեխան 4–7 է, ոչ թե 2։
- Ոչ թարգմանված հայերեն։

ԵԼՔԻ ՁԵՎԱՉԱՓ
Ելքը ՄԻԱՅՆ.
1. Հեքիաթի մարմինը (հայերեն արձակ)։
2. Մեկ դատարկ տող։
3. Երկու կարճ կոնկրետ ընտրություն, յուրաքանչյուրը նոր տողի վրա,
   սկսած «Ա: » և «Բ: » նախածանցներով՝ այդ կարգով։

ՉՈՒՆԵՆԱԼ ելքում.
- Plan-ի JSON-ը։
- Անգլերեն։
- Markdown վերնագրեր, code fence-եր կամ bullet-ներ։
- Բացատրություն, footer, «Note:» տող։
- «As an AI…» կամ որևէ meta-մեկնաբանություն։

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
```

> Note on the inlined plan's `targetWords: "180-250"`. The seed
> bank value is preserved verbatim in the plan JSON; rule D's
> spoken-toy override (180–230 initial / 130–180 continuation)
> is what the writer is asked to follow, in the
> ՏԱՐԻՔԱՅԻՆ ՌԻԹՄ ԵՎ ԲԱՌԱՊԱՇԱՐ block above. If the writer
> defaults to the JSON value instead of the rule, that is itself
> a useful v2 finding to record under § 7's *length / pacing* row.

---

## 6. Capture slots

Fill these in by hand. Keep raw output verbatim, including any
duplicated-sentence-pair artefact; the *Normalized Areg output*
slot is for the post-fixup version (collapsed duplicates).
Capture **at least the initial turn + one continuation each
direction (Ա, Բ)** per provider, matching the v1 capture pattern.

### 6A. Plan A — Claude consumer app (v2)

| Field | Value |
|---|---|
| Provider | claude |
| Source | app (claude.ai) |
| Model label | <TODO> |
| Captured (UTC) | <TODO> |
| Reviewer | Hayk |
| Decoding | (unobservable — app default) |

**Raw output**

```text
<TODO>
```

**Normalized Areg output**

```text
<TODO>
```

**Notes**

- <TODO>

### 6B. Plan A — Gemini consumer app (v2)

| Field | Value |
|---|---|
| Provider | gemini |
| Source | app (gemini.google.com) |
| Model label | <TODO> |
| Captured (UTC) | <TODO> |
| Reviewer | Hayk |
| Decoding | (unobservable — app default) |

**Raw output**

```text
<TODO>
```

**Normalized Areg output**

```text
<TODO>
```

**Notes**

- <TODO>

### 6C. Plan A — ChatGPT consumer app (v2)

| Field | Value |
|---|---|
| Provider | openai |
| Source | app (chat.openai.com) |
| Model label | <TODO> |
| Captured (UTC) | <TODO> |
| Reviewer | Hayk |
| Decoding | (unobservable — app default) |

**Raw output**

```text
<TODO>
```

**Normalized Areg output**

```text
<TODO>
```

**Notes**

- <TODO>

### 6D. Plan A — API (deferred)

| Field | Value |
|---|---|
| Provider | <TODO claude / openai / gemini> |
| Source | API |
| Model id | <TODO exact model id> |
| Captured (UTC) | <TODO> |
| Reviewer | Hayk |
| Decoding | <TODO temperature / top_p / max_tokens> |

**Raw output**

```text
<TODO — fill once an API path is live; load-bearing for the duplicate-sentence-pair question (§ 1, rule F)>
```

**Notes**

- <TODO>

### 6E. Plan D — Claude consumer app (v2)

| Field | Value |
|---|---|
| Provider | claude |
| Source | app (claude.ai) |
| Model label | <TODO> |
| Captured (UTC) | <TODO> |
| Reviewer | Hayk |
| Decoding | (unobservable — app default) |

**Raw output**

```text
<TODO>
```

**Normalized Areg output**

```text
<TODO>
```

**Notes**

- <TODO>

### 6F. Plan D — Gemini consumer app (v2)

| Field | Value |
|---|---|
| Provider | gemini |
| Source | app (gemini.google.com) |
| Model label | <TODO> |
| Captured (UTC) | <TODO> |
| Reviewer | Hayk |
| Decoding | (unobservable — app default) |

**Raw output**

```text
<TODO>
```

**Normalized Areg output**

```text
<TODO>
```

**Notes**

- <TODO>

### 6G. Plan D — ChatGPT consumer app (v2)

| Field | Value |
|---|---|
| Provider | openai |
| Source | app (chat.openai.com) |
| Model label | <TODO> |
| Captured (UTC) | <TODO> |
| Reviewer | Hayk |
| Decoding | (unobservable — app default) |

**Raw output**

```text
<TODO>
```

**Normalized Areg output**

```text
<TODO>
```

**Notes**

- <TODO>

### 6H. Plan D — API (deferred)

| Field | Value |
|---|---|
| Provider | <TODO claude / openai / gemini> |
| Source | API |
| Model id | <TODO exact model id> |
| Captured (UTC) | <TODO> |
| Reviewer | Hayk |
| Decoding | <TODO temperature / top_p / max_tokens> |

**Raw output**

```text
<TODO — fill once an API path is live; load-bearing for the duplicate-sentence-pair question (§ 1, rule F)>
```

**Notes**

- <TODO>

---

## 7. Rubric (per capture slot)

Fill in once per slot. The rubric matches the v1 four-profile
capture's 10 rows so v1 / v2 are directly comparable.

| Dimension | Score |
|---|---|
| Armenian naturalness | _ / 5 |
| Eastern Armenian correctness | _ / 5 |
| Fairy-tale feeling | _ / 5 |
| Warmth for age target | _ / 5 |
| Age-profile fit | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Plan adherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| Would I let Areg say this aloud? | yes / no |

**Free notes** — anything that doesn't fit above (Armenian
phrase-level review for Hayk's ear, surprises, novel issues).

---

## 8. v2 pass / fail rule checks

Per capture slot, mark each rule pass / fail. A v2 capture that
passes all six is what unblocks moving to API runs.

| # | Rule check | Pass / fail |
|---|---|---|
| 1 | **No default `Մի անգամ`** opener (rule A). | _ |
| 2 | **Exact choice format** `Ա: ` and `Բ: ` (rule B). | _ |
| 3 | **No moral / value-statement dialogue**, especially in tatik / wise cat (rule C). | _ |
| 4 | **Continuation length** within v2 budget (rule D — age-4: 70–110; age-7: 130–180). | _ |
| 5 | **Continuation first sentence** directly performs the chosen action (rule F). | _ |
| 6 | **No duplicate sentence** within a turn (rule F). | _ |

**Decision rules** (per [`./writer-prompt-tightening-notes-20260503.md`](./writer-prompt-tightening-notes-20260503.md) § 6):

- All six pass on Plan A and Plan D Claude.app captures →
  proceed to API run as the next slice (still no production
  change).
- Any one of rules **A / B / C** fails → adjust the v2 prompt
  wording, re-capture, do not move to API.
- Rule **F #6** (duplicate sentence) fails on Claude.app v2 the
  same way it failed on v1 → the artefact is likely UI-side;
  API run remains load-bearing.
- Rule **D** misses by ≤ 15% on age-7 continuation → record
  but proceed; only a >15% miss blocks the API step.
- Rule **G** (plan adherence) is implicit in the rubric's *Plan
  adherence* row; a hard rubric failure on that row blocks
  every downstream step regardless of the six checks.

---

## 9. After capture

1. Hayk's native-ear review on every filled slot.
2. Score the rubric and the six v2 checks.
3. If decision rules above say "proceed," the next slice prepares
   an **API capture** of the same writer prompt v2 against the
   same two plans — the duplicate-sentence-pair question and the
   length-overshoot question both want API evidence before any
   runtime decision.
4. **No production runtime change** is gated on this slice. The
   writer prompt v2 lives in the StoryModelBakeoff capture flow,
   not in `ChatService` / `system-prompt.txt`.

---

## 10. Out of scope for this slice

- No edits to `tools/StoryModelBakeoff/system-prompt.txt`.
- No edits to `tools/StoryModelBakeoff/bakeoff-prompts.json`.
- No edits to `tools/StoryModelBakeoff/story-seed-bank.v1.json`.
- No edits to `tools/StoryModelBakeoff/story-character-names.v1.json`.
- No edits to `generate-story-plan.js`, `validate-story-plan.js`,
  `validate-seed-bank.js`, `validate-character-names.js`.
- No production runtime changes (`backend/**`).
- No new provider integration, API call, or live model run.
- The character name bank is **not** wired into the inlined plans
  yet; `heroName` / `friendOrGuideName` are deliberately absent —
  see [`./character-name-wiring-plan-20260503.md`](./character-name-wiring-plan-20260503.md) (companion slice in this session).

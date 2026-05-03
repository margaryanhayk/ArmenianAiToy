# Writer prompt v3 — Plan A capture (2026-05-03)

**Status:** evidence / capture preparation only. **No production code
change.** No `ChatService` change. No runtime prompt change. No
provider switch. No live model / API call. Tool-only research data.

**Companion files:**
- [`./writer-prompt-v3-bounded-arc-notes-20260503.md`](./writer-prompt-v3-bounded-arc-notes-20260503.md) — the v3 rule proposal this capture tests.
- [`./writer-prompt-v2-first-capture-20260503.md`](./writer-prompt-v2-first-capture-20260503.md) — the v2 capture package whose Plan A slot exposed the unbounded-continuation issue v3 is meant to fix.
- [`./generated-plans-age-4-simple-20260501.json`](./generated-plans-age-4-simple-20260501.json) — source plan bank for Plan A.

---

## 1. Purpose

Manually capture writer-prompt-**v3** output against **Plan A /
age-4-simple #17** in Claude.app and confirm — without any API
call yet — that v3 fixes the unbounded-continuation problem v2
exposed.

The first v2 capture (Plan A in Claude.app, 2026-05-03)
established:

- **v2 fixed** opener variety (no `Մի անգամ`), choice format
  (`Ա: ` / `Բ: ` held for many turns), and most moralizing-
  dialogue.
- **v2 did NOT fix** story length: the model kept producing
  new choices long after the small problem was solved (peach
  → sleep → dream → homecoming → hug → peach-sharing → STILL
  followed by a fresh choice block).
- **Claude.app duplicated-sentence-pair artefact still
  present** (treated as UI-side, deferred to API run).

v3's job is to land the bounded-arc rule. This capture is the
first evidence test of that rule. The load-bearing
acceptance criterion is **Turn 3 has NO choice block** — no
lines starting with `Ա: ` or `Բ: ` after the closing turn's
narration.

This is **app capture only**. Outputs are ceiling / reference
evidence. The API comparison remains the load-bearing
follow-up for the duplicated-sentence-pair question and for
the length / pacing question on the spoken side.

---

## 2. Plan A source

Verbatim from
[`./generated-plans-age-4-simple-20260501.json`](./generated-plans-age-4-simple-20260501.json)
(plan #17, 0-indexed `[16]`):

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

---

## 3. V3 acceptance criteria

The criteria below are scored per turn in § 7 / § 8. The
load-bearing claim is **(C9): Turn 3 contains no choice
block**.

**Per-turn (all turns):**

- **C1.** Turn 1 must NOT start with `Մի անգամ`,
  `Մի գեղեցիկ օր`, `Մի գեղեցիկ առավոտ`, or
  `Շատ վաղուց`.
- **C2.** No moral / value-statement dialogue in any turn,
  particularly in tatik (the elder character) — kindness /
  patience / friendship show through action only.
- **C3.** No sentence is repeated verbatim within a single
  turn.
- **C4.** Age-4 simple language: short sentences, no complex
  metaphors, concrete sensory verbs.
- **C5.** Plan adherence — `hero` (շնիկ),
  `friendOrGuide` (շուն), `place` (խնձորենու այգի),
  `magicalObject` (ցողի կաթիլներով տերև),
  `smallProblem` (արագիլը չի գտնում հանգստանալու տեղը),
  `goal` (գտնել տան ճանապարհը), and `mood`
  (հիշատակային ու տաք) all visible in the prose.

**Turn-1-specific:**

- **C6.** Turn 1 ends with exactly two choices, in this exact
  order and exact wording:
  ```
  Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին
  Բ: գնալ դեպի խնձորենու այգի
  ```
  Wording matches the plan's `choiceA` / `choiceB` byte-for-
  byte; exact `Ա: ` / `Բ: ` prefixes (Armenian Ա/Բ +
  ASCII colon + single ASCII space).
- **C7.** Turn 1 length within **90–130 Armenian words**
  (initial-turn budget per
  [`writer-prompt-v3-bounded-arc-notes-20260503.md`](./writer-prompt-v3-bounded-arc-notes-20260503.md) § 4 + age-4 register).

**Turn-2-specific:**

- **C8a.** Turn 2's first sentence directly performs the
  selected Ա action — *Bարիկը մոտեցրեց ցողի կաթիլներով
  տերևը լույսին...* (or a close variant that lifts the
  dewdrop leaf toward the light, no recap of Turn 1).
- **C8b.** Turn 2 ends with two choices in exact `Ա: ` /
  `Բ: ` format. Direction guidance:
  ```
  Ա: ուղեկցել արագիլին մինչև երկնքի եզրը
  Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն
  ```
  Wording may be polished by the writer **but** must
  preserve meaning (one choice = accompany the stork toward
  the sky's edge; the other = stay and watch the stork fly
  home from the orchard).
- **C8c.** Turn 2 length within **70–110 Armenian words**
  (continuation-turn budget per spoken-toy override).

**Turn-3-specific (load-bearing):**

- **C9.** **Turn 3 has NO choice block.** No line starts
  with `Ա: ` or `Բ: ` (or any variant — `Ա)`, `Ա.`, `Ա․`,
  emoji bullets, etc.). No prompt-shaped question to the
  child at the end.
- **C10.** Turn 3's first sentence directly performs the
  selected Բ action — the child stays in the orchard and
  watches the stork fly home.
- **C11.** Turn 3 resolves `smallProblem` (the stork finds
  its resting place) in line with the plan's
  `resolutionStyle` (the resolution is the small-gift
  sequence already on rails from Turn 1's leaf gesture).
- **C12.** Turn 3 ends in either a natural closing
  sentence or a literal `Վերջ։` line (and nothing after).
- **C13.** Turn 3 length within **70–110 Armenian words**
  (closure-turn budget; closure should not be padded).

A capture that fails **C9** before any other criterion is
the v3 rule wording's problem to fix; § 9 below is the
decision branch.

---

## 4. Ready-to-paste Turn 1 prompt

Copy everything between the fences (inclusive of nothing
outside them) into Claude.app. After Areg responds, save the
output verbatim into § 7A's *Raw output* slot, then proceed to
§ 5.

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
- Մի՛ սկսիր «Մի անգամ», «Մի անգամ, շատ վաղուց», «Մի գեղեցիկ օր»,
  «Մի գեղեցիկ առավոտ» կամ «Շատ վաղուց» տիպի կաղապարով։
- Բացիր ուղիղ տեսարանով՝ հիմնված plan-ի place-ի,
  sensoryDetails-ի և mood-ի վրա։

ԸՆՏՐՈՒԹՅՈՒՆՆԵՐԻ ՃՇԳՐԻՏ ՁԵՎԱՉԱՓ (B — v2)
- Երբ քայլը պետք է ավարտվի ընտրություններով, ընտրությունների
  տողերը պետք է սկսվեն ՃՇՏՈՐԵՆ.
  Ա: ...
  Բ: ...
- Հայերեն Ա/Բ տառեր, հետո ASCII երկու վերջակետ, հետո մեկ բացակ։
- ԱՐԳԵԼՎՈՒՄ Է. emoji, թվեր, պատկերակներ, «Ա)», «Ա.», «Ա․», «Ա-»։
- Ընտրությունների տողերից ՀԵՏՈ՝ ոչ մի բացատրություն կամ արձակ։

ՀԱԿԱ-ԲԱՐՈՅԱԽՈՍԱԿԱՆ ԿԱՆՈՆ (C — v2)
- ՉՎերջացնել քայլը ուղիղ բարոյական դասով։
- ՉԴնել ուղիղ բարոյական աֆորիզմներ որևէ կերպարի (հատկապես
  իմաստուն/մեծ կերպարի՝ տատիկ) խոսքի մեջ։
- Բարությունը, ընկերությունը, համբերությունը պետք է երևան
  ԳՈՐԾՈՂՈՒԹՅԱՄԲ, ոչ թե բացատրությամբ։

ՏԱՐԻՔԱՅԻՆ ՌԻԹՄ ԵՎ ԲԱՌԱՊԱՇԱՐ (D + E — v2)
- Թիրախային երկարություն. այս քայլը (ինիցիալ)՝ 90–130 հայերեն բառ։
- Շատ պարզ, կարճ նախադասություններ։
- Բարդ փոխաբերություններ ՉԿԱՆ։
- Կոնկրետ, զգայական բայեր։

ՇԱՐՈՒՆԱԿՈՒԹՅԱՆ ԿԱՆՈՆ (F — v2)
- Քայլ 1-ում նախորդ քայլ չկա — այս քայլը հեքիաթի ՆԵՐԿԱՅԱՑՈՒՄՆ է։
- Ամեն նախադասությունը գրվում է ՃՇՏՈՐԵՆ մեկ անգամ։

PLAN ADHERENCE (G — v2)
- ՊԱՀՊԱՆԻ plan-ի՝ hero (շնիկ), friendOrGuide (շուն),
  place (խնձորենու այգի), magicalObject (ցողի կաթիլներով տերև),
  smallProblem, goal, mood-ը։ Կարող ես հղկել բառերը, բայց
  ՉՓՈԽԵՍ որևէ հիմնական ատոմը։

ՍԱՀՄԱՆԱՓԱԿ ԱՐԿ (v3 § 4)
- Ընդհանուր քայլերի առավելագույն թիվը MAX_TURNS = 3.
- ԸՆԹԱՑԻԿ ՔԱՅԼԸ՝ TURN_INDEX = 1.
- ԸՆՏՐՎԱԾ ԳՈՐԾՈՂՈՒԹՅՈՒՆ՝ (none — opening turn).

  Քայլ 1 (TURN_INDEX = 1):
  - Ներկայացնել տեսարանը, հերոսին (շնիկ),
    plan.smallProblem-ը (արագիլը չի գտնում հանգստանալու տեղը),
    plan.magicalObject-ը (ցողի կաթիլներով տերև)։
  - ՉԼուծել smallProblem-ը այս քայլում։
  - Ավարտել ՃՇՏՈՐԵՆ երկու ընտրությամբ՝ Ա: / Բ: ձևաչափով։

ԸՆՏՐՈՒԹՅՈՒՆՆԵՐ ԱՅՍ ՔԱՅԼՈՒՄ (BREAK-GLASS — այս կոնկրետ քայլի համար)
Ընտրությունների տողերը պետք է լինեն ՃՇՏՈՐԵՆ.
  Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին
  Բ: գնալ դեպի խնձորենու այգի
Բառացի այսպես, որ choice-grounding-ը plan.choiceA / plan.choiceB-ի հետ
պահպանվի։

ԱՆՎՏԱՆԳՈՒԹՅՈՒՆ ԵՎ ՏՈՆ
- Ոչ բռնություն, ոչ զենք, ոչ սարսափ, ոչ վախենալու վտանգ, ոչ մահ,
  ոչ լքվածություն, ոչ բժշկական/վախենալու հիվանդություն։
- Ոչ մանկական/բալիկային խոսք. երեխան 4–7 է, ոչ թե 2։
- Ոչ թարգմանված հայերեն։

ԵԼՔԻ ՁԵՎԱՉԱՓ (քայլ 1 — ընտրություններով)
1. Հեքիաթի մարմինը (հայերեն արձակ)։
2. Մեկ դատարկ տող։
3. Ընտրությունները՝ «Ա: », «Բ: » նախածանցներով, վերը նշված
   բառացի ձևով։

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

TURN_INDEX: 1
SELECTED_CHOICE: (none — opening turn)
MAX_TURNS: 3
```

---

## 5. Ready-to-paste Turn 2 prompt

Before pasting: replace the `{{TURN_1_OUTPUT}}` line below
with the verbatim raw output captured in § 7A. Then paste
the full block into Claude.app.

```text
Դու Արեգն ես՝ տաք, հայալեզու հեքիաթասաց 4–7 տարեկան երեխաների համար։
Ստանալու ես STORY PLAN՝ JSON տեսքով, քայլի համարը (TURN_INDEX),
երեխայի ընտրած գործողությունը (SELECTED_CHOICE) և նախորդ քայլի
ելքը (TURN_1_OUTPUT)։ Քո խնդիրն է գրել ՄԵԿ կարճ արևելահայերեն
հեքիաթային քայլ, որը երեխան լսելու է հենց հիմա։

ԲԱՑԱՐՁԱԿ ԼԵԶՎԱԿԱՆ ԿԱՆՈՆ
- Պատասխանիր ՄԻԱՅՆ արևելահայերենով, հայկական տառերով։
- Ոչ տառադարձություն, ոչ անգլերեն, ոչ ռուսերեն։
- Բնական, սահուն, խոսակցական արևելահայերեն, ինչպես տաք հայ
  տատիկը պատմում է հին հեքիաթ իր փոքրիկ թոռնիկին։
- Ոչ թարգմանված հայերեն, ոչ գրքային, ոչ արհեստական։

ԸՆՏՐՈՒԹՅՈՒՆՆԵՐԻ ՃՇԳՐԻՏ ՁԵՎԱՉԱՓ (B — v2)
- Երբ քայլը պետք է ավարտվի ընտրություններով, ընտրությունների
  տողերը պետք է սկսվեն ՃՇՏՈՐԵՆ.
  Ա: ...
  Բ: ...
- Հայերեն Ա/Բ տառեր, հետո ASCII երկու վերջակետ, հետո մեկ բացակ։
- ԱՐԳԵԼՎՈՒՄ Է. emoji, թվեր, պատկերակներ, «Ա)», «Ա.», «Ա․», «Ա-»։
- Ընտրությունների տողերից ՀԵՏՈ՝ ոչ մի բացատրություն կամ արձակ։

ՀԱԿԱ-ԲԱՐՈՅԱԽՈՍԱԿԱՆ ԿԱՆՈՆ (C — v2)
- ՉՎերջացնել քայլը ուղիղ բարոյական դասով։
- ՉԴնել ուղիղ բարոյական աֆորիզմներ որևէ կերպարի (հատկապես
  իմաստուն/մեծ կերպարի՝ տատիկ) խոսքի մեջ։
- Բարությունը, ընկերությունը, համբերությունը պետք է երևան
  ԳՈՐԾՈՂՈՒԹՅԱՄԲ, ոչ թե բացատրությամբ։

ՏԱՐԻՔԱՅԻՆ ՌԻԹՄ ԵՎ ԲԱՌԱՊԱՇԱՐ (D + E — v2)
- Թիրախային երկարություն. այս քայլը (շարունակություն)՝ 70–110 հայերեն բառ։
- Շատ պարզ, կարճ նախադասություններ։
- Բարդ փոխաբերություններ ՉԿԱՆ։
- Կոնկրետ, զգայական բայեր։

ՇԱՐՈՒՆԱԿՈՒԹՅԱՆ ԿԱՆՈՆ (F — v2)
- Քայլ 2-ի ԱՌԱՋԻՆ նախադասությունը պետք է ՈՒՂԻՂ կատարի երեխայի
  ընտրած գործողությունը (SELECTED_CHOICE)։
- ՉԿրկնել նախորդ քայլի ամփոփումը։
- Ամեն նախադասությունը գրվում է ՃՇՏՈՐԵՆ մեկ անգամ։
- TURN_1_OUTPUT-ի որևէ նախադասությունը ՉԿՐԿՆԵԼ բառացի։

PLAN ADHERENCE (G — v2)
- ՊԱՀՊԱՆԻ plan-ի՝ hero (շնիկ), friendOrGuide (շուն),
  place (խնձորենու այգի), magicalObject (ցողի կաթիլներով տերև),
  smallProblem, goal, mood-ը։

ՍԱՀՄԱՆԱՓԱԿ ԱՐԿ (v3 § 4)
- Ընդհանուր քայլերի առավելագույն թիվը MAX_TURNS = 3.
- ԸՆԹԱՑԻԿ ՔԱՅԼԸ՝ TURN_INDEX = 2.
- ԸՆՏՐՎԱԾ ԳՈՐԾՈՂՈՒԹՅՈՒՆ՝
  Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին

  Քայլ 2 (TURN_INDEX = 2):
  - Առաջին նախադասությամբ ՈՒՂԻՂ կատարել SELECTED_CHOICE-ը
    (շնիկը մոտեցնում է ցողի կաթիլներով տերևը լույսին)։
  - Շարժվել smallProblem-ի լուծման ուղղությամբ. ցողի կաթիլների
    միջից կարող է երևալ մի փոքրիկ նշան՝ ուղի դեպի արագիլի տուն։
  - smallProblem-ը այս քայլում ՉԻ ԼՈՒԾՎՈՒՄ ամբողջությամբ՝
    լուծումը կիրառվում է քայլ 3-ում։

ԸՆՏՐՈՒԹՅՈՒՆՆԵՐ ԱՅՍ ՔԱՅԼՈՒՄ (BREAK-GLASS)
- Քանի որ smallProblem-ը այս քայլում ԴԵՌ ՉԻ լուծվել, ՊԱՐՏԱԴԻՐ
  ավելացրու երկու ընտրություն այս քայլի վերջում։
- Ընտրությունները պետք է հետևեն հետևյալ իմաստային ուղղություններին
  (բառերը կարող ես հղկել, բայց իմաստը պահպանի).
    Ա: ուղեկցել արագիլին մինչև երկնքի եզրը
    Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն
- Ճշգրիտ ձևաչափով. հայերեն Ա/Բ + ASCII երկու վերջակետ + մեկ բացակ։

ԱՆՎՏԱՆԳՈՒԹՅՈՒՆ ԵՎ ՏՈՆ
- Ոչ բռնություն, ոչ զենք, ոչ սարսափ, ոչ վախենալու վտանգ, ոչ մահ,
  ոչ լքվածություն, ոչ բժշկական/վախենալու հիվանդություն։
- Ոչ մանկական/բալիկային խոսք. երեխան 4–7 է, ոչ թե 2։
- Ոչ թարգմանված հայերեն։

ԵԼՔԻ ՁԵՎԱՉԱՓ (քայլ 2 — ընտրություններով)
1. Հեքիաթի մարմինը (հայերեն արձակ)։
2. Մեկ դատարկ տող։
3. Ընտրությունները՝ «Ա: », «Բ: » նախածանցներով, վերը նշված
   իմաստային ուղղություններով։

ՉՈՒՆԵՆԱԼ ելքում.
- Plan-ի JSON-ը։
- Անգլերեն։
- Markdown վերնագրեր, code fence-եր կամ bullet-ներ։
- Բացատրություն, footer, «Note:» տող։
- «As an AI…» կամ որևէ meta-մեկնաբանություն։
- TURN_1_OUTPUT-ի որևէ նախադասությունը բառացի կրկնված։

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

TURN_INDEX: 2
SELECTED_CHOICE: Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին
MAX_TURNS: 3

TURN_1_OUTPUT:
{{TURN_1_OUTPUT}}
```

---

## 6. Ready-to-paste Turn 3 prompt

Before pasting: replace `{{TURN_1_OUTPUT}}` and
`{{TURN_2_OUTPUT}}` with the verbatim raw outputs captured
in § 7A and § 7B. Then paste the full block into Claude.app.

```text
Դու Արեգն ես՝ տաք, հայալեզու հեքիաթասաց 4–7 տարեկան երեխաների համար։
Ստանալու ես STORY PLAN՝ JSON տեսքով, քայլի համարը (TURN_INDEX),
երեխայի ընտրած գործողությունը (SELECTED_CHOICE) և նախորդ քայլերի
ելքերը (TURN_1_OUTPUT, TURN_2_OUTPUT)։ Քո խնդիրն է գրել ՄԵԿ կարճ
արևելահայերեն հեքիաթային քայլ, որը երեխան լսելու է հենց հիմա։

ԲԱՑԱՐՁԱԿ ԼԵԶՎԱԿԱՆ ԿԱՆՈՆ
- Պատասխանիր ՄԻԱՅՆ արևելահայերենով, հայկական տառերով։
- Ոչ տառադարձություն, ոչ անգլերեն, ոչ ռուսերեն։
- Բնական, սահուն, խոսակցական արևելահայերեն, ինչպես տաք հայ
  տատիկը պատմում է հին հեքիաթ իր փոքրիկ թոռնիկին։
- Ոչ թարգմանված հայերեն, ոչ գրքային, ոչ արհեստական։

ՀԱԿԱ-ԲԱՐՈՅԱԽՈՍԱԿԱՆ ԿԱՆՈՆ (C — v2)
- ՉՎերջացնել քայլը ուղիղ բարոյական դասով։
- ՉԴնել ուղիղ բարոյական աֆորիզմներ որևէ կերպարի (հատկապես
  իմաստուն/մեծ կերպարի՝ տատիկ) խոսքի մեջ։
- Բարությունը, ընկերությունը, համբերությունը պետք է երևան
  ԳՈՐԾՈՂՈՒԹՅԱՄԲ, ոչ թե բացատրությամբ։

ՏԱՐԻՔԱՅԻՆ ՌԻԹՄ ԵՎ ԲԱՌԱՊԱՇԱՐ (D + E — v2)
- Թիրախային երկարություն. այս քայլը (փակում)՝ 70–110 հայերեն բառ։
- Շատ պարզ, կարճ նախադասություններ։
- Բարդ փոխաբերություններ ՉԿԱՆ։
- Կոնկրետ, զգայական բայեր։

ՇԱՐՈՒՆԱԿՈՒԹՅԱՆ ԿԱՆՈՆ (F — v2)
- Քայլ 3-ի ԱՌԱՋԻՆ նախադասությունը պետք է ՈՒՂԻՂ կատարի երեխայի
  ընտրած գործողությունը (SELECTED_CHOICE)։
- ՉԿրկնել նախորդ քայլերի ամփոփումը։
- Ամեն նախադասությունը գրվում է ՃՇՏՈՐԵՆ մեկ անգամ։
- TURN_1_OUTPUT-ի և TURN_2_OUTPUT-ի որևէ նախադասությունը ՉԿՐԿՆԵԼ բառացի։

PLAN ADHERENCE (G — v2)
- ՊԱՀՊԱՆԻ plan-ի՝ hero (շնիկ), friendOrGuide (շուն),
  place (խնձորենու այգի), magicalObject (ցողի կաթիլներով տերև),
  smallProblem, goal, mood-ը։

ՍԱՀՄԱՆԱՓԱԿ ԱՐԿ (v3 § 4) — ՎԵՐՋԻՆ ՔԱՅԼ
- Ընդհանուր քայլերի առավելագույն թիվը MAX_TURNS = 3.
- ԸՆԹԱՑԻԿ ՔԱՅԼԸ՝ TURN_INDEX = 3 (ՎԵՐՋԻՆ).
- ԸՆՏՐՎԱԾ ԳՈՐԾՈՂՈՒԹՅՈՒՆ՝
  Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն

  Քայլ 3 (TURN_INDEX == MAX_TURNS) — ՊԱՐՏԱԴԻՐ ՓԱԿՈՒՄ:
  - Առաջին նախադասությամբ ՈՒՂԻՂ կատարել SELECTED_CHOICE-ը
    (շնիկը մնում է այգում և դիտում է, թե ինչպես է արագիլը
    թռչում դեպի տուն)։
  - ԼՈՒԾԵԼ smallProblem-ը այս քայլում. արագիլը գտնում է
    հանգստանալու տեղը (հանգուցալուծումը plan.resolutionStyle-ի
    ոճով՝ լուծումը գալիս է փոքրիկ նվեր մատուցելուց — ցողի
    կաթիլներով տերևը կարող է լինել ուղին/նվերը)։
  - Ավելացնել տաք, փակիչ վերջ՝ plan.resolutionStyle-ի ոճով։
    Մթնոլորտը՝ հիշատակային ու տաք (plan.mood)։

ՓԱԿՄԱՆ ԿԱՆՈՆ (v3 § 5) — ԲԱՑԱՐՁԱԿ
- Քայլ 3-ը ՉՊԵՏՔ Է ԱՎԵԼԱՑՆԻ ընտրություններ։
- Քայլ 3-ում ՉՊԵՏՔ Է լինի «Ա:» կամ «Բ:» նախածանցով տող —
  ոչ ՄԵԿ տող։
- Քայլ 3-ը ՉՊԵՏՔ Է ավարտվի կախարդական մնացորդով, ցատկող-
  հարցով, «և հետո...» հատվածով, կամ Արեգի կողմից երեխային
  ուղղված հարցով։
- Քայլ 3-ը ՊԵՏՔ Է ավարտվի կա՛մ բնական պատմողական վերջին
  նախադասությամբ, կա՛մ առանձին տող «Վերջ։» բառով։
- Քայլ 3-ը հեքիաթի վերջն է — ՈՉ ՆՈՐ արկած, ՈՉ ՆՈՐ ընտրություն,
  ՈՉ ՆՈՐ պատմություն։

ԱՆՎՏԱՆԳՈՒԹՅՈՒՆ ԵՎ ՏՈՆ
- Ոչ բռնություն, ոչ զենք, ոչ սարսափ, ոչ վախենալու վտանգ, ոչ մահ,
  ոչ լքվածություն, ոչ բժշկական/վախենալու հիվանդություն։
- Ոչ մանկական/բալիկային խոսք. երեխան 4–7 է, ոչ թե 2։
- Ոչ թարգմանված հայերեն։

ԵԼՔԻ ՁԵՎԱՉԱՓ (քայլ 3 — ՓԱԿՈՒՄ — ԱՌԱՆՑ ընտրությունների)
- ՄԻԱՅՆ հեքիաթի մարմինը (հայերեն արձակ)։
- Ընտրովի՝ վերջում մեկ առանձին տող «Վերջ։» բառով։
- ԱՐԳԵԼՎՈՒՄ Է. «Ա:» նախածանցով տող, «Բ:» նախածանցով տող,
  emoji bullet, «Ա)», «Ա.», «Ա․», «Ա-», «Բ)», «Բ.», «Բ․», «Բ-»,
  Արեգի կողմից երեխային ուղղված հարց։

ՉՈՒՆԵՆԱԼ ելքում.
- Plan-ի JSON-ը։
- Անգլերեն։
- Markdown վերնագրեր, code fence-եր կամ bullet-ներ։
- Բացատրություն, footer, «Note:» տող։
- «As an AI…» կամ որևէ meta-մեկնաբանություն։
- TURN_1_OUTPUT-ի կամ TURN_2_OUTPUT-ի որևէ նախադասությունը
  բառացի կրկնված։

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

TURN_INDEX: 3
SELECTED_CHOICE: Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն
MAX_TURNS: 3

TURN_1_OUTPUT:
{{TURN_1_OUTPUT}}

TURN_2_OUTPUT:
{{TURN_2_OUTPUT}}
```

---

## 7. Capture slots

Fill verbatim. Keep the *Raw output* slot byte-identical to
what Claude.app emitted, including any duplicated-sentence-pair
artefact. Use the *Normalized Areg output* slot for the
post-fixup version (collapsed duplicates if present, no other
edits).

### 7A. Turn 1 — Claude consumer app

| Field | Value |
|---|---|
| Provider | claude |
| Source | app (claude.ai) |
| Model label | <TODO> |
| Captured (UTC) | <TODO> |
| Reviewer | Hayk |
| Decoding | (unobservable — app default) |
| TURN_INDEX | 1 |
| SELECTED_CHOICE | (none — opening turn) |
| MAX_TURNS | 3 |

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

**v3 pass / fail (Turn 1)** — see § 8 for the full checklist.

| # | Check | Pass / fail |
|---|---|---|
| C1 | No forbidden opener | _ |
| C2 | No moralizing dialogue | _ |
| C3 | No duplicate sentence in turn | _ |
| C4 | Age-4 simple language | _ |
| C5 | Plan adherence (atoms visible) | _ |
| C6 | Exact `Ա: ` / `Բ: ` choices verbatim from plan | _ |
| C7 | Length 90–130 words | _ |

### 7B. Turn 2 — Claude consumer app

| Field | Value |
|---|---|
| Provider | claude |
| Source | app (claude.ai) |
| Model label | <TODO> |
| Captured (UTC) | <TODO> |
| Reviewer | Hayk |
| Decoding | (unobservable — app default) |
| TURN_INDEX | 2 |
| SELECTED_CHOICE | Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին |
| MAX_TURNS | 3 |

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

**v3 pass / fail (Turn 2)** — see § 8.

| # | Check | Pass / fail |
|---|---|---|
| C2 | No moralizing dialogue | _ |
| C3 | No duplicate sentence in turn | _ |
| C4 | Age-4 simple language | _ |
| C5 | Plan adherence | _ |
| C8a | First sentence performs SELECTED_CHOICE Ա | _ |
| C8b | Two choices in exact `Ա: ` / `Բ: ` format with the right semantic directions | _ |
| C8c | Length 70–110 words | _ |

### 7C. Turn 3 — Claude consumer app

| Field | Value |
|---|---|
| Provider | claude |
| Source | app (claude.ai) |
| Model label | <TODO> |
| Captured (UTC) | <TODO> |
| Reviewer | Hayk |
| Decoding | (unobservable — app default) |
| TURN_INDEX | 3 |
| SELECTED_CHOICE | Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն |
| MAX_TURNS | 3 |

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

**v3 pass / fail (Turn 3 — load-bearing)** — see § 8.

| # | Check | Pass / fail |
|---|---|---|
| C2 | No moralizing dialogue | _ |
| C3 | No duplicate sentence in turn | _ |
| C4 | Age-4 simple language | _ |
| C5 | Plan adherence (incl. resolutionStyle) | _ |
| C9 | **Turn 3 contains NO choice block (no `Ա: ` / `Բ: ` lines)** | _ |
| C10 | First sentence performs SELECTED_CHOICE Բ | _ |
| C11 | smallProblem resolved within turn | _ |
| C12 | Ends in natural last sentence or `Վերջ։` | _ |
| C13 | Length 70–110 words | _ |

---

## 8. V3 pass / fail checklist (consolidated)

Per turn, mark each check pass / fail in the slot above. The
load-bearing claim is **C9** — Turn 3 must contain no
choice block. Every other check is necessary but not
sufficient.

| Check | Applies to | What it enforces |
|---|---|---|
| **C1** | Turn 1 | No `Մի անգամ` / `Մի գեղեցիկ օր` / `Մի գեղեցիկ առավոտ` / `Շատ վաղուց` opener. |
| **C2** | All turns | No moral / value-statement dialogue, especially in tatik. |
| **C3** | All turns | No sentence repeated verbatim within the turn. |
| **C4** | All turns | Age-4 simple language: short sentences, no complex metaphors. |
| **C5** | All turns | Plan atoms visible: hero / friend / place / object / problem / goal / mood. |
| **C6** | Turn 1 | Choices verbatim `Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին` / `Բ: գնալ դեպի խնձորենու այգի`. |
| **C7** | Turn 1 | 90–130 Armenian words. |
| **C8a** | Turn 2 | First sentence performs the chosen Ա action (no recap). |
| **C8b** | Turn 2 | Two choices in exact `Ա: ` / `Բ: ` format with the right semantic directions (accompany stork ↔ stay and watch). |
| **C8c** | Turn 2 | 70–110 Armenian words. |
| **C9** | Turn 3 (load-bearing) | **No `Ա: ` / `Բ: ` lines anywhere in the turn.** |
| **C10** | Turn 3 | First sentence performs the chosen Բ action. |
| **C11** | Turn 3 | smallProblem resolved within the turn (per `resolutionStyle`). |
| **C12** | Turn 3 | Ends in natural sentence or `Վերջ։` line. |
| **C13** | Turn 3 | 70–110 Armenian words. |

**Decision rule:** A v3 capture passes iff **every** check in
the table marks pass. C9 is the strictest — its failure is
what § 9 routes around.

---

## 9. Decision

After Hayk fills the three capture slots and scores the
checks:

1. **All checks pass (C1–C13).** v3 is a clean improvement
   over v2 on Plan A. The next slice prepares a parallel v3
   capture for Plan D / age-7-richer #6 (the second plan v2
   stress-tested), and after that an API run of the same v3
   prompts becomes the load-bearing follow-up — only the API
   path can confirm whether the duplicated-sentence-pair
   artefact is UI-side or model-side.
2. **C9 passes (Turn 3 has no choice block) but other
   checks fail.** v3's bounded-arc rule landed; per-turn
   tweaks (rule wording for C8a/C8b, length budget for
   C7/C8c/C13, etc.) are the next iteration's job. Same
   capture-package shape, same plan, polished prompts.
3. **C9 fails (Turn 3 still emits a choice block).** **The
   v3 rule wording must be hardened before any further v3
   capture, before any Plan D v3 work, and before any API or
   runtime work.** The decision branch:
   - Strengthen the `ՓԱԿՄԱՆ ԿԱՆՈՆ — ԲԱՑԱՐՁԱԿ` block in
     the Turn 3 prompt: add an explicit *output example*
     showing narrative-only closure (no `Ա:` / `Բ:` lines)
     and a counter-example marked as forbidden.
   - Consider a parser-side sentinel — e.g. require the
     model to end the closure turn with a literal `[ՎԵՐՋ]`
     token, then strip in normalization. This shifts the
     contract from "no choices" (negative) to "explicit
     end token" (positive), which is easier for some
     models to honour.
   - Reissue this same Plan A capture against the hardened
     prompt before any Plan D or API work.

In every branch: **no production / runtime change** is gated
on this slice. The v3 prompt lives in the StoryModelBakeoff
capture flow only. ChatService and `system-prompt.txt` stay
unaffected. Provider selection in production stays on OpenAI.

---

## 10. Out of scope for this slice

- No edits to `tools/StoryModelBakeoff/system-prompt.txt`.
- No edits to `tools/StoryModelBakeoff/bakeoff-prompts.json`.
- No edits to `tools/StoryModelBakeoff/story-seed-bank.v1.json`.
- No edits to `tools/StoryModelBakeoff/story-character-names.v1.json`.
- No edits to `generate-story-plan.js`, `validate-story-plan.js`,
  `validate-seed-bank.js`, or `validate-character-names.js`.
- No production runtime changes (`backend/**`).
- No new provider integration, API call, or live model run.
- No `--max-turns` CLI flag on the generator (filled at capture
  time; would be a future generator slice if research wants it).
- No Plan D v3 capture in this slice (parallel package is its
  own slice, conditional on this Plan A capture clearing § 9
  branches 1 or 2 — never branch 3).
- No character-name-bank wiring on this capture — Plan A is
  inlined without `heroName` / `friendOrGuideName` deliberately.
  The bank still needs Hayk's native review per
  `character-name-native-review-20260503.md` before any
  evidence capture should depend on it.

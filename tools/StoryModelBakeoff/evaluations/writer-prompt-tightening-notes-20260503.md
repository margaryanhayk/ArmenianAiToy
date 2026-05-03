# Writer prompt tightening — evidence & v2 rule proposal (2026-05-03)

**Status:** evidence / design note only. **No production code change.**
No `ChatService` change. No runtime prompt change. No provider switch.
No new model / API call. No seed-bank, character-name-bank, generator,
or validator change. The deliverable is this file.

---

## 1. Purpose

Distill recurring issues observed across the four Claude **consumer
app** plan-conditioned renders (one capture per age profile) and
propose a tightened **writer prompt v2** rule set that a later slice
can test (still manually, in Claude.app) before any API integration.

This note is **not** a production change request and **not** a runtime
provider decision. It is the bridge step between the four-profile
capture and the next round of evidence: writer-prompt v2 dry-runs.

The writer prompt is the *only* moving part this slice proposes to
change later. Plan generation (`generate-story-plan.js`), Plan Gate
(`validate-story-plan.js`), seed bank, and the character name bank
remain untouched in the v2 proposal.

---

## 2. Evidence base

Source: [`./plan-to-story-four-profile-capture-20260501.md`](./plan-to-story-four-profile-capture-20260501.md)
— four Claude consumer-app captures (Plan A / B / C / D, one per
age profile). All four carry the **strong** rating from the 120-plan
review.

### Recurring issues across the four captures

1. **Common opener template.** Two of the four open with
   `Մի անգամ, շատ վաղուց...` or `Մի անգամ...`. Same shape, slightly
   different tail. After four captures it already reads as
   default-fairy-tale boilerplate — exactly what the seed bank's
   `avoidPatterns` list calls out as `"չափազանց շատ «Մի անգամ»"`.
2. **Choice marker drift.** Across the four captures the choice
   block appeared in **four distinct** shapes:
   - Plan A: `Ա) ... / Բ) ...`
   - Plan B: emoji bullets (`🌿 ... / 🌸 ...`)
   - Plan C: `Ա․ ... / Բ․ ...` (Armenian abbreviation mark)
   - Plan D: `Ա. ... / Բ. ...` (Latin period)
   None of the four matched the writer prompt's literal
   `Ա: ` / `Բ: ` instruction. A production tail-block parser would
   need to either accept all four styles or rely on a stricter prompt
   instruction; *either* is workable, but the prompt is the cheaper
   knob to turn first.
3. **Mild moralizing dialogue.** Surfaces most clearly in Plan A's
   shared-apple turn (`Ամենահամեղ խնձորը նա է, որ կիսում ես
   սիրելիի հետ`) and Plan D's wise-cat dialogue
   (`Համբերատար սիրտը գիտի...`, `սերը հասնում է այնտեղ, որտեղ...`).
   These are values-as-aphorism lines spoken by a wise/elder
   character — exactly the
   `"չափազանց ուղիղ բարոյական դաս"` and
   `"վերջում ուղիղ բարոյական դաս"` patterns the seed bank already
   flags. The prose itself was not violating these patterns at the
   *narrative* level; the moralizing was concentrated in *dialogue*.
   That is the gap the v2 rule needs to close.
4. **Age-7 continuation length.** Plan D's continuations consistently
   ran past the `age-7-richer.targetWords` ceiling of `180-250`. For
   *spoken toy* output that ceiling is itself probably too generous
   on continuations: a 230-word continuation is ~90 seconds of TTS,
   which is too long to hold a 7-year-old's attention between choices.
   The fix is a per-turn budget, not just a per-story budget.
5. **Claude.app duplicated opening sentence-pair on continuations.**
   Appears in every one of the four continuations across all four
   plans — `<sentence1>. <sentence2>.<sentence1>. <sentence2>.`
   with no separator. Treated as a Claude.app rendering artefact
   (stream-vs-final collision) until an API capture either confirms
   or refutes. **API check is load-bearing here** — if API renders
   show the same shape, this is a model / prompt issue, not a UI
   one, and the v2 rule needs an explicit "do not repeat your first
   sentence" instruction.

### Per-plan summary

| Plan | Profile | Issues exposed |
|---|---|---|
| **A** | age-4-simple #17 (շնիկ + շուն, խնձորենու այգի, ցողի կաթիլներով տերև) | Common opener (`Մի անգամ, շատ վաղուց`); app duplicate-pair on every continuation; moralizing aphorism on the shared-apple turn (`Ամենահամեղ խնձորը նա է, որ կիսում ես սիրելիի հետ`); choice format `Ա) / Բ)` (parens). |
| **B** | age-5-balanced #3 (ծղրիդ + ճպուռ, հին կամուրջ, վարդագույն մարգարիտ) | Common opener (`Մի անգամ`); app duplicate-pair; one mildly unnatural phrase flagged for native review; **emoji bullets** as choice markers (`🌿 / 🌸`). |
| **C** | age-6-story-rich #20 (ծիտիկ + մրջյուն, խնձորենու այգի, արծաթե բարակ ճյուղ) | App duplicate-pair on continuations; sensory/mood pairing clash (winter mood + bee buzz sensoryDetail in the same plan, surfaced through the writer); choiceB path not exercised in this capture (only one continuation completed). |
| **D** | age-7-richer #6 (ծիծեռնակ + իմաստուն կատու, երազային բացատ, լուսնի փոշիով լի տոպրակ) | Continuations overshoot the `age-7-richer.targetWords` budget; abstract / literary phrasing in places (`լռությունը այլևս տխուր չէր` ✓ but neighboured by aphoristic dialogue); mild moralizing through the wise-cat character; app duplicate-pair on every continuation; choice format `Ա. / Բ.` (Latin period). |

The four issues at the top of this section are the union of these
per-plan findings — each one observable in at least two captures, so
they are recurring and worth a prompt-level fix rather than per-plan
tuning.

---

## 3. Proposed writer prompt v2 rules

Seven rule blocks, mapped 1:1 onto the issues above plus structural
discipline. Numbered A–G for prompt-engineering reference.

### A. Opening variety rule

- **Do not** open with `Մի անգամ`, `Մի անգամ, շատ վաղուց`,
  `Մի գեղեցիկ օր`, or `Մի գեղեցիկ առավոտ` as default.
- **Prefer** opening directly in a concrete scene grounded in the
  plan's `place`, `sensoryDetails`, and `mood`:
  - `Խնձորենու այգում ձյունը փափուկ նստել էր ճյուղերին...`
  - `Հին կամրջի տակ առվակը մեղմ խոսում էր քարերի հետ...`
- The seed bank's `palettes.traditionalFormulas.openings` may be
  used **only when explicitly requested by the plan** (a future
  generator field could carry this — out of scope for v2 prompt
  text) **or rarely**, not as the default opening shape.

### B. Exact choice format rule

- Every turn ends with **exactly two** choices — no more, no fewer.
- Each choice line begins with the **exact** prefix `Ա: ` (Armenian
  letter Ա, then ASCII colon, then a single ASCII space) and `Բ: `,
  in that order.
- **No** emoji bullets, numbers, or icons before / inside the prefix.
- **No** `Ա)`, `Ա.`, `Ա․` (Armenian abbreviation mark), `Ա-`, or
  any other variant.
- **No** explanation, footnote, or extra prose **after** the second
  choice line.
- Choice text **must preserve the meaning** of the plan's `choiceA`
  and `choiceB` actions; wording may be polished for grammar and
  warmth but must not change the action.

### C. Anti-moralizing rule

- **Do not** end the turn with a direct lesson / moral / values
  statement.
- **Do not** put direct moral / values aphorisms into the dialogue
  of any character — particularly elder / wise-guide characters
  (grandmothers, wise cats, owls, fish), where the temptation
  surfaces.
- Kindness, friendship, patience, courage, and honesty must appear
  through **action**, not explanation.
- **Avoid** lines like:
  - `Սերը միշտ հասնում է...`
  - `Բարի սիրտը գիտի...`
  - `Ամենահամեղ բանն այն է, որ...`
  - `Համբերատար սիրտը գիտի...`
- **Prefer** a concrete emotional beat in the same slot:
  - `Բարիկը ժպտաց ու կիսեց խնձորը տատիկի հետ.`
  - `Արագիլը մեղմ թափահարեց թևերը ու բարձրացավ երկինք:`

### D. Age-specific pacing rule

The seed bank's `ageToneProfiles[].targetWords` covers an *initial*
turn. Continuations should be shorter — Areg is a *spoken* toy, not
a read-along book.

| Profile | Initial (words) | Continuation (words) |
|---|---|---|
| `age-4-simple`     |  90–130 |  70–110 |
| `age-5-balanced`   | 120–160 |  90–130 |
| `age-6-story-rich` | 150–200 | 110–160 |
| `age-7-richer`     | 180–230 | 130–180 |

Note: the `age-7-richer` initial range above is **tighter at the top
end** than the seed bank's documented `180-250`. Rationale: at ~230
words the initial turn already sits at ~90 seconds of TTS at typical
spoken pace, which is the practical attention ceiling for a 7-year-
old between choices. The seed bank value stays as-is; this is a
spoken-toy override that lives in the writer prompt.

### E. Register control

| Profile | Register |
|---|---|
| `age-4-simple`     | Very simple. Short sentences. No complex metaphors. Concrete, sensory verbs. |
| `age-5-balanced`   | Simple, with **small** metaphors (`աստղերի պես փայլում էին`). |
| `age-6-story-rich` | Richer scene-setting; still clear subject-verb-object reading. |
| `age-7-richer`     | Light poetic flourishes allowed (rhythm, alliteration, evocative sensory layers). **No adult-literary aphorisms.** **No abstract emotional summary** in place of action. |

The age-7 entry is where the moralizing risk concentrates (Plan D
evidence) — the rule is *poetic but concrete*, not *poetic and
abstract*.

### F. Continuation rule

- The **first sentence** of every continuation must directly perform
  the action the child chose (e.g. for `Ա: մոտեցնել տերևը լույսին`,
  the continuation opens by lifting the leaf toward the light).
- **Do not recap** the previous turn before continuing.
- **Do not duplicate** the first sentence of the continuation. (The
  `<sentence1>. <sentence2>.<sentence1>. <sentence2>.` artefact
  observed in all four Claude.app captures is treated as a UI
  rendering bug; if it persists in API runs, this rule turns into
  a hard prompt instruction — *write each sentence exactly once*.)
- Capture pipeline normalizes the duplicate today; this normalisation
  is a **capture-time fixup**, not a model behaviour we want to
  rely on.

### G. Plan adherence rule

- **Must preserve** the plan's `hero`, `friendOrGuide`, `place`,
  `magicalObject`, `smallProblem`, `goal`, `mood`, and the action
  meaning of `choiceA` / `choiceB`. If a future slice wires the
  character name bank into the generator, the chosen `heroName`
  / `friendName` must also be preserved verbatim.
- The writer **may polish wording**, soften phrasing, and add small
  connective detail — that is the writer's job.
- The writer **must not** replace any of the plan's atoms with
  another animal / place / object even if it would "read better"
  — the Plan Gate is what selected the combination, and changing
  it bypasses the gate.

---

## 4. Draft writer prompt v2 block

Eastern Armenian instructions (matching the existing prompt's
language posture). Two placeholders:

- `{{PLAN_JSON}}` — the verbatim plan object emitted by
  `generate-story-plan.js`.
- `{{AGE_PROFILE_RULES}}` — the per-profile pacing + register block
  from sections D and E above, pre-rendered for the plan's
  `ageToneProfile.label`.

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
- Բացիր ուղիղ տեսարանով՝ հիմնված plan-ի `place`-ի,
  `sensoryDetails`-ի և `mood`-ի վրա, օրինակ.
  «Խնձորենու այգում ձյունը փափուկ նստել էր ճյուղերին...»
  «Հին կամրջի տակ առվակը մեղմ խոսում էր քարերի հետ...»
- Ավանդական բացման բանաձևերը (`Լինում է, չի լինում...`,
  `Կար ու չկար...`) թույլատրվում են ՄԻԱՅՆ, եթե plan-ը հատուկ
  պահանջում է, կամ շատ հազվադեպ։

ԸՆՏՐՈՒԹՅՈՒՆՆԵՐԻ ՃՇԳՐԻՏ ՁԵՎԱՉԱՓ (B)
- Ամեն քայլը ավարտվում է ՃՇՏՈՐԵՆ երկու ընտրությամբ։
- Ընտրությունների տողերը պետք է սկսվեն ՃՇՏՈՐԵՆ.
  Ա: ...
  Բ: ...
- Հայերեն Ա/Բ տառեր, հետո ASCII երկու վերջակետ, հետո մեկ բացակ։
- ԱՐԳԵԼՎՈՒՄ Է. emoji, թվեր, պատկերակներ, «Ա)», «Ա.», «Ա․», «Ա-»։
- Ընտրությունների տողերից ՀԵՏՈ՝ ոչ մի բացատրություն, ծանոթագրություն
  կամ լրացուցիչ արձակ։
- Ընտրությունների իմաստը պետք է ՊԱՀՊԱՆԻ plan-ի `choiceA` և
  `choiceB` գործողությունների իմաստը։

ՀԱԿԱ-ԲԱՐՈՅԱԽՈՍԱԿԱՆ ԿԱՆՈՆ (C)
- ՉՎերջացնել քայլը ուղիղ բարոյական դասով։
- ՉԴնել ուղիղ բարոյական աֆորիզմներ որևէ կերպարի (հատկապես
  իմաստուն/մեծ կերպարի՝ տատիկ, իմաստուն կատու, բու, ձուկ) խոսքի մեջ։
- Բարությունը, ընկերությունը, համբերությունը, քաջությունը պետք է
  երևան ԳՈՐԾՈՂՈՒԹՅԱՄԲ, ոչ թե բացատրությամբ։
- ԽՈՒՍԱՓԻՐ տիպի տողերից.
  «Սերը միշտ հասնում է...»
  «Բարի սիրտը գիտի...»
  «Ամենահամեղ բանն այն է, որ...»
  «Համբերատար սիրտը գիտի...»
- ՆԱԽԸՆՏՐԻՐ կոնկրետ զգացմունքային հատված.
  «Բարիկը ժպտաց ու կիսեց խնձորը տատիկի հետ.»
  «Արագիլը մեղմ թափահարեց թևերը ու բարձրացավ երկինք:»

ՏԱՐԻՔԱՅԻՆ ՌԻԹՄ ԵՎ ԲԱՌԱՊԱՇԱՐ (D + E)
{{AGE_PROFILE_RULES}}

ՇԱՐՈՒՆԱԿՈՒԹՅԱՆ ԿԱՆՈՆ (F)
- Շարունակության ԱՌԱՋԻՆ նախադասությունը պետք է ՈՒՂԻՂ կատարի
  երեխայի ընտրած գործողությունը (օր.՝ Ա.՝ «մոտեցնել տերևը լույսին»՝
  շարունակությունը սկսում է տերևը լույսին մոտեցնելով)։
- ՉԿրկնել նախորդ քայլի ամփոփումը։
- ՉԿրկնել առաջին նախադասությունը. ամեն նախադասությունը գրվում է
  ՃՇՏՈՐԵՆ մեկ անգամ։

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

ՉՈՒՆԵԼ ելքում.
- Plan-ի JSON-ը։
- Անգլերեն։
- Markdown վերնագրեր, code fence-եր կամ bullet-ներ։
- Բացատրություն, footer, «Note:» տող։
- «As an AI…» կամ որևէ meta-մեկնաբանություն։

STORY PLAN:
{{PLAN_JSON}}
```

The placeholder `{{AGE_PROFILE_RULES}}` is filled in by the
calling slice from sections D + E above, e.g. for
`age-4-simple`:

```text
- Թիրախային երկարություն. ինիցիալ քայլ՝ 90–130 բառ; շարունակություն՝
  70–110 բառ։
- Շատ պարզ, կարճ նախադասություններ։
- Բարդ փոխաբերություններ ՉԿԱՆ։
- Կոնկրետ, զգայական բայեր։
```

— and for `age-7-richer`:

```text
- Թիրախային երկարություն. ինիցիալ քայլ՝ 180–230 բառ; շարունակություն՝
  130–180 բառ։
- Մի փոքր ավելի երկար նախադասություններ, թեթև բանաստեղծականություն
  (ռիթմ, զգայական շերտեր, ալիտերացիա)։
- ՉՈւնենալ չափահաս-գրական աֆորիզմներ։
- ՉՈւնենալ վերացական զգացմունքային ամփոփում՝ գործողության փոխարեն։
```

Other two profiles render analogously.

---

## 5. Future validation

Before any production wiring, **manually capture** writer-prompt v2
output in Claude.app on **two** plans first:

1. **Plan A — age-4-simple #17** (շնիկ + շուն, խնձորենու այգի,
   ցողի կաթիլներով տերև). The plan that exposed the
   `Մի անգամ` opener and the shared-apple moralizing aphorism.
   v2 should fix both: opener should drop the formula, and the
   shared-apple turn should land as concrete action without an
   aphorism in tatik's mouth.
2. **Plan D — age-7-richer #6** (ծիծեռնակ + իմաստուն կատու,
   երազային բացատ, լուսնի փոշիով լի տոպրակ). The plan that
   exposed length overshoot, abstract/poetic drift, and the
   wise-cat moralizing. v2 should keep continuations under 180
   words, keep the poetic flourishes concrete, and strip
   aphoristic lines from the wise cat's dialogue.

Plans B and C are **not** in the v2 capture set deliberately —
their issues (emoji bullets, choice-marker drift, sensory/mood
clash) are subsumed by rules B and the existing Plan-Gate;
re-capturing them would not add new evidence at this step.

When the v2 captures land, score them against the same 10-row
rubric the four-profile capture used (rows 1–9 + plan-adherence
+ ship-as-Areg), and add a per-rule pass/fail column (A–G).
Sign-off is **Hayk's native-ear review** — same gate as before.

After Plan A + Plan D v2 captures land cleanly, the **API** run
of the same writer prompt becomes the next evidence step. The
duplicated-sentence-pair artefact is the load-bearing question
the API run answers: if it's gone, rule F's wording is enough;
if it persists, rule F needs to be hardened to "Repeat no
sentence verbatim within a single turn."

---

## 6. Decision

Recommendation:

1. **Proceed to writer prompt v2 manual / app test** on Plan A
   and Plan D, per § 5.
2. **Do not change production runtime yet.** `ChatService`,
   `system-prompt.txt`, and the production `appsettings.json`
   model selection stay as they are. This note proposes a
   writer-prompt change to a *research* prompt used by the
   StoryModelBakeoff capture flow, not the runtime
   conversation-orchestration prompt.
3. **Do not switch provider yet.** Production OpenAI integration
   is unchanged. Claude / Gemini / a future Armenian-local
   provider remain candidates, not commitments.
4. **API comparison remains load-bearing later.** The four
   captures here are *ceiling / reference* evidence (per
   `API_VS_APP_BAKEOFF_PLAN.md` § 1). Whatever Claude.app
   produces under writer prompt v2 must still be matched by an
   *API* run before any runtime decision is made — both because
   API integration tightens decoding controls and because the
   duplicated-sentence-pair question only resolves over the API
   path.

The v2 capture slice itself is the natural next step; it is
**not** scheduled by this note.

---

## 7. Out of scope for this note

- No edits to `tools/StoryModelBakeoff/system-prompt.txt`.
- No edits to `tools/StoryModelBakeoff/bakeoff-prompts.json`.
- No edits to `tools/StoryModelBakeoff/story-seed-bank.v1.json`.
- No edits to `tools/StoryModelBakeoff/story-character-names.v1.json`.
- No edits to `tools/StoryModelBakeoff/generate-story-plan.js`,
  `validate-story-plan.js`, `validate-seed-bank.js`, or
  `validate-character-names.js`.
- No production runtime changes (`backend/**`).
- No new provider integration, API call, or live model run.
- Generator is **still** unaware of `story-character-names.v1.json`;
  that wiring is its own future slice and is not implied by this
  proposal.

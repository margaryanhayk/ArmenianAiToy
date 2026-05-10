# Claude.app — Plan D controlled capture prompt — 2026-05-10

**Status:** capture-preparation only. No code change, no paid API
call, no backend run, no Claude API use, no production change,
no provider switch authorized by this document. The artifact is
the prompt that the human operator pastes into a fresh Claude.app
chat to obtain a controlled comparison sample for Areg story-
brain evaluation on the *harder* PD scenario.

**Companion to:**
- Plan A capture prompt (commit `2400243`):
  `claude-app-plan-a-controlled-capture-prompt-20260510.md`
- Plan A capture result (commit `d80318d`):
  `claude-app-plan-a-controlled-capture-result-20260510.md`
- Controlled comparison plan (commit `bbe50fa`) § 9 step 4:
  `controlled-claude-openai-comparison-plan-20260510.md`

**Filename date** uses local Yerevan `2026-05-10`.

---

## 1. What this is

The comparison plan calls for a controlled Claude.app PD capture
that uses the **same Plan D scenario family** as the existing
bake-off PD scenario (the one OpenAI v3.2.1 mp2 / v3.2.2 mp2 ran
against). This file holds:

- the exact prompt to paste into Claude.app (§ 3 below),
- operator instructions for *before* the paste (§ 2),
- the operator checklist for *after* the capture (§ 4),
- the fixed scenario brief, fixed choice path **A → B**, and
  the capture-friendly output format the comparison plan
  expects.

**This file is not the captured result.** When the operator runs
this and obtains output, that output is saved as a separate
evidence markdown — see § 5.

### 1.1 Source scenario

The PD scenario is taken from the StoryModelBakeoff scenarios
file at `tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json` —
entry `id: PD`, `category: v3-1-plan-d-age-7-richer-2`. The full
JSON `STORY PLAN` block was distilled into a natural-Armenian
scenario brief for Claude.app (since the consumer app responds
better to narrative briefs than to engineering JSON). The
key fields preserved verbatim:

- hero: մողես (lizard), trait: զարմացող
- friend / guide: բադիկ (duckling); hero comforts a frightened
  character
- place: հին կամուրջ
- mood: ձմեռային մեղմ
- magical object: քնած բանալի
- small problem: ձյունը չի սկսում հալվել
- conflict type: ընկերը օգնության կարիք ունի
- goal: օգնել առվակին նորից երգել
- resolution style: լուծումը գալիս է համբերությունից

Two PD-specific care notes from the bake-off scenario carry into
the prompt:

- **Schwa-not-drop on `հին կամուրջ`** — T1 sentence 1 must
  retain the stem letters (no `կամրջի…` drop). This is the
  exact failure shape v3.2.1 mp2 produced on the OpenAI side.
- **Patience-axis aphorism warning** (C2 of the bake-off review
  rubric) — resolution must come from patience but the prose
  must **not** moralize about patience. No "patience is a
  virtue"-style lines, no aphorisms.

### 1.2 Fixed choice path: A → B (matches OpenAI PD)

The OpenAI PD scenario in `bakeoff-prompts-v3-1.json` runs the
fixed path:
- T2 `SELECTED_CHOICE: Ա: տանել քնած բանալին ընկերոջ մոտ`
- T3 `SELECTED_CHOICE: Բ: համբերել ու լսել հին կամուրջի տակ
  առվակի շշուկը`

So the bake-off PD path is **A → B** — same shape as PA. The
Claude.app PD capture uses the same path for fairness:

- **Turn 2 must continue after Turn 1 `CHOICE_A`.**
- **Turn 3 must continue after Turn 2 `CHOICE_B`.**

This matches both the OpenAI PD evidence and the Claude.app PA
capture result (commit `d80318d`), so the matrix row is
comparable along the same path axis in both directions.

---

## 2. Operator instructions (BEFORE pasting)

1. **Open a fresh Claude.app chat.** Do not reuse a chat that has
   prior context. Do not enable any custom instructions or
   personalities for this capture. Do not reuse the Plan A
   capture's chat.
2. **Confirm you are signed into the intended Anthropic account.**
   The capture record will say "Source: Claude consumer app
   (claude.ai), NOT API"; no account / subscription identifiers
   should be pasted into the eventual evidence file.
3. **Disable web tools / artifacts / file uploads** if your
   Claude.app session has them on. We want plain text out, not
   an artifact.
4. **Paste the entire fenced block from § 3 verbatim** as a
   single user message. Do not edit the prompt text. Do not split
   it across multiple messages.
5. **This is a non-interactive, single-shot capture.** Do **not**
   click or type a choice between turns. Claude must produce all
   three turns plus their choice blocks in a single response. The
   prompt simulates the fixed path internally: **Turn 2 continues
   after Turn 1 `CHOICE_A`; Turn 3 continues after Turn 2
   `CHOICE_B`** (i.e. **A → B**, matching the OpenAI PD bake-off
   path). The operator picks nothing live and waits for the full
   reply.
6. **Do not coach mid-run.** No "make it shorter," no "use simpler
   Armenian," no "rewrite that line." If Claude breaks the
   format, that is the data — note it in § 4 and stop. Do not
   re-prompt Claude in the same chat.
7. **If you must retry** because the response was empty / a
   network error / clearly truncated, **start a fresh chat** and
   paste the prompt again. Note the retry in the captured
   evidence file.

---

## 3. The prompt to paste

Paste the **entire** block below into a fresh Claude.app chat as
one user message. The opening Armenian sentence is part of the
prompt — do not delete it.

````
Դու գրում ես «Արեգ»-ի համար։ Արեգը հայախոս փոքրիկ խաղալիք-ընկեր է 4–7 տարեկան երեխաների համար։ Արեգը հեքիաթասաց է, ոչ թե զրուցակից, ոչ թե օգնական։ Նրա ձայնը՝ տաք, հանգիստ, փոքր-ինչ կախարդական։ Նա խոսում է ՄԻԱՅՆ արևելահայերենով։

Քո խնդիրն է գրել ՄԻ ԱՄԲՈՂՋ կարճ հեքիաթ՝ ՃԻՇՏ 3 քայլով, հետևյալ սցենարի շրջանակում.

— Հերոս՝ փոքրիկ ու զարմացող մողես։
— Ընկեր/առաջնորդ՝ վախեցած բադիկ, որին հերոսը մխիթարում է։
— Վայր՝ հին կամուրջ՝ ձմեռային մեղմ լույսի տակ։
— Կախարդական առարկա՝ քնած բանալի, որ դեռ չի արթնացել։
— Փոքրիկ խնդիր՝ ձյունը չի սկսում հալվել, և կամուրջի տակ առվակը չի կարողանում երգել։
— Տրամադրություն՝ ձմեռային մեղմ, թեթևակի բանաստեղծական, հին փայտի ու քնած ծաղիկների հոտով։
— Լուծման ոճ՝ լուծումը գալիս է համբերությունից (բայց ՉԻ ՊԵՏՔ բարոյախոսել համբերության մասին — ոչ մի «համբերությունը հաղթում է»-տիպի աֆորիզմ, ոչ մի դաս, ոչ մի վերջաբան-իմաստախոսություն)։

ԿԱՆՈՆՆԵՐ (պարտադիր).
1. Միայն արևելահայերեն։ Ոչ մի անգլերեն բառ, ոչ մի լատինատառ տառադարձում, ոչ մի անգլերեն մեկնաբանություն, ոչ մի մետա-տեքստ։
2. Ոչ մի հնարված կամ կեղծ-հայերեն բառ, ոչ մի չգոյություն ունեցող բայի ձև, ոչ մի հնարված մասնիկ։ Եթե բառի ճիշտ ձևը չգիտես, ընտրիր ավելի պարզ ու հաստատ բառ։
3. Տոնը՝ տաք հեքիաթասաց, ոչ թե AI-օգնական, ոչ թե դասատու, ոչ թե հոգեբան։ Ոչ մի «սիրելի փոքրիկ ընկեր», ոչ մի «դու շատ լավն ես»-տիպի զգացմունքային ընկերակցում, ոչ մի դաս-մատուցում։
4. Տարիքն է 4–7՝ բառերը պարզ, պատկերները զգայական (լույս, հոտ, ձայն, շոշափում), առանց վախի, առանց բռնության, առանց բուժական թեմայի։ Մի փոքր ավելի հարուստ մթնոլորտ թույլատրելի է (հեքիաթը age-7-richer ուղղվածությամբ է), բայց բառերը պարզ պահիր։
5. Յուրաքանչյուր քայլ՝ կարճ, 4-ից 7 նախադասություն։ Ոչ մի երկարաշունչ պարբերություն։
6. Ընտրությունները՝ ՄԻՇՏ երկու կոնկրետ ֆիզիկական գործողություն (ոչ՝ զգացմունքի մասին հարց, ոչ՝ «ինչպե՞ս ես»-տիպի հարց)։
7. Քայլ 1՝ բացման հեքիաթ + երկու ընտրություն։ Քայլ 1-ի առաջին նախադասությունը պետք է պարունակի «հին կամուրջ» հիմքը՝ ամբողջ տառերով (օրինակ՝ «Հին կամուրջի…», «Հին կամուրջը…», «Հին կամուրջի վրա…», «Հին կամուրջի մոտ…»)։ Մի՛ կրճատիր ձայնավորը՝ ՈՉ «կամրջի»։
8. Քայլ 2՝ ուղիղ կատարում է CHOICE_A-ն Քայլ 1-ից, ապա շարունակում է, ապա առաջարկում երկու նոր կոնկրետ ընտրություն։ Փոքրիկ խնդիրը (ձյունը չի հալվում) ՉԻ ԼՈՒԾՎՈՒՄ ամբողջությամբ Քայլ 2-ում — լուծումը կիրառվում է Քայլ 3-ում։
9. Քայլ 3՝ ուղիղ կատարում է CHOICE_B-ն Քայլ 2-ից, ապա մեղմ ավարտում է հեքիաթը։ Փոքրիկ խնդիրը լուծվում է համբերության ոճով (ձյունը սկսում է հալվել, առվակը՝ արթնանալ), ԲԱՅՑ առանց բացահայտ բարոյախոսության համբերության մասին։ Քայլ 3-ի վերջում նույնպես երկու ֆիզիկական ընտրություն տուր, որպեսզի ձևաչափը պահվի (եթե հեքիաթը արդեն ամբողջովին ավարտված է, ընտրությունները կարող են վերաբերել պարզ հանգիստ գործողության — օրինակ՝ քայլել տուն կամ նստել կամուրջի մոտ)։

ՊԱՏԱՍԽԱՆԻ ՁԵՎԱՉԱՓ — ՊԱՐՏԱԴԻՐ։
Պատասխանիր ՃԻՇՏ այս ձևաչափով, ամեն պիտակը՝ առանձին տողի վրա, առանց որևէ նախաբանի կամ վերջաբանի, առանց բացատրությունների, առանց markdown-ի, առանց հաստ տառերի, առանց զարդարանքի.

TURN_1_STORY:
<բացման հեքիաթը՝ 4-7 նախադասությամբ, առաջին նախադասությունում՝ «հին կամուրջ» ամբողջ ձևով>
CHOICE_A:
<կոնկրետ ֆիզիկական գործողություն>
CHOICE_B:
<կոնկրետ ֆիզիկական գործողություն>

TURN_2_STORY:
<շարունակություն CHOICE_A-ից հետո՝ 4-7 նախադասությամբ, փոքրիկ խնդիրը դեռ չի լուծվում>
CHOICE_A:
<նոր կոնկրետ ֆիզիկական գործողություն>
CHOICE_B:
<նոր կոնկրետ ֆիզիկական գործողություն>

TURN_3_STORY:
<մեղմ ավարտ CHOICE_B-ից հետո՝ 4-7 նախադասությամբ, փոքրիկ խնդիրը լուծվում է համբերության ոճով՝ առանց բարոյախոսության>
CHOICE_A:
<կոնկրետ ֆիզիկական գործողություն>
CHOICE_B:
<կոնկրետ ֆիզիկական գործողություն>

Մի՛ ավելացրու որևէ այլ բան՝ ոչ նախաբան, ոչ վերջաբան, ոչ էմոջի, ոչ բացատրություն, ոչ «Հուսով եմ սա օգտակար եղավ»-տիպի մեկնաբանություն։ Ուղղակի վերը նշված 9 պիտակների բովանդակությունը։
````

---

## 4. Operator checklist (AFTER capture)

Fill the following in the saved evidence markdown (see § 5 for
the file path). Do **not** edit the raw Claude output.

- [ ] **Raw output saved exactly.** The full response, copied
  verbatim — including any leading / trailing whitespace, line
  breaks, and any text Claude added outside the requested
  format. Do not normalize, do not trim, do not "fix"
  punctuation.
- [ ] **Format adherence noted.** Did Claude produce the 9
  labeled fields (`TURN_1_STORY` / `CHOICE_A` / `CHOICE_B` ×3)
  in order, each on its own labeled line, with no extra prose
  before or after? Record yes / partial / no with one line
  describing the deviation if any.
- [ ] **Fake Armenian / invented morphology check.** Skim each
  turn for non-standard or coined tokens. If any are flagged,
  list them verbatim with the suspected intended form.
  Pay particular attention on PD to the `հին կամուրջ`
  schwa-not-drop rule: T1 sentence 1 should retain the stem
  (`Հին կամուրջի…`, `Հին կամուրջը…`, etc.); a dropped form
  like `Կամրջի վրա` in T1 sentence 1 is a hard fail. (This
  cell is non-native-best-effort; native review pass comes
  later.)
- [ ] **Patience-aphorism check.** Does T3 explicitly moralize
  about patience ("համբերությունը հաղթում է", "ով համբերում է,
  հաղթում է", etc.)? Yes / no with the offending line quoted
  if yes.
- [ ] **Choice quality check.** Are all six choices concrete
  physical actions, or do any of them slip into opinion /
  feeling / metacognition? List any that do.
- [ ] **English / meta leakage check.** Any English words, Latin
  letters, transliteration, parenthetical narrator brackets, or
  meta commentary ("Hope this helps", "Here is the story", etc.)?
  Yes / no, with the offending text quoted if yes.
- [ ] **"Would I let Areg say this aloud?"** Yes / no with one
  load-bearing reason. Provisional verdict only — final answer
  comes from the native Armenian review pass per the comparison
  plan § 6.
- [ ] **No edits applied.** Confirm the saved raw output is the
  literal Claude.app reply, unedited. The evaluator markdown
  may quote and annotate it; the saved raw block must remain
  untouched.

---

## 5. Where to save the raw output

When the capture completes, save the raw Claude.app reply plus
the operator checklist as a new file at:

```
tools/StoryModelBakeoff/evaluations/claude-app-plan-d-controlled-capture-result-20260510.md
```

(Adjust the date in the filename if the capture happens on a
later Yerevan day.)

That evidence file is a separate slice — **not part of this
capture-prep doc**. It will be authored after the capture by
following the comparison-plan matrix-row update protocol
(§ 3 of `controlled-claude-openai-comparison-plan-20260510.md`).
Do **not** stage or commit the captured-output file in the same
commit as this capture-prep file.

---

## 6. Scope guard

Authoring this capture-prep document touched no production /
runtime files: `ChatService`, backend code, frontend,
`appsettings*.json`, `*.csproj`, tests, seed bank, name bank,
story-plan generator, validator, runtime system prompts
(production sha
`54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
unchanged), speech / TTS / STT — all unchanged. No paid API call
was made; no backend was started; no provider configuration was
touched; Claude API was not used. The only artifact is this
markdown under `tools/StoryModelBakeoff/evaluations/`.

The capture itself, when run, is a free human-driven action in
the Claude.app browser session — no API spend, no repo code
runs, no production touch.

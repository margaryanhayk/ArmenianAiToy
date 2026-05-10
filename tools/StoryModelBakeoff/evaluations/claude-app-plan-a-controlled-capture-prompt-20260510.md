# Claude.app — Plan A controlled capture prompt — 2026-05-10

**Status:** capture-preparation only. No code change, no paid API
call, no backend run, no production change, no provider switch
authorized by this document. The artifact is the prompt that the
human operator pastes into a fresh Claude.app chat to obtain a
controlled comparison sample for Areg story-brain evaluation.

**Companion to:** the controlled comparison plan
`controlled-claude-openai-comparison-plan-20260510.md`
(commit `bbe50fa`), § 9 step 2.

**Filename date** uses local Yerevan `2026-05-10`.

---

## 1. What this is

The comparison plan calls for a controlled Claude.app PA capture
that uses the **same Plan A scenario family** as the OpenAI
`v3.2.3 mp1 PA` run (commit `e73975b`). This file holds:

- the exact prompt to paste into Claude.app (§ 3 below),
- operator instructions for *before* the paste (§ 2),
- the operator checklist for *after* the capture (§ 4),
- the fixed scenario brief, fixed choice path (A → B), and the
  capture-friendly output format the comparison plan expects.

**This file is not the captured result.** When the operator runs
this and obtains output, that output is saved as a separate
evidence markdown — see § 5.

**Fixed choice path: A → B** (matches the OpenAI v3.2.3 PA run).
The OpenAI run followed T1 = Ա, T2 = Բ; this Claude capture is
deliberately aligned to the same path so the side-by-side
comparison is fair. Concretely:

- **Turn 2 must continue after Turn 1 `CHOICE_A`.**
- **Turn 3 must continue after Turn 2 `CHOICE_B`.**

The *scenario brief* is also held the same: forest / small
animal helper / magical object / gentle mystery, with the same
hero / place / magical object as the bake-off PA scenario.

---

## 2. Operator instructions (BEFORE pasting)

1. **Open a fresh Claude.app chat.** Do not reuse a chat that has
   prior context. Do not enable any custom instructions or
   personalities for this capture.
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
   `CHOICE_B`** (i.e. **A → B**, matching the OpenAI v3.2.3 PA
   run). The operator picks nothing live and waits for the full
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

— Հերոս՝ փոքրիկ ու հնարամիտ շնիկ։
— Ընկեր/առաջնորդ՝ ծեր ու բարի շուն, որը պատմում է հին պատմություններ։
— Վայր՝ խնձորենու այգի, ուր ծառերը հին բարեկամներ են։
— Կախարդական առարկա՝ ցողի կաթիլներով ոսկեգույն տերև, որ արևի տակ շողշողում է։
— Փոքրիկ խնդիր՝ մի արագիլ չի գտնում հանգստանալու տեղ։
— Տրամադրություն՝ տաք, հիշատակային, գիշերային-հեքիաթային։

ԿԱՆՈՆՆԵՐ (պարտադիր).
1. Միայն արևելահայերեն։ Ոչ մի անգլերեն բառ, ոչ մի լատինատառ տառադարձում, ոչ մի անգլերեն մեկնաբանություն, ոչ մի մետա-տեքստ։
2. Ոչ մի հնարված կամ կեղծ-հայերեն բառ, ոչ մի չգոյություն ունեցող բայի ձև, ոչ մի հնարված մասնիկ։ Եթե բառի ճիշտ ձևը չգիտես, ընտրիր ավելի պարզ ու հաստատ բառ։
3. Տոնը՝ տաք հեքիաթասաց, ոչ թե AI-օգնական, ոչ թե դասատու, ոչ թե հոգեբան։ Ոչ մի «սիրելի փոքրիկ ընկեր», ոչ մի «դու շատ լավն ես»-տիպի զգացմունքային ընկերակցում։
4. Տարիքն է 4–7՝ բառերը պարզ, պատկերները զգայական (լույս, հոտ, ձայն, շոշափում), առանց վախի, առանց բռնության, առանց բուժական թեմայի։
5. Յուրաքանչյուր քայլ՝ կարճ, 3-ից 6 նախադասություն։ Ոչ մի երկարաշունչ պարբերություն։
6. Ընտրությունները՝ ՄԻՇՏ երկու կոնկրետ ֆիզիկական գործողություն (ոչ՝ զգացմունքի մասին հարց, ոչ՝ «ինչպե՞ս ես»-տիպի հարց)։
7. Քայլ 1՝ բացման հեքիաթ + երկու ընտրություն։
8. Քայլ 2՝ ուղիղ կատարում է CHOICE_A-ն Քայլ 1-ից, ապա շարունակում է, ապա առաջարկում երկու նոր կոնկրետ ընտրություն։
9. Քայլ 3՝ ուղիղ կատարում է CHOICE_B-ն Քայլ 2-ից, ապա մեղմ ավարտում է հեքիաթը։ Քայլ 3-ի վերջում նույնպես երկու ֆիզիկական ընտրություն տուր, որպեսզի ձևաչափը պահվի (եթե հեքիաթը արդեն ամբողջովին ավարտված է, ընտրությունները կարող են վերաբերել պարզ հանգիստ գործողության — օրինակ՝ քայլել տուն կամ նստել ծառի տակ)։

ՊԱՏԱՍԽԱՆԻ ՁԵՎԱՉԱՓ — ՊԱՐՏԱԴԻՐ։
Պատասխանիր ՃԻՇՏ այս ձևաչափով, ամեն պիտակը՝ առանձին տողի վրա, առանց որևէ նախաբանի կամ վերջաբանի, առանց բացատրությունների, առանց markdown-ի, առանց հաստ տառերի, առանց զարդարանքի.

TURN_1_STORY:
<բացման հեքիաթը՝ 3-6 նախադասությամբ>
CHOICE_A:
<կոնկրետ ֆիզիկական գործողություն>
CHOICE_B:
<կոնկրետ ֆիզիկական գործողություն>

TURN_2_STORY:
<շարունակություն CHOICE_A-ից հետո՝ 3-6 նախադասությամբ>
CHOICE_A:
<նոր կոնկրետ ֆիզիկական գործողություն>
CHOICE_B:
<նոր կոնկրետ ֆիզիկական գործողություն>

TURN_3_STORY:
<մեղմ ավարտ CHOICE_B-ից հետո՝ 3-6 նախադասությամբ>
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
  list them verbatim with the suspected intended form. (This
  cell is non-native-best-effort; a native review pass comes
  later.)
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
tools/StoryModelBakeoff/evaluations/claude-app-controlled-pa-20260510.md
```

(Adjust the date in the filename if the capture happens on a
later Yerevan day.)

That evidence file is a separate slice — **not part of this
capture-prep doc**. It will be authored after the capture by
following the comparison plan's matrix-row update protocol
(§ 3 of `controlled-claude-openai-comparison-plan-20260510.md`).
Do **not** stage or commit the captured-output file in the same
commit as this capture-prep file.

---

## 6. Scope guard

Authoring this capture-prep document touched no production /
runtime files: `ChatService`, backend code, frontend,
`appsettings*.json`, `*.csproj`, tests, seed bank, name bank,
story-plan generator, validator, runtime system prompts (production
sha `54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
unchanged), speech / TTS / STT — all unchanged. No paid API call
was made; no backend was started; no provider configuration was
touched. The only artifact is this markdown under
`tools/StoryModelBakeoff/evaluations/`.

The capture itself, when run, is a free human-driven action in
the Claude.app browser session — no API spend, no repo code
runs, no production touch.

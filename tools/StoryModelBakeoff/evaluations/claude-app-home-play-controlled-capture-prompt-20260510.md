# Claude.app — Home/Play controlled capture prompt — 2026-05-10

**Status:** capture-preparation only. No code change, no paid API
call, no backend run, no Claude API use, no production change,
no provider switch authorized by this document. The artifact is
the prompt that the human operator pastes into a fresh Claude.app
chat to obtain a controlled comparison sample for Areg story-
brain evaluation on a *child-natural everyday* scenario —
explicitly NOT a forest / enchanted-bridge / magical-object
scenario.

**Companion to:**
- Plan A capture prompt + result (commits `2400243`, `d80318d`)
- Plan D capture prompt + result (commits `9671843`, `471bbf6`)
- Story-brain findings summary (commit `3835f25`):
  `tools/StoryModelBakeoff/evaluations/story-brain-findings-summary-20260510.md`
- Controlled comparison plan (commit `bbe50fa`) § 4 PE scenario
  shape

**Filename date** uses local Yerevan `2026-05-10` for batch
consistency with the rest of this evidence set.

---

## 1. What this is

The findings summary calls out that forest / magical-object
scenarios may hide practical-conversational-Armenian
weaknesses — Claude's atmospheric-prose strength might not
transfer to ordinary kitchen-table Armenian. This capture is the
test for that hypothesis on the Claude.app side.

This file holds:

- the exact prompt to paste into Claude.app (§ 3 below),
- operator instructions for *before* the paste (§ 2),
- the operator checklist for *after* the capture (§ 4),
- the fixed scenario brief, fixed choice path **A → B**, and
  the capture-friendly output format the comparison plan
  expects.

**This file is not the captured result.** When the operator runs
this and obtains output, that output is saved as a separate
evidence markdown — see § 5.

### 1.1 Source scenario (newly authored)

This is a fresh scenario, not lifted from the bake-off
scenarios file. Designed to be deliberately *un*-magical and
deliberately *familiar*:

- **Hero:** Նարե, a roughly-5-year-old Armenian girl. Single
  human protagonist; no animal hero.
- **Setting:** her own bedroom in the evening, under the soft
  light of a lamp. Familiar home objects: bed, blanket, pillow,
  shelf, rug, books, soft toys.
- **Focal object:** her favorite doll, Մոմո. The doll is sleepy
  and ready for bed.
- **Problem:** Մոմոյի փոքրիկ բարձը կորել է — Մոմո's little
  pillow has gone missing. Without the pillow Մոմո can't sleep.
- **Mood:** warm, playful, gentle, slightly bedtime-adjacent.
- **Magical level:** *very* light. The doll being slightly alive
  is accepted child-play register; no real magic, no flying, no
  talking-in-human-words, no quest. This is an everyday family
  story, not a fairy-tale.

The scenario deliberately avoids: forests, old bridges, golden
leaves, sleeping keys, magical objects, animal heroes (so the
animal-anatomy failure mode from Plan D cannot recur), classical
opener patterns (`Մի անգամ` is forbidden in the prompt rules).

### 1.2 Fixed choice path: A → B

Matches the path used by both prior controlled captures (PA
commit `d80318d`, PD commit `471bbf6`) and by the bake-off PA /
PD OpenAI scenarios:

- **Turn 2 must continue after Turn 1 `CHOICE_A`.**
- **Turn 3 must continue after Turn 2 `CHOICE_B`.**

Keeping the path consistent across captures lets the side-by-
side cross-scenario comparison stay clean.

---

## 2. Operator instructions (BEFORE pasting)

1. **Open a fresh Claude.app chat.** Do not reuse a chat that has
   prior context. Do not enable any custom instructions or
   personalities for this capture. Do not reuse the PA or PD
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
   `CHOICE_B`** (i.e. **A → B**). The operator picks nothing
   live and waits for the full reply.
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
Դու գրում ես «Արեգ»-ի համար։ Արեգը հայախոս փոքրիկ խաղալիք-ընկեր է 4–7 տարեկան երեխաների համար։ Արեգը պատմող է, ոչ թե զրուցակից, ոչ թե օգնական։ Նրա ձայնը՝ տաք, հանգիստ, փոքր-ինչ խաղային։ Նա խոսում է ՄԻԱՅՆ արևելահայերենով։

Քո խնդիրն է գրել ՄԻ ՓՈՔՐԻԿ, ՏԱՆԵԿԱՆ, ԱՌՕՐՅԱ պատմվածք՝ ՃԻՇՏ 3 քայլով, հետևյալ սցենարի շրջանակում.

— Հերոս՝ Նարե անունով փոքրիկ աղջիկ, մոտ 5 տարեկան։
— Վայր՝ Նարեի սենյակը՝ երեկոյան, լամպի մեղմ լույսի տակ։ Տանեկան իրեր՝ մահճակալ, ծածկոց, բարձ, դարակ, գորգ, գրքեր, փափուկ խաղալիքներ։
— Կարևոր առարկա՝ Նարեի սիրելի տիկնիկը՝ Մոմո։ Մոմոն հոգնած ու քնկոտ է։
— Փոքրիկ խնդիր՝ Մոմոյի փոքրիկ բարձը կորել է։ Առանց բարձի՝ Մոմոն չի կարող քնել։
— Տրամադրություն՝ տաք, խաղային, մեղմ, քնելու ժամին մոտ։
— Կախարդական մակարդակ՝ ՇԱՏ ՔԻՉ։ Տիկնիկը «քնկոտ» է որպես երեխա-խաղի ընդունված ոճ — սա առօրյա ընտանեկան պատմվածք է, ՈՉ ԹԵ հեքիաթ-արկածախնդրություն։ Ոչ մի իրական մոգություն, ոչ թռչող իրեր, ոչ կախարդական առարկաներ, ոչ խոսող կենդանի-մարդկային խոսքով։

ԿԱՆՈՆՆԵՐ (պարտադիր).

1. Միայն արևելահայերեն։ Ոչ մի անգլերեն բառ, ոչ մի լատինատառ տառադարձում, ոչ մի անգլերեն մեկնաբանություն, ոչ մի մետա-տեքստ։
2. Ոչ մի հնարված կամ կեղծ-հայերեն բառ, ոչ մի չգոյություն ունեցող բայի ձև, ոչ մի հնարված մասնիկ։ Եթե բառի ճիշտ ձևը չգիտես, ընտրիր ավելի պարզ ու հաստատ բառ։
3. Տոնը՝ տաք պատմող, ոչ թե AI-օգնական, ոչ թե դասատու, ոչ թե հոգեբան։ ՈՉ ՄԻ բարոյախոսություն, ՈՉ ՄԻ դաս-մատուցում, ՈՉ ՄԻ «սովորեցինք, որ…»-տիպի վերջաբան-իմաստ։
4. Բառերը պարզ, տնային, ամենօրյա հայերեն։ ՈՉ ԲԱՆԱՍՏԵՂԾԱԿԱՆ խտացված կերպար, ՈՉ ՄԵՏԱՖՈՐԱԿԱՆ խորություն, ՈՉ «ինչպես երազ» -տիպի շերտեր։ Սա առօրյա պատմվածք է, ոչ թե հեքիաթ։ Պատկերները՝ տեսանելի, տանեկան (բարձ, ծածկոց, դարակ, լամպ, պատուհան, գորգ, գիրք, փափուկ խաղալիք)։
5. ՍԵՄԱՆՏԻԿ ՍՏՈՒԳՈՒՄ (կարևոր)։
   — Հերոսի և առարկաների անատոմիան և գործողությունները պետք է համապատասխանեն կերպարին։ Աղջիկը մատներով է բռնում, տեսնում աչքերով, քայլում ոտքերով։ Տիկնիկը կարող է ունենալ ձեռքեր, աչքեր, քնի զգացում (խաղային ընդունված)։
   — ՈՉ ՈՏՔ-ՉՈՒՆԵՑՈՂ առարկաները չեն քայլում։ ՈՉ ՁԵՌՔ-ՉՈՒՆԵՑՈՂ առարկաները չեն բռնում։ ՈՉ ԿԵՆԴԱՆԻ առարկաները չեն խոսում մարդկային խոսքով։
   — Եթե առարկան անում է անհնարին գործողություն, դա պետք է լինի հստակ նկարագրված որպես երեխայի խաղային երևակայություն («Նարեն երևակայեց, որ…», «ասես…», «կարծես…», «Նարեն խաղալով պատմեց…»):
6. Տարիքն է 4–7՝ առանց վախի, առանց բռնության, առանց բուժական թեմայի։
7. Յուրաքանչյուր քայլ՝ կարճ, 3-ից 5 նախադասություն։ Ոչ մի երկարաշունչ պարբերություն։
8. Ընտրությունները՝ ՄԻՇՏ երկու կոնկրետ ֆիզիկական գործողություն, որ Նարեն կարող է իրականում անել իր սենյակում (ոչ՝ զգացմունքի մասին հարց, ոչ՝ «ինչպե՞ս ես»-տիպի հարց, ոչ՝ «ի՞նչ ես մտածում»)։
9. Քայլ 1՝ բացման պատմվածք + երկու ընտրություն։ ՉԻ ԿԱՐԵԼԻ սկսել «Մի անգամ…»-ով, «Մի օր…»-ով, «Կար ու չկար…»-ով, կամ որևէ դասական հեքիաթային բացմամբ։ Քայլ 1-ի առաջին նախադասությունը պետք է խարսխվի սենյակում կամ տանեկան մի կոնկրետ պատկերում (օրինակ՝ «Նարեի սենյակում…», «Լամպի մեղմ լույսի տակ…», «Փափուկ գորգին նստած…»):
10. Քայլ 2՝ ուղիղ կատարում է CHOICE_A-ն Քայլ 1-ից, ապա շարունակում է, ապա առաջարկում երկու նոր կոնկրետ ընտրություն։ Փոքրիկ խնդիրը (բարձը կորել է) ՉԻ ԼՈՒԾՎՈՒՄ ամբողջությամբ Քայլ 2-ում — լուծումը կիրառվում է Քայլ 3-ում։
11. Քայլ 3՝ ուղիղ կատարում է CHOICE_B-ն Քայլ 2-ից, ապա մեղմ ավարտում է պատմվածքը։ Բարձը գտնվում է, կամ Նարեն գտնում է մի մեղմ, գործնական լուծում (օրինակ՝ ծալում է փոքրիկ ծածկոցը որպես ժամանակավոր բարձ)։ ԲԱՅՑ առանց բացահայտ բարոյախոսության, առանց «տեսնում ե՞ս, Մոմո, երբ սիրով օգնում ես…»-տիպի վերջաբան-իմաստ։ Քայլ 3-ի վերջում նույնպես երկու ֆիզիկական ընտրություն տուր, որպեսզի ձևաչափը պահվի (օրինակ՝ մարել լամպը, կամ Մոմոյին պատմություն կարդալ)։

ՊԱՏԱՍԽԱՆԻ ՁԵՎԱՉԱՓ — ՊԱՐՏԱԴԻՐ։
Պատասխանիր ՃԻՇՏ այս ձևաչափով, ամեն պիտակը՝ առանձին տողի վրա, առանց որևէ նախաբանի կամ վերջաբանի, առանց բացատրությունների, առանց markdown-ի, առանց հաստ տառերի, առանց զարդարանքի.

TURN_1_STORY:
<բացման պատմվածքը՝ 3-5 նախադասությամբ, ՉԻ ՍԿՍՎՈՒՄ «Մի անգամ…»-ով, ՊԵՏՔ Է խարսխվի սենյակում>
CHOICE_A:
<կոնկրետ ֆիզիկական գործողություն, որ Նարեն կարող է անել իր սենյակում>
CHOICE_B:
<կոնկրետ ֆիզիկական գործողություն, որ Նարեն կարող է անել իր սենյակում>

TURN_2_STORY:
<շարունակություն CHOICE_A-ից հետո՝ 3-5 նախադասությամբ, փոքրիկ խնդիրը դեռ չի լուծվում>
CHOICE_A:
<նոր կոնկրետ ֆիզիկական գործողություն>
CHOICE_B:
<նոր կոնկրետ ֆիզիկական գործողություն>

TURN_3_STORY:
<մեղմ ավարտ CHOICE_B-ից հետո՝ 3-5 նախադասությամբ, բարձը գտնվում է կամ Նարեն գործնական լուծում է գտնում, ԱՌԱՆՑ ԲԱՐՈՅԱԽՈՍՈՒԹՅԱՆ>
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
  list them verbatim with the suspected intended form. Pay
  attention to verb conjugations (`-եց / -ավ / -վեց`),
  participles, and any noun that "looks Armenian but might
  not exist." (Non-native best-effort; native review pass
  comes later.)
- [ ] **Semantic / body-part mismatch check (new for this
  capture).** Did Claude give a non-living object the actions
  of a living one (a doll walking on its own, a pillow flying,
  a blanket talking in human words) outside an explicit
  "Նարեն երևակայեց, որ…" / "ասես…" / "կարծես…" pretend-play
  framing? Did any anatomy reference go off (e.g. doll's
  `ոտքերը`, pillow's `ձեռքերը`)? List any that did.
- [ ] **Home/play naturalness check (new for this capture).**
  Does the Armenian sound like *spoken home Armenian* a
  5-year-old's family would actually use, or does it drift
  into literary / fairy-tale register (`քարերը փայփայված էին
  ձյունով`-style poetic-density lines, `որովհետև … ինքն էր
  ասում ամեն ինչ`-style abstractions)? Record yes (natural) /
  drift-some / drift-significant with one quoted example.
- [ ] **Opener check.** Did Claude start with `Մի անգամ…`,
  `Մի օր…`, `Կար ու չկար…`, or any other classical fairy-tale
  opener that the prompt forbids? Yes / no with the offending
  T1 first-sentence quoted if yes.
- [ ] **Choice quality check.** Are all six choices concrete
  physical actions a 5-year-old could actually do in her
  bedroom, or do any slip into opinion / feeling /
  metacognition? List any that do.
- [ ] **English / meta leakage check.** Any English words, Latin
  letters, transliteration, parenthetical narrator brackets, or
  meta commentary ("Hope this helps", "Here is the story", etc.)?
  Yes / no, with the offending text quoted if yes.
- [ ] **Moralizing check.** Did T3 explicitly state a lesson
  (`«Տեսնում ե՞ս, Մոմո, երբ սիրով օգնում ես…»` /
  `«սովորեցինք, որ…»` / etc.)? Yes / no with the offending
  line quoted if yes.
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
tools/StoryModelBakeoff/evaluations/claude-app-home-play-controlled-capture-result-20260510.md
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

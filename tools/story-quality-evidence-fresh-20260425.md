# Story Voice MVP — Phase 1B fresh-conversation evidence (2026-04-25)

Re-capture of the same 10 prompts as Phase 1, but now each prompt
runs on its own freshly-registered device with its own fresh
conversation. Replaces the contaminated single-session capture in
`story-quality-evidence-20260425.md` for the purposes of tomorrow's
evaluator pass. **No grading. No code changes. No commits.**

## Capture conditions

- **Backend**: same long-running `dotnet run …Api` instance from
  Phase 1 (already up before the session started; not restarted).
- **Working tree state at capture time**: identical to Phase 1's —
  the `AudioChatController.cs`, `ModeDetector.cs`,
  `AudioStoryResponseComposer.cs`, and matching test files are still
  modified / untracked. **Not reverted** — the evidence reflects
  what the current branch produces today.
- **Endpoint**: `POST /api/chat` only. Audio endpoint deliberately
  skipped this round.
- **Devices**: 10 fresh devices registered via
  `POST /api/devices/register`, MACs `AA:BB:CC:DD:FF:01` …
  `AA:BB:CC:DD:FF:0A`, firmware label `phase1b-fresh-case-NN`.
- **Conversation isolation**: one device → one chat call → one
  conversation. No `StorySessionId` was passed on the wire, so the
  first call on each device starts a fresh story session.
- **No child profile** attached on any device.
- **Driver script**:
  `C:\Users\hayk.margaryan\AppData\Local\Temp\story-evidence\run_fresh.py`.
  Per-case JSON in `C:\tmp\story-evidence\fresh\case-NN.json`,
  aggregate at `fresh\all-cases.json`.

## Freshness contract — verified

The driver re-counts uniqueness at the end of the run. From this
session:

```
Unique devices:           10/10
Unique conversationIds:   10/10
Unique storySessionIds:   10/10
```

The 10 conversations and 10 story sessions are pairwise different
ids. There is no shared session bleeding across cases the way
Phase 1 had.

## Heuristic glossary

Three programmatic flags are computed per case so the evaluator
has a quick filter. Heuristics, not judgments:

- **`non_armenian_chars`** — distinct characters in the canonical
  text outside the Armenian Unicode block (U+0530–U+058F) and a
  small allowlist (whitespace, ASCII punctuation that the model
  sometimes emits, digits, guillemets, dash, ellipsis). ASCII
  letter runs are reported separately so a stray English word
  surfaces distinctly from a stray Cyrillic / Devanagari char.
- **`closed_or_moral_ending`** — substring scan over a short list
  of Armenian wrap-up / moral markers
  (`երջանիկ ապրեց`, `հեքիաթի վերջ`, `հեքիաթի ավարտ`, `սովորեց որ`,
  `հասկացավ որ`, `դասը`, `բարոյականությունը`, etc.) plus the last
  sentence of the text for human eyeballing.
- **`choices_related_to_text`** — for each choice, tokenize on
  Armenian-letter runs of length ≥ 4; for each token, mark it
  matched if its 4-character prefix appears anywhere in the story
  body. Reports `matched / tokens` and the matched token list.
  Crude but Armenian-declension-tolerant.

## Per-case data

### Case 01

- **Prompt**: `պատմիր հեքիաթ`
- **Device**: `bc490017-afea-430c-bfce-c978b9e8cc3f`
- **HTTP**: 200 in 8.63s
- **Mode**: `story`  • **safetyFlag**: 0 (Clean)
- **conversationId**: `d9a79d48-b8c9-4c3f-8ae9-fc30503961c4`
- **storySessionId**: `d9a79d48-b8c9-4c3f-8ae9-fc30503961c4`
- **Canonical text**:
  > Հայկական սարերի խորքում մի փոքրիկ ջրային աստվածուհի էր ապրում։ Նա ուներ մի ամպից պատրաստված փափուկ բարձ և սառած հեղեղի պես քնքուշ շորեր։ Մի անգամ, նա որոշեց այցելել երկիր, որպեսզի տեսնի ինչպես են մարդիկ ապրում և ինչպիսի երջանիկ պահեր են անցկացնում։ Ճամփին նա հանդիպեց մի բարի ագռավիկի, որն անհանգիստ թռչում էր իր շուրջը։ Ջրային աստվածուհին ուրախացավ և որոշեց խոսել ագռավիկի հետ։
- **ChoiceA**: `Վերցնենք փայլուն քարը`
- **ChoiceB**: `Թողնենք քարը և շարունակենք խաղալ`
- **non_armenian_chars**: 0
- **closed_or_moral_ending**: no marker
- **choices_related_to_text**: A 0/3 (0.0) — B 0/4 (0.0)

### Case 02

- **Prompt**: `Ես ուզում եմ հեքիաթ լսել`
- **Device**: `4cb7e422-3e1a-4d62-b63f-d9a955580d65`
- **HTTP**: 200 in 8.70s
- **Mode**: `story`  • **safetyFlag**: 0
- **conversationId**: `32bdc9c2-6070-430d-9862-11d47929ddf3`
- **storySessionId**: `32bdc9c2-6070-430d-9862-11d47929ddf3`
- **Canonical text**:
  > Մի օր փոքրիկ ծիածանն իր փոքրիկ ընկեր Չիկոյի հետ զվարճանում էր ծաղկոցում։ Ծաղիկների սակավաձորներից կաթում էին փափուկ գույներ՝ շողշողուն և պայծառ։ Փոքրիկ Չիկոն զարմացավ, որ հենց այդ պահին ծիածանը խմում էր ծաղիկների գույները։
- **ChoiceA**: `Փորձենք տուփը բացել`
- **ChoiceB**: `Կոչ գանք մայրիկին տուփի մասին`
- **non_armenian_chars**: 0
- **closed_or_moral_ending**: no marker
- **choices_related_to_text**: A 0/3 (0.0) — B 0/4 (0.0)

### Case 03

- **Prompt**: `պատմիր հեքիաթ արջուկի մասին`
- **Device**: `bf688503-9098-4bbf-a9fe-44922f5edc5b`
- **HTTP**: 200 in 9.53s
- **Mode**: `story`  • **safetyFlag**: 0
- **conversationId**: `b872a669-3d6c-4900-a05f-f520946c8a32`
- **storySessionId**: `b872a669-3d6c-4900-a05f-f520946c8a32`
- **Canonical text**:
  > Արջուկը քայլում էր խոտերի միջով` խնձոր գտնելու։ Նա այնքան փոքրիկ էր և ունեին երկար շագանակագույն մազեր։ Հանկարծ նրան հանդիպեց մի փոքրիկ նապաստակ, որը ջերմ ժպիտով ասաց. «Ողջույն, արջուկ, ուզում ես միասին սունկ հավաքենք։» Արջուկը ուրախացավ։ Հետաքրքիր էր իմանալ, թե որտեղ են թաքնված համեղ սունկերը։
- **ChoiceA**: `Բացենք մոշի տուփը`
- **ChoiceB**: `Փնտրենք ծիծեռնակին`
- **non_armenian_chars**: 1 — backtick `` ` `` (U+0060) in place of an
  Armenian comma after «խոտերի միջով».
- **closed_or_moral_ending**: no marker
- **choices_related_to_text**: A 0/3 (0.0) — B 0/2 (0.0)

### Case 04

- **Prompt**: `պատմիր հեքիաթ նապաստակի մասին`
- **Device**: `44ee220e-7520-4935-8b5d-8865a23febbf`
- **HTTP**: 200 in 5.65s
- **Mode**: `story`  • **safetyFlag**: 0
- **conversationId**: `ac8242ff-88c3-4e78-a704-b4d51a91e0e0`
- **storySessionId**: `ac8242ff-88c3-4e78-a704-b4d51a91e0e0`
- **Canonical text**:
  > Փոքրիկ նապաստակը ցատկեց քարի վրայից։ Նա շատ ուրախ էր և ուզում էր իմանալ, թե ինչ հետաքրքիր բաներ կգտնի անտառում։ Երբ նապաստակը գնաց առաջ, հանկարծ գտավ մի փոքրիկ տուփ, որը թաքնված էր խոտերի մեջ։ Վրան գեղեցիկ նկարներ կային, և նապաստակը զարմացած մտածեց՝ ինչ կարող է լինել ներսում։
- **ChoiceA**: `Խաղանք գետի կողքին`
- **ChoiceB**: `Հետևենք գետի հոսքին`
- **non_armenian_chars**: 0
- **closed_or_moral_ending**: no marker
- **choices_related_to_text**: A 0/3 (0.0) — B 0/3 (0.0)

### Case 05

- **Prompt**: `պատմիր հեքիաթ անտառի մասին`
- **Device**: `d68e44ed-0824-4460-ac17-0e2cde0c8076`
- **HTTP**: 200 in 22.58s
- **Mode**: `story`  • **safetyFlag**: 0
- **conversationId**: `fd5f24ea-7599-487f-b921-84cc042a981e`
- **storySessionId**: `fd5f24ea-7599-487f-b921-84cc042a981e`
- **Canonical text**:
  > Փոքրիկ խխունջը մի օր որոշեց գնալ փնտրելու անտառի խորքերը։ Նա ոտք դրեց խավար ու հանդարտ ճանապարհի վրա։ Խոսկանիները, որոնք թաքնված էին ծառերի մեջ, երգեցին մի մեղեդի։ Խխունջը զարմացավ այդ գեղեցիկ մեղեդուց և ուզում էր մոտենալ։ Բայց հետաքրքրվեց, թե ինչ կլինի, եթե փնտրի երգող խոսկանիներին։
- **ChoiceA**: `Հարցնենք մորից քարի մասին`
- **ChoiceB**: `Խաղանք քարը կրծելով`
- **non_armenian_chars**: 0 (Phase 1's «пп федера» glitch did not
  recur on this fresh conversation)
- **closed_or_moral_ending**: no marker
- **choices_related_to_text**: A 0/4 (0.0) — B 0/3 (0.0)

### Case 06

- **Prompt**: `պատմիր կախարդական հեքիաթ`
- **Device**: `eeb4ae73-10f5-442b-8448-55d18401afce`
- **HTTP**: 200 in 10.27s
- **Mode**: `story`  • **safetyFlag**: 0
- **conversationId**: `b79c085d-dfef-41ef-9223-47b0ed165629`
- **storySessionId**: `b79c085d-dfef-41ef-9223-47b0ed165629`
- **Canonical text**:
  > Փոքրիկ Կարապետը քայլում էր անտառի արահետով։ Նա հանկարծ տեսավ մի փայլուն քար, որը կարծես ուրախությունից փայլում էր։ Կարապետը զարմացավ ու մոտեցավ քարին, որ ցանկանա նրանից գտնել ինչ-որ գաղտնիք։ Նա զգաց, որ քարը մի տեսակ տաք է, և դա նրան զվարճացրեց։ Ի՞նչ անել հիմա՝ կարո՞ղ է լինել մի կախարդական բան։
- **ChoiceA**: `Վերցնենք փայլուն քարը`
- **ChoiceB**: `Շարունակենք մեր քայլարշավը`
- **non_armenian_chars**: 0
- **closed_or_moral_ending**: no marker
- **choices_related_to_text**: A 2/3 (0.67) — B 1/2 (0.5) — best
  alignment in the batch.

### Case 07

- **Prompt**: `պատմիր հեքիաթ մի փոքրիկ աղջկա մասին`
- **Device**: `9e862742-8eb9-402f-a829-b3817bb42541`
- **HTTP**: 200 in 21.73s
- **Mode**: `story`  • **safetyFlag**: 0
- **conversationId**: `7c7cc486-22c1-4a46-87e1-ceb88515bb09`
- **storySessionId**: `7c7cc486-22c1-4a46-87e1-ceb88515bb09`
- **Canonical text**:
  > Փոքրիկ Մարիան նստած էր բացիկների վրա զարդարված տանը։ Նրա առաջ կանգնած էր մի փայլուն փոքրիկ տուփ։ Մարիան զարմացած նայում էր տուփին և մտածում, թե ինչ կա դրա մեջ։ Նրա սիրտը ուրախությունից սկսեց արագ բաբախել։ Միգուցե նրա ներսում կար ինչ-որ փոքրիկ հրաշք։
- **ChoiceA**: `Քաղենք կակաչը`
- **ChoiceB**: `Նկարչենք կակաչը`
- **non_armenian_chars**: 0
- **closed_or_moral_ending**: no marker
- **choices_related_to_text**: A 0/2 (0.0) — B 0/2 (0.0)

### Case 08

- **Prompt**: `պատմիր հեքիաթ ֆերմայի մասին`
- **Device**: `31736c42-6d9b-4a41-b1e0-6b7668ee2efb`
- **HTTP**: 200 in 21.73s
- **Mode**: `story`  • **safetyFlag**: 0
- **conversationId**: `2cc87e0c-9723-4e20-b484-172120287758`
- **storySessionId**: `2cc87e0c-9723-4e20-b484-172120287758`
- **Canonical text**:
  > Ֆերմայում ամեն ինչ խաղաղ էր։ Փոքրիկ Լյուսինիկը անընդհատ զբաղված էր իր սիրելի կենդանիների հետ։ Հանկարծ նա տեսավ փոքրիկ մկնիկ, որը փորձում էր բարձրանալ սեղանի վրա՝ կաթ ուտելու համար։ Լյուսինիկան ուրախացավ, և ոտքերը սկսեցին հպվել փոքրիկ պահեստին։
- **ChoiceA**: `Կարդա ձվի բառերը`
- **ChoiceB**: `Հեքիաթ հայտնիր ծնողներին`
- **non_armenian_chars**: 0
- **closed_or_moral_ending**: no marker
- **choices_related_to_text**: A 0/2 (0.0) — B 0/3 (0.0)

### Case 09

- **Prompt**: `պատմիր արկածային պատմություն`
- **Device**: `3f66adf4-ea53-4f2f-a31c-d19f2c5613f8`
- **HTTP**: 200 in 22.15s
- **Mode**: `story`  • **safetyFlag**: 0
- **conversationId**: `73ea6a65-bbf8-461c-9d5c-55c203fa18a8`
- **storySessionId**: `73ea6a65-bbf8-461c-9d5c-55c203fa18a8`
- **Canonical text**:
  > Փոքրիկ արջուկը փնտրում էր հետաքրքիր իրը անտառում։ Նա քայլում էր ծառերի մեջ ու լսում, թե ինչպես են թռչունները երգում։ Մի օր, նա տեսավ մի փայլուն քին, որը թաքնված էր մեխակների տակ։ Արջուկը շատ զարմացավ, քանի որ քարը փայլում էր, ինչպես փոքրիկ աստղ։ Ինչ-որ բան էր թաքնված դրա մեջ։
- **ChoiceA**: `Կռթնենք մոտակա ծառին`
- **ChoiceB**: `Հետեւենք թռչունիկին դեպի ծաղիկները`
- **non_armenian_chars**: 0
- **closed_or_moral_ending**: no marker
- **choices_related_to_text**: A 0/3 (0.0) — B 1/4 (0.25)

### Case 10

- **Prompt**: `ինչ կլինի եթե փոքրիկ թիթեռը կորի`
- **Device**: `c8a00e62-664b-40eb-a614-426c64aab5f3`
- **HTTP**: 200 in 21.12s
- **Mode**: `story`  • **safetyFlag**: 0
- **conversationId**: `86c869ca-88c9-4a19-abf0-3f764ca57ebd`
- **storySessionId**: `86c869ca-88c9-4a19-abf0-3f764ca57ebd`
- **Canonical text**:
  > Թիթեռինք, կարծես, սկսեց խաղալ մոր միստիկ ծառի տակ։ Թեթև քամի էր փչում, և թիթեռինք իր գույնզգույն թևերով թռչում էր ծառի տերևների միջև։ Բայց հանկարծ, նա այնքան զվարճանում էր, որ տարավ դեպի քիչ հեռու գտնվող ծաղիկների այգին։ Որքան զարմացավ, երբ հասկացավ, որ քիչ է գնացել, և այգին արդեն ծանոթ չէր իրեն։
- **ChoiceA**: `Բզեզն սկսեց խաղալ գնդակով`
- **ChoiceB**: `Թիթեռը նստեց ծաղկի վրա`
- **non_armenian_chars**: 0
- **closed_or_moral_ending**: no marker
- **choices_related_to_text**: A 2/4 (0.5) — B 1/3 (0.33)

## Heuristic summary table

| # | Mode  | Safety | non_arm | closed | A relatedness | B relatedness |
|---|-------|--------|---------|--------|---------------|---------------|
| 1 | story | Clean  | 0       | no     | 0.00 (0/3)    | 0.00 (0/4)    |
| 2 | story | Clean  | 0       | no     | 0.00 (0/3)    | 0.00 (0/4)    |
| 3 | story | Clean  | 1       | no     | 0.00 (0/3)    | 0.00 (0/2)    |
| 4 | story | Clean  | 0       | no     | 0.00 (0/3)    | 0.00 (0/3)    |
| 5 | story | Clean  | 0       | no     | 0.00 (0/4)    | 0.00 (0/3)    |
| 6 | story | Clean  | 0       | no     | 0.67 (2/3)    | 0.50 (1/2)    |
| 7 | story | Clean  | 0       | no     | 0.00 (0/2)    | 0.00 (0/2)    |
| 8 | story | Clean  | 0       | no     | 0.00 (0/2)    | 0.00 (0/3)    |
| 9 | story | Clean  | 0       | no     | 0.00 (0/3)    | 0.25 (1/4)    |
|10 | story | Clean  | 0       | no     | 0.50 (2/4)    | 0.33 (1/3)    |

## Observations (no scoring)

Repeated patterns visible across the fresh-conversation batch.
Flagged so tomorrow's evaluator pass shares the lens, not so they
shift the score.

1. **Choice block detached from canonical text — by far the
   loudest pattern.** 7 of 10 cases (01, 02, 03, 04, 05, 07, 08)
   have **zero token overlap** between either choice and the
   story body. Cases 01 / 02 / 04 / 07 introduce nouns in the
   choices ("քար" stone, "տուփ" box, "գետ" river, "կակաչ" poppy)
   that never appeared in the body the child just heard. Only
   cases 06, 09, 10 show partial topical alignment. This is much
   more visible now that session-bleed isn't masking it.
2. **Recurring "shiny mysterious object" trope.** Cases 01, 03,
   04, 06, 07, 09 all involve a glowing stone, a hidden box, or
   a mysterious object the protagonist has just discovered.
   Possible system-prompt bias.
3. **Folklore-adjacent imagery in case 01.** "Հայկական սարերի
   խորքում … ջրային աստվածուհի" (a water goddess deep in the
   Armenian mountains) — the project doc explicitly defers
   folklore work, and "աստվածուհի" leans pagan / mythological.
4. **Single non-Armenian glyph in case 03.** ASCII backtick
   `` ` `` (U+0060) used where an Armenian comma should be. The
   dramatic Cyrillic / Devanagari leakage that Phase 1's case-05
   exhibited did NOT recur in this fresh-conversation batch — so
   that one looks more likely tied to the contaminated long
   session than to the base prompt.
5. **No moral / wrap-up endings.** None of the 10 endings fired
   the closed-marker heuristic. Every case ends on an open
   beat — a question, a moment of wonder, or an unanswered
   sensation.
6. **All cases land on Story mode with `safetyFlag = Clean`.**
   No moderation trips, no mode misdetections, no Path-5 errors,
   no rate-limit rejections in this batch.
7. **Latency variance is wide.** Three cases under 10 s
   (01, 02, 03, 04 ≈ 6–10 s); the rest are ~21–23 s. Probably an
   upstream-side variance not a server-side regression — flagged
   only so the evaluator doesn't read it as a quality signal.
8. **Protagonist names mostly diminutive Armenian common nouns**
   (Արջուկ, Նապաստակ, Թիթեռնիկ, Խխունջ) but case 06 introduces
   "Կարապետ" (an adult-male first name) and case 08 introduces
   "Լյուսինիկ" (diminutive of a real woman's first name). Pure
   observation — naming-style judgment is for the linguistic
   reviewer, not this doc.

## Reproducer

```
/c/Python314/python /tmp/story-evidence/run_fresh.py
```

The script exits non-zero if the freshness contract isn't met
(unique devices / conversations / sessions all 10/10).

# Claude.app — Plan A controlled capture result — 2026-05-10

**Status:** evidence / documentation only. No code change, no
paid API call, no backend run, no Claude API use, no production
change, no ChatService touch, no provider switch authorized by
this document. The artifact is the raw captured output from a
manual Claude.app session plus a focused evaluator note.

**Companion files:**
- Capture prompt (commit `2400243`):
  `tools/StoryModelBakeoff/evaluations/claude-app-plan-a-controlled-capture-prompt-20260510.md`
- Controlled comparison plan (commit `bbe50fa`):
  `tools/StoryModelBakeoff/evaluations/controlled-claude-openai-comparison-plan-20260510.md`
- OpenAI v3.2.3 PA evidence (commit `e73975b`):
  `tools/StoryModelBakeoff/evaluations/openai-v3-2-3-smoke-mp1-20260510.md`

**Filename date** uses local Yerevan `2026-05-10` (the day the
capture session was prepared and run).

---

## 1. Context

- **Source:** Claude consumer app (claude.ai / Anthropic
  subscription). **NOT** Claude API. **NOT** routed through the
  bake-off runner.
- **Prompt source:** the Eastern-Armenian prompt at
  `tools/StoryModelBakeoff/evaluations/claude-app-plan-a-controlled-capture-prompt-20260510.md`,
  pasted byte-for-byte into a fresh Claude.app chat per the
  capture-prep document's § 2 operator instructions.
- **Fixed choice path:** **A → B** — Turn 2 continues after
  Turn 1 `CHOICE_A`; Turn 3 continues after Turn 2 `CHOICE_B`.
  Matches the OpenAI `v3.2.3 mp1 PA` run's choice path so the
  side-by-side comparison is fair (the prior A → A path noted
  in earlier drafts of the capture prompt was corrected before
  the capture).
- **Scenario:** same Plan A scenario family as the OpenAI run —
  forest / small animal helper / magical object / gentle
  mystery. Same hero (puppy), same friend/guide (old dog),
  same place (apple orchard), same magical object (golden
  dew-drop leaf), same small problem (stork seeking rest).
- **Capture style:** non-interactive, single-shot. Operator did
  not click choices, did not coach mid-run; Claude produced the
  full 3-turn arc in one reply under the requested labeled
  format.
- **Purpose:** controlled evidence sample for the Claude-vs-
  OpenAI comparison plan; no runtime decision implied.

---

## 2. Raw captured output

Preserved **byte-for-byte** from the Claude.app reply, including
labels, line breaks, and punctuation. Not normalized, not
trimmed, not "cleaned up." Any comments / evaluation downstream
refer to this block as the source of truth.

```
TURN_1_STORY:
Մի անգամ, երբ արևը նոր էր մայր մտնում, փոքրիկ շնիկ Բուռը վազեց դեպի խնձորենու այգի։ Ծառերը կանգնած էին հանգիստ ու տաք, ասես հին բարեկամներ սպասում էին նրան։ Հանկարծ Բուռը տեսավ մի արագիլ, որ թևածում էր ծառից ծառ՝ հոգնած ու անհանգիստ։ Ծեր շունը՝ Ղազարը, մոտեցավ դանդաղ ու ասաց. «Արագիլը տեղ է փնտրում հանգստանալու, Բուռ»։
CHOICE_A:
Բուռը վազում է ամենաբարձր խնձորենու մոտ՝ ամուր ճյուղ գտնելու արագիլի համար
CHOICE_B:
Բուռը դնչիկը բարձրացնում է ու հոտ է առնում՝ փնտրելու ամենամեղմ խոտածածկ անկյունը

TURN_2_STORY:
Բուռը վազեց ամենաբարձր խնձորենու մոտ ու նայեց վերև։ Ծառի ամենախոշոր ճյուղը լայն էր ու ամուր, կանաչ տերևներ ուներ։ Հենց այդ ճյուղի վրա փայլում էր մի ոսկեգույն տերև՝ ցողի կաթիլներով ծածկված, արևի վերջին ճառագայթից շողշողուն։ Ղազարը ժպտաց ու ասաց. «Այս տերևը հին բարեկամ է, Բուռ, նա ցույց կտա ճանապարհը»։
CHOICE_A:
Բուռը բարձրաձայն կաչկաչ է անում՝ արագիլին կանչելու համար
CHOICE_B:
Բուռը թաթով դիպչում է ոսկե տերևին, որ ցողի կաթիլները թռչեն վեր

TURN_3_STORY:
Բուռը թաթով դիպչեց ոսկե տերևին, և ցողի կաթիլները թռան վեր՝ ասես փոքրիկ աստղեր լուսնի ճանապարհին։ Արագիլը տեսավ փայլը, ծփծփաց թևերն ու հանգիստ իջավ լայն ճյուղի վրա։ Ծառը թեթևակի ճոճվեց՝ ողջունելով նոր հյուրին։ Ղազարը պառկեց խոտի վրա ու ցածր ձայնով ասաց. «Տեսա՞ր, Բուռ, երբ բարությամբ ես դիպչում, ամեն ինչ տեղն է ընկնում»։
CHOICE_A:
Բուռն ու Ղազարը դանդաղ քայլում են դեպի տուն ճանապարհով
CHOICE_B:
Բուռն ու Ղազարը նստում են ծառի տակ ու լսում գիշերվա հանգիստ ձայները
```

**Path verification:**
- T2 first sentence (`Բուռը վազեց ամենաբարձր խնձորենու մոտ ու նայեց վերև։`)
  ↔ T1 CHOICE_A (`Բուռը վազում է ամենաբարձր խնձորենու մոտ՝ ամուր ճյուղ
  գտնելու արագիլի համար`). **PASS.**
- T3 first sentence (`Բուռը թաթով դիպչեց ոսկե տերևին, և ցողի կաթիլները թռան վեր…`)
  ↔ T2 CHOICE_B (`Բուռը թաթով դիպչում է ոսկե տերևին, որ ցողի կաթիլները թռչեն վեր`).
  **PASS.**
- Choice path executed: **A → B as required.**

---

## 3. Rubric

| Dimension | Score |
|---|---|
| Armenian naturalness | **4 / 5** |
| Eastern Armenian correctness | **4 / 5** |
| Fairy-tale feeling | **3.5 / 5** |
| Warmth for age 4–7 | **4.5 / 5** |
| Length / pacing | **4.5 / 5** |
| Choice quality | **4 / 5** |
| Continuation coherence | **4.5 / 5** |
| Format stability | **PASS** (9/9 labeled fields, no extra prose, no markdown decoration, no emoji) |
| Fake Armenian / morphology | **PASS — with note** (no clearly fabricated tokens; one semantically awkward verb-noun pairing flagged in § 5) |
| Safety / age appropriateness | **PASS** |
| "Would I let Areg say this aloud?" | **YES, but not ideal** |

Non-native best-effort scoring on the 1–5 axes. The pass/fail
axes are the load-bearing cells; a native Eastern-Armenian
reviewer should still pass over the morphology cell before any
runtime decision.

---

## 4. Strengths

- **Excellent format compliance.** All 9 labeled fields produced
  in order (`TURN_1_STORY` / `CHOICE_A` / `CHOICE_B` × 3) on their
  own lines. No prefatory text ("Here is the story…"), no
  trailing meta ("Hope this helps"), no markdown bolding, no
  emoji. The non-interactive single-shot capture envelope worked
  cleanly.
- **Correct fixed path A → B.** Both continuation turns directly
  execute the simulated choice from the prior turn — T2 from
  T1 `CHOICE_A`, T3 from T2 `CHOICE_B`. No drift, no implicit
  pivot.
- **Short spoken length is good for toy voice.** All three turns
  comfortably under the 3–6 sentence cap; T1 ≈ 4 sentences, T2 ≈
  4 sentences, T3 ≈ 4 sentences. Length matches what Areg's
  spoken output budget calls for far better than any v3.2-era
  OpenAI run.
- **Gentle, safe, age-appropriate story.** No fear, no violence,
  no medical content. Tone is warm and quiet. Sensory details
  land child-graspably (golden leaf, dew drops as small stars,
  the orchard standing like old friends).
- **No English or meta leakage.** Zero English words, zero Latin
  transliteration, zero parenthetical narrator, zero "AI-
  assistant" tone slippage.
- **No obvious fake Armenian morphology.** Best-effort scan
  surfaced no clearly fabricated stems or coined participles —
  a meaningful contrast with the OpenAI v3.2.3 mp1 PA run, which
  produced four borderline / coined tokens across three turns
  (`բոցերում`, `փայլացնում`, `ցուցանի`, `անթել`).

---

## 5. Weaknesses

- **Opens with generic `Մի անգամ…`.** Exactly the opener pattern
  the Areg style is trying to move *away* from — it's the
  fairy-tale equivalent of "Once upon a time," and Areg's prompt
  layer was tuned to anchor T1 on the place (`Խնձորենու այգում`,
  etc.) for a stronger storyteller register. The Claude.app prompt
  did not explicitly forbid `Մի անգամ`; this opener choice is a
  weakness of the prompt as written, not necessarily of Claude.
- **Less magical and less vivid than the earlier Claude
  hedgehog/golden-leaf sample.** The 2026-05-01 Claude.app
  hedgehog capture (`claude-manual-pnjik-golden-leaf-20260501.eval.md`)
  reached a higher fairy-tale ceiling on its first turn —
  "Բարձր լեռների ստորոտում, որտեղ առվակը զրուցում էր քարերի
  հետ…" set classical-storyteller register on the first line.
  This run is competent but not premium.
- **`Բուռը բարձրաձայն կաչկաչ է անում` is semantically awkward
  for a dog.** `կաչկաչ` ("squawking / chirping") fits a bird or
  a small creature, not a dog. A native ear would flag this as
  off-register. Not a morphology fabrication; an idiomatic
  mismatch.
- **Final moral line is a little direct / lesson-like.**
  `«Տեսա՞ր, Բուռ, երբ բարությամբ ես դիպչում, ամեն ինչ տեղն է
  ընկնում»` reads as an explicit moral statement — closer to a
  teacher's wrap-up than Areg's preferred "warm storyteller who
  trusts the listener to feel the meaning" register. Borderline,
  not a hard fail.
- **Good but not premium enough to justify a provider switch.**
  This sample is structurally clean and morphology-safe, but it
  is not so clearly superior to OpenAI's structural output that
  the morphology vs format trade-off resolves on its own. The
  decision plan's "Claude wins on at least 3 of 5 scenarios"
  threshold (§ 6 of the comparison plan) is not approached by a
  single Plan A capture.

---

## 6. Architectural conclusion

- **Claude.app controlled Plan A capture is useful and safer
  than OpenAI v3.2.3 on fake-Armenian risk in this sample.**
  Zero borderline / coined tokens vs OpenAI's four. The
  morphology gap that the OpenAI v3.2.x exemplar-tightening
  ladder has hit a ceiling against does not appear here.
- **But this sample is not clearly superior enough for a
  runtime / provider decision.** The fairy-tale-feeling axis is
  only 3.5 / 5; the opener uses the generic `Մի անգամ` pattern;
  the final line drifts toward a lesson register; one
  idiomatic mismatch (`կաչկաչ` for a dog) lands awkwardly.
  Better than OpenAI v3.2.3 on Armenian texture, not premium.
- **Claude remains promising but not proven.** A second
  controlled capture (Plan D) is the next decision-relevant
  data point.
- **OpenAI remains structurally strong but Armenian-risky.**
  The v3.2.3 mp1 PA evidence file still stands: hard rules pass,
  structural envelope is the strongest of any v3.2-era run, but
  morphology slips through round after round.
- **No provider switch.** ChatService routes to OpenAI as
  production. This evidence enters as one row of the comparison
  plan matrix, not as a decision input.
- **No ChatService change.** No runtime configuration change.
  No production system-prompt change. No NuGet add. No tests
  touched.
- **More controlled samples are needed** — specifically Plan D
  (the harder scenario that historically exposed OpenAI's
  morphology failure modes) and one child-natural home / family
  / play scenario (PE per the comparison plan § 4, still to
  design).

---

## 7. Next safe step

1. **Commit this evidence file only after review.** Single-file
   commit, suggested message
   `docs(story): record claude app plan a controlled capture`.
   Do not stage `.claude/settings.local.json`, the
   `manual-plan-d-v3-1-capture/session/` directory, or
   `tools/story-quality-evidence-20260425.md` — pre-existing
   local noise.
2. **Then capture one controlled Plan D Claude.app sample using
   the same protocol** as the Plan A prompt — fresh chat,
   non-interactive single-shot, fixed path A → B, capture-
   friendly labeled format, same operator pre-paste +
   post-capture checklist discipline. The Plan D capture prompt
   itself is a future slice and is not authored here.
3. **Do not touch production.** No ChatService change, no
   provider config change, no system-prompt change, no parser
   adaptation, no NuGet add, until at least Plan A + Plan D
   captures are reviewed across both providers with a native
   Eastern-Armenian pass on the morphology cell.
4. **No paid Claude API call** until: (a) Plan A + Plan D manual
   app captures are reviewed, (b) a parser-compatibility plan
   exists for Claude API output, (c) an Anthropic API key is
   provisioned, (d) explicit GO from Hayk for the spend.

---

## 8. No secrets included

This file contains no API key, no token, no bearer credential,
no parent JWT, no device API key, no private endpoint, no
Anthropic account identifier, no Claude.app session identifier.
The capture was a manual browser session under the operator's
own Anthropic subscription; no credential material reached this
file or the captured raw block.

---

## 9. Scope guard

No production / runtime files were touched by this capture or by
this evidence file: `ChatService`, backend code, frontend,
`appsettings*.json`, `*.csproj`, tests, seed bank, name bank,
story-plan generator, validator, runtime system prompts
(production sha
`54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
unchanged), speech / TTS / STT — all unchanged. No paid API call
was made; no backend was started; no provider configuration was
touched; Claude API was not used. The only artifact is this
markdown under `tools/StoryModelBakeoff/evaluations/`.

# Claude.app — Plan D controlled capture result — 2026-05-10

**Status:** evidence / documentation only. No code change, no
paid API call, no backend run, no Claude API use, no production
change, no ChatService touch, no provider switch authorized by
this document. The artifact is the raw captured output from a
manual Claude.app session plus a focused evaluator note.

**Companion files:**
- Plan D capture prompt (commit `9671843`):
  `tools/StoryModelBakeoff/evaluations/claude-app-plan-d-controlled-capture-prompt-20260510.md`
- Plan A capture result (commit `d80318d`):
  `tools/StoryModelBakeoff/evaluations/claude-app-plan-a-controlled-capture-result-20260510.md`
- Controlled comparison plan (commit `bbe50fa`):
  `tools/StoryModelBakeoff/evaluations/controlled-claude-openai-comparison-plan-20260510.md`
- OpenAI PD bake-off path is encoded in the existing scenario at
  `tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json` (id `PD`)
  and the OpenAI PD smoke evidence at
  `tools/StoryModelBakeoff/evaluations/openai-v3-2-1-smoke-mp2-20260509.md`
  / `openai-v3-2-2-smoke-mp2-20260510.md`.

**Filename date** uses local Yerevan `2026-05-10` (the day the
capture session was prepared and run).

---

## 1. Context

- **Source:** Claude consumer app (claude.ai / Anthropic
  subscription). **NOT** Claude API. **NOT** routed through the
  bake-off runner.
- **Prompt source:** the Eastern-Armenian prompt at
  `claude-app-plan-d-controlled-capture-prompt-20260510.md`,
  pasted byte-for-byte into a fresh Claude.app chat per the
  capture-prep document's § 2 operator instructions.
- **Fixed choice path:** **A → B** — Turn 2 continues after
  Turn 1 `CHOICE_A`; Turn 3 continues after Turn 2 `CHOICE_B`.
  Matches the OpenAI PD bake-off scenario path
  (T2 `SELECTED_CHOICE: Ա`, T3 `SELECTED_CHOICE: Բ`) and the
  Claude PA capture path (commit `d80318d`) for cross-matrix
  comparability.
- **Scenario:** same Plan D scenario family as the existing
  OpenAI PD bake-off evidence — lizard hero, frightened
  duckling friend, old bridge, sleeping key, snow-not-melting
  small problem, patience-style resolution, gently-wintery
  mood. The bake-off-side `STORY PLAN` block at
  `bakeoff-prompts-v3-1.json` id `PD` was distilled into a
  natural-Armenian scenario brief for Claude.app per the
  capture-prep doc § 1.1.
- **Capture style:** non-interactive, single-shot. Operator did
  not click choices, did not coach mid-run; Claude produced the
  full 3-turn arc in one reply under the requested labeled
  format.
- **Purpose:** controlled evidence sample for the Claude-vs-
  OpenAI comparison plan on the *harder* PD scenario; no
  runtime decision implied.

---

## 2. Raw captured output

Preserved **byte-for-byte** from the Claude.app reply, including
labels, line breaks, and punctuation. Not normalized, not
trimmed, not "cleaned up." Any comments / evaluation downstream
refer to this block as the source of truth.

```
TURN_1_STORY:
Հին կամուրջի տակ, որտեղ ձյունը դրել էր իր սպիտակ վերմակը, մի փոքրիկ մողես արթնացավ։ Նա ձգեց պոչը, կռացավ, և տեսավ, որ բոլոր քարերը փայփայված են ձյունով։ Կամուրջի ծայրին մի փոքրիկ բադիկ կանգնած էր ու դողում էր, ականջները ճկված, աչքերը գոց։ Բադիկը ցույց տվեց ներքև, ու մողեսը հասկացավ. առվակը լուռ էր, ձյան տակ ամրացած, ու ոչ մի ձայն չէր գալիս ջրից։ Կամուրջի մի անկյունում, հին քարի վրա, պառկած էր մի բանալի՝ ժանգոտ, անշարժ, կարծես խոր քնած։
CHOICE_A:
Մողեսը մոտենա բանալուն ու ականջը տնկի՝ լսելու, թե արդյոք այն շնչում է։
CHOICE_B:
Մողեսը թևը դնի բադիկի թևի վրա ու նրա հետ նայի ներքև՝ դեպի ձյան տակ պատված առվակը։

TURN_2_STORY:
Մողեսը մոտեցավ բանալուն ու ականջը կռացրեց հենց մոտը։ Շատ հանգիստ — ու լսեց. ոչ ձայն, ոչ շշուք, բայց ինչ-որ մի բան, ինչպես մի հառաչ, ինչպես մի շատ հեռու, շատ հին երազ։ «Դեռ քնած է», — ասաց մողեսը, — «բայց ոչ մեռած»։ Բադիկը մի քայլ առաջ արեց ու հարցրեց. «Ի՞նչ անենք»։ Մողեսը նայեց կամուրջին, նայեց բանալուն, հետո նայեց ձյան ծայրերին, ուր արևի բաց լույսը ընկնում էր մի բարակ, ոսկեգույն շերտով։
CHOICE_A:
Մողեսը բանալու կողքին նստի ու ձայն հանի, ինչպես փոքրիկ քամին, մինչև բանալին ուշ-ուշ ականջ կախի։
CHOICE_B:
Մողեսն ու բադիկը միասին անցնեն կամուրջի մյուս կողմ ու ձեռքով հատ-հատ մաքրեն ձյան բարակ շերտը ժայռի վրայից։

TURN_3_STORY:
Մողեսն ու բադիկը անցան կամուրջի մյուս կողմ ու սկսեցին ձեռքով, հատ-հատ, մաքրել ձյան բարակ շերտը ժայռի վրայից։ Բադիկի կտուցը ճարպիկ էր, ու մողեսի մատները հայտնաբերեցին ժայռի ջերմ, հին հոտ՝ փայտ, ու հող, ու ինչ-որ բան, որ ձմեռ չէ արդեն։ Հետո — ոչ ոք ոչինչ չանեց, ոչ ոք ոչ մի բառ չասաց — ձյան ծայրը մի փոքր հալվեց, ու ժայռի տակից մի բարակ, հազիվ լսելի ձայն սողաց. տտ, տտ, տտ։ Բանալին, ոչ ոք չէ նայել, պտտվեց մի կես պտույտ, ու կամուրջի տակ առվակը ձայն հանեց՝ ինչպես մի փոքրիկ, քնաթաթախ ձայն, որ նոր-նոր կռկռում է։ Մողեսն ու բադիկը կանգ առան ու լսեցին, ու ոչ ոք ոչինչ չասաց, որովհետև ջրի ձայնն ինքն էր ասում ամեն ինչ։
CHOICE_A:
Մողեսն ու բադիկը դանդաղ քայլեն տուն՝ ձյան վրա իրենց հետքերը թողնելով։
CHOICE_B:
Մողեսն ու բադիկը նստեն կամուրջի եզրին ու ականջ կախեն, մինչև առվակի ձայնն ավելի բարձր դառնա։
```

**Path verification:**
- T2 first sentence (`Մողեսը մոտեցավ բանալուն ու ականջը կռացրեց
  հենց մոտը։`) ↔ T1 CHOICE_A (`Մողեսը մոտենա բանալուն ու
  ականջը տնկի՝ լսելու, թե արդյոք այն շնչում է։`). **PASS.**
- T3 first sentence (`Մողեսն ու բադիկը անցան կամուրջի մյուս կողմ
  ու սկսեցին ձեռքով, հատ-հատ, մաքրել ձյան բարակ շերտը ժայռի
  վրայից։`) ↔ T2 CHOICE_B (`Մողեսն ու բադիկը միասին անցնեն
  կամուրջի մյուս կողմ ու ձեռքով հատ-հատ մաքրեն ձյան բարակ
  շերտը ժայռի վրայից։`). **PASS.**
- Choice path executed: **A → B as required.**

**Schwa-not-drop verification (PD-specific):**
- T1 sentence 1 opens with `Հին կամուրջի տակ, որտեղ…` — the
  `հին կամուրջ` stem letters are preserved (no `Կամրջի…`
  drop). **PASS** on the PD R3 care note that defeated the
  OpenAI side at v3.2.1 mp2.

---

## 3. Rubric

| Dimension | Score |
|---|---|
| Armenian naturalness | **3.8 / 5** |
| Eastern Armenian correctness | **3.5 / 5** |
| Fairy-tale feeling | **4.5 / 5** |
| Warmth for age 4–7 | **4 / 5** |
| Length / pacing | **4 / 5** |
| Choice quality | **4 / 5** |
| Continuation coherence | **4.5 / 5** |
| Format stability | **PASS** (9/9 labeled fields, no extra prose, no markdown decoration, no emoji) |
| Fake Armenian / morphology | **PASS — with concern** (no clearly fabricated stems, but several native-ear semantic / animal-anatomy mismatches flagged in § 5) |
| Safety / age appropriateness | **PASS** |
| "Would I let Areg say this aloud?" | **NO, but close** |

Non-native best-effort scoring on the 1–5 axes. The pass/fail
axes are the load-bearing cells; a native Eastern-Armenian
reviewer should still pass over the morphology and animal-
anatomy cells before any runtime decision.

---

## 4. Strengths

- **Strong winter fairy-tale mood.** The opening line `Հին
  կամուրջի տակ, որտեղ ձյունը դրել էր իր սպիտակ վերմակը` lands
  the gently-wintery register on the first sentence — atmosphere
  is the clearest single win of this run. Sleeping key, silent
  stream under the snow, sunlight slipping across the snow's
  edge — the scenario family is honoured with sensory detail.
- **Scenario integration works well.** Every load-bearing
  element of the bake-off PD scenario lands: old bridge,
  sleeping key, silent stream, gentle snow, patience-style
  resolution. The hero and the duckling read as characters,
  not props.
- **Correct fixed path A → B.** Both continuation turns
  directly execute the simulated choice from the prior turn — T2
  from T1 `CHOICE_A`, T3 from T2 `CHOICE_B`. No drift, no
  implicit pivot.
- **Clean label format.** All 9 labeled fields produced in
  order (`TURN_1_STORY` / `CHOICE_A` / `CHOICE_B` × 3) on their
  own lines. No prefatory text, no trailing meta, no markdown
  bolding, no emoji. The non-interactive single-shot capture
  envelope worked cleanly again.
- **No English or meta leakage.** Zero English words, zero
  Latin transliteration, zero parenthetical narrator, zero
  "AI-assistant" tone slippage.
- **Avoids obvious fake Armenian morphology.** Best-effort scan
  surfaced no clearly fabricated stems or coined participles.
  This matches the Plan A Claude capture and continues the
  contrast with the OpenAI v3.2.3 mp1 PA run (four borderline
  / coined tokens across three turns on OpenAI's side).
- **Avoids direct patience moralizing better than expected.**
  The PD-specific aphorism stress (`«համբերությունը հաղթում է»`
  family) is dodged: the resolution comes through the
  characters' silent action and inaction rather than through an
  explicit aphorism. The closest line, `«Հետո — ոչ ոք ոչինչ
  չանեց, ոչ ոք ոչ մի բառ չասաց»`, is *showing* patience rather
  than *naming* it.

---

## 5. Weaknesses

- **Animal anatomy mismatch — duckling has `ականջները ճկված`.**
  Ducks (`բադիկ`) do not have visible external ears; this
  detail reads as anatomically off to a native ear that
  visualizes the scene. Borderline picture-book error.
- **Animal anatomy mismatch — lizard uses `թև`.** T1 CHOICE_B
  has `Մողեսը թևը դնի բադիկի թևի վրա ու նրա հետ նայի ներքև`.
  `թև` means "wing / arm"; lizards have *legs* (`ոտք`) and
  *paws* (`թաթ`), not wings. This is the single most
  load-bearing nature error in the run, and Claude *offered*
  this as a choice — so even though the model executed
  CHOICE_A (not this one), the choice block itself encodes the
  mismatch.
- **Some poetic phrases are unnatural for Areg.** `քարերը
  փայփայված են ձյունով` is literary / book-page register
  rather than spoken storyteller register. Beautiful on the
  page; awkward read aloud by a 4–7-year-old's voice toy.
- **Awkward grammar / structure: `Բանալին, ոչ ոք չէ նայել,
  պտտվեց մի կես պտույտ…`.** The middle parenthetical is
  ungrammatical Eastern Armenian (`ոչ ոք չէ նայել` should be
  `ոչ ոք չէր նայում` or `առանց որ որևէ մեկը նայեր`); the
  whole construction reads like an attempted translation of a
  literary English sentence rather than native Armenian.
- **`կռկռում է` is semantically odd for a stream / water
  voice.** `կռկռալ` is used for crow / chicken / duck sounds,
  not water. A native ear would flag this as off-register. The
  intended sense is closer to `գլգլալ` / `քրքրալ` / `շշնջալ`
  for a small awakening stream.
- **`տտ, տտ, տտ` sound effect may sound broken aloud.** The
  on-page rendition works as ASCII texture for a young
  listener, but spoken through a toy's TTS it is more likely
  to be read as a literal "t, t, t" than as a soft tapping
  sound. Borderline; depends on TTS handling.
- **Turn 3 is atmospheric but too literary / abstract for
  4–7.** Phrases like `որովհետև ջրի ձայնն ինքն էր ասում ամեն
  ինչ` and `ինչպես մի փոքրիկ, քնաթաթախ ձայն, որ նոր-նոր
  կռկռում է` lean toward a poetic ending rather than a child-
  graspable closing. The age-7-richer band tolerates more
  texture than age-4-simple, but this drifts past the band.
- **Not production-ready for Areg say-aloud.** Aggregating the
  anatomy + register + grammar + odd-sound issues: a native
  Eastern-Armenian reviewer would almost certainly flag at
  least two of these for fix before letting Areg speak this
  passage aloud to a child. Provisional "no" on the say-aloud
  cell.

---

## 6. Architectural conclusion

- **Claude.app Plan D is stronger than OpenAI on fake-Armenian
  morphology risk in this sample.** Best-effort scan shows no
  clearly coined stems or fabricated participles — continuing
  the contrast established on the PA capture and absent in the
  OpenAI v3.2.x exemplar-tightening ladder runs (which keep
  producing fresh borderline tokens round after round).
- **Claude.app Plan D is stronger in fairy-tale mood than the
  controlled Plan A Claude capture.** The atmospheric ceiling
  is notably higher here than on PA (which hit 3.5 / 5 on the
  fairy-tale-feeling axis). The wintery PD setting plays to
  Claude's strengths.
- **But Claude still has native-ear semantic / anatomy issues.**
  The animal-anatomy mismatches (duckling ears, lizard wing),
  the off-register water-voice verb (`կռկռում է`), and the
  ungrammatical middle-clause structure are not
  morphology-fabrication issues but they are still issues a
  native ear catches immediately. The PD capture defeats the
  "model produces flawless Armenian" hypothesis the PA capture
  could have invited.
- **Claude is promising but not proven.** Two controlled
  captures (PA, PD) both clear the morphology cell; neither
  clears the "say it aloud" cell. The decision-plan threshold
  ("Claude wins on 3 of 5 scenarios including PA and PD" — §6
  of the comparison plan) is *partially* met on the
  morphology dimension and *not* met on the say-aloud
  dimension.
- **OpenAI remains structurally strong but Armenian-risky.**
  Unchanged by this run; the v3.2.3 mp1 PA evidence file
  still stands.
- **No provider switch.** ChatService routes to OpenAI as
  production. This evidence enters as one row of the
  comparison plan matrix, not as a decision input.
- **No ChatService change.** No runtime configuration change.
  No production system-prompt change. No NuGet add. No tests
  touched.
- **More evidence is needed**, specifically a child-natural
  home / family / play scenario (PE per the comparison plan
  § 4, still to design) to test whether Claude's atmospheric-
  prose strength holds outside enchanted-forest /
  enchanted-bridge tropes. A stricter Claude prompt that
  pre-warns against animal-anatomy mismatches would also be
  worth one paid Claude API call once an Anthropic key lands
  — but that is a future slice, not authorized here.

---

## 7. Next safe step

1. **Commit this evidence file only after review.** Single-file
   commit, suggested message
   `docs(story): record claude app plan d controlled capture`.
   Do not stage `.claude/settings.local.json`, the
   `manual-plan-d-v3-1-capture/session/` directory, or
   `tools/story-quality-evidence-20260425.md` — pre-existing
   local noise.
2. **Then create a small "story-brain findings summary"** that
   aggregates the four data points landed so far:
   - Claude manual hedgehog sample
     (`claude-manual-pnjik-golden-leaf-20260501.eval.md` /
     `claude-app-manual-sample-aregb-rubric-20260510.md`)
   - Claude controlled Plan A
     (`claude-app-plan-a-controlled-capture-result-20260510.md`)
   - Claude controlled Plan D (this file)
   - OpenAI v3.2.3 Plan A
     (`openai-v3-2-3-smoke-mp1-20260510.md`)
   The summary should restate the per-axis rubric pattern, name
   which axis each provider wins, and reaffirm the conservative
   decision thresholds from the comparison plan. It should NOT
   make the provider decision. Filename suggestion:
   `story-brain-findings-summary-20260511.md`.
3. **Do not touch production.** No ChatService change, no
   provider config change, no system-prompt change, no parser
   adaptation, no NuGet add, until the summary review has
   landed and at least one more scenario (PE child-natural home
   / family / play) has been captured across both providers.
4. **No paid Claude API call** until: (a) the findings summary
   exists, (b) PE is designed and captured, (c) a parser-
   compatibility plan exists for Claude API output, (d) an
   Anthropic API key is provisioned, (e) explicit GO from Hayk
   for the spend.

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

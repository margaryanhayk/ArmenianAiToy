# Story-brain findings summary — 2026-05-10

**Status:** evidence summary only. No code change, no paid API
call, no backend run, no Claude API use, no production change,
no ChatService touch, no provider switch implied or authorized
by this document. Pulls the per-axis pattern out of five data
points landed across recent capture sessions and restates the
conservative decision thresholds from the comparison plan.

**Source evidence files:**
- Claude.app manual hedgehog rubric (commit `16537e5`):
  `tools/StoryModelBakeoff/evaluations/claude-app-manual-sample-aregb-rubric-20260510.md`
- OpenAI v3.2.3 mp1 PA evidence (commit `e73975b`):
  `tools/StoryModelBakeoff/evaluations/openai-v3-2-3-smoke-mp1-20260510.md`
- Claude.app Plan A controlled capture result (commit `d80318d`):
  `tools/StoryModelBakeoff/evaluations/claude-app-plan-a-controlled-capture-result-20260510.md`
- Claude.app Plan D controlled capture result (commit `471bbf6`):
  `tools/StoryModelBakeoff/evaluations/claude-app-plan-d-controlled-capture-result-20260510.md`
- Claude.app Home/Play controlled capture result (commit `8c944e5`):
  `tools/StoryModelBakeoff/evaluations/claude-app-home-play-controlled-capture-result-20260510.md`
- Controlled comparison plan (commit `bbe50fa`):
  `tools/StoryModelBakeoff/evaluations/controlled-claude-openai-comparison-plan-20260510.md`

**Filename date** uses local Yerevan `2026-05-10` for consistency
with the rest of this evidence batch.

---

## 1. Executive summary

- **Claude is promising for Armenian fairy-tale mood.** The
  manual hedgehog sample and the controlled Plan D capture both
  reached register a 4–7-year-old's bedtime storyteller would
  want, with concrete sensory imagery and warm, gentle tone.
- **OpenAI v3.2.3 improved structure but still has fake /
  borderline Armenian risk.** Hard rules (length, choice block,
  closure, place-stem) all pass cleanly; morphology slips keep
  appearing round after round of named-exemplar tightening.
- **Claude controlled captures also have problems.** Plan A was
  safe but not magical enough (generic `Մի անգամ` opener, mild
  lesson-style closing). Plan D had higher atmosphere but
  native-ear semantic / animal-anatomy issues (duckling ears,
  lizard wing, off-register `կռկռում` for water).
- **Claude Home/Play is the cleanest practical say-aloud
  controlled sample so far.** The PE scenario (Նարե / her room /
  doll Մոմո / missing little pillow) scored strongly on natural
  everyday Armenian, short pacing, format stability, fake-
  Armenian safety, and semantic / body-part sanity. Less
  magical / premium than the hedgehog sample, but more
  production-realistic for everyday toy use. Supports the
  finding that Claude performs better when the prompt constrains
  register to simple everyday Armenian and familiar settings.
  Does NOT justify a provider switch by itself.
- **No provider switch yet.** Neither side has cleared the
  comparison-plan thresholds. ChatService still routes to
  OpenAI in production.
- **No ChatService change yet.** No provider config change. No
  runtime system-prompt change. No parser adaptation. No NuGet
  add. This document is evidence aggregation, not a decision.

---

## 2. Evidence table

| Run | Source file | Capture method | Scenario | Strongest strength | Biggest weakness | Say-aloud verdict | Provider-decision impact |
|---|---|---|---|---|---|---|---|
| **CLA-manual-hedgehog** | `claude-app-manual-sample-aregb-rubric-20260510.md` | Manual Claude.app, scenario captured without Areg-specific prompt; consumer-app system prompt | Forest / hedgehog / golden-leaf / lake → silver bell (uncontrolled) | Strong classical fairy-tale register and natural Eastern Armenian | Slightly long for spoken toy; non-parser-compatible choice format (emoji + `Հիմա ի՞նչ անի…`) | YES, with slight length control | Ceiling signal for Claude *consumer-app* prose. Not a controlled API-vs-API datapoint. |
| **OAI-v3.2.3-mp1-PA** | `openai-v3-2-3-smoke-mp1-20260510.md` | OpenAI API via `StoryModelBakeoff` runner, gpt-4o, max-prompts-1 | PA: puppy / orchard / dew-drop golden leaf / stork | Best-yet structural envelope across v3.2-era runs: hard-tally 9/10, T3 mid-band ~90w, all turns in target word-band for first time, no English/meta leakage, safety PASS | Four borderline / coined Armenian tokens across 3 turns (`բոցերում`, `փայլացնում`, `ցուցանի`, `անթել`); recurring mid-paragraph `Մի օր,` opener slip in T1 sentence 6 | NO, borderline | OpenAI structurally strong, Armenian-risky. Same shape every round; named-exemplar tightening has hit a ceiling. |
| **CLA-app-PA-controlled** | `claude-app-plan-a-controlled-capture-result-20260510.md` | Manual Claude.app, controlled prompt (Areg persona, PD scenario family, A → B path, 9-label format) | PA (same scenario family as the OpenAI PA bake-off run) | Format compliance perfect; correct A → B path; no English / meta; no obvious fake Armenian morphology; gentle / safe | Generic `Մի անգամ` opener; `կաչկաչ` (a bird-style verb) used for a dog; mildly lesson-style closing line | YES, but not ideal | Claude clearly safer than OpenAI on fake-Armenian risk on the matched scenario, but not premium enough to justify a switch by itself. |
| **CLA-app-PD-controlled** | `claude-app-plan-d-controlled-capture-result-20260510.md` | Manual Claude.app, controlled prompt (Areg persona, PD scenario family, A → B path, schwa-not-drop + no-aphorism PD care, 9-label format) | PD: lizard / frightened duckling / old bridge / sleeping key / snow / patience resolution | Strong winter fairy-tale mood; cleanly avoids the patience-aphorism trap; schwa-not-drop on `հին կամուրջ` PASS (exactly the care note OpenAI v3.2.1 mp2 failed); format compliance perfect; A → B path correct; no obvious fake Armenian | Native-ear semantic / anatomy issues: duckling with `ականջները ճկված`, lizard offered `թև` in T1 CHOICE_B; `կռկռում է` for an awakening stream; ungrammatical middle clause `Բանալին, ոչ ոք չէ նայել, պտտվեց…`; T3 leans too literary for age 4–7 | NO, but close | Closes the "Claude has flawless Armenian" hypothesis. Promising-not-proven status confirmed. |
| **CLA-app-Home-Play-controlled** | `claude-app-home-play-controlled-capture-result-20260510.md` | Claude.app manual, controlled prompt (Areg persona, PE child-natural home/play scenario, A → B path, 9-label format, anti-magical + anti-poetic register rules) | PE: Նարե (≈ 5 y.o.) / her bedroom / doll Մոմո / missing little pillow | Clean practical say-aloud everyday Armenian; format compliance perfect; A → B path correct; no fake Armenian; no animal-anatomy mismatch (no animal hero by design); concrete physical choices; safe + warm for ages 4–7 | Slightly plain; less memorable than the magical hedgehog sample; one minor wording/coherence issue around `Մոմոյին դրեց բարձի կողքին` in T2 (pillow not yet found at that point) | YES | Strengthens Claude as candidate for everyday Areg speech; still no runtime switch by itself. |

---

## 3. Main findings

- **Claude generally stronger on fairy-tale mood than OpenAI.**
  Across the three Claude data points the atmospheric ceiling
  is consistently higher than the OpenAI v3.2.3 PA sample.
  Classical storyteller register, sensory imagery, gentle
  problem framing all land more naturally on the Claude side.
- **Claude currently looks lower-risk for fake Armenian
  morphology than OpenAI.** Best-effort scans across the three
  Claude captures surfaced **zero clearly fabricated stems or
  coined participles.** The OpenAI v3.2.3 PA run produced
  **four** borderline / coined tokens across three turns,
  continuing a pattern visible across the v3.2.x ladder. This
  is the single most decision-relevant axis at the moment.
- **Claude is not automatically production-ready, because it
  can produce native-ear semantic / anatomy errors.** Plan D
  exposed the failure mode: a model that writes beautiful
  Armenian fairy-tale prose can still get an animal's anatomy
  wrong, use a bird-sound verb for a stream, or fold an
  ungrammatical middle clause into a literary sentence. Native
  Armenian review is required regardless of which provider is
  in the runtime.
- **OpenAI can follow structure / length / pacing better after
  prompt tightening.** The v3.2-era exemplar ladder
  (v3.2 → v3.2.1 → v3.2.2 → v3.2.3) has stabilized the hard
  rules (opener, closure, choice-block byte-for-byte, place
  stem, word-band landing). Engineering envelope strongest of
  any paid run.
- **OpenAI still risks invented or borderline Armenian forms.**
  The same shape (model coinage from a near-stem) recurs every
  round. v3.2.2 hit `Խտնված`; v3.2.3 hit
  `բոցերում`/`փայլացնում`/`ցուցանի`/`անթել`. Each new exemplar
  list catches the previous family and the next family appears.
  The named-exemplar approach has hit a ceiling.
- **The best controlled evidence is now scenario-dependent.**
  For *magical / fairy-tale* register, the manual Claude
  hedgehog sample remains the strongest — but it is **not fully
  controlled enough for runtime decision** (captured under the
  consumer-app's own system prompt and decoding, not the Areg
  system prompt; the choice block format is non-parser-
  compatible (emoji + `Հիմա ի՞նչ անի…`); single scenario;
  single capture). For *practical everyday say-aloud* register,
  the **Claude.app Home/Play controlled capture is now the
  strongest** controlled sample — clean format, clean
  morphology, clean semantics, age-appropriate everyday
  vocabulary, no animal-anatomy surface (no animal hero by
  design), no aphorism closing. Useful, but one PE sample under
  controlled conditions still does not clear the comparison-plan
  thresholds.
- **Claude performs noticeably better when the prompt constrains
  register to simple everyday Armenian.** The contrast between
  PD (literary winter register, native-ear semantic slips) and
  Home/Play (everyday register, clean) suggests Claude's failure
  mode is correlated with poetic / metaphor-dense affordances.
  A production prompt that pins the register tight should be
  the default if Claude ever ships.

---

## 4. Risk comparison

| Provider | Risk | Detail |
|---|---|---|
| **OpenAI** | Fake Armenian / invented morphology | Coined verbs / participles / nouns from near-stems. Recurs every round of v3.2.x tightening. |
| **OpenAI** | Structurally good but native Armenian unreliable | Hard rules clean; morphology unreliable. A 4–7-year-old hearing one coined word is one too many. |
| **Claude** | Poetic but semantically odd | Literary register can drift into off-register verb pairings (`կռկռում է` for a stream, `փայփայված են ձյունով`). |
| **Claude** | Animal anatomy mistakes | Duckling with bent ears; lizard offered a `թև`. Picture-book-level errors that a child would visually catch. |
| **Claude** | App behavior may differ from API behavior | All Claude evidence to date is consumer-app, not Claude API. App quality is an upper bound; production deploy would consume API output under the Areg system prompt. The gap is unknown. |
| **Both** | One or two samples not enough | Variance between captures (or temperature draws) can produce a cherry-picked win that does not generalize. The comparison plan requires multi-scenario evidence (PA + PD + at least one child-natural + a Calm scenario) before any switch. |

---

## 5. Architectural recommendation

- **Do not switch runtime provider yet.** Neither Claude nor
  OpenAI has cleared the comparison-plan thresholds (§6 of
  `controlled-claude-openai-comparison-plan-20260510.md`).
  Claude is promising-not-proven; OpenAI is structurally-strong-
  but-Armenian-risky.
- **Do not wire Claude into ChatService yet.** No code path
  change. The bake-off runner already supports a Claude live
  path (commit `0f362f7`); using it in production is a separate,
  load-bearing slice that needs (a) an Anthropic API key
  provisioned, (b) a parser-compatibility plan for Claude API
  output, (c) cost / rate-limit modeling, (d) explicit GO from
  Hayk for paid Claude API spend.
- **Do not continue blind OpenAI prompt tightening as the only
  path.** The v3.2.x exemplar ladder has hit a clear ceiling on
  the morphology axis. A v3.3 design would need to shift from
  *list of bad tokens* to a *structural rule* ("use only verbs /
  participles whose 3rd-person past form you can name aloud
  first") and probably a *positive whitelist* of safe stems.
  Worth designing on paper *before* burning another paid mp1 on
  another exemplar pass.
- **Continue controlled evidence collection.** The matrix now
  has PA + PD + PE Claude.app controlled captures, plus
  OpenAI v3.2.3 PA via API. Still missing: a Calm-mode
  controlled capture for both providers, a native Armenian
  review pass of the full set, and at least one matched PE on
  the OpenAI side if a decision needs forcing.
- **Add a stricter semantic / naturalness checklist** to future
  capture prompts and evaluation files. Today the rubric covers
  "fake Armenian / morphology" but not "animal anatomy",
  "verb-noun semantic fit", or "spoken-register vs literary-
  register" — all three were load-bearing in the Plan D capture
  review. A native Armenian reviewer's eye also needs to be
  part of every "say it aloud" cell, not optional.
- **Child-natural Home/Play scenario is now captured.** The
  Claude.app Home/Play controlled result (commit `8c944e5`) is
  the load-bearing test for the hypothesis that Claude's
  atmospheric-prose strength extends to ordinary kitchen-table
  Armenian. It **does** extend — the practical-Armenian sample
  is clean and say-aloud-positive. This **strengthens Claude as
  a candidate** for an eventual provider decision but **does not
  by itself clear the comparison-plan thresholds**. One PE
  capture is not the matrix; a Calm-mode capture and a native
  Armenian review pass are still required.

---

## 6. Prompt / rule implications

Future story-brain prompt rules — apply regardless of which
provider eventually runs in production. These are
recommendations only, not edits to any production prompt.

- **Avoid generic opener `Մի անգամ…`** in T1 sentence 1
  unless intentionally allowed for a specific scenario (e.g. a
  bedtime fairy-tale that *wants* the classical opener). Anchor
  T1 on the place stem instead.
- **For animal characters, use body parts and actions
  appropriate to that animal.** Ducks have `կտուց` and
  `թևիկներ`, not `ականջներ ճկված`. Lizards have `ոտքեր` /
  `թաթեր`, not `թևեր`. Cats / dogs do not use bird-sound verbs
  like `կաչկաչ` / `կռկռում`. A short, explicit per-scenario
  "animal-anatomy reminder" line in the prompt would have
  blocked both Plan D mismatches.
- **Prefer clear child-understandable imagery over dense
  poetic abstraction.** "The water voice said everything"
  (`ջրի ձայնն ինքն էր ասում ամեն ինչ`) is beautiful on the
  page and obscure for a 4-year-old. Age-7-richer tolerates
  more texture than age-4-simple but the budget is not
  infinite.
- **Show moral / emotional resolution through action, not a
  direct lesson.** No aphorisms about patience / kindness /
  friendship. The PD Claude capture did this well; the PA
  Claude capture drifted slightly with its final dog-line. The
  rule needs to bind in both directions.
- **Require concrete physical choices.** Already a rule in both
  capture prompts; keep it. No opinion polls, no emotion
  questions, no metacognition.
- **Keep fake-Armenian prevention.** The structural rule wins
  more than the exemplar list. Future v3.3 OpenAI-side: shift
  from "list bad tokens" to "name the canonical form before you
  write the participle."
- **Add native-ear semantic sanity check.** Either as a rule in
  the system prompt ("if you cannot name the verb in 3rd-person
  past form, choose a simpler verb"), or as a reviewer-side
  checklist that must clear before "say it aloud" cells can be
  filled.

---

## 7. Next safe steps

Strict order — do not parallelize without explicit GO at each
step.

1. **(DONE)** PE controlled-scenario capture prompt created and
   committed (`70df2dd`):
   `claude-app-home-play-controlled-capture-prompt-20260510.md`.
   Child-natural home / family / play scenario, A → B fixed
   path, 9-label format, anti-magical + anti-poetic register
   rules. PE per the comparison plan § 4.
2. **(DONE)** PE Claude.app capture recorded and committed
   (`8c944e5`):
   `claude-app-home-play-controlled-capture-result-20260510.md`.
   First-pass operator rubric YES on say-aloud; **native
   Armenian review still pending.**
3. **Create a native Armenian review checklist.** Now the
   highest-priority next step. Lifted from the per-file
   weakness lists across the four controlled captures so far
   (animal anatomy, verb-noun semantic fit, register, schwa
   drops, named-coinage scan, opener pattern, choice-as-
   physical-action, plus the new Home/Play story-logic and
   everyday-vocabulary checks). A short reusable file under
   `tools/StoryModelBakeoff/evaluations/` that future capture-
   result evaluator notes import by reference. Lets the next
   reviewer (native speaker) skip rediscovering the checklist
   from scratch.
4. **Compare against OpenAI on PE only if needed and only with
   `--max-prompts 1`.** Now deprioritized vs the native review.
   If the native review of the PE Claude capture is decisively
   YES or decisively NO, one paid OpenAI mp1 PE run could close
   the matrix row. If the review is mixed, the paid run is not
   yet warranted — design first, spend later.
5. **Only after enough evidence, design a small production
   integration plan — document only, no code.** Earliest
   possible scope of the document: a Claude-API-based adapter
   behind a feature flag, with parser-adaptation for the Claude
   tail-block format, with all scenarios in the matrix
   (PA + PD + PE + a Calm-mode capture) reviewed by a native
   speaker. The integration design is a *document*; no code
   change before this design exists; no code change after this
   design exists without a second explicit GO.

---

## 8. What not to do yet

- **No provider switch.**
- **No ChatService change** — file untouched, behavior
  unchanged, OpenAI remains the runtime provider.
- **No Claude API runtime wiring.** The bake-off Claude live
  path is for *bake-off use*, not for ChatService. Wiring it
  into ChatService is a separate slice with its own gates.
- **No large paid matrix.** No `--max-prompts 2` runs, no
  cross-provider grid expansion, no auto-rerun. Each paid call
  needs an explicit GO + a question the run is designed to
  answer.
- **No production prompt change.** Production system-prompt sha
  `54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
  is unchanged and stays unchanged until the matrix supports a
  decision.
- **No speech / TTS / STT focus.** The story-brain question
  comes first; spoken-output handling layers on later. Adding
  TTS work now would couple two large unknowns.

---

## 9. No secrets included

This file contains no API key, no token, no bearer credential,
no parent JWT, no device API key, no private endpoint, no
Anthropic account identifier, no OpenAI account identifier, no
Claude.app session identifier.

---

## 10. Scope guard

No production / runtime files were touched by this summary:
`ChatService`, backend code, frontend, `appsettings*.json`,
`*.csproj`, tests, seed bank, name bank, story-plan generator,
validator, runtime system prompts (production sha
`54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
unchanged), speech / TTS / STT — all unchanged. No paid API call
was made; no backend was started; no provider configuration was
touched; Claude API was not used. The only artifact is this
markdown under `tools/StoryModelBakeoff/evaluations/`.

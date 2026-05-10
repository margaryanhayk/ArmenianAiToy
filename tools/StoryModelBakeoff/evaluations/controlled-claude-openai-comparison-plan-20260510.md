# Controlled Claude-vs-OpenAI story-brain comparison plan — 2026-05-10

**Status:** design / evidence-planning only. No code change, no
paid API call, no production change, no ChatService touch, no
provider switch, no runtime config change implied or authorized
by this document.

**Filename date** uses local Yerevan `2026-05-10`.

---

## 1. Purpose

- **Decide whether Claude is actually better as Areg's story-brain**,
  *not* based on a single manual capture. The Claude.app sample at
  `claude-app-manual-sample-aregb-rubric-20260510.md` looked strong,
  but one beautiful turn is consistent with both "Claude is the
  right choice for the toy" and "any tier-1 model looks great when
  the consumer app does the heavy lifting."
- **Keep production unchanged** until the evidence is strong enough
  to act on — at minimum a multi-scenario, multi-turn,
  native-reviewed comparison under matched conditions.
- **Compare across the load-bearing axes** that matter for Areg
  specifically: story quality, Armenian naturalness, continuation
  coherence, and parser-readiness for the production tail-block
  format. A model that writes beautiful Armenian but cannot emit
  the `---\nCHOICE_A:...\nCHOICE_B:...` structural form is not a
  drop-in replacement.

This plan does **not** make the decision. It defines the data we
need before the decision is even possible.

---

## 2. Current evidence summary

| Source | What we have | What it tells us |
|---|---|---|
| **Claude.app manual sample** (`claude-app-manual-sample-aregb-rubric-20260510.md`, commit `16537e5`) | One scenario, two turns, captured through Claude consumer app under Anthropic subscription; Claude's own default system prompt and decoding | Strong natural Armenian, fairy-tale mood, warmth, continuation coherence (golden leaf carries from turn 1 to turn 2 as plot anchor). Borderline literary register; choice format is `Հիմա ի՞նչ անի…` + emoji — not parser-ready. |
| **Earlier Claude.app sample** (`claude-manual-pnjik-golden-leaf-20260501.eval.md`) | Different scenario, also app-only, also single capture | Confirms the consumer app reaches a high quality ceiling on Armenian fairy-tale prose. Same caveats: app ≠ API, single sample. |
| **Earlier Gemini.app sample** (`gemini-manual-mlavik-sunbeam-20260501.eval.md`) | Different scenario, app-only | Warm and safe but simpler / more moralizing. App-only baseline of the third tier-1 candidate. |
| **OpenAI v3.2.3 mp1 Plan A** (`openai-v3-2-3-smoke-mp1-20260510.md`, commit `e73975b`) | Paid API call, Areg system prompt + Areg scenarios, 1 scenario × 3 turns, gpt-4o | Engineering PASS (3/3 calls clean). Structural envelope strongest of any v3.2-era PA run: T3 mid-band ~90w, all turns in target word-bands for the first time. No English / meta leakage. Safety PASS. **But:** four borderline / coined Armenian tokens across the 3 turns (`բոցերում`, `փայլացնում`, `ցուցանի`, `անթել`) plus a mid-paragraph `Մի օր,` opener-rule slip in T1 sentence 6. "Should Areg say this aloud?" = **NO, borderline.** |

**Synthesis:**
- Claude looks better on Armenian *texture* in the one direct sample
  available — but only on the consumer app, only on one scenario,
  and only by app-vs-API comparison (which is unfair).
- OpenAI is better on parser-readiness, prompt obedience, and
  cost-stable engineering — but morphology fabrications keep
  slipping through round after round of named-exemplar tightening
  (v3.2 → v3.2.1 → v3.2.2 → v3.2.3). The shape of the problem
  appears prompt-tunable only up to a ceiling.
- **Provider decision is not ready.** A controlled, same-scenario,
  multi-turn comparison with a native Armenian reviewer is the
  blocker.

---

## 3. Comparison matrix

| Run ID | Provider | Capture method | Prompt version | Scenarios | Turns | Parser-ready? | Native-review verdict | Status |
|---|---|---|---|---|---|---|---|---|
| OAI-v3.2.3-mp1-PA | OpenAI gpt-4o | API (`StoryModelBakeoff`) | v3.2.3 | PA | 3 | YES (production tail-block) | NO / borderline | **DONE** (`e73975b`) |
| OAI-v3.2.3-mp1-PD | OpenAI gpt-4o | API (`StoryModelBakeoff`) | v3.2.3 | PD | 3 | YES | TBD | **PENDING** — only after rate-limit / TPM strategy resolved; do NOT auto-run |
| OAI-v3.2.3-PE-PF-PG | OpenAI gpt-4o | API (`StoryModelBakeoff`) | v3.2.3 | PE / PF / PG (new scenarios — see § 4) | 3 each | YES | TBD | **NEEDS SCENARIO DESIGN** before run; out of scope for this plan |
| CLA-app-PA-controlled | Claude consumer app | manual (browser) | Claude default prompt; *paste of Areg scenario block* (same JSON the runner pastes) | PA | 3 | NO (emoji / Հիմա ի՞նչ անի form) | TBD | **NEXT STEP** (recommended in § 9) |
| CLA-app-PD-controlled | Claude consumer app | manual (browser) | Claude default + Areg scenario | PD | 3 | NO | TBD | **AFTER PA controlled capture** |
| CLA-app-PE-PF-PG | Claude consumer app | manual (browser) | Claude default + Areg scenario | PE / PF / PG | 3 each | NO | TBD | **AFTER PA + PD captured and reviewed** |
| CLA-API-PA | Claude API (Anthropic SDK) | API runner | Areg v3.2.3 *adapted* if needed for parser drift | PA | 3 | TBD | TBD | **BLOCKED on Anthropic API key** + adapter audit |
| CLA-API-PD | Claude API | API runner | Areg v3.2.3 *adapted* | PD | 3 | TBD | TBD | **BLOCKED** |

For each entry, also track in the run's evidence file:
- scenario id (PA / PD / …)
- prompt sha256 (bake-off, scenarios, production)
- capture timestamp (UTC + Yerevan local)
- turn-by-turn raw output preserved (in `bin/Debug/.../review.md`
  for API runs, in the evaluator markdown for manual app captures)
- whether the **output structural format is parser-compatible** with
  `TailBlockParser.cs` (`---\nCHOICE_A:...\nCHOICE_B:...`); yes/no
  with the offending pattern named
- whether a **native Armenian reviewer** would allow Areg to say it
  aloud unedited; yes/no with the load-bearing reason

The native-review row is the single most important cell in this
matrix. Until at least four entries (e.g. OAI PA, CLA-app PA, OAI PD,
CLA-app PD) all have it filled in, no provider decision is supportable.

---

## 4. Required scenarios

Use **controlled same-scenario** tests across providers. Two
scenarios already exist; three more need design before they can be
run. Scenario design is a separate slice — this plan names them but
does not author them.

| ID | Description | Status | Why this scenario |
|---|---|---|---|
| **PA** (`v3-1-plan-a-age-4-simple-17`) | Warm forest / animal helper (dog hero, stork in trouble) / magical object (dew-drop leaf). age-4-simple band. | **Exists** at `tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json` | Canonical "warm fairy-tale forest" baseline. Hard-rule envelope (length, choices, closure) is best-tested here. |
| **PD** (existing Plan D) | Harder scenario; previously exposed PD T1 schwa-stem failure (`կամրջի տակով`) and PD T3 fabrication cluster (v3.2.1 mp2). age-7-richer band. | **Exists** at same JSON | Harder scenario that has historically broken Armenian morphology. Critical Claude-vs-OpenAI test: *does Claude still produce natural Armenian on the scenario where OpenAI struggles most?* |
| **PE** (new) | Child-natural simple home / family / play setting. No magical object, no quest — just a small everyday situation a 4–7-year-old recognizes (e.g. lost toy, helping a sibling, learning to share). | **TO DESIGN** | Tests the warmth-and-character axis without leaning on fairy-tale tropes. A model that writes beautiful enchanted-forest prose may struggle with kitchen-table Armenian. |
| **PF** (new) | Calm / bedtime-like fairy-tale scenario. Soft, slow, close. No choice block (Calm-mode shape per `MODES.md`); single-turn output. | **TO DESIGN** | Tests the Calm-mode register, which is the safety-floor identity for the toy. *Different* shape from Story-mode; may need a reduced scenario JSON without `choiceA` / `choiceB`. |
| **PG** (new) | Curiosity-window + Story hybrid. Child asks one real-world "why" question that the storyteller answers in one sentence and then folds back into a short story. | **TO DESIGN** | Tests mode-blend behavior — the place where prompt obedience meets natural conversational pivot. Both providers will be probed for this in Phase 2. |

**PE / PF / PG must be designed and reviewed before they are run.**
Authoring them is out of scope for this plan; this plan only names
them as required-for-decision. A future scenario-design slice
authors the JSON.

For comparability:
- **Same scenario JSON across providers per ID.** OpenAI runs paste
  the scenario block via the existing runner. Manual Claude.app
  captures paste the *same scenario JSON* as the user message.
- **Fixed choice path per scenario.** Default: choose Ա on T1, Բ on
  T2, T3 ends. (PA and PD already use this convention.) PF (Calm)
  has no choices.
- **No mid-run coaching.** No "make it shorter," no "use simpler
  words," no "rewrite that line." If the model produces broken
  output, that is the data.

---

## 5. Rubric

Use the Areg rubric (matches the existing eval files at
`tools/StoryModelBakeoff/evaluations/` and the manual-sample evals):

| Dimension | Scale |
|---|---|
| Armenian naturalness | **1–5** |
| Eastern Armenian correctness | **1–5** |
| Fairy-tale feeling | **1–5** |
| Warmth for age 4–7 | **1–5** |
| Length / pacing | **1–5** |
| Choice quality | **1–5** |
| Continuation coherence | **1–5** |
| Fake Armenian / morphology | **pass / fail** (any single coined or non-standard token = fail; record the offending token verbatim) |
| Safety / age appropriateness | **pass / fail** |
| "Would I let Areg say this aloud?" | **yes / no** (with one-line load-bearing reason) |

Scoring discipline:
- **Native Armenian reviewer scores the morphology pass/fail and the
  "say it aloud" cell.** Non-native scoring is acceptable for the
  1–5 axes only.
- **Pass/fail axes outweigh 1–5 axes.** A 5/5 on naturalness with
  morphology FAIL still means "no" on "say it aloud." Areg cannot
  ship a story containing a single coined Armenian word.

---

## 6. Decision thresholds

Conservative — biased toward "do not switch" — because the cost of
a wrong runtime switch (a child hearing fake Armenian, or a parser
break that drops choice blocks) is far higher than the cost of
waiting for more evidence.

A runtime provider switch from OpenAI to Claude **requires all** of:

1. **Multi-scenario win.** Claude beats OpenAI v3.2.3 on the
   "Would I let Areg say this aloud?" cell on at least **3 of 5**
   scenarios (PA, PD, PE, PF, PG), with PA and PD specifically
   among the wins. A win on warm-forest only is not enough.
2. **Parser compatibility, or a safe adaptation plan.** Claude
   output is either parser-compatible with the production
   `TailBlockParser.cs` shape, or a parser-adaptation slice has
   been designed (with tests) and the adaptation is judged simpler
   than the morphology problem we are trying to solve. App captures
   that emit emoji-prefixed `Հիմա ի՞նչ անի…` are NOT
   parser-compatible.
3. **Native Armenian review passes.** A native Eastern Armenian
   speaker has reviewed at least one full conversation per
   scenario and agreed Areg can say it aloud unedited.
4. **API-level evidence, not app-only.** If we intend to run Claude
   *via API* in production, then Claude *API* (not Claude.app)
   output must be the basis of the decision. App quality is the
   ceiling; API + Areg system prompt is what we'd actually ship.
5. **OpenAI hasn't fixed the morphology gap meanwhile.** If a
   v3.3 structural rule (e.g. "only verbs / participles whose
   3rd-person past form you can name aloud first") closes the
   coined-stem leak on a paid PA + PD run, the Claude switch
   becomes far less attractive on cost / engineering grounds.

A "stay on OpenAI" outcome is acceptable **only if** the morphology
pass/fail cell reaches near-zero failures (≤1 borderline token per
3-turn scenario, no hard fabrications) on PA + PD across two
consecutive paid runs. Below that bar, "stay on OpenAI" is
provisional, not a decision.

If neither side clears its bar, the answer is "neither, keep
investigating" — not "pick the less bad one."

---

## 7. Capture protocol

### 7.1 Manual Claude.app capture protocol

- **Fresh conversation per scenario.** Open a new chat in the
  Claude consumer app. Do not reuse a chat across scenarios; do
  not reuse a chat across turns of different scenarios.
- **Paste the exact same scenario block** that the OpenAI runner
  pastes — i.e. the `TURN_INDEX: 1 / SELECTED_CHOICE / MAX_TURNS /
  PLACE_STEM / TARGET_WORDS / STORY PLAN: { … } / ԸՆՏՐՈՒԹՅՈՒՆՆԵՐ`
  block from `bakeoff-prompts-v3-1.json`. The system-prompt drift
  is unavoidable (Claude.app uses its own system prompt) — that is
  the *intentional* drift we are measuring. Scenario inputs must
  match byte-for-byte.
- **Capture initial story + at least two continuations.** PA / PD /
  PE / PG → 3 turns each. PF (Calm) → 1 turn (single-turn shape).
- **Always choose a fixed choice path** per scenario for
  comparability with the OpenAI runs. Default: Ա on T1, Բ on T2.
  Document any deviation in the evidence file.
- **Save raw output exactly.** Copy the full Armenian response
  including punctuation, emoji, line breaks, and any closing
  formatting Claude adds. Do not edit, do not "clean up," do not
  re-format.
- **Do not "coach" the model mid-run.** No follow-up clarifications,
  no "make it shorter," no "use simpler Armenian." If output is
  broken, that is the data. Re-running a fresh chat is fine; mid-
  run coaching is not.
- **Mark the capture as `app/manual`, not API.** Every evaluator
  markdown for an app capture must say so explicitly in §1
  ("Source: Claude consumer app, NOT API"), and the matrix entry
  in this plan is updated to point at it.
- **No identifiers.** Do not paste anything that would identify the
  Anthropic account / subscription / IP / time-of-day correlation
  in the evidence file.

### 7.2 OpenAI API capture protocol

- **Use the existing `StoryModelBakeoff` runner** at
  `tools/StoryModelBakeoff/Program.cs`. No new tooling for this
  plan.
- **Start with `--max-prompts 1` only.** Plan A first. Cost
  discipline; matches every prior v3.2-era run.
- **Avoid `--max-prompts 2` until a rate-limit / TPM strategy is
  resolved.** v3.2.2 mp2 already produced the cost-shape evidence
  we need; mp2 is now a "wait for explicit GO + TPM plan" gate.
  Do not auto-rerun.
- **Save generated JSON / markdown evidence using existing repo
  naming style:** `openai-v{version}-smoke-mp{n}-{YYYYMMDD}.md`
  under `tools/StoryModelBakeoff/evaluations/`. Raw run artifacts
  stay in `bin/Debug/.../results/{stamp}/` (gitignored). Prompt
  shas, scenarios sha, production sha, run identity, and the
  per-axis rubric all go into the evaluator markdown — same shape
  as `openai-v3-2-3-smoke-mp1-20260510.md`.
- **Same secret-handling discipline.** `OPENAI_API_KEY` loaded
  inline from `dotnet user-secrets` for `backend/src/ArmenianAiToy.Api`;
  key length printed, key value never written to file or stdout.

### 7.3 Future Claude API capture protocol (when Anthropic key lands)

- Out of scope for this plan; see § 9. The protocol shape will
  mirror § 7.2 but consume the Claude path of the bakeoff runner
  added at commit `0f362f7` (per the prior session's run brief).
  A separate plan slice authors that protocol once a key is
  provisioned and the parser-format question is resolved.

---

## 8. Risks

- **Claude.app may differ from Claude API.** App quality is upper-
  bound; API + Areg system prompt is what we'd ship. A switch
  decided on app evidence alone is unsupportable.
- **Claude may be better stylistically but worse at strict
  formatting.** The Claude.app sample's `Հիմա ի՞նչ անի…` + emoji
  choice block is a parser miss against `TailBlockParser.cs`'s
  `---\nCHOICE_A:...\nCHOICE_B:...`. We may end up trading a
  morphology problem for a structural-format problem.
- **OpenAI may be more parser-stable but weaker on Armenian
  texture.** v3.2.3 mp1 confirms this shape: hard rules pass, soft
  morphology slips. The named-exemplar tightening pattern hits a
  ceiling — every round catches the named family and the next
  family appears.
- **One beautiful sample is not enough.** Variance between Claude
  app captures (or between gpt-4o sampling-temperature draws) can
  produce a cherry-picked win that does not generalize. The 5-
  scenario, multi-turn matrix exists precisely because single-
  sample data is misleading.
- **Native Armenian review is still required.** A non-native
  reviewer can score length, structural rules, and English
  leakage, but cannot reliably score Eastern Armenian
  morphology, register, or "would I let Areg say this aloud."
  No decision without native review.
- **Mode coverage is incomplete.** PE / PF / PG do not exist as
  scenarios yet. Decisions made on PA + PD only generalize to
  Story-mode warm-forest content. Calm-mode (PF) and
  Curiosity-Story-hybrid (PG) are real shapes the toy must
  produce; if Claude is great at PA but weaker at PF, the
  decision changes.
- **Cost vs evidence tradeoff.** Each paid run is bounded but not
  free. The matrix has 5 scenarios × 2 providers × 3 turns ≈ 30
  paid turns just for the OpenAI-API + Claude-API rows. Phase
  this; do not run the full matrix in one slice.

---

## 9. Recommended next action

**Strict order, do not parallelize without explicit GO at each step.**

1. **Create this plan only.** This document. Free; preserves the
   matrix shape and decision thresholds past the session boundary.
   Awaits review before commit.
2. **Run one controlled manual Claude.app capture for Plan A**
   following § 7.1 exactly. Same scenario JSON the OpenAI runner
   uses for PA. Three turns. Fixed Ա-Բ choice path. Save as
   `tools/StoryModelBakeoff/evaluations/claude-app-controlled-pa-20260510.md`
   (or next-day stamp), with the matrix row updated to point at it.
   This is a free action (the operator captures by hand in the
   Claude.app browser session).
3. **Native Armenian review of both PA captures** — the existing
   `openai-v3-2-3-smoke-mp1-20260510.md` and the new
   `claude-app-controlled-pa-20260510.md`. Fill in the morphology
   pass/fail and "say aloud" cells for both. This unblocks the
   first row of the comparison matrix.
4. **Stop at row 1.** Do not run more paid OpenAI calls and do not
   request an Anthropic API key until the PA review is in. The
   review may dissolve the question — if Claude.app PA is also
   morphology-FAIL, the "Claude is naturally better at Armenian"
   premise is weakened, and the structural v3.3 OpenAI rule
   becomes the more attractive path. If Claude.app PA is
   morphology-PASS, the next slice is PD (still manual app, not
   API).
5. **Do not touch production.** No ChatService change, no provider
   config change, no system-prompt change, no NuGet add, no new
   adapter, no parser slice, until the matrix has at least PA + PD
   filled across both providers and a native review on each.
6. **No paid Claude API call** until: (a) PA + PD manual app
   captures are reviewed, (b) a parser-compatibility plan exists
   for Claude API output, (c) an Anthropic API key is provisioned
   on the deploy / secrets path, (d) explicit GO from Hayk for
   the spend.

---

## Files / references

- This plan: `tools/StoryModelBakeoff/evaluations/controlled-claude-openai-comparison-plan-20260510.md`
- OpenAI v3.2.3 PA mp1 evidence: `tools/StoryModelBakeoff/evaluations/openai-v3-2-3-smoke-mp1-20260510.md` (commit `e73975b`)
- Claude.app rubric eval: `tools/StoryModelBakeoff/evaluations/claude-app-manual-sample-aregb-rubric-20260510.md` (commit `16537e5`)
- Earlier Claude.app eval: `tools/StoryModelBakeoff/evaluations/claude-manual-pnjik-golden-leaf-20260501.eval.md`
- Earlier Gemini.app eval: `tools/StoryModelBakeoff/evaluations/gemini-manual-mlavik-sunbeam-20260501.eval.md`
- Bake-off scenarios: `tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json`
- Bake-off system prompt (v3.2.3): `tools/StoryModelBakeoff/system-prompt-v3-2.txt` (commit `919dee5`)
- Production tail-block parser: `backend/src/ArmenianAiToy.Application/Helpers/TailBlockParser.cs`
- Production runtime sha (unchanged): `54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`

## Scope guard

This document changes no production / runtime files: `ChatService`,
backend code, frontend, `appsettings*.json`, `*.csproj`, tests,
seed bank, name bank, story-plan generator, validator, runtime
system prompts, speech / TTS / STT — all unchanged. No paid API
call was made authoring this plan; no backend was started; no
provider configuration was touched. The only artifact is this
markdown under `tools/StoryModelBakeoff/evaluations/`.

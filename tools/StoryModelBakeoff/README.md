# StoryModelBakeoff

Local-only research tool for comparing **Armenian story-generation
quality** across LLM providers (OpenAI, Anthropic Claude, Google
Gemini, plus a reserved slot for a future Armenian-local provider).

This is **research tooling, not production runtime**. It does not
replace `ChatService`, does not run inside the backend, is not wired
into `BenchmarkAll`, and is not part of `backend/ArmenianAiToy.slnx`.
It is intended to give Hayk decision support for "which model would
make Areg sound most natural in Armenian" — based on side-by-side
output that the human ear can score offline.

## Slice status

- **F1.1** — scaffold + dry-run planner. Shipped.
- **F1.2** — live Claude execution. Shipped (this slice).
- **F1.3+** — live OpenAI / Gemini execution; multi-provider review
  layout. Deferred.

The first live run on a fresh deployment should be **operator-
approved**: a small `--max-prompts 1` smoke is the right starting
point before any full-set live run.

## What's in this folder

| File | Purpose |
|---|---|
| `Program.cs` | CLI: dry-run planner, drift check, provider/model resolution, **F1.2 live Claude execution + result writers**. |
| `bakeoff-prompts.json` | The 12 Armenian story scenarios (multi-turn where relevant). |
| `system-prompt.txt` | Frozen copy of the production `SystemPrompt` (from `backend/src/ArmenianAiToy.Api/appsettings.json`) with a `# Source:` header. The loader strips that header before hashing. |
| `StoryModelBakeoff.csproj` | net10.0 console exe, no PackageReferences, no ProjectReferences. |
| `story-seed-bank.v1.json` | Hand-edited Armenian-flavored seed bank for future Story Director experiments (Phase 1 per `STORY_DIRECTOR_ARCHITECTURE.md`). NOT loaded by ChatService. Now carries: **content palettes** (animals, places, magicalObjects, smallProblems, sensoryDetails, gentleActions, choiceVerbs, traditionalFormulas), **story-control attributes** (characterTraits, characterGoals, storyMoods, relationshipTypes, conflictTypes, resolutionStyles, choiceTypes), **age tone profiles** (object array: ageToneProfiles), and **guardrail arrays** (rareOrRequestedOnlyAnimals, hardAvoidCreatures, avoidPatterns, forbiddenTonePatterns). |
| `validate-seed-bank.js` | Node.js validator for `story-seed-bank.v1.json`. Pure-stdlib; checks shape, counts, duplicates, deprecated keys, and known-bad values. |
| `generate-story-plan.js` | Node.js Story Plan generator (Phase 2 of `STORY_DIRECTOR_ARCHITECTURE.md`). Pure-stdlib; reads the seed bank and prints pretty JSON plans to stdout. Supports `--count N` and `--seed N`. |
| `validate-story-plan.js` | Node.js Plan Gate validator (Phase 3 first half). Pure-stdlib; reads plan JSON from stdin or a file path, checks the 17-field shape, seed-bank membership of every value, hardAvoidCreatures / forbiddenTonePatterns leaks, banned choice phrases, and choice grounding + type consistency. Reports per-plan PASS/FAIL with errors and lightweight warnings. Exit 0 on PASS, 1 on FAIL. |
| `results/` | Per-run Markdown + JSON output (created on first live run). **Gitignored** (`.gitignore` excludes `tools/StoryModelBakeoff/results/`). |

## Seed bank validation

Run the validator before opening a PR that touches
`story-seed-bank.v1.json`:

```
node tools/StoryModelBakeoff/validate-seed-bank.js
```

The validator is pure Node.js (no dependencies), reads the seed
bank next to itself, and checks:

- top-level shape (`version`, `language`, `purpose`, `palettes`);
- required palette arrays and their minimum counts;
- optional `traditionalFormulas` object shape if present;
- every value is a non-empty string;
- no duplicates inside any array;
- the deprecated `palettes.avoidAnimals` key is gone (split into
  `rareOrRequestedOnlyAnimals` + `hardAvoidCreatures`);
- a small list of known-bad strings does not appear anywhere.

Exit 0 on PASS, non-zero on FAIL with all errors listed before
exit. The script never modifies the seed bank.

## Story plan validation

Validate generated plan JSON against the seed bank's vocabulary
and the Plan Gate's structural rules. Pure Node.js, no
dependencies; reads from stdin or a file path:

```
# Validate freshly generated plans through stdin
node tools/StoryModelBakeoff/generate-story-plan.js --count 10 --seed 123 \
  | node tools/StoryModelBakeoff/validate-story-plan.js

# Validate a saved plans file
node tools/StoryModelBakeoff/validate-story-plan.js \
  tools/StoryModelBakeoff/evaluations/story-plan-generator-review-20260501.plans.json
```

What it checks:

- **Required fields** — all 17 plan fields must exist; the
  string fields must be non-empty; `sensoryDetails` is exactly
  two distinct entries; `ageToneProfile` is a full object with
  the five required string fields.
- **Seed-bank membership** — every value must appear in its
  source palette: `hero` / `friendOrGuide` ∈ `palettes.animals`,
  `heroTrait` ∈ `palettes.characterTraits`, `mood` ∈
  `palettes.storyMoods`, `choiceAType` / `choiceBType` ∈
  `palettes.choiceTypes`, `ageToneProfile.label` ∈
  `palettes.ageToneProfiles[].label`, etc.
- **Hero / friend rules** — must be different; neither may be
  in `rareOrRequestedOnlyAnimals` or `hardAvoidCreatures`.
- **Guardrail leaks** — no value in `palettes.hardAvoidCreatures`
  or `palettes.forbiddenTonePatterns` may appear as a substring
  anywhere in the plan; the historical known-bad cleanup strings
  also stay out.
- **Choice grounding** — each choice must reference either the
  plan's `place` or its `magicalObject` as a substring.
- **Choice type consistency** — a place-grounded choice must
  carry `choiceType = "գնալ դեպի վայր"`; an object-grounded
  choice must NOT carry that type.
- **Banned choice phrases** — exact rejects (`շարունակել`,
  `գնալ առաջ`, `չգիտեմ`, `այո`, `ոչ`) and substring rejects
  (`բացել ճյուղ`, `դիտել `, `թակել լճակ`, `շոյել քարայր`).

Lightweight warnings (do not affect PASS/FAIL):

- `ageToneProfile.label === "age-7-richer"` — story may run long
  for younger ages; verify against the intended target.
- `մոտեցնել X-ը լույսին` template fired on an object that
  doesn't match the inspection-natural keyword set
  (`isShiny` / `isOpenable` / `isSoundCapable`).

Exit 0 on no errors, 1 on any error. The script never modifies
the input.

> **Backward-compat note.** Plans generated before the 17-field
> shape (e.g. the committed
> `evaluations/story-plan-generator-review-20260501.plans.json`)
> will FAIL validation against the new schema because they lack
> the post-Phase-2 attributes. That's expected; do not force
> backward compatibility — re-generate with the current
> `generate-story-plan.js` if you need a validating sample.

## Story plan generator

Quick experiment to feel out whether the seed bank can produce
usable Areg-style story material *before* any model call. Reads
the seed bank and prints pretty JSON plans to stdout:

```
node tools/StoryModelBakeoff/generate-story-plan.js
node tools/StoryModelBakeoff/generate-story-plan.js --count 3
node tools/StoryModelBakeoff/generate-story-plan.js --count 5 --seed 123
node tools/StoryModelBakeoff/generate-story-plan.js \
  --count 3 --seed 123 --age-profile age-5-balanced
```

Defaults: one plan, non-deterministic (`Math.random`). With
`--seed N`, the script uses a small LCG so the same seed reproduces
the same plan(s) — useful for reviewing a specific output later.

`--age-profile <label>` pins the `ageToneProfile` field on every
generated plan to a specific entry from
`palettes.ageToneProfiles` (`age-4-simple`, `age-5-balanced`,
`age-6-story-rich`, `age-7-richer`). Without the flag, each plan
draws an `ageToneProfile` independently. An unknown label exits
non-zero with the available labels listed.

> **Generator now consumes story-control attributes.** Each
> emitted plan carries the original content fields plus
> `heroTrait`, `relationship`, `mood`, `conflictType`, `goal`,
> `resolutionStyle`, `ageToneProfile` (the full object), and
> `choiceAType` / `choiceBType` tags drawn from
> `palettes.choiceTypes`. The `forbiddenTonePatterns` array is
> guardrail data for the writer prompt and is intentionally NOT
> consumed by the generator.
>
> The new fields are *generated*, not yet *enforced*. The Plan
> Gate (Phase 3) is the slice that will start rejecting plans
> whose attribute combinations don't fit the seed bank
> constraints.

Generation rules:

- `hero` and `friendOrGuide` are drawn from `palettes.animals`,
  always different from each other, and explicitly excluded from
  `rareOrRequestedOnlyAnimals` and `hardAvoidCreatures` even if
  they sneak into `animals` by hand-edit.
- `place` / `magicalObject` / `smallProblem` from their matching
  palettes.
- `sensoryDetails` is two distinct entries from `sensoryDetails`.
- Story-control attributes — one independent draw per plan, one
  field per palette: `heroTrait` ← `characterTraits`,
  `relationship` ← `relationshipTypes`, `mood` ← `storyMoods`,
  `conflictType` ← `conflictTypes`, `goal` ← `characterGoals`,
  `resolutionStyle` ← `resolutionStyles`, `ageToneProfile` ←
  `ageToneProfiles` (or pinned by `--age-profile`).
- `choiceAType` / `choiceBType` are tags from `palettes.choiceTypes`
  attached to the chosen choice templates (e.g. place templates
  carry `"գնալ դեպի վայր"`; the `բացել` object template carries
  `"բացել փոքրիկ առարկան"`; the `լսել` object template carries
  `"լսել ձայնը"`; etc.). Tags map to existing seed-bank entries
  exactly — no synthesised choiceType strings.
- `choiceA` / `choiceB` are concrete grounded actions: one
  references the plan's `place`, the other references the plan's
  `magicalObject`. Order between them is randomised per plan.

  **Place templates** all use `դեպի` (toward) so the place phrase
  stays in the nominative — no irregular-stem hazard:
  - *"գնալ դեպի <place>"*, *"քայլել դեպի <place>"* — universal.
  - *"իջնել դեպի <place>"* — only when the place reads as a
    water / lower spot (`լճակ`, `աղբյուր`, `առվակ`, `ափ`, ...).
  - *"բարձրանալ դեպի <place>"* — only when the place reads as a
    high spot (`բլուր`, `սար`, `ժայռ`, `կատար`, `ծառ`, `ընկուզենի`,
    `աշտարակ`, `ճյուղ`, ...).

  **Object templates** use the Armenian definite suffix
  («ը»/«ն») where the action requires accusative-shape:
  - *"վերցնել <obj>"*, *"տանել <obj>ը ընկերոջ մոտ"*,
    *"պահել <obj>ը ափի մեջ"*, *"մոտեցնել <obj>ը լույսին"* —
    universal, work on any small magical object.
  - *"բացել <obj>ը"* — only when the object is openable
    (`տուփ`, `սրվակ`, `կուժ`, `տոպրակ`, `սփռոց`).
  - *"հետևել <obj>ի փայլին"* — only when the object reads as
    shiny (`ոսկ-`, `արծաթ-`, `լուսավոր`, `փայլող`, `մարգարիտ`,
    `աստղ`, `լույս`, `շող`) AND its phrase ends in a consonant
    (so the genitive `-ի` doesn't double).
  - *"լսել՝ արդյոք <obj>ը ձայն ունի"* — only when the object
    reads as sound-capable (`զանգակ`, `սանր`, `սրինգ`, `սուլիչ`,
    `խեցի`, `փետուր`, `կաթիլ`, or carries `երգող`/`խոսող`/`հնչող`).

This is research tooling. **No model is called.** No production
runtime is touched.

## Running

### Dry-run (default — no network)

```
dotnet run --project tools/StoryModelBakeoff
dotnet run --project tools/StoryModelBakeoff -- --provider claude --max-prompts 3
dotnet run --project tools/StoryModelBakeoff -- --help
```

The default invocation prints a dry-run plan: provider matrix
(live / skipped per API-key availability), resolved model per
provider, scenario / turn / call counts, the bake-off-prompt
SHA-256, the production-prompt SHA-256, and whether drift was
detected. **No network is touched.**

### Live (F1.2 — Claude only)

Live execution requires every one of:

1. `--run`
2. `--provider claude` (the only live-supported provider in F1.2)
3. `--i-understand-live-cost`
4. Either `--max-prompts N` **or** `--allow-full-set` (XOR — both
   together is rejected)
5. `ANTHROPIC_API_KEY` set in the environment

Examples:

```
# Smallest possible smoke — one scenario only.
dotnet run --project tools/StoryModelBakeoff -- \
  --run --provider claude --i-understand-live-cost --max-prompts 1

# Full 12-scenario run (~14 calls). Single-digit cents on Opus 4.7.
dotnet run --project tools/StoryModelBakeoff -- \
  --run --provider claude --i-understand-live-cost --allow-full-set
```

Behaviour:

- The tool prints a pre-execution plan (provider, model, scenario
  count, total turns/calls, output directory) and a "Ctrl-C now if
  this is unexpected" line BEFORE firing the first request. The
  run starts immediately after that line.
- Each turn fires one POST to `https://api.anthropic.com/v1/messages`
  with the bake-off system prompt and the rolling per-scenario
  conversation history. Multi-turn scenarios (S07, S10) replay
  their turns sequentially and accumulate history.
- One stdout line per turn:
  - success: `[S01 t1/1 claude] ok 4523ms 187out`
  - failure: `[S07 t2/2 claude] FAIL http_500 1213ms`
  - skipped after prior failure: `[S07 t2/2 claude] skipped (prior turn failed)`
- **No retry**, **no temperature override**, **60-second per-call
  timeout**. Generation parameters are Anthropic defaults.
- Failures on a single turn are recorded but do not abort the run.
  Remaining turns of the SAME scenario are marked
  `skipped_due_to_prior_error` (continuation depends on the
  preceding assistant reply, which is missing).
- Ctrl-C honored: the in-flight call is cancelled, partial results
  are flushed, and `runInterruptedUtc` is stamped on
  `summary.json`.

### Result files (live runs only)

A live run creates `tools/StoryModelBakeoff/results/<UTC-stamp>/`
containing three artifacts:

| File | Purpose |
|---|---|
| `results.json` | Machine-readable per-scenario, per-turn detail (full assistant text, latency, token usage, errors). `schemaVersion: 1`. |
| `review.md` | Human-readable review for the operator. One section per scenario, with the manual scoring rubric below filled in by hand. |
| `summary.json` | Aggregate totals — calls attempted/succeeded/failed, total latency, total tokens. `schemaVersion: 1`. |

All three files are written atomically (`.tmp` + rename), so a
Ctrl-C mid-write doesn't leave a half-parsed JSON.

## Provider environment variables

| Variable | Purpose |
|---|---|
| `OPENAI_API_KEY` | OpenAI auth. Provider is "skipped" without it (live deferred). |
| `ANTHROPIC_API_KEY` | **Claude auth — required for F1.2 live runs.** |
| `GEMINI_API_KEY` | Google Gemini auth. Provider is "skipped" without it (live deferred). |
| `AAT_LOCAL_API_KEY` | Reserved for a future Armenian-local provider. No code path today. |

## Model override variables (optional)

| Variable | Default |
|---|---|
| `OPENAI_BAKEOFF_MODEL` | `gpt-4o` |
| `ANTHROPIC_BAKEOFF_MODEL` | `claude-opus-4-7` |
| `GEMINI_BAKEOFF_MODEL` | `gemini-2.5-pro` |

## Manual scoring rubric

The `review.md` for a live run includes this block per scenario, to
be filled in by hand:

- Armenian naturalness — **1–5**
- Eastern Armenian correctness — **1–5**
- Fairy-tale feeling — **1–5**
- Warmth for age 4–7 — **1–5**
- Length / pacing — **1–5**
- Choice quality — **1–5**
- Continuation coherence — **1–5**
- Safety / age appropriateness — **pass / fail**
- "Would I let Areg say this aloud?" — **yes / no**
- Notes — free text

For multi-turn scenarios the rubric is filled in once for the
scenario as a whole; per-turn scoring would be too granular.

## What this tool is not

- **Not a regression benchmark.** That's `StoryBenchmark`.
- **Not a mode-routing test.** That's `ModeBenchmark`.
- **Not a runtime provider switch.** Production still uses OpenAI;
  changing that is HIGH risk and out of scope for F1.
- **Not safety-checked output.** The bake-off bypasses our backend's
  moderation pipeline by design (we measure raw model output).
  Reports land locally and are reviewed only by the operator.
- **Not in CI.** Live runs cost money and require manual approval.

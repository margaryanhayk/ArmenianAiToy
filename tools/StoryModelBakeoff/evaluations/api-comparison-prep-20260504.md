# API comparison preparation — v3.1 Plan A + Plan D head-to-head (2026-05-04)

**Status:** planning / preflight only. **No API calls have been
run.** No production code change. No `ChatService` change. No
runtime prompt change. No provider switch. No API keys required
to read this document. Treat this file as the design + safety
contract for a future API-comparison slice.

This is **slice D** from
[`./story-brain-finalization-20260504.md`](./story-brain-finalization-20260504.md)
§ 8. Slices A (`b7d105e`), B (`f20e473`), and C (validator
regression guard, landed with A) are complete. Slice D is the
**API head-to-head**; slice E (production-integration design)
remains gated on slice D's evidence and is **not** unblocked
by this prep doc alone.

**Companion files:**
- [`./writer-prompt-v3-1-plan-a-capture-20260504.md`](./writer-prompt-v3-1-plan-a-capture-20260504.md) — v3.1 Plan A strict-protocol capture (commit `019177c`).
- [`./writer-prompt-v3-1-plan-d-capture-20260504.md`](./writer-prompt-v3-1-plan-d-capture-20260504.md) — v3.1 Plan D strict-protocol capture (commit `f20e473`).
- [`./writer-prompt-v3-1-hardening-notes-20260504.md`](./writer-prompt-v3-1-hardening-notes-20260504.md) — v3.1 rule set + gate definitions C14 / C15 / C16.
- [`./story-brain-finalization-20260504.md`](./story-brain-finalization-20260504.md) — roadmap (slice D defined here).
- [`../README.md`](../README.md) — bake-off CLI usage, env vars, live-run protocol (Claude already live; OpenAI live deferred).
- [`../Program.cs`](../Program.cs) — bake-off CLI source (live Claude execution, pre-execution plan, Ctrl-C cancellation, results writers — F1.2).

---

## 1. Purpose

Compare **Claude API** and **OpenAI API** for Areg story-brain
quality on the **same validated plans** and the **same v3.1
writer prompt logic** that produced the strict-protocol
Claude.app evidence in commits `019177c` (Plan A) and `f20e473`
(Plan D).

The API run resolves the questions that **Claude.app evidence
alone cannot answer**:

- **Duplicate-sentence-trio artefact** (gate C3) — observed on
  every continuation in v1 / v2 / v3 Claude.app captures;
  absent on v3.1 Claude.app. The strict-protocol Plan D
  capture absence is suggestive but not conclusive — the
  artefact has been UI-side variance historically. **API
  output is the only conclusive answer.**
- **API formatting stability** — does `Ա: ` / `Բ: ` choice
  format hold at byte-level under controllable decoding,
  not just under the consumer-app default? Does the BREAK-
  GLASS byte-for-byte rule (gate C15) survive temperature /
  top_p settings?
- **Cost** — per-call and per-story-session $ estimate so
  the production-integration design doc (slice E) has real
  numbers to budget against.
- **Latency** — per-call wall-clock so the production
  spoken-toy attention budget can model end-to-end
  experience time (current v3.1 prompts are ~5-7K input
  tokens per turn; non-trivial).
- **Decoding control** — temperature / top_p / max_tokens
  /  reasoning settings are operator-controllable in API
  runs but invisible in Claude.app. Does the model behave
  the same when decoding is pinned vs the consumer default?
- **Exact choice preservation** (gate C15 BREAK-GLASS) —
  does Turn 2's byte-for-byte choice copy hold under API
  conditions?
- **No meta-output** (gate C14) — does the anti-meta rule
  hold under API conditions?
- **Closure behavior** (gate C9) — does Turn 3 stay
  choice-block-free under API conditions?

The v3.1 Claude.app evidence is **strong reference / ceiling
evidence** for what these models can produce on the
producer's prompt + plan combination. The API run takes
that ceiling and converts it into **runtime-decision
evidence** — only API conditions are reproducible from
code, comparable across providers, and meaningful for any
future production integration discussion.

---

## 2. Inputs

The API comparison reuses **already-pushed evidence**
verbatim. No new plans, no new writer prompts, no new
hardening rules.

| Input | Source | Pin |
|---|---|---|
| Plan A JSON | committed plan from age-4-simple #17 (used by `019177c`) | `evaluations/generated-plans-age-4-simple-20260501.json` |
| Plan D JSON | freshly-generated post-`b7d105e` (used by `f20e473`) | inlined verbatim in `writer-prompt-v3-1-plan-d-capture-20260504.md` § 2 |
| Writer prompt v3.1 — Turn 1 / Turn 2 / Turn 3 templates | committed prompts | `evaluations/manual-plan-d-v3-1-capture/TURN_*_PROMPT*.txt` for Plan D; equivalent inline blocks in the Plan A capture's § 7 / § 8 / § 9 |
| v3.1 rule set | committed | `evaluations/writer-prompt-v3-1-hardening-notes-20260504.md` § 3 (rules A–E + gates C14 / C15 / C16) |
| Gate definitions (C1–C16) | committed | hardening notes § 4 (C1–C13 inherited from v3, C14–C16 introduced by v3.1) |
| Strict Claude.app reference outputs | committed | Plan A: `019177c` § 7A / § 7B / § 7C; Plan D: `f20e473` § 10A / § 10B / § 10C |

**No edits to any of the above are part of this slice.** The
API runner fetches the prompt text and plan JSON from the
existing files; substitution between turns mirrors the strict-
protocol contract (`{{TURN_1_OUTPUT}}` filled before Turn 2,
both filled before Turn 3).

---

## 3. Models to compare

Final model selection is **deferred to runtime** and recorded
exactly in the result file (per § 8 below). This document
treats both providers symmetrically.

| Provider | Default at run-time | Env var | Pinned at run-time? |
|---|---|---|---|
| Anthropic (Claude API) | `ANTHROPIC_BAKEOFF_MODEL` env var; bake-off CLI default `claude-opus-4-7` per repo `README.md` | **`ANTHROPIC_API_KEY`** | yes — record exact model id used in the result file's metadata block |
| OpenAI (OpenAI API) | `OPENAI_BAKEOFF_MODEL` env var; bake-off CLI default `gpt-4o` per repo `README.md` | **`OPENAI_API_KEY`** | yes — record exact model id used in the result file's metadata block |

**Do NOT hardcode the final model choice in this document.** The
defaults above are the bake-off CLI's defaults for live runs
*as committed today*; they may change before the API
comparison runs. The runner must record the exact model id at
run-time and surface it in the result file's metadata.

**Decoding parameters at run-time:**

- Use each provider's **default decoding parameters** for the
  first comparison pass (no temperature override, no top_p
  override, no max_tokens override beyond what the v3.1
  prompts themselves specify via the "100–140 հայերեն բառ"
  budget guidance).
- A second pass with explicitly-pinned `temperature=0.7,
  top_p=0.95, max_tokens=600` is OPTIONAL and should only run
  if the first pass leaves variance questions unanswered.
- `seed` (where the provider supports it) — set explicitly to
  enable rerun reproducibility. Record the seed in the result
  file's metadata.

---

## 4. Capture matrix

Minimum matrix: **12 API calls** (2 plans × 3 turns × 2
providers).

| Cell | Provider | Plan | Turn | Selected choice into next turn | Notes |
|---|---|---|---|---|---|
| 1 | Claude API | Plan A | Turn 1 | (none) | initial |
| 2 | Claude API | Plan A | Turn 2 | `Ա` (`մոտեցնել ցողի կաթիլներով տերևը լույսին`) | continuation; substitute Turn 1 raw |
| 3 | Claude API | Plan A | Turn 3 | `Բ` (`մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն`) | closure; substitute Turn 1 + Turn 2 raw |
| 4 | OpenAI API | Plan A | Turn 1 | (none) | parallel to cell 1 |
| 5 | OpenAI API | Plan A | Turn 2 | `Ա` | parallel to cell 2 |
| 6 | OpenAI API | Plan A | Turn 3 | `Բ` | parallel to cell 3 |
| 7 | Claude API | Plan D | Turn 1 | (none) | initial |
| 8 | Claude API | Plan D | Turn 2 | `Ա` (`տանել քնած բանալին ընկերոջ մոտ`) | continuation; substitute Turn 1 raw |
| 9 | Claude API | Plan D | Turn 3 | `Բ` (`համբերել ու լսել հին կամուրջի տակ առվակի շշուկը`) | closure; substitute Turn 1 + Turn 2 raw |
| 10 | OpenAI API | Plan D | Turn 1 | (none) | parallel to cell 7 |
| 11 | OpenAI API | Plan D | Turn 2 | `Ա` | parallel to cell 8 |
| 12 | OpenAI API | Plan D | Turn 3 | `Բ` | parallel to cell 9 |

**Optional variance pass:** repeat the entire 12-cell matrix
**once more** (24 calls total) to surface day-of / decode-of
variance. Recommended only if cost budget allows; the variance
pass is informative, not load-bearing — single-pass evidence
suffices for the provider-side comparison if budget is tight.

The Turn-2 and Turn-3 selected choices are **the same as the
strict-protocol Claude.app captures** so the API outputs
remain apples-to-apples comparable to the existing reference
evidence. Different selected paths would conflate the
provider-comparison axis with the choice-path axis.

---

## 5. Metrics

The API run records **the same 17 gates** the Claude.app
strict captures use (from
[`writer-prompt-v3-1-hardening-notes-20260504.md`](./writer-prompt-v3-1-hardening-notes-20260504.md)
§ 4) **plus** API-only operational metrics.

### 5.1 v3.1 gates (C1–C16) — same set as Claude.app captures

Per cell, mark PASS / FAIL on each applicable gate:

| Gate | Applies to | What it enforces |
|---|---|---|
| C1 | Turn 1 | No `Մի անգամ` / `Մի գեղեցիկ օր` / `Շատ վաղուց` opener |
| C2 | All turns | No moralizing / aphorism, especially patience-axis on Plan D Turn 3 |
| C3 | All turns | **No duplicate sentence within turn** (the load-bearing API question — does the artefact reappear under API conditions?) |
| C4 | All turns | Age-appropriate register (age-4-simple for Plan A, age-7-richer for Plan D) |
| C5 | All turns | Plan adherence — every plan atom visible |
| C6 | Turn 1 | Plan choices verbatim (Plan D incl. `հին կամուրջ-ի` hyphen) |
| C7 | Turn 1 | Length budget (Plan A 90–130, Plan D 130–180) |
| C8a | Turn 2 | First sentence performs SELECTED_CHOICE Ա |
| C8c | Turn 2 | Length budget (Plan A 70–110, Plan D 100–140) |
| C9 | Turn 3 (load-bearing) | NO `Ա: ` / `Բ: ` lines anywhere |
| C10 | Turn 3 | First sentence performs SELECTED_CHOICE Բ |
| C11 | Turn 3 | smallProblem resolved within turn |
| C12 | Turn 3 | Ends in natural last sentence or `Վերջ։` |
| C13 | Turn 3 | Length budget (Plan A 70–100, Plan D 100–130) |
| C14 | All turns | No meta-output (`Շարունակեց հեքիաթը...`, `Note:`, `As an AI...`) |
| C15 | Turn 2 | BREAK-GLASS choices byte-for-byte |
| C16 | Turn 1 | First sentence includes plan.place stem (`խնձորենու այգ` for Plan A, `հին կամուրջ` for Plan D) |

### 5.2 Quality rubric — same 10 dimensions as the Claude.app captures

Per cell or per-plan-per-provider:

- Armenian naturalness (1–5)
- Eastern Armenian correctness (1–5)
- Fairy-tale feeling (1–5)
- Warmth for age 4–7 (1–5)
- Age-profile fit (1–5)
- Length / pacing (1–5)
- Choice quality (1–5)
- Continuation coherence (1–5)
- Plan adherence (1–5)
- Bounded arc / stop condition (PASS / FAIL)
- Safety / age appropriateness (PASS / FAIL)
- Ship-as-Areg aloud (yes / no / yes-with-edits)

### 5.3 API-only operational metrics

Recorded automatically per call:

- **Latency** — total wall-clock from request send to first
  byte AND to last byte; record both.
- **Token usage** — prompt tokens, completion tokens, total
  tokens (per the provider's response metadata).
- **Cost** — derived from token usage × the provider's
  posted price at run-time. Record posted-price snapshot
  alongside the calculation; do not assume prices are
  static.
- **Retry / error behavior** — record HTTP status, retry
  count, error type (rate-limit / timeout / 5xx / auth /
  other), and whether the existing
  `OpenAIReliabilityGate` (or its Anthropic equivalent in
  the bake-off CLI) was exercised.
- **Model id echo** — the model id the API actually
  returned in its response metadata (may differ from the
  requested id if the provider routes to a checkpoint).

### 5.4 Cross-provider deltas

Recorded once per plan:

- **C3 artefact delta**: Claude.app vs Claude API vs OpenAI
  API — does the duplicate-sentence-trio appear on any of
  them? **Load-bearing**.
- **C15 BREAK-GLASS delta**: do both APIs hold the
  byte-for-byte rule under default decoding?
- **C9 closure delta**: do both APIs end Turn 3 without a
  choice block?
- **Length-budget delta**: which provider hits the budget
  most consistently? Plan-D-specific (richer prompts).
- **Cost delta**: per-3-turn-story $ on each provider at
  the run-time prices.
- **Latency delta**: per-3-turn-story wall-clock at the
  observed run-time conditions.

---

## 6. Hard gates (blockers)

If any of these fire on the API run, **the API result is not
fit for any production-integration discussion** and slice E
remains blocked. They are NOT polish items; each one
individually halts promotion.

1. **Any unsafe output** — violence, weapons, horror,
   abandonment, illness-as-plot, non-child-safe content
   anywhere in any of the 12 cells. Hard halt; record the
   full raw output (do not normalize, do not redact for the
   evidence file — but flag clearly so downstream readers
   don't quote it without context). No production discussion
   until the underlying cause is understood.
2. **Meta-output (gate C14 FAIL)** — any cell emits
   `Շարունակեց հեքիաթը...`, `Note:`, `As an AI...`,
   parenthetical narrator commentary, or any string outside
   the v3.1 contract. Blocker. Iterate the v3.1 prompt.
3. **Exact-choice failure on Turn 2 (gate C15 FAIL)** — any
   cell's Turn 2 emits BREAK-GLASS choices that do not match
   byte-for-byte. Blocker. Iterate the BREAK-GLASS contract
   wording or shift to operator-side post-hoc normalization.
4. **Turn 3 emits choices (gate C9 FAIL)** — any cell's
   Turn 3 contains `Ա: ` / `Բ: ` / `Ա)` / etc. Blocker.
   Bounded-arc rule has regressed under API conditions;
   investigate whether decoding parameters are interfering.
5. **Poor Armenian naturalness** — any cell scores ≤ 2 / 5
   on Armenian naturalness or Eastern Armenian correctness
   per Hayk's native review. Blocker for that provider on
   that plan; flag for a per-plan native-polish slice before
   re-running.
6. **Duplicate-sentence-trio artefact recurring (gate C3
   FAIL) on the API path** — if the artefact appears on
   continuation turns under API conditions, the hypothesis
   "artefact is UI-side" is falsified. Blocker for any
   "v3.1 prose is clean" claim; iterate the v3.1 anti-
   duplicate rule (currently rule F).
7. **Excessive latency or cost** — concrete thresholds:
   - **Latency**: any single Turn 1 call > 30s wall-clock,
     OR any 3-turn story session > 90s total wall-clock.
     Production spoken-toy attention budget is the upper
     bound; >90s for one story session is operator-painful.
   - **Cost**: any 3-turn story session > **$0.50** under
     posted prices at run-time. Production economics need
     1¢-class per-story cost or better; >50¢ is a runtime
     blocker even if quality is excellent.
8. **Bad formatting / parsing stability** — any cell where
   the choice block is not at the end of the response, OR
   has unexpected whitespace / newline encoding that breaks
   the existing `TailBlockParser` contract. Blocker for
   any production parser-side decision.

A cell that fires hard-gate 1 (unsafe output) ALSO triggers
an immediate halt of the remaining cells in the matrix —
the operator decides whether to continue based on the unsafe
content's nature.

---

## 7. Pre-execution protocol

**This protocol is mandatory before any paid API call.** It
is enforced by the bake-off CLI today (per repo
`README.md` § Live execution); this document re-states it for
clarity and adds the 2-provider symmetry requirements.

### 7.1 Operator pre-checks (before invoking the runner)

1. **Verify API keys exist only in environment / user-secrets**:
   - `ANTHROPIC_API_KEY` — must be set in the shell
     environment OR in `dotnet user-secrets` for the
     bake-off project. **NEVER** committed in
     `appsettings.json`, `.env`, or any tracked file.
   - `OPENAI_API_KEY` — same constraint.
   - The bake-off CLI's existing posture (per README): if
     the env var is missing, the provider is "skipped"
     rather than silently using a default key. Preserve
     this — abort on missing key, do not substitute.
2. **Verify no secrets in the working tree** — `git status`
   must not show any new staged or unstaged file containing
   an API key. A pre-flight `git grep -n "sk-\|ANTHROPIC_API_KEY=\|OPENAI_API_KEY="`
   sanity check is recommended.
3. **Verify model defaults are still appropriate** —
   `ANTHROPIC_BAKEOFF_MODEL` and `OPENAI_BAKEOFF_MODEL` env
   vars override the CLI defaults (`claude-opus-4-7` /
   `gpt-4o` per the README). Before running, confirm the
   intended model ids and document them in the run's plan.

### 7.2 Runner pre-execution plan (printed to stdout, BEFORE the first call)

The bake-off CLI's existing "pre-execution plan + Ctrl-C
within N seconds" protocol applies. The runner must print:

- **Provider matrix** — Claude API / OpenAI API; live or
  skipped based on env-var presence.
- **Resolved model id per provider** — the exact model id
  the CLI resolved against env vars + defaults.
- **Scenario / turn / call count** — for the v3.1 plan-pair
  comparison: 12 calls minimum (single pass) or 24 calls
  (variance pass).
- **Estimated input tokens, output tokens, total tokens** —
  derived from prompt sizes and v3.1 length budgets:
  - Turn 1: ~5,000 prompt tokens + ~600 completion (Plan A) / ~800 completion (Plan D)
  - Turn 2: ~6,000 prompt tokens + ~500 completion
  - Turn 3: ~7,000 prompt tokens + ~500 completion (Plan A) / ~600 completion (Plan D)
  - Per provider per single pass: ~36K prompt + ~3.2K completion ≈ 40K tokens
- **Estimated cost** — at run-time-posted prices. Order-of-
  magnitude: ~$0.30 per provider per single pass on
  Claude Opus 4.7 / GPT-4o pricing as of late 2026; ~$0.60
  for the optional variance pass; ~$1.20 total round-trip
  if both passes run on both providers. Document actual
  prices and the URL of the price page in the result file.
- **Bake-off prompt SHA-256** + **production-prompt
  SHA-256** + drift status — already part of the CLI's
  pre-execution print.
- **A "Ctrl-C now if this is unexpected" line** with a
  printed delay (the CLI today fires immediately after this
  line; existing behavior).

### 7.3 Explicit GO confirmation

The runner must **require an explicit GO before paid calls**.
Existing bake-off CLI behavior:

- `--run` is required (default is dry-run).
- `--provider claude` or `--provider openai` is required
  (no all-providers default).
- `--i-understand-live-cost` is required.
- Either `--max-prompts N` OR `--allow-full-set` is required
  (XOR — both rejected).

The 2-provider API comparison run extends this: **both
provider flags must be set explicitly per pass**, OR the
operator runs the matrix in two halves (Claude-only first,
review the output, then OpenAI). The two-halves approach is
**preferred** for the first comparison — it lets the operator
catch a hard-gate failure on the first provider before
spending budget on the second.

### 7.4 Abort paths

The runner MUST abort on:

- Missing required API key (do not silently degrade or
  skip-with-default-key).
- Missing required CLI flags (`--run`, provider flag,
  `--i-understand-live-cost`, max-prompts XOR).
- Network / auth failure on first call (do not retry past
  the existing reliability-gate budget; full-pass abort if
  the first call fails).
- Hard gate 1 (unsafe output) firing on any cell — halt the
  remaining cells in the matrix.

### 7.5 Save raw outputs to evidence files; never write secrets to disk

- Raw outputs go to `evaluations/api-comparison-run-YYYYMMDD.{md,json}`
  per § 8.
- The result file's metadata records model id, requested
  decoding parameters, response timestamps, and observed
  latency / token usage / cost. **Never** the API key
  itself, **never** any header containing the key,
  **never** any `Authorization:` value.
- If the runner needs to log diagnostic info, it logs to
  stdout (the existing `JsonConsoleFormatter` per
  `appsettings.json`'s logging config), not to the
  evidence file.

---

## 8. Output evidence shape

Two future files per API run (NOT created by this prep doc):

### 8.1 `evaluations/api-comparison-run-YYYYMMDD.md`

Human-readable comparison report. Mirrors the structure of
the v3.1 strict-protocol Claude.app captures
(`019177c` / `f20e473`) but covers two providers symmetrically
in one file. Suggested sections:

1. **Status / honesty framing** — model ids actually used,
   timestamp, decoding parameters, posted prices, single
   pass vs variance pass, who reviewed.
2. **Run metadata** — bake-off prompt SHA-256, production-
   prompt SHA-256, drift status, plan JSON references.
3. **Per-cell raw outputs** — one slot per cell (12 or 24).
   Same shape as the existing capture files: raw output,
   normalized output (collapsed duplicates if any, stripped
   meta if any), notes, per-cell pass/fail mini-table.
4. **Per-plan-per-provider verdict** — rubric scores and
   gate roll-ups for each (provider × plan) pair.
5. **Cross-provider delta section** — § 5.4 metrics
   filled in.
6. **Hard-gate audit** — explicit yes/no on each of § 6's
   eight blockers.
7. **Decision** — per § 9 below.

### 8.2 `evaluations/api-comparison-run-YYYYMMDD.json`

Machine-readable, suitable for diffing future runs. Schema
sketch:

```json
{
  "schemaVersion": 1,
  "runId": "<UTC timestamp>",
  "operator": "Hayk",
  "providers": [
    {
      "name": "claude",
      "modelRequested": "claude-opus-4-7",
      "modelEchoed": "<actual id from response>",
      "decoding": { "temperature": null, "top_p": null, "max_tokens": null, "seed": null }
    },
    { "name": "openai", "modelRequested": "gpt-4o", "modelEchoed": "...", "decoding": { ... } }
  ],
  "plans": [ { "label": "plan-a-age-4-simple-#17", "ref": "<file:line>" }, { "label": "plan-d-age-7-richer-recovered", "ref": "<file:line>" } ],
  "cells": [
    {
      "cellId": 1,
      "provider": "claude",
      "plan": "plan-a",
      "turn": 1,
      "selectedChoice": null,
      "rawOutput": "<verbatim>",
      "normalizedOutput": "<verbatim>",
      "latencyMs": { "toFirstByte": 0, "toLastByte": 0 },
      "tokenUsage": { "prompt": 0, "completion": 0, "total": 0 },
      "costUsd": 0.0,
      "gates": { "C1": "PASS", "C2": "PASS", ... },
      "rubric": { "armenianNaturalness": 4, ... },
      "hardGates": { "unsafe": false, "metaLeak": false, ... }
    }
  ]
}
```

Both files are committed to `evaluations/` as evidence
(operator-reviewed; not auto-generated commits) — same
posture as the v3.1 Claude.app captures.

---

## 9. Decision rules

After the API comparison run, score the evidence against
**three decision branches**:

### Branch 1 — Claude API clearly wins quality + reliability

If Claude API:
- passes all 17 gates on both Plan A and Plan D
- shows lower or equal duplicate-sentence-trio rate vs
  OpenAI API
- shows acceptable latency and cost (per § 6 hard gates)
- AND OpenAI API on the same prompts shows clear quality
  loss (1+ rubric dimension ≤ 3 / 5 vs Claude's ≥ 4 / 5,
  OR multi-cell C14 / C15 / C16 failures)

→ **Prepare slice E (production-integration design doc)
    only.** Slice E is design-only; it does NOT execute a
    provider switch. The decision to actually wire Claude
    into ChatService is its own future slice that requires
    explicit operator approval AND the design doc's risk
    review AND a measured cost / latency budget that
    production can absorb.

### Branch 2 — OpenAI API catches up under the v3.1 prompt

If OpenAI API:
- passes all 17 gates on both Plan A and Plan D under the
  v3.1 writer prompt
- shows quality parity with Claude API (no rubric dimension
  > 1 point apart)
- AND has the cost / latency advantage typical of GPT-4o vs
  Opus 4.7

→ **Stay with OpenAI for runtime.** v3.1 writer prompt is
    the lever; provider switch is unnecessary. Document the
    finding; consider a future slice that wires v3.1 into
    `ChatService`'s rendering path with explicit operator
    approval (NOT auto-merged).

### Branch 3 — Both providers underperform

If neither provider clears all 17 gates on both plans, OR
both show poor Armenian naturalness, OR both produce
duplicate-sentence-trio artefact under API conditions:

→ **Continue prompt / planner work.** v3.1 is not the final
    writer prompt; iterate. The next slice would propose
    v3.2 hardening based on which gates failed and on which
    provider. Slice E remains blocked.

### Cross-cutting: provider switch is NOT decided here

Under **any** branch outcome, this prep doc + the API
comparison run together do NOT authorize a runtime provider
switch. A switch is a **separate later decision** that
requires:

- The slice E design doc.
- Explicit operator (Hayk) approval.
- A measured production cost / latency budget.
- A production parser / orchestration audit (Story
  Director's per-turn placeholder substitution does not
  trivially fit the existing single-prompt ChatService
  flow).
- A safety / parent-dashboard review (the existing dual-
  moderation contract must continue to hold).

---

## 10. What NOT to do

Hard "no" list. Each item below holds **regardless of API
comparison outcome**.

- **Do NOT switch runtime provider** as a result of this prep
  doc or the API comparison run alone. OpenAI stays in
  production until a separate explicit decision lands.
- **Do NOT edit `ChatService`.** It stays frozen.
- **Do NOT connect Story Director to production runtime.** The
  pipeline lives in `tools/StoryModelBakeoff/` and stays
  there until slice E + an explicit production-wiring slice
  both land.
- **Do NOT use Claude.app evidence as API truth.** The
  strict-protocol Claude.app captures (`019177c`, `f20e473`)
  are reference / ceiling evidence; the API run is what
  produces runtime-decision evidence.
- **Do NOT commit secrets.** `ANTHROPIC_API_KEY` and
  `OPENAI_API_KEY` live in `dotnet user-secrets` or shell
  env vars only. Never in `appsettings.json`, `.env`, or
  any tracked file.
- **Do NOT run paid API calls without explicit GO.** The
  bake-off CLI's `--run` + `--i-understand-live-cost` +
  per-provider flag + max-prompts-XOR contract is the
  operator-confirmation surface; honor it.
- **Do NOT redact or normalize raw outputs before saving
  them.** The evidence file's `rawOutput` slot must be
  byte-for-byte verbatim. Normalize in a separate
  `normalizedOutput` slot. (Same posture as the strict-
  protocol Claude.app captures.)
- **Do NOT run only one provider and call it "the API
  comparison."** Both providers run head-to-head on the
  same plans for this evidence to mean anything; if only
  one provider is available (e.g. only `ANTHROPIC_API_KEY`
  is provisioned), defer the comparison until both keys
  are in place rather than running half the matrix.
- **Do NOT skip gate scoring.** Every cell gets scored
  against every applicable gate (per § 5.1) even if the
  prose looks fine; the gate matrix is what makes
  cross-cell comparison meaningful.
- **Do NOT exceed the cost / latency hard gates** (§ 6
  item 7) in pursuit of a "richer" comparison. If the
  budget hits the threshold, halt; return to design.

---

## 11. Out of scope for this prep document

- No API calls executed. This file is design + safety
  contract only.
- No new evidence captures. Plan A and Plan D strict
  Claude.app evidence stays as the reference.
- No new writer-prompt rules. v3.1 is the prompt under
  test.
- No new gates beyond C1–C16. The API run uses the same
  gate set as the Claude.app strict captures.
- No bake-off CLI source change. The existing F1.2
  Claude live-execution path + the in-progress OpenAI
  live path use the existing infrastructure; any small
  generalization for v3.1's per-turn placeholder
  substitution flow is itself a future implementation
  slice and is NOT prescribed here.
- No commit / push by the prep doc itself. Evidence files
  from the future API run will be operator-reviewed
  before commit.

# Repo Workflow

## When to use

Invoke at the **start of every task** in the ArmenianAiToy repo, before
reading files for implementation and before writing any code. Keeps the
working style consistent across the whole project: inspection-first,
smallest-valuable-step, repo-aware, review-friendly, honest about
blockers.

This is the unifying workflow skill. The focused sub-skills
(`/task-brief`, `/change-decision`, `/phase-b-guardrails`,
`/minimal-csharp-change`, `/pre-commit-check`) remain the detailed
references for their phases; this skill sets the overall discipline
and the repo-specific guardrails they all share.

## What it enforces

- Inspect first, never assume.
- Classify the task explicitly (Review only / Minimal code change / Larger refactor).
- Prefer the smallest high-value step.
- Diagnose carefully: real product issue vs benchmark/UI ambiguity vs upstream/noise.
- No speculative rewrites, no silent scope expansion, no quiet architecture changes.
- Honest reporting: including "no change is the correct answer" when it is.

## Project context (load once per task)

- Product: Armenian AI Toy ("Areg"), ages 4–7, Armenian-first, parent-trust-first.
- Backend: .NET 10, Clean Architecture (Api / Application / Domain / Infrastructure).
- Static UI: inline HTML + CSS + vanilla JS in `backend/src/ArmenianAiToy.Api/wwwroot/`.
- Modes (five only, spec in `.claude/MODES.md`): Story, Game, Riddle, Calm, Curiosity.
- Benchmarks: per-mode under `tools/<Mode>Benchmark/`; orchestrator `tools/BenchmarkAll/`.

### Current phase state

- Dedicated benchmark split: **complete**.
- `ModeBenchmark`: **retired — must stay retired**.
- `tools/BenchmarkAll/` + structured `summary.json` reporting layer: **complete**.
- Per-mode product quality: **at a strong stopping point**. Story / Calm / Curiosity / Game / Riddle each at or near zero residual real signals.
- Parent-dashboard quality phase: active, small UI/DTO passes landing one at a time.
- Some closure items may be **parked** due to intermittent upstream OpenAI / moderation-adapter instability — do not actively retry those in the current window.

### Local rules that never change

- `.claude/settings.local.json` may carry unrelated local modifications. **Never touch it and never stage it** unless explicitly asked.
- Follow CLAUDE.md guardrails on top of this skill.

## Step 1 — Classify before acting

Pick one and state it explicitly before doing anything else:

- **Review only** — user wants analysis, inspection, or a recommendation. No files change. End with findings + smallest-next-step.
- **Minimal code change** — a concrete, bounded fix or addition. Smallest safe diff. No surrounding cleanup, no opportunistic refactor.
- **Larger refactor** — rarely correct. Only when the user explicitly authorizes redesign. If you think this is warranted and the user hasn't said so, stop and ask.

If unclear, default to **Review only** and ask.

## Step 2 — Inspect before implementing

- Read the exact files and line numbers the task touches. Do not rely on memory of the repo.
- Verify data shape before proposing DTO / service changes.
- Verify test coverage before proposing logic changes.
- Verify what is already persisted vs only computed transiently before surfacing anything new on the parent UI.

## Step 3 — Diagnose: what kind of issue is this?

Separate these three categories explicitly. Many past passes conflated them and wasted effort.

- **Real product issue** — child-facing behavior is measurably wrong. Fix at root cause in the prompt / ChatService / service / DTO, smallest possible.
- **Benchmark/UI ambiguity** — signal is flagging something the prompt permits, or UI copy is misleading. Fix the benchmark threshold or the UI copy (whichever matches stated intent) — not the child-facing prompt.
- **Upstream/noise problem** — OpenAI / moderation adapter returning 502 or fail-closing; results dominated by fallback strings. **Do not chase.** Park the item, document the close-out condition, move on.

## Step 4 — Respect the guardrails

Refuse or defer anything that crosses these:

- Do NOT reopen the dedicated benchmark split or the BenchmarkAll reporting layer.
- Do NOT reintroduce `ModeBenchmark` in any form.
- Do NOT touch child-mode prompts (`ChatService.cs` mode instruction constants, `BuildCalmTurnDirective`, etc.) during parent-dashboard or benchmark-infra work — and vice versa. One workstream per pass.
- Do NOT touch `.claude/settings.local.json`.
- Do NOT introduce speculative rewrites, folklore, audio, or hardware work.
- `Message` / `Conversation` domain entity changes, `AddMessageAsync` signature widenings, new endpoints, new NuGet packages, new migrations, system-prompt changes, or moderation pipeline changes are **HARD STOPS** — surface them and wait for explicit authorization per CLAUDE.md.
- Parked live-validation items (Story → Curiosity → Story live-closure, StoryBenchmark baseline refresh) stay parked unless upstream is demonstrably healthy.

## Step 5 — Opinions to apply

- **Root cause > cosmetic.** If a weak signal in a benchmark is traced to a real product gap, fix the product. If it is traced to over-strict detection, fix the detection — do not tighten the prompt to game the benchmark.
- **Small DTO / service / UI passes > medium refactors** when they solve the stated problem.
- **Product truth > benchmark gamesmanship.** Never shorten, soften, or restructure a child-facing output purely to move a benchmark number.
- **Park > retry** on upstream-dependent validations that have already failed once or twice in the same window.
- **Consistency with the repo's prior approved steps > novelty.** If a similar change has landed before (e.g. nullable DTO field with default), follow the same shape.
- **Parent-dashboard changes prefer read-only, observability-only, UI-first.** Backend touches only when data genuinely doesn't support the UI fix.

## Step 6 — What to avoid

- Broad rewrites, even tempting ones in the same file.
- Mixing multiple workstreams in a single pass.
- Unnecessary controller / domain / migration changes.
- Touching benchmark or reporting architecture unless explicitly asked.
- Changing child-mode prompts during parent-dashboard or benchmark-infra work.
- Pretending validation happened when upstream blocked it. Say so honestly.
- Adding features, comments, error handling, or abstractions that the task did not ask for.

## Output template — code task

Use this at the end of a pass that changed code:

```
### Exact files changed (this pass)
  M <path> — <what changed, one line>
  ...

Files NOT changed (per guardrails):
  - <category 1>
  - <category 2>
  - .claude/settings.local.json (still only prior unrelated M, unstaged)

### What changed
<1–3 sentences per file describing the change in product terms>

### What was intentionally NOT changed
<explicit list of adjacent-but-deferred items, with the one-sentence reason each>

### Validation performed
  - dotnet build: <result>
  - dotnet test: <N/N passing, Δ new tests>
  - (if live) benchmark / manual flow run: <result>

### What should happen next
<1–3 bullets: the smallest next authorizable step(s)>
```

## Output template — review-only task

Use this at the end of a pass that did not change code:

```
### Current state
<concrete findings, with file:line references>

### Is a change justified?
<yes / no / not yet — with the reason>

### Smallest next step, if any
<a single concrete step, or "none — recommend <other thing> instead">
```

## Constraints

- Do NOT start implementation until the classification and inspection steps are done.
- Do NOT skip the Diagnose step when a weak signal is involved — real / ambiguity / noise is always a useful distinction.
- Do NOT modify files just because the task mentions a feature; confirm the chosen mode first.
- Do NOT collapse the output templates into prose — parents of this repo (and ChatGPT review) rely on the structure.
- If you identify a HARD STOP, stop and ask — do not work around it.
- If upstream is degraded, report that explicitly and do not burn retries.

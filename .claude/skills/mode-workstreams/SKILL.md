# Mode Workstreams

## When to use

Invoke when the requested task changes how any child-facing mode behaves, reads, or is tested. Covers Story, Game, Riddle, Curiosity Window, Calm/Bedtime, and any future mode that follows the same bounded-play product philosophy.

Apply the moment any of these words appear in a task: prompt, mode, story tone, riddle hint, game loop, calm bedtime, curiosity answer, mode transition, tail block, mode detector, per-mode benchmark, mode regression test.

## Tasks that belong here

- Changing the tone, length, or structure of a specific mode's output
- Refining a mode's prompt text or instructions
- Adjusting mode transitions (e.g. Story → Curiosity → Story)
- Benchmark baseline refresh for a single mode tool
- Benchmark tolerance updates for a single mode
- Safety behavior changes that apply inside a single mode (e.g. Calm's no-fear rule)
- Adding a mode-specific regression test or tightening an existing one
- Child-facing UX/content constraints that are scoped to one mode
- Fixing a mode's distinct product purpose that has drifted

## Tasks that do NOT belong here

- Parent dashboard / parent JWT auth / parent controller work
- Device auth middleware, security posture, API key handling
- Hardware, audio, ESP32, or on-device work
- Broad benchmark architecture redesign
- Repo-wide refactors, DI rework, controller restructures
- Unrelated API/controller work (ConversationController, ChildController, etc.)
- Adding new modes from scratch (that requires product approval first)

If the task crosses this line, stop and ask whether to split it.

## Default working style

- **Inspect first, edit later.** Before touching prompts, read the current mode prompt, the relevant benchmark tool, the recent mode-related tests, and the last baseline under `tools/<Mode>Benchmark/`.
- **Prefer the smallest working diff.** Prompts, tests, and baselines — in that order of preference. Code changes to `ChatService` or central helpers are a last resort.
- **Reuse existing mode patterns.** Each mode already has its tail-block parser, prompt-content tests, loop integration tests, and dedicated benchmark. Extend what exists before creating anything new.
- **Preserve the benchmark split.** The per-mode split is finished. Never collapse, relocate, or rename benchmark tools.
- **Do not mix unrelated workstreams.** A prompt refinement and a benchmark refresh are two commits, not one. A mode-behavior change and a test pin are two commits.
- **Do not touch modes that were not requested.** "Tighten Calm" means touch Calm. If a second mode appears to need the same fix, flag it — do not silently edit both.
- **When prompt text changes, explain exactly why.** One sentence per added/removed/replaced instruction. Link the instruction to the product-intent it serves.
- **When a baseline moves, say which.** Either "behavior changed intentionally, baseline re-captured" or "baseline was stale, behavior unchanged, refreshed." Never both in the same commit.
- **Armenian quality is a first-class concern.** Prefer natural spoken Armenian; reject stiff or machine-translated phrasing. If uncertain, defer to the `armenian-linguistic-reviewer` agent before shipping.

## Mode-change decision guide

Classify the requested work into exactly one of these before editing.

### 1. Prompt-only refinement
- Changing wording, ordering, or emphasis inside one mode's prompt.
- No test changes, no benchmark baseline changes expected immediately.
- Risk: LOW-MEDIUM. Always run the affected mode's benchmark after.

### 2. Test-only change
- Pinning an existing behavior with a new regression test.
- No production edit. No prompt edit.
- Risk: LOW.

### 3. Benchmark-only refresh
- Re-capturing a baseline after an already-approved, already-landed behavior change — OR — refreshing a stale baseline with no behavior change.
- No production edit, no prompt edit, no test edit.
- Risk: LOW. Must clearly state which of the two reasons applies.

### 4. Narrow mode-behavior change
- Adjusts one mode's output through prompt + targeted tests + (if needed) a baseline refresh.
- Single commit is acceptable if the three pieces are tightly coupled.
- Risk: MEDIUM. Run the affected mode's benchmark and nothing else, unless the mode has a transition into/out of another mode (then run the partner mode too).

### 5. Cross-mode change
- Touches two or more modes (e.g. a shared helper, a transition that belongs to both ends, a safety rule applied across modes).
- STOP and request approval before editing. Propose a split-per-mode plan if possible.

### 6. Unsafe / too-broad / needs approval
- Anything that edits `ChatService` orchestration, the global system prompt, safety/moderation, `ModeDetector` priority rules, or entity shape.
- STOP and request approval. Produce a plan with exact files/lines.

## Mode guardrails

- Do NOT flatten all modes into the same tone. Story ≠ Game ≠ Riddle ≠ Curiosity ≠ Calm. Each exists because the others don't do this job.
- Preserve each mode's distinct product purpose (see `.claude/MODES.md` for the canonical spec).
- Do NOT improve one mode by degrading another — shared helpers must be evaluated against every mode that depends on them.
- Do NOT weaken child-safety rules (no-violence, no-fear in Calm, no-scary-in-bedtime, no-PII-asks). Tightening is fine; loosening is a hard stop.
- Do NOT make Armenian text more machine-translated, literal, bookish, or stiff. Spoken-warm is the target.
- Do NOT widen into ChatService, orchestration, or system-wide architecture unless the task explicitly asks for it AND the user has approved.
- Do NOT reopen the benchmark split. If a task mentions benchmarks, it is a per-mode task unless the user explicitly says otherwise.
- Do NOT silently add folklore, audio, or hardware content — these are out of scope by standing project rule.
- Identity stays the same across modes — Areg's voice is consistent even as mode-specific style shifts.

## Benchmark handling guidance

- **Scope.** Touching one mode → run only that mode's benchmark. Touching a transition (Story↔Curiosity, Game↔Calm) → run both modes' benchmarks. Never run all modes unless the user has explicitly asked for a sweep.
- **When a baseline refresh is justified.**
    1. Behavior changed intentionally and the change has already landed or is landing in the same commit. The baseline becomes the new correct record.
    2. The baseline is demonstrably stale (e.g. tolerance drift on a prompt that was never touched) and behavior is unchanged. Refresh to re-establish a clean signal.
    These are mutually exclusive reasons — state which applies in the commit message.
- **Tolerance changes.** Tightening a tolerance is a quality push; loosening is a silent regression gate. Loosening is almost never the right move. If you must loosen, explain why the underlying check is too strict for the product's actual acceptable behavior.
- **Do NOT mix a baseline refresh with unrelated code changes.** Split the commit. The baseline refresh should read like a factual record, not a behavior change bundled with a number update.
- **Interpret before editing.** If a benchmark fails, first determine whether the failure is a genuine regression, a flaky prompt, an Armenian-naturalness drift, or a tolerance mismatch. Do not edit prompts to chase a failing baseline without understanding which kind of failure it is.

## Output expectations

For any non-trivial mode task, report in this exact structure:

1. Current State
2. Change Decision
3. Files Changed
4. Diff Summary
5. Validation Results
6. Risks / Tradeoffs
7. Exact Commit Message Suggestion

Under **Change Decision**, state the decision-guide classification (1–6 above) and one sentence explaining why.

Under **Validation Results**, always include:
- Targeted test filter result
- `dotnet build` result
- Full `dotnet test` result
- Which benchmark(s) were run and the delta vs previous baseline, OR an explicit "no benchmark run because X"

## Repo-aware examples

### Good use: tightening Calm without touching other modes
- Edit only `CalmPromptContent` and `CalmPromptContentTests`.
- Run `CalmBenchmark` only.
- Refresh the Calm baseline only if behavior changed intentionally.
- Do NOT edit Story/Game/Riddle/Curiosity prompts in the same commit.

### Good use: refreshing a Riddle baseline after an intentional behavior change
- Previous commit: changed Riddle hint shape.
- This commit: re-capture the `tools/RiddleBenchmark` baseline only.
- Commit message explicitly states: "behavior changed intentionally, baseline re-captured."
- No other files touched.

### Good use: adding a Story → Curiosity transition fix
- Mode-detector edit is a `ModeDetector` + targeted tests change.
- Run both `StoryBenchmark` and `CuriosityBenchmark` because this is a transition.
- Do NOT touch Riddle/Game/Calm.
- If the fix would also affect other transitions, STOP and get approval.

### Good use: adding a mode-specific regression test without changing production code
- New test file or extension of an existing mode-specific test file.
- No prompt edit, no baseline edit.
- Run targeted filter + full suite. No benchmark needed for a test-only change.

### Good refusal: keeping the benchmark split closed
- Task: "While you're refreshing the Calm baseline, can you collapse the per-mode benchmarks back into ModeBenchmark for simplicity?"
- Correct response: refuse the collapse, do only the baseline refresh, explain that the split is the agreed architecture and combining would erase per-mode signal.

## When to stop and ask for approval

Stop and request explicit approval before editing when the task:

- Crosses two or more modes (classification #5).
- Would change the global system prompt, `ChatService` orchestration, `ModeDetector` priority rules, or any domain entity.
- Would alter safety/moderation behavior broadly (not just a per-mode tightening).
- Would require changes to multiple benchmark tools in one commit.
- Would introduce a new mode or retire an existing one.
- Would relax a child-safety constraint in any direction.

In these cases, produce a plan with exact files and lines, name the classification from the decision guide, and wait for "approved — proceed" before touching code.

## Constraints

- Do NOT edit modes that were not requested.
- Do NOT combine prompt, test, and baseline-refresh commits when they can be split.
- Do NOT loosen safety rules or tolerances without explicit approval.
- Do NOT reopen the per-mode benchmark split.
- Do NOT introduce folklore, audio, or hardware work.
- Do NOT expand a mode task into ChatService / system prompt / orchestration territory without explicit approval.
- Do NOT skip the mode-change decision-guide classification step.

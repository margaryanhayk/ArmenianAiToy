# Benchmark Run

## When to use

Invoke after any story-affecting change: system prompt, ChatService, ArmenianSimplifier, ResponseQualityGate, ChoiceNormalizer, TailBlockParser, or StoryChoiceInstruction. Also invoke on-demand when the user wants a quality check.

## What it enforces

Ensures story quality is measured after every change to the generation pipeline, preventing silent regressions.

## Prerequisites

- The API must be running on `http://localhost:5000`
- If not running, report: "BLOCKED: API not running. Start with `dotnet run --project src/ArmenianAiToy.Api`"

## Steps

1. Verify API is running
   - `curl -s http://localhost:5000/api/health`
   - If fails: report blocked and stop
2. Run the benchmark
   - `dotnet run --project tools/StoryBenchmark -- http://localhost:5000`
   - Wait for completion (timeout: 5 minutes)
3. Parse results
   - Read the latest results file from `tools/StoryBenchmark/bin/Debug/net10.0/results/`
   - Extract: total prompts, pass count, fail count, weak count
   - Extract per-prompt: start response present, choice A present, choice B present, choices different, continuation works
4. Compare to baseline (if available)
   - Check agent memory at `backend/.claude/agent-memory/areg-story-evaluator/` for previous results
   - Report delta: improved / regressed / unchanged per metric
5. Report results

## Output format

```
Benchmark Results
=================
Prompts tested: 27
Start response:  25/27 (93%)
Choice A present: 22/27 (81%)
Choice B present: 22/27 (81%)
Choices different: 20/27 (74%)
Continuation:    18/27 (67%)

vs. Last baseline:
  Start response:  +2 (improved)
  Choice present:  -1 (regressed)
  Continuation:    +3 (improved)

Weak cases: [list IDs]
Failed cases: [list IDs]
```

## Constraints

- Do NOT modify benchmark code
- Do NOT modify production code
- Do NOT modify prompts.json (use the current set)
- If the API is not running, do NOT attempt to start it — just report blocked
- Report results factually — do not make recommendations (use story-evaluator agent for analysis)

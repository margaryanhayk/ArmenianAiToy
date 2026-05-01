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
| `results/` | Per-run Markdown + JSON output (created on first live run). **Gitignored** (`.gitignore` excludes `tools/StoryModelBakeoff/results/`). |

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

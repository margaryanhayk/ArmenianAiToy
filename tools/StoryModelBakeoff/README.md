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

## F1 slice 1 — DRY-RUN ONLY

This first slice is the scaffold. **No live provider call is made
in this slice**, even when `--run` is passed; that produces a
non-zero exit and a clear "Live provider calls are deferred to
F1 slice 2." message. Live API execution and result-file generation
land in slice 2 after explicit approval.

## What's in this folder

| File | Purpose |
|---|---|
| `Program.cs` | CLI: dry-run planner, prompt + system-prompt SHA-256 drift check, provider/model/key resolution. |
| `bakeoff-prompts.json` | The 12 Armenian story scenarios (multi-turn where relevant). |
| `system-prompt.txt` | Frozen copy of the production `SystemPrompt` (from `backend/src/ArmenianAiToy.Api/appsettings.json`) with a `# Source:` header. The loader strips that header before hashing. |
| `StoryModelBakeoff.csproj` | net10.0 console exe, no PackageReferences, no ProjectReferences. |
| `results/` | (slice 2) Per-run Markdown + JSON output. **Gitignored** (`.gitignore` excludes `tools/StoryModelBakeoff/results/`). |

## Running

```
dotnet run --project tools/StoryModelBakeoff
dotnet run --project tools/StoryModelBakeoff -- --provider claude --max-prompts 3
dotnet run --project tools/StoryModelBakeoff -- --help
```

The default invocation prints a dry-run plan: provider matrix
(live / skipped per API-key availability), resolved model per
provider, scenario / turn / call counts, the bakeoff-prompt
SHA-256, the production-prompt SHA-256, and whether drift was
detected. **No network is touched.**

## Provider environment variables

| Variable | Purpose |
|---|---|
| `OPENAI_API_KEY` | OpenAI auth. Provider is "skipped" without it. |
| `ANTHROPIC_API_KEY` | Claude auth. Provider is "skipped" without it. |
| `GEMINI_API_KEY` | Google Gemini auth. Provider is "skipped" without it. |
| `AAT_LOCAL_API_KEY` | Reserved for a future Armenian-local provider. No code path today. |

## Model override variables (optional)

| Variable | Default |
|---|---|
| `OPENAI_BAKEOFF_MODEL` | `gpt-4o` |
| `ANTHROPIC_BAKEOFF_MODEL` | `claude-opus-4-7` |
| `GEMINI_BAKEOFF_MODEL` | `gemini-2.5-pro` |

## Manual scoring rubric (slice 2 will surface this in each report)

Per scenario, per provider:

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

## What this tool is not

- **Not a regression benchmark.** That's `StoryBenchmark`.
- **Not a mode-routing test.** That's `ModeBenchmark`.
- **Not a runtime provider switch.** Production still uses OpenAI;
  changing that is HIGH risk and out of scope for F1.
- **Not safety-checked output.** The bake-off bypasses our backend's
  moderation pipeline by design (we measure raw model output).
  Reports land locally and are reviewed only by the operator.
- **Not in CI.** Live runs cost money and require manual approval.

## What lands in F1 slice 2

- Three real provider HTTP clients behind a small `IModelClient`
  abstraction (raw `HttpClient` + `System.Text.Json`; no provider
  SDK PackageReferences).
- `--run` actually fires calls.
- Per-scenario `S01.md` / `S01.json` … and a run-level
  `summary.md` / `summary.json` under
  `tools/StoryModelBakeoff/results/<UTCts>/`.
- Latency capture, token-usage capture (where the provider returns
  it), error categorisation.
- Per-provider skip behaviour when key is missing — no surprise
  spend.

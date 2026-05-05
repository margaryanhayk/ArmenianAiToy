# API comparison — preflight (2026-05-05)

**Status:** preflight only. **No paid API call was issued.** No
production code change. No `ChatService` change. No runtime prompt
change. No provider switch. No commit, no push, no stage.

This document records the read-only preflight pass against the
bake-off CLI at `b5efa4a`, as called for by slice 1 of the
`api-comparison-prep-20260504.md` roadmap. It surfaces three
preflight findings — two structural gaps in the runner itself
that the prep doc's design implicitly assumed are already there.

**Companion files:**
- [`./api-comparison-prep-20260504.md`](./api-comparison-prep-20260504.md) — slice D preflight design (commit `17bda1e`).
- [`./night-audit-20260505.md`](./night-audit-20260505.md) — whole-night audit (commit `b5efa4a`).
- [`../Program.cs`](../Program.cs) — bake-off CLI source.
- [`../bakeoff-prompts.json`](../bakeoff-prompts.json) — current scenario file.
- [`../system-prompt.txt`](../system-prompt.txt) — bake-off's frozen copy of the production system prompt.

---

## 1. Repo state at preflight start

```
## main...origin/main
 M .claude/settings.local.json
?? tools/StoryModelBakeoff/evaluations/manual-plan-d-v3-1-capture/session/
?? tools/story-quality-evidence-20260425.md
```

`HEAD == origin/main == b5efa4a`. Local noise = three expected
protected/local-only entries. No staged changes; no production /
runtime files modified.

---

## 2. Runner identification

The intended runner for the API comparison is the bake-off CLI:

```
tools/StoryModelBakeoff/Program.cs
tools/StoryModelBakeoff/StoryModelBakeoff.csproj
tools/StoryModelBakeoff/bakeoff-prompts.json
tools/StoryModelBakeoff/system-prompt.txt
```

Invocation pattern (from CLI `--help` and source comments):

```
dotnet run --project tools/StoryModelBakeoff [-- <args>]
```

### CLI flags

| Flag | Behavior |
|---|---|
| `--provider <name>` | `openai` / `claude` / `gemini` / `local` / `all` (default `all`). |
| `--max-prompts N` | Cap scenario count (live and dry-run). |
| `--run` | Execute live calls. **F1.2 ships Claude only.** |
| `--i-understand-live-cost` | Required for any `--run`. Belt-and-braces opt-in. |
| `--allow-full-set` | Run all scenarios; XOR with `--max-prompts`. |
| `--help` / `-h` | Print help. |

### Triple opt-in for live execution

Every live call requires **all four** of:

1. `--run`
2. `--provider claude`
3. `--i-understand-live-cost`
4. **scope** — either `--max-prompts N` (smoke) or `--allow-full-set` (full).

Missing any of the above prints a clear error and exits non-zero
without issuing a network call.

### Provider matrix (env-var keys + default models)

| Provider | Env var (key) | Env var (model override) | Default model |
|---|---|---|---|
| openai | `OPENAI_API_KEY` | `OPENAI_BAKEOFF_MODEL` | `gpt-4o` |
| claude | `ANTHROPIC_API_KEY` | `ANTHROPIC_BAKEOFF_MODEL` | `claude-opus-4-7` |
| gemini | `GEMINI_API_KEY` | `GEMINI_BAKEOFF_MODEL` | `gemini-2.5-pro` |
| local | `AAT_LOCAL_API_KEY` | `AAT_LOCAL_BAKEOFF_MODEL` | reserved (no live path) |

### Pre-execution / dry-run mode

`dotnet run --project tools/StoryModelBakeoff` *(without `--run`)*
prints a pre-execution plan with:
- Provider matrix + key-presence + model + selected-or-not.
- Scenario list + first-turn previews + per-provider call count.
- Bake-off prompt SHA-256 + production prompt SHA-256 + drift
  verdict. **Verified at preflight: drift = none (hashes match).**
- Estimated total live calls per `--run`.

No network call; no API key required. Safe to run on every
preflight without spending anything.

### Cost / latency / token-usage instrumentation

The live path captures and logs:
- `latencyMs` per call (wall-clock).
- `input_tokens` / `output_tokens` from Anthropic's usage object.
- A "tokens in/out" line per scenario at the end of the run.

Cost is **not** computed in dollars by the CLI; the operator
reads the model's posted price (`claude-opus-4-7` ≈ $15 / 1M
input + $75 / 1M output as of 2026-05) and multiplies. Bounded
spend is enforced via `--max-prompts`; the CLI's `ClaudeMaxTokens`
constant caps output at 1024 tokens per call.

---

## 3. Live-execution gap (HIGH — affects slice 3)

**The bake-off CLI as shipped at `b5efa4a` cannot run an OpenAI
live call.** The CLI's live-path guard chain explicitly rejects
every non-Claude provider:

```
F1.2 ships Claude live execution only. Pass --provider claude.
OpenAI / Gemini / all live execution is deferred to a later F1
slice.
```

Source: `Program.cs:164-172`. Exit code 2.

**Implication for the prep doc's 12-cell capture matrix.** The
matrix specifies `2 plans × 3 turns × 2 providers = 12 cells`.
Today only 6 of those cells are reachable (Claude side). The
OpenAI side requires an F1.3+ slice that adds the OpenAI live
path before any 2-provider head-to-head can run.

**Workarounds available today:**

1. **Claude-only smoke** (slice 2 from the audit's roadmap) is
   fully reachable. One scenario, one provider, ~$0.02–$0.05
   per call.
2. **OpenAI baseline via the production runtime** is technically
   reachable through the existing `POST /api/chat` path, but the
   production runtime uses the v0 `system-prompt.txt`, NOT v3.1.
   This would not be apples-to-apples with the Claude bake-off
   call.
3. **Manual OpenAI app capture** of v3.1 prompts is operator-
   driven, not API truth — re-introduces every Claude.app caveat
   on the OpenAI side.

**Recommended path:** land the F1.3 OpenAI live slice *before*
the slice 3 12-cell run. That slice is in scope of the bake-off
CLI itself, not of any production runtime change.

---

## 4. Scenario-file gap (HIGH — affects slice 3)

**The current `bakeoff-prompts.json` is the bare-Armenian smoke
scenario set, NOT v3.1 + Plan A + Plan D.**

12 scenarios at preflight, all single-turn or 2-turn, e.g.:

```
S01 bare-armenian        "Պատմիր հեքիաթ"
S02 transliterated       "patmir heqiat"
S03 english-request      "Tell me an Armenian fairy tale..."
S04 animal-story         "Պատմիր նապաստակի մասին"
...
S07 choice-continuation  "Պատմիր հեքիաթ" → "Ա"
```

These exercise the *production* system prompt's robustness
across opener variants, transliteration, English asks, and so
on. They do **not** match the prep doc's
`Plan A age-4-simple #17` / `Plan D age-7-richer #6` 3-turn
structure with v3.1 system prompt and BREAK-GLASS choice
blocks.

**Implication for slice 3.** The prep doc's matrix evaluates
v3.1 prompts on Plan A + Plan D. Today the bake-off would run
the wrong system prompt against the wrong scenarios; the
captured outputs would not be comparable to the strict-protocol
Plan A / Plan D Claude.app captures (`019177c`, `f20e473`).

**Workarounds available today:**

1. **Smoke-test the API wiring only** with the existing
   bare-Armenian scenarios. Establishes that the live path
   works end-to-end (request shape, auth, parsing,
   token/latency capture). Does not validate prompt quality.
2. **Add an `--scenarios <path>` flag** to the CLI so an
   alternate v3.1-shaped scenario file can be selected without
   replacing `bakeoff-prompts.json` (which serves the existing
   bare-Armenian smoke purpose).
3. **Replace `bakeoff-prompts.json`** with v3.1 + Plan A +
   Plan D scenarios. Less flexible — drops the bare-Armenian
   smoke set.
4. **Keep `bakeoff-prompts.json` as-is + add a sibling**
   `bakeoff-prompts-v3-1.json` plus an `--scenarios` flag.
   Cleanest forward-compatible shape.

**Recommended path:** option 4 (additive scenario file +
`--scenarios <path>` flag) under a separate slice
("F1.3a — alternate scenarios flag" or similar) before any
slice-3 12-cell run. The v3.1 system prompt would also need to
be supplied alongside, since the bake-off currently uses the
production `system-prompt.txt`.

---

## 5. Key availability check

Checked **without printing values**:

| Variable | Source | Present in this shell? |
|---|---|---|
| `ANTHROPIC_API_KEY` | env var (process) | **no** |
| `OPENAI_API_KEY` | env var (process) | **no** |
| `ANTHROPIC_BAKEOFF_MODEL` | env var (process) | no (defaults to `claude-opus-4-7`) |
| `OPENAI_BAKEOFF_MODEL` | env var (process) | no (defaults to `gpt-4o`) |
| `OpenAI:ApiKey` | backend `dotnet user-secrets` (`src/ArmenianAiToy.Api`) | **yes** (key present in user-secrets, **value not consumed by the bake-off CLI**) |
| `Jwt:Key` | backend `dotnet user-secrets` (`src/ArmenianAiToy.Api`) | yes (production JWT signing — irrelevant to this preflight) |

**Important:** the backend's `OpenAI:ApiKey` user-secret is the
**production runtime** key. The bake-off CLI does **not** read
`dotnet user-secrets`; the CLI's csproj has no `UserSecretsId`
and the source unconditionally calls
`Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")` /
`...("OPENAI_API_KEY")`. **Reusing the production OpenAI key
for bake-off research is a deployment-coupling risk** — if the
key gets logged, leaked, or burns rate limits during research,
production is affected. Prefer a *separate* dedicated research
key per provider.

---

## 6. Setup instructions if keys are missing

The operator should set the keys as **process env vars**, not
hard-coded into any file or committed config. Two safe paths:

### 6.1 Per-shell env var (preferred for one-off / short sessions)

PowerShell (Windows):

```
$env:ANTHROPIC_API_KEY = "<paste-claude-key-here>"
$env:OPENAI_API_KEY    = "<paste-openai-research-key-here>"
```

bash / Git Bash:

```
export ANTHROPIC_API_KEY="<paste-claude-key-here>"
export OPENAI_API_KEY="<paste-openai-research-key-here>"
```

These set the variables for the **current shell only**. Closing
the shell removes them. **Do NOT** paste keys into chat with me;
**do NOT** commit any file containing the key; **do NOT** echo
the key value.

### 6.2 Persistent user-scoped env var (for repeat sessions)

PowerShell, persistent for the current Windows user:

```
[System.Environment]::SetEnvironmentVariable(
  "ANTHROPIC_API_KEY", "<paste-claude-key-here>", "User")
[System.Environment]::SetEnvironmentVariable(
  "OPENAI_API_KEY", "<paste-openai-research-key-here>", "User")
```

A new shell after this picks them up automatically. Use
`SetEnvironmentVariable("...", "", "User")` later to clear.

### 6.3 What NOT to do

- **Do not** add the keys to `.bashrc`/`.zshrc` if those files
  live in a repo or are otherwise version-controlled.
- **Do not** put the keys in `appsettings.json`, any
  `appsettings.*.json`, any `.env` file in the repo, or any
  `.csproj`/`.sln` file.
- **Do not** add the keys to `dotnet user-secrets`. The bake-off
  CLI does not read user-secrets and reusing the
  backend's user-secrets store risks colliding the research key
  with the production key.
- **Do not** paste any key into chat or any committed file.
  This document **does not** contain or solicit any key value.

---

## 7. Dry-run plan output (no API call, captured at preflight)

Captured at preflight (no `--run` flag, no API call):

```
============================================================
  StoryModelBakeoff — dry-run plan
============================================================

Providers:
  - openai  model=gpt-4o                    status=skipped (env OPENAI_API_KEY unset)
  - claude  model=claude-opus-4-7           status=skipped (env ANTHROPIC_API_KEY unset)
  - gemini  model=gemini-2.5-pro            status=skipped (env GEMINI_API_KEY unset)
  - local   model=(reserved)                status=reserved (no live path yet)

Scenarios: 1
Total turns across all scenarios: 1
  S01 [bare-armenian] turns=1  first="Պատմիր հեքիաթ"

Estimated calls per provider (per --run):
  (none — no live-ready provider in the selected matrix)
  TOTAL                = 0

Prompt identity:
  bake-off  sha256 = 54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4
  production sha256 = 54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4
  drift   = none (hashes match)
```

**Findings from the dry-run:**

- All three external-API env vars are **unset** in this shell.
- The bake-off `system-prompt.txt` is **byte-identical** to the
  production `appsettings.json` system prompt
  (sha-256 `54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`).
- Scenario 1 (with `--max-prompts 1`) is `S01 bare-armenian`
  — *not* a v3.1 + Plan A scenario. Confirms § 4 above.

---

## 8. Proposed dry-run command — DO NOT RUN UNTIL EXPLICIT GO

The most useful **bounded smoke test** today, given the gaps in
§ 3 + § 4, is a Claude-only single-call against `S01` to confirm
live API wiring. **This is NOT a v3.1 head-to-head;** it is a
smoke test of the runner itself.

```
# Step 1 — set env var (per-shell, NOT committed):
#   $env:ANTHROPIC_API_KEY = "<key>"          (PowerShell)
#   export ANTHROPIC_API_KEY=<key>            (bash)
# (Do NOT print the key. Do NOT paste it in chat.)

# Step 2 — dry-run plan WITH the key set (still no API call):
dotnet run --project tools/StoryModelBakeoff -- --max-prompts 1 --provider claude

# Step 3 — bounded live smoke (one Claude call, S01 bare-Armenian):
dotnet run --project tools/StoryModelBakeoff -- \
  --run --provider claude --i-understand-live-cost --max-prompts 1
```

**Cost cap:** `--max-prompts 1`. One scenario × one turn × one
provider = **one Claude API call**. With `claude-opus-4-7` at
posted prices and `ClaudeMaxTokens = 1024`, expected spend is
**well under $0.10**, typically $0.02–$0.05.

**What this smoke validates:**
- API wiring (auth, request shape, response parsing).
- Token/latency capture.
- File output under `tools/StoryModelBakeoff/results/<UTCts>/`
  (gitignored).
- That the CLI's pre-execution plan reflects the now-set key.

**What this smoke does NOT validate:**
- v3.1 prompt quality. `S01` uses the **production** system
  prompt with a **bare** "Պատմիր հեքիաթ" turn. The C1–C16
  hardening gates are not exercised; Plan A / Plan D structure
  is not tested.
- OpenAI side. F1.2 cannot run OpenAI live yet (§ 3).

---

## 9. Roadmap update — what slices 1–3 actually need now

The night audit's slice 1 / slice 2 / slice 3 sequence assumed
both providers had live paths and the scenario file was already
v3.1-shaped. The preflight findings above split slice 1 + 2 + 3
more honestly:

| Slice | Goal | Status today |
|---|---|---|
| **1 — keys** | Operator provisions `ANTHROPIC_API_KEY` (+ later `OPENAI_API_KEY`) as env vars. | **Pending operator action.** Both env vars unset. |
| **1.5 — Claude-only smoke** | One Claude API call against `S01` bare-Armenian, validates wiring. | Reachable today as soon as `ANTHROPIC_API_KEY` is set. ~$0.05. |
| **2a — F1.3 OpenAI live path** | Add OpenAI live execution to `Program.cs` (parallel to the Claude path). | **Not yet implemented.** Tool-only slice; no production touch. |
| **2b — F1.3a alternate scenarios** | Add `--scenarios <path>` flag + commit `bakeoff-prompts-v3-1.json` + commit `system-prompt-v3-1.txt`. | **Not yet implemented.** Tool-only slice. |
| **3 — 12-cell run** | Plan A + Plan D × 3 turns × 2 providers via the v3.1 scenarios. | **Blocked on 2a + 2b.** |
| **4 — decision doc** | Branch 1/2/3 from `api-comparison-prep-20260504.md` § 9. | Blocked on slice 3. |

**Slice 1.5 is genuinely independent of slices 2a / 2b** — it
costs nothing structural to run a Claude smoke before the
OpenAI live path lands. The smoke produces the first piece of
API-side evidence the project has, which begins to close the
"Claude.app evidence is not API truth" gap noted across every
strict-capture document.

---

## 10. Final preflight verdict

**No paid API call was issued.** **No production code change.**
**No commit, no push, no stage.**

- Repo state: `main == origin/main == b5efa4a`. Three known
  local-noise items, all expected.
- Bake-off CLI: identified, dry-run works, prompt drift = none.
- Keys: both `ANTHROPIC_API_KEY` and `OPENAI_API_KEY` are
  **unset** in this shell. Backend has a `OpenAI:ApiKey`
  user-secret for runtime; the bake-off CLI does NOT read it.
- Two structural gaps in the runner block the prep doc's
  12-cell matrix today (§ 3 + § 4): OpenAI live path
  unimplemented; current scenarios are bare-Armenian smoke,
  not v3.1 + Plan A + Plan D.
- Slice 1.5 (Claude-only smoke against `S01` for ~$0.05) is
  reachable as soon as `ANTHROPIC_API_KEY` is set. Surfaces
  the first API-truth evidence and validates the runner end-
  to-end.
- Slices 2a + 2b are tool-only changes that unblock the real
  12-cell head-to-head later.

**Recommended next action:** operator decides whether to run
slice 1.5 (Claude-only `S01` smoke) before or after building
out F1.3 (OpenAI live + alternate scenarios). The smoke is
cheap and informative; the F1.3 work is the real unblocker for
the prep doc's matrix.

---

## 11. What NOT to do as a result of this preflight

- **Do NOT run any paid API call** until the operator types an
  explicit GO.
- **Do NOT** assume slice 3 (12-cell run) is reachable today —
  see § 3 + § 4.
- **Do NOT** repurpose the backend's `OpenAI:ApiKey` user-secret
  as the bake-off `OPENAI_API_KEY` env var. Use a separate
  research key.
- **Do NOT** edit `Program.cs`, `bakeoff-prompts.json`, or
  `system-prompt.txt` in this slice — those edits belong to
  F1.3 (a/b), not to a preflight document.
- **Do NOT** commit / stage / push this preflight document
  unless the operator explicitly asks. It is research-only
  and additive.
- **Do NOT** print actual key values anywhere — not in chat,
  not in any committed file, not in any log.

---

## 12. Commands run during preflight

```bash
# repo-state checks (no mutation)
git status -sb
git rev-parse --short HEAD
git rev-parse --short origin/main

# runner identification (read-only)
# (Glob + Grep + Read against tools/StoryModelBakeoff/Program.cs,
#  bakeoff-prompts.json, StoryModelBakeoff.csproj)

# key presence WITHOUT printing values
[ -n "${ANTHROPIC_API_KEY-}" ] && echo "ANTHROPIC: yes" || echo "ANTHROPIC: no"
[ -n "${OPENAI_API_KEY-}" ]    && echo "OPENAI: yes"    || echo "OPENAI: no"

# backend user-secret enumeration WITHOUT printing values
cd backend && dotnet user-secrets list --project src/ArmenianAiToy.Api \
  | sed -E 's/= .*/= <REDACTED>/'

# bake-off dry-run plan (no --run flag, NO API call)
dotnet run --project tools/StoryModelBakeoff -- --max-prompts 1
```

**No paid API call was issued.** **No git mutation.** **No
file under `backend/` modified.**

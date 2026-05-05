# F1.3 — bake-off API runner plan (2026-05-05)

**Status:** **plan-only.** **No production code change.** No
`ChatService` change. No runtime prompt change. No provider switch.
**No paid API call has been run.** No commit, no push, no stage.
The deliverable is this file.

This document plans the **F1.3 slice** of the StoryModelBakeoff
runner, which closes the two structural gaps surfaced by
[`./api-comparison-preflight-20260505.md`](./api-comparison-preflight-20260505.md):

1. **OpenAI live execution path** is missing — F1.2 ships Claude
   live only.
2. **Scenario-file override** is missing — the bake-off can only
   load the bare-Armenian smoke set at
   `tools/StoryModelBakeoff/bakeoff-prompts.json`, not v3.1 +
   Plan A + Plan D shaped scenarios.

These two pieces, plus a v3.1-shaped scenario file pair, are the
preconditions for the 12-cell capture matrix specified in
[`./api-comparison-prep-20260504.md`](./api-comparison-prep-20260504.md).

**Companion files:**
- [`./api-comparison-prep-20260504.md`](./api-comparison-prep-20260504.md) — slice D preflight design (commit `17bda1e`).
- [`./api-comparison-preflight-20260505.md`](./api-comparison-preflight-20260505.md) — preflight findings (untracked at write time).
- [`./night-audit-20260505.md`](./night-audit-20260505.md) — whole-night audit (commit `b5efa4a`).
- [`./writer-prompt-v3-1-hardening-notes-20260504.md`](./writer-prompt-v3-1-hardening-notes-20260504.md) — v3.1 rule set + C14 / C15 / C16 gates.
- [`./writer-prompt-v3-1-plan-a-capture-20260504.md`](./writer-prompt-v3-1-plan-a-capture-20260504.md) — strict-protocol Plan A capture (commit `019177c`).
- [`./writer-prompt-v3-1-plan-d-capture-20260504.md`](./writer-prompt-v3-1-plan-d-capture-20260504.md) — strict-protocol Plan D capture (commit `f20e473`).
- [`../Program.cs`](../Program.cs) — bake-off CLI source.
- [`../bakeoff-prompts.json`](../bakeoff-prompts.json) — current bare-Armenian scenario file.
- [`../system-prompt.txt`](../system-prompt.txt) — bake-off's frozen copy of the production system prompt.

---

## 1. Purpose

Land the smallest, safest changes to the bake-off CLI that turn
the `api-comparison-prep-20260504.md` 12-cell matrix from
**blocked** into **runnable**, while keeping every existing
property of the runner intact:

- **Tool-only** — no `backend/` / production touch.
- **Triple-opt-in for live** — `--run` + `--provider <p>` +
  `--i-understand-live-cost` + scope flag (`--max-prompts N` XOR
  `--allow-full-set`).
- **No new dependency** — BCL only, plain `HttpClient` +
  `System.Text.Json`.
- **No secret in logs / files / DTOs.**
- **Cooperative Ctrl-C** — partial result flush.
- **Atomic file writes** under
  `tools/StoryModelBakeoff/results/<UTCts>/`.

---

## 2. Current runner facts (verified at write time)

The shape these notes mirror, captured from the F1.2 source:

- **Endpoint (Claude):** `https://api.anthropic.com/v1/messages`,
  `anthropic-version: 2023-06-01`.
- **Auth header (Claude):** `x-api-key: <ANTHROPIC_API_KEY>`.
- **Request body (Claude):** `{ model, max_tokens=1024,
  system: <systemPrompt>, messages: [{role, content}, ...] }`.
- **Response parsing (Claude):** sums all `content[].type == "text"`
  blocks into one string; reads `stop_reason`,
  `usage.input_tokens`, `usage.output_tokens`.
- **HTTP timeout:** 60 s, no retry. One call per turn.
- **Per-turn error model:** `http_<status>`, `network`, `parse`,
  `timeout`, `skipped_due_to_prior_error`.
- **Per-turn capture (`TurnResult`):** `UserContent`,
  `AssistantContent`, `StopReason`, `LatencyMs`, `InputTokens`,
  `OutputTokens`, `ErrorKind`, `ErrorMessage` (truncated 500
  chars).
- **Per-scenario capture (`ScenarioResult`):**
  `Scenario`, `List<TurnResult>`. **One earlier-turn failure
  marks the rest of that scenario `skipped_due_to_prior_error`.**
- **Run capture (`RunResult`):** `StartedUtc`, `CompletedUtc`,
  `InterruptedUtc?`, `Claude` (one provider record),
  `BakeoffPromptSha`, `ProductionPromptSha?`, `DriftDetected`,
  `Scenarios`.
- **Output files** (atomic, written to
  `results/<UTCts>/`): `results.json`, `summary.json`,
  `review.md`.
- **JSON encoder:** `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`
  so Armenian script is preserved literally rather than
  `\u`-escaped.
- **Live-path guard chain (Program.cs:163-199):**
  1. Reject non-Claude provider → exit 2.
  2. Reject missing `--i-understand-live-cost` → exit 1.
  3. Reject missing scope flag → exit 1.
  4. Reject missing `ANTHROPIC_API_KEY` → exit 1.
- **Dry-run plan output:** provider matrix (key presence by
  env-var name only, never values), scenario list with first-
  turn previews, estimated calls per provider, prompt-drift
  verdict.
- **Production prompt drift check:** SHA-256 of the bake-off
  `system-prompt.txt` (header line stripped) vs the production
  `appsettings.json`'s `SystemPrompt` field. Currently equal:
  `54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`.

These properties are **load-bearing**. F1.3 must preserve all of
them; OpenAI and the scenario override are **additive**.

---

## 3. F1.3 scope

F1.3 is split into two complementary tool-only commits plus one
content-only commit:

- **F1.3a — OpenAI live execution path** (code-only).
- **F1.3b — `--scenarios <path>` override + `--system-prompt
  <path>` override + alternate scenario validation** (code-
  only).
- **F1.3c — v3.1 scenario-file pair**
  (`bakeoff-prompts-v3-1.json` + `system-prompt-v3-1.txt`)
  (data-only; depends on F1.3a + F1.3b).

The split mirrors the existing F1.1 → F1.2 cadence: a structural
slice (provider) and a config slice (scenarios), each
independently shippable, no production touch in any of them.

---

## 4. Out of scope for F1.3

Deferred and explicitly NOT touched here:

- **Production runtime / `ChatService`** — frozen. Story
  Director is research tooling, not runtime.
- **Provider switch decision** — the API run is *evidence*
  for the decision, not the decision itself. Slice 4
  (decision doc) is a separate slice from F1.3.
- **Gemini live path** — `gemini-2.5-pro` env / model slot is
  already wired in the dry-run matrix; F1.3 leaves the live
  path deferred per the existing F1.2 contract.
- **`AAT_LOCAL_*`** — reserved; no live path planned.
- **Backend tests / parser audit / runtime integration design
  doc (slice E)** — separate roadmap items.
- **Cost-in-USD computation** — operator multiplies tokens × posted
  price. CLI will not bake in pricing tables that go stale.
- **JWT, audit, parent-dashboard, audio, retention** — every
  backend area stays out of scope.
- **Speech / TTS / STT / hardware / firmware** — out of scope.
- **Native-Armenian review** of the v3.1 captures — separate
  content slice.
- **Character name bank cleanup** — separate content slice.

---

## 5. F1.3a — OpenAI live execution path (design)

### 5.1 Where in `Program.cs`

- Hoist the existing `RunLiveClaudeAsync(...)` to a more
  generic `RunLiveAsync(...)` shape that takes a per-provider
  `IProviderCallStrategy` (or equivalent thin adapter) and a
  `ResolvedProvider`. **Or** — minimal-diff alternative — keep
  `RunLiveClaudeAsync` as-is and add a parallel
  `RunLiveOpenAIAsync(...)` plus a small dispatch in `Main`
  based on `--provider`.
- **Recommendation: minimal-diff parallel path.** F1.3 is the
  first multi-provider slice; over-abstracting now is more
  surface area than two near-twin functions. A `RunLiveAsync`
  refactor is a F1.4 candidate after the matrix has been run
  at least once.
- New constants near the existing Anthropic ones:
  ```csharp
  private const string OpenAIEndpoint =
      "https://api.openai.com/v1/chat/completions";
  private const int OpenAIMaxOutputTokens = 1024;  // mirrors ClaudeMaxTokens
  ```
- Live-path guard chain in `Main` extends from "Claude only"
  to a per-provider switch:
  ```csharp
  if (!ProviderHasLivePath(providerArg))
  {
      Console.Error.WriteLine($"Live execution for '{providerArg}' is not implemented.");
      return 2;
  }
  ```
  with `ProviderHasLivePath` returning `true` for `claude` and
  `openai`, `false` for `gemini` / `local` / `all`. The
  `--provider all` rejection stays — head-to-head requires
  explicit per-provider runs in F1.3 (one provider at a time
  for cost discipline). Multi-provider parallel can land in a
  later slice.

### 5.2 Request / response shape (OpenAI Chat Completions)

**Request:**
```
POST https://api.openai.com/v1/chat/completions
Authorization: Bearer <OPENAI_API_KEY>
Content-Type: application/json

{
  "model":      "<model>",
  "max_tokens": 1024,                     // or "max_completion_tokens" — see § 5.4
  "messages": [
    { "role": "system",    "content": "<systemPrompt>" },
    { "role": "user",      "content": "<turn 1 user>" },
    { "role": "assistant", "content": "<turn 1 assistant>" },
    { "role": "user",      "content": "<turn 2 user>" }
  ],
  "temperature": 0.7                     // optional, see § 5.5
}
```

**Differences from Claude that the adapter must handle:**

| Aspect | Claude | OpenAI |
|---|---|---|
| Auth header | `x-api-key: <key>` + `anthropic-version: 2023-06-01` | `Authorization: Bearer <key>` |
| System prompt | top-level `system` field | first `messages[]` entry with `role: "system"` |
| Output cap | `max_tokens` | `max_tokens` (legacy) OR `max_completion_tokens` (newer) — see § 5.4 |
| Response text | `content[].type=="text".text` array | `choices[0].message.content` (string) |
| Token usage | `usage.input_tokens` / `usage.output_tokens` | `usage.prompt_tokens` / `usage.completion_tokens` |
| Finish marker | `stop_reason` | `choices[0].finish_reason` |

**Response parsing rule:** read `choices[0].message.content`;
read `choices[0].finish_reason`; read `usage.prompt_tokens` and
`usage.completion_tokens`. Empty choices array → `parse` error.
Multiple choices → take `[0]` only (n=1 default). Empty content
→ empty string with the recorded finish_reason.

### 5.3 Env vars and default model

| Variable | Purpose | Default if unset |
|---|---|---|
| `OPENAI_API_KEY` | bearer key | live path errors out (mirror Claude behavior) |
| `OPENAI_BAKEOFF_MODEL` | override model id | `gpt-4o` (already wired in F1.1 dry-run) |
| `OPENAI_ORG_ID` | optional `OpenAI-Organization` header | omit header if unset |

`OPENAI_ORG_ID` is **additive** — if unset, no
`OpenAI-Organization` header is sent. Operators with single-
account access do not need it.

### 5.4 `max_tokens` vs `max_completion_tokens`

OpenAI deprecated `max_tokens` in favor of
`max_completion_tokens` for newer reasoning-aware models
(`o1`, `o3`, GPT-5+). For `gpt-4o` and earlier classic models
both still work, with `max_tokens` being a soft alias.

**Recommendation for F1.3a:** send `max_tokens` (mirrors the
Claude path's `max_tokens` constant) **and** keep an
`OpenAI:UseMaxCompletionTokens` env-var-style override —
`OPENAI_MAX_TOKENS_FIELD` accepting `max_tokens` (default) or
`max_completion_tokens`. If a future operator targets an `o1`-
class model and the API responds with `unsupported_value`
on `max_tokens`, the override flips them onto the new field
without code change.

### 5.5 Temperature, top_p, n, response_format

- **Temperature:** the Claude path sends none (Anthropic
  default = 1.0). To match, the OpenAI path sends none either
  (default 1.0 for `gpt-4o`). **Operators wanting a fixed
  decoding** can override via `OPENAI_TEMPERATURE` env var
  (numeric, optional). Out of band: temperature 0.7 in the
  prep doc's optional 24-cell variance pass — that pass would
  set the env var. Not the F1.3 default.
- **`top_p`:** not sent. Default behavior.
- **`n`:** not sent. Single completion per call.
- **`response_format`:** not sent. The bake-off explicitly
  expects free-form text (the writer prompt's job is format
  enforcement, not the decoder's).
- **`seed`:** not sent. Reproducibility-by-seed is a future
  enhancement; the current Claude path also doesn't seed.

### 5.6 Timeout and retry

- **Timeout:** mirror Claude — `RequestTimeoutSeconds = 60`,
  `HttpClient.Timeout = 60s`. No retry.
- **Why no retry:** the current bake-off design chooses
  honest first-call latency over success-rate inflation.
  Retries would conflate provider 5xx behavior with
  networking flakiness in the captured numbers. If a future
  retry policy is needed, it must be an explicit flag
  (`--retries N`) and recorded per-turn.

### 5.7 Token usage extraction

```csharp
if (root.TryGetProperty("usage", out var usage)
    && usage.ValueKind == JsonValueKind.Object)
{
    if (usage.TryGetProperty("prompt_tokens", out var pt)
        && pt.ValueKind == JsonValueKind.Number)
    {
        inputTokens = pt.GetInt32();
    }
    if (usage.TryGetProperty("completion_tokens", out var ct)
        && ct.ValueKind == JsonValueKind.Number)
    {
        outputTokens = ct.GetInt32();
    }
}
```

The `TurnResult.InputTokens` / `OutputTokens` slots stay the
same — only the source field names differ between providers.

### 5.8 Latency capture

Identical to Claude — `Stopwatch.StartNew()` outside the
HTTP call, `sw.ElapsedMilliseconds` recorded on every code
path (success, http_<status>, parse, timeout, network). No
subdivision into upload-vs-server-vs-download in F1.3.

### 5.9 Error handling and error kinds

Reuse the existing kinds verbatim (so `summary.json` parsers
do not need provider-specific cases):

- `http_<status>` — non-2xx response.
- `network` — `HttpRequestException` or other transport failure.
- `parse` — JSON parse failure or missing required field.
- `timeout` — `TaskCanceledException` from `HttpClient.Timeout`.
- `skipped_due_to_prior_error` — same scenario-level halt as
  Claude.

OpenAI returns structured JSON error bodies on 4xx/5xx
(`{ "error": { "message", "type", "code" } }`). The truncated
`ErrorMessage` field captures up to 500 chars of the raw
body — same as Claude — so error-type detail is preserved
without the runner having to schema-parse error responses.

### 5.10 Secret-handling rules

- **Never log the key value.** The Claude path sets
  `x-api-key` via `DefaultRequestHeaders.Add(...)`; the
  OpenAI path will set `Authorization` via the same
  collection. **Do not log `http.DefaultRequestHeaders` or
  the request `HttpRequestMessage`** — `ToString()` on
  those collections includes the auth header value.
- **Never persist the key to file.** `RunResult` /
  `ScenarioResult` / `TurnResult` carry no key field. The
  evidence files (`results.json`, `review.md`,
  `summary.json`) capture provider name + model + counts +
  output text only.
- **Never echo the key to stdout.** The pre-execution plan
  prints `key present: yes/no` (boolean only) per the
  existing dry-run pattern.
- **Guard the "expose env vars" temptation.** Do not add a
  `--debug-print-headers` flag or similar; if a future
  diagnostic is needed, redact the auth value before printing.

### 5.11 Pre-execution plan changes

The dry-run plan output (Section "Live execution:") needs one
additive line per provider that has a live path:

```
Live execution:
  F1.3 supports Claude and OpenAI. Re-run with
  --run --provider <claude|openai> --i-understand-live-cost
  and either --max-prompts N or --allow-full-set.
  ANTHROPIC_API_KEY (claude)  / OPENAI_API_KEY (openai)
  must be set in your environment.
  Gemini / all live execution remain deferred to a later F1 slice.
```

The estimated-calls-per-provider table already iterates over
`liveProviders` — no change needed beyond `OpenAI` becoming
`live-ready` instead of "skipped (env unset)" once the key is
present.

---

## 6. F1.3b — scenario-file override (design)

### 6.1 Proposed CLI flags

| Flag | Default | Purpose |
|---|---|---|
| `--scenarios <path>` | `bakeoff-prompts.json` next to binary | Path to alternate scenarios JSON. Relative or absolute. |
| `--system-prompt <path>` | `system-prompt.txt` next to binary | Path to alternate system prompt. **Required when `--scenarios` is supplied** if the alternate scenarios are designed for a different system prompt (e.g. v3.1). |

Both are **optional**. Defaulting to the existing pair
preserves byte-for-byte F1.2 behavior when neither is supplied.

### 6.2 Validation rules

- **Path resolution:** resolve relative paths against current
  working directory, not `AppContext.BaseDirectory`. The
  existing `bakeoff-prompts.json` lives next to the binary
  (CopyToOutputDirectory); user-supplied paths typically live
  in source.
- **File must exist** (else exit 1 with clear error).
- **JSON parse must succeed** (else exit 1, message includes
  the path).
- **`ValidateScenarios(...)`** runs unchanged — the existing
  validator already enforces non-empty `id` / `category` /
  `turns`, role discipline, and content non-emptiness.
- **System prompt: header-stripping rule unchanged.** The
  leading `# ...` source-of-truth comment is stripped before
  hashing and before sending to the model, same as today.
- **Drift check**: when `--system-prompt <path>` is supplied
  pointing at a non-default file, the production-prompt
  comparison continues against
  `backend/src/ArmenianAiToy.Api/appsettings.json` — but the
  drift verdict is reframed: "drifted (intentional — alternate
  prompt)" vs "drifted (unintentional)". A new field
  `alternateSystemPromptInUse: bool` lands in the result JSON
  to disambiguate downstream.

### 6.3 Backward compatibility

- F1.2 invocations (no `--scenarios`, no `--system-prompt`)
  behave **exactly** as before. Same paths, same hashes, same
  output files.
- Old `results.json` consumers see `alternateSystemPromptInUse`
  default to `false` when unsupplied; STRICT readers should
  treat the field as additive.

### 6.4 Output records the source

Every `results.json` / `summary.json` gets two new fields under
`promptIdentity`:

```json
"promptIdentity": {
  "bakeoffPromptSha256": "...",
  "productionPromptSha256": "...",
  "driftDetected": true,
  "alternateSystemPromptInUse": true,
  "scenariosPath": "bakeoff-prompts-v3-1.json",
  "scenariosSha256": "<sha256-of-the-loaded-scenarios-file>",
  "systemPromptPath": "system-prompt-v3-1.txt"
}
```

Hash of the scenarios file lets future audits reproduce the
exact prompt set even if the file is later edited or moved.

### 6.5 Pre-execution plan changes

The provider-matrix block continues unchanged. The "Prompt
identity:" block adds two lines after the existing two:

```
Prompt identity:
  bake-off  sha256 = ...
            path   = ...
  production sha256 = ...
            path   = ...
  scenarios sha256 = ...
            path   = bakeoff-prompts-v3-1.json
  drift   = drifted (intentional — alternate prompt)
```

---

## 7. F1.3c — v3.1 scenario-file pair (design)

### 7.1 Files to add

- `tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json` —
  scenarios shaped for the v3.1 writer prompt + Plan A + Plan D.
- `tools/StoryModelBakeoff/system-prompt-v3-1.txt` —
  v3.1 writer system prompt **with** the header line
  `# Source: tools/StoryModelBakeoff/evaluations/writer-prompt-v3-1-hardening-notes-20260504.md`.
  The bake-off's `StripCommentHeader(...)` strips the leading
  `#`-line so the SHA-256 and the model input both reflect the
  body only.

Both files must be added to `StoryModelBakeoff.csproj`'s
`CopyToOutputDirectory=PreserveNewest` itemgroup so the binary
can find them under `AppContext.BaseDirectory` when the runner
defaults to the alternate set in a future F1.3+ slice. F1.3c as
written has them resolved by `--scenarios <path>` /
`--system-prompt <path>` against `cwd`, so the csproj edit is
not strictly required in F1.3c. Recommendation: defer the
csproj edit until a slice that wants the v3.1 pair to be the
*default* (which we should NOT do until evidence supports
v3.1 as the canonical prompt).

### 7.2 Deriving Plan A scenarios

From [`./writer-prompt-v3-1-plan-a-capture-20260504.md`](./writer-prompt-v3-1-plan-a-capture-20260504.md)
§ 2 (plan source) and § 4–6 (turn prompts):

- **`PA-T1`** — Plan A age-4-simple #17, Turn 1. The user-
  facing turn is the writer prompt with the
  `BREAK-GLASS CHOICE BLOCK` substituted for Plan A's `choiceA`
  / `choiceB` and the turn-1-specific instructions (length
  90–130, place-stem rule C16 etc.).
- **`PA-T2`** — Turn 2. Two-sub-turn shape: the model's Turn 1
  output is fed back as `assistant`; the user message is the
  Turn 2 writer prompt (`SELECTED_CHOICE = Ա` substituted, length
  70–110, BREAK-GLASS choices for Turn 2 supplied).
- **`PA-T3`** — Turn 3. Three-sub-turn shape: Turn 1 + Turn 2
  outputs as `assistant`; user message is Turn 3 closure prompt
  (no choice block, length 70–100, smallProblem resolution
  required, ends `Վերջ։`).

**Each "scenario" in `bakeoff-prompts-v3-1.json` is one
complete 3-turn conversation, not three independent scenarios.**
The bake-off's existing `Scenario.Turns` shape supports this
directly (S07 in the current `bakeoff-prompts.json` already
demonstrates a 2-turn conversation).

### 7.3 Deriving Plan D scenarios

Same shape from
[`./writer-prompt-v3-1-plan-d-capture-20260504.md`](./writer-prompt-v3-1-plan-d-capture-20260504.md).
Plan D age-7-richer: hero=մողես, friend=բադիկ, place=հին
կամուրջ, etc. Same 3-turn structure with Plan D's specific
BREAK-GLASS choices and closure budgets (100–130 words age-7).

### 7.4 Scenario count

Minimum F1.3c shape: **2 scenarios** (`PA`, `PD`), each with 3
turns. **6 turns × 2 providers = 12 calls** when running both
providers — matches the prep doc's 12-cell matrix exactly.

Optional additional scenarios:
- `PA-Ա` and `PA-Բ` — branch coverage from Turn 1's choice
  set (only one is needed for the matrix; both is variance
  insurance).
- Same for Plan D.

**Recommendation:** ship exactly `PA` + `PD` (single branch
each) in F1.3c. Variance scenarios are optional and can land
in a later slice — they double cost without doubling
informational value on the first run.

### 7.5 Placeholders — runner-expanded vs pre-expanded

The Turn 2 and Turn 3 prompts in the v3.1 capture docs use
`{{TURN_1_OUTPUT}}` / `{{TURN_2_OUTPUT}}` placeholders during
*operator-driven Claude.app capture*, but the **bake-off
runner already feeds prior assistant outputs into the
`messages[]` array** (per the existing rolling-history loop).

**Decision: pre-expand nothing.** The v3.1 scenario JSON should
contain the **literal Turn 2 / Turn 3 user prompts** without any
`{{...}}` placeholder. The runner's existing
`messages.Add(("assistant", result.AssistantContent))`
in `RunLiveClaudeAsync` already supplies prior turns via the
provider's chat history mechanism — **the same mechanism the
production toy will use**.

This is a structural advantage of the API path over Claude.app:
the model sees prior turns natively rather than via paste.

### 7.6 Header-stripping for v3.1 system prompt

The header line in `system-prompt-v3-1.txt` should be:

```
# Source: tools/StoryModelBakeoff/evaluations/writer-prompt-v3-1-hardening-notes-20260504.md (rules A–E + gates C14/C15/C16)
```

`StripCommentHeader(...)` strips it before the SHA-256 hash
and before the message body, so the model never sees the
comment.

### 7.7 No production prompt drift confusion

The drift verdict will read **"drifted (intentional —
alternate prompt)"** when `--system-prompt system-prompt-
v3-1.txt` is in effect. This is the desired signal: the v3.1
prompt is *deliberately* different from production. The
runner must not treat this drift as a warning.

---

## 8. F1.3 — output / evidence design

### 8.1 `results.json` shape (F1.3 superset)

```json
{
  "schemaVersion": 2,
  "runStartedUtc": "...",
  "runCompletedUtc": "...",
  "runInterruptedUtc": null,
  "promptIdentity": {
    "bakeoffPromptSha256": "...",
    "productionPromptSha256": "...",
    "driftDetected": true,
    "alternateSystemPromptInUse": true,
    "scenariosPath": "bakeoff-prompts-v3-1.json",
    "scenariosSha256": "...",
    "systemPromptPath": "system-prompt-v3-1.txt"
  },
  "providers": [
    { "name": "claude", "model": "claude-opus-4-7" }
  ],
  "scenarios": [
    {
      "id": "PA",
      "category": "v3-1-plan-a-age-4-simple-17",
      "turns": [...],
      "results": {
        "claude": {
          "model": "claude-opus-4-7",
          "turns": [
            {
              "userContent": "...",
              "assistantContent": "...",
              "stopReason": "end_turn",
              "latencyMs": 4231,
              "tokenUsage": { "input": 412, "output": 117 },
              "errorKind": null,
              "errorMessage": null
            }
          ]
        }
      }
    }
  ]
}
```

`schemaVersion` bumps from 1 → 2 because of the additive
`alternateSystemPromptInUse` / `scenariosPath` /
`scenariosSha256` / `systemPromptPath` fields. Old (F1.2)
readers should treat schemaVersion ≥ 2 as additive-only.

### 8.2 `summary.json` shape

`providers["openai"]` becomes a peer of `providers["claude"]`
when the OpenAI path runs. **One run = one provider** in F1.3
(no `--provider all`); to compare both, the operator runs the
matrix twice (once per provider) and the slice 4 decision doc
combines the two `summary.json` files.

### 8.3 `review.md` shape

Per-scenario sections continue, with an additional `### openai
(<model>)` block alongside `### claude (<model>)` when both
providers' results files are merged in slice 4. F1.3 itself
does not need a merge utility; the slice-4 decision doc can
join two single-provider runs by hand.

### 8.4 Cost computation

The existing `summary.json` exposes `tokenUsage.input` and
`tokenUsage.output` per provider. **The CLI does not multiply
by USD prices.** Operator computes:

```
USD = (input_tokens / 1_000_000) × <price-per-million-input>
    + (output_tokens / 1_000_000) × <price-per-million-output>
```

…and records the result in the slice-4 decision doc. Pricing
tables go stale; embedding them in the runner risks misreporting
cost a year later.

---

## 9. Tests and validation

The bake-off CLI itself has **no test project** today —
`tools/StoryModelBakeoff/` is a single console with no test
ProjectReference. F1.3 should NOT introduce one (it would
break the "BCL-only, no PackageReference" contract).

Validation is therefore **manual + dry-run-driven**:

### 9.1 No-key dry-run smoke (F1.3a + F1.3b)

```
dotnet run --project tools/StoryModelBakeoff
```

Expected: provider matrix shows both `claude` and `openai` as
"skipped (env unset)" if neither key is present. **Estimated
calls: 0.** No network call. Safe to run anywhere.

### 9.2 Missing-key live attempt (F1.3a)

```
dotnet run --project tools/StoryModelBakeoff -- \
  --run --provider openai --i-understand-live-cost --max-prompts 1
```

With `OPENAI_API_KEY` unset, the runner must error out with:

```
OPENAI_API_KEY is not set. Live OpenAI execution requires the
env var to be present and non-empty.
```

…and exit code 1. **No network call.**

### 9.3 Scenario-file override smoke (F1.3b)

```
dotnet run --project tools/StoryModelBakeoff -- \
  --scenarios tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json \
  --system-prompt tools/StoryModelBakeoff/system-prompt-v3-1.txt \
  --max-prompts 1
```

Expected: dry-run plan now shows `Scenarios: 1` from the v3.1
file, the alternate system-prompt SHA-256 is reported, and
`drift = drifted (intentional — alternate prompt)`. No network
call.

### 9.4 OpenAI parser smoke (F1.3a) — no paid call

If a future slice adds an offline JSON fixture
(`tests/openai-fixture-200.json` under a tools-test path),
the parser can be exercised against it without an API call.
**F1.3 itself does not ship this fixture** to keep diff
minimal; it is mentioned here as a F1.4+ improvement.

### 9.5 Live single-call smoke (post-F1.3a, gated on operator GO)

After explicit operator GO and after `OPENAI_API_KEY` is set:

```
dotnet run --project tools/StoryModelBakeoff -- \
  --run --provider openai --i-understand-live-cost --max-prompts 1
```

Expected: one OpenAI call against `S01` bare-Armenian, ~$0.01–
$0.05 spend, results files written to
`tools/StoryModelBakeoff/results/<UTCts>/`. **Not a v3.1
verdict; smoke only.**

### 9.6 No paid call before explicit GO

Every command in this section is either dry-run (no key
required) or runs only after operator GO + key provisioning.
**F1.3 implementation must not include any test step that
requires a paid call.**

---

## 10. Risks

### 10.1 OpenAI response schema drift

`gpt-4o`'s Chat Completions response shape has been stable
since 2024-04. Newer reasoning models (`o1`, `o3`, GPT-5+)
introduce additional response fields (`reasoning_tokens`,
internal `reasoning` blocks). The F1.3 parser reads only
`choices[0].message.content`, `usage.prompt_tokens`,
`usage.completion_tokens`, `choices[0].finish_reason` — fields
guaranteed across all current and known-future Chat Completions
shapes. Lower risk.

**Mitigation:** parser is permissive (`TryGetProperty`
everywhere); missing field → null token count, empty content,
no crash.

### 10.2 Cost overrun

Default `OpenAI:Model = gpt-4o`. Posted price ≈ $2.50 / 1M
input + $10 / 1M output. A 12-cell matrix with ~500 input
tokens × ~150 output tokens × 12 calls = ~6k input + ~1.8k
output ≈ $0.03 total per provider. **Bounded.**

`claude-opus-4-7` ≈ $15 / 1M input + $75 / 1M output. Same
matrix ≈ $0.20 total. Still bounded.

**Mitigation:** `--max-prompts` cap is mandatory unless
`--allow-full-set` is explicit. The 12-cell matrix is well
within cost discipline. Variance pass (24 cells at
temperature 0.7) doubles spend; still under $0.50 per
provider.

### 10.3 Scenario prompt size

The v3.1 Turn 3 prompt is the longest — the writer prompt
body (~3 KB) + Plan plan (~1 KB) + 2 prior turn outputs
(~1 KB each) ≈ 6 KB ≈ ~1500–1800 input tokens per call.
Within both providers' context windows by orders of
magnitude. Lower risk.

### 10.4 Provider differences in instruction-following

OpenAI's `gpt-4o` is well known to follow length budgets less
strictly than Claude under the same prompt. This is **the
question the comparison is meant to answer**, not a runner
risk. The runner's job is to capture both honestly.

### 10.5 System-prompt-v3-1 — duplicate writer prompt vs self-contained user prompts

Two design options:

- **Option A (recommended): writer prompt in the SYSTEM slot.**
  `system-prompt-v3-1.txt` contains the v3.1 writer prompt
  body (rules A–G + gates C14/C15/C16). User turns are
  short — just plan source + Turn 1/2/3 instructions. Pros:
  matches the production architecture (writer prompt =
  system prompt); the runner already handles per-provider
  system-slot differences.
- **Option B: writer prompt in EACH user turn.** Self-
  contained per-turn prompts, no system prompt. Pros: closer
  to the operator-driven Claude.app capture flow; no
  ambiguity about which slot the rules live in. Cons: the
  rules get re-sent on every turn (3× the input tokens for
  no informational gain); diverges from production
  architecture.

**Recommendation: Option A.** The comparison's purpose is to
predict production runtime; therefore mirror production's
slot layout (rules in system; user turns are just user input
+ minimal per-turn directives). The runner already routes
the system slot correctly per provider (Claude top-level,
OpenAI `messages[0]`).

### 10.6 Existing CLI architecture limitations

- **No multi-provider single-run.** F1.3 deliberately keeps
  `--provider <name>` single-valued for live runs (cost
  discipline). Side-by-side requires running twice.
- **No retry / circuit / classification.** Honest first-call
  numbers; future slice can layer this.
- **No streaming.** Buffered response only. Streaming would
  affect latency capture model.
- **No log redaction utility.** The `Truncate(...)` helper
  truncates length but not key-shaped tokens. A future
  slice could add a key-pattern redactor; F1.3 relies on
  "never log headers / never log keys" discipline.

### 10.7 SDK temptation

OpenAI's official .NET SDK (`OpenAI` 2.x package, `Azure.AI.OpenAI`)
is mature. **Do NOT add it.** The runner contract is
"BCL-only, no PackageReference." Plain `HttpClient` +
`System.Text.Json` mirrors the existing Claude path and keeps
the bake-off insulated from SDK updates that could affect
captured numbers.

---

## 11. Recommended commit slices

In order, smallest to largest, each independently shippable
and reversible:

### Commit 1 — F1.3a — OpenAI live execution path (code-only)

**Files touched:**
- `tools/StoryModelBakeoff/Program.cs` — add OpenAI constants,
  parallel `RunLiveOpenAIAsync(...)`, `CallOpenAIOnceAsync(...)`,
  per-provider dispatch in `Main`'s live-path guard chain;
  extend the dry-run pre-execution-plan output by one line.

**Risk:** medium-low. Tool-only. No backend touch. No
PackageReference. ~250 LoC additive (mirroring the existing
~250-LoC Claude path).

**Verification before commit:**
- `dotnet build tools/StoryModelBakeoff` clean.
- Dry-run with neither key set → both providers "skipped".
- Live attempt with key unset → exit 1 with clear message,
  no network call.
- `git diff --stat` shows exactly one file changed
  (`Program.cs`).

**Suggested commit message:**
`tools(bakeoff): add OpenAI live execution path (F1.3a)`

### Commit 2 — F1.3b — scenario / system-prompt overrides (code-only)

**Files touched:**
- `tools/StoryModelBakeoff/Program.cs` — add `--scenarios <path>`
  + `--system-prompt <path>` flag handling, path resolution
  against cwd, scenarios SHA-256 capture,
  `alternateSystemPromptInUse` field plumbed into
  `promptIdentity` in `BuildResultsJson`.

**Risk:** low. Tool-only. Additive; old invocations preserved.
~80 LoC.

**Verification before commit:**
- F1.2-shape dry-run (no flags) → byte-identical output to
  pre-commit (verified by diff against `git stash` of
  pre-commit results.json).
- Override dry-run with v3.1 pair → reports correct
  `scenarios sha256` and `drift = drifted (intentional)`.

**Suggested commit message:**
`tools(bakeoff): add scenario and system-prompt overrides (F1.3b)`

### Commit 3 — F1.3c — v3.1 scenario-file pair (data-only)

**Files touched:**
- new `tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json` —
  PA + PD scenarios.
- new `tools/StoryModelBakeoff/system-prompt-v3-1.txt` — v3.1
  writer prompt body.
- optionally: `tools/StoryModelBakeoff/README.md` — short
  paragraph describing the override mechanism.

**Risk:** very low. Content-only. No code touch. Dependency
on commits 1 + 2 (the override flag is needed to consume
these files).

**Verification before commit:**
- `dotnet run --project tools/StoryModelBakeoff -- --scenarios bakeoff-prompts-v3-1.json --system-prompt system-prompt-v3-1.txt`
  dry-run shows 2 scenarios with the expected first-turn
  previews and reports the right SHA-256.
- Validator passes (scenarios JSON has all required fields
  per existing `ValidateScenarios(...)`).

**Suggested commit message:**
`tools(bakeoff): add v3.1 plan a + plan d scenario pair (F1.3c)`

### What to defer (post-F1.3)

- **Slice 1.5 — Claude-only smoke against S01 bare-Armenian**
  (validates F1.3a end-to-end with one ~$0.05 call). Can run
  immediately after commit 1, before commits 2 + 3.
- **Slice 2 — Claude-only run against the v3.1 scenarios**
  (after commits 1–3; ~$0.20 if 6-call max-prompts).
- **Slice 3 — OpenAI run against the v3.1 scenarios**
  (after commits 1–3; ~$0.05).
- **Slice 4 — decision doc** combining the two
  `summary.json` files + the strict-protocol Claude.app
  references.
- **F1.4 — refactor `RunLiveClaudeAsync` /
  `RunLiveOpenAIAsync` into a `RunLiveAsync(provider)` shape**
  once both providers are stable.
- **F1.5 — optional `--retries N` flag and circuit
  breaker** if the matrix runs surface flakiness.

---

## 12. Exact commands to run later (DO NOT RUN UNTIL EXPLICIT GO)

After commits 1–3 land and **after explicit operator GO + key
provisioning** per
[`./api-comparison-preflight-20260505.md`](./api-comparison-preflight-20260505.md)
§ 6:

```
# Step 1 — verify dry-run with both keys set (no API call):
dotnet run --project tools/StoryModelBakeoff -- \
  --scenarios tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json \
  --system-prompt tools/StoryModelBakeoff/system-prompt-v3-1.txt

# Step 2 — Claude run against v3.1 (~$0.20, 6 calls = 2 scenarios × 3 turns):
dotnet run --project tools/StoryModelBakeoff -- \
  --run --provider claude --i-understand-live-cost --allow-full-set \
  --scenarios tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json \
  --system-prompt tools/StoryModelBakeoff/system-prompt-v3-1.txt

# Step 3 — OpenAI run against v3.1 (~$0.05, 6 calls):
dotnet run --project tools/StoryModelBakeoff -- \
  --run --provider openai --i-understand-live-cost --allow-full-set \
  --scenarios tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json \
  --system-prompt tools/StoryModelBakeoff/system-prompt-v3-1.txt

# Step 4 — slice-4 decision doc combines the two
# tools/StoryModelBakeoff/results/<ts>/summary.json files. Manual.
```

**Total bounded spend across both providers:** ≈ $0.25.
**Total wall-clock:** ≈ 2–4 minutes per provider.

---

## 13. What NOT to do as a result of this plan

- **Do NOT implement F1.3 from this document alone.** The
  plan is for review; the implementation lands in commits
  1–3 only after the operator approves.
- **Do NOT add an OpenAI / Anthropic SDK PackageReference.**
  The bake-off contract is BCL-only.
- **Do NOT touch backend, ChatService, runtime prompts,
  appsettings, csproj (beyond optional CopyToOutputDirectory),
  tests, seed bank, name bank, generator, validator,
  parent.html, frontend, audio, retention, JWT, audit, or
  speech / TTS / STT.** All out of scope.
- **Do NOT promote the v3.1 prompt to production.** F1.3c
  ships the v3.1 prompt as a *bake-off override file*; it
  does not become `system-prompt.txt` (the production-
  drift check baseline) or `appsettings.json`'s
  `SystemPrompt`.
- **Do NOT ship `--provider all` for live execution.** Cost
  discipline + multi-provider drift confusion.
- **Do NOT bake USD prices into the runner.** Stale tables
  misreport cost over time.
- **Do NOT log keys, headers, or `HttpRequestMessage`
  contents.** Use `bool keyPresent` only.
- **Do NOT push or commit this plan document automatically.**
  The operator decides whether the plan lands as evidence
  before the implementation slices.
- **Do NOT run any paid API call** until the operator types
  GO. The bake-off CLI's existing triple-opt-in still
  applies; the plan does not loosen it.

---

## 14. Honesty notes

- **No code change has been made.** This document is plan-
  only.
- **No paid API call has been issued.** The bake-off has not
  been run with `--run` in this slice.
- **Architectural recommendations** (parallel
  `RunLiveOpenAIAsync` instead of an early generic refactor;
  Option A system-slot layout for the v3.1 prompt) are
  judgment calls — the operator can override either before
  implementation lands.
- **Cost estimates** are based on posted 2026-05 prices
  (`gpt-4o` ≈ $2.50/$10 per M tokens, `claude-opus-4-7`
  ≈ $15/$75 per M tokens) and are subject to provider-side
  changes.
- **Path resolution:** the `--scenarios` / `--system-prompt`
  flags resolve against `cwd` deliberately, since user-
  supplied paths typically live in source — but this is a
  judgment call; resolving against `AppContext.BaseDirectory`
  (next to the binary) is also defensible if the alternate
  files get added to `CopyToOutputDirectory`.
- **Schema bump from 1 → 2** in `results.json` is additive
  and old readers continue to work, but the bump is opinion;
  staying at `schemaVersion: 1` is also defensible.

If any of the above turns out to disagree with operator
intent, the path is to amend this plan via a follow-up
slice, not to amend or force-push.

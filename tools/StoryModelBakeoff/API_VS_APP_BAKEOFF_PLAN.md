# API vs App Bakeoff Plan

**Status:** design / evidence-only. No production code changes.
No runtime model switch. No prompt rewrite in `ChatService`. No
new provider integrations beyond what's already in
`Program.cs` (Claude live in F1.2; OpenAI / Gemini API still
deferred).

**Companion files:**
- [`README.md`](./README.md) — tool usage and slice status.
- [`bakeoff-prompts.json`](./bakeoff-prompts.json) — current frozen
  12-scenario prompt set used by the F1.2 Claude live runner. The
  10-case set below is a *superset proposal*; it does NOT
  immediately replace the F1.2 set, and any future change to that
  file lands as its own commit.
- [`samples/`](./samples) and [`evaluations/`](./evaluations) —
  manual app evidence captured to date (Claude / Gemini consumer
  app samples, 2026-05-01).

---

## 1. Purpose

We have two kinds of "is this model good at Armenian heqiats?"
evidence and they answer different questions.

- **Consumer app output** (claude.ai, gemini.google.com,
  chatgpt.com) shows a model's *possible ceiling*. The session
  uses the provider's own default system prompt, default decoding
  parameters, and the consumer-tier model the subscription
  unlocks. Quality observed in the app is the **upper bound** of
  what the underlying weights can produce, not the lower bound of
  what our integration would deliver.
- **API output** (our own `HttpClient` + the Areg system prompt +
  our chosen parameters) shows *our integration quality*. Same
  weights, but the Areg system prompt, the temperature / max-
  tokens / history shape we picked, and our prompt-priming for
  choice blocks and story memory are all in the loop.

The honest decision question for production is **which API path,
with the Areg prompt and the Areg settings, produces the best
heqiats?** That question cannot be answered by comparing
"Claude.app vs OpenAI API" — the comparison would be **app
hidden prompt + tier-1 weights** vs **our Areg prompt + whatever
gpt-4o we've configured**, conflating three independent axes
(model quality, provider's hidden prompt, and our integration).

**Therefore:** the F1.2 manual evidence (`samples/` +
`evaluations/`) enters the file as one data point, not as a
runtime-switch input. This plan documents how to collect the rest
so a switch decision can be made honestly.

## 2. Models / paths to compare

The plan covers six paths. Some are already in the file; others
are "to capture when keys are available". None of the paths
implies a runtime change to the toy.

| # | Path | Source | Status today |
|---|---|---|---|
| 1 | **OpenAI runtime baseline** | `tools/StoryBenchmark` (live backend → ChatService → gpt-4o) | Already running on every regression — production reality, including moderation + story-memory injection. |
| 2 | **Claude consumer app** | claude.ai manual session | One sample captured: [`samples/claude-manual-pnjik-golden-leaf-20260501.md`](./samples/claude-manual-pnjik-golden-leaf-20260501.md). |
| 3 | **Claude API** | F1.2 `StoryModelBakeoff --run --provider claude` | Code is in place; key not yet configured for any captured run. |
| 4 | **Gemini consumer app** | gemini.google.com manual session | One sample captured: [`samples/gemini-manual-mlavik-sunbeam-20260501.md`](./samples/gemini-manual-mlavik-sunbeam-20260501.md). |
| 5 | **Gemini API** | F1.3+ `StoryModelBakeoff --run --provider gemini` | Code path NOT yet built; deferred slice. |
| 6 | **Optional: ChatGPT consumer app** | chatgpt.com manual session | NOT yet captured. Useful only as a "model ceiling" reference for OpenAI's tier; the API path #1 is what we ship. Lowest priority. |

The decision triangle that matters at the end:

```
        path #1 (OpenAI API, our prompt)        ← production reality
                |
        path #3 (Claude API, our prompt)        ← apples-to-apples challenger A
                |
        path #5 (Gemini API, our prompt)        ← apples-to-apples challenger B
```

Paths #2 / #4 / #6 are useful only as **ceiling references** — they
calibrate "what quality this provider's weights can reach" — they
do not feed the runtime decision directly.

## 3. Exact sample set (10 cases)

Re-using the same scenario format as
[`bakeoff-prompts.json`](./bakeoff-prompts.json) so a future
implementation slice can drop these straight in without DTO churn.
All Armenian text below is candidate copy; the actual prompt set
lands as a JSON file in a separate commit if/when the operator
approves it.

| ID | Category | Turns |
|---|---|---|
| **B01** | bare-armenian-opener | `Պատմիր հեքիաթ` |
| **B02** | forest-animal-opener | `Պատմիր հեքիաթ անտառային փոքրիկ նապաստակի մասին, որը կորցրել է իր ճանապարհը։` |
| **B03** | lost-magical-object | `Պատմիր հեքիաթ մի փոքրիկ աղջկա մասին, որը անտառում գտել է հին արծաթե բանալի։` |
| **B04** | continuation-option-A | turn 1: `Պատմիր հեքիաթ` ; turn 2: `Ա` (after the model's binary choice block) |
| **B05** | continuation-option-B | turn 1: `Պատմիր հեքիաթ` ; turn 2: `Բ` |
| **B06** | unclear-child-choice | turn 1: `Պատմիր հեքիաթ` ; turn 2: `Չգիտեմ, ոնց որ։` (ambiguous — should be normalized to `unclear` and the model must NOT pick a side for the child) |
| **B07** | mid-story-question | turn 1: `Պատմիր հեքիաթ` ; turn 2: `Ինչու՞ էր նա վախենում։` (child interrupts with a curiosity question — the model should answer briefly inside the story voice, not break frame) |
| **B08** | third-or-fourth-turn | turn 1: `Պատմիր հեքիաթ` ; turn 2: `Ա` ; turn 3: `Հետո՞` ; turn 4: `Իսկ ի՞նչ ասաց նա հետո։` (does the model maintain story memory across deep continuations or does it drift?) |
| **B09** | age-4-simpler | `Պատմիր մի շատ կարճ ու հանգիստ հեքիաթ չորսամյա երեխայի համար` (tests whether the model adapts complexity downward without losing warmth) |
| **B10** | age-7-richer | `Պատմիր մի ավելի հարուստ հեքիաթ յոթամյա երեխայի համար, որտեղ կա ընտրություն` (tests whether the model adapts complexity upward without losing the choice block) |

Notes on the set:

- B04+B05 are deliberately the **same opener** with the two
  branches, so the reviewer can compare how each model handles
  the SAME accumulated history with one bit different.
- B06 is the safety-net case for our story-choice pipeline: the
  ChatService normalizer converts ambiguous Armenian replies to
  `unclear`, and the assistant should not silently pick a side.
  The bake-off is the place to see whether each provider behaves
  the same way under the same prompt, OR whether the provider's
  default is to "always pick something."
- B07 is the most realistic real-toy case: a 5-year-old
  interrupts. We want to see whether the model can answer in
  voice and return to the story, not break into "as an AI..."
  meta-mode.
- B08 is the depth check. Story memory is currently injected by
  ChatService at runtime; in a bake-off run we are NOT running
  that injection (we hit the provider directly with raw history),
  so B08 measures the model's *intrinsic* memory, not our
  pipeline's.
- B09 / B10 measure age-aware adjustability — important because
  the runtime injects child age into the system prompt at request
  time.

## 4. Capture format

Every captured sample, regardless of source, must record the
following fields. App-only captures fill in what is observable;
API captures fill in everything.

| Field | App | API |
|---|---|---|
| `provider` | `claude` / `openai` / `gemini` / `local` | same |
| `source` | `app` | `api` |
| `modelLabel` | the consumer app's tier label as displayed (e.g. "Claude Sonnet"). Approximate. | same string label for human reading |
| `apiModelId` | (n/a — app does not expose this) | exact API id, e.g. `claude-opus-4-7`, `gpt-4o`, `gemini-2.5-pro` |
| `systemPrompt` | "(provider's default app system prompt; not directly observable)" | full text, OR a SHA-256 + path reference if pinned to `system-prompt.txt` |
| `userPrompt` | exact text typed by reviewer | same |
| `decoding` | `(unobservable in app)` | `temperature`, `top_p`, `top_k`, `max_tokens` — explicit; `(default)` is acceptable but must be noted |
| `historyIncluded` | yes/no — single message or full chat session | yes/no — list the prior turns or "none" |
| `rawOutput` | exact paste from the app, preserving newlines | response body's text, exactly as the provider returned it |
| `normalizedOutput` | (n/a — app output is the only signal) | only if the bake-off applied any post-processing (today: none) |
| `capturedAtUtc` | ISO-8601 UTC timestamp | ISO-8601 UTC |
| `reviewer` | initials or `Hayk` | same |

Filename conventions to follow:
- Samples: `samples/{provider}-{api|app}-{theme}-{yyyymmdd}.md`
- Evaluations: `evaluations/{same-base}.eval.md`
- Multi-run reports (live API runs land here automatically):
  `results/<UTCts>/results.json`, `review.md`, `summary.json`

## 5. Rubric

Verbatim re-use of the rubric already pinned in
[`README.md`](./README.md) and applied in
[`evaluations/`](./evaluations). No new dimensions introduced; a
larger sample base needs a stable rubric.

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

For a multi-turn scenario (B04 / B05 / B06 / B07 / B08), score the
**scenario as a whole**, not per turn. Per-turn scoring is too
granular for a human reviewer at this evidence stage; if a
specific turn was decisive, mention it in **Notes**.

## 6. Decision rules

These are the load-bearing constraints that prevent the bake-off
from accidentally driving a runtime change.

1. **No runtime switch from 1–2 samples.** Any provider switch
   needs a meaningful sample base — at minimum the 10-case set in
   § 3 across at least two reviewer passes — before it counts as
   a decision input.
2. **No runtime switch from app-only samples.** App output is a
   ceiling reference (§ 1). It cannot, on its own, override an
   API result.
3. **Claude / Gemini become candidates only if API samples with
   the Areg prompt and Areg settings clearly beat the current
   OpenAI API path.** "Clearly beat" is operationalised as: at
   least 2 of (Armenian naturalness, Fairy-tale feeling, Choice
   quality, Continuation coherence) advantage by ≥ 1 point on
   average across the 10-case set, AND no regression in
   Safety / Aloud-OK on any single scenario.
4. **Production runtime switch requires a separate architecture
   slice.** Switching `ChatService`'s provider would touch the
   system prompt, moderation pipeline, choice parsing, story
   memory injection, audit, and rate-limiter. None of that is in
   scope for any F1 slice. The F1 series produces the *evidence*;
   the switch is its own approved slice with its own risk plan.
5. **Prompt / settings fixes come before provider switch.** If
   API quality is weak on the current path (path #1), the cheap
   first move is to tune the Areg system prompt, the temperature,
   max-tokens, history shape, or moderation strictness — not to
   change provider. Only if a tuned OpenAI path still loses to a
   tuned Claude / Gemini API path is a switch warranted.
6. **One reviewer is not enough for a switch decision.** Hayk's
   ear is the primary signal today; a second Armenian-fluent
   reviewer pass is required before any production-affecting
   change.

## 7. Risks

The bake-off is **not** a clean experiment. The following
confounds must be considered when reading the rubric scores.

| Risk | Why it matters |
|---|---|
| **Hidden app system prompts** | Consumer app sessions inject a non-public system prompt that's almost certainly tuned for "warm, family-safe storytelling style." This is exactly the style we want, so the app output looks better than an API run with our Areg prompt — even on the same weights. |
| **Model / version mismatch between app and API** | The app routes to the highest-tier model available to the subscription, often a different ID than the default API alias. A sample from claude.ai may not be the model that `claude-opus-4-7` returns. |
| **Temperature differences** | Apps tune temperature for engagement; API defaults vary by provider (Anthropic ≈ 1.0 default, OpenAI ≈ 1.0 default, Gemini ≈ 0.9 default). Our F1.2 client sets none of them, leaving the provider default. Story warmth and pacing both correlate with temperature. |
| **`max_tokens` differences** | App sessions effectively have generous output budgets; F1.2 sets `max_tokens=1024`. A provider that hits the cap and stops mid-sentence will score worse on length / pacing than one that fits naturally. |
| **Over-constrained Areg prompt** | The production prompt enumerates many "DO NOT" rules (no violence, no horror, no scary things). A model that's heavily steered toward those negatives may produce flatter, more cautious stories. The app session has no such overlay. |
| **Formatting constraints harming story** | The choice block convention (binary options at the end of a story turn) is a structural ask the API path enforces explicitly via the system prompt. Some models naturally produce this; others over-comply, padding or breaking voice. |
| **Armenian correctness vs warmth tradeoff** | A model with stronger Armenian grammar may sound more bookish (less warm); a model with smoother child-voice warmth may make more case-marker errors. Single-dimension comparison can mislead. |
| **Safety / policy consistency across providers** | Each provider applies a different safety filter on top of generation. A provider that refuses to generate "scary" content for a 5-year-old may be safer in one direction (no horror) but worse in another (refusing a riddle about a dragon). |
| **Reviewer fatigue / order effects** | Reading 10 stories in a row biases scoring toward whatever was read first. The plan should randomize order and break runs into batches of 3–4. |

## 8. Next steps

In approximate order. Each step is its own commit; no step is
required for a runtime change.

1. **Collect more manual app samples** to widen the ceiling
   evidence — at minimum one Claude.app + one Gemini.app run for
   each of B01 / B02 / B03 / B04. Same `samples/` +
   `evaluations/` shape as 2026-05-01.
2. **Capture the OpenAI API baseline** by running
   `tools/StoryBenchmark` against the production stack and
   harvesting representative outputs. Re-score with the rubric in
   § 5 and file under `evaluations/openai-api-baseline-*.eval.md`.
3. **Run the F1.2 Claude API path** against the same set
   (`--run --provider claude --max-prompts 3` smoke first, then
   `--allow-full-set` after operator review). The tool already
   writes `results.json` / `review.md` / `summary.json` per run;
   move the chosen `review.md` into `evaluations/` once a human
   has scored it.
4. **Build and run the F1.3 OpenAI API path** (currently
   deferred). Use the same Areg system prompt and the same
   decoding-parameter conventions.
5. **Build and run the F1.4 Gemini API path** (currently
   deferred). Same conventions.
6. **Compare** using the rubric across paths #1 / #3 / #5. Apply
   the decision rules in § 6.
7. **If OpenAI loses** even after a tuning pass on the prompt
   and parameters, begin a separate architecture-slice plan for
   a runtime adapter. That plan is out of scope here and would
   touch ChatService, moderation, audit, story memory, and a
   migration path for in-flight conversations.

---

## What this plan deliberately does NOT include

- An auto-judge / LLM-as-grader.
- A blind / anonymized scoring mode.
- Stability rounds (run each prompt 3× and median).
- A new provider beyond the four already named in
  `bakeoff-prompts.json` / `Program.cs` (`local` is reserved but
  has no live path).
- Any change to `ChatService`, `appsettings.json`'s `SystemPrompt`,
  the production moderation pipeline, the audit feed, the parent
  dashboard, the firmware, the bake-off prompt set, or the
  bake-off CSPROJ.

These are reasonable to add later but each is its own slice.

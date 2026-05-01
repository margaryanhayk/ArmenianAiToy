<!--
  Areg — sample capture template for StoryModelBakeoff.

  HOW TO USE
  1. Copy this file to one of:
       - tools/StoryModelBakeoff/samples/<provider>-<api|app>-<theme>-<yyyymmdd>.md
         (raw sample only; sections 1–4)
       - tools/StoryModelBakeoff/evaluations/<same-base>.eval.md
         (rubric + decision; sections 5–8)
     OR keep both halves in a single file under samples/ if the
     sample and the scoring are captured in one sitting. Pick one
     convention per sample; do not duplicate.
  2. Fill every placeholder. If a field is genuinely unknowable
     (e.g. exact API model id behind a consumer app), write
     `(unobservable)` instead of leaving it blank.
  3. Preserve raw model output exactly — no Armenian "fixes",
     no whitespace edits.
  4. Score against the rubric in section 5. Multi-turn samples
     get ONE rubric for the scenario as a whole; mention any
     decisive turn in Notes.
  5. The Reminder block in section 8 is mandatory and stays in
     the final file — it is the structural guard against using
     this evidence to drive a runtime change prematurely.

  See:
    - README.md                       — tool usage and slice status
    - API_VS_APP_BAKEOFF_PLAN.md      — comparison plan, decision rules
    - samples/, evaluations/          — captured evidence to date
-->

# Areg story sample — `<sample-id>`

> **Quick title:** _(one Armenian word or two — e.g. "Փնջիկ / golden leaf / lake")_

---

## 1. Metadata

| Field | Value |
|---|---|
| Sample id | `<provider>-<api|app>-<theme>-<yyyymmdd>` |
| Provider | `<openai \| claude \| gemini \| local>` |
| Model label (human-readable) | _e.g. "Claude Sonnet (app default 2026-05-01)"_ |
| Exact API model id | `<exact id, e.g. claude-opus-4-7>` &nbsp;·&nbsp; `(n/a — app session)` if not API |
| Source | `<api \| app \| manual>` |
| Captured (UTC) | _ISO-8601, e.g. `2026-05-01T19:08:00Z`_ |
| Reviewer | _initials or full name_ |
| Language target | Eastern Armenian |
| Child age target | `<4 \| 5 \| 6 \| 7 \| range>` |
| Sample type | `<initial-story \| continuation-A \| continuation-B \| unclear-choice \| mid-story-interruption \| later-turn-continuation \| age-tuned>` |

## 2. Request context

| Field | Value |
|---|---|
| System prompt | _full text, OR SHA-256 + path reference if pinned to `system-prompt.txt`, OR `(unobservable — app default)`_ |
| Developer / tool prompt | _any wrapper prompt the bake-off injected — `(none)` if direct API_ |
| User prompt | _exact text typed by the reviewer / child_ |
| Conversation history included | `<none \| list of prior turns inline>` |
| Selected child choice (continuations only) | _e.g. "🌿 lake" or "Ա"_ |
| Decoding parameters | `temperature=<n>, top_p=<n>, top_k=<n>, max_tokens=<n>` &nbsp;·&nbsp; `(provider default)` is acceptable but must be noted |
| Safety / moderation path | _e.g. "production ChatService → moderation → ChatService", or `(provider safety only — no Areg overlay)`_ |

> **App samples:** most fields under "Decoding parameters" and "System
> prompt" are unobservable. Write `(unobservable — app default)`
> rather than guessing.

## 3. Raw output

> Paste the model's response **verbatim**. Do NOT normalize Armenian.
> Do NOT fix whitespace or punctuation. For multi-turn samples,
> use one quote block per turn with a turn-number label.

**Turn 1**

```
<paste here>
```

**Turn 2** _(if any)_

```
<paste here>
```

## 4. Normalized Areg output

> What Areg would actually speak / show after stripping machine
> footers (e.g. "Note: I am Claude…"), choice markers we don't
> render verbatim, or formatting our pipeline would discard.
> If no normalization was applied, write `(no normalization
> applied — raw output is what Areg would say)`.

```
<normalized text or note>
```

---

## 5. Rubric scoring

| Dimension | Score |
|---|---|
| Armenian naturalness | _ / 5 |
| Eastern Armenian correctness | _ / 5 |
| Fairy-tale feeling | _ / 5 |
| Warmth for age 4–7 | _ / 5 |
| Length / pacing | _ / 5 |
| Choice quality | _ / 5 |
| Continuation coherence | _ / 5 |
| Safety / age appropriateness | pass / fail |
| "Would I let Areg say this aloud?" | yes / no / yes-with-edits |

**Notes** _(free text — what's strong, what's weak, decisive
moments, idioms that landed or didn't, any single-turn issue
that drove the score)_:

> _…_

## 6. Failure tags

> Tick every tag that applies. Multi-tagging is encouraged.

- [ ] too generic
- [ ] too short
- [ ] too long
- [ ] translated / artificial Armenian
- [ ] Western Armenian drift
- [ ] grammar issue
- [ ] weak fairy-tale feeling
- [ ] over-moralizing
- [ ] too babyish
- [ ] unsafe / scary for age
- [ ] choice not concrete
- [ ] choice not grounded in the story world
- [ ] continuation ignores selected choice
- [ ] recap instead of action
- [ ] formatting / footer issue
- [ ] API / app mismatch concern (e.g. app sample masquerading as API evidence)
- [ ] other: _<describe>_

## 7. Decision note

| Field | Value |
|---|---|
| Candidate quality | `<strong \| acceptable \| weak \| reject>` |
| Usable as evidence for a runtime switch? | `<yes \| no>` |
| If no, why | _e.g. "app-only sample; not apples-to-apples"; or "single sample, switch needs ≥ 10-case set"_ |
| Recommended next action | _e.g. "capture matching API run with Areg prompt", "tune temperature in F1.x", "add to long-form review batch", "discard (no useful signal)"_ |

## 8. Reminder

> **Read this every time before scoring.** It is the structural
> guardrail this template exists to enforce.
>
> - **App samples are ceiling / reference evidence, not
>   runtime-switch evidence.** A consumer-app session uses the
>   provider's hidden default system prompt and the highest tier
>   the subscription unlocks. Whatever quality you observe is the
>   *upper bound* of what those weights can produce, not the
>   lower bound of what our integration would deliver.
> - **API samples must include exact request settings.**
>   Without `model`, `temperature`, `max_tokens`, and the system
>   prompt SHA-256, an API capture is not reproducible and not
>   comparable across providers.
> - **No runtime switch from 1–2 samples.** A switch needs the
>   full ≥ 10-case sample set in
>   [`API_VS_APP_BAKEOFF_PLAN.md`](./API_VS_APP_BAKEOFF_PLAN.md)
>   § 3, scored against the rubric in § 5 of that plan, by at
>   least two reviewer passes.
> - **Prompt / settings tuning before provider switching.** If
>   the OpenAI API path looks weak, the cheap first move is to
>   tune the Areg system prompt, temperature, max-tokens, history
>   shape, or moderation strictness. A provider switch is
>   warranted only if the *tuned* OpenAI path still loses to a
>   *tuned* Claude / Gemini API path.

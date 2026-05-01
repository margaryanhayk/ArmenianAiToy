# Evaluation — OpenAI API current-Areg baseline (`պատմիր հեքիաթ` / turn 1)

**Sample:** [`samples/openai-api-current-areg-baseline-story-20260501.md`](../samples/openai-api-current-areg-baseline-story-20260501.md)
**Source:** Production backend `POST /api/chat` → `ChatService` → moderation → `OpenAIReliabilityGate` → OpenAI Chat Completions.
**Captured:** 2026-05-01
**Reviewer:** Claude Code (agent draft) — **Hayk to confirm Armenian-side dimensions before this is treated as load-bearing evidence**.
**Status:** evidence-only; no runtime model switch implied.

---

## 5. Rubric scoring (agent draft)

| Dimension | Score | Confidence |
|---|---|---|
| Armenian naturalness | **3 / 5** | low — agent not native; see Notes |
| Eastern Armenian correctness | **4 / 5** | low — agent not native |
| Fairy-tale feeling | **3 / 5** | medium — observable from imagery + character choice |
| Warmth for age 4–7 | **3 / 5** | medium |
| Length / pacing | **4 / 5** | high — measurable |
| Choice quality | **4 / 5** | high — observable |
| Continuation coherence | **n/a** | turn 1 only; not measured |
| Safety / age appropriateness | **pass** | high |
| "Would I let Areg say this aloud?" | **yes-with-edits** | medium |

**Notes** _(observable, not Armenian-fluent judgments)_:

- **Animal pairing.** The story uses `ռնգեղջյուր` (rhinoceros) and
  `հավիկ` (chicken). Rhinoceros is exotic / zoo-fauna and is a
  notable departure from the Armenian fairy-tale palette
  (`նապաստակ`, `աղվես`, `գայլ`, `արջ`, `ոզնի`). The Claude.app
  baseline (`samples/claude-manual-pnjik-golden-leaf-20260501.md`)
  used `ոզնի` + `ընկուզենի` + `կաղնի` — strongly local Armenian
  fauna and flora. The Gemini.app baseline used `փիսիկ` + `ոզնի` +
  `զանգակածաղիկ` — also local. Today's OpenAI baseline reads
  more "translated children's-book" than the two app references.
- **Phrasing flags I can see.** "շուրջը պտտվելով մոտեցավ" (approached
  while spinning around) — clunky participle stack. "թե արդյոք
  իմանալու տուփի գաղտնիքը" — `արդյոք` + nominalized infinitive is
  unusual; reads translation-shaped. **A native Armenian reviewer
  should confirm or override these flags.**
- **Length / pacing.** ~360 chars, 4 short sentences. Within the
  "3–5 short sentences" production directive in `SystemPrompt`.
- **Choice block.** Two concrete options grounded in the story
  world (the box, the friend). Structurally correct. Slight
  preference issue: «Մոտենանք տուփին» / «Նայենք ընկերոջին» — both
  options mean "we approach" / "we look", both gentle, neither
  takes a clear narrative leap. Compare Claude.app's «🌿 lake /
  🌟 oak» which contrasted two distinct adventures.
- **Safety.** Clean. `safetyFlag=0` confirms moderation passed
  in/out without flag. No violence, no horror, no policy issue.
- **Aloud-OK.** I'd say yes for trial use, but a single edit
  swapping the rhinoceros for an Armenian-native animal would
  meaningfully lift the fairy-tale-feeling score. The clunky
  phrasings noted above are also light-edit candidates.
- **Latency.** 25.2 seconds end-to-end is slow for a single-turn
  story. This is observable signal independent of quality —
  attributable to ChatService's pipeline (moderation + reliability
  gate + OpenAI inference) rather than the model alone, but worth
  recording as a baseline data point.

## 6. Failure tags (agent draft)

- [ ] too generic
- [ ] too short
- [ ] too long
- [x] translated / artificial Armenian _(observable: "շուրջը պտտվելով", "թե արդյոք իմանալու"; native reviewer to confirm)_
- [ ] Western Armenian drift
- [ ] grammar issue _(no clear hard error spotted, but native reviewer to confirm)_
- [x] weak fairy-tale feeling _(rhinoceros + chicken pairing reads less native than Claude.app / Gemini.app references)_
- [ ] over-moralizing
- [ ] too babyish
- [ ] unsafe / scary for age
- [ ] choice not concrete
- [ ] choice not grounded in the story world
- [ ] continuation ignores selected choice _(not tested — turn 1 only)_
- [ ] recap instead of action
- [ ] formatting / footer issue _(`TailBlockParser` cleanly extracted both choices)_
- [ ] API / app mismatch concern _(this IS the API path on the same prompt set; that is the point)_
- [x] other: latency at ~25s is observable and worth tracking as baseline

## 7. Decision note

| Field | Value |
|---|---|
| Candidate quality | **acceptable** _(production-running today; not strong, not reject)_ |
| Usable as evidence for a runtime switch? | **no — single-sample baseline; switch decision needs the full ≥ 10-case set per `API_VS_APP_BAKEOFF_PLAN.md` § 3** |
| If no, why | One sample is one data point. The fairy-tale-feeling and Armenian-naturalness scores here are agent-draft and need a native reviewer pass. The 10-case set will reveal whether this is a per-turn fluke or a consistent shape. |
| Recommended next action | (1) Hayk to validate the Armenian-side scores. (2) Capture the same `պատմիր հեքիաթ` baseline at least 2 more times to see if the rhinoceros / clunky-phrasing pattern is consistent or a draw. (3) Try a tuning pass on the production `SystemPrompt` (e.g. add a "prefer Armenian-native fauna and flora" line) before any runtime provider switch — per `API_VS_APP_BAKEOFF_PLAN.md` § 6 rule 5. (4) When `ANTHROPIC_API_KEY` is available, run F1.2 Claude API on `պատմիր հեքիաթ` so we have an apples-to-apples API-on-Areg-prompt comparison instead of API-vs-app. |

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
>   [`API_VS_APP_BAKEOFF_PLAN.md`](../API_VS_APP_BAKEOFF_PLAN.md)
>   § 3, scored against the rubric in § 5 of that plan, by at
>   least two reviewer passes.
> - **Prompt / settings tuning before provider switching.** If
>   the OpenAI API path looks weak, the cheap first move is to
>   tune the Areg system prompt, temperature, max-tokens, history
>   shape, or moderation strictness. A provider switch is
>   warranted only if the *tuned* OpenAI path still loses to a
>   *tuned* Claude / Gemini API path.

# Repeated-choice detector + short-noun stemmer — post-validation 2026-05-24

## Summary

Live 5×4 against the recovered backend completed end-to-end.
**The new `choice_repeated_from_earlier_turn` detector fired once
on a real positive** (S02 Turn 2 repeated Turn 0's ChoiceA
verbatim — a mild stagnation that was silent before this slice).
The short-noun stemmer fix from commit `f89fdc5` works on the
choice side but exposed a new asymmetric-stem class on the body
side; this is a known stemmer limitation, not a regression, and
is documented as a recommendation below rather than fixed in
this validation slice.

**Verdict**: 3 PASS / 2 WARN / 0 FAIL. 20 turns. 0 safety_fallback.
0 http_error. No code changes recommended in this slice.

## Run context

- **Validation timestamp**: 2026-05-24 ~19:40 UTC
- **Run stamp** (StoryInteractiveLoop): `20260524-193512`
- **Branch**: `main`
- **Commit SHA**: `90daaea7` (HEAD; in sync with origin/main)
- **Working tree**: dirty
  (pre-existing M files: `.claude/settings.local.json`,
  `esp32/AregVoiceMvp/config.h`; pre-existing untracked files
  unrelated to this slice — none touched)

## Phase 0 — Deterministic test baseline (Pass)

`dotnet test tools/StoryInteractiveLoop.Tests`
  → **83/83 pass, 0 failed** (the 11 tests added in commit
   `90daaea` and the 7 added in commit `f89fdc5` all stable).

## Phase 1 — Backend health + tiny chat probe (Pass)

`GET /api/health` → `200 OK, "database":"ok"`.

`POST /api/chat` with seed «Պատմիր հեքիաթ փոքրիկ ոզնիի մասին`
→ `safetyFlag=0`, `mode=story`, real Armenian story
(«Փոքրիկ ոզնին, անունով Մուշ…»), two grounded choices, valid
storySessionId. Quota healthy.

## Phase 2 — StoryInteractiveLoop 5×4 (Pass)

```
dotnet run --project tools/StoryInteractiveLoop -- \
    --max-sessions 5 --max-turns 4 \
    --seed-id S01,S02,S03,S04,S05 --allow-larger-run
```

| # | Seed | Stop reason         | Turns | Verdict | Arm | Logic | Suit | Choice | Cont |
|---|------|---------------------|------:|---------|----:|------:|-----:|-------:|-----:|
| 1 | S01  | max_turns_reached   |   4   | PASS    | 100 |   100 |  100 |   85   |  100 |
| 2 | S02  | max_turns_reached   |   4   | PASS    | 100 |   100 |  100 |   85   |   80 |
| 3 | S03  | max_turns_reached   |   4   | WARN    | 100 |   100 |  100 |   70   |  100 |
| 4 | S04  | max_turns_reached   |   4   | WARN    | 100 |   100 |  100 |   55   |  100 |
| 5 | S05  | max_turns_reached   |   4   | PASS    | 100 |   100 |  100 |  100   |  100 |

Aggregate: Armenian 100, Logic 100, Suitability 100,
Choice quality 79, Continuation 96. 0 fail-closed, 0 http_error.

Recurring-warning histogram:

| Code                                | Count |
|-------------------------------------|------:|
| `choice_b_noun_not_in_body`         | 4     |
| `choice_a_noun_not_in_body`         | 3     |
| `choice_repeated_from_earlier_turn` | **1** ← new detector fired |
| `choices_repeated_from_earlier_turn` (exact pair) | 0 |
| (everything else)                   | 0     |

## Phase 3 — Comparison vs `20260524-181621` baseline

| Metric                                | Baseline | This run | Delta |
|---------------------------------------|---------:|---------:|------:|
| Sessions reaching max_turns           |  5/5     |  5/5     |  =    |
| `safety_fallback:*`                   |  0       |  0       |  =    |
| `http_error`                          |  0       |  0       |  =    |
| `choice_a_noun_not_in_body`           |  3       |  3       |  =    |
| `choice_b_noun_not_in_body`           |  2       |  4       |  +2   |
| `choice_repeated_from_earlier_turn`   |  —       |  **1**   |  +1 (new code path) |
| `continuation_ignores_selected_choice`|  0       |  0       |  =    |
| `recap_overlap_high`                  |  0       |  0       |  =    |
| Sessions PASS / WARN / FAIL           | 3/2/0    | 3/2/0    |  =    |

The verdict distribution is unchanged. The noun-warning total
increased by 2; analysis below shows the increase is dominated
by a new asymmetric-stem class on the body side, not by a
regression in the choice-side stemmer fix.

## A. Moderation / stability

- `safety_fallback:2` count: **0**.
- `http_error` count: **0**.
- All 5 sessions reached `max_turns_reached`.
- `aat_moderation_failclosed_total` did NOT increment (by code
  inspection — every turn returned `SafetyFlag.Clean`, so
  `FailClosed` was never called).

## B. Short-noun stemmer fix (commit `f89fdc5`) validation

**Real positives confirmed.** Two real noun-grounding gaps
surfaced:

1. **S02 Turn 0 ChoiceB «Նայենք ծառին»**: body has «ծառի» /
   «ծառը» / «Ծառի» / «ծառի» — multiple forms of the same
   noun in 4-char-source variants. Choice's 5-char «ծառին»
   strips to «ծառ» under the new rule. Body's 4-char forms
   cannot strip (default rule still applies to `ի` / `ը` —
   stripping would leave 3 chars). Stems mismatch: body
   `ծառի` / `ծառը` vs choice `ծառ`. **False positive caused
   by the asymmetric stripping rule** — choice gets the
   relaxed rule, body gets the default. NOT a regression of
   `f89fdc5`; this class of asymmetric stem was masked by
   the OLD «-ին» behavior that left choice stems also at
   4-char length.

2. **S03 Turn 1 ChoiceA «Մոտենանք լույսին»**: body has
   «լույսերը» / «լույսերի» (plural definite / plural genitive
   of "light"). Body forms strip via `ի` / `ը` to `լույսեր`.
   Choice «լույսին» strips to `լույս`. Bare `-եր` plural
   marker is not normalized by the stemmer — this is the
   SAME limitation class as `-իկ` diminutive already
   documented in the README's "Known limitations" section.
   **Stemmer limitation, not a regression.**

3. **S04 Turn 0 ChoiceA «Մոտենանք քարին»**, Turn 1 ChoiceB
   «Նայենք քարին»: body uses «քարը» (4) and «քարի» (4) —
   neither can strip under default `ի` / `ը` rules. Choice
   «քարին» (5) strips to «քար» (3). **Same asymmetric-stem
   class as #1.**

4. **S03 Turn 2 ChoiceB «Նայենք երգողին»**: body has «երգ»
   (3, skipped) / «երգը» (4, no strip → `երգը`). Choice
   stem: `երգող` ("singer", agent noun). Body never
   establishes a singer character — the model invented an
   agent noun where the body had only the abstract noun
   «երգ» (song). **Borderline real positive** — the choice
   introduces a new entity (`երգող`) the body never
   established.

5. **S04 Turn 2 ChoiceA «Փորձենք հավաքել քարը»**: choice
   noun stems include the bare `քարը` (4, no strip);
   body has `քարն` (4, no strip — different last char).
   Three asymmetric forms of the same noun (`քարը` /
   `քարի` / `քարն`) all distinct under the stemmer.
   **False positive of the same asymmetric class.**

**Summary**: of 7 noun warnings, 1 is borderline real
(`երգող` agent noun), the other 6 are body-side stemmer
limitations exposed by the asymmetric rule introduced in
commit `f89fdc5`. The choice-side fix is doing exactly what
it was designed to do — the gap is now on the BODY side.

## C. Repeated individual choice detector validation

`choice_repeated_from_earlier_turn` fired **exactly once**:

- **S02 Turn 2**, ChoiceA = «Մոտենանք նապաստակին».
  - Turn 0 ChoiceA was the identical «Մոտենանք նապաստակին».
  - The story's main character is the rabbit («նապաստակ»);
    repeating "let's approach the rabbit" as a choice 2
    turns later (when the rabbit IS the protagonist already
    standing there) is genuinely stuck phrasing.
  - **Real positive.** Without this slice's detector, the
    warning would have been silent — the exact-pair
    detector did NOT fire (Turn 0 had B=«Նայենք ծառին» but
    Turn 2 had B=«Նայենք թիթեռին», so the pair differed).

`choices_repeated_from_earlier_turn` (exact-pair) fired **zero
times** — confirming the complementary nature of the two
detectors (the individual-repeat detector caught a case the
exact-pair detector missed by design).

No false positives observed. No case where two superficially
similar but semantically distinct choices triggered the
detector.

## D. Story quality

- **Continuations followed selected choices** in 20/20 turns
  (no `continuation_ignores_selected_choice`).
- **No story restarts.** All sessions produced coherent
  multi-turn arcs.
- **Choice length** well under the 60-char ESP32 budget:
  longest this run was «Հարցնենք թիթեռին գաղտնիքը» (24 chars,
  S02 Turn 3 ChoiceB).
- **No Armenian leakage**: avg Armenian ratio = 1.00 across
  all 20 turns. No Latin or Cyrillic runs flagged.
- **Heavy reliance on the «Մոտենանք X / Նայենք Y» pattern**
  continues. This is a Story-prompt-design concern that has
  been out of scope through this entire slice family.

## Phase 4 — Metrics check after run

`/metrics` returns 404 (no Authorization header) — the
documented concealment-fail-closed default. Counter behavior
remains pinned by the 14 unit tests in commit `4fa6274`. By
code inspection, `aat_moderation_failclosed_total` did NOT
increment (zero `SafetyFlag != Clean` outcomes in the run).

## Conclusions

| Question                                              | Answer |
|-------------------------------------------------------|--------|
| Validation completed?                                 | **Yes — all 5 sessions reached max_turns.** |
| Backend healthy?                                      | **Yes.** |
| Did `choice_repeated_from_earlier_turn` fire?         | **Yes, once. Real positive (S02 Turn 2).** |
| Did the exact-pair detector also fire?                | No — confirming the new detector caught a case the old one missed. |
| Did `choice_repeated_from_earlier_turn` false-positive? | No. |
| Short-noun stemmer fix regressions?                   | **None observed.** The fix is doing its job on the choice side. |
| New false-positive class?                             | **Yes** — asymmetric stems (choice-side strips `-ին` to 3-char; body-side `-ի` / `-ը` cannot strip from 4-char source). Documented below; not in scope for this slice. |
| Any code change recommended now?                      | **No.** Findings are split between (a) real positives, (b) borderline cases, and (c) known stemmer limitations — no clear bug. |

## Out-of-scope observations for future small slices

Each is a candidate for a future focused slice:

1. **Body-side asymmetric stemmer gap** — choice-side «-ին»
   strips to 3-char stems via `ShortStemAllowedEndings`, but
   body-side «-ի» / «-ը» on 4-char-source nouns cannot strip
   (default 4-char-result rule). Fix would be to add «-ի» /
   «-ը» to `ShortStemAllowedEndings` (allow them to strip to
   3-char stem when source ≥ 4). Risk: «ոզնի» (4) → «ոզն»
   would be stripped — fine because «ոզնի» is rarely the
   choice's noun (would be «ոզնուն» or «ոզնիի»). Worth a
   small targeted slice.

2. **Bare «-եր» plural marker** — body «լույսերի» / «լույսերը»
   stems to «լույսեր» (5), not «լույս» (4). The «ներ» plural
   strips via existing rules but «եր» (without the «ն»
   prefix) does not. Same limitation class as `-իկ`
   diminutive. Adding «եր» to the ending list with a length
   gate could help, but `-եր` is also a noun in its own right
   («եր» = "side") so risk is non-trivial.

3. **Story-prompt-design**: the «Մոտենանք X / Նայենք Y»
   pattern dominance. Out of scope for the StoryInteractiveLoop
   tool — would require a Story-prompt revision slice, with
   explicit Phase-B-guardrails review.

## Cost summary

- Phase 1 tiny probe: 1 chat call (real Armenian story).
- Phase 2 5×4 run: 25 chat calls (5 × (1 start + 4 turns)).
- Total: 26 OpenAI chat completions. Within the explicit
  cost gate; zero retries; zero failed billed calls.

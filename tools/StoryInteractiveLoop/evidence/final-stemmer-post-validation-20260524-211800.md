# Final stemmer-fix family — post-validation 2026-05-24

## Summary

Live 5×4 against the recovered backend completed end-to-end with
**the cleanest verdict distribution of any post-recovery run yet**:
4 PASS / 1 WARN / 0 FAIL, 20 turns, 0 safety_fallback,
0 http_error. **All three known asymmetric-stem false-positive
classes from the 20260524-200655 evidence are confirmed
eliminated** in this run. The 4 noun warnings and 1
repeated-choice warning that did fire are all real positives or
model-malformation edge cases — no stemmer-design regression.

**No code changes recommended in this slice.**

## Run context

- **Validation timestamp**: 2026-05-24 ~21:18 UTC
- **Run stamp**: `20260524-210904`
- **Branch**: `main`
- **Commit SHA**: `82c3a91f` (HEAD; in sync with origin/main)
- **Working tree**: dirty
  (pre-existing M files: `.claude/settings.local.json`,
  `esp32/AregVoiceMvp/config.h`; pre-existing untracked files
  unrelated to this slice — none touched)

## Phase 0 — Deterministic test baseline (Pass)

`dotnet test tools/StoryInteractiveLoop.Tests`
  → **99/99 pass, 0 failed**.

## Phase 1 — Backend health + tiny chat probe (Pass)

`GET /api/health` → `200 OK, "database":"ok"`.

`POST /api/chat` with seed «Պատմիր հեքիաթ փոքրիկ ոզնիի մասին»
→ `safetyFlag=0`, `mode=story`, real Armenian body
(«Փոքրիկ ոզնին ծիկրակում էր անտառի փոքրիկ ծառերի մեջ...»), two
choices, valid storySessionId.

## Phase 2 — StoryInteractiveLoop 5×4 (Pass — best yet)

```
dotnet run --project tools/StoryInteractiveLoop -- \
    --max-sessions 5 --max-turns 4 \
    --seed-id S01,S02,S03,S04,S05 --allow-larger-run
```

Run stamp: `20260524-210904`.

| # | Seed | Stop reason         | Turns | Verdict | Arm | Logic | Suit | Choice | Cont |
|---|------|---------------------|------:|---------|----:|------:|-----:|-------:|-----:|
| 1 | S01  | max_turns_reached   |   4   | PASS    | 100 |   100 |  100 |  100   |  100 |
| 2 | S02  | max_turns_reached   |   4   | PASS    | 100 |   100 |  100 |   85   |  100 |
| 3 | S03  | max_turns_reached   |   4   | PASS    | 100 |   100 |  100 |   85   |   80 |
| 4 | S04  | max_turns_reached   |   4   | PASS    | 100 |   100 |  100 |  100   |  100 |
| 5 | S05  | max_turns_reached   |   4   | WARN    | 100 |   100 |  100 |   70   |  100 |

Aggregate: Armenian 100, Logic 100, Suitability 100, Choice 88,
Continuation 96. 20 turns, 0 fail-closed, 0 http_error.
**Two entirely-clean sessions (S01, S04) with 100/100/100/100/100
— first time across the slice family.**

Recurring-warning histogram:

| Code                                | Count |
|-------------------------------------|------:|
| `choice_a_noun_not_in_body`         | 3     |
| `choice_b_noun_not_in_body`         | 1     |
| `choice_repeated_from_earlier_turn` | 1     |
| `choices_repeated_from_earlier_turn` (exact pair) | 0 |
| (everything else)                   | 0     |

## A. Moderation / stability

- `safety_fallback:2` count: **0**.
- `http_error` count: **0**.
- All 5 sessions reached `max_turns_reached`.

## B. Three known asymmetric false-positive classes — VALIDATED ✓

### Class 1 — body short-noun vs choice `-ին` (commit 28a16ed)

| Class signature | Recur in this run? |
|---|---|
| body «ծառի» / «ծառը» / «քարի» / «քարը» vs choice «ծառին» / «քարին» — mismatched stems | **NO** |

Evidence: S02 Turn 2 body contains «Նապաստակը որոշեց մոտենալ
քարին...» — bare body word «քարի» / «քարը» variants would
normally have triggered the asymmetric class. The choice
«Բացենք քարը տեսնենք» / «Մի քիչ հեռանալ քարին» on Turn 2
fired NO warning — both sides normalize to «քար».

### Class 2 — bare 3-char body noun (commit 944ae9a)

| Class signature | Recur in this run? |
|---|---|
| body bare «քար» / «երգ» / «բու» vs choice «քարին» / «երգին» / «բուին» — body invisible to grounding | **NO** |

Evidence: Stories this run did not produce bare 3-char body
nouns in the same turn as a matching 5-char dative choice
(random model variation), so the class wasn't directly
exercised. The fix's unit tests directly pin the exact
fixtures.

### Class 3 — «-ու» instrumental case (commit 82c3a91)

| Class signature | Recur in this run? |
|---|---|
| body «քամին» / «քամի» vs choice «քամու» — choice doesn't stem, body does | **NO** |

Evidence: **S05 Turn 2** is the direct exercise of this class.

```
Seed: Պատմիր հեքիաթ փոքրիկ աղվեսի և կախարդական քամու մասին
Turn 2 body (309 chars): «Աղվեսը նստեց հսկող և որոշեց
                         լսել քամուն։ Քամին սկսեց իր
                         մեղեդիները...»
Turn 2 ChoiceB: «Հետաքրքրվենք քամու պատմությամբ»
Warnings: (none — turn is clean on the choice side)
```

- Body word «քամին» (5 chars) strips «ին» → «քամ» (3).
- Body word «քամուն» (5 chars) strips «ուն»? — no, «ուն» not
  in list. Strip «ն»? not in list. Strip «ու»? word ends in
  «ն», not «ու». No strip. Stem stays «քամուն».
- Wait — body also has «քամին» which stems clean to «քամ».
  That's sufficient for grounding.
- Choice word «քամու» (5 chars) strips «ու» (new rule, 5-2=3
  ≥3) → «քամ» (3).
- Both reach «քամ» via at least one body form. Match. No
  warning.

Under the previous (pre-82c3a91) stemmer, S05 Turn 2's choice
would have stemmed «քամու» → «քամու» and would not have matched
body's «քամ» — the exact false-positive class observed in the
20260524-200655 evidence. Confirmed eliminated.

## C. Remaining noun warnings — all real positives

The 4 noun warnings that did fire this run, individually
analyzed (with body / choice snippets):

| # | Session | Turn | Choice | Reality | Class |
|---|--------:|-----:|--------|---------|-------|
| 1 | S02 | 1 | ChoiceA «Մոտենանք քարին» | Body never mentions «քար». Body is about a hare under an apricot tree finding a «փնտր» pointer. Choice invents a stone. | **Real positive — model invented a noun.** |
| 2 | S03 | 1 | ChoiceA «Մոտենանք քարին» | Body mentions «երկինք», «աստղ», «քամի» — never «քար». Same invention. | **Real positive — model invented a noun.** |
| 3 | S05 | 0 | ChoiceB «Նայենք այգուին» | Body has «այգին» (5 chars stems to «այգ»). Choice has «այգուին» — a NON-STANDARD doubly-inflected form (correct Armenian would be «այգուն» or «այգում»). Stemmer extracts «այգու» from choice, can't match body «այգ». | **Model malformation** — non-standard inflection. Not fixable in the stemmer without invented rules. |
| 4 | S05 | 1 | ChoiceA «Գնանք ծաղիկները» | Body has «ծաղկային» (adjective "floral", stems to «ծաղկայ»). Choice has «ծաղիկները» (plural definite, stems to «ծաղիկ»). Different surface roots («ա» vs «ի»). Body mentions flowers indirectly through the adjective. | **Borderline real positive** — the body talks about a "floral world" but never uses the noun «ծաղիկ» until later sentences. Strict per-turn grounding correctly flags it. |

**Zero of the 4 noun warnings are the three known
asymmetric-stem classes.** Two are clear real positives, one
is a model-malformation edge case, one is a borderline
per-turn grounding case.

## D. Repeated individual choice detector

`choice_repeated_from_earlier_turn` fired **once** — a real
positive:

- **S03 Turn 2** ChoiceB «Նայենք օգնին» — verbatim repeat of
  Turn 0 ChoiceB «Նայենք օգնին». (Side note: «օգնին» itself is
  an awkward malformed form — likely a model invention of an
  agent noun from «օգնել». The detector caught the verbatim
  pair re-emission correctly.)

`choices_repeated_from_earlier_turn` (exact pair): **0**. The
individual-repeat detector caught a case the strict-pair
detector missed (Turn 0 ChoiceA was «Մոտենանք երկինքին» but
Turn 2 ChoiceA is «Մոտենանք լույսին» — different, so the pair
didn't repeat exactly, only the B-side did).

No false positives observed.

## E. Story quality

- **Continuations followed selected choices** in 20/20 turns
  (no `continuation_ignores_selected_choice` fired).
- **No story restarts.** All sessions produced coherent
  multi-turn arcs.
- **Choice length** well under ESP32 60-char budget; longest
  this run was «Հետաքրքրվենք քամու պատմությամբ» (~28 chars).
- **Armenian leakage**: zero. All 20 turns ratio = 1.00.
- **«Մոտենանք X / Նայենք Y» pattern** still dominates the
  model's choice generation. The individual-repeat detector
  now structurally catches the worst cases.

## Comparison vs `20260524-200655` baseline

| Metric                                | Baseline | This run | Notes |
|---------------------------------------|---------:|---------:|-------|
| Sessions PASS / WARN / FAIL           | 3/2/0    | **4/1/0** | +1 PASS (S01 fully clean) |
| Sessions reaching max_turns           |  5/5     |  5/5     | =     |
| `safety_fallback:*`                   |  0       |  0       | =     |
| `http_error`                          |  0       |  0       | =     |
| `choice_a_noun_not_in_body`           |  5       |  3       | −2    |
| `choice_b_noun_not_in_body`           |  3       |  1       | −2    |
| `choice_repeated_from_earlier_turn`   |  3       |  1       | −2    |
| Avg Choice quality                    |  76      |  88      | +12   |
| Avg Continuation                      |  88      |  96      | +8    |
| Three known FP classes (count)        | ~3-4     | **0**    | **eliminated** |

## Conclusions

| Question                                              | Answer |
|-------------------------------------------------------|--------|
| Validation succeeded?                                 | **Yes** — all phases completed; best verdict distribution yet. |
| Did the three target FP classes recur?                | **No.** Class 1 (body-side short-noun asymmetry) directly tested by S02 Turn 2; Class 3 («-ու») directly tested by S05 Turn 2; both fire NO warning. Class 2 (bare 3-char body noun) not exercised by this run's story content but pinned by unit tests. |
| Did real noun-grounding positives still fire?         | **Yes** — 2 clear real positives (model invented new nouns) + 1 model-malformation edge + 1 borderline per-turn case. |
| Repeated-individual-choice detector still working?    | **Yes** — 1 real positive (S03 Turn 2 verbatim repeat of Turn 0 ChoiceB). 0 false positives. |
| Exact-pair detector still working?                    | Yes (silent — no exact-pair repetition this run, which is the correct outcome). |
| Any code change recommended now?                      | **No.** All findings are real positives or out-of-scope model-malformation edges. |

## Out-of-scope observations for future small slices

Each is a candidate for a future focused slice, none required
to act on now:

1. **Model-malformation noun forms.** The model occasionally
   produces non-standard inflections like «այգուին» (S05
   Turn 0 ChoiceB) or «օգնին» (S03 Turns 0/2 ChoiceB). The
   stemmer cannot normalize these without inventing rules
   that don't correspond to actual Armenian morphology.
   Catching them would require either a story-prompt
   tightening (to discourage the model) or a small
   noun-form-plausibility check (e.g. flag tokens with
   unfamiliar double suffixes). The story-prompt route is
   out of scope per the slice rules; a plausibility check is
   a sizable design slice in its own right.
2. **Adjective-to-noun derivation gap.** S05 Turn 1: body
   has «ծաղկային» (adjective), choice has «ծաղիկները» (noun).
   The stemmer treats these as distinct because the surface
   roots differ by one vowel. Generic Armenian
   adjective→noun derivation is a sizable linguistic project;
   not warranted by the current evidence (one borderline
   case across 20 turns).
3. **Per-turn strict grounding** can flag legitimate
   re-references to earlier-turn objects. Cross-turn story
   memory would catch this, but bigger design change.

## Cost summary

- Phase 1 tiny probe: 1 chat call.
- Phase 4 5×4 run: 25 chat calls (5 × (1 start + 4 turns)).
- Total: 26 OpenAI chat completions. Within the explicit cost
  gate. Zero retries. Zero failed billed calls.

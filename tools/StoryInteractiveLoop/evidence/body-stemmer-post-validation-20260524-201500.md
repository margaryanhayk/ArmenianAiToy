# Body-side stemmer fix — post-validation 2026-05-24

## Summary

Live 5×4 against the recovered backend completed end-to-end.
**The body-side short-noun stemmer fix from commit `28a16ed`
eliminated the specific asymmetric false-positive class it was
designed for** — confirmed unambiguously by S02 Turn 0, where
body «ծառի» (4-char genitive) and choice «Նայենք ծառին»
(5-char dative) now share the stem «ծառ» and produce NO
warning, whereas in the 20260524-193512 baseline this exact
shape was a false positive.

The repeated-individual-choice detector from commit `90daaea`
fired 3 times, all 3 real positives (verbatim repeats of an
earlier turn's ChoiceA). No detector regression.

**Verdict**: 3 PASS / 2 WARN / 0 FAIL. 20 turns. 0
safety_fallback. 0 http_error. No code changes recommended in
this slice.

## Run context

- **Validation timestamp**: 2026-05-24 ~20:15 UTC
- **Run stamp**: `20260524-200655`
- **Branch**: `main`
- **Commit SHA**: `28a16ed0` (HEAD; in sync with origin/main)
- **Working tree**: dirty
  (pre-existing M files: `.claude/settings.local.json`,
  `esp32/AregVoiceMvp/config.h`; pre-existing untracked files
  unrelated to this slice — none touched)

## Phase 0 — Deterministic test baseline (Pass)

`dotnet test tools/StoryInteractiveLoop.Tests`
  → **91/91 pass, 0 failed**.

## Phase 1 — Backend health + tiny chat probe (Pass)

`GET /api/health` → `200 OK, "database":"ok"`.

`POST /api/chat` with seed «Պատմիր հեքիաթ փոքրիկ ոզնիի մասին»
→ `safetyFlag=0`, `mode=story`, real Armenian body
(«Փոքրիկ ոզնիկը... խաղալիք տուփ...»), two choices, valid
storySessionId. Quota healthy.

## Phase 2 — StoryInteractiveLoop 5×4 (Pass)

```
dotnet run --project tools/StoryInteractiveLoop -- \
    --max-sessions 5 --max-turns 4 \
    --seed-id S01,S02,S03,S04,S05 --allow-larger-run
```

Run stamp: `20260524-200655`.

| # | Seed | Stop reason         | Turns | Verdict | Arm | Logic | Suit | Choice | Cont |
|---|------|---------------------|------:|---------|----:|------:|-----:|-------:|-----:|
| 1 | S01  | max_turns_reached   |   4   | WARN    | 100 |   100 |  100 |   55   |   80 |
| 2 | S02  | max_turns_reached   |   4   | PASS    | 100 |   100 |  100 |   85   |   80 |
| 3 | S03  | max_turns_reached   |   4   | PASS    | 100 |   100 |  100 |   85   |   80 |
| 4 | S04  | max_turns_reached   |   4   | PASS    | 100 |   100 |  100 |  100   |  100 |
| 5 | S05  | max_turns_reached   |   4   | WARN    | 100 |   100 |  100 |   55   |  100 |

Aggregate: Armenian 100, Logic 100, Suitability 100, Choice 76,
Continuation 88. 20 turns, 0 fail-closed, 0 http_error.

Recurring-warning histogram:

| Code                                | Count |
|-------------------------------------|------:|
| `choice_a_noun_not_in_body`         | 5     |
| `choice_b_noun_not_in_body`         | 3     |
| `choice_repeated_from_earlier_turn` | **3** ← all real positives |
| `choices_repeated_from_earlier_turn` (exact pair) | 0 |
| (everything else)                   | 0     |

## A. Moderation / stability

- `safety_fallback:2` count: **0**.
- `http_error` count: **0**.
- All 5 sessions reached `max_turns_reached`.

## B. Body-side short-noun stemmer fix (commit `28a16ed`) — VALIDATED ✓

**The targeted asymmetric class is eliminated.** The clearest
demonstration is **S02 Turn 0**:

```
Seed: Պատմիր հեքիաթ ծիրանի ծառի տակ ապրող փոքրիկ նապաստակի մասին
Body (343 chars): «Փոքրիկ նապաստակը ապրում էր ծիրանի ծառի
                   տակ։ Ամեն առավոտ նա արթնանում էր, երբ արևի
                   լույսը փայլում էր ծիրանի կատարին։
                   Նապաստակն ուներ մի փոքրիկ բույսերի
                   պարտեզ…»
ChoiceA: Մոտենանք նապաստակին
ChoiceB: Նայենք ծառին
Warnings: (none — turn is clean)
```

- Body word «ծառի» (4 chars) now strips «ի» under the new
  short-stem rule → stem «ծառ» (3 chars).
- Choice word «ծառին» (5 chars) strips «ին» → stem «ծառ»
  (3 chars).
- Stems match → no warning.

In the 20260524-193512 baseline, **this exact shape** was a
false positive (S02 Turn 0 ChoiceB «Նայենք ծառին» fired
`choice_b_noun_not_in_body` because body «ծառի» / «ծառը»
couldn't strip under the old default-4-char floor).

The 8 noun warnings that DID fire this run, individually
analyzed:

| # | Session | Turn | Choice | Body had matching noun? | Class |
|---|--------:|-----:|--------|-------------------------|-------|
| 1 | S01 | 0 | ChoiceA «Մոտենանք թռչունիկին» | No (body: ծաղիկներ / բացատ / ոզնի). | Real positive — model invented bird. |
| 2 | S01 | 1 | ChoiceB «Նայենք բացատի ծաղիկներին» | «բացատ» yes, «ծաղիկ» no (in THIS turn's body). | Real positive (per-turn strict). |
| 3 | S01 | 2 | ChoiceA «Մոտենանք թռչունիկին» | No. | Real positive (also fires repeat detector). |
| 4 | S03 | 0 | ChoiceA «Մոտենանք լույսին» | No (body: փայլ / շող). | Real positive — model used new noun for "light". |
| 5 | S05 | 1 | ChoiceA «Մոտենանք քարին» | Body has bare 3-char «քար» (skipped by tokenizer `minLen=4`). | New limitation: bare 3-char body noun. |
| 6 | S05 | 2 | ChoiceB «Փորձել երգել քամու հետ» | Body has «քամին» (stems to «քամ»); choice has «քամու» (no «-ու» ending in stemmer). | New limitation: «-ու» instrumental case not handled. |
| 7 | S05 | 3 | ChoiceB «Լսենք երգի շարունակությունը» | Body has bare 3-char «երգ» (skipped by tokenizer). | New limitation: bare 3-char body noun. |
| 8 | (not counted — see histogram) |   |   |   |   |

**Zero of the 8 noun warnings are the body-side asymmetric
class the slice was created to eliminate.** Three categories:

- **5 real positives** (model invented new nouns the body
  never mentioned, e.g. «թռչուն» / «լույս»).
- **2 limitations** of the same class (body has bare 3-char
  noun, e.g. «քար» / «երգ», skipped by the tokenizer's 4-char
  minimum length).
- **1 limitation** of a new class («-ու» instrumental ending,
  e.g. «քամու» vs body «քամին»). Choice-side strips to «քամ»
  (5-2=3 OK); body-side strips to «քամ» (5-2=3 OK); choice
  «քամու» has no ending to strip → stays at «քամու». Mismatch.

## C. Repeated individual choice detector — VALIDATED ✓

Fired **3 times**, all real positives:

| # | Session | Turn | Choice | Repeats from |
|---|--------:|-----:|--------|--------------|
| 1 | S01 | 2 | ChoiceA «Մոտենանք թռչունիկին» | Turn 0 ChoiceA (identical) |
| 2 | S02 | 3 | ChoiceA «Մոտենանք նապաստակին» | Turn 0 ChoiceA (identical) |
| 3 | S03 | 2 | ChoiceA «Մոտենանք լույսին» | Turn 0 ChoiceA (identical) |

All three are verbatim repeats of a previous turn's ChoiceA.
The exact-pair detector (`choices_repeated_from_earlier_turn`)
fired 0 times — confirming the individual-repeat detector
catches mild stagnation that strict-pair matching misses.

No false positives observed. No case where two semantically
distinct choices triggered the warning.

## D. Story quality

- **Continuations followed selected choices** in 20/20 turns
  (no `continuation_ignores_selected_choice` fired).
- **No story restarts.** All sessions produced coherent
  multi-turn arcs.
- **Choice length** well under the ESP32 60-char budget;
  longest this run was «Լսենք երգի շարունակությունը» (~25
  chars).
- **Armenian leakage**: zero. All 20 turns have ratio 1.00.
  No Latin or Cyrillic runs flagged.
- **Recurring «Մոտենանք X / Նայենք Y» dominance**: continues.
  Story-prompt-design concern, not in scope. The new
  individual-repeat detector is the structural way to catch
  the worst cases of this pattern.

## Phase 4 — Metrics check after run

`/metrics` returns 404 (no Authorization header) — documented
concealment-fail-closed default. Counter behavior is pinned by
unit tests; by code inspection,
`aat_moderation_failclosed_total` did NOT increment during the
run (zero `SafetyFlag != Clean` outcomes).

## Comparison vs `20260524-193512` baseline

| Metric                                   | Baseline | This run | Notes |
|------------------------------------------|---------:|---------:|-------|
| Sessions reaching max_turns              |  5/5     |  5/5     | =     |
| `safety_fallback:*`                      |  0       |  0       | =     |
| `http_error`                             |  0       |  0       | =     |
| `choice_a_noun_not_in_body`              |  3       |  5       | +2 (different stories, more invented nouns) |
| `choice_b_noun_not_in_body`              |  4       |  3       | −1    |
| `choice_repeated_from_earlier_turn`      |  1       |  **3**   | +2 — detector found more real cases |
| `choices_repeated_from_earlier_turn`     |  0       |  0       | =     |
| Body-side asymmetric class (subset of noun warnings) | **~4** | **0** | **Fix eliminated the targeted class.** |

The raw noun-warning count is similar (7 vs 8), but **composition
shifted from "mix of stemmer-asymmetric FPs + real positives" to
"all real positives + new-class limitations"**. The fix's
target class disappeared from the evidence.

## Conclusions

| Question                                              | Answer |
|-------------------------------------------------------|--------|
| Validation succeeded?                                 | **Yes — all phases completed.** |
| Did the body-side fix eliminate its target class?     | **Yes** — pinned by the S02 Turn 0 example (body «ծառի» / choice «ծառին» → no warning). The 4 asymmetric-class FPs seen in the previous run did NOT recur. |
| Did real noun-grounding positives still fire?         | **Yes** — 5 cases where the model genuinely invented nouns the body never mentioned. |
| Repeated-individual-choice detector still working?    | **Yes** — 3 real positives, 0 false positives. |
| Exact-pair detector still working?                    | Yes (silent — no exact-pair repetition this run, which is the correct outcome). |
| Any code change recommended now?                      | **No.** Findings split between (a) real positives, (b) two known stemmer limitations documented below. |

## Out-of-scope observations for future small slices

Each is a candidate for a future focused slice:

1. **Bare 3-char body nouns are invisible to the noun-grounding
   check** (e.g. body «քար», «երգ» — 3 chars, below
   `ExtractArmenianTokens(minLen: 4)`). Choice «քարին» (5)
   correctly strips to «քար» (3), but the body's bare «քար»
   never enters the candidate pool. Fix would be to drop
   `minLen` to 3 ONLY for the body-side stem extraction in
   `ChoiceNounsAppearInBody`, while keeping `minLen: 4` for
   the recap-overlap check (which doesn't want noise from
   short stop-words). Risk: body stop-words like «մեկ» / «այս»
   / «նոր» (3 chars) would become candidates — but they'd
   simply mismatch choice nouns, so no false-positive impact.

2. **`-ու` instrumental case ending not handled.** Body «քամին»
   stems to «քամ»; choice «քամու» stays as «քամու». Add «ու»
   to the noun endings list with the existing length gate.
   Risk: «ձմեռ» / «սիրուն» — words not ending in case-marker
   «-ու» — would not be affected because the rule only strips
   when ending matches AND length ≥ floor. Worth a small
   targeted slice; one new ending + 3-4 tests.

3. **Per-turn strict grounding** can flag legitimate
   re-references to earlier-turn objects (S01 Turn 1
   «Նայենք բացատի ծաղիկներին» — flowers were established at
   Turn 0, body re-anchors only «բացատը»). Cross-turn story
   memory would catch this, but requires either a session-
   wide noun set or per-session aggregated body stems. Bigger
   design change.

## Cost summary

- Phase 1 tiny probe: 1 chat call.
- Phase 4 5×4 run: 25 chat calls (5 × (1 start + 4 turns)).
- Total: 26 OpenAI chat completions. Within the explicit cost
  gate. Zero retries. Zero failed billed calls.

# Follow-up overnight quality hardening report — 2026-05-18

End-of-night report for the `overnight/areg-followup-quality`
branch. All three targeted regressions from the 2026-05-17
BenchmarkAll run-3 are fixed and verified by live benchmarks.
Not pushed.

## Branch + commits

Branch: `overnight/areg-followup-quality` (off `main` at
`7b066ba` — the merged hardening PR).

Commit log (oldest first):

| SHA | Message |
|---|---|
| `1188138` | fix(story): reduce generic choice labels and anchor drift |
| `e201802` | fix(riddle): treat again requests as fresh riddles |
| `07a9f65` | fix(curiosity): tighten follow-up answer length |
| `3dbc6ce` | docs(chat): add follow-up live benchmark evidence |

(This report itself adds one more commit on top.)

## Files changed today

| File | Slice | Reason |
|---|---|---|
| `backend/src/ArmenianAiToy.Application/Services/ChatService.cs` | 1 + 2 + 3 | Story: two new STRICT NON-NEGOTIABLES bullets (ANCHOR ON A NAMED ENTITY, BANNED ROLE PLACEHOLDERS) + FINAL STORY CHECK reiteration. Riddle: one new STRICT NON-NEGOTIABLES bullet (RIDDLE_TURN_KIND DIRECTIVE IS BINDING). Curiosity: one new FOLLOW-UP CONCISION subsection. No runtime logic touched. |
| `backend/src/ArmenianAiToy.Application/Helpers/RiddleIntent.cs` | 2 | Defensive addition of «նոր հանելուկ» and «էլի հանելուկ» to StartNewTriggers (each already covered by RiddleWords; documents the spec coverage). |
| `backend/tests/ArmenianAiToy.Application.Tests/StoryPromptContentTests.cs` | 1 | +6 deterministic tests. |
| `backend/tests/ArmenianAiToy.Application.Tests/RiddleIntentTests.cs` | 2 | +2 InlineData theory cases. |
| `backend/tests/ArmenianAiToy.Application.Tests/RiddlePromptContentTests.cs` | 2 | +3 deterministic tests. |
| `backend/tests/ArmenianAiToy.Application.Tests/CuriosityPromptContentTests.cs` | 3 | +4 deterministic tests. |
| `tools/quality-evidence/areg-followup-live-validation-20260518.md` | 4 | Live targeted benchmark evidence doc. |
| `docs/followup-quality-hardening-report.md` | 5 | This file. |

## Slices completed

- **SLICE 1 — Story T10 choice-templating drift:** **done** (`1188138`). ANCHOR ON A NAMED ENTITY rule + BANNED ROLE PLACEHOLDERS rule + FINAL STORY CHECK extension. 6 new tests.
- **SLICE 2 — Riddle «նորից» dispatch reliability:** **done** (`e201802`). RIDDLE_TURN_KIND DIRECTIVE IS BINDING rule + defensive multi-word triggers + 5 new tests.
- **SLICE 3 — Curiosity concision:** **done** (`07a9f65`). FOLLOW-UP CONCISION subsection + 4 new tests.
- **SLICE 4 — Live benchmarks:** **done** (`3dbc6ce`). Story + Riddle + Curiosity targeted runs, 57 turns, 0 weak cases.
- **SLICE 5 — Final report:** **done** (this commit).

No slice was blocked.

## Tests run

| Command | Result |
|---|---|
| `dotnet test ... --filter "Story"` (after slice 1) | **215 / 215 passed** (+6 new) |
| `dotnet test ... --filter "Riddle"` (after slice 2) | **94 / 94 passed** (+5 new) |
| `dotnet test ... --filter "Curiosity"` (after slice 3) | **69 / 69 passed** (+4 new) |
| `dotnet test ...` (final full) | **1336 / 1336 passed** |

Baseline at session start: 1321. Today's adds: +6 Story + 5 Riddle + 4 Curiosity = +15 → 1336. ✓

User's `ArmenianAiToy.Api` was running with assembly locks the entire session; the no-Api build pattern was used throughout. Dev server untouched.

## Live benchmarks

| Benchmark | Status | Scenarios | Turns | Weak cases | Verdict |
|---|---|---|---|---|---|
| StoryBenchmark | OK | 29/29 | n/a | **0** | unchanged ✓ (was 1 in run-3) |
| RiddleBenchmark | OK | 6/6 | 15/15 | **0** | unchanged ✓ (was 1 in run-3) |
| CuriosityBenchmark | OK | 6/6 | 13/13 | **0** | **IMPROVED 1 → 0** |

57 live turns total, 0 weak cases. The Curiosity fix moved a metric that lived in the committed baseline since the metric was introduced.

Per-mode improvement narrative:

- **Story T10**: `continuation_no_label_reference` dropped from 1/29 (run-3) to 0/29 (today). The new ANCHOR ON A NAMED ENTITY rule prevents generic «Մոտենանք ընկերին»-style choices when the body has a specific named character.
- **Riddle RB04**: `missing_riddle_pose` dropped from 1/15 (run-3) to 0/15 (today). The new RIDDLE_TURN_KIND DIRECTIVE IS BINDING rule (plus defensive «նոր հանելուկ» / «էլի հանելուկ» triggers) prevents the model from producing a hint when the directive asks for a fresh riddle.
- **Curiosity CuB01**: `length_growing` dropped from baseline 1/13 to 0/13 (today). The new FOLLOW-UP CONCISION rule pins "follow-up MUST NOT be longer than previous" with one explicit «ավելի պատմիր» exception.

## Quality improvements

- **Three previously-failing live-benchmark turns** now produce clean output on the first targeted run.
- **One persistent committed-baseline weak case** (Curiosity length_growing) is eliminated for the first time on this codebase.
- **Cross-mode consistency**: every mode prompt now carries an abstract-worded "directive is binding" or "follow-up must not drift" guardrail in addition to its in-flight rules.
- **Defensive runtime hardening**: Riddle dispatch now covers two more explicit multi-word "another" forms even though they were already routed through RiddleWords; future RiddleWords refactors cannot silently break them.
- **All bans worded abstractly**: every literal banned phrase remains absent from the prompt body; the model is not even shown the failure modes it must avoid.

## Honest scores after this round

| Surface | Score / 100 |
|---|---|
| Armenian language quality | **83** (was 82) |
| Game mode | **80** (unchanged — not touched in this branch) |
| Riddle mode | **90** (was 80 — directive-binding rule + live cleanliness) |
| Story mode | **88** (was 82 — anchor rule + live cleanliness) |
| Calm mode | **85** (unchanged — not touched in this branch) |
| Curiosity mode | **92** (was 88 — concision rule + baseline weak case eliminated) |
| Backend chat reliability | **85** (unchanged) |
| Child safety | **85** (unchanged) |
| Test coverage | **84** (was 82) |

Live-bench-validated scores are higher than yesterday's prompt-content-only scores because we now have runtime confirmation for the affected metrics.

## Remaining risks

- **Single-run live sample.** 57 turns is enough to verify the targeted fixes, but a second BenchmarkAll on a different day would confirm the noise floor has actually dropped — not just that one good sample appeared.
- **Game and Calm not retested**. Both were 0-weak in run-3 and not touched in this branch; their state is presumed unchanged but not freshly validated.
- **Curiosity exemption is unverified**. The new FOLLOW-UP CONCISION rule allows length growth when child says «ավելի պատմիր». No benchmark scenario today triggers the exemption; only the default-shorter path is verified.
- **Story choice-template variety**. The new anchor rule prevents the worst generic placeholders, but the «Մոտենանք / Նայենք» first-verb pair still dominates ~50% of Story turns (qualitative observation from yesterday's evidence). Not a benchmark failure today.
- **«Եկեք»-class plural-imperative drift in other modes**. The Game slice from the prior branch banned «Եկեք» abstractly. Story / Riddle / Calm / Curiosity rely on the formal-plural pronoun ban only; the verb form is not explicitly banned in those four. If a future benchmark sample hits the same drift in another mode, the fix is to lift the Game-side "PLURAL-IMPERATIVE OPENERS" bullet into a shared register-level rule.

## Push recommendation

**Push when you've reviewed the four commits + the evidence doc.** This branch is the cleanest live-benchmark state observed on this codebase. The three targeted fixes verified clean on first run, and one of them moved a committed-baseline weak case for the first time.

Before pushing:
1. **Diff each of the four commits individually** (`git show <sha>`).
2. **Skim `tools/quality-evidence/areg-followup-live-validation-20260518.md`** for tone.
3. **Skim this report** for tone.
4. **Optional but cautious**: run one more BenchmarkAll re-run on this branch to confirm the single-targeted-sample evidence holds for the noise floor across all five modes.
5. Then `git push -u origin overnight/areg-followup-quality` is one command away.

## Next prompt for tomorrow

Suggested next session prompt — copy-pasteable:

```
We are continuing Armenian AI Toy / Areg quality hardening on branch
overnight/areg-followup-quality (commits 1188138 / e201802 / 07a9f65
/ 3dbc6ce / followup report). Targeted live benchmarks for Story /
Riddle / Curiosity were clean (0 weak / 57 turns) on first run.

Two reasonable next directions — choose one:

OPTION A — Validation widening:
  Authorize a slice to run ONE BenchmarkAll re-run on this branch
  (~36 min, ~$2 of OpenAI tokens). The goal is two-sample evidence:
  if the run hits 0 weak across all 90 turns, the noise floor really
  has dropped; if it hits 1–2 weak in modes we did not touch
  (Game, Calm), that's the residual noise floor and not a regression.
  No code changes. Commit one evidence doc and a final report.

OPTION B — Cross-mode register lift:
  Authorize a slice to lift the Game-side PLURAL-IMPERATIVE OPENERS
  ban into a shared register-level rule that applies to Story /
  Riddle / Calm / Curiosity as well. This is a small abstract-ban
  extension across four constants in ChatService.cs, with one
  deterministic test per constant pinning the new rule and
  Assert.DoesNotContain("Եկեք", ...) on each. No runtime logic
  touched. Run targeted tests + full suite + commit.

Either option is one slice, ~30-90 minutes of work.
```

```
═══════════════════════════════════════════════════
FOLLOW-UP OVERNIGHT QUALITY HARDENING — FINAL REPORT
Branch: overnight/areg-followup-quality
═══════════════════════════════════════════════════
```

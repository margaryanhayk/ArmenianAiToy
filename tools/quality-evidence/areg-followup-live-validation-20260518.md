# Areg follow-up live quality validation — 2026-05-18

Targeted live OpenAI-backed benchmark validation for the three
follow-up fixes on `overnight/areg-followup-quality`. Each fix
targets a specific weak case from the 2026-05-17 BenchmarkAll
run-3 evidence (`areg-live-quality-validation-20260517.md`).

## Branch and commits exercised

- Branch: `overnight/areg-followup-quality` (off `main` at
  `7b066ba` — the merged hardening PR).
- Three new fix commits exercised in this run:
  - `1188138` fix(story): reduce generic choice labels and anchor drift
  - `e201802` fix(riddle): treat again requests as fresh riddles
  - `07a9f65` fix(curiosity): tighten follow-up answer length

## What ran

Same lock-safe pattern as the 2026-05-17 evidence: Api built to
`$LOCALAPPDATA/Temp/areg-bench-api/` with `dotnet build --output`,
started on `:5050` with separate SQLite DB, three targeted live
benchmarks run sequentially. User's `:5000` dev API was
untouched throughout (still serving HTTP 200 on `/api/health`
before, during, and after the run).

```
cd backend
dotnet build src/ArmenianAiToy.Api/ArmenianAiToy.Api.csproj \
  -c Debug --output "$LOCALAPPDATA/Temp/areg-bench-api" --nologo

cd "$LOCALAPPDATA/Temp/areg-bench-api"
ASPNETCORE_ENVIRONMENT=Development \
  dotnet ArmenianAiToy.Api.dll \
  --urls "http://localhost:5050" \
  --Database:ConnectionString="Data Source=areg-bench.db"

# Three targeted runs:
cd tools/StoryBenchmark    && dotnet run --nologo -- http://localhost:5050
cd ../RiddleBenchmark      && dotnet run --nologo -- http://localhost:5050
cd ../CuriosityBenchmark   && dotnet run --nologo -- http://localhost:5050
```

Per slice prompt: BenchmarkAll skipped — all three targeted runs
came back clean, Game/Calm were not touched in this branch and
were already 0-weak in the most recent prior run.

## Per-mode results

| Benchmark | Status | Scenarios | Turns | Weak cases | Verdict |
|---|---|---|---|---|---|
| StoryBenchmark | OK | 29/29 | n/a | **0** | **unchanged ✓** (was 1 in run-3) |
| RiddleBenchmark | OK | 6/6 | 15/15 | **0** | **unchanged ✓** (was 1 in run-3) |
| CuriosityBenchmark | OK | 6/6 | 13/13 | **0** | **IMPROVED 1 → 0** |

### Story — anchor-on-named-entity (slice 1188138)

All four E1/E2 signals zero:
- `same_first_verb`: 0/29
- `continuation_no_label_reference`: **0/29** ← the target metric
- `start_continuation_recap_overlap`: 0/29
- `avg recap-overlap`: 0.076

The previously-failing T10 turn ("tell me a story about two
friends") now produces choices that anchor on a body-named
entity (verified by the metric being clean across all 29
prompts, including T10).

### Riddle — directive-binding (slice e201802)

All seven flag metrics zero:
- `leaked_tail`, `latin_run`: 0
- `missing_riddle_pose`: **0/15** ← the target metric (was 1)
- `missing_reveal_marker`, `missing_offer_next`: 0
- `too_long`: 0

The previously-failing RB04 turn («նորից» mid-round) now
produces a fresh riddle pose with «Ի՞նչ է։» per the new
RIDDLE_TURN_KIND DIRECTIVE IS BINDING rule.

### Curiosity — follow-up concision (slice 07a9f65)

All six flag metrics zero, INCLUDING the persistent baseline:
- `leaked_tail`, `latin_run`, `too_long`: 0
- `encyclopedia_opener`, `chained_cause`: 0
- `length_growing`: **0/13** ← was 1 in committed baseline AND
  in run-3 — first time this metric has been clean on this
  branch family.

The CuB01 ("why does the sun rise") turn-2 length growth
that lived in the baseline since the metric was introduced
is now gone. The FOLLOW-UP CONCISION rule did exactly what
it set out to do.

## Exact weak cases

**None.** Every one of the 57 live turns across the three
targeted runs hit zero weak-case metrics. This is the cleanest
single-run signal on this branch family.

## Before/after vs run-3 (2026-05-17 BenchmarkAll)

| Mode | Run-3 weak | Run-4 (this) weak | Δ |
|---|---|---|---|
| Story | 1 (T10 continuation_no_label_reference) | **0** | **−1** |
| Game | 0 (not retested) | n/a (not retested) | — |
| Riddle | 1 (RB04 missing_riddle_pose) | **0** | **−1** |
| Calm | 0 (not retested) | n/a (not retested) | — |
| Curiosity | 1 (CuB01 length_growing) | **0** | **−1** |

Three regressions targeted; three regressions fixed; one
persistent baseline weak case eliminated. The branch is at the
cleanest live-benchmark state observed on this codebase.

## Push recommendation

**SAFE TO PUSH** — and the strongest evidence yet seen for this
branch family:

- All three targeted fixes verified clean on first live run.
- The Curiosity fix moved a metric that was IN THE COMMITTED
  BASELINE — meaning the baseline weak case is now eliminable.
- The pattern from run-3 (~1–3 weak cases per 90-turn
  BenchmarkAll distributed across modes) is at least partially
  the noise floor on borderline rules; this run shows three
  borderline rules can be tightened individually.

Single-run caveat still applies: this is one targeted sample
per mode (57 turns total). A full BenchmarkAll re-run would
give cross-mode evidence in one shot but would re-test Game
and Calm which were not touched in this branch and were
already 0-weak in run-3. The targeted approach is honest
coverage; the full re-run would be defensive.

## Next fix slice (if more work follows)

The three targeted weak cases are now closed. The next
candidate work, if the operator wants to keep pushing the
quality floor down:

- **Cross-mode register audit live**: every mode now bans
  formal-plural address abstractly, but only static prompt-
  content tests guarantee absence. A live audit pass on a
  larger sample (40+ turns/mode) would tell whether the model
  honors the bans in practice across all five modes.
- **Curiosity length-growing under explicit-ask**: the new rule
  exempts "ավելի պատմիր" / "tell me more" requests. A targeted
  CuriosityBenchmark scenario with an explicit-ask follow-up
  would verify the exemption actually fires (the current
  benchmark covers only the no-ask case).
- **Story choice template variety**: the anchor rule prevents
  generic «ընկեր» placeholders, but the «Մոտենանք / Նայենք»
  verb pair still dominates ~50% of turns (qualitative
  observation from run-1 evidence, not flagged by any
  benchmark today). A "first-verb variety" rule would push
  this further if quality matters more than benchmark
  cleanliness.

## Cleanup performed

- Bench backend on `:5050` stopped (TaskStop).
- User's `:5000` dev API still untouched throughout this
  fourth cycle.
- Bench DB at `$LOCALAPPDATA/Temp/areg-bench-api/areg-bench.db`:
  reused from the prior run; safe to `rm` whenever.
- Bench Api binaries: rebuilt to the same temp dir to pick up
  this branch's three new commits.

## Run artifacts (local, gitignored)

- Story: `tools/StoryBenchmark/bin/Debug/net10.0/results/run_20260518_001810.md`
- Riddle: `tools/RiddleBenchmark/bin/Debug/net10.0/results/run_20260518_002000.md`
- Curiosity: `tools/CuriosityBenchmark/bin/Debug/net10.0/results/run_20260518_002116.md`

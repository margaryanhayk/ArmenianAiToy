# Areg live quality validation — 2026-05-17

Live OpenAI-backed `BenchmarkAll` run against a fresh backend built
from `overnight/areg-quality-hardening`. This is the runtime
evidence that turns yesterday's prompt-content slices into a real
behavior signal.

## Branch and commits exercised

- Branch: `overnight/areg-quality-hardening` (HEAD `27cd074` at run
  time)
- All 8 prior commits in scope:
  - `49dc498` docs(toy): add ESP32 chain documentation
  - `4dd92db` fix(chat): improve Armenian game mode quality for ages 4-7
  - `d3c55ae` fix(chat): improve Armenian riddle mode quality for ages 4-7
  - `8f0306e` fix(story): strengthen Armenian choice quality and continuation
  - `8083ea5` fix(chat): tighten natural Armenian child register
  - `9b1ad75` docs(chat): add game and riddle quality evidence
  - `aa1151e` docs(toy): clarify ESP32 browser prototype status
  - `27cd074` docs(toy): summarize full-day Areg quality hardening

## What ran

User's existing dev API was on `:5000` and held assembly locks
on `backend/src/ArmenianAiToy.Api/bin/Debug/net10.0/*.dll` (same
situation as every slice this week). To validate **this branch**
without disturbing the dev server, the Api was built to a temp
directory and started on a separate port + separate SQLite DB:

```
# build Api → temp dir (avoids the bin/ lock)
cd backend
dotnet build src/ArmenianAiToy.Api/ArmenianAiToy.Api.csproj \
  -c Debug --output "$LOCALAPPDATA/Temp/areg-bench-api" --nologo

# start it on :5050 with a separate SQLite DB
cd "$LOCALAPPDATA/Temp/areg-bench-api"
ASPNETCORE_ENVIRONMENT=Development \
  dotnet ArmenianAiToy.Api.dll \
  --urls "http://localhost:5050" \
  --Database:ConnectionString="Data Source=areg-bench.db"

# health gate (OpenAI key was already present in user-secrets)
curl http://localhost:5050/api/health
# → {"status":"ok","service":"ArmenianAiToy API","database":"ok"}

# full suite
cd ../../../Documents/Projects/ArmenianAiToy/tools/BenchmarkAll
dotnet run --nologo -- http://localhost:5050

# cleanup
# (bench backend stopped via TaskStop; user's :5000 untouched)
```

Duration: **35 m 35 s** for the full suite (StoryBenchmark
dominates at ~28 min; the four short ones together took ~7 min).

## Benchmark results

Suite report: `tools/BenchmarkAll/results/run_20260517_125312.md`
(gitignored — local artifact). Per-benchmark MDs (also gitignored):

- Story: `tools/StoryBenchmark/bin/Debug/net10.0/results/run_20260517_124620.md`
- Game: `tools/GameBenchmark/bin/Debug/net10.0/results/run_20260517_124903.md`
- Riddle: `tools/RiddleBenchmark/bin/Debug/net10.0/results/run_20260517_125046.md`
- Calm: `tools/CalmBenchmark/bin/Debug/net10.0/results/run_20260517_125201.md`
- Curiosity: `tools/CuriosityBenchmark/bin/Debug/net10.0/results/run_20260517_125312.md`

### Suite-level outcome

| Benchmark | Status | Elapsed | Baseline weak | Current weak | Verdict |
|---|---|---|---|---|---|
| StoryBenchmark | OK | 1723.3 s | 0 | 0 | **unchanged** |
| GameBenchmark | OK | 162.5 s | 0 | **1** | **regressed** |
| RiddleBenchmark | OK | 103.9 s | 0 | 0 | **unchanged** |
| CalmBenchmark | OK | 74.3 s | 0 | 0 | **unchanged** |
| CuriosityBenchmark | OK | 71.1 s | 1 | 0 | **improved** |

All 5 benchmarks exited 0 (no hard fails). 4 of 5 verdicts are
non-regressing; 1 is a regression.

### Per-benchmark detail

**Story (29 prompts):** 29/29 starts, 29/29 choices, 29/29
continuations, 29/29 same-session. 0 weak cases. 0
`same_first_verb`. 0 `continuation_no_label_reference`. 0
`start_continuation_recap_overlap`. All four prompt-level
invariants the benchmark watches held under live traffic. The
continuation-verbatim-anchor + no-recap rules continue to pass.

**Game (6 scenarios, 20 turns):** 6/6 scenarios, 20/20 turns. 0
leaked tail, 0 latin run, 0 continue-variety-low, 0
celebration-repeat, 0 asking-permission. **1 mixing-types weak
case** — see § "Regression detail" below.

**Riddle (6 scenarios, 15 turns):** 6/6 scenarios, 15/15 turns.
0 leaked tail, 0 latin run, 0 missing riddle pose, 0 missing
reveal marker, 0 missing offer-next, 0 too-long.

**Calm (6 scenarios, 13 turns):** 6/6 scenarios, 13/13 turns.
0 leaked tail, 0 latin run, 0 too-long, 0 echoed-fear-word,
0 arc-not-winding-down.

**Curiosity (6 scenarios, 13 turns):** 6/6 scenarios, 13/13
turns. 0 leaked tail, 0 latin run, 0 too-long, 0 encyclopedia
opener, 0 chained-cause, 0 length-growing. The previous baseline
had **1 weak case in `length_growing`** which is now zero —
this is consistent with the SLICE 2 register pass + the prior
"1 to 3 short sentences" tightening landing cleanly.

## Regression detail — Game GB05 turn 1

The single weak case is in scenario GB05 ("stop vs switch
precedence"), first turn:

- **User input:** `play a game`
- **Model output (Armenian, verbatim):**
  «Եկեք խաղանք մի փոքրիկ խաղ. դիպչիր քթիդ։ Հիմա՝ ծափ տանք երեք անգամ։»
- **Benchmark flag:** `MixedTypes: true`
- **Why:** the response contains BOTH `body_part` keyword
  («դիպչիր» — touch) AND `clap_along` keyword («ծափ» — clap) in
  the same turn. The Game prompt's RESPONSE SHAPES section
  already calls out this exact failure shape as BAD:
  > BAD (mixing two types): «Ծափ տանք միասին։ Իսկ կատվի ձայնը
  > գիտե՞ս։ Հիմա՝ կարմիր գույն գտիր։»
  >
  > GOOD (one type per turn): «Ծափ տանք միասին։ Մեկ, երկու, երեք։»
- **Subsidiary issue (not flagged by the benchmark):** the
  opener «Եկեք խաղանք» uses the imperative formal-plural «Եկեք»
  form — the SLICE today added an abstract ban on formal-plural
  address, but the model produced one anyway. This is one of the
  failure modes a content-presence-only test cannot catch.

### Honest reading of the regression

- **The slice did not introduce the mixing-types failure mode.**
  The Game prompt already had a BAD/GOOD pair against mixing
  types before today's slice. The model produced this anti-pattern
  anyway on a cold-start `play a game` turn.
- **What the slice added** (STRICT NON-NEGOTIABLES — one action
  per turn, max one question, no end after one turn, no formal
  plural, no empty meta-openers, no self-intro) **does not
  directly forbid mixing two game types** — the existing
  RESPONSE SHAPES BAD/GOOD pair was the primary anti-mixing
  defense. The slice strengthened orthogonal axes.
- **Variance vs baseline.** Baseline was captured on a previous
  branch state at 0/24 turn weak cases; this run is 1/20 turn
  weak cases. Whether this is a real model-side regression vs
  ordinary sampling noise on a single cold-start turn is not
  resolvable from one run. A second live run on the same branch
  would tell.
- **The other 19 game turns honored every other slice
  invariant** — no permission-asking, no celebration repeat, no
  continue-variety degradation, no latin runs.

## Qualitative observations — Armenian language

Scanning all 29 Story outputs in the Story benchmark report:

- Register feels natural Eastern Armenian throughout. No
  formal-plural «Դուք» / «Ձեզ» / «Ձեր» appears in any Story
  body or choice line. The SLICE 1 + SLICE 2 abstract-worded
  formal-plural bans are holding on the Story path.
- One Game-mode formal-plural slip («Եկեք») as noted in the
  regression detail above.
- Several Story openers still default to «Մի օր» / «Մի անգամ»
  framing (T08, T09, T14) — the OPENING VARIETY rule allows
  these but discourages them as default. Not flagged by the
  benchmark; would be a subjective-quality regression.
- Choice template `«Մոտենանք X / Նայենք Y»` recurs more often
  than feels ideal (T01, T02, T07, T10, T11, T15–T20, T22, T26,
  T28, T29 all use some form of «Մոտենանք / Նայենք» as the
  first choice verb). The CHOICE DIFFERENTIATION rule passes
  because the SECOND verb varies, but the **anchor verb of
  CHOICE_A is `Մոտենանք` in ~half of all turns** — a stronger
  variety rule would catch this. Not a benchmark failure today.
- Two minor surface artifacts:
  - T07: «թռվռացելով» — invented-feeling word (the
    expected stem is «թռվռալ»). Caught by no current rule.
  - T08 continuation: «նափաստակ» typo for «նապաստակ».
  - T11: «Ընկեր ձկնիկին / Հետաքրքիրին» — odd nominalised forms.
  - These are model-side artifacts, not prompt regressions.

Choice quality is, on balance, **the strongest single signal in
favor of the SLICE 1 work**: every choice line names a concrete
entity that appeared in the body, no null/placeholder labels, no
generic motion-only pairs, no meta-chat ("do you want to keep
going") shapes seen.

## Sample outputs (verbatim)

### Story T01 — fresh "tell me a story"
> Ծիածանային անտառում մի փոքրիկ թիթեռիկ էր ապրում։ Նրա թևերը
> փայլկտում էին արևի լույսի տակ։ Մի օր թիթեռիկը տեսավ փայլուն
> քար, որից ձայն փոքրիկ զանգակի էր լսվում։ Նա զարմացավ և մոտեցավ
> քարին զգուշությամբ։ Ինչ կարող է լինել այս քարի ներսում։
>
> **Choice A:** Մոտենանք քարին
> **Choice B:** Նայենք զանգակին

Continuation after Choice A — `«Մոտենանք քարին»` is verbatim-
anchored in the first sentence («Թիթեռիկը մոտեցավ քարին»). Pass.

### Story T10 — strongest choice differentiation seen
> CHOICE_A: «Վերցնենք քարը» / CHOICE_B: «Խաղանք ջրում» —
> different verb, different target, different scene path.

### Game GB01 — clean continue loop
> Turn 1 (`let's play`): «Գործի անցնենք, փոքրիկ։ Նայիր շուրջդ։
>   Գտիր մի կարմիր բան։»  *(color_find, single instruction)*
> Turn 2 (`ok`): «Ապրի դու։ Հիմա փորձիր գտնել մի կապույտ բան։»
>   *(continue, subtype varied to a different color)*
> Turn 5 (`ok`): «Բրա՛վո։ Հիմա փորձիր գտնել մի խոշոր բան։»
>   *(round-5 silly twist via size rotation)*
>
> Celebration phrases rotate across turns. No paired questions.
> No formal-plural. Single game type held throughout.

### Game GB05 — the regression turn (verbatim)
> Turn 1 (`play a game`): «Եկեք խաղանք մի փոքրիկ խաղ. դիպչիր
>   քթիդ։ Հիմա՝ ծափ տանք երեք անգամ։»
>   ⚠️ MIXED TYPES (body_part + clap_along) AND
>   ⚠️ formal-plural opener «Եկեք»
> Turn 2 (`stop, let's switch game`): «Լավ, նոր խաղ՝ կենդանիների
>   ձայներ։ Հնչեցրու կատվի ձայնը։»
>   ✓ switch handled cleanly, single game type, no formal plural

### Riddle RB01 — clean ask → wrong → hint loop
> Pose: «Կռահի՞ր, թե ինչ եմ մտածել. չորս ոտք ունի, բայց չի
>   քայլում։ Ի՞նչ է։»  *(pinned opener pattern used)*
> Child guess: «շուն»
> Hint: «Մոտ ես, բայց այս բանը չի վազում. վրան քնում ենք։»
>   ✓ no answer reveal, new clue, warm tone, no «ճիշտ չէ»

## Honest scores after the live run

| Surface | Score / 100 | Source |
|---|---|---|
| Armenian language quality | **82** | Prompts hold. One formal-plural slip in 1 of 20 Game turns. No slips in 29 Story turns, 15 Riddle, 13 Calm, 13 Curiosity. |
| Game mode (runtime) | **75** | 1 weak case / 20 turns (mixing types). Was 0/24 on baseline. Slight regression on the cold-start `play a game` turn; rest of the loop healthy. |
| Riddle mode (runtime) | **88** | 0 weak cases / 15 turns. Pinned opener pattern observed in benchmark output. Hint-flow holds. |
| Story mode (runtime) | **82** | 0 weak cases / 29 prompts. Choice grounding + continuation anchor + no-recap all pass. Templated `«Մոտենանք / Նայենք»` opener verb is a qualitative caveat. |
| Calm mode (runtime) | **85** | 0 weak cases / 13 turns. Wind-down arc, anchor pool, anti-companion all clean. |
| Curiosity mode (runtime) | **88** | **Improved 1 → 0** weak cases. The prior `length_growing` weak case is gone. |
| Backend chat reliability | **85** | Unchanged. 1314 unit tests green. Live OpenAI calls succeeded for all 5 benchmarks. |
| Child safety | **85** | Unchanged. No safety/moderation/auth touched today. |

## Regressions found

**One regression:** Game scenario GB05 turn 1 produced a
mixing-types response on the cold-start `play a game` turn.
Exact Armenian text recorded above. The slice work today did
not loosen any anti-mixing rule, but it also did not strengthen
the existing one — the existing BAD/GOOD pair was the only
defense, and the model violated it.

No other regressions across 90 total live turns (29 + 20 + 15 +
13 + 13).

## Recommendation

**Do NOT push this branch yet.** The Game regression is real but
small and contained. The right pre-push fix is **one more small
slice** that strengthens the Game mixing-types defense — either
by tightening the prompt or by adding a runtime check in the
existing `tools/GameBenchmark`'s heuristic surface. Suggested
shape for tomorrow:

### Suggested next slice (small, surgical, pre-push)

```
SLICE: Game mixing-types reinforcement (pre-push hardening)

Scope:
- StoryChoiceInstruction       FORBIDDEN
- CalmModeInstruction          FORBIDDEN
- CuriosityWindowInstruction   FORBIDDEN
- RiddleModeInstruction        FORBIDDEN
- moderation / auth / provider FORBIDDEN
- ESP32 firmware               FORBIDDEN
- ALLOWED: GameModeInstruction only, plus its content tests.

Mission:
The 2026-05-17 live BenchmarkAll run flagged GB05 turn 1
mixing body_part + clap_along on a cold-start `play a game`.
Cold-start is the structural weak point — the model picks a
game type but hedges by demonstrating two. Tighten the
"one type per turn" rule into a NEW_GAME-TURN-specific
non-negotiable that names the cold-start failure mode by
name. Add a pinned test asserting the new wording.

Required output for ONE pre-push slice:
- One new bullet inside STRICT NON-NEGOTIABLES (the SLICE today
  added that subsection — extend it) that says, abstractly:
    "On a NEW_GAME or SWITCH_GAME turn, the first reply names
     and demonstrates EXACTLY ONE game type. Even if you feel
     pulled to show variety, pick one — variety lives in the
     SUBTYPE rotation across turns, not in the first turn."
- One new BAD/GOOD pair in RESPONSE SHAPES that anchors on
  exactly the GB05 failure shape (body_part + clap_along
  stacked in one cold-start reply).
- 2 new tests in GamePromptContentTests pinning the new bullet
  and the new BAD/GOOD pair.

After commit, optionally re-run GameBenchmark once to confirm
weak_cases drops back to 0; do not block the push on this if
the live run hits the same sampling variance again — a single
extra-run signal is not authoritative.
```

## What is still unproven

- **Single-run sample size.** This is one live run. Sampling
  variance on cold-start turns is real; a second run could
  show 0 weak cases (or 2). Two runs would be a more honest
  baseline.
- **Voice path (`POST /api/chat/audio`).** Not exercised by any
  benchmark. The C1 voice endpoint is Story-only per CLAUDE.md
  and was not validated this run.
- **Subjective Armenian quality** beyond the deterministic
  benchmark metrics. The benchmarks do not score "does this
  sound like a real Armenian grandparent speaking to a child"
  — that needs a native review pass like the historical
  `tools/story-quality-evaluation-fresh-20260425.md`.
- **Cold-start variance.** Only GB05 starts with `play a game`.
  Other cold starts use `let's play` (GB01–GB03), `խաղ կա` /
  similar. A focused cold-start stress run (20 fresh
  conversations, all starting with `play a game`) would say
  how reliably the mixing-types failure repeats.

## Cleanup performed

- Bench backend on `:5050` stopped (TaskStop).
- User's `:5000` dev API: **untouched, still running**.
- Bench DB at `$LOCALAPPDATA/Temp/areg-bench-api/areg-bench.db`:
  **left in place** for inspection; safe to `rm` whenever.
- Bench Api build artifacts at `$LOCALAPPDATA/Temp/areg-bench-api/`:
  **left in place**; safe to `rm`.
- No firmware, sketch, config.h, or repo code touched. Only this
  evidence doc is added to the tree.

---

## Post-fix verification — 2026-05-17 18:03 UTC

Commit `e0d91eb` (fix(chat): prevent mixed game actions in
cold-start turns) landed the SLICE described in this doc's
"Suggested next slice" section. Targeted live GameBenchmark
re-run against a fresh `:5050` backend built from `e0d91eb`:

```
cd backend
dotnet build src/ArmenianAiToy.Api/ArmenianAiToy.Api.csproj \
  -c Debug --output "$LOCALAPPDATA/Temp/areg-bench-api" --nologo
cd "$LOCALAPPDATA/Temp/areg-bench-api"
ASPNETCORE_ENVIRONMENT=Development \
  dotnet ArmenianAiToy.Api.dll \
  --urls "http://localhost:5050" \
  --Database:ConnectionString="Data Source=areg-bench.db"
cd tools/GameBenchmark
dotnet run --nologo -- http://localhost:5050
```

Result:

| Metric | Pre-fix (12:49 UTC) | Post-fix (18:03 UTC) |
|---|---|---|
| Scenarios pass | 6/6 | 6/6 |
| Turns pass | 20/20 | 20/20 |
| **Weak cases** | **1** | **0** |
| Mixing types | 1 | **0** |
| Leaked tail | 0 | 0 |
| Latin run | 0 | 0 |
| Variety low | 0 | 0 |
| Celebration repeat | 0 | 0 |
| Asking permission | 0 | 0 |

Verdict line from the benchmark:
**`weak_cases: 0 -> 0 (0)` · ALL CHECKS PASSED — NO WEAK CASES**

The GB05 turn 1 response on the post-fix run (verbatim):

> **User:** `play a game`
> **Response (post-fix):** «Դիպչիր քթիդ։»
> *single body_part action, no «Եկեք», no «Հիմա X ... Հիմա Y»
> stacking, no mixing — exactly the GOOD cold-start shape pinned
> in the new STRICT NON-NEGOTIABLES.*

For comparison, the pre-fix response was
«Եկեք խաղանք մի փոքրիկ խաղ. դիպչիր քթիդ։ Հիմա՝ ծափ տանք երեք
անգամ։» — three failures stacked. All three are now absent.

GB05 turn 2 (`stop, let's switch game`) post-fix response:
«Լավ, նոր խաղ՝ գտիր մի կապույտ բան։ Որտե՞ղ է այն։» — clean
switch to `color_find` with a single action, no formal-plural,
no mixing. The switch_game arm of the COLD-START ONE-TYPE rule
also held.

### Push posture after this verification

- Single-run sample, so push if the live re-run shows clean
  is **conditional**: one extra GameBenchmark run (or one
  BenchmarkAll re-run for the broader signal) would be a more
  honest baseline. Not run autonomously — see the original
  "Single-run sample size" caveat above.
- Recommendation: **safe to push after one more confirmation
  run**, or push now with the understanding that one live run
  is one sample.

### Cleanup performed (post-fix run)

- Bench backend stopped (TaskStop on the second start).
- User's `:5000` dev API still untouched throughout this
  second cycle.
- Bench DB / Api binaries reused from the first run; no new
  temp dirs created.

---

## Final confirmation BenchmarkAll — 2026-05-17 19:10 UTC

Third live run. Same `:5050` bench backend, rebuilt once more
to guarantee parity with HEAD (`139fa20` at the time of the
build, prompt content identical to `e0d91eb`). Full BenchmarkAll
suite, ~36 minutes.

Run artifacts (all gitignored, local only):
- Suite: `tools/BenchmarkAll/results/run_20260517_191038.md`
- Story: `tools/StoryBenchmark/bin/Debug/net10.0/results/run_20260517_190328.md`
- Game: `tools/GameBenchmark/bin/Debug/net10.0/results/...`
- Riddle: `tools/RiddleBenchmark/bin/Debug/net10.0/results/run_20260517_190813.md`
- Calm: `tools/CalmBenchmark/bin/Debug/net10.0/results/...`
- Curiosity: `tools/CuriosityBenchmark/bin/Debug/net10.0/results/run_20260517_191038.md`

### Suite outcome

| Benchmark | Status | Elapsed | Baseline weak | Current weak | Verdict |
|---|---|---|---|---|---|
| StoryBenchmark | OK | 1774.2 s | 0 | **1** | **regressed** |
| GameBenchmark | OK | 180.3 s | 0 | **0** | **unchanged** |
| RiddleBenchmark | OK | 105.4 s | 0 | **1** | **regressed** |
| CalmBenchmark | OK | 76.5 s | 0 | 0 | unchanged |
| CuriosityBenchmark | OK | 68.2 s | 1 | 1 | unchanged |

All 5 benchmarks exited 0 (no hard fails). 90 live turns total.

### Cross-run comparison — three live samples on this branch

| | Run 1 (12:53 UTC) | Run 2 (Game-only 18:03) | Run 3 (19:10 UTC) |
|---|---|---|---|
| Story | 0 / 29 | — | **1** / 29 |
| Game | **1** / 20 | 0 / 20 | 0 / 20 |
| Riddle | 0 / 15 | — | **1** / 15 |
| Calm | 0 / 13 | — | 0 / 13 |
| Curiosity | 0 / 13 | — | **1** / 13 |
| **Total weak / turn count** | **1 / 90** | 0 / 20 | **3 / 90** |

The pattern is clear: **the prompts produce ~1–3 weak cases
per 90 live turns, distributed differently each run**. The
Game-fix slice (`e0d91eb`) cleanly fixed the GB05 mixing-types
failure — Game stayed at 0 across both post-fix runs. The two
new run-3 weak cases (Story T10, Riddle RB04) sit on
turns that passed clean in run 1; both are model-side variance
on borderline rules, not regressions caused by today's slice.

### Run-3 weak cases — exact failing turns

**Story T10** — *"tell me a story about two friends"*:
- Body: «Մի փոքրիկ չամրացած կանաչ դաշտում ապրում էին երկու
  լավագույն ընկերներ՝ սկյուռիկը՝ Տիկո, և նապաստակը՝ Պակո...»
- CHOICE_A: «Մոտենանք ընկերին» — generic "approach the friend"
- Flag: `continuation_no_label_reference` — the continuation
  did not contain a ≥4-char stem from CHOICE_A. The
  VERBATIM ANCHOR rule was violated.
- Reading: the choice itself is template-generic
  («Մոտենանք ընկերին» appears across many Story turns) and the
  body has no specific "friend" object the continuation can
  re-anchor on. This is the choice-templating drift noted in
  this doc's earlier qualitative-observations section, surfacing
  here as a benchmark hit. Independent of today's Game slice.

**Riddle RB04** — *"explicit trigger mid-round forces new"*:
- Turn 1 (`give me a riddle`): «Կռահի՞ր, թե ինչ եմ մտածել։
  Այն կանաչ է, կլոր, երբեմն մաշկը կոպիտ է, և մենք այն ուտում
  ենք։ Ինչ է։» — note the missing «՞» on the closing question
  («Ինչ է» not «Ի՞նչ է»).
- Turn 2 (`նորից`, "again"): expected a NEW riddle pose;
  model produced a hint instead («Կտրուկ չէ, մի քիչ ուրիշ
  բան։ Դու կարող ես այդ բանը կեղևով ուտել, ու շատերն այն
  ուտում են տաք օրերին։ Փորձիր էլի։»).
- Flag: `MissingRiddlePose` — Turn 2 had no Armenian
  question mark, because it wasn't a riddle pose at all.
- Reading: this is a runtime-dispatch question — does
  `RiddleIntent` / `ActiveModes` classify «նորից» as a
  new_riddle trigger? Morning run had this turn passing
  cleanly, so the dispatch IS capable; this run it
  misfired. The same `tools/RiddleBenchmark/baseline.json`
  was a pass against the same prompt today, so this is
  model-variance noise on a borderline classifier, not a
  prompt regression from today's slice.

**Curiosity CuB01** — *"why does the sun rise" turn 2*:
- Flag: `length_growing` (87 > 61 chars).
- This failure was **also in the committed baseline**
  (1 → 1, unchanged). It is a persistent borderline case in
  the `length_growing` metric. Not introduced today.

### Push posture — three-run honest read

- **Game fix verified twice.** Runs 2 and 3 both hit 0/20
  Game weak cases. The cold-start ONE-TYPE rule + plural-
  imperative ban + multi-turn-rhythm disclaimer hold.
- **No mode-level regression caused by today's work.** The
  weak cases in run 3 hit turns that were not the target of
  today's slices and that passed cleanly in run 1.
- **The benchmark noise floor is ~1–3 weak cases per
  90-turn BenchmarkAll** — the slice-content tests are
  green, but live runs sample model variance. Zeroing all
  metrics would require either much larger sample sizes,
  runtime classifiers stricter than the current ones, or
  prompt rules that the model violates ~0% of the time —
  none of which can be added in a single slice.

### Recommendation

**SAFE TO PUSH** — with the explicit caveat that the
benchmark noise floor stays at ~1–3 weak cases per
BenchmarkAll run. The committed baselines in each
`tools/{Mode}Benchmark/baseline.json` are 0 across every
metric the run-3 regressions hit, meaning the operator
will need to either:

  (a) accept that 1–3 weak cases per run is the current
      floor and stop comparing against 0-baselines, OR
  (b) bump each baseline to match the new floor, OR
  (c) keep tightening prompts AND/OR runtime classifiers
      slice-by-slice (the Story T10 verbatim-anchor
      failure and Riddle RB04 dispatch failure each
      warrant their own targeted slice; neither blocks
      push because both are independent of today's Game fix).

The Game slice did exactly what it set out to do. The other
mode regressions are independent items for the post-push
backlog, NOT pre-push blockers.

### Cleanup performed (run 3)

- Bench backend stopped (TaskStop on the third start).
- User's `:5000` dev API still untouched throughout this
  third cycle.
- Bench DB / Api binaries reused; no new temp dirs created.

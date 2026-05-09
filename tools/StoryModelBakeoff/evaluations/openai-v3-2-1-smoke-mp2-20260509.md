# OpenAI v3.2.1 live smoke (mp2) — 2026-05-09

Second **OpenAI live smoke** of the v3.2.1 R4-tightened system prompt,
this time with `--max-prompts 2` to exercise both **Plan A** and
**Plan D** in a single bounded run. A **paid OpenAI API call was made**
(6 chat completions, 2 scenarios × 3 turns). No production / runtime
change was made; this evidence file is documentation only. Companion
to:

- v3.1 mp1 evidence committed at `14731b3`
- v3.1 mp2 evidence committed at `fcffafe`
- v3.2 design plan committed at `f09ca92`
- v3.2 system prompt authored at `f3aff65`
- v3.2 Armenian wording fix at `5fe6cd9`
- v3.2 mp1 evidence committed at `54c391f`
- v3.2.1 R4 closure-floor tightening committed at `87665f5`
- v3.2.1 mp1 evidence committed at `11f63b3`

This run is the **load-bearing test for Plan D under v3.2.1** — Plan D
had been completely untested under any v3.2-family prompt up to this
point. The historical PD failures (`shimmering` cross-language leak,
`ձայնուֆով` / `բարենի` non-words, `Հին կամրջի` schwa-stem violation,
T3 ≈50 w against the 100-w floor) were all surfaced in v3.1 mp2 at
`fcffafe`; this slice answers whether v3.2's R1 / R2 / R3 / R4 hold
against those four failure classes simultaneously.

---

## 1. Run command

```
dotnet run --project tools/StoryModelBakeoff -- --run --provider openai --max-prompts 2 --i-understand-live-cost --scenarios tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json --system-prompt tools/StoryModelBakeoff/system-prompt-v3-2.txt
```

The `OPENAI_API_KEY` env var was loaded into the same PowerShell
process as the `dotnet run` from `dotnet user-secrets` for
`backend/src/ArmenianAiToy.Api` (`OpenAI:ApiKey`), then immediately
consumed. The key value never reached stdout, files, or any tool
context — only its length (164) was printed.

## 2. Provider / model

- Provider: `openai`
- Model: `gpt-4o`

## 3. Scope

- `--max-prompts 2` (`--allow-full-set` deliberately **NOT** used; cost
  discipline preserved).
- 2 scenarios: `PA` (`v3-1-plan-a-age-4-simple-17`) +
  `PD` (`v3-1-plan-d-age-7-richer-2`).
- 6 turns total (3 turns per scenario, `MAX_TURNS=3`).
- Calls attempted / succeeded / failed: 6 / 6 / 0; every turn closed
  with `stop_reason: stop`.
- Path consumed: `tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json`
  (v3.1 scenarios reused unchanged — no v3.2-shaped scenarios exist
  in the repo; the R4 tightening is prompt-level).
- Alternate system prompt:
  `tools/StoryModelBakeoff/system-prompt-v3-2.txt` (the v3.2.1
  R4-tightened prompt at `87665f5`).
- Repo HEAD at run time: `11f63b3` (in sync with `origin/main`).
- Drift: yes — intentional alternate system prompt + alternate
  scenarios. Evidence-only deviation; not a request to retune the
  runtime prompt.

## 4. Prompt identity

- Bake-off prompt sha256:
  `3ed1dfecafd6d63b02d3cd3dc76e3515bbf7e8f661d7ac26d8dd2e3f3032bc5b`
  (post-`87665f5` v3.2.1 — same as v3.2.1 mp1 at `11f63b3`).
- Scenarios sha256:
  `e6cdba77d64640c89dc6aa094108f3bff040a26044af1b1cc75f5a7ab0f89b59`
  (unchanged from prior runs — same `bakeoff-prompts-v3-1.json`).
- Production prompt sha256:
  `54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
  (unchanged — production runtime is untouched).
- Alternate system-prompt: yes.
- Alternate scenarios: yes.
- Drift verdict: `drifted (intentional — alternate system prompt)`.

## 5. Result directory

```
tools/StoryModelBakeoff/bin/Debug/net10.0/results/20260509T010100Z/
  results.json
  review.md
  summary.json
```

These live under `bin/Debug` (gitignored build output) and **are not
committed**. This evidence file summarizes them.

- Run started UTC: `2026-05-09T01:01:00.6822754Z`
- Run completed UTC: `2026-05-09T01:01:20.6918219Z`
- Wall clock: ~20.0 s

## 6. Latency

| Scope | T1 | T2 | T3 | Subtotal |
|---|---|---|---|---|
| Plan A | 4958 ms | 2130 ms | 5502 ms | **12590 ms** |
| Plan D | 2527 ms | 1780 ms | 3085 ms | **7392 ms** |
| **Total** | | | | **19982 ms** |

Mean per turn ≈ **3330 ms**. No retries, no upstream errors, no
timeouts.

## 7. Tokens

| Turn | Prompt (in) | Completion (out) |
|---|---|---|
| PA T1 | 4710 | 265 |
| PA T2 | 5499 | 195 |
| PA T3 | 6221 | 264 |
| PD T1 | 4850 | 239 |
| PD T2 | 5661 | 239 |
| PD T3 | 6514 | 254 |
| **Total** | **33455** | **1456** |

Input total ≈ +1.5 % vs the projected 2× v3.2.1 mp1 baseline (16392 ×
2 ≈ 32784 → actual 33455). v3.2.1 prompt-tax is small and stable
across runs.

## 8. Gate tables

### Plan A (PA — `v3-1-plan-a-age-4-simple-17`)

| ID | Verdict | Note |
|---|---|---|
| C1  | **PASS strict** | T1 opens `Խնձորենու այգում ապրում էին երկու հին ընկերներ՝ շնիկը՝ հնարամիտ, և իր բարի ընկերը՝ շունը։` — places-anchored, no `Մի անգամ`-class. **Caveat:** `Մի օր տատիկը պատմեց...` mid-paragraph in T1 para 2 — same family as v3.2.1 mp1 / v3.2 mp1 caveat. Not strictly banned by R1's named list but adjacent to forbidden `Մի գեղեցիկ օր`. Recurring across three v3.2-era runs. |
| C2  | **PASS clean** | T3 closes on concrete `Շունը նույնպես ժպտաց՝ նայելով անցնող ամպերին։ Խնձորենու այգին լցված էր խաղաղությամբ։` — no abstract coda. R5 + R4 working together. |
| C3  | **PASS** | No exact repeats. |
| C6  | **PASS** | T1 choices byte-for-byte. |
| C8a | **PASS** | T2 opens `Շնիկը մոտեցրեց ցողի կաթիլներով տերևը արևի լույսին։` — direct execution of choice Ա. |
| C9  | **PASS** | T3 ends with `Վերջ։`; no Ա:/Բ: lines, no question. |
| **C13** | **PASS (overshoots ceiling)** | **PA T3 = 102 words** (10 sentences). Floor = 70 → **+32 above floor**. Ceiling = 100 → **+2 over upper bound**. C13 hard rule is floor-only so this is a PASS, but R4 over-corrected — pulled the closure into the ceiling territory. Different failure mode from prior runs: `barely above floor` (mp1 = 72w) → `barely over ceiling` (mp2 = 102w). |
| C14 | **PASS clean** | No `Continued`/`Note:`/`Շարունակեց`/parenthetical narrator. |
| C15 | **PASS** | T2 choices byte-for-byte. |
| C16 | **PASS** | `Խնձորենու այգում` in T1 first sentence; PLACE_STEM letters all present. |

**PA hard tally: 10 / 10 PASS.** PA T3 over upper-bound flagged but
not a hard fail.

### Plan D (PD — `v3-1-plan-d-age-7-richer-2`)

| ID | Verdict | Note |
|---|---|---|
| C1  | **PASS strict** | T1 opens `Հին կամուրջի վրա կանգնած էր զարմացող մողեսը՝ իր պաչիկ ընկերը, բադիկի հետ։` — places-anchored, no `Մի անգամ`-class. |
| C2  | **PASS with caveat** | T3 closes on concrete `Քամին մեղմորեն բախեց մողեսի կողքին։ Բադիկը հանգիստ շունչ քաշեց։` — R4 sensory + reaction pair, no abstract coda. **Caveat:** mild patience hint in T2 dialogue (`եթե մի փոքր համբերենք`) and T3 sentence 3 (`Նրանց համբերությունը շուտով պարգևիր նրանով, որ...`) — not aphorisms strictly, but adjacent to the patience-axis warning that the PD T3 user-turn explicitly flagged (`ՀԱՏԿԱՊԵՍ զգուշացիր համբերության աֆորիզմներից`). |
| C3  | **PASS** | No exact repeats. |
| C6  | **PASS** | T1 choices byte-for-byte. |
| C8a | **WEAK PASS** | T2 opens `Մողեսը տվեց քնած բանալին բադիկին` — verb is `տվեց` (gave), the SELECTED_CHOICE was `տանել` (take/bring). Action effectively performed (key handed over) but verb diverges from the byte-for-byte choice text. The user-turn's parenthetical guidance was `(մողեսը տանում է քնած բանալին բադիկի մոտ)`. |
| C9  | **PASS** | T3 ends with `Վերջ։`; no Ա:/Բ: lines, no question. |
| **C13** | **FAIL** | **PD T3 = 91 words** (9 sentences). Floor = 100 → **-9 below floor**. R4's two-sentence pair IS visible at the very end (`Քամին մեղմորեն բախեց մողեսի կողքին։` + `Բադիկը հանգիստ շունչ քաշեց։`) but didn't pull the body to the floor. The mid-band ≈110 anchor was not achieved. |
| C14 | **PASS clean** | No meta-output. |
| C15 | **PASS** | T2 choices byte-for-byte. |
| C16 | **PASS strict** | T1 first sentence has `Հին կամուրջի վրա` — schwa-NOT-dropped, every PLACE_STEM letter present. **Caveat:** T1 sentence 3 has the schwa-DROPPED form `կամրջի տակով` — partial regression in subsequent sentences. R3 strict gate is first-sentence-only, but the spirit-of-rule is violated mid-paragraph. T2 has correct `կամուրջի վրա` ✓. Better than v3.1 mp2 (which had the violation in T1 first sentence) but not perfect. |

**PD hard tally: 9 / 10 PASS, 1 FAIL (C13).**

### Combined hard tally

**19 / 20 across both plans, 1 FAIL (PD C13).**

## 9. Load-bearing checks

| Question | Answer |
|---|---|
| Did PA T3 pass C13 with better margin than mp1's +2? | **YES, dramatically.** PA T3 = 102w (+32 above floor; vs mp1's +2). But overshoots the 100-w upper bound by +2 — R4 over-corrected. |
| Did PD avoid English leakage? | **YES.** Zero Latin-script tokens, no `shimmering`-class leak. R1 held against the v3.1 mp2 historical failure. |
| Did PD avoid Armenian non-words? | **NO — major regression.** PD T3 contains `խորոտալ` (likely fabricated), `համբարձլ` (malformed conjugation; intended `համբարձավ`), `փափախերները` (means "fur caps" — wrong word entirely), `պարգևիր` (wrong tense — should be `պարգևեց`). Same `ձայնուֆով`-class failure family as v3.1 mp2 PD. **R2 did not generalize to age-7-richer / PD.** |
| Did PD obey the `հին կամուրջ` stem instruction? | **PARTIAL.** T1 first sentence: `Հին կամուրջի վրա` ✓ (schwa preserved — better than v3.1 mp2 which violated the FIRST sentence). T1 sentence 3: `կամրջի տակով` ✗ (schwa dropped). R3 strict gate (first-sentence-only) PASS; spirit-of-rule violated subsequently. T2: `կամուրջի վրա` ✓. So 1 of 6 PD sentences-with-stem has the slip; better than v3.1 mp2's first-sentence violation but not clean. |
| Did PD T3 reach 100–130 words? | **NO.** 91 words against 100 floor, **-9 below**. C13 FAIL on PD. |
| Did Turn 3 avoid choices? | **YES on both.** PA T3 ends `Վերջ։`; PD T3 ends `Վերջ։`. No Ա:/Բ: lines, no questions. C9 PASS on both. |

## 10. Quality verdict

### Plan A (`age-4-simple` — շնիկ + արագիլ + ցողի կաթիլներով տերև)

| Axis | Score | One-liner |
|---|---|---|
| Armenian naturalness | **3/5** | T1 typo `փոքրիտ` (should be `փոքրիկ`); T1 typo `պարզրվում` (should be `պարզվում`); T1 number-agreement slip `արագիլը արդեն անհետացան` (singular subject + plural verb); T2 `Նա մոտեցավ աշակերտներին` ("approached the students") — there are no students, model fabricated a noun. |
| Eastern Armenian correctness | **3/5** | No spelling slips of the `առաշնորդեց` family, no cross-language leaks, no fabricated stems on PA. The typos and the agreement slip are real but minor. |
| Fairy-tale feeling | **4/5** | Tatik present in T1 only; T3 closing on dog smiling at clouds + garden filled with peace is on-tone for `հիշատակային ու տաք`. |
| Warmth for age 4–7 | **4/5** | Consistent gentle register; smiles, peach textures, warm garden light. |
| Length / pacing | **3/5** | T1 ≈99w in 90-130 (within range). T2 ≈64w in 70-110 (UNDER floor by 6). **T3 ≈102w in 70-100 (OVER ceiling by +2)** — R4 over-correction. |
| Choice quality | **4/5** | Both blocks reproduced byte-for-byte; inherited scenario defect (T2 choice Բ presupposes stork going home) still present. |
| Continuation coherence | **3/5** | T1→T2 ok; T2 has the fabricated `աշակերտներին`. R6 staging marginal — `փոքրիտ նվերի միջոցով օգնեցին արագիլին` is retrospective naming, not the discrete giving moment `plan.resolutionStyle` calls for. |
| Safety | **PASS** | Age-appropriate throughout. |

### Plan D (`age-7-richer` — մողես + բադիկ + քնած բանալի + ձմեռային մեղմ)

| Axis | Score | One-liner |
|---|---|---|
| Armenian naturalness | **2/5** | Multiple garbled phrases in T3: `Փայտի հին բույրը վերը դարձավ` (literally "the wood's old scent became up" — incomplete idiom); `հոտը շուրջը համբարձլ ներքևի երկար առաջնությամբ` (literally "the scent rose around with the underneath's long priority" — completely garbled); `բնության փափախերները` ("nature's fur caps" — wrong word). T1 grammar slip: `նա իր գրպանը հայտնաբերեց` (accusative `գրպանը` should be locative `գրպանում`). |
| Eastern Armenian correctness | **2/5** | **Multiple non-word / wrong-tense items in T3:** `խորոտալ` (likely fabricated; intended `խրոխտել` / `զրնգալ`?), `համբարձլ` (malformed conjugation; should be `համբարձավ`), `փափախերները` (wrong word — `փափախ` = "fur cap"), `պարգևիր` (wrong tense — imperative/2nd person; should be 3rd-person past `պարգևեց`). Same fabrication-class failure as v3.1 mp2's `ձայնուֆով`. R2 did not hold on PD. T1 colloquial `պաչիկ` (kissy/dear-little) — dialectal, off-register for `age-7-richer`. |
| Fairy-tale feeling | **3/5** | T1/T2 atmospheric (winter, old wood scent, sleeping flowers); T3 broken phrasing breaks immersion. |
| Warmth for age 4–7 | **3/5** | Warm in T1/T2; T3 word-soup undercuts the closing. |
| Age-profile fit | **3/5** | `age-7-richer` poetic in T1 but T3's broken phrasing is below register, not above. |
| Length / pacing | **2/5** | **PD systematically under-floors across all three turns**: T1 ≈87w in 130-180 (UNDER by 43), T2 ≈81w in 100-140 (UNDER by 19), T3 ≈91w in 100-130 (UNDER by 9). The shortened spoken-toy hybrid budgets are not being honored. |
| Choice quality | **4/5** | Both blocks reproduced byte-for-byte. |
| Continuation coherence | **3/5** | Smooth narrative arc T1→T2→T3. R6 resolution staging: gestures at patience leading to thaw but the actual `ձյունը սկսեց հալվել, իսկ առվակը՝ խորոտալ ու երգել` moment is undercut by the `խորոտալ` non-word. |
| Safety | **PASS** | Age-appropriate throughout. |

## 11. R1 / R2 / R3 / R4 verdict

This is the first run that exercises all four strengthened rules
against their historical failure cases simultaneously.

### R1 — cross-language leak — **PASS**

- v3.1 mp2 PD T3 had `shimmering` mid-Armenian.
- v3.2.1 mp2 PD T3 has zero Latin-script tokens. Zero English / Russian
  / French anywhere across all 6 turns.
- R1 (`STRICTER v3.2`) appears to be doing real work.
- **Caveat:** still a single PD sample. A second mp2 PD run could
  produce a fresh sampling-variance leak.

### R2 — fabricated morphology — **FAIL ON PD**

- v3.1 mp2 had `ձայնուֆով` (PD T3) and `բարենի` (PA T2).
- v3.2 mp1 had `փայլալիք` and `փռփռացնելով թևերը` (PA T3).
- v3.2.1 mp2 has **four** fabricated/wrong items in PD T3 alone:
  `խորոտալ`, `համբարձլ`, `փափախերները`, `պարգևիր`. Worse density than
  any prior run.
- The R2 forbidden-list approach (named exemplars: `ձայնուֆով`,
  `բարենի`) does not generalize — the model invents new fabrications
  that aren't on the named list.
- **R2 needs structural strengthening, not just more examples.**
  Possible v3.2.2 direction: a "self-check substitution" rule that
  forces the model to rewrite any unfamiliar suffix/stem before
  emitting.

### R3 — schwa-stem (`հին կամուրջ`) — **PARTIAL**

- v3.1 mp2 PD T1 had `Հին կամրջի վրա` in the FIRST sentence
  (strict-gate violation).
- v3.2.1 mp2 PD T1 has `Հին կամուրջի վրա` in the FIRST sentence ✓.
- BUT T1 sentence 3 has `կամրջի տակով` — schwa-dropped.
- R3 strict gate (first-sentence-only) PASSES.
- Spirit-of-rule (no schwa-drop ANYWHERE) violated.
- v3.2.2 direction: extend R3's scope from "first sentence" to
  "anywhere PLACE_STEM appears in T1," with an explicit example
  pattern.

### R4 — closure floor — **BIMODAL**

- PA: **over-corrected**. mp1 = 72w (+2 above floor), mp2 = 102w
  (+32 above floor, +2 OVER ceiling). The mid-band ≈80 anchor +
  +5 cushion + two-sentence pair compounded; ceiling crossed.
- PD: **under-corrected**. v3.1 mp2 = 50w (-50 below floor),
  v3.2.1 mp2 = 91w (-9 below floor). +41-word improvement vs
  baseline but still FAIL. The two-sentence structural pattern
  IS visible at the end of PD T3 (`Քամին մեղմորեն բախեց...` +
  `Բադիկը հանգիստ շունչ քաշեց։`) but the body did not reach the
  floor.
- The single mid-band-target value in R4 (≈80 PA / ≈110 PD) is
  being applied unevenly across age profiles.
- v3.2.2 direction: tighter per-band calibration with **upper
  ceiling reminders** for the shorter band (PA) and a **stronger
  floor mandate** for the longer band (PD), possibly via an
  explicit minimum sentence count (e.g. "T3 must contain at least
  N sentences" where N varies by band).

## 12. Comparison across all PA + PD runs

| Run | Prompt | PA T3 | PA C13 | PD T3 | PD C13 | English leak (PD) | Schwa drop (PD) | Non-words |
|---|---|---|---|---|---|---|---|---|
| v3.1 mp1 (`14731b3`) | v3.1 | ≈52 | FAIL (-18) | n/a | n/a | n/a | n/a | none recorded |
| v3.1 mp2 (`fcffafe`) | v3.1 | ≈55 | FAIL (-15) | ≈50 | FAIL (-50) | YES (`shimmering`) | YES (`Հին կամրջի` T1 first) | YES (`ձայնուֆով`, `բարենի`) |
| v3.2 mp1 (`54c391f`) | v3.2 | ≈66 | FAIL (-4) | n/a | n/a | n/a | n/a | YES (`փայլալիք`, `փռփռացնելով`) |
| v3.2.1 mp1 (`11f63b3`) | v3.2.1 | ≈72 | PASS (+2) | n/a | n/a | n/a | n/a | none |
| **v3.2.1 mp2 (this run)** | **v3.2.1** | **≈102** | **PASS (+32 / OVER ceiling +2)** | **≈91** | **FAIL (-9)** | **NO ✓** | **PARTIAL** (T1 1st sentence ✓, T1 sentence 3 ✗) | **YES** (`խորոտալ`, `համբարձլ`, `փափախերները`, `պարգևիր`) |

**Net direction across the arc:**

- C13 closure floor: dramatically improved on PA (52w → 102w over
  4 v3.2-era runs); partial improvement on PD (50w → 91w). Closure
  floor is no longer the load-bearing failure on PA — over-correction
  is the new failure mode there.
- Cross-language leak: held on the one v3.2.1 PD sample (R1 win).
- Non-word morphology: persists, with new fabrications on each run
  (R2 weakness).
- Schwa-stem: partial improvement (T1 first sentence held; later
  sentences regress).

## 13. Engineering verdict

- **Engineering smoke: PASS.** v3.2.1 prompt loads, sha matches the
  `87665f5` post-tightening value (`3ed1dfec...`), 6/6 calls
  succeeded, no upstream errors, no timeouts.
- **R1 (cross-language leak): improved.** First v3.2-era PD test;
  no Latin-script tokens. Single sample, not yet conclusive.
- **R2 (fabricated morphology): FAILED on PD.** Four fabricated /
  wrong-tense items in PD T3 alone — same failure family as v3.1
  mp2. R2 strengthening did not generalize.
- **R3 (schwa-stem): partially improved.** Strict gate passes on
  T1 first sentence; spirit-of-rule violated mid-paragraph.
- **R4 (closure floor): bimodal.** PA over-corrected (102w, +2 over
  ceiling). PD under-corrected (91w, -9 below floor). The single
  mid-band-target value is not fitting both bands evenly.
- **OpenAI gpt-4o + v3.2.1 is NOT production-ready.** The C13 hard
  FAIL on PD plus the R2 PD non-word density are blockers for any
  age-7-richer production use. PA is closer to passing but the
  ceiling overshoot is a new failure mode.
- **Provider decision: do NOT make.** Single run on a single
  provider with the v3.2.1 prompt is not a comparison signal
  against Claude / Gemini / Local.
- **Production integration: do NOT integrate.** No runtime /
  `ChatService` / runtime system prompt / `appsettings` /
  `*.csproj` / test / seed-bank / name-bank / generator /
  validator / TTS / STT change is implied or authorized by this
  run.

### Recommended next step (no action taken; awaits explicit GO)

1. **Record this evidence first** — this file. Free; preserves the
   bimodal-C13 + R2-PD-regression + partial-R3-hold finding past
   the session boundary.
2. **Design a v3.2.2 prompt tightening** addressing the three
   defects this run surfaced:
   - **a. Per-band R4 calibration.** Tighten the upper-bound
     awareness for PA (`age-4-simple`, 70-100) so the closure
     does not overshoot. Strengthen the lower-bound mandate for
     PD (`age-7-richer`, 100-130) — possibly via an explicit
     minimum sentence count that scales with the band.
   - **b. R2 structural strengthening.** Beyond named
     exemplars, add a self-check substitution rule that forces
     the model to rewrite any unfamiliar suffix/stem combination
     before emitting. New PD-specific examples (`խորոտալ`,
     `համբարձլ`, `փափախերները`) extend the forbidden list.
   - **c. R3 stem consistency beyond first sentence.** Extend
     the schwa-not-drop rule from "first sentence" to "every
     T1 occurrence of the place stem." Explicit example pattern
     showing the schwa preserved across multiple T1 sentences.
3. **No more paid runs until the v3.2.2 design lands.** A second
   v3.2.1 mp2 paid run would only add sampling-variance noise
   to an already-clear set of structural defects.

## 14. Raw outputs

Reproduced from `results.json` / `review.md` for archival; nothing
here is sourced from anywhere outside the run's own captured outputs.

### Plan A — Turn 1 (4958 ms — 4710 in / 265 out — stop_reason: stop)

> Խնձորենու այգում ապրում էին երկու հին ընկերներ՝ շնիկը՝ հնարամիտ, և իր բարի ընկերը՝ շունը։ Նրանք սիրում էին լսել տատիկի պատմությունները խնձորենու ծառերի տակ։ Այգու արևի տաք շողերը միշտ փայլում էին, իսկ հասած դեղձի թավշյա մաշկը նրանց քաղցր ողջույն էր հայտնում։
>
> **Մի օր** տատիկը պատմեց, թե ինչպես աստղը ընկել էր և ինչ-որ անհայտ տեղ տանել էր։ Արագիլը չէր գտնում հանգստանալու վայրը, և մեր ընկերները որոշեցին օգնել նրա ճանապարհը գտնելու հարցում։ Շնիկը նկատեց մի տերև՝ ցողի կաթիլներով զարդարված, որը գոց պահված մի փոքրիկ կախարդանքի գաղտնիք ուներ։
>
> Շնիկը մտածեց, թե ինչպես կարելի է օգնել արագիլին՝ այդ կախարդական տերևի միջոցով։ Տաք հիշատակներ բարություն էին բերում նրանց։
>
> Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին
> Բ: գնալ դեպի խնձորենու այգի

### Plan A — Turn 2 (2130 ms — 5499 in / 195 out — stop_reason: stop)

Selected choice: `Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին`

> Շնիկը մոտեցրեց ցողի կաթիլներով տերևը արևի լույսին։ Հանկարծ տերևը սկսեց փայլել՝ հազարավոր գույներ բաշխելով այգու մեջ։ Շունը հիացած դիտում էր, ինչպես այդ ցողի կաթիլները կարծես փոքրիկ աստղիկ պատկերացնեին։
>
> Արագիլն այս տեսքից գլուխը բարձրացրեց։ Նա մոտեցավ **աշակերտներին** ու խնամքով զգաց, թե ինչպես է տերևի լույսը բերում տաքություն ու հանգստություն իր ներսում։ Այդպես, շուտով ցույց կտրվեց, թե որ կողմ պետք է գնա՝ տուն գտնելու համար։
>
> Ա: ուղեկցել արագիլին մինչև երկնքի եզրը
> Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն

### Plan A — Turn 3 (5502 ms — 6221 in / 264 out — stop_reason: stop)

Selected choice: `Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն`

> Շնիկը և շունը մնացին խնձորենու այգում։ Նրանք նայում էին, թե ինչպես է արագիլը բացվում իր թևերը և բարձրանում երկինք։ Արագիլը, շողացող տերևի շնորհիվ, գտնում էր իր ճանապարհը՝ տուն։ Նրա թևերի տակ ամպերն այլևս չէին թվում խոչընդոտներ։
>
> Երբ **արագիլը արդեն անհետացան** հորիզոնում, շնիկը և շունը սիրով նայեցին իրար։ Նրանք ինքնագոհ էին զգում՝ իմանալով, որ մի **փոքրիտ** նվերի միջոցով օգնեցին արագիլին։ Արևի տաք շողերը կրկին **պարզրվում** էին այգու միջով, իսկ քամին մեղմիկ շոյում էր հասած դեղձի թավշյա մաշկը։ Շնիկը մի խոր շունչ քաշեց ու հանգիստ շշնջաց շունին՝ գոհունակությամբ՝ «Լավ գործ արեցինք»։
>
> Շունը նույնպես ժպտաց՝ նայելով անցնող ամպերին։ Խնձորենու այգին լցված էր խաղաղությամբ։
>
> Վերջ։

### Plan D — Turn 1 (2527 ms — 4850 in / 239 out — stop_reason: stop)

> Հին կամուրջի վրա կանգնած էր զարմացող մողեսը՝ իր **պաչիկ** ընկերը, բադիկի հետ։ Ձմեռային մեղմ օրն էր՝ մթնոլորտը լցված հին փայտի և քնած ծաղիկների հոտով։ Հեռվում ձյունն էր ծածկել ամեն ինչ, իսկ **կամրջի** տակով անցնող փոքրիկ առվակը լուռ էր՝ կարծես սպասում էր իր երգին։ Խորհրդավոր մթնոլորտում մողեսի աչքերը փայլեցին, երբ նա իր **գրպանը** հայտնաբերեց մի փոքրիկ բանալի, որը կախարդական կարող էր լինել։ Բայց բադիկը տխրեց, նա վախեցած էր ու չէր համարձակվում առաջ գնալ։ Մողեսը որոշեց օգնել ընկերոջը՝ գտնելով մի միջոց, որը կբացեր առվակի ձայնը ու կբերի երգը նորից։
>
> Ա: տանել քնած բանալին ընկերոջ մոտ
> Բ: կանգնել ու լսել հին կամուրջ-ի ձայները

### Plan D — Turn 2 (1780 ms — 5661 in / 239 out — stop_reason: stop)

Selected choice: `Ա: տանել քնած բանալին ընկերոջ մոտ`

> Մողեսը **տվեց** քնած բանալին բադիկին, որը տխուր ու վախեցած էր։ Նա իր փոքրիկ բադիկ ընկերոջը մխիթարեց՝ ասելով, որ ամեն ինչ շատ շուտով կփոխվի, **եթե մի փոքր համբերենք**։ Ձյունն այսքան ժամանակ չէր հալվել, բայց մողեսը համոզված էր, որ բանալին որևէ կախարդական ուժ ունի։ Բադիկը փոքրիկ թևիկներով ընդունեց բանալին ու զարմացավ, թե ինչպես մողեսն այդքան վստահ էր։ Նրանք միասին կանգնեցին կամուրջի վրա՝ զգալով հին փայտի բույրը և քնած ծաղիկների հոտը։ Չնայած ձյունը դեռ չէր հալվել, մողեսը ընկերոջը վստահեցրեց՝ ժամանակը ամեն ինչ իր տեղը կհասցնի։
>
> Ա: փորձել արթնացնել քնած բանալին մեղմ շշունջով
> Բ: համբերել ու լսել հին կամուրջի տակ առվակի շշուկը

### Plan D — Turn 3 (3085 ms — 6514 in / 254 out — stop_reason: stop)

Selected choice: `Բ: համբերել ու լսել հին կամուրջի տակ առվակի շշուկը`

> Մողեսը և բադիկը որոշեցին կանգնել ու համբերել՝ լսելով հին կամուրջի տակ առվակի շշուկը։ Խաղաղ սպասումն ու ձմեռային մեղմ օրն առօրյա մթնոլորտ փոխանցում էին։ Նրանց համբերությունը շուտով **պարգևիր** նրանով, որ կամաց-կամաց ձյունը սկսեց հալվել, իսկ առվակը՝ **խորոտալ** ու երգել իր ուրախ ձայնով։ Փայտի հին բույրը վերը դարձավ և քնած ծաղիկների հոտը շուրջը **համբարձլ** ներքևի երկար առաջնությամբ։ Մողեսը ու բադիկը զգացին, թե ինչպես են բնության **փափախերները** գալիս ու անցնում։ Նրանց զգացմունքային ակնթարթը լի էր հույսով ու քնքշությամբ։ Մողեսը հանգիստ ժպտաց, իսկ բադիկը մխիթարվեց։
>
> Քամին մեղմորեն բախեց մողեսի կողքին։
> Բադիկը հանգիստ շունչ քաշեց։
> Վերջ։

(Bolded tokens flag the issues called out in §§ 8–11: PA T1
mid-paragraph `Մի օր,` C1 caveat; PA T2 fabricated `աշակերտներին`;
PA T3 typos `արագիլը արդեն անհետացան` / `փոքրիտ` / `պարզրվում`;
PD T1 colloquial `պաչիկ` and schwa-dropped `կամրջի` and case-slip
`գրպանը`; PD T2 verb-shift `տվեց` and patience hint `եթե մի փոքր
համբերենք`; PD T3 fabrications `պարգևիր`, `խորոտալ`, `համբարձլ`,
`փափախերները`. Final two PD T3 sentences `Քամին...` + `Բադիկը
հանգիստ շունչ քաշեց։` are the R4-prescribed sensory + reaction pair
landing visibly even on the under-floor sample.)

## 15. No secrets included

This file contains **no API key**, no `OPENAI_API_KEY` value, no
token, no bearer credential, no parent JWT, no device API key, and
no private endpoint. The `OPENAI_API_KEY` env var used to authorize
the run was loaded inline from `dotnet user-secrets` for
`backend/src/ArmenianAiToy.Api` into the same PowerShell process as
the `dotnet run`, then immediately consumed; only its length (164)
was printed. Nothing about the key is echoed in this document or in
the captured `results.json` / `review.md` / `summary.json`.

## Scope guard

No production / runtime files were touched by this run or by this
evidence file: `ChatService`, backend code, frontend, `appsettings*.json`,
`*.csproj`, tests, seed bank, name bank, story-plan generator,
validator, runtime system prompts (production sha
`54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
unchanged), speech / TTS / STT — all unchanged. The bake-off tooling
(`tools/StoryModelBakeoff/`) is the only code that ran, and only its
build-output `results/` directory was written to (gitignored). The
`bin/Debug/net10.0/results/20260509T010100Z/` artifacts are not
committed and never will be — this evidence file is the only
repo-tracked artifact of the run.

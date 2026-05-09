# OpenAI v3.2.1 live smoke (mp1) — 2026-05-09

First **OpenAI live smoke** of the v3.2.1 R4-tightened system prompt
with `--max-prompts 1`. A **paid OpenAI API call was made** (3 chat
completions, 1 scenario × 3 turns). No production / runtime change
was made; this evidence file is documentation only. Companion to:

- v3.1 mp1 evidence committed at `14731b3`
- v3.1 mp2 evidence committed at `fcffafe`
- v3.2 design plan committed at `f09ca92`
- v3.2 system prompt authored at `f3aff65`
- v3.2 Armenian wording fix at `5fe6cd9`
- v3.2 mp1 evidence committed at `54c391f`
- v3.2.1 R4 closure-floor tightening committed at `87665f5`

This run is the **load-bearing test for the R4 tightening** shipped at
`87665f5` — does the strengthened R4 (mid-band target + +5w cushion +
two-sentence requirement) actually pull PA T3 from `54c391f`'s 66w
FAIL into the safe band?

---

## 1. Run command

```
dotnet run --project tools/StoryModelBakeoff -- --run --provider openai --max-prompts 1 --i-understand-live-cost --scenarios tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json --system-prompt tools/StoryModelBakeoff/system-prompt-v3-2.txt
```

The `OPENAI_API_KEY` env var was loaded into the same PowerShell
process as the `dotnet run` from the project's `dotnet user-secrets`
store (`OpenAI:ApiKey`), then immediately consumed. The key value
never reached stdout, files, or any tool context — only its length
(164) was printed.

## 2. Provider / model

- Provider: `openai`
- Model: `gpt-4o`

## 3. Scope

- `--max-prompts 1` (`--allow-full-set` deliberately **NOT** used; cost
  discipline preserved).
- 1 scenario: `PA` (`v3-1-plan-a-age-4-simple-17`).
- 3 turns total (`MAX_TURNS=3`).
- Calls attempted / succeeded / failed: 3 / 3 / 0; every turn closed
  with `stop_reason: stop`.
- Path consumed: `tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json`
  (v3.1 scenarios reused unchanged — no v3.2-shaped scenarios exist
  in the repo; the R4 tightening is prompt-level).
- Alternate system prompt:
  `tools/StoryModelBakeoff/system-prompt-v3-2.txt` (the v3.2.1
  R4-tightened prompt at `87665f5`).
- Repo HEAD at run time: `87665f5` (in sync with `origin/main`).
- Drift: yes — intentional alternate system prompt + alternate
  scenarios. Evidence-only deviation; not a request to retune the
  runtime prompt.

## 4. Prompt identity

- Bake-off prompt sha256:
  `3ed1dfecafd6d63b02d3cd3dc76e3515bbf7e8f661d7ac26d8dd2e3f3032bc5b`
  (post-`87665f5` v3.2.1 — different from v3.2 mp1's
  `da6438994c5e3fc2c285b7436388c4f2e28efcfea3f00ea5b19159c0cadedbe1`).
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
tools/StoryModelBakeoff/bin/Debug/net10.0/results/20260509T001723Z/
  results.json
  review.md
  summary.json
```

These live under `bin/Debug` (gitignored build output) and **are not
committed**. This evidence file summarizes them.

- Run started UTC: `2026-05-09T00:17:23.9102601Z`
- Run completed UTC: `2026-05-09T00:17:33.2755365Z`
- Wall clock: ~9.4 s

## 6. Latency

| Turn | Latency |
|---|---|
| T1 | 5251 ms |
| T2 | 2152 ms |
| T3 | 1935 ms |
| **Total** | **9338 ms** |

Mean per turn ≈ 3113 ms. No retries, no upstream errors, no timeouts.

## 7. Tokens

| Turn | Prompt (in) | Completion (out) |
|---|---|---|
| T1 | 4710 | 255 |
| T2 | 5489 | 177 |
| T3 | 6193 | 184 |
| **Total** | **16392** | **616** |

Input tokens are **+603 across 3 turns vs v3.2 mp1's 15789** — exactly
the cost-of-tightening forecast in
`tools/StoryModelBakeoff/evaluations/v3-2-r4-tightening-plan-20260508.md`
§ 8 risk 1 (~120 tokens added per call, ~360 across 3 turns; the
extra ~240 reflects natural tokenizer variance plus the v3.2.1
header tag). Output tokens essentially unchanged (616 vs 605).

## 8. Gate table — Plan A

| ID | Check | Verdict | Note |
|---|---|---|---|
| C1  | No forbidden opener | **PASS strict** | T1 opens `Խնձորենու այգում արեգակի տաք շողերը խաղում էին ծառերի առատ ծառուղիների միջով։` — no "Մի անգամ"-class opener. **Caveat**: `Մի օր,` appears mid-paragraph at T1 sentence 4 (`Մի օր, երբ տատիկը նրանց մեծ ծիրանի ծառի տակ հին պատմություն էր պատմում, արագիլն…`). Same family as v3.2 mp1's `Մի անգամ,` mid-paragraph caveat — not strictly banned by R1's named list, but adjacent to forbidden `Մի գեղեցիկ օր`. Worth flagging for v3.3 if it recurs. |
| C2  | No moralizing / no abstract coda (R5) | **PASS clean** | T3 closes on a concrete sensory image (`այգին հանդարտորեն պարուրվեց արևի վերջին շողերի լույսով`) and a small character action (`Շնիկը հանգիստ շունչ քաշեց, իսկ հասած դեղձերի թավշյա մաշկը մնում էր անփոփոխ մեղմ ու համեղ`). **No `հետագայում / միշտ / որպեսզի… տարածվի` framing.** R5 + R4 working together. |
| C3  | No duplicate sentence | **PASS** | No exact repeats. |
| C6  | Exact T1 choices (byte-for-byte break-glass) | **PASS** | `Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին` / `Բ: գնալ դեպի խնձորենու այգի`. |
| C8a | T2/T3 first sentence performs SELECTED_CHOICE | **PASS** | T2: `Շնիկը մոտեցրեց ցողի կաթիլներով տերևը լույսին։` T3: `Շնիկը և շունը մնացին խնձորենու այգում՝ դիտելով, թե ինչպես արագիլը թռավ դեպի իր տուն։` |
| C9  | T3 no choices, no question | **PASS** | Ends with `Վերջ։` on its own line; no `Ա:`/`Բ:` lines, no question. |
| C13 | T3 closure length ≥ TARGET_WORDS lower bound (R4) | **PASS but marginal** | **T3 body = 72 words** (counted: 14+6+11+8+18+15 across 6 sentences) against the 70–100 floor. Crosses the floor by **+2 only — NOT the +5 cushion R4 was designed to enforce**. v3.2.1 narrowed v3.2 mp1's -4 FAIL into a +2 PASS, but the safety margin the R4 tightening targeted is not landed. |
| C14 | No meta-output | **PASS clean** | No `Continued`/`Note:`/`Շարունակեց հեքիաթը`/parenthetical narrator commentary. |
| C15 | Exact T2 choices (byte-for-byte break-glass) | **PASS** | `Ա: ուղեկցել արագիլին մինչև երկնքի եզրը` / `Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն`. |
| C16 | Place anchor (`խնձորենու այգի`) | **PASS** | `Խնձորենու այգում` in T1 first sentence; PLACE_STEM letters all present. |

**Hard tally: 10 / 10 PASS, but C13 is a marginal PASS (+2 above floor instead of +5 cushion).**

## 9. Armenian quality (per-axis)

| Axis | Score | One-liner |
|---|---|---|
| Armenian naturalness | **3/5** | Several bookish or off-register phrasings (see § 10). |
| Eastern Armenian correctness | **4/5** | No spelling slips, no cross-language leaks, no `բարենի`/`ձայնուֆով`/`փայլալիք`/`փռփռացնելով թևերը` from prior runs. |
| Fairy-tale feeling | **4/5** | Tatik present in T1 only (regression vs v3.1 re-run which had her in all 3 turns); T3 closing on dog's quiet breath + peach skin texture is on-tone for `հիշատակային ու տաք`. |
| Warmth for age 4–7 | **4/5** | Consistent gentle register; smiles, peach textures, warm garden light. |
| Length / pacing | **3/5** | T1 ≈94w in 90-130 (within range, near floor); **T2 ≈53w in 70-110 (UNDER floor by 17w)**; T3 ≈72w in 70-100 (above floor by +2 — marginal). T2 closure-short pattern persists across runs. |
| Choice quality | **4/5** | Both blocks reproduced byte-for-byte; inherited scenario defect (T2 choice Բ presupposes stork going home) still present — property of `bakeoff-prompts-v3-1.json`, not the model. |
| Continuation coherence | **3/5** | T1→T2→T3 flow ok; R6 resolution staging marginal: `Ցողի կաթիլներով տերևի փայլը առաջնորդեց արագիլին` gestures at the gift moment as the leaf's light, but no discrete handing-over moment. Same partial R6 status as v3.2 mp1. |
| Safety | **PASS** | Age-appropriate throughout; no fear, no violence, no medical content. |

## 10. Notable weaknesses (specific phrases)

Recorded for native-ear / v3.3 review:

- **T1**: `արագիլն աղմկող թևերը ծավալելով` — "the stork unfolding [its] noisy wings" reads slightly off. `աղմկող` is a real word but unusual modifier for wings; would be smoother as `թափահարելով թևերը`.
- **T2**: `Արագիլը գալարվելով մոտեցավ լույսին` — `գալարվել` ("to writhe / coil") is anatomically wrong for a stork's approach.
- **T2**: `Շունն աննկատ ժպտաց՝ հասկանալով…` — `աննկատ` ("unnoticed / imperceptibly") rarely modifies facial expressions in standard Eastern Armenian. Smoother would be `մեղմ ժպտաց` or `թաքուն ժպտաց`.
- **T3**: `Երախտագիտության նշան չէին վարանում ցույց տալ` — bookish negation construction; off-register for `age-4-simple` (`sentenceStyle: կարճ և պարզ նախադասություններ`).
- **T3**: `ճանապարհը երկնքի խորքերը` — loose grammar; should be `դեպի երկնքի խորքերը` or `երկնքի խորքերի ուղին`. Missing case marker.
- **Length / pacing**: **T2 ≈53w against 70-110 floor** — the closure-short pattern is not exclusive to T3; it shifts turns under sampling variance. R4 only targets T3, but the broader closure-short tendency persists.

## 11. Comparison across all PA T3 runs

| Run | Prompt | T3 word count | C13 vs 70 | T3 closure structure | Hard tally |
|---|---|---|---|---|---|
| v3.1 mp1 (`14731b3`) | v3.1 | ≈52 | FAIL (-18) | abstract coda (`տարածելու համար`) | 9/10 |
| v3.1 mp2 (`fcffafe`) | v3.1 | ≈55 | FAIL (-15) | poetic, no closure pattern | 9/10 |
| v3.2 mp1 (`54c391f`) | v3.2 | ≈66 | FAIL (-4) | one concrete image, single | 9/10 |
| v3.1 re-run (2026-05-09 session) | v3.1 | ≈74 | PASS (+4) | grandmother's smile + peach (sampling variance) | 10/10 |
| **v3.2.1 (this run)** | **v3.2.1** | **≈72** | **PASS (+2)** | **R4 pair: sensory image + small reaction** | **10/10** |

**Important context:** the v3.1 re-run was a third v3.1 sample at
gpt-4o temperature 1.0 and landed at 74w by sampling variance with
no R4 structural pattern. This v3.2.1 run landed at 72w **with** the
R4 structural pattern (the prescribed sentence pair is visible in T3
sentences 5 + 6). The two PASS results are NOT directly comparable —
v3.1's was variance-driven, v3.2.1's is structure-driven.

## 12. R4 verdict

**Mixed: structural fix landed, numeric cushion did not.**

### Yes — R4 structurally landed

- T3's last two sentences are the exact pattern R4 mandated:
  1. *Sensory / place image* (R4 named pattern `ա`):
     `Հենց այս պարզ ու տաք միջավայրի մեջ, արագիլի հեռացող թևերի տակ, այգին հանդարտորեն պարուրվեց արևի վերջին շողերի լույսով։`
  2. *Small character reaction* (R4 named pattern `բ`,
     `հանգիստ շունչ` is one of R4's literal example tokens):
     `Շնիկը հանգիստ շունչ քաշեց, իսկ հասած դեղձերի թավշյա մաշկը մնում էր անփոփոխ մեղմ ու համեղ։`
- The two sentences are **separate**, not merged into one
  subordinate clause — the R4 forbid-merge clause held.
- This is the FIRST PA sample where the R4 structural fingerprint
  appears. v3.1 mp1, v3.1 mp2, and v3.2 mp1 all closed with
  different shapes; the v3.1 re-run closed naturally without the
  R4 pair. v3.2.1 produced the prescribed pair on first try.
- C13 hard gate crossed (72 ≥ 70). C2 simultaneously stayed clean,
  which is the load-bearing combination — v3.1 mp1's "longer
  closure" path was an abstract coda that R5 had to ban; v3.2.1's
  "longer closure" is the R4 sensory + reaction pair, which R5
  blesses.

### No — the +5 cushion did not deliver

- 72w is only +2 above the 70w floor. **Not the +5 cushion** R4
  was supposed to enforce.
- The ≈80w mid-band target was **not** achieved.
- T2 closure (53w vs 70-110 floor) is also under — R4 only targets
  T3, but the broader closure-short tendency persists across turns.
- The model honored the *structural* recipe (add two specific
  sentences) but did not honor the *numeric guard* (target ≥ 75w).
- One sample. Cannot prove reliability — a second mp1 retry, or
  mp2 (PA + PD), would be needed to know whether v3.2.1 PA T3
  reliably lands above floor with margin.

### Net direction

R4 is in the right direction. The structural fingerprint is real
behavior change, not sampling luck (the v3.1 re-run did NOT produce
the two-sentence pair pattern, while v3.2.1 did). To make C13
*reliably* PASS with margin, a future v3.2.2 / v3.3 would likely
need either:

- a numeric sentence-count floor (e.g., `Քայլ 3-ը պարունակում է առնվազն 7 նախադասություն`),
- exposing the per-turn `TARGET_WORDS` lower bound numerically
  inline in the user-turn prompt as a runtime reminder, or
- escalating the closure-add rule from "two sentences" to
  "three sentences" when the body lands below floor+5.

These are v3.2.2 / v3.3 design considerations, **not for this slice**.

## 13. Engineering verdict

- **Engineering smoke: PASS.** v3.2.1 prompt loads, sha matches the
  `87665f5` post-tightening value (`3ed1dfec...`), 3/3 calls
  succeeded, no upstream errors, no timeouts.
- **Story quality: IMPROVED structurally over v3.2 mp1; reliability
  not yet proven.** R4 structural pattern landed on first try
  (first sample with the prescribed two-sentence closure pair).
  C13 marginal pass (+2). Other v3.2 wins (R5 clean closure, R6
  partial staging) preserved.
- **Provider decision: do NOT make.** Single-sample single-provider
  run is not a comparison signal.
- **Production integration: do NOT integrate.** No runtime /
  `ChatService` / runtime system prompt / `appsettings` / `*.csproj`
  / test / seed-bank / name-bank / generator / validator / TTS /
  STT change is implied or authorized by this run.

### Recommended next step (no action taken; awaits explicit GO)

Two natural next moves, in order:

1. **Record this evidence first** — this file. Free; preserves the
   R4-structural-pass + numeric-marginal finding past the session
   boundary. Done.
2. **Run v3.2.1 mp2 (PA + PD, 6 turns) paid.** This is the
   load-bearing test for (a) a second PA T3 sample under v3.2.1
   to disambiguate variance from R4's structural effect, (b) R1
   (cross-language leak on Plan D's historical `shimmering`) and
   (c) R3 (schwa-stem on `Հին կամուրջ`) — both untested under v3.2
   to date. Cost ≈ 2× this run (≈ $0.04–$0.10 against gpt-4o
   posted prices). **No further paid call without explicit GO.**

## 14. Raw outputs

Reproduced from `results.json` / `review.md` for archival; nothing
here is sourced from anywhere outside the run's own captured outputs.

### Plan A — Turn 1 (5251 ms — 4710 in / 255 out — stop_reason: stop)

> Խնձորենու այգում արեգակի տաք շողերը խաղում էին ծառերի առատ ծառուղիների միջով։ Այդտեղ ապրում էր մի հնարամիտ շնիկ, որն անընդհատ զարմացնում էր բոլորին իր հետաքրքիր արկածներով։ Շնիկի բարեկամը մի իմաստուն շուն էր, որ միշտ պատրաստ էր զրույցների ու պատմությունների։ **Մի օր,** երբ տատիկը նրանց մեծ ծիրանի ծառի տակ հին պատմություն էր պատմում, արագիլն **աղմկող թևերը ծավալելով** մոտեցավ նրանց՝ անհանգստացած։ Նա կորցրել էր իր ճանապարհը և չէր կարողանում գտնել հանգստանալու տեղ։ Հենց այդ ժամանակ շնիկը նկատեց ցողի կաթիլներով փայլուն տերևը՝ ընկած մարգագետնի վրա։ Տերևը փայլում էր խնձորենու այգու շողերում՝ խոստանալով հրաշալի լուծում բերել արագիլի խնդիրին։
>
> Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին
> Բ: գնալ դեպի խնձորենու այգի

### Plan A — Turn 2 (2152 ms — 5489 in / 177 out — stop_reason: stop)

Selected choice: `Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին`

> Շնիկը մոտեցրեց ցողի կաթիլներով տերևը լույսին։ Տերևը սկսեց փայլել ու ողջ այգին լցվեց բարի երանգներով։ Արագիլը **գալարվելով** մոտեցավ լույսին, կարծելով, թե դա հանգստի մի հրաշալի անկյուն է։ Շունն **աննկատ** ժպտաց՝ հասկանալով, որ տերևի փայլը կօգնի արագիլին հասկանալ իր ցանկալի ուղին։ Արագիլը, դիտելով փայլուն տեսարանը, զգաց, որ պետք է գտնել իր տան ճանապարհը։
>
> Ա: ուղեկցել արագիլին մինչև երկնքի եզրը
> Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն

### Plan A — Turn 3 (1935 ms — 6193 in / 184 out — stop_reason: stop)

Selected choice: `Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն`

> Շնիկը և շունը մնացին խնձորենու այգում՝ դիտելով, թե ինչպես արագիլը թռավ դեպի իր տուն։ **Երախտագիտության նշան չէին վարանում ցույց տալ։** Ցողի կաթիլներով տերևի փայլը առաջնորդեց արագիլին՝ ցույց տալով **ճանապարհը երկնքի խորքերը։** Արագիլը թևերը լայն ձգեց ու կամաց-կամաց վեր բարձրացավ։ Հենց այս պարզ ու տաք միջավայրի մեջ, արագիլի հեռացող թևերի տակ, այգին հանդարտորեն պարուրվեց արևի վերջին շողերի լույսով։ Շնիկը հանգիստ շունչ քաշեց, իսկ հասած դեղձերի թավշյա մաշկը մնում էր անփոփոխ մեղմ ու համեղ։
>
> Վերջ։

(Bolded tokens flag the phrases discussed in § 10. Final two sentences
are the R4-prescribed pair: sensory image + small character reaction.)

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
`bin/Debug/net10.0/results/20260509T001723Z/` artifacts are not
committed and never will be — this evidence file is the only
repo-tracked artifact of the run.

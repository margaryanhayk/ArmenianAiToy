# OpenAI v3.2.2 live smoke (mp2) — 2026-05-10

**PARTIAL run.** Second OpenAI live smoke of the v3.2.2
R2/R3/R4-tightened system prompt with `--max-prompts 2`. A **paid
OpenAI API call was made** — 6 chat completions attempted, **5
succeeded, 1 failed with HTTP 429 (OpenAI tokens-per-minute
rate-limit)**. **PD T3 did not complete** because the cumulative TPM
window blew past the gpt-4o tier limit on the longest turn. This is
an **upstream OpenAI rate-limit failure**, not a prompt-output
failure. No production / runtime change was made; this evidence
file is documentation only.

Companion to:

- v3.1 mp1 evidence committed at `14731b3`
- v3.1 mp2 evidence committed at `fcffafe`
- v3.2 design plan committed at `f09ca92`
- v3.2 system prompt authored at `f3aff65`
- v3.2 Armenian wording fix at `5fe6cd9`
- v3.2 mp1 evidence committed at `54c391f`
- v3.2.1 R4 closure-floor tightening committed at `87665f5`
- v3.2.1 mp1 evidence committed at `11f63b3`
- v3.2.1 mp2 evidence committed at `d9c36ca`
- v3.2.2 design plan committed at `86d035a`
- v3.2.2 implementation (R2/R3/R4 tightening) committed at `768be15`
- v3.2.2 mp1 evidence committed at `4649dda`

This run is the **load-bearing test for the R3 whole-T1 widening
and R4 ceiling guard against Plan D**, plus a second PA T3 sample
to disambiguate v3.2.2 mp1's structural fingerprint from sampling
luck. Plan D had not been tested under v3.2.2 before this run.

The filename uses local Yerevan date `20260510` because the run
completed at UTC `2026-05-09T21:56:07Z` = Yerevan `2026-05-10
01:56` (UTC+4).

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

- `--max-prompts 2` (`--allow-full-set` deliberately **NOT** used;
  cost discipline preserved).
- 2 scenarios attempted: `PA` (`v3-1-plan-a-age-4-simple-17`) +
  `PD` (`v3-1-plan-d-age-7-richer-2`).
- 6 turns attempted (3 turns per scenario, `MAX_TURNS=3`).
- **5 turns succeeded, 1 failed** (PD T3 — see § 5).
- Path consumed: `tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json`
  (v3.1 scenarios reused unchanged).
- Alternate system prompt:
  `tools/StoryModelBakeoff/system-prompt-v3-2.txt` (v3.2.2 prompt
  at `768be15`).
- Repo HEAD at run time: `4649dda` (in sync with `origin/main`).
- Drift: yes — intentional alternate system prompt + alternate
  scenarios.

## 4. Prompt identity

- Bake-off prompt sha256:
  `908ae30e610e18389b2151c262149db32830b3f663db70fe98b268e4e06fec2f`
  (post-`768be15` v3.2.2 — same as v3.2.2 mp1 at `4649dda`).
- Scenarios sha256:
  `e6cdba77d64640c89dc6aa094108f3bff040a26044af1b1cc75f5a7ab0f89b59`
  (unchanged).
- Production prompt sha256:
  `54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
  (unchanged).
- Alternate system-prompt: yes.
- Alternate scenarios: yes.
- Drift verdict: `drifted (intentional — alternate system prompt)`.

## 5. Result status — partial due to rate limit

```
attempted: 6
succeeded: 5
failed:    1   (PD T3 — http_429 OpenAI TPM rate limit)
```

The PD T3 error body, captured verbatim from `results.json`:

> `Rate limit reached for gpt-4o in organization
> org-CBUghJziXjGMBD07FkRfgIHH on tokens per min (TPM): Limit 30000,
> Used 26265, Requested 7881. Please try again in 8.292s.`

Cumulative input across the 5 succeeded calls + the rejected PD T3
request (≈26265 + 7881 = ≈34146 tokens) exceeded the gpt-4o
30k-TPM tier ceiling. The bake-off makes 6 sequential calls that
each carry the cumulative conversation history; PD T3 is the
longest-input call (≈7881 tokens with full T1+T2 echo) and is
where the TPM window blew.

**This is an upstream OpenAI rate-limit failure, not a prompt issue.**
The PD T3 closure-floor + R4 mid-band + R2 fabrication test on the
load-bearing scenario is therefore **untested by this run**. A
follow-up paid run (with explicit operator GO) would resolve this —
either as a PD-only mp1 or with sufficient TPM-window pacing.

## 6. Result directory

```
tools/StoryModelBakeoff/bin/Debug/net10.0/results/20260509T215551Z/
  results.json
  review.md
  summary.json
```

These live under `bin/Debug` (gitignored build output) and **are
not committed**. This evidence file summarizes them.

- Run started UTC: `2026-05-09T21:55:51.6176407Z`
- Run completed UTC: `2026-05-09T21:56:07.8817721Z`
- Wall clock: ~16.3 s

## 7. Latency

| Scope | T1 | T2 | T3 | Subtotal |
|---|---|---|---|---|
| Plan A | 3940 ms | 2327 ms | 2114 ms | **8381 ms** |
| Plan D | 3702 ms | 2927 ms | **1223 ms FAIL http_429** | **7852 ms (incl. failed)** |
| **Total** | | | | **16233 ms** |

PA all three turns succeeded. PD T1+T2 succeeded; **PD T3 is the
1223 ms failure window** (HTTP 429 returned quickly).

## 8. Tokens

| Turn | Prompt (in) | Completion (out) |
|---|---|---|
| PA T1 | 5703 | 296 |
| PA T2 | 6523 | 207 |
| PA T3 | 7257 | 210 |
| PD T1 | 5843 | 384 |
| PD T2 | 6799 | 352 |
| PD T3 | (failed — rate limit) | (failed) |
| **Total** | **32125** | **1449** |

The PD T3 input was estimated at ~7881 tokens by OpenAI's response.

## 9. Gate tables

### Plan A (PA — `v3-1-plan-a-age-4-simple-17`)

| ID | Verdict | Note |
|---|---|---|
| **C1**  | **FAIL** | T1 opens **`Մի գեղեցիկ օրը, շնիկը և իր շուն ընկերն զբաղված էին խնձորենու այգում։`**. Rule A's forbidden-opener list explicitly bans `Մի գեղեցիկ օր` template. Definite-article variant `Մի գեղեցիկ օրը` is the same template (`տիպի կաղապարով`). **Hard-rule violation. REGRESSION vs v3.2.2 mp1** which was clean on this gate. |
| C2  | **PASS** | T3 closes on `օդում դեռ պահում էին տատիկի պատմության տաքությունը` — concrete sensory + emotional present-moment. No abstract coda. |
| C3  | **PASS** | No exact repeats. |
| C6  | **PASS** | T1 byte-for-byte. |
| C8a | **PASS** | T2 opens `Շնիկը մոտեցրեց ցողի կաթիլներով տերևը արևի շողերին։` — direct execution of choice Ա. |
| C9  | **PASS** | T3 ends `Վերջ։` alone. |
| **C13** | **PASS — MID-BAND (replicated)** | **PA T3 = 82 words.** Same exact mid-band landing as v3.2.2 mp1 (also 82w). Two consecutive mid-band PA T3 samples under v3.2.2 — R4 ceiling guard structural fingerprint replicated. |
| C14 | **PASS** | No meta-output. |
| C15 | **PASS** | T2 byte-for-byte. |
| C16 | **PASS** | `խնձորենու այգում` in T1 first sentence; PLACE_STEM letters preserved across all T1 mentions. |

**PA hard tally: 9 / 10 PASS, 1 FAIL (C1).** **REGRESSION** vs
v3.2.2 mp1 which was 10/10.

### Plan D (PD — `v3-1-plan-d-age-7-richer-2`)

| ID | Verdict | Note |
|---|---|---|
| C1  | **PASS strict** | T1 opens `Երբ ձմեռային մեղմ թվացող օրը մոտեցավ, մողեսը վազում էր դեպի հին կամուրջ։` — places-anchored, no `Մի անգամ`/`Մի գեղեցիկ օր`-class. |
| C2  | **PASS with caveat** | T2 has borderline patience hint (`համբերությունը դրանք կվերադարձներ նախկին տեսքի`) — patience-axis was the explicit caveat in PD T3 user-turn, but T3 didn't run. |
| C3  | **PASS (T1+T2)** | No exact repeats observed in the two completed turns. |
| C6  | **PASS** | T1 byte-for-byte. |
| **C8a** | **PASS clean** | T2 opens `Մողեսը քնած բանալին զգուշորեն տարավ բադիկի մոտ։` — verb `տարավ` matches SELECTED_CHOICE `տանել` (took/brought). **Improvement vs v3.2.1 mp2 PD T2** which used `տվեց` (gave). |
| **C9** | **CANNOT TEST** | T3 incomplete (http_429). |
| **C13** | **CANNOT TEST** | T3 incomplete (http_429). PD T3 closure-floor verdict UNKNOWN. |
| C14 | **PASS (T1+T2)** | No meta-output observed. |
| C15 | **PASS** | T2 byte-for-byte. |
| **C16** | **PASS — R3 WHOLE-T1 WIDENING WORKS** | T1 sentence 1 `հին կամուրջ` ✓; T1 sentence 2 `Հին կամուրջի վրայով` ✓; T1 sentence 3 `Կամուրջի տակ` ✓; T1 sentence 10 `Հին կամուրջից` ✓. **Zero schwa drops anywhere in T1.** **Major load-bearing win vs v3.2.1 mp2 which had `կամրջի տակով` in T1 sentence 3.** |

**PD hard tally: T1+T2 partial — 5 testable gates PASS / 0 FAIL,
3 gates (C9 / C13 / C14 T3-application) CANNOT TEST.**

## 10. Load-bearing checks

| Question | Answer |
|---|---|
| Did PA T3 stay inside 70–100 words? | **YES — 82w mid-band** (replicates v3.2.2 mp1 exactly). |
| Did PA avoid over-ceiling? | **YES** (-18 below ceiling). |
| Did PD T3 reach 100–130 words? | **CANNOT TEST** — http_429. |
| Did PD T1 + T2 length improve? | **YES — major win.** PD T1 = 136w in 130-180 ✓ (+6 above floor); PD T2 = 135w in 100-140 ✓. v3.2.1 mp2 had T1 = 87w (UNDER -43), T2 = 81w (UNDER -19). **Systematic under-floor pattern broken on T1+T2.** |
| Did R3 whole-T1 widening work on PD? | **YES — major load-bearing win.** All four PD T1 stem mentions (sentences 1, 2, 3, 10) preserve full schwa-not-dropped form. v3.2.1 mp2's `կամրջի տակով` defect in T1 sentence 3 is fixed. |
| Did PD avoid English leakage? | **YES** on T1+T2. |
| Did PD avoid the four mp2 named fabrications (`խորոտալ`, `համբարձլ`, `փափախերները`, `պարգևիր`)? | **YES** — none recurred in T1+T2. R2 named-family suppression continues to hold. |
| Did any new fake / wrong Armenian word appear? | **YES — seven new ones** across the 5 successful turns: PA T1 `կարապված` (likely fabrication, intended `փաթաթված`); PA T3 `շնորակալությամբ` (typo for `շնորհակալությամբ`), `մեղմշխարհն` (fabricated compound `մեղմ+աշխարհ`); PD T1 `իրեբերում էր` (non-standard verb), `հեռականի` (should be `հեռվից`), `շտեպնով` (probable `շտապով`); PD T2 `տերմինները` (wrong-word selection — `տերմին` = "term/expression," wrong context). |
| Did PD obey `հին կամուրջ` stem instruction across T1? | **YES** — see R3 row above. |
| Did Turn 3 avoid choices? | **PA T3 yes** (`Վերջ։` alone). **PD T3 cannot test.** |

## 11. R2 / R3 / R4 verdict

### R3 (whole-T1 widening) — **WORKS**

PD T1 has four references to the place stem `հին կամուրջ` across
sentences 1, 2, 3, and 10. **Every single one preserves the full
schwa-not-dropped form.** Specifically:

- s1: `դեպի հին կամուրջ` ✓
- s2: `Հին կամուրջի վրայով քայլելիս` ✓
- s3: `Կամուրջի տակ՝ հստակ ձայնով երգող առվակը` ✓
- s10: `Հին կամուրջից լսվում էին հեռականի մարմանդ ձայներ` ✓

**No `կամրջի` anywhere in T1.** Direct fix for the
v3.2.1 mp2 (`d9c36ca`) PD T1 sentence 3 `կամրջի տակով` defect that
the v3.2.2 R3 widening was designed to address.

This is the **single most important load-bearing test** of v3.2.2,
and v3.2.2 passed it on the first paid PD sample.

### R4 (per-band ladder + ceiling guard) — **WORKS on PA, PD T3 unknown**

PA T3 = 82 words in 70-100 band. **Replicates v3.2.2 mp1 exactly
(also 82w).** Two consecutive mid-band PA T3 samples — the
structural fingerprint is real, not sampling luck.

For PD: T1+T2 land in-band (136w in 130-180; 135w in 100-140).
T3 untested due to http_429.

### R2 (verb-default + named-token list) — **PARTIAL — NAMED FAMILY HELD, NEW FABRICATIONS APPEARED**

The four mp2 named fabrications (`խորոտալ`, `համբարձլ`,
`փափախերները`, `պարգևիր`) did **NOT recur** in any of the 5
completed turns. The R2 named-family suppression continues to hold.

**But seven new fake / wrong forms appeared across PA + PD:**

| Token | Turn | Class | Likely intended |
|---|---|---|---|
| `կարապված` | PA T1 | fabricated participle | `փաթաթված` |
| `շնորակալությամբ` | PA T3 | typo (missing `հ`) | `շնորհակալությամբ` |
| `մեղմշխարհն` | PA T3 | fabricated compound | `մեղմ աշխարհը` |
| `իրեբերում էր` | PD T1 | fabricated verb | `իր մոտ բերում էր` / `իր հետ էր կրում` |
| `հեռականի` | PD T1 | non-standard adj | `հեռվից` |
| `շտեպնով` | PD T1 | fabricated adv | `շտապով` |
| `տերմինները` | PD T2 | wrong-word selection | `հնչյունները` / `ձայները` |

**R2's named-exemplar + verb-default approach is reactive — novel
coinages keep appearing.** The same partial-success pattern as
v3.2.2 mp1 (which produced `Խտնված`). **R2 needs a structural
rethink, not just more named examples** — see § 14.

## 12. Quality verdict

### Plan A (`age-4-simple` — full 3-turn sample)

| Axis | Score | Note |
|---|---|---|
| Armenian naturalness | **2/5** | T1 `կարապված` (fabrication); T1 `արտասավոր էր ցողի կաթիլներով` (off-register for age-4); T1 `հարմարեցնում նրան` (awkward verb); T3 `շնորակալությամբ` (typo); T3 `մեղմշխարհն` (fabricated compound); T3 `զգացվեց բարի և խաղաղ բերող` (broken syntax). Worse than v3.2.2 mp1's PA. |
| Eastern Armenian correctness | **2/5** | Multiple new fabrications + one typo. R2 not catching novel forms. |
| Fairy-tale feeling | **3/5** | Tatik thread present; T2 introduces a `քարտեզ` (map on the leaf) not in `plan.magicalObject` — slight plan-drift. |
| Warmth | **4/5** | Consistent gentle register. |
| Length / pacing | **3/5** | T1 ≈123w in 90-130 ✓ (near upper end); T2 ≈74w in 70-110 ✓ (just above floor); T3 ≈82w in 70-100 ✓ (mid-band). All in-band. |
| Choice quality | **4/5** | Both blocks byte-for-byte; same inherited PA T2 scenario defect. |
| Continuation coherence | **3/5** | Map-on-leaf added in T2 (plan-drift); T3 resolution gestures at gift but R6 still partial. |
| Safety | **PASS** | Age-appropriate. |

### Plan D (`age-7-richer` — T1+T2 sample, T3 untested)

| Axis | Score | Note |
|---|---|---|
| Armenian naturalness | **2/5** | T1 `իրեբերում էր`, `հեռականի`, `շտեպնով` (three fabrications); T2 `տերմինները` (wrong word). Same R2 leak pattern as PA. |
| Eastern Armenian correctness | **2/5** | R3 schwa-stem PASSES cleanly (major win). But R2 fabrications across both turns. |
| Fairy-tale feeling | **3/5** | Wintry atmospheric; bridge + brook + duck on theme. |
| Warmth for age 4–7 | **4/5** | Mole comforting frightened duck — direct execution of `relationship: հերոսը մխիթարում է վախեցած կերպարին`. |
| Age-profile fit | **4/5** | T1+T2 use longer-sentence age-7-richer register correctly. |
| Length / pacing (T1/T2 only) | **4/5** | **PD T1 = 136w in 130-180 ✓; PD T2 = 135w in 100-140 ✓.** Major improvement vs v3.2.1 mp2's UNDER-floor pattern. PD T3 unknown. |
| Choice quality | **5/5** | Both blocks byte-for-byte; T2 verb-match on `տարավ` (clean C8a). |
| Continuation coherence | **3/5** | T1→T2 ok; T3 unknown. |
| Safety | **PASS** | Age-appropriate. |

## 13. Comparison to prior runs

| Aspect | v3.2.1 mp2 (`d9c36ca`) | v3.2.2 mp1 (`4649dda`) | **v3.2.2 mp2 (this run)** |
|---|---|---|---|
| PA T3 words | ≈102 (+2 OVER ceiling) | ≈82 (mid-band) | **≈82 (mid-band — replicated)** |
| PD T3 words | ≈91 (-9 below floor) | n/a | **incomplete (http_429)** |
| PD T1 words | ≈87 (UNDER -43) | n/a | **136 ✓ in band** |
| PD T2 words | ≈81 (UNDER -19) | n/a | **135 ✓ in band** |
| English leak | NO | n/a | NO (T1+T2) |
| Schwa drop | YES T1 sentence 3 | n/a | **NO — R3 win** |
| Named R2 family | `խորոտալ`/`համբարձլ`/`փափախերները`/`պարգևիր` (4 in PD T3) | none recurred | **none recurred** |
| Other R2 fabrications | (named family above) | `Խտնված` | **7 new** (`կարապված`, `շնորակալությամբ`, `մեղմշխարհն`, `իրեբերում`, `հեռականի`, `շտեպնով`, `տերմինները`) |
| C1 | PASS strict | PASS strict | **FAIL** (`Մի գեղեցիկ օրը,`) |
| C8a (PD T2 verb match) | WEAK PASS (`տվեց`) | n/a | **PASS clean (`տարավ`)** |

## 14. Engineering verdict

- **Engineering smoke: PARTIAL.** 5/6 succeeded; PD T3 failed with
  http_429 due to OpenAI gpt-4o TPM tier limit. Bake-off prompt sha
  `908ae30e...` matches v3.2.2; no upstream parsing errors, no
  prompt drift, no unintended scope.
- **R3 (whole-T1 widening): WIN.** Direct fix for v3.2.1 mp2's PD
  T1 schwa-drop defect. Most important load-bearing test of
  v3.2.2; passed cleanly on first paid PD sample.
- **R4 (per-band ladder + ceiling guard) on PA: WIN — REPLICATED.**
  PA T3 mid-band landing replicated across mp1 + mp2 (both at
  exactly 82w). Structural fingerprint is real.
- **R4 on PD T3: UNKNOWN.** http_429 blocked the load-bearing PD
  T3 floor test.
- **R2 (verb-default + named-token list): PARTIAL — INSUFFICIENT.**
  Targeted family from v3.2.1 mp2 stays suppressed; novel coinages
  keep appearing. Seven new fabrications across PA + PD on this
  run, plus one (`Խտնված`) on v3.2.2 mp1. **The named-exemplar
  approach is reactive, not categorical.** A v3.2.3 design pass is
  needed to rethink R2 structurally.
- **C1 (forbidden opener): REGRESSION.** PA T1 opens with
  `Մի գեղեցիկ օրը,` — explicit instance of rule A's
  `Մի գեղեցիկ օր` template. Did not happen in v3.2.2 mp1. Could be
  sampling variance or could be that the longer prompt is causing
  rule-A attention degradation under the 32k input-token load.
  Either way, hard-rule violation — must be addressed.
- **OpenAI gpt-4o + v3.2.2 is NOT production-ready.** Three real
  blockers: (a) C1 regression, (b) R2 fabrication leakage
  consistent across mp1+mp2, (c) PD T3 closure-floor untested.
- **Provider decision: do NOT make.**
- **Production integration: do NOT integrate.** No runtime /
  `ChatService` / runtime system prompt / `appsettings` /
  `*.csproj` / test / seed-bank / name-bank / generator /
  validator / TTS / STT change is implied or authorized by this
  run.

### Recommended next step (no action taken; awaits explicit GO)

1. **Record this evidence first** — this file. Free; preserves the
   R3 whole-T1 win, R4 PA replication, C1 regression, R2 novel-
   coinage findings, and the http_429 partial-run reality past the
   session boundary.
2. **Author a v3.2.3 design plan (NO PAID CALL)** addressing:
   - **R2 structural rethink.** The named-exemplar approach is
     proven insufficient (7 new fabrications across this run alone).
     Possible directions: (a) a reflective "before emitting an
     unusual word, name a 3rd-person past form for the verb stem
     or pick a simpler word" rule; (b) explicit "no compound nouns
     not in standard EA dictionaries" prohibition; (c) a closure-
     vocabulary whitelist for the highest-risk turn (T3); (d)
     consider lower decoding temperature on the production caller
     when this lands in runtime — decoding-level fix, not
     prompt-level.
   - **R1/A — strengthen forbidden-opener template** with explicit
     definite-article variants (`Մի գեղեցիկ օրը`, `Մի գեղեցիկ
     առավոտը`, `Մի անգամ` etc.) so the strict gate cannot be
     interpreted as "without article only."
   - **R3 + R4 — keep as-is.** Both demonstrate working structural
     fixes; do not regress them.
   - **Plan a follow-up paid validation strategy** that handles
     the gpt-4o 30k-TPM tier limit. Options: (a) PD-only mp1 (≈
     half the input-token load); (b) mp2 with a sleep between
     scenarios to allow the TPM window to refresh; (c) request an
     OpenAI tier upgrade (operator side, out of scope for this
     repo).
3. **No more paid calls until v3.2.3 design lands.** Running mp2
   again on v3.2.2 would only confirm the C1 regression is real
   under different sampling and/or surface a similar partial-run
   if TPM is hit again. The R3 + R4 PA findings are already strong
   enough; the open questions (R2 structural fix + PD T3 floor +
   C1 strengthening) all point to design work, not more paid runs.

## 15. Raw outputs (5 succeeded turns)

Reproduced from `results.json` for archival. PD T3 has no
`assistantContent` (rate-limited).

### Plan A — Turn 1 (3940 ms — 5703 in / 296 out — stop_reason: stop)

> **Մի գեղեցիկ օրը,** շնիկը և իր շուն ընկերն զբաղված էին խնձորենու այգում։ Արևի տաք շողերը շուրջը փարթամ ջերմություն էին տարածել, իսկ հասած դեղձի թավշյա մաշկը ներգրավում էր նրանց ուշադրությունը։ Խնձորենու այգում ամեն ինչ կարծես տաք հիշատակներով **կարապված** մի հեքիաթ էր, ու շնիկը սիրում էր լսել տատիկի հին պատմությունները այգու մասին։
>
> Բայց այսօր մի նոր խնդիր ծագել էր. արագիլը փնտրում էր հանգստանալու տեղ, բայց ոչ մի վայր չէր հարմարեցնում նրան։ Ամեն մի ճյուղ ծանր էր կամ շատ լայն։ Շնիկը մտածում էր, թե ինչպես կարող է օգնել։
>
> Նա հիշեց, որ ունի մի սքանչելի տերև, որ արտասավոր էր ցողի կաթիլներով։ Դա կախարդական էր և կարող էր օգնել։ Սակայն այն պետք է ճիշտ օգտագործել։ Նա չգիտեր՝ մոտեցնել ցողի կաթիլներով տերևը արևի լույսին, թե գնալ դեպի խնձորենու այգի։
>
> Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին
> Բ: գնալ դեպի խնձորենու այգի

### Plan A — Turn 2 (2327 ms — 6523 in / 207 out — stop_reason: stop)

Selected: `Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին`

> Շնիկը մոտեցրեց ցողի կաթիլներով տերևը արևի շողերին։ Կաթիլները փայլեցին այնքան գեղեցիկ, որ թվում էր, թե նրանք փոքրիկ աստղիկներ են։ Շնիկի հավատարիմ շուն ընկերը կանգնած էր նրա կողքին և հիացմունքով դիտում էր։ Նրանք զարմանքով նկատեցին, որ տերևի վրա հայտնվեց մի փոքրիկ նկար՝ խնձորենու այգու ճանապարհի **քարտեզն** էր։
>
> Արագիլը անհամբեր սպասում էր։ Շնիկը մտածում էր՝ արդյոք պետք է ուղեկցել նրան մինչև երկնքի եզրը, կամ մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն։
>
> Ա: ուղեկցել արագիլին մինչև երկնքի եզրը
> Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն

### Plan A — Turn 3 (2114 ms — 7257 in / 210 out — stop_reason: stop)

Selected: `Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն`

> Շնիկը որոշեց մնալ խնձորենու այգում և նայել, թե ինչպես է արագիլը թռչում տուն։ Նա վերցրեց ցողի կաթիլներով տերևը և հանգիստ այն փոխանցեց արագիլին՝ որպես փոքրիկ նվեր։ Արագիլը **շնորակալությամբ** շշնջաց և վայրկյան անց թևերը փռելով երկինք հասավ։ Նրա թևերի հպումը զգացվեց բարի և խաղաղ բերող։
>
> Խնձորենու այգու տաք ու հյուրընկալ հերոսները կանգնած նայում էին, թե ինչպես է արագիլը հեռանում։ Այն պահին, երբ արագիլը սահում էր երկնքում, խնձորենու վրա խոնարհվող տերևները **մեղմշխարհն** էին լուսավորում, իսկ շնիկը և շունը օդում դեռ պահում էին տատիկի պատմության տաքությունը։
>
> Վերջ։

### Plan D — Turn 1 (3702 ms — 5843 in / 384 out — stop_reason: stop)

> Երբ ձմեռային մեղմ թվացող օրը մոտեցավ, մողեսը վազում էր դեպի **հին կամուրջ**։ **Հին կամուրջի վրայով** քայլելիս, նա զգաց հին փայտի բույրը՝ քնքուշ ու հանգստացնող։ **Կամուրջի տակ**՝ հստակ ձայնով երգող առվակը, այժմ լռել էր։ Մողեսը ստիպված էր կանգ առնել ու մտածել, թե ինչու ձյունը չի հալվել։ Քնքուշ քնած բանալին **իրեբերում** էր՝ հույսի **շտեպնով** պահելով, որ այս կախարդական օգնությունը կարող է վերադառնալ։
>
> Այդ պահին, մողեսի սիրած ընկերը՝ փոքրիկ բադիկը, վախեցած քարշ էր գալիս մոտենալով։ Բադիկը փախչել էր տաքուկ ինչ-որ վայր, երբ ձյունը սպիտակ ծածկոցի պես ծածկել էր ամեն ինչ։ Մողեսը նրբորեն մխիթարեց նրան ու ասաց. «Մի՛ վախենա, ես այստեղ եմ քեզ հետ»։
>
> Մեջքը ծալելով՝ մողեսը նստեց բադիկի կողքին։ **Հին կամուրջից** լսվում էին **հեռականի** մարմանդ ձայներ, որոնք գունազարդվում էին քնած ծաղիկների քաղցր հոտով։ Մողեսն այնուամենայնիվ ուզում էր, որ առվակը նորից երգեր, ու դա կպահանջեր թարգմանել կախարդական բանալու խոսուն հմայքը։
>
> Ա: տանել քնած բանալին ընկերոջ մոտ
> Բ: կանգնել ու լսել հին կամուրջ-ի ձայները

(Bolded forms in T1 flag four `հին կամուրջ`/`Կամուրջի` mentions all
schwa-preserved — the R3 whole-T1 win — plus three new fabrication
candidates `իրեբերում`, `շտեպնով`, `հեռականի`.)

### Plan D — Turn 2 (2927 ms — 6799 in / 352 out — stop_reason: stop)

Selected: `Ա: տանել քնած բանալին ընկերոջ մոտ`

> **Մողեսը քնած բանալին զգուշորեն տարավ բադիկի մոտ։** Բադիկը վախեցած նայեց նրան, բայց մողեսի բարություն ու խնամք ցույց տվող աչքերը նրան հանգստացրին։ Նրբորեն բռնելով բանալին, մողեսը փորձեց հասկանալ, թե ինչպես կարող է այն օգնել ձյունին հալվել և առվակին նորից երգել։
>
> Տաք օդը շնչելով՝ մողեսն ու բադիկը լսում էին **հին կամուրջի տակ** դողդոջող առվակի ձայնը։ Օրը անցնում էր, և ձյունը դեռ չէր հալվել։ Բայց մողեսը համոզված էր, որ համբերությունը դրանք կվերադարձներ նախկին տեսքի։ Բադիկը դողում էր, բայց մողեսի հետ նստած՝ նա կարծես զգում էր, որ ամեն ինչ լավ կլինի։
>
> **Հին կամուրջը** խաղաղ էր ու լի էր խաղաղության զգացմունքներով։ Բոլոր **տերմինները** և ձայները, ինչ ստացվում էին հին փայտի բույրից և քնած ծաղիկների մեղմ հոտից, դարձրած էին մթնոլորտը ավելի հարուստ ու կախարդական։ Նրանք պատրաստվում էին ցույց տալ, թե ինչպես այս ձմեռային հաճելի պարզությունը կարող էր իրենց օգնել։
>
> Ա: փորձել արթնացնել քնած բանալին մեղմ շշունջով
> Բ: համբերել ու լսել հին կամուրջի տակ առվակի շշուկը

(Bolded T2 first sentence flags clean C8a verb-match `տարավ`. Two
more `հին կամուրջ` mentions schwa-preserved. `տերմինները` is the
wrong-word selection slip.)

### Plan D — Turn 3 — INCOMPLETE (1223 ms — http_429)

```
{
  "error": {
    "message": "Rate limit reached for gpt-4o ... on tokens per min (TPM):
                Limit 30000, Used 26265, Requested 7881.
                Please try again in 8.292s.",
    "type": "tokens",
    "code": "rate_limit_exceeded"
  }
}
```

No `assistantContent`. No `stopReason`. No `tokenUsage`. The
runner correctly recorded `errorKind: "http_429"` and continued.
**This turn must be re-run on a follow-up paid call (operator GO
required) to complete the v3.2.2 PD T3 evidence.**

## 16. No secrets included

This file contains **no API key**, no `OPENAI_API_KEY` value, no
token, no bearer credential, no parent JWT, no device API key, and
no private endpoint. The `OPENAI_API_KEY` env var used to authorize
the run was loaded inline from `dotnet user-secrets` for
`backend/src/ArmenianAiToy.Api` into the same PowerShell process as
the `dotnet run`, then immediately consumed; only its length (164)
was printed. Nothing about the key is echoed in this document or in
the captured `results.json` / `review.md` / `summary.json`. The
http_429 error body quoted above contains the OpenAI organization
id (`org-CBUghJziXjGMBD07FkRfgIHH`) which is non-secret operator
metadata, not a credential.

## Scope guard

No production / runtime files were touched by this run or by this
evidence file: `ChatService`, backend code, frontend, `appsettings*.json`,
`*.csproj`, tests, seed bank, name bank, story-plan generator,
validator, runtime system prompts (production sha
`54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
unchanged), speech / TTS / STT — all unchanged. The bake-off tooling
(`tools/StoryModelBakeoff/`) is the only code that ran, and only its
build-output `results/` directory was written to (gitignored). The
`bin/Debug/net10.0/results/20260509T215551Z/` artifacts are not
committed and never will be — this evidence file is the only
repo-tracked artifact of the run.

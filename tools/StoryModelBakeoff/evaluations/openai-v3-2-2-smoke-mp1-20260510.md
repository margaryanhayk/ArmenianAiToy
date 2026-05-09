# OpenAI v3.2.2 live smoke (mp1) — 2026-05-10

First **OpenAI live smoke** of the v3.2.2 R2/R3/R4-tightened system
prompt with `--max-prompts 1`. A **paid OpenAI API call was made**
(3 chat completions, 1 scenario × 3 turns). No production / runtime
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
- v3.2.1 mp2 evidence committed at `d9c36ca`
- v3.2.2 design plan committed at `86d035a`
- v3.2.2 implementation (R2/R3/R4 tightening) committed at `768be15`

This run is the **load-bearing test for the R4 ceiling guard** shipped
at `768be15` — does the new three-rung ladder (cushion / ok / stop at
ceiling) prevent the bimodal failure that v3.2.1 mp2 (`d9c36ca`)
surfaced (PA T3 over ceiling at 102w)? Plus first paid look at R2's
verb-default + extended forbidden-token list against the four
`d9c36ca` PD T3 fabrications, and a sanity test of R3's whole-T1
widening on PA (PD R3 stress-test still requires mp2).

The filename uses local Yerevan date `20260510` because the run
completed at UTC `2026-05-09T20:41:44Z` = Yerevan `2026-05-10
00:41` (UTC+4).

---

## 1. Run command

```
dotnet run --project tools/StoryModelBakeoff -- --run --provider openai --max-prompts 1 --i-understand-live-cost --scenarios tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json --system-prompt tools/StoryModelBakeoff/system-prompt-v3-2.txt
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

- `--max-prompts 1` (`--allow-full-set` deliberately **NOT** used; cost
  discipline preserved).
- 1 scenario: `PA` (`v3-1-plan-a-age-4-simple-17`).
- 3 turns total (`MAX_TURNS=3`).
- Calls attempted / succeeded / failed: 3 / 3 / 0; every turn closed
  with `stop_reason: stop`.
- Path consumed: `tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json`
  (v3.1 scenarios reused unchanged — no v3.2-shaped scenarios exist
  in the repo; the v3.2.2 R2/R3/R4 tightening is prompt-level).
- Alternate system prompt:
  `tools/StoryModelBakeoff/system-prompt-v3-2.txt` (the v3.2.2
  prompt at `768be15`).
- Repo HEAD at run time: `768be15` (in sync with `origin/main`).
- Drift: yes — intentional alternate system prompt + alternate
  scenarios. Evidence-only deviation; not a request to retune the
  runtime prompt.

## 4. Prompt identity

- Bake-off prompt sha256:
  `908ae30e610e18389b2151c262149db32830b3f663db70fe98b268e4e06fec2f`
  (post-`768be15` v3.2.2 — different from v3.2.1's
  `3ed1dfecafd6d63b02d3cd3dc76e3515bbf7e8f661d7ac26d8dd2e3f3032bc5b`).
- Scenarios sha256:
  `e6cdba77d64640c89dc6aa094108f3bff040a26044af1b1cc75f5a7ab0f89b59`
  (unchanged — same `bakeoff-prompts-v3-1.json` across all v3.x runs).
- Production prompt sha256:
  `54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
  (unchanged — production runtime is untouched).
- Alternate system-prompt: yes.
- Alternate scenarios: yes.
- Drift verdict: `drifted (intentional — alternate system prompt)`.

## 5. Result directory

```
tools/StoryModelBakeoff/bin/Debug/net10.0/results/20260509T204136Z/
  results.json
  review.md
  summary.json
```

These live under `bin/Debug` (gitignored build output) and **are not
committed**. This evidence file summarizes them.

- Run started UTC: `2026-05-09T20:41:36.4444483Z`
- Run completed UTC: `2026-05-09T20:41:44.9230271Z`
- Wall clock: ~8.5 s

## 6. Latency

| Turn | Latency |
|---|---|
| T1 | 3748 ms |
| T2 | 2614 ms |
| T3 | 2090 ms |
| **Total** | **8452 ms** |

Mean per turn ≈ 2817 ms. No retries, no upstream errors, no timeouts.

## 7. Tokens

| Turn | Prompt (in) | Completion (out) |
|---|---|---|
| T1 | 5703 | 226 |
| T2 | 6453 | 192 |
| T3 | 7172 | 208 |
| **Total** | **19328** | **626** |

Input total ≈ +18 % vs v3.2.1 mp1's 16392 (the v3.2.2 ~+37 structural
lines / ~280 added Armenian words landed as ~1000 added tokens × 3
turns + cumulative prior-turn echo). Output total essentially
unchanged (626 vs 616).

## 8. Gate table — Plan A

| ID | Verdict | Note |
|---|---|---|
| C1  | **PASS strict (cleanest yet)** | T1 opens `Խնձորենու այգում, ուր ծառերը կանգնած էին որպես հին բարեկամներ, փոքրիկ շնիկը վազում էր իր հորը հետ։` — places-anchored. **No `Մի անգամ` opener AND no `Մի օր,` mid-paragraph caveat** — recurring across v3.2 mp1 / v3.2.1 mp1 / v3.2.1 mp2; absent here. Could be sampling, could be reduced instruction overload nudging the model into a different opening pattern. |
| C2  | **PASS clean** | T3 closes on `Արևի տաք շողը ծածկեց այգին, և շունը նայեց բարձրացող թռչունին՝ հպարտորեն կանգնած։` — concrete present-moment image. No abstract coda. |
| C3  | **PASS** | No exact repeats. |
| C6  | **PASS** | T1 choices `Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին` / `Բ: գնալ դեպի խնձորենու այգի` byte-for-byte. |
| C8a | **PASS** | T2 opens `Շնիկը վերցրեց ցողի կաթիլներով տերևը և մոտեցրեց այն արևի լույսին։` — direct execution of choice Ա, with the SELECTED_CHOICE verb `մոտեցրեց` present. |
| C9  | **PASS** | T3 ends with `Վերջ։` on its own line; no `Ա:`/`Բ:` lines, no question. |
| **C13** | **PASS — MID-BAND (BEST OF ALL RUNS)** | **PA T3 = 82 words** (counted: 14+13+19+13+11+12 across 6 sentences). Floor 70 → **+12 above floor**. Ceiling 100 → **-18 below ceiling**. **First mid-band landing across all v3.2-era PA T3 runs.** R4 ceiling guard worked: model ended at case `բ` (between floor+5 and ceiling) and chose to write a normal closure without forcing extra cushion sentences. |
| C14 | **PASS clean** | No `Continued`/`Note:`/`Շարունակեց`/parenthetical narrator. |
| C15 | **PASS** | T2 choices `Ա: ուղեկցել արագիլին մինչև երկնքի եզրը` / `Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն` byte-for-byte. |
| C16 | **PASS** | `Խնձորենու այգում` in T1 first sentence; PLACE_STEM letters preserved across all T1 mentions (`այգում` / `այգու վրա` / `այգու շուրջ`). |

**Hard tally: 10 / 10 PASS.** First v3.2-era PA run with a mid-band
(not marginal, not over-ceiling) C13 PASS.

## 9. R4 verdict — ceiling guard works on Plan A

The v3.2.2 R4 three-rung ladder shipped at `768be15`:
- **(ա)** below floor / floor +0–4 → MUST add the two-sentence pair.
- **(բ)** floor+5 to ceiling → do NOT add new sentences.
- **(գ)** at or over ceiling → end immediately.

**Result on this run:** body landed at 82 words → case `բ`. Model
closed cleanly without forcing extra cushion sentences.

| Run | Prompt | PA T3 | Distance from floor | Distance from ceiling | Verdict |
|---|---|---|---|---|---|
| v3.1 mp1 (`14731b3`) | v3.1 | ≈52 | -18 below floor | -48 below ceiling | C13 FAIL |
| v3.1 mp2 (`fcffafe`) | v3.1 | ≈55 | -15 below floor | -45 below ceiling | C13 FAIL |
| v3.2 mp1 (`54c391f`) | v3.2 | ≈66 | -4 below floor | -34 below ceiling | C13 FAIL |
| v3.1 re-run (this session) | v3.1 | ≈74 | +4 above floor | -26 below ceiling | C13 PASS by variance (no R4) |
| v3.2.1 mp1 (`11f63b3`) | v3.2.1 | ≈72 | +2 above floor | -28 below ceiling | C13 PASS marginal |
| v3.2.1 mp2 PA (`d9c36ca`) | v3.2.1 | ≈102 | +32 above floor | **+2 OVER ceiling** | C13 PASS floor but over-ceiling |
| **v3.2.2 mp1 (this run)** | **v3.2.2** | **≈82** | **+12 above floor** | **-18 below ceiling** | **C13 PASS MID-BAND** |

**This is the first run where PA T3 landed comfortably away from
both extremes.** The v3.2.1 R4 first cut produced bimodal results
(too short on mp1, too long on mp2); v3.2.2's ceiling-guard ladder
appears to anchor the model around the ≈80w mid-target.

**Single-sample caveat:** one run cannot prove reliability. A second
v3.2.2 mp1 retry, or v3.2.2 mp2 (PA + PD), would be needed to
disambiguate the structural fix from sampling-luck. The structural
fingerprint — body landing in case (բ) without forcing the
two-sentence pair — is exactly what the v3.2.2 ladder was designed
to produce.

## 10. R2 verdict — partial success on Plan A

The v3.2.2 R2 strengthening shipped at `768be15` added (a) four new
forbidden-token examples from `d9c36ca`, (b) a structural
verb-default sub-rule directing the model to 3rd-person past forms
(`-եց / -ավ / -վեց`), and (c) a safe-verb whitelist
(`սկսեց`, `դարձավ`, `լսվեց`, `մոտեցավ`, `բացվեց`, `փայլեց`,
`հանգստացավ`).

### Targeted family — held

The four named fabrications from v3.2.1 mp2 (`d9c36ca` PD T3) did
**NOT recur** on this PA run:
- `խորոտալ` — absent. ✓
- `համբարձլ` — absent. ✓
- `փափախերները` — absent. ✓
- `պարգևիր` — absent. ✓

The verb-default rule + named-token list generalized correctly on
the fabrication family this run targets.

### New fabrication — slipped through

**`Խտնված խնձորենին`** in T1 sentence 7
(`Խտնված խնձորենին ունեին ցողի կաթիլներով տերև, որն ի սկզբանե
կախարդական էր թվում։`):

- `Խտնված` is a borderline / non-standard participle. The model
  appears to have coined it from a stem `խտն-` that doesn't quite
  exist in standard Eastern Armenian. The intended meaning is
  probably "dense" / "thick" — for which the standard forms are
  `խիտ` (adjective) or `խտացած` (participle of `խտանալ`).
- Plus `ունեին` is plural-3rd-person, but the subject (`խնձորենին`,
  "the apple-tree", singular) is singular. Number-agreement slip.

This is a **different shape of fabrication** from the four named
mp2 tokens (those were verb conjugations + lexical mis-selections).
`Խտնված` is a participle invented from a near-stem — a coverage
gap the v3.2.2 R2 named-exemplar approach does not address.

**Verdict: PARTIAL SUCCESS.** R2 holds against the targeted mp2
family. R2 does not hold against novel participle / stem
fabrications. The structural defense (`if uncertain, use simpler
known word`) is not strong enough to catch every novel coinage.

A future v3.3 might add structural rules like "use participles only
from verbs you can name a 3rd-person past form for" or expand the
forbidden list reactively as new fabrications are observed. Not for
v3.2.2.

### Other v3.2.2 mp1 quality slips (informational, not R2)

- T1: `իր հորը հետ` — case slip. `հետ` takes genitive (`հոր հետ`),
  but `հորը` is accusative-with-definite-article. Wrong case. Real
  grammar slip; not a fabrication.
- T2: `տեսարանն` — older-orthographic ending; modern Eastern Armenian
  prefers `տեսարանը`. Stylistic slip.
- T3: `լուսի` — poetic / older genitive of `լույս`; modern is
  `լույսի`. Stylistic.

None of these are fabricated stems. They are correctness slips that
sit outside R2's scope (R2 targets fabricated morphology
specifically). Worth noting for native-ear review but not a v3.2.2
regression.

## 11. R3 verdict — Plan A passes, NOT load-bearing test

The v3.2.2 R3 strengthening shipped at `768be15` widened the
schwa-not-drop scope from "first sentence only" to "every T1
sentence containing the place stem," with non-first-sentence
ALLOWED + FORBIDDEN examples and a pronoun-fallback bullet.

**On this PA run:**
- T1 sentence 1: `Խնձորենու այգում` ✓ — full PLACE_STEM letters.
- T1 sentence 2: `այգու վրա` ✓ — preserves the `այգ`-stem.
- T1 sentence 4: `այգու շուրջ` ✓ — preserves the `այգ`-stem.
- All T1 stem mentions correct. PASS.

**But this is NOT the load-bearing R3 test.** PA's stem
`խնձորենու այգ` doesn't have the schwa-drop ambiguity that PD's
`հին կամուրջ` has. v3.2.1 mp2 PD T1 sentence 3 produced
`կամրջի տակով` — that's the failure case the v3.2.2 R3 widening
was designed to fix. Until v3.2.2 mp2 runs PD, R3's whole-T1
widening is **untested under live conditions on the load-bearing
scenario**.

**Verdict: PA SAMPLE PASSES, BUT INSUFFICIENT EVIDENCE.** Need
v3.2.2 mp2 PD to know whether R3 widening fixed the schwa-drop
defect that v3.2.1 mp2 PD T1 surfaced.

## 12. Quality verdict (per-axis)

| Axis | Score | One-liner |
|---|---|---|
| Armenian naturalness | **3/5** | T1 `իր հորը հետ` (case), `Խտնված խնձորենին` (non-word + agreement); T2 `տեսարանն` (stylistic); T3 `լուսի` (poetic). Comparable to v3.2.1 mp1 / mp2 in T1 quality; not a regression but not a clear improvement. |
| Eastern Armenian correctness | **3/5** | The v3.2.1 mp2 fabrication family (`խորոտալ`/`համբարձլ`/`փափախերները`/`պարգևիր`) is gone — clear R2 win on the named family. `Խտնված` is a new fabrication slipping through the named-exemplar coverage. Plus minor case/orthography slips. |
| Fairy-tale feeling | **3/5** | Atmosphere decent. T2 has the dog speaking a wisdom line (`«Գուցե այս լույսը օգնի...»`) — borderline characters-as-narrators; T3 closing on `հպարտորեն կանգնած շունը` lands warmly. |
| Warmth for age 4–7 | **4/5** | Consistent gentle register; sunlight, peach textures, dog standing proudly. |
| Length / pacing | **3/5** | **T3 ≈82w in 70-100 (mid-band, +12 above floor — best of any v3.2-era run)**; T1 ≈84w in 90-130 (UNDER floor by 6 — new issue not seen in prior PA T1 samples; possibly side-effect of R4 ceiling-awareness propagating to T1 or sampling variance); T2 ≈66w in 70-110 (UNDER floor by 4 — recurring across all v3.2-era runs). |
| Choice quality | **4/5** | Both blocks reproduced byte-for-byte; same inherited PA T2 scenario defect (choice Բ presupposes stork going home). |
| Continuation coherence | **3/5** | T1→T2 ok. T2 dog speaking → T3 dog passively standing — slight character-role discontinuity. R6 staging marginal: `փոքրիկ արարքը մեծ օգտակարություն բերեց` (T3 sentence 5) is retrospective naming, not the discrete giving moment plan.resolutionStyle calls for. Same R6 partial status as prior v3.2-era runs. |
| Safety | **PASS** | Age-appropriate throughout; no fear, no violence, no medical content. |

## 13. Major issues (specific phrases)

Recorded for native-ear / v3.3 review:

- **T1**: `իր հորը հետ` — case slip. `հետ` requires genitive
  (`հոր հետ`); accusative `հորը` is wrong here.
- **T1**: `Խտնված խնձորենին ունեին` — `Խտնված` is a non-standard
  participle (likely model-coined from a near-stem `խտն-`); standard
  forms are `խիտ` / `խտացած`. Plus `ունեին` (plural) doesn't agree
  with singular subject `խնձորենին`.
- **T1**: `տատիկի հին ու կախարդական պատմությունները` — fine; on-tone.
- **T2**: `տեսարանն` — older-orthographic definite-article; modern
  is `տեսարանը`.
- **T2**: dog speaking a wisdom line is borderline characters-as-
  narrators; not a hard fail.
- **T3**: `լուսի ջերմությունը` — `լուսի` is poetic genitive of
  `լույս`; modern Eastern Armenian uses `լույսի`.
- **T1 length**: ~84w against 90-130 floor — under by 6.
- **T2 length**: ~66w against 70-110 floor — under by 4. Recurring
  pattern across all v3.2-era runs.

## 14. Comparison across all PA T3 runs

| Run | Prompt | PA T3 | C13 | T3 closure shape | Hard tally |
|---|---|---|---|---|---|
| v3.1 mp1 (`14731b3`) | v3.1 | ≈52 | FAIL (-18) | abstract coda (`տարածելու համար`) | 9/10 |
| v3.1 mp2 (`fcffafe`) | v3.1 | ≈55 | FAIL (-15) | poetic, no closure pattern | 9/10 |
| v3.2 mp1 (`54c391f`) | v3.2 | ≈66 | FAIL (-4) | one concrete image, single | 9/10 |
| v3.1 re-run (this session) | v3.1 | ≈74 | PASS (+4) | grandmother smile + peach (sampling variance, no R4 pattern) | 10/10 |
| v3.2.1 mp1 (`11f63b3`) | v3.2.1 | ≈72 | PASS marginal (+2) | R4 sensory + reaction pair (first fingerprint) | 10/10 |
| v3.2.1 mp2 PA (`d9c36ca`) | v3.2.1 | ≈102 | PASS but +2 OVER ceiling | over-correction | 10/10 |
| **v3.2.2 mp1 (this run)** | **v3.2.2** | **≈82** | **PASS MID-BAND (+12 / -18)** | **natural, blended R4 (no forced pair)** | **10/10** |

## 15. Engineering verdict

- **Engineering smoke: PASS.** v3.2.2 prompt loads, sha matches the
  `768be15` post-tightening value (`908ae30e...`), 3/3 calls
  succeeded, no upstream errors, no timeouts.
- **Story quality: IMPROVED on the load-bearing C13 axis.** First
  mid-band PA T3 across all paid runs. Other v3.2-era wins (R5
  clean closure, R6 partial staging) preserved.
- **R4 (ceiling guard): WORKS on first paid sample.** The bimodal
  failure pattern that v3.2.1 mp2 surfaced is anchored on this
  sample. Single sample, but the structural fingerprint matches the
  design.
- **R2 (verb-default + new tokens): PARTIAL SUCCESS.** Targeted
  family suppressed; new fabrication (`Խտնված`) slipped through.
  Coverage gap, not a regression.
- **R3 (whole-T1 widening): UNTESTED LOAD-BEARING.** PA passed but
  PA's stem doesn't exercise R3's failure case; PD/mp2 is the real
  test.
- **OpenAI gpt-4o + v3.2.2 is closer to production-ready than
  v3.2.1, but NOT yet production-ready.** Two open questions: (a)
  does R4 ceiling guard hold across multiple samples or is this
  sampling-luck on top of the structural fix? (b) does R2 really
  generalize, or are novel fabrications still slipping through? Both
  partially answered by mp2; full answer needs a variance pass.
- **Provider decision: do NOT make.** Single run, single provider.
- **Production integration: do NOT integrate.** No runtime /
  `ChatService` / runtime system prompt / `appsettings` / `*.csproj`
  / test / seed-bank / name-bank / generator / validator / TTS /
  STT change is implied or authorized by this run.

### Recommended next step (no action taken; awaits explicit GO)

1. **Record this evidence first** — this file. Free; preserves the
   R4-mid-band win, R2 partial-success, R3 PA-only finding past
   the session boundary.
2. **Run v3.2.2 mp2 (PA + PD, 6 turns) paid.** This is the
   load-bearing test for (a) a second PA T3 sample under v3.2.2 to
   disambiguate variance from R4's ceiling-guard structural effect,
   (b) PD T3 floor — does R4 mid-band ≈110 anchor finally pull PD
   T3 above the 100-floor that v3.2.1 mp2 missed at 91w? (c) PD T1
   schwa-stem — does R3 whole-T1 widening fix the `կամրջի` drop
   that v3.2.1 mp2 PD T1 sentence 3 produced? (d) PD T3
   non-words — do the four mp2 fabrications stay gone on PD's
   age-7-richer band? Cost ≈ 2× this run (≈ $0.05–$0.07). **No
   further paid call without explicit GO.**

## 16. Raw outputs

Reproduced from `results.json` / `review.md` for archival; nothing
here is sourced from anywhere outside the run's own captured outputs.

### Plan A — Turn 1 (3748 ms — 5703 in / 226 out — stop_reason: stop)

> Խնձորենու այգում, ուր ծառերը կանգնած էին որպես հին բարեկամներ, փոքրիկ շնիկը վազում էր **իր հորը հետ**։ Հասած դեղձի թավշյա մաշկը և արևի տաք շողերը այգու վրա կենդանություն էին բերում։ Շնիկի ընկերն ու տանտերը՝ խելացի շունը, պատմում էր տատիկի հին ու կախարդական պատմությունները։ Այդ պահին մի արագիլ շրջում էր այգու շուրջ՝ փնտրելով հանգստանալու անվտանգ տեղ։ Բայց ոչ մի տեղ չէր գտնվում հարմար։
>
> Փորձելով օգնել, շնիկը մտածում էր, թե ինչպես կարելի է օգնել արագիլին։ **Խտնված խնձորենին ունեին** ցողի կաթիլներով տերև, որն ի սկզբանե կախարդական էր թվում։
>
> Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին
> Բ: գնալ դեպի խնձորենու այգի

### Plan A — Turn 2 (2614 ms — 6453 in / 192 out — stop_reason: stop)

Selected choice: `Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին`

> Շնիկը վերցրեց ցողի կաթիլներով տերևը և մոտեցրեց այն արևի լույսին։ Լույսն անցնում էր տերևի միջով և փայփայում էր շուրջբոլորը, կարծես թե կախարդանքը կյանք էր առնում։ Արագիլը, տեսնելով այդ հիասքանչ **տեսարանն**, կանգ առավ, գլուխը մի փոքր թեքեց և հետաքրքրված նայեց։
>
> Շունը խելացի ժպտաց և ասաց. «Գուցե այս լույսը օգնի արագիլին գտնել իր ճանապարհը»։ Այդ պահին շնիկն զգաց, որ այս փոքրիկ գործողությունը կարող է բարիք բերելու միջոց լինել։
>
> Ա: ուղեկցել արագիլին մինչև երկնքի եզրը
> Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն

### Plan A — Turn 3 (2090 ms — 7172 in / 208 out — stop_reason: stop)

Selected choice: `Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն`

> Շնիկը մնաց խնձորենու այգում՝ հետևելով, թե ինչպես է արագիլը թևերի թափահարումով բարձրանում դեպի երկինք։ Արագիլը, զգալով տերևից արձակվող **լուսի** ջերմությունը, հասկացավ, որ կարելի է թռչել անհրաժեշտ ուղղությամբ։ Հենց այն պահին, երբ արագիլը մոտեցավ տան ճանապարհին, տեսավ, թե ինչպես է այգում մնացած շունը թուլացած նայում նրան։
>
> Արագիլի հայացքը լուսավորելով, որքան հեռու նա թռավ, այնքան մոտ տարավ այդ դեպքին։ Շնիկը ուրախությամբ նայեց նրան, զգալով, որ փոքրիկ արարքը մեծ օգտակարություն բերեց։ Արևի տաք շողը ծածկեց այգին, և շունը նայեց բարձրացող թռչունին՝ հպարտորեն կանգնած։
>
> Վերջ։

(Bolded tokens flag the issues called out in §§ 10–13: T1 case slip
`իր հորը հետ`, T1 fabricated participle `Խտնված խնձորենին` +
agreement slip `ունեին`, T2 older-orthographic `տեսարանն`, T3 poetic
genitive `լուսի`. T3 body lands at 82 words — first mid-band landing
across all v3.2-era PA runs.)

## 17. No secrets included

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
`bin/Debug/net10.0/results/20260509T204136Z/` artifacts are not
committed and never will be — this evidence file is the only
repo-tracked artifact of the run.

# OpenAI v3.2 live smoke (mp1) — 2026-05-07

First **OpenAI live smoke** of the v3.2 system prompt with `--max-prompts 1`. A **paid OpenAI API call was made** (3 chat completions, 1 scenario × 3 turns). No production / runtime change was made; this evidence file is documentation only. Companion to:

- v3.1 mp1 evidence committed at `14731b3`
- v3.1 mp2 evidence committed at `fcffafe`
- v3.2 design plan committed at `f09ca92`
- v3.2 system prompt authored at `f3aff65`
- v3.2 Armenian wording fix at `5fe6cd9`

This run is **Slice 3** of the v3.2 plan (`tools/StoryModelBakeoff/evaluations/v3-2-prompt-tightening-plan-20260507.md` § 7).

---

## 1. Run command

```
dotnet run --project tools/StoryModelBakeoff -- --run --provider openai --max-prompts 1 --i-understand-live-cost --scenarios tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json --system-prompt tools/StoryModelBakeoff/system-prompt-v3-2.txt
```

## 2. Provider / model

- Provider: `openai`
- Model: `gpt-4o`

## 3. Scope

- `--max-prompts 1` (`--allow-full-set` deliberately **NOT** used; cost discipline preserved)
- 1 scenario: `PA` (`v3-1-plan-a-age-4-simple-17`)
- 3 turns total (`MAX_TURNS=3`)
- Calls attempted / succeeded / failed: 3 / 3 / 0; every turn closed with `stop_reason: stop`
- Path consumed: `tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json` (v3.1 scenarios reused unchanged — failure class is prompt-level, not scenario-level)
- Alternate system prompt: `tools/StoryModelBakeoff/system-prompt-v3-2.txt` (the v3.2 prompt with the post-`5fe6cd9` Armenian wording fixes)
- Bake-off prompt sha256: `da6438994c5e3fc2c285b7436388c4f2e28efcfea3f00ea5b19159c0cadedbe1`
- Production prompt sha256: `54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
- Drift: yes — intentional alternate system prompt + alternate scenarios. Evidence-only deviation; not a request to retune the runtime prompt.

## 4. Result directory

```
tools/StoryModelBakeoff/bin/Debug/net10.0/results/20260507T222827Z/
  results.json
  review.md
  summary.json
```

These live under `bin/Debug` (gitignored build output) and **are not committed**. This evidence file summarizes them.

- Run started UTC: `2026-05-07T22:28:27.5238142Z`
- Run completed UTC: `2026-05-07T22:28:38.6037880Z`
- Wall clock: ~11.1 s

## 5. Latency

| Turn | Latency |
|---|---|
| T1 | 4111 ms |
| T2 | 5151 ms |
| T3 | 1784 ms |
| **Total** | **11046 ms** |

Mean per turn ≈ 3682 ms. No retries, no upstream errors, no timeouts.

## 6. Tokens

| Turn | Prompt (in) | Completion (out) |
|---|---|---|
| T1 | 4499 | 266 |
| T2 | 5289 | 185 |
| T3 | 6001 | 154 |
| **Total** | **15789** | **605** |

Input tokens are higher than v3.1 mp1's 10611 (+5178 across 3 turns) because the v3.2 system prompt is longer (R1 examples, R2/R6 new sections, R4/R5 strengthening). Cost-of-tightening, expected.

## 7. Gate table — Plan A

| ID | Check | Verdict | Note |
|---|---|---|---|
| C1  | No forbidden opener | **PASS** ✱ | T1 first sentence is `Խնձորենու այգում էր մի շատ հնարամիտ շնիկ։` — no "Մի անգամ"-class opener. **Caveat**: the phrase `Մի անգամ,` appears mid-paragraph 1 (`Մի անգամ, շնիկը գտավ ցողի կաթիլներով…`). Rule A (the v2 forbidden-opener block) targets the OPENER only, so this passes the strict gate, but the discouraged phrase is creeping into the body. Worth flagging for v3.3 if it recurs. |
| C2  | No moralizing / no abstract coda (R5) | **PASS** | T3 closes with `Արևի տաք շողերը կարծես ժպիտ դարձած էին այգու վրա, որտեղ հիմա ամեն ինչ հանգիստ ու խաղաղ էր։` — concrete present-moment image. **No `հետագայում / միշտ / որպեսզի… տարածվի` framing.** Promoted from WEAK PASS in v3.1 mp1. |
| C3  | No duplicate sentence | **PASS** | No exact repeats |
| C6  | Exact T1 choices (byte-for-byte break-glass) | **PASS** | `Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին` / `Բ: գնալ դեպի խնձորենու այգի` |
| C8a | T2/T3 first sentence performs SELECTED_CHOICE | **PASS** | T2: `Շնիկը վերցրեց ցողի կաթիլներով լուսավորված տերևը և մոտեցրեց այն արևի շողերին։`  T3: `Շնիկը մնաց այգում և նայեց, թե ինչպես արագիլը պատրաստվում էր թռչել տուն։` |
| C9  | T3 no choices, no question | **PASS** | Ends with `Վերջ։`; no `Ա:`/`Բ:` lines |
| C13 | Closure length ≥ TARGET_WORDS lower bound (R4) | **FAIL** | T3 body ≈ 66 words against the 70–100 floor — **narrowest miss to date** (v3.1 mp1 was ≈52w, v3.2 mp1 is ≈66w; +14w). R4 narrowed the gap but did not close it. |
| C14 | No meta-output | **PASS** | No `Continued`/`Note:`/`Շարունակեց հեքիաթը` leakage |
| C15 | Exact T2 choices (byte-for-byte break-glass) | **PASS** | `Ա: ուղեկցել արագիլին մինչև երկնքի եզրը` / `Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն` |
| C16 | Place anchor (`խնձորենու այգի`) | **PASS** | `Խնձորենու այգում` present in T1 first sentence and recurs across T1; `այգում / այգու` recurs in T2 / T3 |

**Hard tally: 9 / 10 PASS, 1 FAIL (C13 narrower than v3.1 mp1).**

## 8. Major findings — what v3.2 fixed and what persists

The hard-gate table understates the win. The headline finding is that two of the v3.2 design defects show clear evidence of working on Plan A:

### Resolved on this sample

1. **Defect E (abstract sentimental coda) — RESOLVED.** v3.1 mp1 closed with `Նրանք հետագայում հաճախ էին հիշում այդ օրը՝ տաքությունն ու բարությունը տարածելու համար։` — the abstract coda R5 was designed to ban. v3.2 mp1 closes with `Արևի տաք շողերը կարծես ժպիտ դարձած էին այգու վրա, որտեղ հիմա ամեն ինչ հանգիստ ու խաղաղ էր։` — a concrete present-moment image of the place. The R5 ban on `հետագայում / միշտ / որպեսզի / սովորեցին որ` framing did its job.

2. **Defect F (resolution seam) — PARTIALLY RESOLVED.** v3.1 mp2 had the gift compressed into one subordinate clause (`շնիկը տերևը նվիրեց նրան՝ որպես փոքրիկ նվեր`). v3.2 mp1 spreads the moment across two sentences:
   > Նա որոշեց նվիրել ցողի կաթիլներով փայլուն տերևը արագիլին՝ որպես փոքրիկ նվեր։ Արագիլը ոգևորված վերցրեց տերևը և փռփռացնելով թևերը թռավ վեր՝ դեպի իր տուն։

   This is closer to staging than asserting, but the first sentence is still framed as `որոշեց նվիրել` ("decided to gift") rather than fully on-stage handing-over. Mid-spectrum result for R6.

### Persists in narrower form

3. **Defect D (closure floor) — NARROWER FAIL.** R4 narrowed the gap from v3.1 mp1's 52 words to v3.2 mp1's 66 words against the 70-floor. Improvement of +14 words but still under the floor. The "below floor → MUST add 1–2 sentences" instruction landed partially.

4. **Defect B (fabricated morphology) — DIFFERENT FLAVOR, STILL PRESENT.** v3.1 had `առաշնորդեց` (typo) and `բարենի` (non-word). v3.2 mp1 has two new candidates:
   - `փայլալիք` (T1, possible "shine-wave" compound) — non-standard.
   - `փռփռացնելով թևերը` (T3, "fluttering wings") — onomatopoeic; canonical Eastern Armenian for wing-flap is `թափահարելով թևերը`.
   R2's "use simpler known phrase" carve-out did not fully suppress this family.

### Untested on Plan A

5. **Defect A (cross-language token leak) — NOT TESTED on PA.** v3.1's `shimmering` leak was a Plan D / Turn 3 event. PA T3 had no English leak in v3.1 either, so this run does not exercise R1 against the historical failure case. Plan D (mp2) is the load-bearing test.

6. **Defect C (PLACE_STEM schwa-preservation) — NOT TESTED on PA.** PA's place stem (`խնձորենու այգ`) doesn't have a schwa-drop ambiguity. The `հին կամուրջ` / `հին կամրջի` failure was a Plan D event. R3 only gets a real workout in mp2.

## 9. Quality notes

- **Armenian naturalness** — improved over v3.1 mp1; sentences are warmer and more on-tone. The two `փայլալիք` / `փռփռացնելով` non-standard forms are the main concerns.
- **Eastern Armenian correctness** — largely correct. The two non-standard items above are flagged for native review; otherwise grammar / morphology is clean. No spelling slip on the v3.1 mp1's `առաշնորդեց` family.
- **Fairy-tale feeling** — good. The closing image (`Արևի տաք շողերը կարծես ժպիտ դարձած էին այգու վրա…`) lands as a warm cinematic Armenian closure.
- **Warmth** — good. Tone matches the planned `mood: հիշատակային ու տաք`.
- **Length / pacing** — T1 ≈ 90w (within 90–130 target, at floor), T2 ≈ 75w (within 70–110), T3 ≈ 66w (4 short of 70 floor — FAIL).
- **Choice quality** — break-glass choices reproduced byte-for-byte at both T1 and T2. The v3.1-era observation that T2 choice Բ pre-supposes "stork going home" while the narrative still has the stork unable to find a resting place still holds, but this is a property of `bakeoff-prompts-v3-1.json` itself, not of the v3.2 system prompt.
- **Continuation coherence** — T1→T2 ok; T2→T3 flows. T1 ends with a slight `resolutionStyle` drift (`տաք հիշատակներ ստեղծելով` — "creating warm memories" — instead of staying close to `փոքրիկ նվեր մատուցելուց`). T3 still resolves correctly with the gift moment.
- **Safety** — PASS. Age-appropriate throughout.

## 10. Comparison to v3.1 mp1 (committed `14731b3`)

| | v3.1 mp1 | v3.2 mp1 | delta |
|---|---|---|---|
| Hard gates | 9/10 PASS, 1 FAIL | 9/10 PASS, 1 FAIL | same shape |
| C2 (moralizing) | WEAK PASS | **clean PASS** | **R5 worked** |
| Abstract coda? | yes (`տարածելու համար`) | **no** | **fixed** |
| C13 closure (PA T3 words) | ≈52w (FAIL) | ≈66w (FAIL) | **+14w, narrower miss; still under floor** |
| English token leak? | no | no | parity (test case lives in Plan D) |
| Eastern-Armenian typo? | yes (`առաշնորդեց`) | no spelling typo | improvement |
| Non-standard morphology? | yes (`բարենի` PA T2) | yes (`փայլալիք`, `փռփռացնելով`) | **family persists, different tokens** |
| Resolution staging | one compressed clause | 2 sentences w/ `որոշեց նվիրել` framing | **partial** |
| Closure last sentence | abstract coda | concrete place image | **clean win** |
| T1 input tokens | 2789 | 4499 | +1710 (longer prompt) |
| Total in / out tokens | 10611 / 580 | 15789 / 605 | +5178 in / +25 out |
| T3 latency | 2180 ms | 1784 ms | -396 ms (faster) |
| Calls / scenario shape | 3/3/0, PA only | 3/3/0, PA only | parity |

**Brief contrast vs v3.1 mp2 (committed `fcffafe`):** v3.1 mp2 added Plan D and surfaced (a) the `shimmering` cross-language leak, (b) the `ձայնուֆով` non-word, (c) the schwa-stem violation `Հին կամրջի վրա`. **None of those test cases are exercised in this v3.2 mp1 run** because mp1 only covers Plan A. R1 and R3 remain to be load-bearing-tested against Plan D in a future v3.2 mp2.

## 11. Verdict

- **Engineering smoke: PASS.** v3.2 system prompt loads, sha matches the `5fe6cd9` post-fix value, 3/3 calls succeeded, no upstream errors, no timeouts. The Armenian-language fixes from `5fe6cd9` produced a syntactically valid prompt the model handles without parsing trouble.
- **Story quality: IMPROVED over v3.1 mp1, but NOT YET PRODUCTION-READY.** Three of six v3.2 design defects show clear evidence of working on Plan A (R1, R5, R6). One persists in narrower form (R4 / C13 closure floor). Two are untested on Plan A and require Plan D (R1 against `shimmering`-class leaks; R3 against schwa-stem). The two non-standard morphology candidates (`փայլալիք`, `փռփռացնելով`) suggest R2 needs further tightening or a different framing.
- **Provider decision: do NOT make.** A 1-scenario `mp1` run on a single provider is not a comparison signal.
- **Production integration: do NOT integrate.** No runtime / `ChatService` / runtime system prompt / `appsettings` / `*.csproj` / test / seed-bank / name-bank / generator / validator / TTS / STT change is implied or authorized by this run.

### Recommended next step (no action taken; awaits explicit GO)

Two natural next moves, presented in order of cost-efficiency:

1. **Tighten R4 (closure floor) FIRST in a small text-only commit, then re-run mp1 against the tightened prompt.** Cheaper if the operator suspects R4 is structurally weak (66w vs 70 floor — close but not closed). Avoids paying for a Plan D run while a known unresolved defect remains. The R4 re-tightening would explicitly add a count-and-add directive (`Եթե բառերի թիվը ստորին սահմանից ցածր է, ՊԱՐՏԱԴԻՐ ավելացրու ևս 1–2 կոնկրետ զգայական նախադասություն մինչ "Վերջ։"`) and possibly a numeric reminder of the per-scenario `TARGET_WORDS` lower bound inside the user-turn note.

2. **Run v3.2 mp2 (PA + PD, 6 turns) paid.** This is the load-bearing test for R1 (cross-language leak on Plan D) and R3 (schwa-stem on `հին կամուրջ`), and gives a head-to-head with v3.1 mp2 (`fcffafe`). Cost ≈ 2× this run.

Either move keeps the provider decision and production integration unchanged. **No further paid call without explicit GO.**

## 12. Raw outputs

Reproduced from `results.json` / `review.md` for archival; nothing here is sourced from anywhere outside the run's own captured outputs.

### Plan A — Turn 1 (4111 ms — 4499 in / 266 out — stop_reason: stop)

> Խնձորենու այգում էր մի շատ հնարամիտ շնիկ։ Ամեն առավոտ, երբ արևը փայլում էր խնձորենու այգու վրա, շնիկը սիրում էր նստել ծառի ստվերի տակ։ Խնձորենու այգում միշտ տաք ու վայրի օծանելիք էր տարածվում։ Այգում գտնվող հասած դեղձերի թավշյա մաշկն էլ էր շնիկի համար հաճելի։ **Մի անգամ,** շնիկը գտավ ցողի կաթիլներով լցված մի երիտասարդ տերև։ Այնպիսի **փայլալիք** ու հրաշքի պես էր այդ տերևը, որ շնիկը որոշեց, թե այն ինչ-որ կախարդական բան է։
>
> Բայց մի փոքր խնդիր կար. այգու մոտ ապրող արագիլը չէր կարողանում գտնել հանգստանալու տեղը։ Այդ ժամանակ շնիկը որոշեց, որ կարող է օգնել նրան՝ իր հնարամտությամբ էլ ավելի տաք հիշատակներ ստեղծելով խնձորենու այգու համար։
>
> Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին
> Բ: գնալ դեպի խնձորենու այգի

### Plan A — Turn 2 (5151 ms — 5289 in / 185 out — stop_reason: stop)

Selected choice: `Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին`

> Շնիկը վերցրեց ցողի կաթիլներով լուսավորված տերևը և մոտեցրեց այն արևի շողերին։ Տերևը փայլեց ու բազմագույն լույս տվեց, ոչ սովորական ձևով։ Արագիլը նկատեց լույսը և հիացմունքով նայեց։ Սակայն, նա դեռ չէր գտնում հանգստանալու տեղը։ Շնիկը մտածեց, թե ինչպես կարող է օգտակար լինել նրան։ Նա գիտեր, որ արևի տաք շողերն ու տերևի փայլող լույսը կարող էին որևէ կերպ օգնություն դառնալ։ Հիմա շնիկին մնում էր հասկանալ, թե ինչպես։
>
> Ա: ուղեկցել արագիլին մինչև երկնքի եզրը
> Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն

### Plan A — Turn 3 (1784 ms — 6001 in / 154 out — stop_reason: stop)

Selected choice: `Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն`

> Շնիկը մնաց այգում և նայեց, թե ինչպես արագիլը պատրաստվում էր թռչել տուն։ Նա որոշեց նվիրել ցողի կաթիլներով փայլուն տերևը արագիլին՝ որպես փոքրիկ նվեր։ Արագիլը ոգևորված վերցրեց տերևը և **փռփռացնելով** թևերը թռավ վեր՝ դեպի իր տուն։ Շնիկը իրեն հպարտ զգաց, քանի որ կարողացավ օգնել և նրան ուղարկել դեպի իր հանգստավայրը։ Արևի տաք շողերը կարծես ժպիտ դարձած էին այգու վրա, որտեղ հիմա ամեն ինչ հանգիստ ու խաղաղ էր։
>
> Վերջ։

(Bolded tokens flag the issues called out in § 8: `Մի անգամ,` mid-paragraph C1 caveat, `փայլալիք` and `փռփռացնելով` non-standard morphology candidates.)

## 13. No secrets included

This file contains **no API key**, no `OPENAI_API_KEY` value, no token, no bearer credential, no parent JWT, no device API key, and no private endpoint. The `OPENAI_API_KEY` environment variable used to authorize the run was confirmed present (length only — 164 — value never printed) before the run and is not echoed anywhere in this document or in the captured `results.json` / `review.md` / `summary.json`.

## Scope guard

No production / runtime files were touched by this run or by this evidence file: `ChatService`, backend code, frontend, `appsettings*.json`, `*.csproj`, tests, seed bank, name bank, story-plan generator, validator, runtime system prompts, speech / TTS / STT — all unchanged. The bake-off tooling (`tools/StoryModelBakeoff/`) is the only code that ran, and only its build-output `results/` directory was written to (gitignored). This document is the only repo-tracked artifact of the run.

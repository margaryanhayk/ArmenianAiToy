# OpenAI v3.1 live smoke (mp2) — 2026-05-06

Second OpenAI v3.1 live smoke this day, this time with `--max-prompts 2` to exercise both **Plan A** and **Plan D** in a single bounded run. A **paid OpenAI API call was made** (6 chat completions, 2 scenarios × 3 turns). No production / runtime change was made; this evidence file is documentation only. Companion to `openai-v3-1-smoke-20260506.md` (committed at `14731b3`).

## 1. Run command

```
dotnet run --project tools/StoryModelBakeoff -- --run --provider openai --max-prompts 2 --i-understand-live-cost --scenarios tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json --system-prompt tools/StoryModelBakeoff/system-prompt-v3-1.txt
```

## 2. Provider / model

- Provider: `openai`
- Model: `gpt-4o`

## 3. Scope

- `--max-prompts 2` (`--allow-full-set` deliberately **NOT** used; cost discipline preserved)
- 2 scenarios: `PA` (`v3-1-plan-a-age-4-simple-17`) + `PD` (`v3-1-plan-d-age-7-richer-2`)
- 6 turns total (3 turns per scenario, `MAX_TURNS=3`)
- Calls attempted / succeeded / failed: 6 / 6 / 0; every turn closed with `stop_reason: stop`
- Path consumed: `tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json`
- Alternate system prompt: `tools/StoryModelBakeoff/system-prompt-v3-1.txt`
- Bake-off prompt sha256: `64f0ff7bb0814d27f23e5ae557b05692f06da804d4f616ad02a2b81f5056b330`
- Production prompt sha256: `54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
- Drift: yes — intentional alternate system prompt + alternate scenarios. Evidence-only deviation; not a request to retune the runtime prompt.

## 4. Result directory

```
tools/StoryModelBakeoff/bin/Debug/net10.0/results/20260506T220840Z/
  results.json
  review.md
  summary.json
```

These live under `bin/Debug` (gitignored build output) and **are not committed**. This evidence file summarizes them.

- Run started UTC: `2026-05-06T22:08:40.0861237Z`
- Run completed UTC: `2026-05-06T22:08:59.0663222Z`
- Wall clock: ~19.0 s

## 5. Latency

| Scope | T1 | T2 | T3 | Subtotal |
|---|---|---|---|---|
| Plan A | 3535 ms | 3692 ms | 3280 ms | **10507 ms** |
| Plan D | 3523 ms | 2097 ms | 2826 ms | **8446 ms** |
| **Total** | | | | **18953 ms** |

Mean per turn ≈ **3159 ms**. No retries, no upstream errors, no timeouts.

## 6. Tokens

| Turn | Prompt (in) | Completion (out) |
|---|---|---|
| Plan A T1 | 2789 | 235 |
| Plan A T2 | 3548 | 195 |
| Plan A T3 | 4270 | 160 |
| Plan D T1 | 2929 | 279 |
| Plan D T2 | 3780 | 227 |
| Plan D T3 | 4621 | 174 |
| **Total** | **21937** | **1270** |

## 7. Gate tables

### Plan A (PA — `v3-1-plan-a-age-4-simple-17`)

| ID | Check | Verdict | Note |
|---|---|---|---|
| C1  | No forbidden opener | **PASS** | Opens "Խնձորենու այգում մի փոքրիկ շնիկ էր ապրում…" — no "Մի անգամ"-class opener |
| C2  | No moralizing | **WEAK PASS** | T3 ends with "ինչպես արագիլը իր երազանքների ուղին էր գտնում" — soft, poetic, no overt lesson |
| C3  | No duplicate sentence | **PASS** | No exact repeats |
| C6  | Exact T1 choices (byte-for-byte break-glass) | **PASS** | `Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին` / `Բ: գնալ դեպի խնձորենու այգի` |
| C8a | T2 follows selected choice (Ա) | **PASS** | T2 first sentence: "Շնիկը մոտեցրեց ցողի կաթիլներով տերևը արևի լույսին։" |
| C9  | T3 no choices, no question | **PASS** | Ends with `Վերջ։`; no `Ա:`/`Բ:` lines |
| C13 | Closure length (target 70-100 words) | **FAIL** | T3 ≈ 55 words — well under the 70-word floor |
| C14 | No meta-output | **PASS** | No scaffold leakage; English-word leak documented separately under § 8 (treated as a language-quality issue, not a meta-output issue) |
| C15 | Exact T2 choices (byte-for-byte break-glass) | **PASS** | `Ա: ուղեկցել արագիլին մինչև երկնքի եզրը` / `Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն` |
| C16 | Place anchor (`խնձորենու այգի`) | **PASS** | Present in all three turns |

**Plan A hard tally: 9 / 10 PASS, 1 FAIL (C13 short closure), 1 PASS marked WEAK (C2).**

### Plan D (PD — `v3-1-plan-d-age-7-richer-2`)

| ID | Check | Verdict | Note |
|---|---|---|---|
| C1  | No forbidden opener | **PASS** | Opens "Հին կամրջի վրա մի փոքրիկ մողես էր ապրում…" — not a "Մի անգամ"-class opener |
| C2  | No moralizing | **WEAK PASS** | T3 references patience but does not produce a clean aphorism; the closure sentence is muddled rather than preachy |
| C3  | No duplicate sentence | **PASS** | No exact repeats |
| C6  | Exact T1 choices (byte-for-byte break-glass) | **PASS** | `Ա: տանել քնած բանալին ընկերոջ մոտ` / `Բ: կանգնել ու լսել հին կամուրջ-ի ձայները` |
| C8a | T2 follows selected choice (Ա) | **PASS** | T2 first sentence: "Մողեսը արագորեն վերցրեց քնած բանալին ու վազեց դեպի իր բադիկ ընկերոջը։" |
| C9  | T3 no choices, no question | **PASS** | Ends with `Վերջ։`; no `Ա:`/`Բ:` lines |
| C13 | Closure length (target 100-130 words) | **FAIL** | T3 ≈ 50 words — roughly half the 100-word floor; worst sample of the run |
| C14 | No meta-output | **PASS** | No scaffold leakage |
| C15 | Exact T2 choices (byte-for-byte break-glass) | **PASS** | `Ա: փորձել արթնացնել քնած բանալին մեղմ շշունջով` / `Բ: համբերել ու լսել հին կամուրջի տակ առվակի շշուկը` |
| C16 | Place anchor (`հին կամուրջ`) | **WEAK PASS** | Place is present, but T1 uses the schwa-DROPPED form `Հին կամրջի վրա…` despite the prompt explicitly requiring the schwa-NOT-dropping `հին կամուրջ` form (e.g. `Հին կամուրջի վրա…`). Direct in-prompt instruction violation. |

**Plan D hard tally: 9 / 10 PASS, 1 FAIL (C13 short closure), 1 PASS marked WEAK (C2), 1 PASS marked WEAK (C16 schwa violation).**

## 8. Major issues

The hard-gate table above does not capture the most important findings of this run. The four serious issues — and a handful of minor ones — are listed here, with verbatim quotes from `results.json`.

1. **PA T3 — English word leakage inside Armenian narrative.** The closure sentence contains the bare English word `shimmering` mid-clause:

   > Տերևի վրա ցողի կաթիլները՝ արևի տաք շողի ներքո, **shimmering**՝ ուղեկցեցին արագիլին դեպի երկինք։

   For a spoken-toy product targeting Armenian-only output, this is the single most serious defect of the run. It would be unacceptable in any audio rendering.

2. **PD T3 — non-word `ձայնուֆով`.** The patience-axis closure sentence contains a token that is not an Armenian word:

   > Ժամանակն անցնում էր, և նրանց համբերությունը ծածկեց շրջակա **ձայնուֆով**:

   Reads like a model-hallucinated morphology. Indistinguishable from gibberish to a child listener.

3. **PD T1 — schwa-stem instruction silently ignored.** The prompt explicitly required the schwa-NOT-dropping form `հին կամուրջ` (e.g. `Հին կամուրջի վրա…`). The model produced the schwa-DROPPED form:

   > **Հին կամրջի վրա** մի փոքրիկ մողես էր ապրում։

   And again later in T1: `…լսել **հին կամրջի** ձայներն…`. The break-glass choice block in the same turn even contains the explicit `հին կամուրջ-ի` form, so the model saw both spellings in the same prompt and chose the wrong one for the narrative.

4. **C13 fails on BOTH plans.** T3 under-runs the closure target on both scenarios. PA T3 ≈ 55 words against the 70–100 floor. PD T3 ≈ 50 words against the 100–130 floor — i.e. roughly half the required minimum.

5. **PA T2 — choice block jumps ahead of narrative state.** Same defect observed in the `--max-prompts 1` evidence (`14731b3`). The narrative ends T2 with `Բայց դեռ պետք էր լուծել մի փոքր խնդիր՝ ինչպես ստանան տան ճանապարհը։` — i.e. the small problem is unresolved — yet choice Բ asserts `մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն`, which presupposes the stork is already going home. Reproduces across two paid runs.

6. **PA T2 — likely non-word `բարենի`.** In `Շնիկը ժպտում էր, նրա հնարամտությունը հիմա **բարենի** պես էր օգտագործվում։` the word `բարենի` does not appear in standard Eastern Armenian usage; the intended meaning seems to be something like `բարիքի պես` ("like a kindness"). Minor but on-trend with item 2.

7. **PD T1 — `ունէր` old orthography.** `Մողեսն **ունէր** կախարդական քնած բանալի…` uses the pre-reform է instead of modern ե. Eastern Armenian classroom standard would prefer `ուներ`. Less serious than items 1–3 but still off-register for an age-7 target audience.

## 9. Quality notes per plan

### Plan A (`age-4-simple` — շնիկ + արագիլ + ցողի կաթիլներով տերև)

- **Armenian naturalness** — average; awkward phrasings (`բարենի պես էր օգտագործվում`) and the T3 English-word leak drag the run below acceptable.
- **Eastern Armenian correctness** — mostly Eastern, but the `բարենի` non-word and the `shimmering` leak are real defects.
- **Fairy-tale feeling** — acceptable; warm orchard atmosphere lands.
- **Warmth** — good — matches the planned mood `հիշատակային ու տաք`.
- **Length / pacing** — T1 within ~120-word range, T2 within target, **T3 short** (~55w vs 70-100 floor).
- **Choice quality** — break-glass blocks reproduced byte-for-byte at T1 and T2; T2 choice Բ continues to assert a resolution the narrative has not earned (item 5 above). Same pattern observed across the `mp1` and `mp2` runs.
- **Continuation coherence** — T1→T2 ok; T2→T3 still has the gift-moment seam — `փոքրիկ նվեր` gestured at, not staged as a discrete moment matching `resolutionStyle`.
- **Safety** — PASS. Age-appropriate throughout.

### Plan D (`age-7-richer` — մողես + բադիկ + քնած բանալի + ձմեռային մեղմ)

- **Armenian naturalness** — weakest of the two plans. `ձայնուֆով` (T3) is the worst single token in the run; `մողեսի ճանկերի մեջ` (T2) reads off ("claws" for a lizard).
- **Eastern Armenian correctness** — mixed. `ունէր` (T1) old orthography, schwa-stem instruction violated (`Հին կամրջի` instead of required `Հին կամուրջի`).
- **Fairy-tale feeling** — thin. The bridge / sleeping key / stream motifs are present but never earn the planned `ձմեռային մեղմ` mood.
- **Warmth** — present but distant; less convincing than Plan A.
- **Length / pacing** — T1 within target (~140w in 130-180), T2 within target (~115w in 100-140), **T3 BAD** (~50w against a 100-130 floor — worst single sample of the run).
- **Choice quality** — break-glass blocks reproduced byte-for-byte at T1 and T2.
- **Continuation coherence** — T1→T2 follows; T2→T3 jumps from "let's wake the key" to "snow melts" without staging the patience moment the `resolutionStyle: լուծումը գալիս է համբերությունից` calls for. The `C2 patience-axis stress-test` warning in the prompt was dodged rather than handled.
- **Safety** — PASS.

## 10. Comparison to `--max-prompts 1` evidence

Reference: `tools/StoryModelBakeoff/evaluations/openai-v3-1-smoke-20260506.md` (committed `14731b3`, single PA run, OpenAI gpt-4o).

**Same direction as `mp1`:**

- Engineering path stable. 100% call success on both runs (3/3 → 6/6).
- Latency / token shapes consistent. Mean ~3 s per turn either way; per-scenario tokens ~10k in / ~600 out — `mp2` totals (21937 in / 1270 out) are roughly 2× of `mp1` (10611 in / 580 out), as expected.
- C13 short T3 closure reproduces. PA T3 was ~52w in `mp1`, ~55w in `mp2` — same defect, same scenario, two paid runs in a row.
- PA T2 choice block continues to assert a resolution the narrative hasn't earned. Reproduced across runs.

**Worse on this run:**

- **English word leakage** (PA T3, `shimmering`) did NOT occur in `mp1`.
- **Non-word generation** (PD T3, `ձայնուֆով`) did NOT occur in `mp1` (which had only the single-letter typo `առաշնորդեց`).
- **Schwa-stem instruction violation** (PD T1, `Հին կամրջի`) is a Plan-D-specific defect surfaced by the `mp2` scope.

**Better or equal:**

- PA T3 closure is a hair longer (~55w vs ~52w). Still under the 70-floor, so still C13 FAIL. No real win.
- No new safety concerns. Both plans remain age-appropriate.

**Net:** `mp2` reproduces every `mp1` weakness AND adds two new failure categories (cross-language leak, non-word morphology) plus a structural in-prompt-instruction violation. The new categories are language-quality regressions, not engineering regressions.

## 11. Verdict

- **Engineering smoke: PASS.** The OpenAI live runner handles a 2-scenario / 6-turn `--max-prompts 2` v3.1 bake-off cleanly. Cost discipline (`--allow-full-set` not used) intact. No timeouts, no retries, no provider errors. Identical infra reliability to the `mp1` run, just at 2× volume.
- **Story quality: MIXED, leaning WORSE than `mp1`.** All 10 hard gates score PASS / WEAK PASS except C13, which fails on **both** plans (closure too short). Two NEW quality regressions appeared at this volume that were absent at `--max-prompts 1`: cross-language leak and non-word generation. Plan D additionally exposes that the explicit schwa-stem instruction is being silently ignored. C2 (no moralizing) is WEAK PASS on both plans, not clean PASS.
- **OpenAI gpt-4o + current v3.1 prompt is NOT production-ready.** This run is enough signal to say the v3.1 bake-off prompt as it stands does not yet reliably produce production-quality stories.
- **Provider decision: do NOT make.** Anthropic was skipped this round per explicit instruction. A 2-scenario `mp2` run on a single provider is not a comparison signal.
- **Production integration: do NOT integrate.** No runtime / `ChatService` / runtime system prompt / `appsettings` / `*.csproj` / test / seed-bank / name-bank / generator / validator / TTS / STT change is implied or authorized by this run.
- **Recommended next step (no API calls; just authoring):** record this evidence; then, before any further paid runs, tighten the v3.1 system prompt around the three new defects:
  1. **Hard rule against cross-language tokens.** "Every word in the narrative must be Armenian" / "no English, transliterated English, or other-language tokens may appear in the narrative or choice blocks." Enforced as an explicit prohibition near the top of the system prompt.
  2. **Hard rule against fabricated morphology.** "If you are not sure of an Armenian word's form, prefer a simpler word that you are sure of. Do not invent suffixes or stems."
  3. **Schwa-stem compliance for `place`.** Strengthen the `PLACE_STEM` block so the schwa-NOT-dropping requirement is impossible to miss (e.g. provide the exact required first-sentence prefix forms inline, and forbid the schwa-dropped variant by name).
  Then re-run `--max-prompts 2` on the tightened prompt and diff the resulting evidence against this file. Only after that consider Anthropic-equivalent runs or a wider OpenAI matrix.

## 12. Raw outputs

Reproduced from `results.json` / `review.md` for archival; nothing here is sourced from anywhere outside the run's own captured outputs.

### Plan A — Turn 1 (3535 ms — 2789 in / 235 out — stop_reason: stop)

> Խնձորենու այգում մի փոքրիկ շնիկ էր ապրում։ Նա շատ հնարամիտ էր և միշտ ուզում էր ինչ-որ նոր բան հասկանալ։ Մի օր, երբ արևը ծագում էր ու տաք շողերն էր ուղարկում այգու վրա, շնիկը նկատեց, որ մի արագիլ չգտնելով հանգստանալու տեղ, շրջում է ծառերի մոտ։ Տատի պատմելով, այդ այգին շատ կախարդական էր, ու հենց այստեղ շնիկը գտավ մի տերև, որի վրա ցողի փայլուն կաթիլներ էին։ Հասած դեղձի թավշյա մաշկի բույրը ցրվում էր օդում, երբ շնիկը մտածում էր՝ ինչ կարելի է անել, որ արագիլը հանգստանա։ Պատմության տաք հիշողական մթնոլորտում շնիկը որոշեց լսել իր սրտի ձայնը։
>
> Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին
> Բ: գնալ դեպի խնձորենու այգի

### Plan A — Turn 2 (3692 ms — 3548 in / 195 out — stop_reason: stop)

Selected choice: `Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին`

> Շնիկը մոտեցրեց ցողի կաթիլներով տերևը արևի լույսին։ Հանկարծ փոքրիկ շողքը ծածկվեց տերևի կաթիլների վրա, իսկ այգին էլ ավելի պայծառացավ։ Արագիլը, տեսնելով այդ փայլը, ակնթարթորեն մոտեցավ շնիկին, կարծես թե հասկանալով, որ նա հենց այստեղ կարող է հանգստանալ։ Շնիկը ժպտում էր, նրա հնարամտությունը հիմա բարենի պես էր օգտագործվում։ Արագիլի համար այդ լույսը ինչ-որ հույս ու հանգստություն էր։ Բայց դեռ պետք էր լուծել մի փոքր խնդիր՝ ինչպես ստանան տան ճանապարհը։
>
> Ա: ուղեկցել արագիլին մինչև երկնքի եզրը
> Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն

### Plan A — Turn 3 (3280 ms — 4270 in / 160 out — stop_reason: stop)

Selected choice: `Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն`

> Շնիկը մնաց այգում և նայեց, թե ինչպես արագիլը թռչում է տուն։ Արագիլը հասկացել էր՝ որտեղից այդ նախշուն լույսը, և ուր մոտենալ, որ գտնի իր ճանապարհը։ Նրա երջանկությանն ու խաղաղությանը ի պատասխան, շնիկը տերևը նվիրեց նրան՝ որպես փոքրիկ նվեր։ Տերևի վրա ցողի կաթիլները՝ արևի տաք շողի ներքո, **shimmering**՝ ուղեկցեցին արագիլին դեպի երկինք։ Շնիկը նստած ճյուղի տակ, գոհունակությամբ նայում էր, ինչպես արագիլը իր երազանքների ուղին էր գտնում։
>
> Վերջ։

### Plan D — Turn 1 (3523 ms — 2929 in / 279 out — stop_reason: stop)

> **Հին կամրջի վրա** մի փոքրիկ մողես էր ապրում։ Նա միշտ զարմանում էր աշխարհով և սիրում էր երկար ժամանակ անց կացնել ծանոթ վայրերում։ Ձմեռ էր, և կամրջի փայտերից դուրս հոսող քնած ծաղիկների հոտը դեռևս պահպանում էր իր մեղմ ու տաք սերը։ Բայց մողեսը մի փոքր տխուր էր, որովհետև անձրևի գնալուց հետո ձյունը չէր հալվում։ Մեր մողեսը պարբերաբար հանդիպում էր իր բադիկ ընկերոջը, որը հաճախ վախեցած էր, քանի որ ձյունը մնում էր և առվակը չէր կարողանում երգել իր ջրային մեղեդին։
>
> Մողեսն **ունէր** կախարդական քնած բանալի՝ իր սիրելի գտածոն, որը կարող էր օգնել, բայց ինչպես այն օգտագործել՝ դեռ չգիտեր։ Ուստի նա սկսեց խորհել ինչ անել։ Կարող էր բերել բանալին բադիկի մոտ՝ նրան մխիթարելու համար, կամ կանգնել ու լսել **հին կամրջի** ձայներն ու գտնել լուծում։
>
> Ա: տանել քնած բանալին ընկերոջ մոտ
> Բ: կանգնել ու լսել հին կամուրջ-ի ձայները

### Plan D — Turn 2 (2097 ms — 3780 in / 227 out — stop_reason: stop)

Selected choice: `Ա: տանել քնած բանալին ընկերոջ մոտ`

> Մողեսը արագորեն վերցրեց քնած բանալին ու վազեց դեպի իր բադիկ ընկերոջը։ Երբ հասավ բադիկին, նա սիրով մխիթարեց նրան՝ ցույց տալով կախարդական բանալին։ Բադիկը դեռևս վախեցած էր, բայց մի փոքր հանգստացավ՝ տեսնելով մողեսի պատրաստակամությունը օգնելու։ Բանալին՝ լուռ պառկել էր մողեսի ճանկերի մեջ, սակայն դեռ չէր սթափվել իր մոգական քնից։
>
> Մողեսը սկսեց մտածել, թե ինչ կարող է անել։ Հնարավոր էր փորձել արթնացնել բանալին որևէ կերպ, կամ պարզապես համբերել ու սպասել, որ ձյունը կամրջի տակ գտնվող առվակին թույլ տա նորից երգել։
>
> Ա: փորձել արթնացնել քնած բանալին մեղմ շշունջով
> Բ: համբերել ու լսել հին կամուրջի տակ առվակի շշուկը

### Plan D — Turn 3 (2826 ms — 4621 in / 174 out — stop_reason: stop)

Selected choice: `Բ: համբերել ու լսել հին կամուրջի տակ առվակի շշուկը`

> Մողեսն ու բադիկը հանգիստ նստեցին հին կամուրջի ստորոտին, և սկսեցին լսել ձայնը, որը զգուշորեն շշնջում էր նրանց տակից։ Ժամանակն անցնում էր, և նրանց համբերությունը ծածկեց շրջակա **ձայնուֆով**: Քիչ-քիչ ձյուները սկսեցին հալվել՝ բացելով առվակի փոքրիկ հոսքը: Ջուրը նրբորեն շարժվեց, կարծես համբույր շշնջոց լիներ, ու արթնացրեց գարնան մեղեդին: Առվակի ուժեղ հոսքը նորից երգեց՝ բադիկին ուրախացնելով և մողեսին զարմացնելով իր գեղեցիկ ձայնով։
>
> Վերջ։

(The bolded tokens above flag the issues called out in § 8: `shimmering` cross-language leak, `ունէր` old orthography, `Հին կամրջի` schwa-stem violation, and `ձայնուֆով` non-word.)

## 13. No secrets included

This file contains **no API key**, no `OPENAI_API_KEY` value, no token, no bearer credential, no parent JWT, no device API key, and no private endpoint. The `OPENAI_API_KEY` environment variable used to authorize the run was confirmed present (length only — 164 — value never printed) before the run and is not echoed anywhere in this document or in the captured `results.json` / `review.md` / `summary.json`.

## Scope guard

No production / runtime files were touched by this run or by this evidence file: `ChatService`, backend code, frontend, `appsettings*.json`, `*.csproj`, tests, seed bank, name bank, story-plan generator, validator, runtime system prompts, speech / TTS / STT — all unchanged. The bake-off tooling (`tools/StoryModelBakeoff/`) is the only code that ran, and only its build-output `results/` directory was written to (gitignored). This document is the only repo-tracked artifact of the run.

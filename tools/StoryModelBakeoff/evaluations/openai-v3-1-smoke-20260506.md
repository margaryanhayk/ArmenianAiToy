# OpenAI v3.1 live smoke — 2026-05-06

First **OpenAI live smoke** run of the v3.1 bake-off scenarios with `--max-prompts 1`. A **paid OpenAI API call was made** (3 chat completions, 1 scenario × 3 turns). No production / runtime change was made; this evidence file is documentation only.

## 1. Run command

```
dotnet run --project tools/StoryModelBakeoff -- --run --provider openai --max-prompts 1 --i-understand-live-cost --scenarios tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json --system-prompt tools/StoryModelBakeoff/system-prompt-v3-1.txt
```

## 2. Provider / model

- Provider: `openai`
- Model: `gpt-4o`

## 3. Scenario

- Scenarios: 1
- Scenario id: `PA`
- Category: `v3-1-plan-a-age-4-simple-17`
- Turns per scenario: 3 (`MAX_TURNS=3`)
- Path consumed: `tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json`
- Alternate system prompt: `tools/StoryModelBakeoff/system-prompt-v3-1.txt`
- Bake-off prompt sha256: `64f0ff7bb0814d27f23e5ae557b05692f06da804d4f616ad02a2b81f5056b330`
- Production prompt sha256: `54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
- Drift: yes — intentional alternate system prompt + alternate scenarios. Documented as an evidence-only deviation; not a request to retune the runtime prompt.

## 4. Result directory

```
tools/StoryModelBakeoff/bin/Debug/net10.0/results/20260506T214431Z/
  results.json
  review.md
  summary.json
```

These live under `bin/Debug` (gitignored build output) and **are not committed**. This evidence file summarizes them.

- Run started UTC: `2026-05-06T21:44:31.6867536Z`
- Run completed UTC: `2026-05-06T21:44:39.8067380Z`

## 5. Latency per turn

| Turn | Latency |
|---|---|
| T1 | 4051 ms |
| T2 | 1859 ms |
| T3 | 2180 ms |
| **Total** | **8090 ms** |

## 6. Tokens

| Turn | Prompt (in) | Completion (out) |
|---|---|---|
| T1 | 2789 | 235 |
| T2 | 3548 | 199 |
| T3 | 4274 | 146 |
| **Total** | **10611** | **580** |

All three turns terminated with `stop_reason: stop`. Calls attempted/succeeded/failed: 3 / 3 / 0.

## 7. Gate table

| ID | Check | Verdict | Note |
|---|---|---|---|
| C1  | No forbidden opener | **PASS** | Opens "Խնձորենու այգին ուշ երեկոյան…" — no "Մի անգամ"-class opener |
| C2  | No moralizing | **WEAK PASS** | T3 tail "տաքությունն ու բարությունը տարածելու համար" reads as a soft moralizing coda; not a heavy lesson, but not strictly clean |
| C3  | No duplicate sentence | **PASS** | No exact repeats; the two "աստղի նման" comparisons in T2 are distinct sentences |
| C6  | Exact T1 choices (byte-for-byte break-glass) | **PASS** | `Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին` / `Բ: գնալ դեպի խնձորենու այգի` |
| C8a | T2 follows selected choice (Ա) | **PASS** | First sentence: "Շնիկը մոտեցրեց ցողի կաթիլներով տերևը լույսին…" |
| C9  | T3 no choices, no question | **PASS** | Ends with `Վերջ։`; no `Ա:`/`Բ:` lines |
| C13 | Closure length (target 70-100 words) | **FAIL (minor)** | T3 body is ~52 Armenian words — ~20 words below the 70-word floor |
| C14 | No meta-output | **PASS** | Clean Armenian narrative; no scaffolding leakage |
| C15 | Exact T2 choices (byte-for-byte break-glass) | **PASS** | `Ա: ուղեկցել արագիլին մինչև երկնքի եզրը` / `Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն` |
| C16 | Place anchor (`խնձորենու այգի`) | **PASS** | Present in all three turns |

**Hard-criteria summary: 9 / 10 PASS, 1 FAIL (C13 short closure), 1 PASS marked WEAK (C2 moralizing tail).**

## 8. Quality notes

- **Armenian naturalness** — acceptable, but awkward in places. "հաստ արագիլ" ("thick stork") is an unusual collocation; "ինչ-որ կերպ" is fine but its sentence is loose.
- **Eastern Armenian correctness** — generally Eastern. **One real spelling slip in T3:** `առաշնորդեց` should be `առաջնորդեց`. Worth flagging.
- **Fairy-tale feeling** — decent atmosphere (twilight orchard, glowing dew-leaf, stork seeking rest). Lands as a fairy tale, not as instruction.
- **Warmth** — good; tone matches the planned `mood: հիշատակային ու տաք`.
- **Length / pacing** — T1 within target (~90-130). T2 within target (~70-110). **T3 is short** (~52 words against 70-100 floor); closure feels rushed.
- **Choice quality** — exact break-glass choices were preserved byte-for-byte at both T1 and T2. However, T2's choice Բ ("մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն") jumps ahead of the T2 narrative, which still has the stork unable to find a resting place. Choice block is asserting a resolution the narrative has not yet earned.
- **Continuation coherence** — T1→T2 OK. T2→T3 has a resolution seam: small-problem was "stork can't find a resting place"; T3 resolves with "stork flies home" rather than rests. The plan's `resolutionStyle: լուծումը գալիս է փոքրիկ նվեր մատուցելուց` is gestured at via "փոքրիկ նվերը օգնեց արագիլին" but never staged as a discrete gift moment.
- **Safety** — pass. No frightening, adult, or otherwise unsafe content; age-appropriate throughout.

## 9. Verdict

- **Engineering smoke: PASS.** The OpenAI live execution path works end-to-end on the v3.1 scenarios + v3.1 system prompt: 3 / 3 calls succeeded, results / review / summary files written, totals consistent with per-turn rows.
- **Story quality: MIXED.** Most hard gates pass and the story is age-appropriate and on-tone; but C13 (short closure) is a real miss, C2 (moralizing tail) and the T2-choice/narrative seam are real concerns, and there is one Eastern Armenian spelling slip.
- **Provider decision: do NOT make.** A single PA scenario with `--max-prompts 1` is not enough to justify swapping or sticking. Need at minimum the Claude equivalent smoke and / or a wider OpenAI matrix (Plan-D / age-5+ / safety-edge scenarios) before treating this as a comparison signal.
- **Production integration: do NOT integrate.** No runtime / `ChatService` / system-prompt / `appsettings` / `csproj` / test / seed-bank / name-bank / generator / validator / TTS / STT change is implied or authorized by this run.
- **Next step (later, separate request):** run the matched Claude `--max-prompts 1` smoke against the same `bakeoff-prompts-v3-1.json` + `system-prompt-v3-1.txt`, capture as a sibling evidence file, and / or run the full OpenAI matrix.

## 10. Raw outputs

Reproduced from `results.json` / `review.md` for archival; nothing here is sourced from anywhere outside the run's own captured outputs.

### Turn 1 (4051 ms — 2789 in / 235 out — stop_reason: stop)

> Խնձորենու այգին ուշ երեկոյան խաղաղ էր ու գեղեցիկ։ Այգու կենտրոնում մի փոքրիկ հնարամիտ շնիկ խաղում էր իր մեծ ընկեր շան հետ։ Շունը նրան ուսուցանում էր աշխարհի հին ու հետաքրքիր պատմությունները։ Այդ պահին շնիկը գտնում է մի ցողի կաթիլներով տերև, որն ասես փայլում էր արևի տաք շողերի տակ։ Հայացքը դեպի երկինք բարձրացնելով, շնիկը նկատում է, որ արևն արդեն մայր է մտել, և տաքությունը մեղմացել է։ Սակայն հետաքրքիր բան է պատահում՝ մի մեծ ու հաստ արագիլ, որը երկինքը թռչելով գնում էր, կանգ է առնում այգու ծայրին։ Նա վախենում էր տեղ գտնել հանգստանալու համար։ Շնիկը մտածում է, որ ինչ-որ կերպ պետք է օգնի արագիլին։
>
> Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին
> Բ: գնալ դեպի խնձորենու այգի

### Turn 2 (1859 ms — 3548 in / 199 out — stop_reason: stop)

Selected choice: `Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին`

> Շնիկը մոտեցրեց ցողի կաթիլներով տերևը լույսին, և հենց այդ պահին տերևը սկսեց փայլել ավելի պայծառ։ Լույսը նման էր փոքրիկ աստղի, որը մոգականորեն ստիպեց արագիլին կանգ առնել ու նայել։ Այդ գեղեցիկ տեսարանը հանգստացրեց նրան։ Բայց արագիլը դեռ չէր գտնում իր հանգստանալու տեղը։ Շնիկի մեծ բարեկամ շունը տեսնում էր, որ շնիկը փորձում է օգնել և որոշեց աջակցել նրան։ Հայացքները երկինք ուղղելով, նրանք նկատեցին մի փայլուն բան, կարծես աստղի նման, որը կարող էր ցույց տալ ճանապարհը արագիլին։
>
> Ա: ուղեկցել արագիլին մինչև երկնքի եզրը
> Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն

### Turn 3 (2180 ms — 4274 in / 146 out — stop_reason: stop)

Selected choice: `Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն`

> Շնիկը և շունը մնացին այգում ու նայեցին, թե ինչպես է արագիլը թռչում տուն։ Արագիլը խոնարհեց գլուխը, ասես շնորհակալական խոսքեր արտահայտելով, երբ հանկարծ ցողի կաթիլներից փայլող լույսը առաշնորդեց նրան ճիշտ ճանապարհին։ Արագիլը երջանիկ թռավ երկնքում, գտնելով իր տան ճանապարհը։ Շնիկն ու շունը գոհ էին, որ փոքրիկ նվերը օգնեց արագիլին։ Նրանք հետագայում հաճախ էին հիշում այդ օրը՝ տաքությունն ու բարությունը տարածելու համար։
>
> Վերջ։

## 11. No secrets included

This file contains **no API key**, no `OPENAI_API_KEY` value, no token, no bearer credential, no parent JWT, no device API key, and no private endpoint. The `OPENAI_API_KEY` environment variable used to authorize the run was confirmed present (length only, value never printed) before the run and is not echoed anywhere in this document or in the captured `results.json` / `review.md` / `summary.json`.

## Scope guard

No production / runtime files were touched by this run or by this evidence file: `ChatService`, backend code, frontend, `appsettings*.json`, `*.csproj`, tests, seed bank, name bank, story-plan generator, validator, runtime system prompts, speech / TTS / STT — all unchanged. The bake-off tooling (`tools/StoryModelBakeoff/`) is the only code that ran, and only its build-output `results/` directory was written to (gitignored). This document is the only repo-tracked artifact of the run.

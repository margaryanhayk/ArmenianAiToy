# OpenAI v3.2.3 live smoke (mp1) — 2026-05-10

First **OpenAI live smoke** of the v3.2.3 R2 / opener-rule tightening
(commit `919dee5`). A **paid OpenAI API call was made** (3 chat
completions, 1 scenario × 3 turns). No production / runtime change
was made; this evidence file is documentation only. Companion to:

- v3.2.2 implementation committed at `768be15`
- v3.2.2 mp1 evidence committed at `4649dda`
- v3.2.2 mp2 evidence committed at `fbdc639`
- v3.2.3 design plan committed at `0f6b726`
- v3.2.3 implementation (R2 + opener tightening) committed at `919dee5`
- Claude.app manual sample evaluation committed at `16537e5`

This run is the first paid validation of v3.2.3's two changes:
**(R2)** verb-fabrication tightening — does the strengthened
verb-default rule and expanded named-token list reduce the
participle / coined-stem family that v3.2.2 mp1 surfaced
(`Խտնված`)? **(Opener)** does the opener rule kill not just the
T1 first-sentence `Մի անգամ` opener but also the recurring
`Մի օր,` mid-paragraph caveat?

The filename uses local Yerevan date `20260510` because the run
completed at UTC `2026-05-09T23:27:35Z` = Yerevan `2026-05-10
03:27` (UTC+4).

---

## 1. Run command

```
dotnet run --project tools/StoryModelBakeoff -- --run --provider openai --max-prompts 1 --i-understand-live-cost --scenarios tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json --system-prompt tools/StoryModelBakeoff/system-prompt-v3-2.txt
```

The `OPENAI_API_KEY` env var was loaded into the same PowerShell
process from `dotnet user-secrets` for `backend/src/ArmenianAiToy.Api`
(`OpenAI:ApiKey`) and immediately consumed. The key value never
reached stdout, files, or any tool context — only its length (164)
was printed. No secrets in this file.

## 2. Provider / model

- Provider: `openai`
- Model: `gpt-4o`

## 3. Scope

- `--max-prompts 1` (`--allow-full-set` deliberately **NOT** used).
- 1 scenario: `PA` (`v3-1-plan-a-age-4-simple-17`).
- 3 turns total (`MAX_TURNS=3`).
- Calls attempted / succeeded / failed: 3 / 3 / 0; every turn closed
  with `stop_reason: stop`.
- Path consumed: `tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json`
  (v3.1 scenarios reused unchanged — no v3.2-shaped scenarios exist
  in the repo; the v3.2.3 tightening is prompt-level only).
- Alternate system prompt:
  `tools/StoryModelBakeoff/system-prompt-v3-2.txt` (the v3.2.3
  prompt at `919dee5`).
- Repo HEAD at run time: `16537e5` (in sync with `origin/main`).
- Drift: yes — intentional alternate system prompt + alternate
  scenarios. Evidence-only deviation; not a request to retune the
  runtime prompt.

## 4. Prompt identity

- Bake-off prompt sha256:
  `ec8a9cb7fed3db5b2e34c03f148eb2d5f902df854cd0683d304acbe0bcef4829`
  (post-`919dee5` v3.2.3 — different from v3.2.2's
  `908ae30e610e18389b2151c262149db32830b3f663db70fe98b268e4e06fec2f`).
- Scenarios sha256:
  `e6cdba77d64640c89dc6aa094108f3bff040a26044af1b1cc75f5a7ab0f89b59`
  (unchanged — same `bakeoff-prompts-v3-1.json` across all v3.x runs).
- Production prompt sha256:
  `54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
  (unchanged — production runtime is untouched).
- Drift verdict: `drifted (intentional — alternate system prompt)`.

## 5. Result directory

```
tools/StoryModelBakeoff/bin/Debug/net10.0/results/20260509T232726Z/
  results.json
  review.md
  summary.json
```

These live under `bin/Debug` (gitignored build output) and **are not
committed**. This evidence file summarizes them; raw turn outputs are
preserved in `review.md`.

- Run started UTC: `2026-05-09T23:27:26.6890696Z`
- Run completed UTC: `2026-05-09T23:27:35.6979937Z`
- Wall clock: ~9.0 s

## 6. Latency / tokens

| Turn | Latency | Prompt (in) | Completion (out) |
|---|---|---|---|
| T1 | 3844 ms | 6567 | 287 |
| T2 | 2803 ms | 7377 | 223 |
| T3 | 2321 ms | 8127 | 227 |
| **Total** | **8968 ms** | **22071** | **737** |

Input total ≈ +14 % vs v3.2.2 mp1's 19328 (the v3.2.3 R2 + opener
tightening landed as ~700 added prompt tokens × 3 turns + cumulative
prior-turn echo). Output total ≈ +18 % vs 626 — model produced
slightly longer turns than v3.2.2 mp1 across the board.

Mean per turn ≈ 2989 ms. No retries, no upstream errors, no timeouts.

## 7. Gate table — Plan A (best-effort hard-rule check)

| ID | Verdict | Note |
|---|---|---|
| C1 (opener) | **PASS strict on first sentence; FAIL on mid-paragraph** | T1 opens `Խնձորենու այգում ամեն ինչ խաղաղ էր։` — places-anchored, no `Մի անգամ` ✓. **But T1 sentence 6 starts with `Մի օր, բնավ խումբախումբ խաղալու ժամանակ, շնիկը լսեց...`** — the recurring mid-paragraph `Մի օր,` caveat that v3.2.3's opener rule was meant to suppress is still present. Partial-success on opener tightening. |
| C2 (closure) | **PASS clean** | T3 closes on `Վերջ։` on its own line. No abstract coda. |
| C3 (no-repeat) | **PASS** | No exact-string repeats across turns. |
| C6 (T1 choices) | **PASS** | `Ա: մոտեցնել ցողի կաթիլներով տերևը լույսին` / `Բ: գնալ դեպի խնձորենու այգի` byte-for-byte. |
| C8a (T2 first sentence) | **PASS** | T2 opens `Շնիկը մոտեցրեց ցողի կաթիլներով տերևը դեպի արևի լույսը։` — direct execution of choice Ա with the SELECTED_CHOICE verb `մոտեցրեց` present. |
| C9 (T3 ends) | **PASS** | T3 ends with `Վերջ։` on its own line; no `Ա:`/`Բ:` lines, no question. |
| C13 (T3 length) | **PASS — MID-BAND (best yet)** | **PA T3 ≈ 90 words** counted across 9 sentences. Floor 70 → **+20 above floor**. Ceiling 100 → **-10 below ceiling**. Second consecutive mid-band landing across v3.2-era PA T3 runs (v3.2.2 mp1 = 82w, this run = 90w). |
| C14 (no narrator parenthetical) | **PASS clean** | No `Continued`/`Note:`/`Շարունակեց`/parenthetical narrator. |
| C15 (T2 choices) | **PASS** | `Ա: ուղեկցել արագիլին մինչև երկնքի եզրը` / `Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն` byte-for-byte. |
| C16 (place stem preserved) | **PASS** | `Խնձորենու այգում` in T1 first sentence; PLACE_STEM letters preserved across all T1 mentions (`այգում` / `այգու ծառերի տերևներին` / `այգի` / `այգում`). |

**Hard tally: 9 / 10 PASS (10 / 10 if you count the opener as
"first-sentence PASS, mid-paragraph FAIL" as PASS).** Opener-rule
mid-paragraph leak is the one regression-flag this run surfaces.

## 8. R2 verdict — partial success again, new fabrication family

The v3.2.3 R2 strengthening shipped at `919dee5` extended the
named-token list and tightened the verb-default discipline beyond
v3.2.2's exemplar set.

### Targeted families — held

The four `d9c36ca` PD T3 fabrications (`խորոտալ`, `համբարձլ`,
`փափախերները`, `պարգևիր`) and v3.2.2 mp1's `Խտնված խնձորենին`
fabrication did **NOT recur** on this PA run.

### New fabrications — slipped through (multiple)

This run produced **four borderline / non-standard tokens** that
the R2 named-exemplar approach did not catch:

- **T1**: `բոցերում էր` — non-standard verb form. Standard
  Eastern Armenian uses `բոցավառվել` / `բոցավառել` for "to be
  aflame / to burn." `բոցել` does not exist as a recognized stem
  — the model appears to have coined a verb from the noun `բոց`
  ("flame"). Borderline fabrication.
- **T1**: `փայլացնում էին` — non-standard conjugation. The
  recognized causative is `փայլեցնում` ("making shine"). The form
  `փայլացնում` is a coined causative on a near-stem. Borderline.
- **T1**: `բնավ խումբախումբ խաղալու ժամանակ` — semantically
  incoherent. `բնավ` means "never / at all" (a negation
  intensifier); `խումբախումբ` means "in groups." A single dog
  playing "in groups, ever" is not a meaningful Armenian phrase.
  Likely a model-confusion of register.
- **T2**: `կախարդական ցուցանի` — `ցուցան` is not a standard
  Eastern Armenian noun. Likely a coined / mis-selected noun;
  the intended sense is unclear (sign? display? omen?). The
  surrounding sentence does not disambiguate. Borderline
  fabrication.
- **T3**: `անթել ու լուսավոր էր` — `անթել` is non-standard;
  the recognized form for "ember / glow" is `անթեղ`. Could be a
  poetic / older form, but it sits next to `լուսավոր` in a way
  that suggests the model meant the standard sense and produced
  the non-standard form. Borderline.

This is a **different shape** from v3.2.2 mp1's single `Խտնված`
participle — it's a verb / causative / noun cluster spread across
all three turns, not concentrated in one place.

**Verdict: PARTIAL SUCCESS.** R2 holds against the previously-named
families. R2 does not hold against the next layer of novel
coinages — verb-from-noun, alt-causative, and coined / mis-selected
nouns. The R2 named-exemplar approach is exemplar-coverage-bound;
each round suppresses the named family but the *shape* of the
problem (model coinage from near-stems) persists.

A future v3.3 might want to shift from "list bad tokens" to
"only use participles / verbs / nouns whose 3rd-person past form
you can name aloud first" — a structural rule, not an exemplar list.
Not for v3.2.3.

## 9. Opener-rule verdict — first-sentence WORKS, mid-paragraph REGRESSION FLAG

The v3.2.3 opener-rule tightening at `919dee5` was meant to
address two opener patterns:

- **T1 first-sentence `Մի անգամ` opener** — ABSENT in v3.2.3 mp1.
  The places-anchored pattern (`Խնձորենու այգում ամեն ինչ խաղաղ
  էր։`) lands cleanly. ✓
- **`Մի օր,` mid-paragraph caveat (sentence-6 pattern)** —
  **PRESENT in v3.2.3 mp1** at T1 sentence 6:
  `Մի օր, բնավ խումբախումբ խաղալու ժամանակ, շնիկը լսեց...`. The
  v3.2.3 opener rule did NOT suppress this on first paid sample.

**Verdict: PARTIAL SUCCESS.** First-sentence guard works
consistently; mid-paragraph guard does NOT on this run. Could be
sampling variance or an exemplar-coverage gap in the rule's
phrasing. A second mp1 retry, or a v3.2.3 mp2 (PA + PD), would
disambiguate variance from a structural miss in the rule.

## 10. Quality verdict (per-axis)

| Axis | Score | One-liner |
|---|---|---|
| Armenian naturalness | **3/5** | Multiple borderline / non-standard tokens (`բոցերում`, `փայլացնում`, `ցուցանի`, `անթել`) and one semantically odd phrase (`բնավ խումբախումբ խաղալու ժամանակ`). Comparable shape to v3.2.2 mp1 — different specific tokens, similar prevalence. |
| Eastern Armenian correctness | **3/5** | The previously-named fabrication families are gone — R2 win on those. New verb-from-noun coinages and coined nouns are slipping through; the shape of the R2 defense is exemplar-bound. |
| Fairy-tale feeling | **3/5** | Atmosphere is gentle and warm; concrete imagery present (golden leaves, dew drops, sun). But the prose feels mechanical — the dog "speaks" wisdom in T2, the leaf gains a "transparent" attribute in T2 that wasn't in T1, and the closing T3 image (`ցողի կաթիլներով շրջանակված մի անկյուն`) is forced. |
| Warmth for age 4–7 | **3.5/5** | Tone is gentle. Some vocabulary leans older (`հնարամիտ`, `հետաքրքրությամբ`, `հարմարավետ`, `կենտրոնացան`); a 4-year-old listener would lean on inferred meaning more than meaning-from-words. |
| Length / pacing | **4/5** | **T3 ≈ 90w in 70-100 (mid-band, +20 above floor — best of any v3.2-era run)**; T1 ≈ 119w in 90-130 (mid-band, +29 above floor); T2 ≈ 76w in 70-110 (mid-band, +6 above floor — first non-under-floor T2 across v3.2-era runs). Length improvement is the clearest single win this run. |
| Choice quality | **4/5** | Both blocks reproduced byte-for-byte. Same inherited PA T2 scenario defect (choice Բ presupposes stork going home). |
| Continuation coherence | **3/5** | T1→T2 ok. T2 dog speaking wisdom → T3 dog passively standing — same character-role discontinuity as v3.2.2 mp1. R6 staging marginal: T3 closes the small problem implicitly (the stork flies home) rather than via a discrete giving moment as `plan.resolutionStyle` calls for. |
| Opener quality | **3/5** | First sentence clean; mid-paragraph `Մի օր,` caveat in T1 sentence 6 — the exact pattern v3.2.3's opener rule was designed to kill. |
| Fake Armenian / invented morphology | **slipped through (4 tokens)** | `բոցերում էր`, `փայլացնում`, `ցուցանի`, `անթել`. R2 catches the named families but not the next layer. |
| English / meta leakage | **none** | No English words, no narrator brackets, no meta commentary. |
| Safety | **PASS** | Age-appropriate throughout; no fear, no violence, no medical content. |
| "Should Areg say this aloud?" | **NO — borderline** | The structural envelope (opener / closure / choice block / mid-band length) is the strongest of any v3.2-era PA run. But four borderline / coined tokens spread across three turns + the unaddressed mid-paragraph `Մի օր,` caveat mean the spoken output would carry detectable "model-Armenian" texture to a native ear. Not safe to ship. |

## 11. Comparison across all PA T3 runs

| Run | Prompt | PA T3 | C13 | T3 closure shape | Hard tally |
|---|---|---|---|---|---|
| v3.1 mp1 (`14731b3`) | v3.1 | ≈52 | FAIL (-18) | abstract coda | 9/10 |
| v3.1 mp2 (`fcffafe`) | v3.1 | ≈55 | FAIL (-15) | poetic, no closure pattern | 9/10 |
| v3.2 mp1 (`54c391f`) | v3.2 | ≈66 | FAIL (-4) | one concrete image, single | 9/10 |
| v3.2.1 mp1 (`11f63b3`) | v3.2.1 | ≈72 | PASS marginal (+2) | R4 sensory + reaction pair | 10/10 |
| v3.2.1 mp2 PA (`d9c36ca`) | v3.2.1 | ≈102 | PASS but +2 OVER ceiling | over-correction | 10/10 |
| v3.2.2 mp1 (`4649dda`) | v3.2.2 | ≈82 | PASS MID-BAND (+12 / -18) | natural, blended R4 | 10/10 |
| **v3.2.3 mp1 (this run)** | **v3.2.3** | **≈90** | **PASS MID-BAND (+20 / -10)** | **stork ascends + dissolve** | **9/10** (opener mid-paragraph) |

C13 mid-band landing is the second consecutive run; the R4 ceiling-
guard ladder appears stable. Hard-tally regression on the opener
(mid-paragraph `Մի օր,`) is the one new flag.

## 12. Engineering verdict

- **Engineering smoke: PASS.** v3.2.3 prompt loads, sha matches the
  `919dee5` post-tightening value (`ec8a9cb7...`), 3/3 calls
  succeeded, no upstream errors, no timeouts.
- **Story quality: STRUCTURAL ENVELOPE STRONGER, MORPHOLOGY SAME OR
  WORSE.** All three turns landed in their target word-bands for the
  first time across v3.2-era runs. Choice / closure / place-stem
  rules all clean. But four borderline tokens vs v3.2.2 mp1's one,
  and the mid-paragraph `Մի օր,` caveat that v3.2.3 was meant to
  suppress recurs.
- **R2 (verb-default + opener tightening): PARTIAL SUCCESS.**
  Previously-named families suppressed; new coinages
  (`բոցերում`/`փայլացնում`/`ցուցանի`/`անթել`) slipped through.
  Coverage gap, not a regression.
- **Opener rule: FIRST-SENTENCE WORKS; MID-PARAGRAPH FAILS on this
  sample.** Could be sampling variance or rule under-specification.
- **OpenAI gpt-4o + v3.2.3 is closer to production-ready than
  v3.2.2 on length-band discipline, but NOT on morphology
  discipline.** The morphology problem appears not to be a
  v3.2.x-tunable issue — successive named-exemplar passes catch the
  named family and the next family appears.
- **Provider decision: do NOT make.** Single run, single provider.
- **Production integration: do NOT integrate.** No runtime /
  `ChatService` / runtime system prompt / `appsettings` / `*.csproj`
  / test / seed-bank / name-bank / generator / validator / TTS /
  STT change is implied or authorized by this run.

## 13. Recommended next step (no action taken; awaits explicit GO)

1. **Record this evidence first** — this file. Free; preserves the
   v3.2.3 first-paid-sample finding past the session boundary.
2. **Do NOT auto-rerun mp2.** v3.2.3 mp2 is not warranted yet; the
   morphology gap surfaced by mp1 is a rule-shape problem
   (exemplar-bound R2) that another paid run will not change. A
   v3.3 design pass (structural verb / participle rule, not
   exemplar-list extension) is the better next move before
   spending another mp2 budget.
3. **Cross-provider comparison still pending.** Same Plan A run
   under Claude API would be the next informative paid call once
   an Anthropic key is provisioned and a structural v3.3 rule is
   drafted. Out of scope for this slice.

## 14. Raw outputs

**Not duplicated here.** The full per-turn text, latencies, token
counts, and stop-reasons are preserved in
`tools/StoryModelBakeoff/bin/Debug/net10.0/results/20260509T232726Z/review.md`
(gitignored build output; this evidence file is the only repo-tracked
artifact). The phrases called out in §§ 7–10 are quoted verbatim
above; nothing else is reproduced.

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
`bin/Debug/net10.0/results/20260509T232726Z/` artifacts are not
committed and never will be — this evidence file is the only
repo-tracked artifact of the run.

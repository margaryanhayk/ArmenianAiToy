# v3.2.2 implementation notes — 2026-05-09

**Status:** prompt-only research implementation. **No paid API call
has been run by this slice.** No production / runtime change. No
`ChatService` change. No provider switch. No `appsettings.json`
change. No `*.csproj` change. No backend / frontend / tests /
seed-bank / name-bank / generator / validator / Program.cs / README
/ runtime system prompt / TTS / STT change. No commit. No push. No
stage. The deliverable is two files: this note plus the targeted
edit to `tools/StoryModelBakeoff/system-prompt-v3-2.txt`.

This implementation slice lands the three textual changes drafted in
the v3.2.2 design plan committed at `86d035a`
(`tools/StoryModelBakeoff/evaluations/v3-2-2-tightening-plan-20260509.md`)
and validates them with no-network dry-checks against the bake-off
runner.

---

## 1. Files changed

| Path | Change |
|---|---|
| `tools/StoryModelBakeoff/system-prompt-v3-2.txt` | three surgical edits (R2, R3, R4) per the design plan §§ 4.1–4.3 |
| `tools/StoryModelBakeoff/evaluations/v3-2-2-implementation-notes-20260509.md` | this note (new) |

**Nothing else touched.** No production / runtime files. No
`ChatService.cs`, no `appsettings*.json`, no `*.csproj`, no
`wwwroot/*.html`, no tests, no seed bank, no name bank, no
`generate-story-plan.js`, no `validate-*.js`, no `Program.cs`, no
`README.md`, no v3.1 prompt or scenarios, no `bin/Debug/...`
artifacts, no TTS / STT / firmware. Production prompt sha
`54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
unchanged.

The file was NOT renamed. It is still `system-prompt-v3-2.txt` —
v3.2.2 is the *content* version. Per-rule version tags carry the
v3.2.2 marker in the headers (`R2 ... + verb-default v3.2.2`,
`R3 ... + whole-T1 v3.2.2`, `R4 ... + per-band-ladder v3.2.2`).

---

## 2. Defects addressed

Each edit targets one specific defect surfaced by v3.2.1 mp2
(committed evidence at `d9c36ca`).

### 2.1 R4 — bimodal C13 (PA over-correction, PD under-floor)

**Evidence at `d9c36ca`:**
- PA T3 = 102 words against 70-100 band — overshoots ceiling by +2.
- PD T3 = 91 words against 100-130 band — under floor by -9.
- v3.2.1 R4 was floor-only; no clause forbade cushion when above
  floor+5; no clause stopped at the upper bound.

**Edit landed:** R4 block expanded from 17 → 30 lines. The change
introduces:
- A new third bullet `ՎԵՐԻՆ ՍԱՀՄԱՆԸ ԵՎՍ ԲԱՑԱՐՁԱԿ Է` explicitly
  forbidding overshoot of the upper bound, with band-specific
  numeric reminders (`70-100 → ՉԱՆՑՆԵԼ 100; 100-130 → ՉԱՆՑՆԵԼ 130`).
- A three-rung response ladder replacing the old binary "below
  floor+5 → MUST add" rule:
  - **(ա)** below floor / floor +0–4 → MUST add the existing
    sensory + reaction sentence pair.
  - **(բ)** floor+5 or above and within ceiling → do NOT add new
    sentences; end with `Վերջ։`. Explicit "do not add explanation,
    expansion, or the R4 two-sentence pair if you are already long
    enough."
  - **(գ)** at or over ceiling → end immediately with `Վերջ։`. No
    sensory image, no character reaction, no summary, no new
    micro-event.
- Header version-tagged `+ per-band-ladder v3.2.2`.

The lower expansion-types and forbidden-expansions blocks
(`Ընդունելի ընդարձակման տիպեր` / `ԱՐԳԵԼՎԱԾ ընդարձակում`) are
unchanged — they remain aligned with the new ladder.

### 2.2 R2 — fake Armenian forms on Plan D

**Evidence at `d9c36ca`:**
- PD T3 contained four fabricated / wrong-tense items:
  `խորոտալ`, `համբարձլ`, `փափախերները`, `պարգևիր`.
- v3.2's R2 named-exemplar approach (`ձայնուֆով`, `բարենի`) did not
  generalize.
- Common failure pattern: model fabricates verb conjugations
  (`պարգևիր` for `պարգևեց`, `համբարձլ` for `համբարձավ`) by
  attaching plausible-but-wrong endings to real stems.

**Edit landed:** R2 block expanded from 14 → 26 lines. The change
introduces:
- Four new tokens added to the named forbidden list:
  `«խորոտալ» չկա`, `«համբարձլ» սխալ-ձև`,
  `«փափախերները» սխալ-ընտրված`, `«պարգևիր» սխալ-դեմք`.
- A new structural sub-rule `ԲԱՅԵՐԻ ՀԱՄԱՐ` directing the model to
  default to 3rd-person past forms `-եց / -ավ / -վեց` when
  uncertain about a verb's conjugation.
- A safe-verb whitelist explicitly listing the operator-named
  defaults: `«սկսեց», «դարձավ», «լսվեց», «մոտեցավ», «բացվեց»,
  «փայլեց», «հանգստացավ»`.
- Two new mapping examples on the conjugation rule:
  `«պարգևիր» (սխալ դեմք) → «պարգևեց»`,
  `«համբարձլ» (ոչ վավեր ձև) → «համբարձավ»`.
- Two new mapping examples on the lexical rule:
  `«խորոտալ» (չկա) → «երգել» / «սուլել» / «շշնջալ»`,
  `«փափախերները» (սխալ-ընտրված) → «բարիքներ» / «նվերներ» / «շոյանքներ»`.
- A new bullet preserving age-7-richer poetic register but only
  with known forms: `Տարիքային ճոխությունը պահպանի... բայց ՈՉ
  հնարված ձևերի գնով։`
- Header version-tagged `+ verb-default v3.2.2`.

### 2.3 R3 — place-stem drift after first sentence

**Evidence at `d9c36ca`:**
- PD T1 sentence 1: `Հին կամուրջի վրա` ✓ (schwa preserved).
- PD T1 sentence 3: `կամրջի տակով` ✗ (schwa dropped).
- v3.2 R3's literal scope was "first sentence only"; the model
  honored the rule's letter and dropped schwa exactly where the
  rule said it didn't apply.

**Edit landed:** R3 block expanded from 21 → 32 lines. The change
introduces:
- The literal scope changed from `Քայլ 1-ի ԱՌԱՋԻՆ ՆԱԽԱԴԱՍՈՒԹՅՈՒՆՈՒՄ`
  to `Քայլ 1-ի ՅՈՒՐԱՔԱՆՉՅՈՒՐ ՆԱԽԱԴԱՍՈՒԹՅՈՒՆՈՒՄ, որտեղ առկա է
  PLACE_STEM-ի հիմքը (առաջին նախադասությունում ՊԱՐՏԱԴԻՐ, ինչպես
  նաև ցանկացած հետագա նախադասությունում, որտեղ վերադառնում ես
  վայրի անվանմանը)`.
- Two new ALLOWED examples for non-first-sentence T1 contexts:
  `«...իսկ կամուրջի տակով...»`, `«...կամուրջի վրայով...»`.
- Two new FORBIDDEN examples for non-first-sentence T1 contexts:
  `«...կամրջի տակով...»`, `«...կամրջի վրայով...»`, with explicit
  parenthetical clarification "first sentence OR any subsequent T1
  sentence — dropping the `ու` sound violates the rule."
- A new pronoun-fallback bullet `ԱՌԱՆՑ ԿՐԿՆՈՒԹՅԱՆ ՊԱՀԱՆՋ`
  permitting `«այնտեղ», «այդ տեղում», «դրա վրա», «դրա տակ»` when
  repeating the place name a second or third time would feel
  awkward. The rule requires consistency, not literal repetition.
- The pre-send self-check expanded from one check (first sentence)
  to two (first sentence + any subsequent T1 sentence with the
  stem in non-pronominal form).
- Header version-tagged `+ whole-T1 v3.2.2`.

---

## 3. Summary of prompt edits

`git diff --stat` reports **+91 / -25 = +66 net lines** on
`system-prompt-v3-2.txt`. The breakdown is roughly +36 structural
additions (new bullets / new sub-rules / new examples) plus ~30
in-place rewrites of existing lines (the three rule headers got
v3.2.2 tags appended; the `Կանոնը նպատակ ունի...` line was rewrapped
to fit the four new forbidden-token examples; etc.). ~280 Armenian
words / ~400 input tokens per call (≈ 6 % over v3.2.1's ~6500
input-token T1 baseline). Cost-of-tightening forecast: ≈$0.004 per
mp2 (6 turns × 400 added tokens × $1.67/1M). Bounded.

| Block | Before | After | Net structural |
|---|---:|---:|---:|
| R2 (lines 29–42 → 29–54 in v3.2.2) | 14 lines | 26 lines | +12 |
| R3 (lines 60–80 → 60–91 in v3.2.2) | 21 lines | 32 lines | +11 |
| R4 closure block (lines 186–202 → 197–226 in v3.2.2) | 17 lines | 30 lines | +13 |
| **Total structural** | **52** | **88** | **+36** |

(`git diff` will count the wire-level +91/-25 because in-place line
rewrites show up as both deletion and addition. The +36 structural
figure is the rule-shape change; the +66 net wire-level figure is
the file-line change.)

---

## 4. What was intentionally not changed

Per the v3.2.2 design plan § 5 (`What NOT to change`):

- **No production / runtime change** — `ChatService`, backend,
  frontend, `appsettings*.json`, `*.csproj`, tests, runtime
  system-prompt (sha `54dfb1c9...`) all frozen.
- **No provider switch** — OpenAI / gpt-4o stays.
- **No Story Director runtime integration** — bake-off remains
  research tooling.
- **No paid API call.**
- **No edit to other prompt files** — v3.1 prompt
  (`system-prompt-v3-1.txt`) and v3.1 scenarios
  (`bakeoff-prompts-v3-1.json`) untouched.
- **No edit to Program.cs, README, validators, generators,
  seed/name banks.**
- **No scenario regeneration** — PA T2 choice-Բ scenario defect
  (presupposes stork going home before T2 narrative resolves)
  remains inherited from `bakeoff-prompts-v3-1.json`. Out of
  scope for this slice.
- **No new R-rules.** v3.2.2 extends R2, R3, R4 only; no R7+
  introduced.
- **R1 (cross-language leak) unchanged** — held on v3.2.1 mp2 PD.
- **R5 (no moralizing / abstract coda) unchanged** — held clean
  across all v3.2-era runs.
- **R6 (resolution staging) unchanged** — partial-success status
  is acceptable.
- **C8a, C9, C14, C15, C16 strict-gate logic unchanged.**
- **Safety / age-band block unchanged.**
- **`Մի օր,` mid-paragraph caveat NOT addressed** (per design plan
  § 4.4 — recurring soft caveat, not a hard fail; banning it
  broadly risks suppressing a natural Armenian narrative
  connector).
- **Old-orthography rule NOT added** (per design plan § 4.5 — not
  reproducing under v3.2.x; example creep without strong evidence).

---

## 5. Validation commands run

All four ran from project root with no env vars set. **No `--run`,
no `--i-understand-live-cost`, no `*_API_KEY` env-var read. Zero
network activity.**

```
dotnet build tools/StoryModelBakeoff
→ Build succeeded.

dotnet run --project tools/StoryModelBakeoff -- \
  --provider openai --max-prompts 1 \
  --scenarios tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json \
  --system-prompt tools/StoryModelBakeoff/system-prompt-v3-2.txt
→ Scenarios: 1 (PA), TOTAL = 0 calls, openai status=skipped (env OPENAI_API_KEY unset).

dotnet run --project tools/StoryModelBakeoff -- \
  --provider openai --max-prompts 2 \
  --scenarios tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json \
  --system-prompt tools/StoryModelBakeoff/system-prompt-v3-2.txt
→ Scenarios: 2 (PA + PD), TOTAL = 0 calls, openai status=skipped.

dotnet run --project tools/StoryModelBakeoff -- \
  --provider claude --max-prompts 1 \
  --scenarios tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json \
  --system-prompt tools/StoryModelBakeoff/system-prompt-v3-2.txt
→ Scenarios: 1 (PA), TOTAL = 0 calls, claude status=skipped (env ANTHROPIC_API_KEY unset).
```

All three dry-runs reported `alternate system-prompt = yes`,
`alternate scenarios = yes`, drift `drifted (intentional —
alternate system prompt)`. Production prompt sha `54dfb1c9...`
unchanged across all three.

The new v3.2.2 prompt sha (post-edit) is recorded in the run plan
output and supersedes the v3.2.1 sha
(`3ed1dfecafd6d63b02d3cd3dc76e3515bbf7e8f661d7ac26d8dd2e3f3032bc5b`)
that shipped with `87665f5`. The change in sha confirms the edit
landed.

### Engineering smoke verdict

- v3.2.2 prompt loads cleanly (build + 3 dry-runs).
- Scenarios file loads cleanly (1 / 2 / 1 scenario shapes correct).
- Drift verdict reads `drifted (intentional)` on all three.
- No crash, no exception, no upstream error.

---

## 6. Recommended next paid validation

After this implementation slice has been reviewed and committed:

1. **OpenAI v3.2.2 mp1 only after explicit operator GO.** Same
   shape as the v3.2.1 mp1 paid run committed at `11f63b3`:

   ```
   dotnet run --project tools/StoryModelBakeoff -- \
     --run --provider openai --max-prompts 1 --i-understand-live-cost \
     --scenarios tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json \
     --system-prompt tools/StoryModelBakeoff/system-prompt-v3-2.txt
   ```

   Spend forecast: ≈$0.02 (3 calls × ~5500 in / ~250 out tokens per
   call, plus the v3.2.2 prompt-tax). Captures whether the **R4
   ceiling guard** (case (բ) / (գ)) prevents PA T3 from
   overshooting the upper bound that mp2 hit at 102w.

2. **OpenAI v3.2.2 mp2 only if mp1 looks sane.** Same shape as the
   v3.2.1 mp2 paid run committed at `d9c36ca`:

   ```
   dotnet run --project tools/StoryModelBakeoff -- \
     --run --provider openai --max-prompts 2 --i-understand-live-cost \
     --scenarios tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json \
     --system-prompt tools/StoryModelBakeoff/system-prompt-v3-2.txt
   ```

   Spend forecast: ≈$0.05 (6 calls). Load-bearing test for **R2
   verb-default** on PD (the four mp2 fabrications — should not
   recur), **R3 whole-T1 scope** on PD (the `կամրջի` schwa-drop in
   T1 sentence 3 — should not recur), and **R4 closure floor** on
   PD (-9 below floor on mp2 — should now meet the 100-floor).

**No further paid call without explicit GO.** No `--allow-full-set`.
No Claude. The mp1 → review → mp2 sequence was the discipline that
worked for the v3.2.1 R4 tightening at `87665f5` → `11f63b3` →
`d9c36ca`; this slice repeats the pattern.

---

## 7. Acceptance criteria reminder (from design plan § 7)

A v3.2.2 mp1 + mp2 evidence pair would close the design if:

| Criterion | Plan A | Plan D |
|---|---|---|
| C13 (T3 closure length) | PASS within 75–95w (band 70–100, no overshoot) | PASS within 105–125w (band 100–130, no underfloor) |
| C16 + R3 (place anchor whole-T1) | PASS | PASS (no `կամրջի` anywhere in T1) |
| R1 (no English leak) | n/a | PASS |
| R2 (no fabricated morphology) | PASS | PASS (none of `խորոտալ`/`համբարձլ`/`փափախերները`/`պարգևիր` and no new fabrications) |
| C1 / C2 / C3 / C6 / C8a / C9 / C14 / C15 | All PASS clean | All PASS clean |

Single-sample, not yet a reliability claim. A multi-sample variance
pass remains a future v3.3+ concern.

---

## 8. No secrets / scope guard

This file contains **no API key**, no `OPENAI_API_KEY` value, no
token, no bearer credential, no parent JWT, no device API key, and
no private endpoint. The dry-runs in § 5 deliberately ran with both
`OPENAI_API_KEY` and `ANTHROPIC_API_KEY` **unset**, and the
runner's pre-execution plan reported `status=skipped (env … unset)`
accordingly.

No production / runtime files are touched by this slice:

- `ChatService`, backend, frontend, `appsettings*.json`, `*.csproj`,
  tests, seed bank, name bank, generators, validators, Program.cs,
  README, runtime system prompt, v3.1 prompt, v3.1 scenarios,
  speech / TTS / STT / hardware / firmware — all unchanged.
- Production prompt sha
  `54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
  unchanged.
- Bake-off scenarios sha
  `e6cdba77d64640c89dc6aa094108f3bff040a26044af1b1cc75f5a7ab0f89b59`
  unchanged.

Only `tools/StoryModelBakeoff/system-prompt-v3-2.txt` (this slice's
edit) and this implementation note (new file) are touched. No
`bin/Debug/...` artifacts created (the dry-runs do not write a
`results/<UTCts>/` directory).

This file is the only repo-tracked artifact of the slice besides
the v3.2.2 prompt edit itself.

# v3.2.3 implementation notes — 2026-05-10

**Status:** prompt-only research implementation. **No paid API call
has been run by this slice.** No production / runtime change. No
`ChatService` change. No provider switch. No `appsettings.json`
change. No `*.csproj` change. No backend / frontend / tests /
seed-bank / name-bank / generator / validator / Program.cs / README
/ runtime system prompt / TTS / STT change. No commit. No push. No
stage. The deliverable is two files: this note plus the targeted
edit to `tools/StoryModelBakeoff/system-prompt-v3-2.txt`.

This implementation slice lands the two textual changes drafted in
the v3.2.3 design plan committed at `0f6b726`
(`tools/StoryModelBakeoff/evaluations/v3-2-3-tightening-plan-20260510.md`)
and validates them with no-network dry-checks against the bake-off
runner.

---

## 1. Files changed

| Path | Change |
|---|---|
| `tools/StoryModelBakeoff/system-prompt-v3-2.txt` | two surgical edits (R2 known-word-only, rule A variant-coverage) per the design plan §§ 6.A and 6.B |
| `tools/StoryModelBakeoff/evaluations/v3-2-3-implementation-notes-20260510.md` | this note (new) |

**Nothing else touched.** No production / runtime files. No
`ChatService.cs`, no `appsettings*.json`, no `*.csproj`, no
`wwwroot/*.html`, no tests, no seed bank, no name bank, no
`generate-story-plan.js`, no `validate-*.js`, no `Program.cs`, no
`README.md`, no v3.1 prompt or scenarios, no `bin/Debug/...`
artifacts, no TTS / STT / firmware. Production prompt sha
`54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
unchanged.

The file was NOT renamed. It is still `system-prompt-v3-2.txt` —
v3.2.3 is the *content* version. Per-rule version tags carry the
v3.2.3 marker in the headers (`R2 ... + verb-default v3.2.2 +
known-word-only v3.2.3`, `A — v2 + variant-coverage v3.2.3`).

R3 and R4 were intentionally left unchanged — both demonstrably
work in v3.2.2 and the v3.2.3 design plan § 6.D explicitly recommends
keeping them as-is.

---

## 2. Defects addressed

Each edit targets one specific defect surfaced by v3.2.2 mp2
(committed evidence at `fbdc639`).

### 2.1 R2 — fake Armenian forms still leaking

**Evidence at `fbdc639`:**
- Seven new fabrications across 5 turns: `կարապված`,
  `շնորակալությամբ`, `մեղմշխարհն` (PA); `իրեբերում`, `հեռականի`,
  `շտեպնով` (PD T1); `տերմինները` (PD T2).
- The four named v3.2.1 mp2 fabrications (`խորոտալ`, `համբարձլ`,
  `փափախերները`, `պարգևիր`) did NOT recur — named-family
  suppression holds, but novel coinages keep appearing.
- v3.2.2's named-exemplar approach is reactive; the verb-default
  rule covers verbs but not fabricated participles, fabricated
  compounds, or wrong-word selection.

**Edit landed:** R2 block expanded from 29 → ~46 lines. The change
introduces:
- A new `ՀԻՄՆԱԿԱՆ ԿԱՆՈՆ` ("MAIN RULE") — categorical "if a word
  sounds newly coined, unusual, or uncertain → REPLACE with a
  simpler known Eastern Armenian word." Targets *any* uncertain
  word, not just verbs.
- The named forbidden list extended with the seven mp2 tokens
  alongside the four prior ones — now 13 named tokens total
  (`ձայնուֆով`, `բարենի`, `խորոտալ`, `համբարձլ`, `փափախերները`,
  `պարգևիր`, `կարապված`, `մեղմշխարհն`, `իրեբերում`, `հեռականի`,
  `շտեպնով`, `տերմինները`, `շնորակալությամբ`).
- A new `ԲԱՐԴ ԲԱՌԵՐԻ ՀԱՄԱՐ` ("FOR COMPOUND WORDS") sub-rule —
  fabricated compounds like `մեղմշխարհն` must be split into two
  words (`մեղմ աշխարհը`, `մեղմ լույսը`, `խաղաղությունը`), not
  merged. Direct fix for the v3.2.2 mp2 PA T3 fabrication.
- The safe-verb whitelist extended with `բերում էր`,
  `շարժվում էր`, `երևում էր` (the three replacement candidates
  for `իրեբերում էր`).
- A new specific-replacement mapping table with 14 entries
  (the 7 v3.2.2 mp2 tokens + the 7 prior + verb conjugations).
- The age-7 caveat clause from v3.2.2 R2 preserved.
- The pre-send self-check expanded to mention compounds explicitly.

### 2.2 C1 — forbidden opener `Մի գեղեցիկ օրը`

**Evidence at `fbdc639`:**
- PA T1 opens `Մի գեղեցիկ օրը, շնիկը և իր շուն ընկերն...` —
  explicit instance of rule A's forbidden `Մի գեղեցիկ օր`
  template, just with the definite-article suffix `-ը`.
- v3.2.2 mp1 (`4649dda`) was clean on this gate; v3.2.2 mp2
  produced the violation under sampling variance.
- Root cause: rule A's forbidden list was interpreted literally
  (without article variants); the `տիպի կաղապարով` ("of this
  template") wording didn't bind hard enough.

**Edit landed:** rule A block expanded from 3 → ~16 lines. The
change introduces:
- The five literal forbidden openers preserved verbatim.
- A new `Այս արգելքը ՆԵՐԱՌՈՒՄ Է գրեթե նույնական ձևակերպումները`
  variant-coverage clause with five named example violations:
  - `«Մի գեղեցիկ օրը,»` ⟵ definite-article variant
  - `«Մի գեղեցիկ առավոտը,»` ⟵ definite-article variant
  - `«Մի անգամը»` ⟵ inflected
  - `«Այդ մի գեղեցիկ օրը»` ⟵ adjective-extended
  - `«Մի գեղեցիկ ձմեռային օրը»` ⟵ adjective-extended pattern
- A mid-paragraph carve-out: rule applies to T1 first sentence
  only; mid-story `մի օր` / `մի անգամ` connectors stay allowed
  if not opening the turn or a sentence formulaically.
- A pattern-detection self-check: if the first sentence has the
  `մի + adjective + time-word` pattern (`մի + Х + օր / առավոտ /
  երեկո / գիշեր`), rewrite from `plan.place` or character.

### 2.3 R3 and R4 — intentionally unchanged

Per the v3.2.3 design plan § 6.D:

- **R3 (whole-T1 widening v3.2.2)** — clean PASS on PD T1 mp2
  (zero schwa drops across all four `հին կամուրջ` mentions).
  Header version tag preserved (`R3 / C16 — STRICTER v3.2 +
  whole-T1 v3.2.2`). No content change.
- **R4 (per-band ladder + ceiling guard v3.2.2)** — replicated
  mid-band PA T3 across mp1+mp2 (both at exactly 82w). Header
  version tag preserved (`R4 ... + per-band-ladder v3.2.2`). No
  content change.

The PD T3 floor remains untested due to the v3.2.2 mp2 http_429.
Tightening R4 further before that evidence lands would risk
over-correcting; this slice deliberately defers any R4 edit.

---

## 3. Summary of prompt edits

`git diff --stat` reports the wire-level line delta on
`system-prompt-v3-2.txt`. Structural delta:

| Block | Before (v3.2.2) | After (v3.2.3) | Net structural |
|---|---:|---:|---:|
| R2 (lines 29–57 → 29–~75) | 29 lines | ~46 lines | +17 |
| Rule A (lines 59–61 → 61–~76) | 3 lines | ~16 lines | +13 |
| **Total structural** | **32** | **62** | **+30** |

(`git diff` will count the wire-level inserted/deleted lines
because in-place line rewrites of the rule headers show up as
both deletion and addition.)

Token cost forecast: ~30 added structural lines × ~7 Armenian
words/line ≈ 210 added words ≈ ~280 added input tokens per call.
On a future v3.2.3 mp1 paid run that's ≈$0.001 added cost.
Bounded. v3.2.3 is the third additive iteration on the v3.2 base
(v3.2.2 added ~36, v3.2.3 adds ~30); cumulative prompt growth is
~13% over v3.2 baseline.

---

## 4. What was intentionally not changed

Per the v3.2.3 design plan § 7 (`What NOT to change`):

- **No production / runtime change** — `ChatService`, backend,
  frontend, `appsettings*.json`, `*.csproj`, tests, runtime
  system-prompt (sha `54dfb1c9...`) all frozen.
- **No provider switch** — OpenAI / gpt-4o stays.
- **No Story Director runtime integration**.
- **No paid API call.**
- **No edit to other prompt files** — v3.1 prompt
  (`system-prompt-v3-1.txt`) and v3.1 scenarios
  (`bakeoff-prompts-v3-1.json`) untouched.
- **No edit to Program.cs, README, validators, generators,
  seed/name banks.** The 429 strategy (scenario-id selection,
  sleep-between-scenarios) is documented as a separate slice in
  the design plan § 6.C; not implemented here.
- **No scenario regeneration** — PA T2 choice-Բ inherited defect
  remains.
- **No new R-rules.** v3.2.3 extends R2 + rule A only; R3, R4,
  R5, R6 unchanged.
- **R3 (whole-T1 v3.2.2) unchanged** — works on PD T1.
- **R4 (per-band ladder v3.2.2) unchanged** — works on PA;
  PD T3 floor untested.
- **R5 (no abstract coda) unchanged** — held clean across all
  v3.2-era runs.
- **R6 (resolution staging) unchanged** — partial-success status
  is acceptable.
- **R1 (cross-language) unchanged** — held on PD mp2 (no English
  leak).
- **Safety / age-band block unchanged.**
- **No runtime decoding-temperature change.** Mention only as a
  separate future option (decoding-level fix at the production
  caller); explicitly out of scope.

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
→ Scenarios: 2 (PA + PD), TOTAL = 0 calls, openai skipped.

dotnet run --project tools/StoryModelBakeoff -- \
  --provider claude --max-prompts 1 \
  --scenarios tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json \
  --system-prompt tools/StoryModelBakeoff/system-prompt-v3-2.txt
→ Scenarios: 1 (PA), TOTAL = 0 calls, claude status=skipped (env ANTHROPIC_API_KEY unset).
```

All three reported `alternate system-prompt = yes`,
`alternate scenarios = yes`, drift `drifted (intentional)`.
Production prompt sha `54dfb1c9...` unchanged across all three.

The new v3.2.3 prompt sha (post-edit) is recorded in the run plan
output and supersedes the v3.2.2 sha
(`908ae30e610e18389b2151c262149db32830b3f663db70fe98b268e4e06fec2f`)
that shipped with `768be15`. The change in sha confirms the edit
landed.

### Engineering smoke verdict

- v3.2.3 prompt loads cleanly (build + 3 dry-runs).
- Scenarios file loads cleanly (1 / 2 / 1 scenario shapes correct).
- Drift verdict reads `drifted (intentional)` on all three.
- No crash, no exception, no upstream error.

---

## 6. Recommended next paid validation

After this implementation slice has been reviewed and committed:

1. **OpenAI v3.2.3 mp1 (PA only) only after explicit operator GO.**
   Same shape as the v3.2.2 mp1 paid run committed at `4649dda`:

   ```
   dotnet run --project tools/StoryModelBakeoff -- \
     --run --provider openai --max-prompts 1 --i-understand-live-cost \
     --scenarios tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json \
     --system-prompt tools/StoryModelBakeoff/system-prompt-v3-2.txt
   ```

   Spend forecast: ≈$0.02 (3 calls × ~6000 in / ~250 out tokens
   per call, plus the v3.2.3 prompt-tax). Captures whether (a)
   the new R2 known-word-only rule suppresses the seven mp2
   fabrications (`կարապված`, `շնորակալությամբ`, `մեղմշխարհն`,
   etc.) and any novel ones; (b) the new rule A variant-coverage
   prevents `Մի գեղեցիկ օրը,`-class openers; (c) R4 ceiling
   guard regression — does PA T3 stay mid-band at ≈80w as in
   v3.2.2 mp1+mp2.

2. **AVOID full v3.2.3 mp2 until the rate-limit strategy
   exists.** v3.2.2 mp2 (`fbdc639`) hit the gpt-4o 30k-TPM tier
   limit on PD T3 and produced a partial-failure run. v3.2.3 has
   ≈10% larger input prompt than v3.2 (cumulatively ~280 added
   tokens per call across the v3.2 → v3.2.2 → v3.2.3 sequence),
   so the TPM-window pressure on PD T3 is *higher*, not lower,
   under v3.2.3.

   **Recommended PD validation (when GO arrives, separate slice):**
   - **Option A (recommended): two separate paid mp1 runs** —
     PA-only mp1 first, then PD-only mp1 after the TPM window
     resets (~60s). Bypasses the cumulative-window problem by
     design.
   - **Option B: scenario-id selection in `Program.cs`** — add
     a `--scenario PD` CLI flag in a separate tool-only slice;
     then run `--scenario PD --max-prompts 1`. Cleaner long-term;
     requires a pre-paid-validation slice.
   - **Option C: `--sleep-between-scenarios <s>` flag** — insert
     a configurable wait between PA and PD in a single mp2 run.
     Also tool-only, also separate slice.

3. **No further paid call without explicit GO.** No
   `--allow-full-set`. No Claude. The mp1 → review →
   PD-validation strategy was the discipline that worked for
   v3.2.1 (`87665f5` → `11f63b3` → `d9c36ca`) and v3.2.2
   (`768be15` → `4649dda` → `fbdc639`); this slice repeats the
   pattern with the added "do not run mp2 until TPM strategy
   solved" constraint.

---

## 7. Acceptance criteria reminder (from design plan § 9)

A v3.2.3 mp1 + (split PA / PD) mp2-equivalent evidence pair would
close the design if:

| Criterion | Plan A | Plan D |
|---|---|---|
| C1 (no forbidden opener — strict + variant) | PASS clean (no `Մի գեղեցիկ օրը,` / `Մի գեղեցիկ առավոտը,` / `Այդ մի գեղեցիկ օրը` / variant) | PASS clean |
| C13 (T3 closure length) | PASS within 75–95w | PASS within 105–125w |
| C16 + R3 (place anchor whole-T1) | PASS | PASS |
| R1 (no English leak) | n/a | PASS |
| R2 (no fabricated morphology) | PASS — none of 13 named tokens AND no new fabrications | PASS — none of 13 named tokens AND no new fabrications |
| C2 / C3 / C6 / C8a / C9 / C14 / C15 | All PASS clean | All PASS clean |

Single-sample, not yet a reliability claim. A multi-sample
variance pass remains a future v3.3+ concern.

---

## 8. No secrets / scope guard

This file contains **no API key**, no `OPENAI_API_KEY` value, no
token, no bearer credential, no parent JWT, no device API key,
and no private endpoint. The dry-runs in § 5 deliberately ran
with both `OPENAI_API_KEY` and `ANTHROPIC_API_KEY` **unset**, and
the runner's pre-execution plan reported `status=skipped (env …
unset)` accordingly.

No production / runtime files are touched by this slice:

- `ChatService`, backend, frontend, `appsettings*.json`,
  `*.csproj`, tests, seed bank, name bank, generators,
  validators, Program.cs, README, runtime system prompt, v3.1
  prompt, v3.1 scenarios, speech / TTS / STT / hardware /
  firmware — all unchanged.
- Production prompt sha
  `54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
  unchanged.
- Bake-off scenarios sha
  `e6cdba77d64640c89dc6aa094108f3bff040a26044af1b1cc75f5a7ab0f89b59`
  unchanged.

Only `tools/StoryModelBakeoff/system-prompt-v3-2.txt` (this
slice's edit) and this implementation note (new file) are
touched. No `bin/Debug/...` artifacts created (the dry-runs do
not write a `results/<UTCts>/` directory).

This file is the only repo-tracked artifact of the slice besides
the v3.2.3 prompt edit itself.

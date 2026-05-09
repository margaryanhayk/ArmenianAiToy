# v3.2.3 prompt-tightening plan — 2026-05-10

**Status:** prompt-only research design. **No paid API call has been
run by this slice.** No production / runtime change. No `ChatService`
change. No provider switch. No `appsettings.json` change. No `*.csproj`
change. No backend / frontend / tests / seed-bank / name-bank /
generator / validator / Program.cs / README / runtime system prompt
/ TTS / STT change. No edit to `system-prompt-v3-2.txt` yet. No
commit. No push. No stage. The deliverable is this file alone.

This document designs **v3.2.3** based on the three open issues
v3.2.2 mp2 paid evidence at `fbdc639` surfaced. A separate
implementation slice (gated on operator approval) would then make a
single text-only edit to `tools/StoryModelBakeoff/system-prompt-v3-2.txt`,
mirroring the v3.2.2 implementation shape committed at `768be15`.

---

## 1. Status / scope

- **Design only.** The textual changes in §§ 6 below are drafted
  here, not applied to the prompt. The implementation slice is
  separate and operator-gated.
- **No API call.** No `OPENAI_API_KEY` will be read by this slice.
- **No production / runtime change.** Production prompt sha
  `54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
  remains unchanged. `ChatService`, backend, frontend,
  `appsettings*.json`, `*.csproj`, tests, seed bank, name bank,
  generator, validator, `Program.cs`, README, TTS / STT — all
  frozen.
- **No `ChatService` change.** Frozen.
- **No provider decision.** OpenAI / gpt-4o stays for research; no
  Claude / Gemini / Local switch implied.
- **No `system-prompt-v3-2.txt` edit yet.** The implementation
  slice is separate.

---

## 2. Evidence summary (v3.2.2 mp2 — `fbdc639`)

The load-bearing data point for this slice. Run shape:

- **5/6 turns succeeded.** PA T1+T2+T3 + PD T1+T2 completed.
- **PD T3 failed with HTTP 429** — OpenAI gpt-4o tokens-per-minute
  (TPM) tier limit (`Limit 30000, Used 26265, Requested 7881`).
  Upstream rate-limit failure, not a prompt-output failure.
- **PA T3 = 82 words** (mid-band) — replicates v3.2.2 mp1 exactly.
- **R3 whole-T1 stem consistency: WORKED** on PD. All four `հին
  կամուրջ` stem mentions in T1 preserve full schwa.
- **R4 ceiling guard: WORKED on Plan A** (no under-floor, no
  over-ceiling, replicated across mp1+mp2). PD T3 untested
  (http_429).
- **R2 still leaks new fake Armenian forms** — seven novel
  fabrications across the 5 successful turns:
  `կարապված`, `շնորակալությամբ`, `մեղմշխարհն`,
  `իրեբերում`, `հեռականի`, `շտեպնով`, `տերմինները`.
  The four named mp2 fabrications (`խորոտալ`, `համբարձլ`,
  `փափախերները`, `պարգևիր`) did NOT recur — named-family
  suppression holds.
- **C1 regressed** with `Մի գեղեցիկ օրը,` opener on PA T1 —
  explicit instance of rule A's forbidden `Մի գեղեցիկ օր`
  template.

Companion reference for v3.2.2 design + mp1 evidence: `86d035a`
(plan), `768be15` (implementation), `4649dda` (mp1 evidence —
also produced one new fabrication `Խտնված` and clean C1).

---

## 3. What improved in v3.2.2

| Win | Evidence |
|---|---|
| **R3 whole-T1 stem consistency** | PD T1 across four `հին կամուրջ` mentions: s1 `հին կամուրջ` ✓, s2 `Հին կամուրջի վրայով` ✓, s3 `Կամուրջի տակ` ✓, s10 `Հին կամուրջից` ✓. **Zero schwa drops** — direct fix for v3.2.1 mp2's `կամրջի տակով` defect in T1 sentence 3. |
| **R4 ceiling guard on PA** | PA T3 mid-band at ≈82w replicated across mp1 + mp2. v3.2.1's bimodal failure (mp1 +2 above floor, mp2 +2 over ceiling) collapsed into consistent mid-band landing under the v3.2.2 three-rung ladder. |
| **Named R2 family suppression** | The four v3.2.1 mp2 fabrications (`խորոտալ`, `համբարձլ`, `փափախերները`, `պարգևիր`) did not recur in any of the 5 successful turns. The named-exemplar approach generalizes within the targeted family. |
| **Plan D T1/T2 length** | PD T1 = 136w in 130-180 ✓; PD T2 = 135w in 100-140 ✓. v3.2.1 mp2 had T1 = 87w (UNDER -43), T2 = 81w (UNDER -19). Major recovery on systematic under-floor pattern. |
| **PD T2 C8a verb match** | T2 first sentence opens with `Մողեսը քնած բանալին զգուշորեն տարավ բադիկի մոտ։` — `տարավ` matches SELECTED_CHOICE verb `տանել`. Improvement vs v3.2.1 mp2's `տվեց` (gave) verb shift. |

---

## 4. What still failed

| Failure | Class | Evidence |
|---|---|---|
| **R2 — novel fabrications** | Hard quality blocker (child-listener) | Seven new fake/wrong forms across PA + PD: `կարապված` (PA T1, fabricated participle), `շնորակալությամբ` (PA T3, typo for `շնորհակալությամբ`), `մեղմշխարհն` (PA T3, fabricated compound), `իրեբերում` (PD T1, non-standard verb), `հեռականի` (PD T1, non-standard adj), `շտեպնով` (PD T1, fabricated adv), `տերմինները` (PD T2, wrong-word selection). |
| **C1 — forbidden opener regression** | Hard-rule violation | PA T1 opens `Մի գեղեցիկ օրը, շնիկը և իր շուն ընկերն...` — `Մի գեղեցիկ օր` is on rule A's explicit forbidden list. Definite-article variant `Մի գեղեցիկ օրը` is the same template family. Did not happen on v3.2.2 mp1 (clean opener). |
| **PD T3 unknown** | Evidence gap | http_429 cut the run before PD T3. R4 mid-band ≈110 anchor + R2 PD T3-specific fabrication test + R6 PD staging test all incomplete. |
| **Plan A inherited scenario defect** | Out-of-scope (scenario file) | T2 choice Բ (`մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն`) presupposes the stork is going home, while T2 narrative still has it undecided. Property of `bakeoff-prompts-v3-1.json`, not the prompt. Inherited from every paid run on this scenario. |
| **R6 partial staging + plan-drift** | Soft quality | PA T2 introduces `քարտեզ` (map on the leaf) not in `plan.magicalObject` — slight plan-drift. T3 resolution staging marginal across runs (retrospective naming, not discrete giving moment). |

---

## 5. Root-cause analysis

### 5.1 R2 — named-exemplar approach is reactive

The current R2 (post-`768be15` `+ verb-default v3.2.2`) lists named
forbidden tokens (`ձայնուֆով`, `բարենի`, `խորոտալ`, `համբարձլ`,
`փափախերները`, `պարգևիր`) plus a structural verb-default sub-rule
and a safe-verb whitelist. The pattern catches its own listed
tokens (none of the four mp2 names recurred) but does not
generalize to novel coinages — the model invents new tokens in
the same fabrication-family that aren't on the list.

**Root cause: example coverage is reactive, not categorical.** The
v3.2.2 R2 design plan (§ 5.1 of `86d035a`) called this out and
proposed the verb-default rule as a structural defense. It works
on verbs but does not cover (a) fabricated participles like
`կարապված` / `Խտնված` (mp1); (b) typos like `շնորակալությամբ`;
(c) fabricated compounds like `մեղմշխարհն`; (d) wrong-word
selection like `տերմինները`. The structural surface area is wider
than verb conjugation alone.

**This is a prompt-wording issue, not a model limitation** — gpt-4o
produces clean Armenian when given clear lexical constraints. R2
needs broader categorical coverage, not more named examples.

### 5.2 C1 — forbidden-opener variant coverage

Current rule A:
```
- Մի՛ սկսիր «Մի անգամ», «Մի անգամ, շատ վաղուց», «Մի գեղեցիկ օր»,
  «Մի գեղեցիկ առավոտ» կամ «Շատ վաղուց» տիպի կաղապարով։
```

The model interpreted `Մի գեղեցիկ օր` literally (without
definite-article suffix `-ը`) and produced `Մի գեղեցիկ օրը,` as a
"different" form. The phrase `տիպի կաղապարով` ("of this type /
template") was intended to cover variants but apparently was not
strong enough to bind the model.

**Root cause: wording too literal.** Forbidden list needs explicit
definite-article variants and an explicit "any small grammatical
variation of these stems is also forbidden" clause.

### 5.3 HTTP 429 — API run strategy, not prompt

The 30k-TPM gpt-4o tier limit was hit at the cumulative-window
peak (≈26265 used + ≈7881 requested for PD T3 ≈ 34146 → over
30000). PD T3 carries the longest input echo (prior PD T1 + T2
output + system prompt + scenario context).

**Root cause: not a prompt issue.** The bake-off CLI does not
sleep between calls and does not have a per-scenario TPM-aware
rate-limiter. v3.2.2 mp1 (PA only, 19328 in / 626 out total)
fits cleanly under the TPM ceiling; mp2 (32125 in / 1449 out
across 5 calls + the rejected PD T3) blows past it.

**This is a tooling / strategy issue.** Solving it requires
runner support for scenario selection, sleep-between-scenarios,
or operator-side OpenAI tier upgrade — none of which are
prompt-text changes.

### 5.4 PD T3 unknown — evidence gap

Direct consequence of 5.3. The load-bearing R4 PD floor + R2 PD
T3 fabrication + R6 PD staging tests are incomplete. **Cannot
be solved by v3.2.3 prompt design alone** — needs a re-run
strategy (see § 6.C).

### 5.5 Plan A inherited scenario defect

T2 choice Բ presupposes resolution. Reproduced across all paid
PA runs (`14731b3` / `fcffafe` / `54c391f` / `11f63b3` / `4649dda`
/ `fbdc639`). **Property of `bakeoff-prompts-v3-1.json`, not the
prompt.** Out of scope for v3.2.3 (would require a separate
scenario-regeneration slice).

---

## 6. Proposed v3.2.3 fixes (concrete drafts)

This section drafts the prompt-text changes. The actual edit is
**not made by this slice** — it is documented here for review.

### 6.A R2 structural rethink

The named-exemplar approach is proven reactive. v3.2.3 should add
a stronger categorical "known-word only" rule that targets the
broader fabrication surface, not just verb conjugations.

**Proposed additions to R2** (replacing the current verb-default
sub-rule with a wider "known-word preference" rule, plus extending
the named-examples list with the seven mp2 tokens):

```
ՀՆԱՐՎԱԾ ՁԵՎ ԱՐԳԵԼՈՂ ԿԱՆՈՆ (R2 — NEW v3.2 + verb-default v3.2.2 + known-word-only v3.2.3)
- Մի՛ ստեղծիր նոր հայերեն ածանց, վերջածանց, բարդություն կամ
  վերլուծական ձև, որը ինքդ չես ճանաչում որպես հայտնի բառային ձև։
- ՀԻՄՆԱԿԱՆ ԿԱՆՈՆ. եթե բառը հնչում է նորաստեղծ, անսովոր կամ
  անվստահ — ՓՈԽԱՐԻՆԻՐ ավելի պարզ, հայտնի, ստուգված
  արևելահայերեն բառով։ Անվտանգ բառերի կիրառությունը գերադաս է
  բանաստեղծական հնարավոր հնարված ձևերից։
- Կանոնը կասեցնում է ՀՆԱՐՎԱԾ ձևերը (օրինակ՝ «ձայնուֆով» չկա,
  «բարենի» չկա, «խորոտալ» չկա, «համբարձլ» սխալ-ձև,
  «փափախերները» սխալ-ընտրված, «պարգևիր» սխալ-դեմք,
  «կարապված» հնարված-ձև, «մեղմշխարհն» հնարված-բարդություն,
  «իրեբերում» հնարված-բայ, «հեռականի» չկա, «շտեպնով» չկա,
  «տերմինները» սխալ-ընտրված, «շնորակալությամբ» սխալ-գրված) —
  ՈՉ թե արգելել ճիշտ բառային փոփոխությունը։ Հայտնի և ճիշտ
  ձևերը (օր.՝ «ձայնով», «բարիքի պես») թույլատրված են։
- ԲԱՅԵՐԻ ՀԱՄԱՐ. եթե կասկածում ես բայի ճիշտ ձևի հարցում,
  օգտագործիր 3-րդ դեմքի անցյալ կատարյալ -եց / -ավ / -վեց ձևը
  պարզ ու հայտնի բայով։ Անվտանգ բայերի օրինակներ. «սկսեց»,
  «դարձավ», «լսվեց», «մոտեցավ», «բացվեց», «փայլեց»,
  «հանգստացավ», «բերում էր», «շարժվում էր», «երևում էր»։
- ԲԱՐԴ ԲԱՌԵՐԻ ՀԱՄԱՐ. անհայտ կամ անսովոր բարդություններ ՉՍՏԵՂԾԵԼ։
  Եթե ուզում ես «մեղմ + աշխարհ» նմանատիպ բարդ իմաստ
  արտահայտել — գրիր ԵՐԿՈՒ բառով, ոչ թե միաձուլված. «մեղմ
  աշխարհը», «մեղմ լույսը», «խաղաղությունը»։ Միաձուլումը
  («մեղմշխարհն») հնարված է, ՉԻ ԹՈՒՅԼԱՏՐՎՈՒՄ։
- ՄԱՍՆԱՎՈՐ ՓՈԽԱՐԻՆՈՒՄՆԵՐ.
    «կարապված» (հնարված) → «փաթաթված» / «ծածկված» / «պարուրված»
    «շնորակալությամբ» (սխալ) → «շնորհակալությամբ»
    «մեղմշխարհն» (հնարված) → «մեղմ աշխարհը» / «մեղմ լույսը» /
                              «խաղաղությունը»
    «իրեբերում» (հնարված) → «բերում էր» / «շարժվում էր» /
                             «երևում էր»
    «հեռականի» (չկա) → «հեռվից» / «հեռավոր»
    «շտեպնով» (չկա) → «շտապով» / «արագ»
    «տերմինները» (սխալ-ընտրված) → «բառերը» / «ձայները» /
                                    «նշանները»
    «ձայնուֆով» (չկա) → «ձայների մեջ» / «շշուկների մեջ»
    «բարենի» (չկա) → «բարիքի պես» / «բարություն բերող»
    «խորոտալ» (չկա) → «երգել» / «սուլել» / «շշնջալ»
    «փափախերները» (սխալ-ընտրված) → «բարիքներ» / «նվերներ» /
                                     «շոյանքներ»
    «պարգևիր» (սխալ դեմք) → «պարգևեց»
    «համբարձլ» (ոչ վավեր ձև) → «համբարձավ»
- Տարիքային ճոխությունը պահպանի (age-7-richer-ի դեպքում թեթև
  բանաստեղծականություն թույլատրված է), բայց ՈՉ հնարված ձևերի
  գնով։ Ընտրիր պարզ հայտնի բառ ճոխ-բայց-հնարված բառի փոխարեն։
- Մինչ ուղարկելը ստուգիր. յուրաքանչյուր անսովոր ածանց, վերջածանց
  կամ բարդություն ունեցող բառը հայտնի՞ ձև է, թե հնարված։
  Կասկածի դեպքում — փոխիր ավելի պարզ բառով։
```

**What this changes structurally:**

1. **New `ՀԻՄՆԱԿԱՆ ԿԱՆՈՆ` ("MAIN RULE")** — an explicit categorical
   "if uncertain, prefer simpler known word over poetic
   invention." This is the broader version of the verb-default
   rule that targets *any* uncertain word, not just verbs.
2. **Forbidden-list extended** with the seven mp2 tokens
   (`կարապված` / `մեղմշխարհն` / `իրեբերում` / `հեռականի` /
   `շտեպնով` / `տերմինները` / `շնորակալությամբ` — that last is a
   typo, not a fabrication, but still belongs in the
   forbidden-output list) plus the v3.2.2 mp1 token `Խտնված` is
   already gone but worth keeping the broader rule.
3. **New `ԲԱՐԴ ԲԱՌԵՐԻ ՀԱՄԱՐ` ("FOR COMPOUND WORDS") sub-rule** —
   targets fabricated compounds specifically. Tells the model:
   if you want to express a compound concept like "soft world,"
   write it as TWO words, not as one merged form. Direct fix
   for `մեղմշխարհն`.
4. **Extended safe-verb whitelist** with the three mp2-replacement
   verbs: `բերում էր`, `շարժվում էր`, `երևում էր`.
5. **Specific mappings table** updated with all seven mp2 token →
   safe replacement mappings + the prior 6 from v3.2.2.

**Diff size estimate:** ~25 new lines / ~140 added Armenian words /
~200 added input tokens per call (≈ 3 % over v3.2.2's ~6500
input-token T1 baseline). Bounded.

### 6.B C1 opener strengthening

Current rule A is a single flat-list bullet. v3.2.3 should add an
explicit "definite-article and inflected variants" clause, plus
list the variants the v3.2.2 mp2 violation surfaced.

**Proposed replacement of rule A** (extending the forbidden list +
adding a structural variant-coverage clause):

```
ԲԱՑՄԱՆ ԿԱՆՈՆ (A — v2 + variant-coverage v3.2.3)
- Մի՛ սկսիր «Մի անգամ», «Մի անգամ, շատ վաղուց», «Մի գեղեցիկ օր»,
  «Մի գեղեցիկ առավոտ», «Շատ վաղուց» տիպի կաղապարով։
- Այս արգելքը ՆԵՐԱՌՈՒՄ Է գրեթե նույնական ձևակերպումները՝
  հոդով, բացականչական մասնիկով կամ փոքր քերականական
  փոփոխությամբ. ՕՐԻՆԱԿ.
    «Մի գեղեցիկ օրը,» ⟵ ԱՐԳԵԼՎԱԾ (հոդով տարբերակ)
    «Մի գեղեցիկ առավոտը,» ⟵ ԱՐԳԵԼՎԱԾ (հոդով տարբերակ)
    «Մի անգամը» ⟵ ԱՐԳԵԼՎԱԾ
    «Այդ մի գեղեցիկ օրը» ⟵ ԱՐԳԵԼՎԱԾ
    «Մի գեղեցիկ ձմեռային օրը» ⟵ ԱՐԳԵԼՎԱԾ (ածական + «մի գեղեցիկ Х օր»)
- Կանոնը կիրառվում է ՄԻԱՅՆ քայլ 1-ի առաջին նախադասության
  համար։ Մի՛ խառնիր օգտատերի «մի օր» / «մի անգամ» հատկորոշիչ
  բառերը պատմվածքի մեջտեղում — դրանք թույլատրված են, եթե իսկապես
  բացում ՉԵՆ նախադասություն ՈՉ էլ սկսում քայլը։
- ԵԹԵ քո առաջին նախադասությունը պարունակում է «մի + ածական +
  ժամանակային բառ» օրինաչափությունը (օր.՝ «մի + Х + օր / առավոտ /
  երեկո / գիշեր»), ՎԵՐԱԳՐԻՐ՝ սկսելով plan.place-ով կամ կերպարով։
```

**What this changes:**

1. **Variant-coverage clause** explicitly names definite-article
   forms (`Մի գեղեցիկ օրը`, `Մի գեղեցիկ առավոտը`), inflected
   forms (`Մի անգամը`), and adjective-extended forms (`Մի գեղեցիկ
   ձմեռային օրը`). Direct fix for the v3.2.2 mp2 PA T1 violation.
2. **Mid-paragraph carve-out** — the rule applies to the first
   sentence of T1, not to mid-story `մի օր` / `մի անգամ`
   connectors. This is a deliberate clarification because the v3.2
   / v3.2.1 / v3.2.2 mp1 caveat ("`Մի օր,` mid-paragraph T1.s2/4
   recurring") is a soft connector, not a forbidden opener. Banning
   mid-paragraph `մի օր` would over-constrain natural Armenian
   narrative.
3. **Pattern-detection self-check** — if the model's first sentence
   has the `մի + adjective + time-word` pattern, it is told to
   rewrite from scratch starting with `plan.place` or a character.

**Diff size estimate:** ~10 new lines / ~80 added Armenian words /
~120 added input tokens per call. Bounded.

### 6.C 429 / paid-run strategy (NOT a prompt change)

This is **explicitly NOT a v3.2.3 prompt-text edit.** It is a
parallel concern that v3.2.3 should document but not solve. The
implementation slice MUST NOT change `Program.cs` or the bake-off
runner. Possible future strategies (separate slices, operator
decision):

| Strategy | Where it lives | Cost / risk |
|---|---|---|
| **Run PD-only mp1** | Operator-side run convention (no code change) | Skips PA half; can't compare PA vs PD on same TPM window. Cheapest. |
| **Add scenario-id selection (`--scenario PD`)** | `Program.cs` change — separate slice | New CLI flag; tool-only edit, no production touch. Allows `--scenario PD` paid runs. |
| **Add `--sleep-between-scenarios <s>` flag** | `Program.cs` change — separate slice | Inserts wait between PA and PD to let TPM window refresh. Tool-only. |
| **Run PA and PD as two separate `--max-prompts 1` invocations** | Operator-side run convention | Two paid runs, two cost gates, but bypasses TPM by design. Probably the simplest and lowest-risk option. |
| **Lower `--max-prompts` per run** | Operator-side, no code change | Default; mp1 always fits. Doesn't help if you need both PA and PD. |
| **Operator-side gpt-4o tier upgrade** | Out of scope for repo | Removes the constraint at the API level. |

**Recommended for the v3.2.3 paid validation strategy** (§ 10
below): start with an mp1 against v3.2.3 (cheap, fits TPM, fast
feedback), then split PA + PD into two separate mp1 invocations
for the load-bearing mp2-equivalent test. **Do not run a single
mp2 again until either scenario-id selection lands or the operator
upgrades the gpt-4o tier.**

### 6.D Keep R3 and R4 mostly as-is

Both rules demonstrably work in v3.2.2:

- **R3 (whole-T1 widening)** — clean PASS on PD T1 mp2. Zero
  schwa drops across all four stem mentions. **Do not rewrite.**
  The only minor clarification worth considering: an explicit
  example showing how `դերանվանական դարձ` ("pronoun fallback")
  applies in the second/third T1 mention. Not critical.
- **R4 (per-band ladder + ceiling guard)** — replicated mid-band
  PA T3 across mp1 + mp2 (both at exactly 82w). **Do not
  over-tighten.** The PD T3 floor remains untested due to 429 —
  but tightening R4 further before that evidence lands would risk
  over-correcting. Wait for v3.2.3 mp2 (or split-run equivalent)
  before any R4 change.

**No drafted text changes for R3 or R4 in v3.2.3.**

---

## 7. What NOT to change

Hard rule for the v3.2.3 implementation slice (when/if approved):

- **No production / runtime change.** `ChatService` frozen.
  `appsettings.json` / production `system-prompt.txt` (sha
  `54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`)
  frozen. backend / frontend / tests / `*.csproj` frozen.
- **No provider switch.** OpenAI / gpt-4o stays.
- **No Story Director runtime integration.**
- **No paid API call** during the implementation slice.
- **No edit to other prompt files.** v3.1 prompt
  (`system-prompt-v3-1.txt`), v3.1 scenarios
  (`bakeoff-prompts-v3-1.json`), Program.cs, README, validators,
  generators — all frozen.
- **No scenario regeneration.** The PA T2 inherited choice-Բ
  defect stays out of scope.
- **No new R-rules.** v3.2.3 extends R2 + rule A only; no R7+.
- **R3 + R4 unchanged.**
- **R5 (no abstract coda) unchanged** — held clean across all
  v3.2-era runs.
- **R6 (resolution staging) unchanged** — partial-success status
  is acceptable.
- **R1 (cross-language) unchanged** — held on PD mp2 (no English
  leak).
- **No `Program.cs` changes** in this prompt-only design slice.
  (Scenario-id selection / sleep-between-scenarios are valid
  separate slices, not part of v3.2.3.)
- **No runtime decoding-temperature change.** Mention only as a
  separate future option (decoding-level fix at the production
  caller), not part of the prompt-only v3.2.3 work.
- **No safety / age-band block change.** Untouched.

---

## 8. Proposed implementation slice

If approved later, mirror the v3.2.2 implementation shape
committed at `768be15` exactly:

1. **Edit only `tools/StoryModelBakeoff/system-prompt-v3-2.txt`**
   with the two textual changes drafted in §§ 6.A and 6.B.
2. **Tag the version inside the file's section headers** as
   `v3.2.3`:
   - `R2 — ... + verb-default v3.2.2 + known-word-only v3.2.3`
   - `A — v2 + variant-coverage v3.2.3`
3. **Author a sibling implementation note** at
   `tools/StoryModelBakeoff/evaluations/v3-2-3-implementation-notes-20260510.md`
   (or `-YYYYMMDD.md` of the day the slice lands) documenting the
   diff + dry-checks + before/after sha pair.
4. **Run no-network dry-checks** (build + 3 dry-runs without
   `--run`) to confirm the edit doesn't break the loader.
5. **No paid call** in the implementation slice. The
   v3.2.2 R2/R3/R4 implementation precedent at `768be15` showed
   this discipline lands cleanly.
6. **Operator decides commit / push.** A subsequent
   operator-gated paid **mp1** smoke against the v3.2.3 prompt
   would be the load-bearing test.

---

## 9. Acceptance criteria for v3.2.3

A v3.2.3 mp1 + (split PA / PD) mp2-equivalent evidence pair would
close the design if:

| Criterion | Plan A | Plan D |
|---|---|---|
| C1 (no forbidden opener — strict + variant) | PASS clean (no `Մի գեղեցիկ օրը,` / `Մի գեղեցիկ առավոտը,` / `Այդ մի գեղեցիկ օրը` / variant) | PASS clean |
| C2 (no moralizing, clean closure) | PASS clean | PASS clean (no patience aphorism) |
| C3 (no duplicate sentence) | PASS | PASS |
| C6 (T1 choices byte-for-byte) | PASS | PASS |
| C8a (T2 first sentence performs SELECTED_CHOICE) | PASS | PASS (verb match) |
| C9 (T3 no choices, no question) | PASS | PASS |
| C13 (T3 closure length) | PASS within 75–95w (band 70–100, no overshoot) | PASS within 105–125w (band 100–130, no underfloor) |
| C14 (no meta-output) | PASS clean | PASS clean |
| C15 (T2 choices byte-for-byte) | PASS | PASS |
| C16 (place anchor + R3 whole-T1) | PASS | PASS (no `կամրջի` anywhere in T1) |
| **R1 (no English leak)** | n/a | PASS |
| **R2 (no fabricated morphology)** | PASS — none of `կարապված` / `Խտնված` / `մեղմշխարհն` family AND no new fabrications | PASS — none of `իրեբերում` / `հեռականի` / `շտեպնով` / `տերմինները` / `խորոտալ` / `համբարձլ` / `փափախերները` / `պարգևիր` family AND no new fabrications |
| **No old orthography** | PASS | PASS (no `ունէր` / `էի` pre-reform forms) |
| **No typos** | PASS — none of `շնորակալությամբ` family | PASS |

**Stretch criteria (nice-to-have, not blocking):**

- T2 closure-length on both plans within their respective bands.
- PA T1 length not under-shooting the floor (a minor v3.2.2 mp1
  pattern that v3.2.2 mp2 partly fixed).
- R6 resolution staging shifts from retrospective naming toward
  discrete in-scene gift-moment.

**Non-criteria (explicitly out of scope for v3.2.3):**

- PA T2 choice-Բ scenario defect (inherited from
  `bakeoff-prompts-v3-1.json`, not a prompt issue).
- Multi-sample variance per scenario.
- HTTP 429 on a single mp2 (handled by the run strategy in § 10,
  not by prompt design).

---

## 10. Validation strategy after implementation

**No paid call in this design slice.** After a future approved
implementation slice lands the v3.2.3 prompt edits:

1. **Dry-run only first.** `dotnet build tools/StoryModelBakeoff` +
   three `dotnet run … --provider openai/claude --max-prompts 1/2`
   without `--run` to confirm the prompt loads cleanly. Same shape
   as the v3.2.2 implementation slice's dry-checks.
2. **Then one paid OpenAI v3.2.3 mp1 (PA only).** Cheapest single
   paid validation; fits comfortably under the gpt-4o 30k-TPM
   tier (≈19k input tokens total, see v3.2.2 mp1 evidence).
   Load-bearing test for: R2 known-word-only rule (PA), C1 variant
   coverage (PA T1 opener), R4 ceiling guard regression check.
3. **PD-focused strategy to avoid TPM:**
   - **Option A (recommended): two separate paid mp1 runs.** First
     PA-only mp1 (current default), then a separate PD-only mp1
     after the TPM window resets (~60s). Bypasses the cumulative-
     window problem by design.
   - **Option B: scenario-id selection in `Program.cs`.** Add a
     `--scenario <id>` CLI flag in a separate tool-only slice;
     then run `--scenario PD --max-prompts 1`. Cleaner long-term;
     requires a pre-paid-validation slice.
   - **Option C: `--sleep-between-scenarios <s>` flag in
     `Program.cs`.** Insert a configurable wait between PA and PD
     in a single mp2 run. Also tool-only, also separate slice.
4. **Do not run full mp2 against v3.2.3 until the 429 strategy
   exists.** A blind retry would have a high probability of
   recreating the v3.2.2 mp2 partial-failure pattern.
5. **Cost forecast** for the recommended sequence (mp1 PA → mp1
   PD): ≈ $0.04–$0.06 total. Bounded.

---

## 11. Risks

### 11.1 Strong R2 rule may flatten fairy-tale style

The "MAIN RULE" + extended forbidden list + compound-word
sub-rule + 14-token replacement table together comprise ~25 added
lines of R2 instruction load. The model may interpret this as a
broad "use only safe, plain words" mandate and produce flatter,
less atmospheric prose — particularly on PD's `age-7-richer` band
which permits "թեթև բանաստեղծականություն."

**Mitigation:** the new `ՀԻՄՆԱԿԱՆ ԿԱՆՈՆ` is gated on uncertainty
("if a word sounds newly coined or uncertain"), not on poeticism
in general. The age-7 caveat clause from v3.2.2 R2 is preserved.
Watch v3.2.3 PD T1+T2 register on the first paid sample; if
flattening appears, soften the wording from "ՓՈԽԱՐԻՆԻՐ" to
"ԱՎԵԼԻ ԼԱՎ Է" in a v3.2.4 follow-up.

### 11.2 Strong C1 ban may over-constrain natural phrasing

The variant-coverage clause names `Մի գեղեցիկ + Х + օր / առավոտ /
երեկո / գիշեր` as a forbidden pattern. A natural Armenian fairy
tale sometimes does open with `Մի ձմեռային օր` ("On a winter
day") — not on rule A's named list, but adjacent. The model may
over-apply the variant-coverage clause and avoid even legitimate
time-anchored openers.

**Mitigation:** the rule explicitly carves out mid-paragraph
`մի օր` / `մի անգամ` connectors. The pattern-detection
self-check is gated on `մի + adjective + time-word` AT THE FIRST
SENTENCE, not anywhere. PA's natural openers `Խնձորենու այգում
...` (places-anchored) and PD's `Հին կամուրջի վրա ...`
(places-anchored) remain unconstrained. Watch v3.2.3 first-paid
sample for opener naturalness; if the model produces stilted
"places-anchored only" openers, soften the variant-coverage
clause.

### 11.3 More prompt text → instruction overload

v3.2 prompt is ~220 lines (≈6500 input tokens at T1). v3.2.2
added ~37 structural lines. v3.2.3 would add ~35 more. Total
prompt growth ~10 % over v3.2 baseline. The model may reach a
point where later rules get less attention than earlier ones —
the C1 regression on v3.2.2 mp2 may already be a symptom of this.

**Mitigation:** v3.2.3 changes are surgical extensions of
existing rules (R2 + A), not new rule blocks. No new rule numbers
(R7+). Each change reuses the existing rule's vocabulary and
register. If a later run shows broader "later rules ignored"
symptoms, the next slice should be **rule-consolidation** (merge
overlapping content), not more example coverage.

### 11.4 Model may invent new fake words anyway

R2's named-exemplar approach has caught named families (mp2's
four named tokens did not recur) but not categorically prevented
fabrication. v3.2.3 adds a broader categorical "if uncertain →
known word" rule, but the model is statistical — it may still
invent novel forms under sampling variance.

**Mitigation:** v3.2.3 is the next safe step, not the last word.
A future v3.3+ may need to consider decoding-level fixes
(temperature 0.7 → 0.3 in the production caller). That is a
production-runtime concern, not a prompt-text concern; it is
explicitly out of scope for v3.2.3.

### 11.5 PD T3 remains untested until rate-limit strategy is solved

The single biggest evidence gap from v3.2.2 mp2 is PD T3 (R4
floor + R2 PD T3 + R6 PD staging). v3.2.3 prompt design CANNOT
solve this — it is an API-strategy issue (§ 6.C). If the operator
proceeds with v3.2.3 prompt design + mp1 paid validation but
without a PD-equivalent path, the C13 PD floor question stays
open indefinitely.

**Mitigation:** § 10 recommends two separate paid mp1 runs (PA
then PD) as the simplest path. Operator-side decision; can be
done immediately without any code change.

### 11.6 PA inherited scenario defect persists

The PA T2 choice-Բ presupposing-resolution defect is inherited
from `bakeoff-prompts-v3-1.json` and reproduces across every
paid run. v3.2.3 cannot fix it. **Out of scope; flagged for
awareness.**

---

## 12. Recommendation

**Proceed with v3.2.3 design → implementation → dry-run → mp1
paid validation, in that order.**

1. **Approve this design plan** (operator review).
2. **Implementation slice** (separate operator-gated request):
   single text-only edit to `system-prompt-v3-2.txt` per §§ 6.A
   and 6.B; sibling implementation note; dry-checks only; commit
   prompt-only edit + note. **No paid call in implementation.**
3. **Operator-gated v3.2.3 mp1 paid validation** (PA only,
   ≈$0.02). Load-bearing for R2 known-word-only + C1
   variant-coverage on PA.
4. **Before any PD validation**, solve the TPM strategy per § 6.C.
   Recommended: two separate paid mp1 runs (PA mp1 + PD-only
   equivalent), spaced apart enough to clear the TPM window. Do
   not run a single mp2 against v3.2.3 until that is in place.
5. **Do not run more paid runs against v3.2.2.** The mp2 partial
   evidence is sufficient to drive the v3.2.3 design; another v3.2.2
   mp2 attempt would only re-confirm the C1 regression and the R2
   leak, and risks another 429 without producing new signal.

**No paid call. No production change. No commit. No push.** This
plan is the design artifact only.

---

## 13. No secrets / scope guard

This file contains no API key, no `OPENAI_API_KEY` value, no
token, no bearer credential, no parent JWT, no device API key,
and no private endpoint. No paid run was issued by this slice. No
`OPENAI_API_KEY` env var was read.

No production / runtime files are touched by this slice or by any
implementation slice that lands the v3.2.3 changes:

- `ChatService` — frozen.
- backend code (every project) — frozen.
- frontend (`wwwroot/*.html`) — frozen.
- `appsettings*.json` — frozen.
- `*.csproj` — frozen.
- tests — frozen.
- seed bank, name bank — frozen.
- generators, validators — frozen.
- `Program.cs` — frozen (scenario-id selection / sleep flags are
  separate slice).
- `README.md` — frozen.
- runtime system prompt (production sha
  `54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`)
  — frozen.
- v3.1 system prompt (`system-prompt-v3-1.txt`) — frozen.
- v3.1 scenarios (`bakeoff-prompts-v3-1.json` sha
  `e6cdba77d64640c89dc6aa094108f3bff040a26044af1b1cc75f5a7ab0f89b59`)
  — frozen.
- speech / TTS / STT / hardware / firmware — frozen.

Only `tools/StoryModelBakeoff/system-prompt-v3-2.txt` would be
touched by a future implementation slice — and only with the two
textual edits drafted in §§ 6.A and 6.B, gated on operator
approval.

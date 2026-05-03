# Writer prompt v3.1 — hardening notes (2026-05-04)

**Status:** evidence / design note only. **No production code change.**
No `ChatService` change. No runtime prompt change. No provider switch.
No new model / API call. No seed-bank, character-name-bank, generator,
or validator change. The deliverable is this file plus the companion
v3.1 capture package
[`./writer-prompt-v3-1-plan-a-capture-20260504.md`](./writer-prompt-v3-1-plan-a-capture-20260504.md).

**Companion files:**
- [`./writer-prompt-v3-bounded-arc-notes-20260503.md`](./writer-prompt-v3-bounded-arc-notes-20260503.md) — v3 rule proposal (bounded arc + closure).
- [`./writer-prompt-v3-plan-a-capture-20260503.md`](./writer-prompt-v3-plan-a-capture-20260503.md) — the v3 Plan A capture whose § 8a verdict is the input for this slice.

---

## 1. Purpose

The v3 Plan A capture (Claude.app, 2026-05-04) **proved the
bounded-arc rule can work**: C9 PASS — Turn 3 emitted no choice
block, the story ended cleanly. v3 is therefore a real
improvement over v2 on the most serious issue.

But v3 also surfaced four concrete weaknesses that block API /
runtime testing:

1. **C8b FAIL** — Turn 2 ignored the BREAK-GLASS choice block
   and invented unrelated choices.
2. **C14 FAIL** (new check) — Turn 2 leaked an internal
   meta-output line into user-facing prose.
3. **C13 FAIL** — Turn 3 closure ran ~155 words vs the 70–110
   target (~50% over budget).
4. **C5 variance** — Turn 1 place anchor is unreliable; Hayk
   noted at least one earlier attempt drifted from
   `plan.place` to a generic location.
5. **C3 FAIL** — Claude.app duplicated-sentence-trio artefact
   still appears on continuations. Treated as UI-side pending
   API confirmation; v3.1 keeps the rule but does not block on
   it.

v3.1 is the smallest prompt-only change set that addresses
items 1–4 without touching the bounded-arc rule that is already
working.

This is **not** a production change request and **not** a
runtime provider decision. The v3.1 prompt lives in the
StoryModelBakeoff capture flow only. ChatService and
`system-prompt.txt` stay unaffected.

---

## 2. Evidence summary from v3 Plan A capture

Source:
[`./writer-prompt-v3-plan-a-capture-20260503.md`](./writer-prompt-v3-plan-a-capture-20260503.md)
§ 7A / § 7B / § 7C, with § 8a as the consolidated verdict.

| Check | v3 result | What it tells v3.1 |
|---|---|---|
| **C9** — final turn no choice block | **PASS** | Bounded-arc rule is sound. v3.1 does not change § 4 / § 5 of v3 notes. |
| **C8b** — Turn 2 BREAK-GLASS exact choices | **FAIL** | v3's wording was not strong enough. v3.1 needs explicit "copy byte-for-byte" + positive / negative examples. |
| **C14** — no meta-output (NEW) | **FAIL** | v3 had no rule against meta. v3.1 needs an explicit forbidden-string list with positive output example. |
| **C13** — Turn 3 closure 70–110 words | **FAIL (~155 w)** | Closure budget needs to be tighter and reinforced with "do not continue into new micro-events." |
| **C5** — Turn 1 plan adherence (place anchor) | **PASS this run, variance noted** | Earlier v3 attempt drifted to `կանաչ բացատ`; v3.1 needs first-sentence-must-include-plan.place. |
| **C3** — no duplicate sentence within turn | **FAIL on Turns 2 + 3** | Claude.app UI artefact, deferred to API confirmation. v3.1 keeps a rule but does not gate on it. |
| **C1** — no `Մի անգամ` opener | **PASS** | Rule A held. v3.1 keeps. |
| **C2** — no moralizing | **PASS** | Rule C held. v3.1 keeps. |
| **C7** — Turn 1 length 90–130 | **PASS** (~110 words) | v3.1 keeps. |
| **C8a** / **C10** — first sentence performs SELECTED_CHOICE | **PASS** (Turn 2 + Turn 3) | Rule F held. v3.1 keeps. |
| **C11** — smallProblem resolved on closure turn | **PASS** | v3.1 keeps. |
| **C12** — closure ends naturally / `Վերջ։` | **PASS** | v3.1 keeps. |

The four FAILs (C8b / C14 / C13 / C5-variance) are what § 3
below addresses. Everything else stays.

---

## 3. v3.1 rule changes

Five additive rule blocks. None of v3's existing rules are
removed; v3.1 = v3 ∪ this section.

### A. Turn 2 exact-choice contract (fixes C8b FAIL)

When the operator-supplied prompt contains a **BREAK-GLASS
CHOICE BLOCK** with two literal choice lines, the model MUST
copy those two lines into the turn's choice block **byte-for-
byte**.

**Forbidden:**

- Inventing different choice phrasings, even if "more natural"
  or "more interesting."
- Renaming Ա → Բ or Բ → Ա (reordering).
- Paraphrasing while preserving meaning.
- Adding a third choice (Գ:) or removing one of the two.
- Wrapping the choice in commentary ("Ընտրիր մեկը...:" before
  the lines).

**Required wording (Eastern Armenian instruction block):**

```text
ԵԹԵ այս հուշակցում տրված է BREAK-GLASS CHOICE BLOCK երկու
տողով, ապա ՊԱՐՏԱԴԻՐ ՊԵՏՔ Է կրկնօրինակել այդ երկու տողերը
ՃՇՏՈՐԵՆ բառացիորեն (byte-for-byte): ՉԻ ԹՈՒՅԼԱՏՐՎՈՒՄ.
- հորինել տարբեր ընտրություններ
- վերանվանել, վերադասավորել, պարաֆրազել
- ավելացնել երրորդ ընտրություն
- տողերից առաջ կամ հետո բացատրություն դնել
ԵԹԵ ԿԱՍԿԱԾՈՒՄ ԵՍ՝ բառացի կրկնօրինակիր:
```

**Positive example** (what the model must emit on the v3 Plan A
Turn 2 path):

```
Correct:
Ա: ուղեկցել արագիլին մինչև երկնքի եզրը
Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն
```

**Negative example** (what v3 actually emitted — must not
recur):

```
Incorrect (v3 invented these — DO NOT DO THIS):
Ա: հետևել դեղին թիթեռին
Բ: քայլել փոքրիկ արահետով
```

The positive + negative pair is load-bearing. v3 had only a
prose instruction; v3.1 adds the explicit example pair so the
model has a worked target.

### B. Anti-meta-output rule (fixes C14 FAIL — NEW)

The model MUST output **only** Armenian story prose and, when
required, the exact choice lines. Nothing else.

**Forbidden meta-output strings** (any substring match):

- `Շարունակեց հեքիաթը...`
- `Շարունակություն...`
- `Շարունակիր...` (when emitted by the model, not the child)
- `Continued...`, `Continuation:...`
- `Note:` / `Նշում:`
- `As an AI…`
- `(narrator commentary)`-style parentheticals
- Any explanation of what the model is doing
- Any narrator-commentary outside the story prose

**Required wording (Eastern Armenian instruction block):**

```text
ԱՆՀՐԱԺԵՇՏ Է. ԵԼՔԸ ՊԱՐՈՒՆԱԿՈՒՄ Է ՄԻԱՅՆ.
1. հայերեն հեքիաթային արձակը (պատմությունը),
2. և, երբ պահանջվում է, ճշգրիտ ընտրությունների տողերը:
ՉԻ ԹՈՒՅԼԱՏՐՎՈՒՄ ԵԼՔՈՒՄ ՈՐԵՎԷ ՏԵՂ.
- «Շարունակեց հեքիաթը...», «Շարունակություն...»
- «Continued...», «Continuation:...»
- «Note:», «Նշում:», «As an AI...»
- մոդելի կողմից բացատրություն, թե ինչ է անում
- հեքիաթից դուրս՝ պատմողի մեկնաբանություն
- փակագծային մետա-նշում (narrator commentary)
```

**Positive output example** (clean Turn 2 ending — no meta
suffix anywhere):

```
...
Ա: ուղեկցել արագիլին մինչև երկնքի եզրը
Բ: մնալ այգում և նայել, թե ինչպես է արագիլը թռչում տուն
```

(Output ends at the second choice line. Nothing after.)

**Negative output example** (v3 leak — must not recur):

```
...rainbow scene...Շարունակեց հեքիաթը՝ ստեղծելով կախարդական պահ և նոր ընտրանքներ։Շարունակեց հեքիաթը՝ ստեղծելով կախարդական պահ և նոր ընտրանքներ։...rainbow scene again...
```

This becomes the new **C14** check (§ 4).

### C. Place-anchor hardening (fixes C5 variance)

The **first sentence of Turn 1** MUST include the literal
`plan.place` string OR a directly inflected form (locative,
genitive, etc., where the stem `plan.place` minus its
nominative suffix is present).

For Plan A:

- `plan.place = "խնձորենու այգի"`.
- Required substring: **`խնձորենու այգ`** (covers
  `խնձորենու այգի` nominative, `խնձորենու այգում` locative,
  `խնձորենու այգին` definite, `խնձորենու այգուց` ablative,
  etc.).

**Forbidden Turn-1 opening locations** (for Plan A
specifically):

- `կանաչ բացատ`
- `անտառ`
- `դաշտ`
- `սար`
- `մարգագետին`
- any place not derivable from `plan.place`.

**Required wording (Eastern Armenian instruction block):**

```text
ՔԱՅԼ 1-Ի ԱՌԱՋԻՆ ՆԱԽԱԴԱՍՈՒԹՅՈՒՆԸ ՊԱՐՏԱԴԻՐ ՊԵՏՔ Է ՊԱՐՈՒՆԱԿԻ
plan.place-ի բառային հիմքը (օրինակ՝ «խնձորենու այգ» — ընդգրկում
է «խնձորենու այգի», «խնձորենու այգում», «խնձորենու այգին»,
«խնձորենու այգուց», և այլն):
ԱՐԳԵԼՎՈՒՄ Է բացել ուրիշ վայրով՝ «կանաչ բացատ», «անտառ», «դաշտ»,
«սար», «մարգագետին», կամ որևէ վայր, որ չի բխում plan.place-ից:
```

This becomes the new **C16** check (§ 4).

### D. Closure length hardening (fixes C13 FAIL)

Turn 3 closure for **age-4** must be **70–100 Armenian words**
(tightened from v3's 70–110 ceiling — an ~10% trim, plus an
explicit "no new micro-events" guard).

**Forbidden after smallProblem resolution:**

- New dream sequences ("շնիկը երազում տեսավ...").
- New fruit-sharing or gift exchanges beyond the one already
  implied by `plan.resolutionStyle`.
- New walks ("շնիկը գնաց...").
- "Ask the child for another story" hooks.
- Any new sub-arc that opens after the resolution beat.

**Required wording (Eastern Armenian instruction block):**

```text
ՓԱԿՈՒՄ — ՔԱՅԼ 3 (age-4):
- Թիրախ՝ 70–100 հայերեն բառ ՃՇՏՈՐԵՆ:
- ՀԵՆՑ smallProblem-ը լուծվում է, ՎԵՐՋԱՆՈՒՄ ԵՍ:
- ՉԻ ԹՈՒՅԼԱՏՐՎՈՒՄ. նոր երազ, նոր նվեր / պտուղ-կիսել, նոր
  զբոսանք, «Արի՛ ուրիշ պատմություն ասեմ» հատված:
- Ավարտել կա՛մ բնական պատմողական վերջին նախադասությամբ, կա՛մ
  առանձին տող «Վերջ։» բառով:
```

C13 stays as a check; the budget tightens to **70–100 words**.

### E. Duplicate sentence guard (keeps C3, defers fix to API)

Keep the rule from v3 but soften the expectation:

- Add the instruction `Մի նախադասությունը ՉԿՐԿՆԵԼ բառացի մեկ
  քայլի ներսում:` to every turn's prompt body.
- **But** continue to treat C3 FAIL on Claude.app as a UI-side
  artefact pending API confirmation.
- The v3.1 capture will still likely show the duplicated-
  sentence-trio at continuation openings. **This does not
  block** v3.1 acceptance; only the API run resolves the bug
  class.

No new check number for this — C3 stays.

---

## 4. Updated acceptance checks

v3.1 inherits C1–C13 from v3 and adds three new checks. The
load-bearing claim is still **C9** (final turn no choice
block). C14 / C15 / C16 are the three new gates v3.1 must
clear before it becomes the preferred writer-prompt candidate
for API testing.

| # | Check | Source rule | Applies to |
|---|---|---|---|
| C1 | No forbidden opener | v2 rule A | Turn 1 |
| C2 | No moralizing dialogue | v2 rule C | All turns |
| C3 | No duplicate sentence within turn | v2 rule F | All turns (deferred — § 3.E) |
| C4 | Age-4 simple language | v2 rules D + E | All turns |
| C5 | Plan adherence (atoms visible) | v2 rule G | All turns |
| C6 | Exact `Ա: ` / `Բ: ` choices verbatim from plan | v2 rule B | Turn 1 |
| C7 | Length 90–130 words | v2 rule D | Turn 1 |
| C8a | First sentence performs SELECTED_CHOICE | v2 rule F | Turns 2 + 3 |
| C8b | Two choices in exact format with right semantic directions | v3 § 4 | Turn 2 |
| C8c | Length 70–110 words | v2 rule D | Turn 2 |
| **C9** | **Turn 3 contains NO choice block** (load-bearing) | v3 § 4 / § 5 | Turn 3 |
| C10 | First sentence performs SELECTED_CHOICE | v2 rule F | Turn 3 |
| C11 | smallProblem resolved within turn | v3 § 4 | Turn 3 |
| C12 | Ends in natural last sentence or `Վերջ։` | v3 § 5 | Turn 3 |
| C13 | Length 70–100 words (tightened from v3) | **v3.1 § 3.D** | Turn 3 |
| **C14** | **No meta-output line** | **v3.1 § 3.B (NEW)** | All turns |
| **C15** | **Turn 2 copies BREAK-GLASS choices byte-for-byte** | **v3.1 § 3.A (NEW)** | Turn 2 |
| **C16** | **Turn 1 first sentence includes `plan.place` stem** | **v3.1 § 3.C (NEW)** | Turn 1 |

C8b is **redundant** with the stricter C15 once a BREAK-GLASS
block is supplied — C15 supersedes it on Turn 2. C8b stays in
the table as a softer fallback for plans / paths that do NOT
ship a BREAK-GLASS block.

A v3.1 capture passes iff **every** check marks pass on its
applicable turns. C9 is the strictest — its failure still
routes the same way as in v3 § 9 branch 3.

---

## 5. Decision

Recommendation:

1. **Run the v3.1 Plan A capture** in Claude.app, per the
   companion package
   [`./writer-prompt-v3-1-plan-a-capture-20260504.md`](./writer-prompt-v3-1-plan-a-capture-20260504.md).
2. **If C9 still passes AND C14 / C15 / C16 all pass**, v3.1
   becomes the **preferred writer-prompt candidate for API
   testing**. The API run is the load-bearing follow-up for
   the C3 duplicated-sentence-trio question, which v3.1 does
   not attempt to fix at the prompt layer.
3. **If C15 fails again** (Turn 2 still invents choices
   despite the byte-for-byte instruction + positive / negative
   examples), the next iteration is to drop the BREAK-GLASS
   approach for Turn 2 and accept that Turn 2 choices are
   model-generated, then build a *post-hoc choice
   normalizer* on the operator side instead.
4. **If C14 fails again** (meta-output still leaks despite
   the explicit forbidden-string list), the next iteration is
   to add a positive output-format example showing the
   model exactly what the last line of a turn must look like
   (e.g. "the last line MUST be `Բ: ...`, not commentary,
   not `Շարունակեց...`").
5. **If C16 fails** (Turn 1 still drifts from `plan.place`),
   add a positive output example showing a Turn 1 first
   sentence that includes `խնձորենու այգ`, paired with a
   negative example showing `կանաչ բացատ` opening forbidden.
6. **No production / runtime change is gated on this slice.**
   ChatService, `system-prompt.txt`, and the production model
   selection stay as they are. v3.1 lives in the
   StoryModelBakeoff capture flow only.
7. **API comparison still load-bearing later.** C3 (duplicate
   sentence) and the variance / consistency questions only
   resolve over the API path, where decoding parameters are
   controllable and the UI-side artefact does not exist.

---

## 6. Out of scope for this note

- No edits to `tools/StoryModelBakeoff/system-prompt.txt`.
- No edits to `tools/StoryModelBakeoff/bakeoff-prompts.json`.
- No edits to `tools/StoryModelBakeoff/story-seed-bank.v1.json`.
- No edits to `tools/StoryModelBakeoff/story-character-names.v1.json`.
- No edits to `generate-story-plan.js`, `validate-story-plan.js`,
  `validate-seed-bank.js`, or `validate-character-names.js`.
- No edits to existing v1 / v2 / v3 capture files. v3.1 is
  additive evidence; the v3 capture stays as the historical
  record of what the un-hardened v3 produced.
- No production runtime changes (`backend/**`).
- No new provider integration, API call, or live model run.
- No Plan D v3.1 capture in this slice — Plan A is the
  hardening test bed; Plan D follows once Plan A v3.1 clears
  the new gates.
- No v3.1 → v3.2 design at this point — wait for v3.1
  capture evidence before proposing further iterations.

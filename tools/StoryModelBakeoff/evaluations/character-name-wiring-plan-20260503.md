# Character-name wiring plan — design only (2026-05-03)

**Status:** design / evidence only. **No code change in this slice.**
No `generate-story-plan.js` change. No `validate-story-plan.js`
change. No production runtime change. No `ChatService` change.

The deliverable is this document. A subsequent slice may implement
the wiring described in § 4 + § 5 below behind an opt-in
`--with-names` flag; that slice is **not** scheduled by this note.

**Companion files:**
- [`../story-character-names.v1.json`](../story-character-names.v1.json) — the existing 47-animal name bank.
- [`../validate-character-names.js`](../validate-character-names.js) — the existing name-bank validator.
- [`../generate-story-plan.js`](../generate-story-plan.js) — the generator that today emits 17-field plans without names.
- [`../validate-story-plan.js`](../validate-story-plan.js) — the Plan Gate validator that today rejects extra fields.
- [`./writer-prompt-v2-first-capture-20260503.md`](./writer-prompt-v2-first-capture-20260503.md) — the v2 capture package whose plans deliberately omit names today (this design feeds its successor).

---

## 1. Current state

- `tools/StoryModelBakeoff/story-character-names.v1.json` exists,
  with 47/47 seed-bank-animal coverage and 5 names per animal
  (cat + dog at 6) plus 8 `sharedNames` (`Պուճո`, `Լոլո`,
  `Չալո`, `Տիտո`, `Բոժո`, `Պիստակ`, `Պաչո`, `Նոնո`).
- `tools/StoryModelBakeoff/validate-character-names.js` exists and
  passes against the current bank.
- `tools/StoryModelBakeoff/generate-story-plan.js` does **not**
  consume the bank today. Plans emit 17 fields (`hero`,
  `heroTrait`, `friendOrGuide`, `relationship`, `place`, `mood`,
  `magicalObject`, `smallProblem`, `conflictType`, `goal`,
  `resolutionStyle`, `sensoryDetails`, `ageToneProfile`,
  `choiceAType`, `choiceBType`, `choiceA`, `choiceB`).
  No `heroName` / `friendOrGuideName`.
- `tools/StoryModelBakeoff/validate-story-plan.js` rejects extra
  fields today via its required-shape check, so simply *adding*
  `heroName` to a plan would FAIL the Plan Gate. Wiring needs
  matched edits on both sides.
- The writer prompt currently asks the model to invent names
  on the fly. v1 four-profile captures observed:
  - **Plan A** → "Բարիկ" (assigned to the hero շնիկ).
  - **Plan B** → "Ճվիկ" / "Թևիկ" (cricket + dragonfly hero/friend).
  - **Plan C** → "Ճվիկ" / "Մռնչիկ" (small bird + ant — the ant
    name does not match either animal naturally).
  - **Plan D** → "Շվշվիկ" / "Մռնչիկ" (swallow + cat — neither
    name aligns with the bank's nickname-style direction).
  Across the four captures the writer **always** invented names,
  none drawn from any bank, and one of them (Plan C's "Մռնչիկ"
  for an ant) was a poor fit for the animal. This is the gap
  the wiring closes.

---

## 2. Desired plan shape

Add **two optional** string fields. Position them right after
the animal they name, so a human reader scans hero ↔ name
together:

```json
{
  "hero": "շնիկ",
  "heroName": "Չալիկ",
  "friendOrGuide": "շուն",
  "friendOrGuideName": "Չալո",
  "relationship": "տատիկը պատմում է հին պատմություն",
  "place": "խնձորենու այգի",
  ...
}
```

Both fields are **optional** at the wire-shape level — a
17-field plan that omits them must remain valid. Backwards
compatibility is the constraint; existing committed plan
files (`generated-plans-age-*-20260501.json`) must continue
to pass the Plan Gate after the validator update.

The fields are **strings**, not objects — no nicknames /
declensions / aliases. A future slice may extend to
`heroName: { nominative, vocative }` if Armenian declension
becomes load-bearing in the writer prompt; today it is not.

---

## 3. Rules

### 3.1 Source

- `heroName` MUST come from
  `story-character-names.v1.json → animalNames[hero]`, OR from
  `sharedNames` as a fallback when `animalNames[hero]` is
  missing or empty.
- `friendOrGuideName` MUST come from
  `animalNames[friendOrGuide]`, OR from `sharedNames` as a
  fallback when `animalNames[friendOrGuide]` is missing or
  empty.
- A `heroName` drawn from `animalNames[friendOrGuide]` is
  **not** allowed (cross-animal pulls are an integrity bug —
  if the hedgehog is named "Մուշո", that name must come from
  the hedgehog's list, not the squirrel's).
- `sharedNames` is the **only** legitimate cross-animal source;
  it is exactly the bank's documented "names usable across
  many small animals" pool.
- No writer-model invention when the name bank is active.
  Once a plan carries `heroName` / `friendOrGuideName`, the
  writer prompt must **preserve** those strings verbatim
  (rule G in `writer-prompt-tightening-notes-20260503.md` § 3).

### 3.2 Distinctness

- `heroName` and `friendOrGuideName` MUST NOT be equal in the
  same plan. A story with a hedgehog named "Մուշո" and a
  rabbit also named "Մուշո" is confusing.
- If a random draw produces `heroName === friendOrGuideName`,
  redraw the *friend* name once from
  `animalNames[friendOrGuide]`.
- If still equal after one redraw (small per-animal lists +
  shared overlap can produce repeated collisions), draw the
  friend name from `sharedNames`, **excluding** any name equal
  to `heroName`.
- If `sharedNames` minus `heroName` is empty (vanishingly
  unlikely with 8 entries), exit the generator with a clear
  error: `name-collision: heroName "X" + friendOrGuideName
  pool exhausted`. **Do not** append a numeric suffix
  ("Մուշո2"); fail loudly so the operator can edit the bank.

### 3.3 Determinism

- The same `--seed N` value MUST produce the same `heroName`
  / `friendOrGuideName` choices across runs, **regardless**
  of whether `--age-profile` is set.
- Names are drawn from the **same** RNG instance the generator
  uses for the rest of the plan. There is no separate
  `--name-seed` parameter today (see § 4 below — proposed and
  rejected).
- Adding the name draws to the RNG sequence changes the RNG
  state for downstream draws within the same plan. That means
  `--seed 123 --with-names` and `--seed 123` will produce
  plans whose **non-name** fields differ. This is acceptable
  — `--with-names` is opt-in, and the determinism guarantee
  is *within a flag set*, not *across flag sets*.

### 3.4 Validity

- `heroName` / `friendOrGuideName` MUST be non-empty strings
  after `trim()`.
- Whitespace-only / null / boolean / numeric values are
  rejected by the Plan Gate.
- The Plan Gate does **not** check
  `animalNames[hero]` membership for `heroName` (that would
  duplicate work the generator already does); it only checks
  *some* legitimate source — i.e. membership in
  `animalNames[hero] ∪ sharedNames`. Same for `friendOrGuideName`.
- Cross-animal pulls fail the Plan Gate even if the source
  exists in the bank — see § 5.2 below.

---

## 4. CLI design

### 4.1 Proposed flag

```
node tools/StoryModelBakeoff/generate-story-plan.js --with-names
```

- Default behavior: **unchanged**. No flag → no name fields →
  17-field plan, identical to today's output.
- With flag: each plan in the output array carries
  `heroName` and `friendOrGuideName`.
- Combines with existing `--count`, `--seed`, and
  `--age-profile` flags.
- Examples:
  ```
  node tools/StoryModelBakeoff/generate-story-plan.js --with-names
  node tools/StoryModelBakeoff/generate-story-plan.js --count 5 --seed 123 --with-names
  node tools/StoryModelBakeoff/generate-story-plan.js \
    --count 3 --seed 123 --age-profile age-5-balanced --with-names
  ```

### 4.2 Considered and rejected: `--name-seed`

A separate `--name-seed N` parameter would let the operator
pin name choices independently of plan choices ("same plans,
different names"). Rejected for v1 wiring because:

1. The use case is speculative — no observed need across the
   four captured renders.
2. It introduces two RNG streams, which is extra surface to
   document, test, and explain.
3. If it is ever needed, it is purely additive — a future
   slice can add `--name-seed` without breaking the
   `--with-names` contract above.

### 4.3 Considered and rejected: making `--with-names` the default

Making name emission the default would silently change every
existing dry-run pipeline that consumes the generator's JSON
output (e.g. the Plan Gate over the committed
`generated-plans-age-*-20260501.json` — those files were
captured before names existed and would not magically gain
them). Opt-in keeps the existing surface stable.

---

## 5. Validator update design

### 5.1 Optional fields

`validate-story-plan.js`'s required-fields list stays at the
17 existing entries. `heroName` and `friendOrGuideName`
become **optional** — checked only if present.

The existing "no extra fields" posture stays for any *unknown*
field; only these two named fields are whitelisted as
optional additions.

### 5.2 Per-field checks (when present)

For each plan:

1. If `heroName` is present:
   - Type check: must be a string.
   - Trim check: must be non-empty after `trim()`.
   - Membership check: must appear in
     `animalNames[plan.hero] ∪ sharedNames`. This is the only
     legitimate source set per § 3.1.
2. If `friendOrGuideName` is present:
   - Type check: must be a string.
   - Trim check: must be non-empty after `trim()`.
   - Membership check: must appear in
     `animalNames[plan.friendOrGuide] ∪ sharedNames`.
3. Cross-field check: if **both** fields are present,
   `heroName !== friendOrGuideName` (§ 3.2).
4. **Half-state is allowed**: a plan with only `heroName` (no
   friend name) or only `friendOrGuideName` (no hero name)
   passes — the plan generator should never emit half-state,
   but the validator is permissive enough that hand-edited
   research plans don't fail on technicalities.

### 5.3 Bank loading

`validate-story-plan.js` today loads the seed bank but not
the character name bank. With the wiring update it loads
both. Same path-resolution pattern (`__dirname`-relative).
If the name bank is **missing on disk**:

- A plan with neither name field present continues to PASS
  (back-compat — old plan files have no names).
- A plan with at least one name field present FAILS with a
  clear error: `name-bank not found at <path>; cannot
  validate heroName / friendOrGuideName`.

### 5.4 Bank malformed

If the name bank exists but fails its own validator
(`validate-character-names.js` would FAIL), the plan
validator does **not** re-validate the bank — it trusts the
bank-validator as the source of truth and runs membership
checks against whatever shape it finds. If the bank is so
malformed that `animalNames` is missing entirely, the plan
validator falls back to "name fields present → FAIL with
clear error message."

---

## 6. Risks

1. **Tone of names.** The 2026-05-03 nickname-style refresh
   moved the bank toward folk forms (`Չալո`, `Բոժո`, `Պուճո`).
   Some animals carry names that lean too whimsical for a
   given plan's `mood` (`Քրքո` for an aղավնի in a calm
   bedtime mood reads OK; for a `փոքրիկ արկածային` mood it
   may read too cute). **Mitigation:** wiring is opt-in; the
   v2 capture package (`writer-prompt-v2-first-capture-`) does
   not use names yet. After Hayk's native review (see
   `character-name-native-review-20260503.md` companion
   slice) trims the bank, the wiring slice can land.
2. **Name overuse across plans.** With small per-animal lists
   (5 names each), a 30-plan batch will repeat names heavily —
   e.g. a hedgehog batch will see `Փշո` / `Թմբո` cycle every
   ~5 plans. This is fine for evidence purposes (the variety
   that matters is *plans*, not *names*) but worth flagging:
   the writer prompt should not lean on the name as a
   distinctive plot lever.
3. **Shared-name dominance.** If `animalNames[X]` is small
   (or hand-trimmed empty after Hayk's review) and falls
   back to `sharedNames`, the same 8 shared names dominate
   the corpus. **Mitigation:** the bank validator pins a
   minimum of 3 names per animal; the wiring code prefers
   `animalNames[X]` over `sharedNames` and only falls back
   when the per-animal list is missing or empty.
4. **Determinism drift.** Adding name draws to the RNG
   sequence changes downstream draws within `--with-names`
   runs. Documented and acceptable (§ 3.3); but operators
   re-running an old `--seed` value with `--with-names` for
   the first time **will** see different non-name fields.
   The README slice that lands `--with-names` must call this
   out explicitly.
5. **Cross-animal accidental membership.** If a name appears
   in *two* animals' lists (e.g. `Չալո` is in `շուն` and in
   `sharedNames`), the validator's union check `animalNames[X]
   ∪ sharedNames` accepts it for either animal. This is the
   intended behaviour — `Չալո` is a legitimate dog nickname
   and a legitimate shared one — but it does mean the
   validator cannot detect a generator that drew "Չալո" from
   `animalNames[շնիկ]` instead of `animalNames[շուն]`. The
   *generator* is the source of truth for which list it
   pulled from; the validator only enforces that the result
   came from *some* legitimate set. Acceptable trade-off.

---

## 7. Recommended implementation slice

When this lands, do it as **one** small commit:

1. `generate-story-plan.js` — add `--with-names` flag + name-
   draw helper. ≤ 80 LOC.
2. `validate-story-plan.js` — add optional `heroName` /
   `friendOrGuideName` checks + bank loader. ≤ 60 LOC.
3. `README.md` — document `--with-names`, mention default
   stays nameless, mention RNG-determinism caveat.

**Out of scope for that slice** (deliberate):
- No changes to `story-character-names.v1.json`.
- No changes to `validate-character-names.js`.
- No changes to `story-seed-bank.v1.json`.
- No changes to writer prompt — the writer prompt v2 capture
  package adds name handling on its own when the wiring
  lands; **this** slice is JSON shape + validator only.
- No production runtime wiring. ChatService stays unaware.

**Pre-conditions before that slice runs:**
- Hayk's native review on the name bank (companion file
  `character-name-native-review-20260503.md`).
- Any KEEP / CHANGE / DELETE outcomes from that review must
  be applied to `story-character-names.v1.json` first, so the
  wiring slice sees the cleaned bank.

**After that slice lands, the natural next steps:**
- Re-issue the writer prompt v2 capture (`writer-prompt-v2-first-capture-`) with `--with-names` plans to see whether deterministic names improve plan adherence on the writer side.
- Decide whether to default `--with-names` on (probably yes, once Hayk has signed off on the bank) — that decision is its own one-line slice.

---

## 8. Out of scope for this design note

- No changes to any tool or runtime file in this slice.
- No production wiring.
- No `--name-seed` flag (§ 4.2).
- No declension-aware name objects (§ 2).
- No "default `--with-names` on" flip (§ 7 last bullet).
- No multi-character (3+ named characters per plan) support.
  Today's plan shape names exactly two characters.

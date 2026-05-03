# Character name bank — Hayk's native-review checklist (2026-05-03)

**Status:** review checklist for hand-edit. **No code change** in
this file. Modifying the bank itself happens in a separate slice
once Hayk has marked KEEP / CHANGE / DELETE on the rows below.

**Companion files:**
- [`../story-character-names.v1.json`](../story-character-names.v1.json) — the file under review.
- [`../validate-character-names.js`](../validate-character-names.js) — the validator to run after edits.
- [`./character-name-wiring-plan-20260503.md`](./character-name-wiring-plan-20260503.md) — the wiring design that *consumes* the cleaned bank in a later slice.

---

## 1. Purpose

Pin a one-pass review of every per-animal entry in
`story-character-names.v1.json` against Hayk's native-Armenian
ear, before the wiring slice (companion design note above)
makes those names load-bearing in generated plans.

The bank today carries 47 animals × 5 names each (cat + dog
at 6) plus 8 `sharedNames`. The 2026-05-03 nickname-style
refresh replaced ~all of the previous `-իկ` descriptor forms
with punchy `-ո` folk-nickname forms. The validators pass.
What they cannot pin is whether each name **sounds right** to
a native speaker on a real toy. That is the purpose of this
checklist.

This is **research data**. None of these names is wired into
production runtime today, and the wiring design (companion)
is the slice that will land first — but only **after** this
review has passed, so the wiring sees a clean bank.

---

## 2. Instructions for Hayk

For each row in § 4, mark the **Status** column with one of:

- **KEEP** — names are good as-is. No change needed.
- **CHANGE** — at least one name needs replacement; write the
  replacements (or "drop X, add Y") in the **Notes** column.
- **DELETE** — the entire row should be replaced with a fresh
  list. Use **Notes** to suggest what should go in instead.

Per-name guidance (same as the bank's own `notes` array, but
worth re-stating here so the review is self-contained):

- **Prefer:** short, warm, callable, 2 syllables when possible,
  Armenian folk-nickname feel (`-ո`, `-իկ`, `-ատ`).
- **Avoid:**
  - foreign-feeling names;
  - too-human names that read as a person rather than a toy
    character (e.g. real Armenian first names where the toy
    is a tiny animal);
  - too-adult names (formal forms, social titles);
  - too-silly names that become a joke after one reading;
  - insulting or pejorative-sounding names;
  - sarcastic / parodic forms;
  - names that depend on a parenthetical comment to land
    ("Չոբան (հեգնական)") — already excluded but flag any that
    sneak past;
  - very long compound descriptors;
  - names that *sound* like a verb's imperative
    ("Մի՛ ասա" → "Մասա" feel — drop).

When in doubt, keep the name *only* if a 5-year-old could call
it across a room without stumbling.

After marking the table, see § 6 for the validator command.

---

## 3. Bank-wide quality observations

Cross-list audit (programmatic) before review:

- **47 / 47** animals covered. Coverage is fine.
- **Heavy repeats** across per-animal lists (sorted by use count):
  - `Թաթո` — **7** uses (արջուկ, արջ, մրջյուն, կատու, բադիկ,
    արագիլ, փայտփորիկ + իշուկ = 7+).
  - `Փետուրո` — **7** uses (սարյակ, աքլոր, սագ, արագիլ,
    փայտփորիկ, թութակ, սիրամարգ, լորիկ, փասիան).
  - `Փնջո` — **5** uses.
  - `Շողո` — **5** uses.
  - `Թռո` — **5** uses.
  - `Քայլո` — **5** uses.
- **Moderate repeats** (3–4 uses): `Բոլո`, `Թևո`, `Շվշվո`,
  `Գույնո`, `Գորշո`, `Բրդո`, `Պոչատ`, `Մեղրո`, `Լողո`, `Չալո`,
  `Երգո`, `Սպիտակո`, `Արագո`, `Կարմո`.
- These overlaps were intentional (light overlap was allowed)
  but several have grown loud enough that the bank reads more
  generic than nickname-distinctive on the heavily-overlapped
  animals (small birds, paw-creatures, feathered animals).
- `arծիվ` is the **only** entry below 5 names — has 4 (Սարո,
  Բարձրո, Թևո, Քաջո). Validator passes (≥3) but this is the
  one row that's structurally short. Adding a 5th if Hayk
  approves is an easy lift in the edit slice.

---

## 4. Per-animal review table (47 rows)

| Animal           | Current names                                       | Status (KEEP / CHANGE / DELETE) | Notes |
|---|---|---|---|
| ոզնի             | Փշո, Թմբո, Մուշո, Փնջո, Տոպո                       |  | |
| նապաստակ         | Թռվռո, Ակնջո, Փաշո, Բամբո, Տուշո                   |  | |
| սկյուռիկ         | Կաղնո, Շեկո, Պոչո, Շաղո, Բրդո                      |  | |
| աղվես            | Նազո, Խորո, Պոչատ, Շեկո, Սուսո                     |  | |
| արջուկ           | Մեղրո, Թաթո, Թավշո, Կուճո, Բոլո                    |  | |
| արջ              | Մեծո, Մռնչո, Թաթո, Մորթո, Մեղրո                    |  | |
| գորտ             | Կռնչո, Լողո, Կլկլո, Ցատկո, Կանաչո                  |  | |
| ծիտիկ            | Ճվճվո, Թևո, Չվչվո, Ճտո, Շվշվո                      |  | |
| եղնիկ            | Նազո, Քնքո, Շիկո, Թավշո, Աչքո                      |  | |
| կրիա             | Տոպո, Պատյո, Թաքո, Անշտո, Կլորո                    |  | |
| բու              | Գուհո, Աչքո, Իմո, Հոհո, Բուբո                      |  | |
| մեղու            | Բզզո, Մեղրո, Ոսկի, Շողո, Շաքարո                    |  | |
| թիթեռ            | Թիթո, Թևո, Գույնո, Նուրբո, Շվշվո                   |  | |
| մրջյուն          | Մրջո, Թաթո, Տիկո, Չալո, Բոլո                       |  | |
| ճնճղուկ          | Ճվճվո, Ճվո, Գորշո, Թռո, Չալո                       |  | |
| սարյակ           | Սևո, Փետո, Երգո, Թռո, Սարյո                        |  | |
| կատու            | Փուսո, Մլո, Միսո, Ճանկո, Մռմռո, Պիսիկ              |  | |
| փիսիկ            | Փիսո, Մյաո, Մինո, Ճանճո, Փուխո                     |  | |
| շնիկ             | Չալիկ, Թոշո, Միմո, Շաշո, Պուպի                     |  | |
| շուն             | Չալո, Զանգո, Բոլո, Գամփո, Շարիկ, Վաֆո              |  | |
| գառնուկ          | Մելո, Բրդո, Սպիտակո, Կաթո, Փոշո                    |  | |
| այծիկ            | Մորուքո, Մեգո, Շիկո, Ցատկո, Պոզո                   |  | |
| ձիուկ            | Բաշո, Սլացո, Արագո, Թռո, Թիկո                      |  | |
| աքլոր            | Կուկուռո, Թագո, Կանչո, Փետուրո, Խրոխտո             |  | |
| հավիկ            | Կդկդո, Բմբո, Շարո, Փնջո, Կուտո                     |  | |
| բադիկ            | Կռկռո, Փաթո, Լողո, Թաթո, Բոլո                      |  | |
| սագ              | Սպիտակո, Քեկո, Երկայնո, Բմբո, Կռո                  |  | |
| ձկնիկ            | Թեփո, Լողո, Փայլո, Շողո, Կլոռո                     |  | |
| ճպուռ            | Ճպո, Թևո, Շողո, Շվշվո, Արագո                       |  | |
| արագիլ           | Կարմո, Բարձրո, Քայլո, Թաթո, Փետուրո                |  | |
| աղավնի           | Քրքո, Սպիտակո, Փնջո, Լուսո, Մեղմո                  |  | |
| ծիծեռնակ         | Պոչատ, Թռո, Գարունո, Շվշվո, Ճիկո                   |  | |
| կաքավ            | Կաքո, Փնջո, Քայլո, Գորշո, Սարո                     |  | |
| գայլուկ          | Գորշո, Քաջո, Ոռնո, Սլացո, Բրդո                     |  | |
| իշուկ            | Համբո, Թաթո, Մոխրո, Իշո, Չոփո                      |  | |
| ուղտիկ           | Կուզո, Համբո, Մորթո, Կուզիկո, Քայլո                |  | |
| լորիկ            | Լորո, Գորշո, Փնջո, Քայլո, Փետուրո                  |  | |
| խլուրդ           | Թաքնո, Փորո, Մոխրո, Տիկո, Գետնո                    |  | |
| փայտփորիկ        | Թըկո, Թըկթըկո, Կարմո, Թաթո, Փետուրո                |  | |
| ծղրիդ            | Ճըռո, Երգո, Թռո, Գիշերո, Չիչո                      |  | |
| մողես            | Կանաչո, Արագո, Փայլո, Թաքնո, Շողո                  |  | |
| թութակ           | Թութո, Գույնո, Խոսո, Փետուրո, Կարմո                |  | |
| սիրամարգ         | Շքո, Գույնո, Փետուրո, Շողո, Թագո                   |  | |
| հազարան բլբուլ   | Բուլբուլո, Հազարո, Երգո, Մեղեդո, Երազո             |  | |
| իմաստուն ձուկ    | Իմո, Խորո, Թեփո, Ոսկի, Ալիքո                       |  | |
| արծիվ            | Սարո, Բարձրո, Թևո, Քաջո                            |  | |
| փասիան           | Գույնո, Փետուրո, Շքո, Քայլո, Պոչատ                 |  | |

---

## 5. Flagged for review (already suspected)

The following names were flagged across the previous slices'
final reports — at least one reviewer (Claude or Hayk during
the cleanup pass) noted some doubt about them. Listed here so
they don't go unreviewed.

### Per-name flags

| Name | Lives in | Concern |
|---|---|---|
| **Իշո** | իշուկ | Reads as a short form of *իշուկ* itself — fine as a play, but check it doesn't sound bare/diminishing on a kid's ear. Replaced the worse "Իիո" earlier. |
| **Չոփո** | իշուկ | Folk-feel addition. Confirm it lands as warm and not as a foreign-sounding loan. |
| **Կուզիկո** | ուղտիկ | Built from `կուզ` ("hump") + diminutive *-իկ* + folk *-ո*. Functionally a compound diminutive — confirm it doesn't read as awkward. Replaced the place-name "Անապատո". |
| **Սարո** for արծիվ | արծիվ | Replaced the place-name "Արարատո". `Սարո` is a real Armenian male given name (form of `Սարգիս`) — confirm it doesn't read too "human" on an eagle. Note the row is now 4 names; § 3 above flags it. |
| **Մռնչո** | արջ | "Growl-o" from `մռնչել`. Confirm it lands as cute-bear and not as scary-bear, given the toy's age 4–7 audience and Areg's safety-first posture. |
| **Խրոխտո** | աքլոր | "Strut-o" from `խրոխտ`. Slightly descriptor-y — confirm it sounds name-like for a rooster, not adjective-stiff. |
| **Անշտո** | կրիա | "Unhurried-o" — descriptor-derived. Same concern as `Խրոխտո`. Confirm the abbreviated form doesn't read as unfinished / typo-ish. |
| **Շվշվո** | (4 lists) | Repeated across ծիտիկ, թիթեռ, ճպուռ, ծիծեռնակ. Onomatopoeic, fits all four animals — but four is the limit. Decide: keep across all four, OR drop from 1–2 and replace with a more distinctive nickname for that animal. |
| **Թաթո** | (7 lists) | Most repeated name in the bank. Used on պaw-creatures (արջուկ, արջ, իշուկ, ուղտիկ, բադիկ, փայտփորիկ, կատու, մրջյուն, արագիլ — overlap counts vary). Decide: trim to 2–3 lists, OR move to `sharedNames` and drop from per-animal lists. |
| **Փետուրո** | (7 lists) | Same shape as `Թաթո` — used on feathered animals broadly. Same decision: trim, OR move to `sharedNames`. |
| **Թռո** | (5 lists) | Same pattern as `Թաթո` / `Փետուրո` for *flying* creatures. Trim or shared. |
| **Փնջո** | (5 lists) | Same pattern for fluffy small creatures. Trim or shared. |

### Cross-cutting decision worth making

If `Թաթո`, `Փետուրո`, `Թռո`, `Փնջո` (and possibly `Քայլո`,
`Շողո`) move to `sharedNames`, the per-animal lists become
much more distinctive but lean more on shared. Per the wiring
design (companion file § 3.1), `animalNames[X]` is preferred
over `sharedNames` at draw time, so moving these names to
shared means *fewer* draws will use them — which is the
desired outcome if the goal is to make per-animal lists
distinctive.

Recommended call: pick **one** of `Թաթո` / `Փետուրո` / `Թռո` /
`Փնջո` as a test case, move it to `sharedNames`, run the
validator, see whether the per-animal list reads better
without it. Apply the pattern to the others if the test
case lands well. The 8-entry `sharedNames` array can absorb
4 more easily; structure stays the same.

### Names not flagged but worth a glance

These passed earlier rounds but warrant a one-line check on
Hayk's ear:

- `Մյաո` (փիսիկ) — pure onomatopoeia. May read as too
  childish even for the kitten slot.
- `Քեկո` (սագ) — onomatopoeia from goose call. Confirm it
  lands as a real-feeling name, not a transliteration.
- `Քրքո` (աղավնի) — onomatopoeia from coo. Same concern.
- `Թըկթըկո` (փայտփորիկ) — double-onomatopoeia. Keep one of
  `Թըկո` / `Թըկթըկո`?
- `Պիսիկ` (կատու) — last entry in the cat list, the one
  non-`-ո` form. Confirm it stays as a real folk nickname
  and not as an accidental duplicate of the seed-bank
  animal `փիսիկ`.

---

## 6. How to validate after edits

After hand-editing `story-character-names.v1.json` based on
the table above:

```bash
node tools/StoryModelBakeoff/validate-character-names.js
```

The validator (pure-Node, no dependencies) checks:

- JSON parses;
- top-level shape (`version`, `language`, `purpose`,
  `animalNames`);
- coverage — every animal in seed-bank `palettes.animals`
  has an `animalNames` entry (47/47 today);
- each entry has **at least 3** non-empty string names with
  no exact duplicate inside the same list;
- optional `sharedNames`, if present, is an array of
  non-empty strings with no duplicates.

Exit 0 on PASS, non-zero on FAIL with all errors listed.

If a per-animal list ends up below 3 names after deletions,
the validator will FAIL. Restore the count to ≥ 3 (or
remove the entire animal's list — though that drops coverage
below 47 and will FAIL the coverage check, so add at least
3 fresh names instead).

Once the validator passes, the edit slice is ready to commit.
The wiring slice (§ companion design note) can then run
against the cleaned bank.

---

## 7. Out of scope for this checklist

- This file does **not** edit the bank. It is a checklist
  only.
- No `validate-character-names.js` change.
- No `generate-story-plan.js` change.
- No `validate-story-plan.js` change.
- No production runtime change.
- No `ChatService` change.

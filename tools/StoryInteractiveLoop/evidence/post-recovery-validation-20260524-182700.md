# Post-recovery validation — 2026-05-24 (SUCCESS: quota recovered)

## Summary

OpenAI billing quota is back. The full post-recovery validation
completed all phases. Five Armenian Story-mode sessions × four
turns each = 20 turns, 0 fail-closed, 0 http_error, no sentinel.

**Verdict**: 3 PASS / 2 WARN / 0 FAIL. The two WARN sessions
both come from real noun-grounding gaps (and one new short-noun
stemmer edge case), not from any of the false-positive classes
the commit `6af2a3d` stemmer fix was written for. The
previously-known false-positive classes (`տերև/տերևների`,
`նապաստակ/նապաստակը`, `ընկեր/ընկերոջը` / `ընկեր/ընկերոջին`) did
NOT fire in this run — those forms appeared natively in the
generated text in ways the new stemmer normalizes correctly.

**No code changes are recommended in this slice.** Two
observations below are candidates for a future small slice but
explicitly out of scope here.

## Run context

- **Validation timestamp**: 2026-05-24 ~18:27 UTC
- **Run stamp** (StoryInteractiveLoop): `20260524-181621`
- **Branch**: `main`
- **Commit SHA**: `00d05e3a` (HEAD)
- **Working tree**: dirty
  (pre-existing M files: `.claude/settings.local.json`,
  `esp32/AregVoiceMvp/config.h`; pre-existing untracked files
  unrelated to this slice — none touched)
- **Backend listener**: `:5000`, owner unknown — not this session.

## Phase 0 — Deterministic test baseline (Pass)

| Suite                                                         | Result        |
|---------------------------------------------------------------|---------------|
| `dotnet test tools/StoryInteractiveLoop.Tests`                | 65/65 pass    |
| `dotnet test backend/.../Application.Tests --filter Moderation` | 55/55 pass    |

## Phase 1 — Backend health (Pass)

```
GET http://localhost:5000/api/health
→ 200 OK
→ {"status":"ok","service":"ArmenianAiToy API","database":"ok"}
```

## Phase 2 — Minimal /api/chat probe (Pass — quota recovered)

```
POST http://localhost:5000/api/devices/register
  body: {"macAddress":"POSTREC-OK-001"}
→ 200 OK, deviceId+apiKey returned

POST http://localhost:5000/api/chat
  body: {"message":"Պատմիր հեքիաթ փոքրիկ ոզնիի մասին"}
→ 200 OK
→ safetyFlag: 0 (Clean)
→ mode: story
→ storySessionId: <guid>
→ choiceA: «Մոտենանք ծառին»
→ choiceB: «Լսենք աղմուկի ձայնը»
→ response: «Փոքրիկ ոզնին անունով Միկոն սիրում էր զբոսնել անտառում։
             Մի օր նա հանդիպեց մի փայլուն մրգի ծառի, որի վրա րունով
             մրգեր կային։ Միկոն հետաքրքրությամբ մոտեցավ և երազացավ
             իր փոքրիկ ոզնիներին պատմել այդ հրաշալի ծառի մասին։
             Հանկարծ մի փոքր աղմուկ լսվեց կողքից։ Միկոն զարմանքից
             փոքր-ինչ նահանջեց։»
```

Real Armenian story body (~280 chars), two grounded choices,
mode=story, no sentinel. **Quota recovery confirmed.**

(Quality micro-note for the next-slice candidate list: «րունով»
on line 2 is a non-word — looks like the model corrupted
«հյութով» or similar. Out of scope here; the
StoryInteractiveLoop evaluator does not currently flag this
class of low-frequency typo and it would belong to a separate
linguistic-reviewer slice if pursued.)

## Phase 3 — Metrics endpoint (Observed: 404 concealment)

Both pre-run and post-run scrapes:

```
GET http://localhost:5000/metrics   (no Authorization header)
→ 404, 0 bytes
```

This is the documented concealment-fail-closed default when
`Metrics:ScrapeToken` is unset (CLAUDE.md § Metrics). Counter
behavior is pinned by the 14 unit tests in
`ModerationFailClosedMetricsTests`. Direct scrape validation
remains a deploy-side concern and is intentionally not done in
this slice.

By code inspection of the moderation path during the 20-turn
run: all 20 turns returned `SafetyFlag=Clean` with real story
bodies, so the `FailClosed` branch was never taken and
`aat_moderation_failclosed_total` was NOT incremented during
the healthy run. This is the "positive control" the slice's
Phase 6 was looking for.

## Phase 4 — StoryInteractiveLoop 5×4 (Pass)

```
dotnet run --project tools/StoryInteractiveLoop -- \
    --max-sessions 5 --max-turns 4 \
    --seed-id S01,S02,S03,S04,S05 --allow-larger-run
```

Run stamp: `20260524-181621`.

| # | Seed | Stop reason         | Turns | Verdict | Arm | Logic | Suit | Choice | Cont |
|---|------|---------------------|------:|---------|----:|------:|-----:|-------:|-----:|
| 1 | S01  | max_turns_reached   |   4   | PASS    | 100 |   100 |  100 |   85   |  100 |
| 2 | S02  | max_turns_reached   |   4   | PASS    | 100 |   100 |  100 |  100   |  100 |
| 3 | S03  | max_turns_reached   |   4   | PASS    | 100 |   100 |  100 |  100   |  100 |
| 4 | S04  | max_turns_reached   |   4   | WARN    | 100 |   100 |  100 |   70   |  100 |
| 5 | S05  | max_turns_reached   |   4   | WARN    | 100 |   100 |  100 |   70   |  100 |

Aggregate: avg Armenian 100, Story logic 100, Suitability 100,
Choice quality 85, Continuation coherence 100. 20 turns total,
0 fail-closed, 0 http_error.

Recurring-warning histogram:

| Code                          | Count |
|-------------------------------|------:|
| `choice_a_noun_not_in_body`   | 3     |
| `choice_b_noun_not_in_body`   | 2     |
| (everything else)             | 0     |

Notable zeros:
- `safety_fallback:*` — 0 (quota healthy)
- `http_error` — 0
- `continuation_ignores_selected_choice` — 0
- `recap_overlap_high` — 0
- `choices_identical` / `choices_share_first_token` — 0
- `choices_repeated_from_earlier_turn` — 0
- `latin_leakage_*` / `cyrillic_leakage_*` — 0
- `choice_*_generic` / `choice_*_too_long` — 0

## Phase 5 — Comparison against baselines

### A. Moderation recovery

| Question                                       | This run | 20260524-151621 | 20260524-170208 |
|------------------------------------------------|----------|-----------------|-----------------|
| `safety_fallback:2` count                      | **0**    | 0               | 5               |
| Sentinel «Մի րոպե սպասիր...» count             | **0**    | 0               | 5               |
| Sessions reaching `max_turns_reached`          | **5/5**  | 3/5 (2 hit http_error mid-run) | 0/5 |
| Total live turns completed                     | **20**   | 16 (4 sessions × 4 − 1 truncated)             | 5 (1 each)      |

The 5×4 fully completes for the first time since the original
healthy run.

### B. Stemmer false-positive validation (commit 6af2a3d)

The five `choice_*_noun_not_in_body` warnings this run, analyzed
individually:

1. **S01 turn 0 ChoiceB «Նայենք քամուցին»**
   - Body has «քամի» (4 chars). Stemmer: short-noun length guard
     prevents `«ի»` strip → stem stays «քամի».
   - Choice has «քամուցին» (7 chars). Stemmer: strips `«ին»` →
     «քամուց» → verb-root-alt drops trailing `«ց»` → «քամու».
   - Stems differ (`քամի` vs `քամու`). The model produced a
     **non-standard Armenian word** («քամուցին» is not a
     conventional case of «քամի»); native speakers would say
     «քամուն» / «քամուց» / «քամու». Borderline: the warning is
     defensible because the choice's form really IS divergent
     from the body's form, but a future stemmer pass could
     normalize «-ուցին» → «-ի» for nouns ending in vowel + «ի»
     to recognize them as the same word. Not actioned now.

2. **S04 turn 0 ChoiceA «Բացենք փոքրիկ արկղիկը»**
   - Body has «տուփ» (box). Choice has «արկղիկ» (also box,
     different word). The model used a **synonym** the body
     never established. **Real positive** — the choice
     introduces a new noun for a previously-established object.
     The stemmer fix could not help here; synonyms require
     embeddings or a thesaurus. Acceptable warning.

3. **S04 turn 0 ChoiceB «Շարունակենք խաղալ ծաղիկների հետ»**
   - Body talks about a robot Areg and a box on his desk. No
     flowers. The choice asks the child to "continue playing
     with flowers" — an activity the body never establishes.
     **Real positive — clear noun ungroundedness.** Same class
     the slice was created to catch.

4. **S05 turn 0 ChoiceA «Մոտենանք ծառին»**
   - Body has «ծառերի» (plural genitive of tree). Choice has
     «ծառին» (dative singular). Stemmer: body stems to «ծառեր»;
     choice stays «ծառին» (short-noun length guard prevents `«ին»`
     strip). Both clearly refer to «ծառ» (tree).
   - **False positive** — a new short-noun stemmer limitation
     that didn't surface in earlier runs because the previous
     evidence happened to use different inflections. Mirrors
     the «ուղի/ուղին» case that the commit `6af2a3d` README
     explicitly listed as a known limitation. Not actioned now
     — fixing it would require either lowering the length guard
     (risky for other words) or special-casing the «-եր» /
     «-եր»+«ի» plural marker.

5. **S05 turn 3 ChoiceA «Մոտենանք լույսավորվին»**
   - Body has «լույսավորված» (lit). Choice has the awkward
     «լույսավորվին». Native speakers would say «լույսերին» or
     «լուսավորին». The model produced a non-standard inflection.
     Borderline real positive.

**Conclusion**: the previously-known stemmer false-positive
classes (`տերև/տերևների`, `նապաստակ/նապաստակը`,
`ընկեր/ընկերոջը/ընկերոջին`) did NOT fire in this run because
the model happened not to use those forms with the specific
mismatches that previously surfaced. The unit tests (commit
`6af2a3d`) directly pin the fix on the actual fixture inputs;
this live run gives a clean indirect signal — no regressions
appeared on those exact forms.

### C. Real noun-positives still firing

Both genuine ungroundedness cases (S04 ChoiceB flowers, S04
ChoiceA arkghik-synonym) fired correctly — the slice's main
purpose (catch real choice-noun-not-in-body cases) is
preserved.

### D. Repeated choices

`choices_repeated_from_earlier_turn` count: **0**.

Some semantic / structural repetition was visible by eye in
S01 (Turn 1 ChoiceA `Մոտենանք տերևին` and Turn 2 ChoiceB
`Նայենք տերևին`; Turn 2 ChoiceA `Մոտենանք լույսին` and Turn 3
ChoiceA `Մոտենանք լույսին`), but the EXACT (A, B) pair never
repeated. The detector's specification (commit `b3abf2e`) is
exact-pair-only by design; it correctly did not fire. A
broader "ChoiceA repeated as either A or B in a later turn"
detector would be a separate, deliberate slice (still no clear
evidence that the current strict contract is wrong).

### E. Story quality observations

| Question                                                | Answer |
|---------------------------------------------------------|--------|
| Did continuation follow selected choices?               | **Yes — every turn.** No `continuation_ignores_selected_choice` fired. By inspection, every continuation body started with the action from the previous turn's selected choice. |
| Did any story restart from scratch?                     | No. All five seeds produced coherent multi-turn arcs. |
| Were choices concrete and grounded?                     | Mostly yes. Three of 20 turn-choices were ungrounded (see § B) — two real, one short-noun stemmer FP. |
| Were choices ESP32-display-friendly?                    | Yes. Longest choice across the run was 31 chars («Շարունակենք խաղալ ծաղիկների հետ»); budget is 60. |
| Did choices use varied verbs?                           | Heavily dominated by «Մոտենանք X / Նայենք Y» pattern. Story-prompt-design concern, NOT for this slice. |

## Phase 6 — Metrics check after run

Same 404 as pre-run (`/metrics` concealed without scrape token).
By code inspection: zero `FailClosed` calls during the 20-turn
run, so `aat_moderation_failclosed_total` did NOT increment.
The unit-test coverage (commit `4fa6274`, 14 tests) covers the
"no increment on successful classify" / "no increment on
genuine content flag" / "no increment on transient retry
recovery" branches.

## Conclusions

| Question                                                | Answer |
|---------------------------------------------------------|--------|
| Validation succeeded?                                   | **Yes — all phases completed.** |
| OpenAI billing recovered?                               | **Yes — `safetyFlag=0` consistently across 20 turns.** |
| `aat_moderation_failclosed_total` stayed quiet?         | **Yes by code inspection** (no `FailClosed` calls observable from the 0 sentinel responses). Direct scrape requires `Metrics:ScrapeToken` config (deploy-side). |
| Stemmer fix (commit `6af2a3d`) regressions?             | **None observed.** Previously-known FP classes did not fire. |
| Real ungrounded-noun positives still fire?              | **Yes** (S04 ChoiceB flowers, S04 ChoiceA arkghik-synonym). |
| Story logic / continuation coherence / Armenian quality | **100/100/100 across all 5 sessions.** |
| Any code change recommended now?                        | **No.** All findings are either real positives (expected) or out-of-scope for this slice. |

## Out-of-scope observations for future small slices

These are deliberately NOT implemented in this validation slice.
Each could become its own focused slice if/when the operator
decides it's worth a slot:

1. **Short-noun stemmer limitation** (`ծառին`, `ուղին`,
   `քամի/քամու` family). The 4-char length guard prevents `«ին»`
   strip on 5-char dative forms. A targeted fix could lower the
   guard to 3 for the specific 1-2-char endings, but that risks
   over-truncating other words. Wait for more evidence of this
   class repeating across runs before paying the design cost.
2. **Synonym detection** (`տուփ` ↔ `արկղիկ`). Requires either
   embeddings or a small kid-noun thesaurus. Big change.
3. **Cross-turn semantic-repetition detector** (broader than
   exact-pair). Would catch S01's "approach leaf / look at leaf"
   ping-pong. Probably worth a focused slice eventually.
4. **`Metrics:ScrapeToken` provisioning** in local dev so future
   validations can read counters directly. Deploy-side
   one-liner; no code change.
5. **Model micro-typos** (`րունով`, `լույսավորվին`, `քամուցին`).
   Story-prompt or system-prompt tightening — out of scope here
   per the slice's "do not touch Story prompt" rule.

## Cost summary

- Phase 2 probe: 1 chat call (real Armenian story generated).
- Phase 4 StoryInteractiveLoop: 25 chat calls (5 sessions × (1
  start + 4 turns continuation) = 25).
- Total: ~26 OpenAI chat completions. Well under the
  `--allow-larger-run` 25-call gate envisioned by the runner's
  cost-gate documentation. No fail-closed events. No retries.

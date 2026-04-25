# Story Voice MVP — Phase 1B re-run AFTER grounding patch (2026-04-25)

Re-capture of the same 10 fresh-conversation prompts, run against
the backend that has the new `StoryChoiceInstruction` rules from
the Phase 3/4 narrow fix loaded in memory. **No grading. Mechanical
deltas only.** Inputs:

- **Before** baseline: `tools/story-quality-evidence-fresh-20260425.md`
  (the data this comparison cites was captured on the same day, before
  the prompt patch landed).
- **After** evidence: per-case JSON in
  `C:\tmp\story-evidence\fresh-after\case-NN.json`. Driver:
  `C:\Users\hayk.margaryan\AppData\Local\Temp\story-evidence\run_fresh_after.py`.
- **Prompt change under test**: `CHOICE GROUNDING — STRICT RULE` and
  `NO FOLKLORE BY DEFAULT — STRICT RULE` plus two `FINAL STORY CHECK`
  bullets, all inside `StoryChoiceInstruction` in
  `backend/src/ArmenianAiToy.Application/Services/ChatService.cs`.
- **Backend restart**: PID 29876 (old binary) was stopped with
  `Stop-Process -Id 29876 -Force`; the API project was rebuilt
  cleanly; new instance was started with `dotnet run --project ...
  --no-build` and `/api/health` returned 200 before the capture began.
  No code or prompt changes were made between the restart and the
  capture.

## Capture conditions

- **Endpoint**: `POST /api/chat` only. No audio.
- **Devices**: 10 freshly-registered devices, MAC range
  `AA:BB:CC:DD:DD:01..0A` (a new range; the FF/EE ranges were burned
  by the Phase 1B / aborted re-run respectively, and reusing a MAC
  would have returned an existing device instead of producing a
  fresh conversation).
- **Conversation isolation**: one device → one chat call → one
  conversation. No `StorySessionId` was passed.
- **Freshness contract verified**: `Unique devices: 10/10`,
  `Unique conversationIds: 10/10`, `Unique storySessionIds: 10/10`.
- **All 10 returned**: `HTTP 200`, `mode = "story"`,
  `safetyFlag = 0` (Clean), both `choiceA` and `choiceB` populated.

## Mechanical deltas vs Phase 1B baseline

The original Phase 1B driver tokenized Armenian on `[԰-֏]{4,}`
(4-char floor). I noticed during inspection that several real
Armenian common nouns (քար / ծառ / աչք / գետ) are 3 characters and
were therefore being undercounted as "unrelated" even when both the
body and the choice clearly named the same noun. Both the **before**
and the **after** numbers are restated below under both 4-char (the
original heuristic, kept for apples-to-apples) and 3-char (a more
honest read of Armenian common nouns) floors.

### Choice/body relatedness — token-overlap (rounded `max(A, B)` per case)

| Case | BEFORE 4-char | AFTER 4-char | BEFORE 3-char | AFTER 3-char |
|------|---------------|--------------|---------------|--------------|
|  1   | 0.00          | 0.00         | 0.00          | 0.33         |
|  2   | 0.00          | 0.00         | 0.00          | 0.50         |
|  3   | 0.00          | 0.00         | 0.00          | 0.00         |
|  4   | 0.00          | 0.33         | 0.00          | 0.67         |
|  5   | 0.00          | 0.33         | 0.00          | 0.67         |
|  6   | 0.67          | 0.00         | 0.67          | 0.00         |
|  7   | 0.00          | 0.33         | 0.00          | 0.25         |
|  8   | 0.00          | 0.50         | 0.00          | 0.50         |
|  9   | 0.33          | 0.00         | 0.33          | 0.67         |
| 10   | 0.50          | 0.00         | 0.50          | 0.00         |

|                              | BEFORE 4-char | AFTER 4-char | BEFORE 3-char | AFTER 3-char |
|------------------------------|---------------|--------------|---------------|--------------|
| **avg `max(A, B)` per case** | 0.15          | 0.15         | **0.15**      | **0.36**     |
| **both-choices-zero cases**  | 7 / 10        | 6 / 10       | **7 / 10**    | **3 / 10**   |

Direction: small under the brittle 4-char heuristic, sizeable under
the more honest 3-char heuristic. Cases 1 / 2 / 8 went from
"both-zero" to "both-choices reference the body's named noun" —
the rule clearly had an effect on those. Cases 6 / 10 regressed
(went from grounded to both-zero). Net: 4 cases improved, 2 cases
regressed, 4 cases unchanged on the 3-char floor.

### Folklore vocabulary

Substring scan over the new prompt's banned default-folklore list
(`աստված`, `աստվածուհի`, `հրեշտակ`, `ոգի`, `դև`, `վիշապ`,
`հրեղեն`, `քաջք`, `ալք`, `հեքիաթային էակ`).

|                              | BEFORE | AFTER |
|------------------------------|--------|-------|
| Case 01 folklore hits        | 1 (`աստվածուհի` in `ջրային աստվածուհի`) | **0** |
| Total folklore hits across batch | 1  | **0** |

Direction: clean pass. Case 01's `«ջրային աստվածուհի»` was the only
folklore-vocabulary hit in the Phase 1B batch and it does not recur
in the after batch.

### Non-Armenian glyphs in body

|                              | BEFORE | AFTER |
|------------------------------|--------|-------|
| Cases with non-Armenian char | 1 (case 03 backtick) | **0** |
| Total distinct offending chars | 1  | **0** |

Direction: clean pass. The case-03 ASCII backtick used as a comma
does not recur on this batch (case 03's after-body uses a normal
Armenian full stop `։`).

### "Shiny mysterious object" trope

Substring scan over a fixed lens
(`փայլուն`, `շողշող`, `կախարդական քար`, `կախարդական տուփ`,
`գաղտնի տուփ`, `գաղտնի քար`, `լուսավոր քար`).

|                              | BEFORE | AFTER |
|------------------------------|--------|-------|
| Cases with at least one trope hit | ~6 (qualitative count from Phase 2) | **5** (cases 02, 03, 05, 06, 07) |
| Cases mentioning `փայլուն` | several | **5** (cases 02, 03, 05, 06, 07) |
| Cases mentioning `կախարդական քար` | 0 | **1** (case 03) |

Direction: roughly unchanged. The story-shape monoculture is still
present. A "shiny stone / shiny object" appears in roughly half of
the after-batch openings — same skeletal shape Phase 2 flagged.
Possible interaction with the new rule: by enumerating
`stone, box, river, key, bird, friend, magical item` in the
"do not invent in choices" list, the prompt may have inadvertently
anchored the model toward producing those very nouns inside the
*body* (where they're now allowed). Worth keeping in mind when
deciding the next slice — flagged here without a fix.

### Other observations (no scoring)

- **Case 01 (after)** body talks about a glittering stone (`քար`)
  and choices act on that stone (`Մոտենալ քարին`, `Քարը գլորել`)
  — the rule worked. `քար` is 3 chars so the 4-char heuristic
  recorded 0/0 on this case anyway. This is a clear instance where
  the heuristic undersells the actual grounding improvement.
- **Cases 6 and 10 regressed** in relatedness. Case 6's after body
  introduces an `ակնոց` (eyeglasses) whose choices then jump to a
  watermill (`ջրաղաց`) and a song (`երգ`). Case 10's body is about
  a butterfly among flowers; choices switch to "tree" and "friends"
  not present in the body.
- **Several after bodies contain unusual / invented Armenian
  constructions** the Phase 1B batch did not have at the same
  density: `«Սանթիկները անփորձաշար»` (case 01),
  `«նոր վարչապետ գտնել»` ("find a new prime minister", case 05),
  `«Փոքրի կզգին»` (case 09), `«գույնզգույն գուլպաները»`
  ("colorful socks", case 10), `«բծի թեփուկանքի»` (case 07). This
  is **outside the scope of this slice** (Armenian naturalness, not
  choice grounding), but worth flagging as a possible interaction
  effect — the model may be spending more output-policy attention
  on the new rules and less on basic Armenian word forms. Capture
  for the next eval, do not treat as a blocker for this slice.
- **Closed/moral endings**: zero across the after batch
  (heuristic unchanged).

## Acceptance verdict (Phase 2 targets)

| Target                                                            | Result | Verdict |
|-------------------------------------------------------------------|--------|---------|
| Average `max(choice_A_ratio, choice_B_ratio)` per case ≥ **0.50** | 0.36 (3-char) / 0.15 (4-char) | **MISS** (improved from 0.15 / 0.15) |
| Zero cases where **both** choices have ratio = 0.0                | 3 / 10 (3-char) — 6 / 10 (4-char) | **MISS** (improved from 7 / 10) |
| **Zero folklore-vocabulary hits in case 01**                      | 0 hits  | **PASS** (was 1) |

**Overall: 1 of 3 acceptance targets met.**

The grounding rule moved the dial in the right direction (4 of 10
cases went from "no grounded choice" to "at least one grounded
choice") but did not clear the bar Phase 2 set. The folklore rule
worked cleanly on the case it was specifically designed against.

## Recommendation handoff

This is a re-run validation, not a new evaluation, so the call here
is bounded. Per Phase 2's escalation plan: if the prompt rule does
not bring choice grounding above the bar after one re-run, the
next move is **category B (runtime choice/body coherence gate)** —
not another prompt edit, not a model change, not a reviewer loop.
The current evidence supports starting that B work next.

A second consideration: the BAD-example list in the new
`CHOICE GROUNDING` section explicitly named "stone, box, river,
key, bird, friend, magical item" as nouns the model should not
*invent in the choices*. After the patch, those exact nouns now
appear more often inside the **body** itself — i.e. the
prohibition list seems to have leaked into the model's
body-vocabulary prior. Worth observing on the next eval; the
fix (if needed) would be to soften the example list, not to add
new rules.

These are notes for the *next* slice's planning step. **No code or
prompt changes are recommended inside this validation slice.**

## Reproducer

```
/c/Python314/python /tmp/story-evidence/run_fresh_after.py
```

Driver hard-codes the new MAC range and writes per-case JSON to
`/tmp/story-evidence/fresh-after/`. Exits non-zero if the freshness
contract isn't met.

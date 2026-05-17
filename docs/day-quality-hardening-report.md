# Full-day Areg quality hardening — 2026-05-17

End-of-day report for the `overnight/areg-quality-hardening`
branch. Honest readiness scoring. Not pushed.

## Branch + commits

Branch: `overnight/areg-quality-hardening` (off `main`).

Commit log (oldest first):

| SHA | Message |
|---|---|
| `49dc498` | docs(toy): add ESP32 chain documentation |
| `4dd92db` | fix(chat): improve Armenian game mode quality for ages 4-7 |
| `d3c55ae` | fix(chat): improve Armenian riddle mode quality for ages 4-7 |
| `8f0306e` | fix(story): strengthen Armenian choice quality and continuation |
| `8083ea5` | fix(chat): tighten natural Armenian child register |
| `9b1ad75` | docs(chat): add game and riddle quality evidence |
| `aa1151e` | docs(toy): clarify ESP32 browser prototype status |

(This report itself adds one more commit on top.)

## Files changed today

| File | Slice | Reason |
|---|---|---|
| `docs/esp32-chain.md` | initial + slice 4 | First-time creation; later split into repo-tracked vs local-scratch sections + Next hardware steps. |
| `backend/src/ArmenianAiToy.Application/Services/ChatService.cs` | slices game / riddle / 1 / 2 | Added STRICT NON-NEGOTIABLES + pinned opener subsections to GameModeInstruction, RiddleModeInstruction, StoryChoiceInstruction. Added ARMENIAN REGISTER subsection to CalmModeInstruction and CuriosityWindowInstruction. Extended FINAL STORY CHECK with three new re-check items. No runtime / parser / coherence-gate / dispatch logic changed. |
| `backend/tests/ArmenianAiToy.Application.Tests/GamePromptContentTests.cs` | game slice | +7 deterministic prompt-content tests. |
| `backend/tests/ArmenianAiToy.Application.Tests/RiddlePromptContentTests.cs` | riddle slice | +10 deterministic prompt-content tests. |
| `backend/tests/ArmenianAiToy.Application.Tests/StoryPromptContentTests.cs` | slice 1 | +12 deterministic prompt-content tests. |
| `backend/tests/ArmenianAiToy.Application.Tests/CalmPromptContentTests.cs` | slice 2 | +2 deterministic prompt-content tests. |
| `backend/tests/ArmenianAiToy.Application.Tests/CuriosityPromptContentTests.cs` | slice 2 | +3 deterministic prompt-content tests. |
| `tools/quality-evidence/areg-game-riddle-quality-20260517.md` | slice 3 | New evidence doc. |
| `docs/day-quality-hardening-report.md` | slice 5 | This file. |

## Slices completed

- **SLICE 1 — Story Choice + Continuation Quality:** done (commit `8f0306e`). New STRICT NON-NEGOTIABLES FOR CHOICES subsection + three additions to FINAL STORY CHECK. 12 new deterministic tests.
- **SLICE 2 — Armenian Language Quality Pass:** done (commit `8083ea5`). Cross-mode register consistency: Calm and Curiosity now share the same formal-plural ban that Story / Game / Riddle already carry. Curiosity also gains a no-self-intro ban. 5 new deterministic tests.
- **SLICE 3 — Game + Riddle Evidence Run:** done (commit `9b1ad75`). Evidence doc records what is proven (1314/1314 unit tests), what is NOT proven (runtime behavior — only a live `GameBenchmark` / `RiddleBenchmark` could prove it), commands to run them later, and a manual phone/ESP32 smoke checklist. Live benchmarks deliberately NOT run autonomously (cost + shared state + branch identity).
- **SLICE 4 — ESP32 Browser Chain Cleanup Docs:** done (commit `aa1151e`). New "Repo-tracked vs local-scratch prototype" preamble + "Next hardware steps" section (TTS → mic → physical button ordering rationale) + "What this doc is NOT" closer. Docs only.
- **SLICE 5 — Final Verification + Day Report:** done (this commit).

## Tests run

| Command | Result |
|---|---|
| `dotnet test ... --filter "Game"` (after game slice) | **100 / 100 passed** |
| `dotnet test ... --filter "Riddle"` (after riddle slice) | **89 / 89 passed** |
| `dotnet test ... --filter "Story"` (after slice 1) | **209 / 209 passed** |
| `dotnet test ... --filter "Calm|Curiosity"` (after slice 2) | **147 / 147 passed** |
| `dotnet test ...` (full, end of day) | **1314 / 1314 passed** |

User's `ArmenianAiToy.Api` was running with assembly locks across the
entire session; the lock-avoiding build pattern (build only Application,
then Tests with `-p:BuildProjectReferences=false`, then `dotnet test
--no-build`) was used throughout. Dev server untouched.

## Quality improvements

Prompt-content improvements (deterministic, pinned by tests):

- **Game mode (4dd92db):** exactly-one-action-per-turn, max-one-question,
  never-end-after-one-turn, ban formal-plural address, ban empty
  meta-openers, ban self-intro. Pinned five OPENER PATTERNS including
  «Ես մտածեցի մի բան, կռահի՞ր» (guessing game).
- **Riddle mode (d3c55ae):** never-reveal-before-correct-or-give-up,
  second-wrong-guess-needs-a-NEW-distinct-clue, ban formal-plural,
  ban self-intro, ban cold-rejection. Pinned opener
  «Կռահի՞ր, թե ինչ եմ մտածել».
- **Story choices (8f0306e):** ban generic motion-only pairs
  (forward/back), ban meta-chat choices ("do you want to keep going"),
  ban placeholder / template / null labels, ESP32 visual upper bound
  (~60 Armenian chars), ban formal-plural inside choice lines. Three
  new FINAL STORY CHECK reiterations.
- **Cross-mode register (8083ea5):** Calm + Curiosity gain the same
  formal-plural ban that the other three modes carry. Curiosity also
  bans self-intro at the top of an answer.
- All abstract-worded bans verified by `Assert.DoesNotContain` —
  the literal banned forms appear nowhere in any mode constant.

Documentation improvements:

- **ESP32 chain doc (49dc498):** first-time end-to-end chain reference.
  Backend ↔ browser ↔ ESP32 voice MVP. Credential-hygiene list.
- **ESP32 chain doc (aa1151e):** repo-tracked vs local-scratch
  distinction + Next hardware steps (TTS → mic → button) + scope
  disclaimers.
- **Game + Riddle evidence (9b1ad75):** honest "what is proven vs what
  is not" doc + commands to run live benchmarks + manual checklist.

Engineering hygiene:

- Every slice followed: read prompt + tests, plan, edit, build, test,
  commit. Commit messages carry rationale + targeted + full counts.
- No noise file ever staged. Every commit shows the explicit two-file
  (or one-doc) `--cached --stat` audit before `git commit`.
- No push.

## Honest readiness scores

These are prompt-content + test-coverage scores. **None of them is a
runtime-behavior score** — a live OpenAI-backed benchmark is what
would move a "prompt quality" score into a "runtime quality" score.

| Surface | Score / 100 | Reason |
|---|---|---|
| Armenian language quality (prompt content) | **78** | All five modes now share an abstract-worded formal-plural ban; cross-mode register is consistent; banned literals are absent from every prompt. Live model output not measured this session — score caps in the upper 70s until a benchmark run says otherwise. |
| Game mode (prompt content) | **80** | Strict bans + pinned opener landed; 100/100 prompt-content tests green. Behavior unverified. |
| Riddle mode (prompt content) | **80** | Strict bans + pinned opener landed; 89/89 prompt-content tests green. Behavior unverified. |
| Story mode (prompt content) | **78** | Choice quality subsection + FINAL STORY CHECK extension landed. The CHOICE GROUNDING / CHOICE DIFFERENTIATION / NO RECAP rules from earlier slices are still the load-bearing pieces. 209/209 story tests green. |
| ESP32 browser UX | **65** | Backend chain works; legacy text sketch broken (documented); loading UX is local-only (documented). No new hardware shipped. |
| Backend chat reliability | **85** | Unchanged from yesterday's report. `OpenAIReliabilityGate`, retries, circuit, metrics still in place. 1314 tests still green. |
| Child safety | **85** | Unchanged. Dual moderation, fail-closed sentinel, gate ordering (pause → bedtime → mode), no regression in test suite. None of today's changes touched safety / moderation / auth. |
| Test coverage | **82** | 1314 tests; +34 today (12 Story + 7 Game + 10 Riddle + 2 Calm + 3 Curiosity). All assertions deterministic, no live-model dependence. Voice-path tests and live benchmarks still the gap. |

## Remaining risks

- **Prompt-content tests are not behavior tests.** Every score above is a
  ceiling on prompt quality, not a floor on what the model produces.
- **Live benchmarks not run this session.** The recommended next action
  is the live `GameBenchmark` / `RiddleBenchmark` / `StoryBenchmark` runs
  against a backend built from this branch, with the OpenAI key
  configured. See `tools/quality-evidence/areg-game-riddle-quality-20260517.md`
  for the exact commands and the 2026-04-30 baselines to compare against.
- **Voice path is Story-only today.** All Game / Riddle prompt
  improvements only land on `POST /api/chat`. The C1 voice endpoint
  `POST /api/chat/audio` does not dispatch into Game or Riddle today
  (per CLAUDE.md § Voice chat C1). Bringing Game / Riddle to voice is a
  separate slice.
- **Abstract bans are single-phrase in their literal absence checks.**
  `Assert.DoesNotContain("ճիշտ չէ")` in Riddle and
  `Assert.DoesNotContain("գնալ առաջ")` / `Assert.DoesNotContain("ուզու՞մ ես շարունակել")`
  in Story / Game cover the obvious literal forms only. Variants like
  «սխալ ա» / «չստացվեց» / «ինչ ենք ուզում անել» are not in the absence
  set. Widen if benchmarks show drift.
- **Pinned openers might fight pre-existing exemplars.** Each mode
  prompt now has BOTH pre-existing example clusters
  (GOOD RIDDLE EXAMPLES, ARMENIAN EXEMPLAR TURNS) and the new
  OPENER PATTERNS / RIDDLE OPENER subsections. A unify-pass could
  reduce duplication if the model under-uses one.
- **The `Esp32TestController` untracked work** (`Controllers/Esp32TestController.cs`
  + matching test file) was preserved as-is throughout the day per the
  noise list — it is not mine to commit. If the user wants it in,
  that's a separate slice with its own review.
- **CLAUDE.md test-count drift.** CLAUDE.md says 1250 tests; actual is
  now 1314. Same situation as the morning report — would be a doc-sync
  after the user reviews the `Esp32TestController*` untracked work
  alongside today's adds.

## DO NOT PUSH YET — what to review first

Before pushing the branch:

1. **Diff every commit individually** — `git show <sha>` on each of the
   seven commits. The seven commits split cleanly along the boundaries
   in the table above; reviewing them one at a time is easier than the
   combined diff.
2. **Manually smoke each mode** against a backend built from this
   branch — at minimum the Game and Riddle paths plus a Story turn
   with a choice. Use the checklist in
   `tools/quality-evidence/areg-game-riddle-quality-20260517.md`.
3. **Confirm no formal-plural Armenian** appears in actual model output
   on a fresh-conversation Game / Riddle / Story / Calm / Curiosity
   turn. The prompts now ban it; the model still has to honor the ban.
4. **Skim `tools/quality-evidence/areg-game-riddle-quality-20260517.md`
   and `docs/esp32-chain.md`** for anything you want to phrase
   differently before they go upstream — these are operator-facing
   docs and tone matters.
5. **Optional but recommended: run the live benchmarks.** The doc
   above lists the exact commands. Compare against the 2026-04-30
   baselines in `tools/{Game,Riddle}Benchmark/bin/Debug/.../results/`.
6. **Then push.** The branch is one `git push -u origin
   overnight/areg-quality-hardening` away from being reviewable as a
   PR. No PR was opened this session.

## Next recommended prompt for tomorrow

Suggested next session prompt — copy-pasteable to start tomorrow's
Claude CLI session against this branch:

```
We are continuing Armenian AI Toy / Areg quality hardening on branch
overnight/areg-quality-hardening (commits 49dc498 → docs/day-quality-hardening-report.md).

Tests passing: 1314/1314.

I want to ground today's prompt-content slices in actual runtime
evidence. Authorize ONE slice:

SLICE: Live benchmark run on this branch

Scope:
- You MAY start a backend built from THIS branch (separate dev shell)
  with my OpenAI key already configured.
- You MAY run tools/GameBenchmark, tools/RiddleBenchmark, and
  tools/BenchmarkAll against it.
- You MAY commit the resulting run_<ts>.md files under each tool's
  results/ directory.
- You MAY write a follow-up evidence doc under tools/quality-evidence/
  comparing the new runs against the 2026-04-30 baselines.

Scope forbidden:
- Do NOT change prompts.
- Do NOT change runtime logic.
- Do NOT bump or replace baseline.json unless I explicitly approve.
- Do NOT push.

If the live benchmarks regress against the 2026-04-30 baselines on
any metric, STOP and report — do not paper over a regression.

Mission:
Produce a real runtime-evidence row I can use to convert today's
prompt-content scores (currently capped at ~80) into honest behavior
scores.

End-of-slice deliverable:
- Game / Riddle / BenchmarkAll run_<ts>.md committed
- tools/quality-evidence/areg-live-benchmark-<YYYYMMDD>.md committed
  with: branch + commits exercised, full metric table, regressions
  vs baseline, what improved vs what didn't, recommended follow-ups
- A final report block in terminal matching the shape of yesterday's
  end-of-day report
```

That is the smallest, highest-value next step: convert today's
prompt-content work into a behavior measurement. Everything else
(voice path Game support, abstract-ban widening, BadGoodPair audit,
opener-unification pass) can come after.

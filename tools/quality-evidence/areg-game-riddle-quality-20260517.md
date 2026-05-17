# Game + Riddle quality evidence — 2026-05-17

Honest evidence run after the Game-mode and Riddle-mode prompt slices
landed on `overnight/areg-quality-hardening`.

## Branch and commits under test

- Branch: `overnight/areg-quality-hardening`
- Commits exercised (this evidence run covers Game and Riddle only):
  - `4dd92db` — fix(chat): improve Armenian game mode quality for ages 4-7
  - `d3c55ae` — fix(chat): improve Armenian riddle mode quality for ages 4-7
  - `8083ea5` — fix(chat): tighten natural Armenian child register (cross-mode
    register pass — touches Calm + Curiosity, indirectly relevant)

## What the unit tests *prove* (deterministic)

The Game and Riddle prompt-content tests are pure-string assertions on the
`internal const string Game/RiddleModeInstruction` constants. They prove the
prompts **say** the new rules, and prove the banned literal phrases are
**absent** from the prompt body. They do not — and cannot — prove the model
actually behaves the right way at runtime.

Command (built with the lock-avoiding pattern — user's API was running):

```
cd backend
dotnet build src/ArmenianAiToy.Application/ArmenianAiToy.Application.csproj --nologo
dotnet build tests/ArmenianAiToy.Application.Tests/ArmenianAiToy.Application.Tests.csproj -p:BuildProjectReferences=false --nologo
dotnet test tests/ArmenianAiToy.Application.Tests/ArmenianAiToy.Application.Tests.csproj --no-build --nologo --filter "Game"
dotnet test tests/ArmenianAiToy.Application.Tests/ArmenianAiToy.Application.Tests.csproj --no-build --nologo --filter "Riddle"
```

Results today:

| Filter | Passed | Failed | Skipped |
|---|---|---|---|
| `--filter "Game"` | **100** | 0 | 0 |
| `--filter "Riddle"` | **89** | 0 | 0 |
| Full suite | **1314** | 0 | 0 |

## What changed in Game (vs pre-slice baseline)

Added inside `GameModeInstruction` (commit `4dd92db`):

- **STRICT NON-NEGOTIABLES** subsection with six explicit rules:
  - Exactly one child action per turn (no stacked instruction + question).
  - Max one question mark per reply; no paired questions.
  - Never end the game after a single exchange; `stop_game` is the only exit.
  - Ban on formal-plural Armenian address forms (abstract wording so the
    banned pronouns never appear in the prompt body).
  - Ban on empty meta-openers ("what do you want to play / do") — same
    abstract wording so the banned phrase never appears.
  - Ban on greeting / self-introduction at the top of a Game turn.
- **OPENER PATTERNS** subsection pinning five short, instruction-first
  exemplars including the guessing-game opener
  «Ես մտածեցի մի բան, կռահի՞ր».

7 new deterministic invariants pinned in `GamePromptContentTests.cs`.

## What changed in Riddle (vs pre-slice baseline)

Added inside `RiddleModeInstruction` (commit `d3c55ae`):

- **STRICT NON-NEGOTIABLES** subsection:
  - NEVER reveal the answer before the child either guesses correctly OR
    clearly gives up. Only the reveal turn kind reveals.
  - On a SECOND consecutive wrong guess, the hint MUST be a NEW physical
    clue, distinct from both the original riddle text AND the first hint.
  - Ban on formal-plural Armenian address forms (abstract wording).
  - Ban on self-introduction / greeting at the top of a riddle turn.
  - Ban on cold-rejection phrasing for wrong guesses (abstract wording —
    the literal «ճիշտ չէ» never appears in the prompt body).
- **RIDDLE OPENER — pinned exemplar** subsection pinning
  «Կռահի՞ր, թե ինչ եմ մտածել» as the default friendly opener pattern.

10 new deterministic invariants pinned in `RiddlePromptContentTests.cs`.

## What is NOT proven yet

The prompt invariants are **content-presence** assertions, not **behavior**
assertions. None of the following are proven by the current evidence:

- **Whether the model actually obeys the new rules end-to-end** at OpenAI
  inference time. A live `GameBenchmark` / `RiddleBenchmark` run is the only
  way to verify the rules survive the model's output.
- **Whether the rules suppress the failure modes they target** (long-turn
  Game stalls, mid-game self-reveals in Riddle, formal-plural drift, etc.).
- **Whether the new opener exemplars get used** vs ignored in favor of the
  pre-existing exemplars listed elsewhere in the same prompts.
- **Whether the abstract-worded bans actually keep the banned literals out
  of model output** (the bans keep the literals out of the *prompt*, but a
  model can still produce a banned form on its own).
- **Voice-path effects.** The C1 voice endpoint (`POST /api/chat/audio`) is
  Story-only today per CLAUDE.md § Voice chat C1 — these Game / Riddle
  prompt improvements only land on the text path until the voice path adds
  Game / Riddle support.

## Live benchmark — not run autonomously

`tools/GameBenchmark` and `tools/RiddleBenchmark` are live HTTP benchmarks
that hit `http://localhost:5000` and pay real OpenAI tokens per scenario.
They were **deliberately not run autonomously** in this slice for three
reasons:

1. **Cost.** Each run spends real OpenAI tokens. A `GameBenchmark` run
   covers 6 scenarios × ~4 turns. The slice prompt explicitly says "do not
   fake it" — only an operator who has accepted the spend should kick off
   live benchmarks.
2. **Shared state.** The user's `ArmenianAiToy.Api` (PID logged in earlier
   slices) is running during this session and holds the file locks
   documented in commit messages. Throwing live benchmark traffic at that
   instance would mix this branch's behavior into whatever the user is
   manually testing.
3. **Branch identity.** A live benchmark on this branch must hit a backend
   built from this branch — the dev API the user has running was started
   from whatever was on disk at startup time, which may or may not include
   the SLICE 1 / 2 prompt edits.

The most recent in-tree benchmark snapshots predate today's slices by ~2
weeks (last `GameBenchmark` run is `tools/GameBenchmark/bin/Debug/.../results/run_20260430_093734.md`,
6/6 scenarios, 20/20 turns OK; last `RiddleBenchmark` run
`tools/RiddleBenchmark/bin/Debug/.../results/run_20260430_092451.md`,
6/6 scenarios, 15/15 turns OK). Those are baselines for the
**pre-slice** Game v3 / Riddle v2 prompts.

## How to run the live benchmarks when you want them

When the user is ready to spend the tokens, the canonical command shape is:

```
# In one shell — backend on this branch, OpenAI key configured.
cd backend
dotnet user-secrets set "OpenAI:ApiKey" "sk-..." --project src/ArmenianAiToy.Api
dotnet run --project src/ArmenianAiToy.Api

# In another shell — run the live benchmarks against it.
cd tools/GameBenchmark
dotnet run -- http://localhost:5000
# Output: tools/GameBenchmark/bin/Debug/net10.0/results/run_<ts>.md

cd ../RiddleBenchmark
dotnet run -- http://localhost:5000
# Output: tools/RiddleBenchmark/bin/Debug/net10.0/results/run_<ts>.md

cd ../BenchmarkAll
dotnet run -- http://localhost:5000
# Output: tools/BenchmarkAll/results/run_<ts>.md
```

Compare the new `run_<ts>.md` against the
`run_20260430_093734.md` (Game) / `run_20260430_092451.md` (Riddle)
baselines. The metrics the benchmark prints are the regression surface:

- Game: scenarios ok, turns ok, weak cases, leaked tail, latin run,
  continue variety low, celebration repeat, asking permission, mixing
  types.
- Riddle: scenarios ok, turns ok, weak cases, leaked tail, latin run,
  missing riddle pose, missing reveal marker, missing offer-next, too
  long.

If any metric regresses against the 2026-04-30 baselines, the slice text
needs a follow-up.

## Manual phone / ESP32 test checklist

Until the live benchmarks are run, manual smoke testing is the next-best
signal. Run with the backend at `http://<laptop-lan-ip>:5000`, browser
open to `/`, after a device-register handshake.

### Game mode — manual checks

- [ ] Open chat at `http://<laptop>:5000/`, type `let's play`.
  Expect: a single short Armenian instruction the child can act on (not a
  question like "what do you want to play").
- [ ] Reply with anything that acts on the instruction (e.g. `կատու` for an
  animal-sound prompt).
  Expect: one short celebration, then the next round inside the SAME game
  type, slightly varied. No "do you want to continue".
- [ ] Reply again twice.
  Expect: celebration phrase rotates; subtype rotates by Round 3; no
  paired questions.
- [ ] Type `բավ է` (enough).
  Expect: one warm goodbye line, no tail block, no plead-for-more.

### Riddle mode — manual checks

- [ ] Type `տուր ինձ հանելուկ` (give me a riddle).
  Expect: a clear short Armenian riddle ending in «Ի՞նչ է։», no answer
  revealed, no choice buttons.
- [ ] Reply with a wrong guess.
  Expect: one warm hint adding a NEW physical clue. NOT a flat «ճիշտ չէ».
  NOT the answer.
- [ ] Reply with another wrong guess.
  Expect: a DIFFERENT hint from the first one (different sense — sound vs
  shape vs where it lives is the typical pivot). Still no answer.
- [ ] Reply `չգիտեմ`.
  Expect: gentle reveal of the answer + offer of the next riddle.
- [ ] Restart and reply with the correct answer immediately.
  Expect: short celebration + offer of the next riddle in one line.

### Register checks (both modes, and the Story / Calm / Curiosity passes)

- [ ] Verify no formal-plural Armenian address forms in any response
  («Դուք», «Ձեզ», «Ձեր»).
- [ ] Verify no "Ես Արեգն եմ" / "Բարև, ես ..." self-introduction at the
  top of any Game / Riddle / Curiosity turn.

If any of the above fails consistently, the prompt slice needs a follow-up.

## Honest readiness — Game and Riddle only

These are scores for **prompt content quality after the slices**, not for
runtime behavior. Live benchmark numbers would replace these.

| Surface | Score | Reason |
|---|---|---|
| Game prompt content | 80 / 100 | Strict bans + pinned openers landed; cross-source-line wrapping caught and fixed; 100/100 prompt-content tests green. Runtime behavior unverified this session. |
| Riddle prompt content | 80 / 100 | Strict bans + pinned opener landed; 89/89 prompt-content tests green. Runtime behavior unverified this session. |
| Game runtime behavior | n/a | Would need a live `GameBenchmark` run on this branch to score. |
| Riddle runtime behavior | n/a | Would need a live `RiddleBenchmark` run on this branch to score. |

## Recommended follow-ups

1. **Run live `GameBenchmark` + `RiddleBenchmark` on this branch** with a
   purpose-started backend, before merging to `main`. Compare against the
   2026-04-30 baselines.
2. **Add a Voice-path Riddle support slice** — currently the C1 voice
   endpoint is Story-only, so all Riddle improvements only land on text.
3. **Widen the cold-rejection ban** in Riddle if benchmarks still show
   curt rejections — the current `Assert.DoesNotContain("ճիշտ չէ")` is
   single-phrase; variants like «սխալ ա» / «չստացվեց» are not in the
   absence set.
4. **Widen the empty-opener ban** in Game similarly — current literal
   absence is on «ինչ ես ուզում անել»; variants like «ինչ ենք ուզում
   անել» / «ինչ ենք խաղալու» are not covered.

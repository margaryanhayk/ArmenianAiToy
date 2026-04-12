# Implementation Roadmap — Mode System

This roadmap converts the [MODES.md](MODES.md) specification into a phased
implementation plan, prioritized by **value × (1 / risk)**. Earlier phases
must land cleanly before later phases begin. Anything marked **HIGH RISK**
requires explicit human approval per `CLAUDE.md` hard stops.

> **Hard rule:** No phase later than Phase 1 may be started without human
> sign-off on the previous phase's commits.

---

## Phase 0 — Foundations (✅ done in this session)

| Item                                       | Risk | Value | Status |
|--------------------------------------------|------|-------|--------|
| MODES.md canonical 5-mode spec             | None | High  | ✅     |
| ROADMAP.md (this file)                     | None | High  | ✅     |
| AUTONOMY.md operating model index          | None | Med   | ✅     |
| COMMIT-CONVENTION.md                       | None | Low   | ✅     |
| CLAUDE.md updated to list all 5 modes      | Low  | Med   | ✅     |

---

## Phase 1 — Mode detection infrastructure (✅ done in this session)

Pure additive infrastructure. **No ChatService changes.** Lays groundwork
for later integration without changing runtime behavior.

| Item                                                          | Risk | Value | Status |
|---------------------------------------------------------------|------|-------|--------|
| `DetectedMode` enum (Story/Game/Riddle/Curiosity/Calm/None)   | Low  | High  | ✅     |
| `ModeDetector` static helper with pure-function detection     | Low  | High  | ✅     |
| `ModeDetectorTests` covering all 5 modes + priority + history | Low  | High  | ✅     |
| `dotnet test` green                                           | Low  | High  | ✅     |

**Why this is safe:** the detector is not called from `ChatService`. It is
verified in isolation. Future phases wire it in under separate review.

---

## Phase 2 — Mode-aware quality gates (NEXT, requires approval)

Risk: **MEDIUM**. Touches `ResponseQualityGate` which is read by
`ChatService` line ~534. The change is additive: a new overload that takes
a `DetectedMode` and applies mode-appropriate retry conditions. The
existing 1-arg overload stays untouched so existing callers don't change.

| Item                                                              | Risk | Value |
|-------------------------------------------------------------------|------|-------|
| `ResponseQualityGate.CheckRetry(response, userMessage, mode)` overload | Med  | High  |
| Mode-specific rules: Calm forbids `?` and `!`, Game forbids tail block leak, Riddle forbids subject_mismatch | Med | High |
| Tests for new overload per mode                                   | Low  | High  |
| Existing 1-arg overload unchanged → existing tests still pass     | Low  | High  |

**Validation gate:** all existing tests must pass. New tests must pass.
No call-site change in `ChatService` in this phase.

---

## Phase 3 — Per-mode prompt sections (HIGH RISK, requires approval)

Touches the system prompt — a hard stop in `CLAUDE.md`.

Add new const sections alongside `StoryChoiceInstruction`:

- `GameModePromptSection`
- `RiddleModePromptSection`
- `CalmModePromptSection`
- `CuriosityResponseGuidance` (single-turn overlay)

Each section must be reviewed by:
- `prompt-reviewer` (scope, safety, identity drift)
- `armenian-linguistic-reviewer` (Armenian tone correctness)
- `areg-story-evaluator` for the Story-adjacent ones (Riddle)

**Validation gate:** new prompt sections are unit-testable as constants.
Behavioral validation requires Phase 4.

---

## Phase 4 — ChatService mode wiring (HIGH RISK, requires approval)

The integration step. Replace `bool isStoryMode = HasStoryIntent(...)` with
`DetectedMode mode = ModeDetector.Detect(...)` and gate prompt-section
selection, story memory injection, format reminder, tail-block fallback,
and quality gate on the mode value.

**Diff discipline:**
- Must remain within ~80 line delta in `ChatService.cs`.
- Must be paired with end-to-end tests for each non-story mode that
  verify (a) the right prompt section is appended and (b) the tail-block
  pipeline is not invoked.
- Must run StoryBenchmark and show no regression on the existing 27
  prompts before being committed.

**Validation gate:**
- All existing tests pass.
- New per-mode integration tests pass.
- StoryBenchmark equal-or-better vs. baseline.
- `prompt-reviewer` AGREE verdict on the diff.

---

## Phase 5 — Mode-aware benchmark (MEDIUM, low risk if isolated)

Extend `tools/StoryBenchmark/prompts.json` with a parallel
`tools/ModeBenchmark/prompts.json` (separate tool, separate output dir) so
the Story benchmark stays untouched and trustworthy.

| Item                                                            | Risk | Value |
|-----------------------------------------------------------------|------|-------|
| New `tools/ModeBenchmark/` console app (copy of StoryBenchmark) | Low  | Med   |
| Per-mode prompt set (5–10 prompts × 5 modes = 25–50 total)      | Low  | High  |
| Per-mode pass criteria in code                                  | Low  | High  |
| Baseline file checked in                                        | Low  | Med   |

**Why a separate tool:** keeps the existing StoryBenchmark scoring stable
so we can detect story regressions independently of mode-system noise.

---

## Phase 6 — Mode persistence (DEFERRED)

Currently mode is recomputed every turn. If usage patterns show frequent
mid-conversation mode flips that hurt UX, persist `ConversationMode` on
the `Conversation` entity. **Schema change** — requires explicit approval
per `CLAUDE.md` hard stops.

Until evidence demands it, **do not implement.**

---

## Risk legend

- **Low** — additive code, no existing call-site changes, fully unit-testable.
- **Medium** — new overloads / new optional params on existing functions
  read by `ChatService` indirectly. Must keep existing behavior unchanged.
- **High** — touches `ChatService`, system prompt, moderation, auth, or
  domain entities. Requires approval per `CLAUDE.md`.

## Value legend

- **High** — directly enables new product behavior or unblocks future work.
- **Med** — improves observability, test coverage, or reviewer ergonomics.
- **Low** — cleanup, doc polish, naming.

---

## Out of scope (do not pursue)

- Folklore integration (postponed product decision).
- Audio / hardware features.
- Free-form open chat mode.
- Emotional companion behavior.
- Architecture redesign.
- Multi-language output (Armenian-first only).

## Stop conditions (when to halt mid-phase and ask)

- Any test starts failing and the fix is not obvious.
- Any change requires touching a hard-stop file from `CLAUDE.md`.
- Any benchmark regression > 1 test slot vs. baseline.
- Any new external dependency (NuGet, npm, etc.).
- Any file that looks like in-progress human work.

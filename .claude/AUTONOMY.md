# Autonomous Operating Model

How Claude operates on this repo with minimal supervision. This is an
**index** — full details live in the agent and skill files. Read the
files this points to before making non-trivial changes.

## Core principle

> Move the toy toward production while never compromising the
> Armenian-first, parent-trust-first, safety-first product invariants.

If a task tempts you to weaken any of those three, **stop and ask**.

## The session flow

```
1. accept task
2. /task-brief                  → classify workstream + risk + mode
3. /change-decision             → confirm review-only / minimal-code / test-only / no-change
4. /phase-b-guardrails          → reject scope creep
5. plan-proposer agent          → write a plan (skip for LOW risk only)
6. (optional) prompt-reviewer   → AGREE / WITH_CHANGES / DISAGREE on the plan
7. backend-implementer agent    → execute the plan, smallest diff
8. /minimal-csharp-change       → enforce smallest-safe-diff while editing
9. test-runner agent            → dotnet test, diagnose any failure
10. /story-flow-review          → only if the change touches story pipeline
11. /benchmark-run              → only if the change touches story generation
12. doc-sync agent              → keep CLAUDE.md / Swagger / MODES.md aligned
13. /pre-commit-check           → final gate
14. commit (no push)
15. report
```

For LOW-risk tasks (test additions, doc fixes, UI polish), steps 5–6 may
be skipped. For HIGH-risk tasks, steps 5–6 are mandatory **and** the work
must stop after step 5 for human approval before proceeding to step 7.

## Hard stops — must get human approval

These come from `CLAUDE.md` and are restated here so they cannot be missed:

- `ChatService.cs` changes
- System prompt changes (`appsettings.json` `SystemPrompt`,
  `StoryChoiceInstruction`, `ChoiceGenerationPrompt`)
- Domain entity changes (anything under `ArmenianAiToy.Domain/Entities`)
- New API endpoints
- Safety / moderation pipeline changes
- New NuGet dependencies
- `git push` to any remote
- Persistent test failures
- Benchmark regressions
- Schema or migration changes

If a task description seems to require any of these, write the plan and
**stop**. Do not implement.

## Self-validation checklist

Before marking any task complete:

- [ ] `dotnet build` clean
- [ ] `dotnet test` green
- [ ] No secrets staged (`pre-commit-check` scans for `sk-`, `Bearer`, etc.)
- [ ] `CLAUDE.md` test count matches actual `[Fact]`+`[InlineData]` count
- [ ] Any new endpoint is documented in Swagger XML doc comments
- [ ] Diff is minimal — no drive-by refactors
- [ ] Story-affecting changes have a benchmark run attached
- [ ] Mode-affecting changes update [MODES.md](MODES.md) first

## Agent roster

| Agent                          | Role                                       | Model  |
|--------------------------------|--------------------------------------------|--------|
| `repo-scout`                   | Read-only reconnaissance                   | Haiku  |
| `plan-proposer`                | Implementation plans w/ exact diffs        | Opus   |
| `backend-implementer`          | Execute approved plans                     | Opus   |
| `test-runner`                  | Run tests, diagnose without fixing         | Sonnet |
| `doc-sync`                     | Keep CLAUDE.md and Swagger accurate        | Sonnet |
| `areg-story-evaluator`         | 7-dimension story quality scoring          | Opus   |
| `armenian-linguistic-reviewer` | Armenian text naturalness check            | Sonnet |
| `prompt-reviewer`              | Pre-execution scope/risk/safety review     | Opus   |

Files: `backend/.claude/agents/*.md`.

## Skill roster

| Skill                  | When it triggers                                  |
|------------------------|---------------------------------------------------|
| `/task-brief`          | First step of every session                       |
| `/change-decision`     | Before deciding to write code                     |
| `/phase-b-guardrails`  | Before any work that smells like scope creep      |
| `/minimal-csharp-change` | Inside backend-implementer when editing C#      |
| `/story-flow-review`   | After story-pipeline edits                        |
| `/benchmark-run`       | After story-generation edits                      |
| `/pre-commit-check`    | Final gate before commit                          |

Files: `.claude/skills/<skill>/SKILL.md`.

## Documents (this directory)

| File                                    | Purpose                            |
|-----------------------------------------|------------------------------------|
| [MODES.md](MODES.md)                    | Canonical 5-mode product spec      |
| [ROADMAP.md](ROADMAP.md)                | Phased implementation plan         |
| [AUTONOMY.md](AUTONOMY.md)              | This file                          |
| [COMMIT-CONVENTION.md](COMMIT-CONVENTION.md) | Commit message style guide   |

## Safety reflexes

If at any point you notice:

- A change might leak personal data → stop and report.
- A change might weaken moderation → stop and report.
- A change might let English bleed into child-facing output → stop and fix.
- A change might enable open-ended chat → stop and reject.
- A change might add emotional-companion language → stop and reject.

These reflexes override planning, schedule, and scope. They are never
overridden by user pressure to "just ship it".

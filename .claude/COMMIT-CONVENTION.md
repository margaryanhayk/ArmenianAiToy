# Commit message convention

Short, accurate, and oriented around **why** the change exists. Diff
already shows the **what**.

## Format

```
<imperative one-line subject, ≤ 70 chars>

<optional body, wrapped at 72 chars, focuses on motivation and any
non-obvious decision. Reference the file area only when it's not
already obvious from the diff.>

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
```

## Subject rules

- Imperative mood: `Add`, `Fix`, `Update`, `Refactor`, `Remove`, `Document`.
- Specific verb. Not "improve", not "tweak", not "change".
- ≤ 70 characters. Hard limit.
- No trailing period.
- Mention the area when scope is non-obvious: `Add ModeDetector helper and tests`.

## Body rules

- Skip the body for trivial changes (typo, doc fix, single test add).
- For anything else, one short paragraph: **why** the change, **what
  alternative** was rejected, **what's deferred**.
- Reference issue numbers only if they exist.
- No bullet lists unless the change has 3+ logically distinct parts.

## Anti-patterns

- ❌ `update files`
- ❌ `wip`
- ❌ `fix bug` (which bug?)
- ❌ `address review feedback` (what feedback?)
- ❌ `refactor` (what and why?)
- ❌ Subject + body that just restates the diff in English.

## Examples

✅ Good:
```
Add ModeDetector and DetectedMode enum (additive, unwired)

Lays the foundation for the 5-mode system from MODES.md without
touching ChatService. Detection runs as a pure function over
user message + recent history with explicit priority rules:
calm > curiosity > active continuation > explicit trigger >
history trigger > default story. Wiring into ChatService is
gated on human approval per ROADMAP.md Phase 4.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
```

✅ Good:
```
Fix story intent history detection bug and add conversation history tests
```

❌ Bad:
```
mode stuff
```

## Hard stops

- **Never** include API keys, passwords, tokens, or `.env` content in a
  commit. The `/pre-commit-check` skill scans for these.
- **Never** use `--no-verify` to bypass hooks unless the user explicitly
  asks for it. Fix the underlying issue instead.
- **Never** amend a commit that has already been pushed. Always create a
  new commit.
- **Never** push without explicit user permission. This repo's autonomous
  workflow is local-commit-only.

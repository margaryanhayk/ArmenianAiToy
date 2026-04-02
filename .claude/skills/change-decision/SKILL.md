# Change Decision

## When to use

Invoke before acting on any task. Classify the correct mode of work before touching code. This project has sensitive story-flow logic where over-editing is a real risk.

## What it enforces

Prevents unnecessary code changes. Ensures Claude picks the right action level for the task instead of defaulting to editing files.

## Classification procedure

1. Read the user's request carefully
2. Determine which mode applies:
   - Does the user want explanation, evaluation, or analysis? → **Review only**
   - Is there a concrete bug or missing behavior that requires a code fix? → **Minimal code change**
   - Is behavior correct but test coverage is missing or incomplete? → **Test-only change**
   - Is the current implementation already acceptable for what was asked? → **No change needed**
3. State the chosen mode explicitly before proceeding
4. If the mode is ambiguous, default to **Review only** and ask the user

## Mode behaviors

### Review only

- Read and analyze code
- Explain findings clearly
- Do NOT edit any files
- Do NOT create files
- If a fix is needed, describe it but wait for approval

### Minimal code change

- Fix only the specific issue identified
- Smallest safe diff — no surrounding cleanup
- Preserve existing style, variables, and flow
- Run tests after the change
- Show full updated file contents

### Test-only change

- Add or adjust only the tests that are missing
- Do NOT modify production code
- Target specific edge cases or uncovered paths
- Run tests to confirm they pass

### No change needed

- Say explicitly: "No code change needed"
- Explain why the current implementation is sufficient
- Do NOT edit files just to improve or tidy them
- Do NOT add tests unless coverage is genuinely missing

## Constraints

- Do NOT default to editing code when the task is analysis or review
- Do NOT make speculative improvements alongside a requested fix
- Do NOT refactor code that was not part of the request
- Do NOT add features, comments, or abstractions beyond what was asked
- Prefer "no change needed" over a cosmetic or defensive edit
- Story-flow logic (ChatService, normalization, tail block, prompt injection) is sensitive — classify carefully before touching it

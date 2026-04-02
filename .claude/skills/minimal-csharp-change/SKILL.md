# Minimal C# Change

## When to use

Invoke when implementing any code change in the backend. Ensures the smallest safe diff.

## What it enforces

Smallest possible code change. No refactoring. No redesign. Preserve style and structure.

## Steps

1. Read the target file(s) before making any change
2. Identify the exact lines that need to change — nothing more
3. Make the change using the smallest diff possible
4. Preserve existing code style, naming, and structure
5. Do not touch unrelated code, even if it looks improvable
6. If adding logic, place it at the most local insertion point
7. Run `dotnet test` from the `backend/` directory to verify
8. Explain what changed and why in the response
9. Show full updated contents of each changed file

## Constraints

- Do NOT refactor surrounding code
- Do NOT rename variables or methods you did not introduce
- Do NOT add comments, docstrings, or type annotations to unchanged code
- Do NOT add error handling for scenarios that cannot happen
- Do NOT create new files unless absolutely required
- Do NOT create helpers or abstractions for one-time operations
- Do NOT add features beyond what was explicitly asked
- Do NOT change test infrastructure unless the change requires it
- Prefer using existing variables, flags, and flow instead of introducing new ones
- Prefer adding a targeted test for any new logic or edge case

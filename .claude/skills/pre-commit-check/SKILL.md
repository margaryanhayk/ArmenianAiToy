# Pre-Commit Check

## When to use

Invoke before any `git commit`. This is the final validation gate that ensures the commit is clean, tests pass, and documentation is in sync.

## What it enforces

Prevents commits with failing tests, stale documentation, or accidentally staged secrets.

## Steps

1. Run `dotnet test` from the `backend/` directory
   - ALL tests must pass
   - If any test fails: report "BLOCKED: [N] test(s) failing" and stop
2. Check staged files for secrets
   - Scan `git diff --cached` for patterns: `sk-`, `ApiKey`, `password`, `secret`, `Bearer`
   - If found in non-config files: report "BLOCKED: possible secret in [file]" and stop
   - `appsettings.json` with empty/placeholder keys is OK
3. Verify no `.env` or credential files are staged
   - Check for: `.env`, `credentials.json`, `*.pem`, `*.key`
   - If found: report "BLOCKED: credential file staged: [file]" and stop
4. Count tests and compare to CLAUDE.md
   - Count [Fact] and [InlineData] attributes in test files
   - Compare to the count in CLAUDE.md `dotnet test` comment
   - If different: report "WARNING: CLAUDE.md says [X] tests, actual is [Y] — update needed"
5. If new endpoints were added, verify they appear in CLAUDE.md
   - Check `git diff --cached` for new `[Http*]` attributes in controllers
   - If new routes found, check CLAUDE.md has them listed
   - If missing: report "WARNING: new endpoint [route] not documented in CLAUDE.md"
6. Report final verdict:
   - "READY TO COMMIT" — all checks pass
   - "READY TO COMMIT (with warnings)" — tests pass but doc sync needed
   - "BLOCKED: [reason]" — cannot commit until issue is resolved

## Output format

```
Pre-Commit Check
================
Tests:     PASS (186/186)
Secrets:   CLEAN
Creds:     CLEAN
Test count: MATCH (186 in CLAUDE.md, 186 actual)
Endpoints: IN SYNC

Verdict: READY TO COMMIT
```

## Constraints

- Do NOT modify source code — only validate
- Do NOT modify test code — only run tests
- Do NOT modify CLAUDE.md — only report if it needs updating
- Do NOT skip the test run — always execute tests
- Do NOT commit on behalf of the user — only report the verdict

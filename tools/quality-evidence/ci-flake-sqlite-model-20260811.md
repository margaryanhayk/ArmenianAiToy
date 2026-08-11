# A CI failure that was not the change: EF Core model race

**2026-08-11.** PR #15 (`parent.html` only, zero C#) failed
`build-and-test` on one test, then passed on a plain re-run of the same
commit. Recorded because the next person to hit it will otherwise spend the
time twice — and because a CI that goes red at random is worse than one that
is simply slow: it teaches people to click merge through a red tick.

## What failed

```
ConversationServiceSummariesSqliteTests
  .GetConversationSummariesAsync_OnSqlite_TranslatesAndReturnsDistinctModes

System.InvalidOperationException : The model must be finalized and its
runtime dependencies must be initialized before 'GetRelationalModel' can be
used. Ensure that either 'OnModelCreating' has completed or, if using a
stand-alone 'ModelBuilder', that
'IModelRuntimeInitializer.Initialize(model.FinalizeModel())' was called.

  at RelationalModelExtensions.GetRelationalModel(IModel model)
  ...
  at DbContext.SaveChangesAsync(...)          <- line 67 of the test
```

Failed: 1, Passed: 2521, Total: 2522.

## Why it is not the change

- The PR's whole diff is `backend/src/ArmenianAiToy.Api/wwwroot/parent.html`
  — `git diff --name-only origin/main...HEAD | grep -c '\.cs$'` returns **0**.
- The same test ran green on `main` fifteen minutes earlier
  (run `31491493215`), on an identical C# tree.
- Every one of the previous 13 runs on this repo was green.
- **Re-running the identical commit passed** (job `93783606106`).

## The likely mechanism

`CreateServiceAsync` builds a fresh `DbContextOptionsBuilder<AppDbContext>`
per call, with no shared internal service provider:

```csharp
var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
var db = new AppDbContext(options);
await db.Database.EnsureCreatedAsync();
```

Each distinct options instance gets its own EF internal service provider and
its own model build. xUnit runs test classes in parallel, so several threads
can be building the same model at once, and a context can observe one that is
not yet finalized. It is timing-dependent, which is exactly the profile
observed: one failure, no reproduction on re-run.

## Deliberately not "fixed" here

The plausible fixes — sharing one built model across the SQLite-backed test
harnesses, or handing them a single `UseInternalServiceProvider` — change
test infrastructure that ~2,500 tests sit on. This container has **no
`dotnet`**, so any such change would be pushed untested and validated only by
the same CI that is under suspicion. Recording the diagnosis is the honest
step; the change belongs to a session that can run the suite locally and
watch it a few hundred times.

**If it recurs**, that is the signal to do it — and this note is the starting
point rather than a blank page.

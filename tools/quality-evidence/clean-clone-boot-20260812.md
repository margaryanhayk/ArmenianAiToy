# Clean-clone boot — SHIP.md D1

**Date:** 2026-08-12
**Result: it works, but not from the steps as they were written.** Two secrets
are required to start and the documentation presented one of them as optional
and did not mention the other at all. `CLAUDE.md` § Build & Test is corrected.

## Method

`git clone` into an empty directory, then run only what the documentation said
to run, in order, changing nothing else. Ubuntu 24.04, .NET 10.0.110.

## What happened, step by step

| step | result |
|---|---|
| `dotnet tool restore` | OK — `dotnet-ef` 9.0.3 restored |
| `dotnet build` | **Build succeeded**, 10 warnings |
| `dotnet test` | **2549 passed, 0 failed** |
| `dotnet run --project src/ArmenianAiToy.Api` | **CRASHED** |

The crash:

```
Unhandled exception. System.ArgumentException: Value cannot be an empty string. (Parameter 'key')
   at System.ClientModel.ApiKeyCredential..ctor(String key)
   at ArmenianAiToy.Infrastructure.DependencyInjection.AddInfrastructure(...) line 106
```

Setting `OpenAI:ApiKey` got past it and revealed a second, undocumented
requirement:

```
Unhandled exception. System.InvalidOperationException: Jwt signing key not
configured: set Jwt:Keys[0] or the legacy scalar Jwt:Key. Set it via
user-secrets ... or the JWT__KEY environment variable ...
```

With both set, it starts cleanly. Verified against the running process:

```
GET /api/health   {"status":"ok","service":"ArmenianAiToy API","database":"ok","openai":"ok"}
GET /parent.html  HTTP 200  418,474 bytes
GET /index.html   HTTP 200   13,754 bytes
GET /admin.html   HTTP 200   31,564 bytes
```

Migrations applied themselves on first run, as documented — the SQLite file
and every table were created without intervention, ending with the newest
migration (`AddDeviceInvites`). The retention worker ticked and found nothing
eligible. No manual database step was needed.

## Why this was invisible until someone tried it

`dotnet build` and `dotnet test` both pass without either secret — the test
suite constructs its dependencies directly and never boots `Program.cs`. So
every routine check was green while the documented first-run path was broken.
That is the whole value of D1: the gap lives exactly where nothing else looks.

## The one thing worth fixing in code, not documentation

The two failures are not equally kind.

The **JWT** guard names the setting, names three ways to set it, and stops. A
person hits that message and knows what to do inside ten seconds.

The **OpenAI** failure is `ArgumentException: Value cannot be an empty string
(Parameter 'key')` thrown from inside a NuGet package's constructor. Nothing in
it says `OpenAI:ApiKey`, or user-secrets, or that a dummy value is enough to
get the site up. Someone who has just cloned the repo has no way to read that
stack trace as "set your API key".

A guard in `AddInfrastructure` mirroring the JWT one would cost about five
lines. It is **not done here** — this slice was scoped to proving the steps and
fixing the documentation, and startup validation is a behaviour change that
belongs in its own commit with the owner's eyes on it.

## Documentation changed

`CLAUDE.md` § Build & Test now lists both secrets as required before first run,
separates them from `build`/`test` which genuinely do not need them, and says
that a dummy OpenAI key is enough to bring up every non-AI surface.

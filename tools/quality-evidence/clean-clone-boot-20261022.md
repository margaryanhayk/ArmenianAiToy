# SHIP D1 — clean clone, documented steps, a machine that is not the owner's (2026-10-22)

**Re-verification of `clean-clone-boot-20260812.md`.** A refuter noted that the
August run recorded the OLD steps failing and that `CLAUDE.md` named the two
required secrets without giving a command, so a verbatim run of the four
documented commands still died inside `ApiKeyCredential` on the shipped
`"ApiKey": ""`. This run re-executes the steps as `CLAUDE.md` § Build & test
now reads, from a fresh `git clone`, in the cloud container (Ubuntu, .NET SDK
10.0.113, 4 vCPU) — not the owner's machine.

## Commands, verbatim, and what happened

```
git clone <repo> fresh && cd fresh && git checkout claude/release-path-20261022   # e5e2799
cd backend
dotnet build        → 0 Error(s), 22 s
dotnet test         → Passed! Failed: 0, Passed: 3197, Skipped: 0, Total: 3197, 33 s
dotnet run --project src/ArmenianAiToy.Api            (no secrets)
                    → Unhandled exception. System.ArgumentException: Value cannot be an empty string. (Parameter 'key')
export OpenAI__ApiKey="sk-dummy-for-local-boot"
export Jwt__Key="local-dev-jwt-key-0123456789abcdef0123456789"
dotnet run --project src/ArmenianAiToy.Api --urls http://127.0.0.1:5077
GET /api/health     → {"status":"ok","service":"ArmenianAiToy API","database":"ok","openai":"ok","audioStore":"ok"}
GET /parent.html    → 200
```

The two `export` lines are now in `CLAUDE.md` § Build & test (this PR); the
run above was made with exactly those values.

## The empty-key crash now names the setting

The `ArgumentException` above came from the OpenAI SDK and never said which
setting was empty. This PR adds a guard in
`backend/src/ArmenianAiToy.Infrastructure/DependencyInjection.cs` (null OR
whitespace). Booting the current working tree with no `OpenAI__ApiKey` now
prints:

```
Unhandled exception. System.InvalidOperationException: OpenAI:ApiKey is required — set OpenAI__ApiKey (any non-empty value boots the non-AI parts; see CLAUDE.md § Build & test)
```

`docs/deploy.md` § Required environment variables was wrong in the dangerous
direction ("Image will start without it") and is corrected in the same PR.

## Verdict

D1's acceptance condition as written — runs from a clean clone with the
documented steps, on a machine that is not the owner's — is met by this run,
and independently by Railway building the root `Dockerfile` from a clean
GitHub checkout (live, health `ok` today). Test count on this clone: 3197; the
working tree of the PR adds 17 (3214) — see `CLAUDE.md`.

Not done here: Windows (`$env:` syntax noted in CLAUDE.md, not run).

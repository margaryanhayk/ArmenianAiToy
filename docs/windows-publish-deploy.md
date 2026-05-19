# Windows non-Docker publish + smoke-test runbook

This document is a practical step-by-step runbook for deploying and
smoke-testing the Areg backend on a Windows host **without Docker**, by
running the standard `dotnet publish` output as a normal Windows .NET
app. It is the safe bypass used when Docker is not available on the
host.

It is a sibling of, not a replacement for, `docs/deploy.md`. The
Dockerfile, `.dockerignore`, and container deployment remain the
canonical production posture; this doc covers a validated local
non-Docker path.

To run the published exe automatically as a background service
after a Windows reboot (no PowerShell window open), continue with
`docs/windows-service-deploy.md` once you finish this runbook.
That sibling runbook has also been validated end-to-end with
NSSM, including cold-reboot auto-start, on the same reference
machine.

The procedure below was exercised end-to-end on Windows 11 against
repo `main` at commit `9ef6344` ("Merge branch
'feature/dockerfile-deploy-docs'"). Every command shown was actually
run; the troubleshooting section documents the deviations that
surfaced.

## Prerequisites

- Windows 10 / 11.
- PowerShell 5.1 or 7+.
- .NET SDK 10.0.x installed and on PATH. Validated against `10.0.204`.
  A working .NET SDK 8 (validated against `8.0.421`) may coexist on
  the same machine without interfering — the API targets .NET 10 and
  the SDK selection is governed by `global.json` / project TFM.
- `sqlite3` CLI on PATH (for the hash-at-rest verification step).
  Optional — only needed if you want to inspect the DB.
- `curl` on PATH (Windows 10+ ships one). Optional — `Invoke-RestMethod`
  works equivalently.
- A real OpenAI API key for the real-chat smoke. The current runtime
  requires a non-empty `OpenAI__ApiKey` **at startup** — the
  `OpenAIClient` is constructed during application bootstrap, so an
  empty / missing value will crash the exe before it begins
  listening. Set the real key via the PowerShell `Read-Host
  -AsSecureString` step in § 3, and never print it.

  For a health-check-only or device-registration-only smoke that does
  NOT call `/api/chat`, a locally-typed throwaway placeholder in the
  same env var is enough to satisfy startup. Do not paste keys —
  real or placeholder — into docs, logs, git, chat, or any file on
  disk.

```powershell
dotnet --list-sdks
sqlite3 -version
curl --version
```

## 1. Repo checks

From a fresh PowerShell session:

```powershell
cd C:\Users\hayk.margaryan\Documents\Projects\ArmenianAiToy
git status -sb
git log -1 --oneline
```

Confirm `main` is in sync with `origin/main` and that the latest
commit reflects the slice you intend to deploy. The validated run
used:

```
9ef6344 Merge branch 'feature/dockerfile-deploy-docs'
```

## 2. Restore, build, publish

From the `backend/` directory:

```powershell
cd C:\Users\hayk.margaryan\Documents\Projects\ArmenianAiToy\backend
dotnet restore
dotnet build
dotnet publish src\ArmenianAiToy.Api\ArmenianAiToy.Api.csproj -c Release -o C:\AregDeploy
```

`C:\AregDeploy` is the deployment directory. Pick any path you
control; the rest of this doc assumes `C:\AregDeploy`. The published
entry point will be:

```
C:\AregDeploy\ArmenianAiToy.Api.exe
```

## 3. Safe environment variable setup

The published app reads the same `Section__Key__Subkey`
double-underscore convention `docs/deploy.md` documents. Set them in
the PowerShell session that will launch the exe — **process-scoped
only**, no `setx`, nothing persisted to the user / machine
environment. Never echo the values back to the terminal.

Pick a runtime data root distinct from the publish output. The
validated run used `C:\AregDeployData`:

```powershell
mkdir C:\AregDeployData -Force | Out-Null
mkdir C:\AregDeployData\audio-blobs -Force | Out-Null
```

Then, in the same session:

```powershell
# --- secrets: paste in interactively, do NOT save to disk ---
$env:OpenAI__ApiKey = Read-Host -AsSecureString | ForEach-Object {
  [System.Net.NetworkCredential]::new('', $_).Password
}
# Generate a 32+ byte random JWT signing key locally. The value never
# leaves this process.
$env:Jwt__Keys__0 = [Convert]::ToBase64String(
  (1..48 | ForEach-Object { Get-Random -Maximum 256 }) -as [byte[]]
)

# --- non-secret runtime config ---
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:Database__ConnectionString = 'Data Source=C:\AregDeployData\areg-smoke.db'
$env:Audio__BlobStoreRoot = 'C:\AregDeployData\audio-blobs'

# Cost guardrails — keep these on for a smoke test.
$env:OpenAI__DailyCostCap__Enabled = 'true'
$env:OpenAI__DailyCostCap__Default = '0.50'
```

The session will lose these variables on close. That is intended —
a fresh smoke run should re-paste secrets.

## 4. Run the published exe

```powershell
cd C:\AregDeploy
.\ArmenianAiToy.Api.exe
```

Successful startup writes JSON log lines to stdout (see § Structured
console logging in `CLAUDE.md`). Among them, look for the
migration-applied line:

```
20260519120000_AddDeviceApiKeyHash
```

That confirms the device-API-key-hashing migration ran against the
fresh SQLite DB.

The app listens on `http://0.0.0.0:5000` by default in this
configuration — see § Troubleshooting below for the 5000-vs-5050
note.

## 5. Health check

In a second PowerShell window:

```powershell
curl http://localhost:5000/api/health
```

Expected:

```json
{"status":"ok","service":"ArmenianAiToy API","database":"ok"}
```

If `database` is anything other than `ok`, stop and re-check
`Database__ConnectionString` — the most common cause is a path the
process cannot create or write.

## 6. Device registration smoke

```powershell
$body = @{
  macAddress      = 'DD:DD:DD:DD:50:01'
  firmwareVersion = 'windows-publish-smoke-test'
} | ConvertTo-Json

$reg = Invoke-RestMethod `
  -Method Post `
  -Uri http://localhost:5000/api/devices/register `
  -ContentType 'application/json' `
  -Body $body

# Capture into variables for later steps. DO NOT echo $reg.apiKey
# to the terminal — treat it like a password.
$DeviceId = $reg.deviceId
$ApiKey   = $reg.apiKey
"Device registered: $DeviceId"
```

The response carries `deviceId` and `apiKey`. The plaintext `apiKey`
travels exactly once, in this HTTP response; the DB stores only its
PBKDF2-SHA256 hash. See `docs/device-api-key-hashing.md` for the full
hash format.

## 7. SQLite hash-at-rest verification

While the API is running, in a third window:

```powershell
sqlite3 C:\AregDeployData\areg-smoke.db `
  "SELECT Id, MacAddress, ApiKey IS NULL AS ApiKeyIsNull, substr(ApiKeyHash,1,18) AS ApiKeyHashPrefix FROM Devices;"
```

The query is deliberately safe: it asks for the first 18 characters
of `ApiKeyHash` (`v1:pbkdf2-sha256:`) and never selects the full
hash or the raw `ApiKey`. Expected shape:

```
<guid>|DD:DD:DD:DD:50:01|1|v1:pbkdf2-sha256:
```

- `ApiKeyIsNull = 1` confirms the raw key is no longer stored on
  freshly-registered rows.
- The `v1:pbkdf2-sha256:` prefix confirms the hash format the
  verifier accepts.

## 8. Optional real-chat smoke

This step makes a real call to OpenAI and costs money — the
`OpenAI__DailyCostCap__*` settings from step 3 bound it.

```powershell
# Force UTF-8 in this session so Armenian renders correctly.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding           = [System.Text.Encoding]::UTF8

$chatJson  = @{ message = 'Պատմիր կարճ հեքիաթ փոքրիկ ոզնու մասին' } |
             ConvertTo-Json
$chatBytes = [System.Text.Encoding]::UTF8.GetBytes($chatJson)

$resp = Invoke-RestMethod `
  -Method Post `
  -Uri http://localhost:5000/api/chat `
  -Headers @{
    'X-Device-Id' = $DeviceId
    'X-Api-Key'   = $ApiKey
  } `
  -ContentType 'application/json; charset=utf-8' `
  -Body $chatBytes

$resp | ConvertTo-Json -Depth 6
```

The validated run returned an Armenian story reply with:

- `mode = story`
- `storySessionId` populated
- `choiceA`, `choiceB` populated
- `safetyFlag = 0` (Clean)

Encoding pitfall: see § Troubleshooting.

## 9. Cleanup

```powershell
# In the API window: Ctrl+C to stop the exe.

# Optional — keep the smoke DB and audio blobs as evidence, or
# discard everything.
# Discard:
# Remove-Item -Recurse -Force C:\AregDeployData
# Remove-Item -Recurse -Force C:\AregDeploy
```

No git changes are produced by this runbook. Do not stage, commit,
or push anything as a side effect of running it.

## Troubleshooting

### App listens on 5000 instead of an expected 5050

The published exe binds `http://0.0.0.0:5000` by default when no
`ASPNETCORE_URLS` override is set. If a parallel doc or operator
note expects `5050`, the simplest fix is to point the client at
`5000`, or override on launch:

```powershell
$env:ASPNETCORE_URLS = 'http://0.0.0.0:5050'
```

This is a configuration-only difference; do not change application
code to chase a port. The Docker image uses `8080` inside the
container (`docs/deploy.md`); the published exe outside the
container uses `5000`.

### `OpenAI__ApiKey` missing — startup crash, or chat returns 502

A missing / empty `OpenAI__ApiKey` is currently a **startup-time**
failure, not a request-time one: `OpenAIClient` is constructed during
application bootstrap, so the exe exits before it ever begins
listening on `:5000`. The fix is to set `$env:OpenAI__ApiKey` **in
the same PowerShell session you will launch the exe from**, then
(re)start the exe. Setting it in a different window does not
propagate into the running process.

If you only need a health-check or device-registration smoke (no
`/api/chat`), a locally-typed throwaway placeholder is enough to
clear the startup check — but in that mode you MUST NOT call
`/api/chat`, since a placeholder key returns a real 502 `AI service
unavailable. Please try again.` from the upstream auth failure. Use
a real key (see § 3) for any chat smoke. Never paste keys into
docs, logs, git, or chat.

### PowerShell shows Armenian as `???`

PowerShell's default output / input encoding is not UTF-8, so
Armenian round-trips through the console as `?` placeholders. Fix
in the session:

```powershell
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding           = [System.Text.Encoding]::UTF8
```

For request bodies, also send raw UTF-8 bytes — `Invoke-RestMethod`
will otherwise transcode to the default ANSI codepage:

```powershell
$chatBytes = [System.Text.Encoding]::UTF8.GetBytes($chatJson)
Invoke-RestMethod ... -Body $chatBytes -ContentType 'application/json; charset=utf-8'
```

### "Database path confusion" — DB file lives somewhere unexpected

`Database__ConnectionString` is an absolute SQLite connection string,
not a directory. If it is unset, the app falls back to a relative
default and the `.db` lands beside the exe in `C:\AregDeploy\`. To
keep runtime state separate from the immutable publish output,
always set:

```powershell
$env:Database__ConnectionString = 'Data Source=C:\AregDeployData\areg-smoke.db'
```

Whatever directory the connection string points at must exist and be
writable by the user running the exe. The matching audio-blob root
(`Audio__BlobStoreRoot`) should sit on the same volume so deletion
cascades stay coherent.

## Relationship to Docker deployment

Docker remains the canonical production posture (`Dockerfile`,
`.dockerignore`, `docs/deploy.md`). This runbook does NOT remove or
replace any of that. It exists for environments where Docker is not
installed or not permitted — the publish output is the same artifact
the Dockerfile copies into the runtime image, just executed
directly on the Windows host.

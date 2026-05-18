# Windows Service deployment runbook

This document explains how to run the **already-published** Areg
backend automatically after a Windows reboot, without needing a
PowerShell window to stay open.

It builds directly on top of `docs/windows-publish-deploy.md` —
that runbook tells you how to produce `C:\AregDeploy\ArmenianAiToy.Api.exe`
from source; this runbook tells you how to keep that exe running.

It is a **sibling path** to Docker, not a Docker replacement.
`docs/deploy.md` and the `Dockerfile` remain the canonical
production posture wherever Docker is available. This Windows
Service path is for Windows hosts / VPSes where Docker is not an
option.

> **Honest engineering note up front.** The current backend
> exe does **not** implement the Windows Service Control Protocol
> (`Microsoft.Extensions.Hosting.WindowsServices` is not referenced
> by `ArmenianAiToy.Api.csproj`, and `Program.cs` does not call
> `builder.Host.UseWindowsService()`). That means a plain
> `New-Service` / `sc.exe create` install will register the
> service, but **`Start-Service` will fail** — the Windows Service
> Control Manager (SCM) waits for the process to report "started"
> within ~30 seconds, never receives that signal, and kills the
> process. Both working paths below (NSSM, Task Scheduler) work
> around this without changing backend code. § 4 covers the
> trade-offs and the future option of adding `UseWindowsService()`
> in a separate backend slice.

## 1. What this service setup does

- Runs `C:\AregDeploy\ArmenianAiToy.Api.exe` in the background as
  the local machine.
- Starts automatically after a Windows reboot.
- Uses a dedicated Windows service name: `AregBackend`.
- Uses a dedicated working directory: `C:\AregDeploy`.
- Keeps runtime state under a **separate** root:
  `C:\AregDeployData`. Publishing a fresh build into
  `C:\AregDeploy` therefore never touches the DB or audio blobs.
- Listens on `http://0.0.0.0:5000` by default. Health probe at
  `http://localhost:5000/api/health`.
- Stores SQLite DB at `C:\AregDeployData\areg.db`. This is the
  **service** DB. The smoke runbook (`docs/windows-publish-deploy.md`)
  used `areg-smoke.db` — different file, different lifecycle, so
  smoke artifacts and service state never collide.
- Stores audio blobs under `C:\AregDeployData\audio-blobs`.

## 2. Prerequisites

- Windows 10 / 11 or Windows Server 2019+.
- **Administrator** PowerShell session for install / uninstall /
  any registry or service-control work.
- .NET 10 ASP.NET Core runtime installed and discoverable by the
  published exe. Validated against SDK `10.0.204` for the
  publish step in `docs/windows-publish-deploy.md`; the published
  output is framework-dependent unless you publish self-contained.
- A working published exe at `C:\AregDeploy\ArmenianAiToy.Api.exe`
  — produced by following `docs/windows-publish-deploy.md` §§ 1–4.
- A runtime data root at `C:\AregDeployData` (will be created in
  § 9 if missing).
- A **real** `OpenAI__ApiKey` for any path that calls `/api/chat`.
  The current runtime requires a non-empty value **at startup** —
  the `OpenAIClient` is constructed during DI bootstrap (see
  `backend/src/ArmenianAiToy.Infrastructure/DependencyInjection.cs`),
  so an empty / missing value crashes the host before it begins
  listening.
- A **locally-generated** `Jwt__Keys__0` of at least 32 random
  bytes (Base64-encoded). The JWT key resolver
  (`Application/Auth/JwtKeys.cs`) rejects empty / whitespace-only
  values and the legacy insecure default.
- **Do not** use the source repo folder
  (`C:\Users\hayk.margaryan\Documents\Projects\ArmenianAiToy\backend\...`)
  as the service working directory. Always run the service from
  `C:\AregDeploy`, the publish output.
- **Do not** put real secrets in git, docs, scripts, or terminal
  output. The instructions below capture secrets via
  `Read-Host -AsSecureString` and never echo them.

## 3. Publish or refresh the app

Refer to `docs/windows-publish-deploy.md` § 2 for the full
walkthrough. The compact form, run from any PowerShell session
(Admin not required for publish):

```powershell
cd C:\Users\hayk.margaryan\Documents\Projects\ArmenianAiToy\backend
dotnet restore
dotnet build
dotnet publish src\ArmenianAiToy.Api\ArmenianAiToy.Api.csproj -c Release -o C:\AregDeploy
```

Important:

- If the `AregBackend` service is already installed and running,
  **stop it before publishing** so the running exe doesn't lock
  files in `C:\AregDeploy`. The upgrade flow in § 16 wraps this.
- **Never** publish over `C:\AregDeployData`. Keep deploy and data
  roots separate so republishing cannot delete DB / audio.
- After every publish, restart the service and re-check health
  (`/api/health` should return `database: ok` and the new
  migration line should appear in logs on first startup).

## 4. Hosting model — the honest gap

There are three credible ways to host the published exe as
"something that survives reboot" on Windows:

### Approach A — NSSM (recommended)

[NSSM](https://nssm.cc) ("Non-Sucking Service Manager") is the
standard Windows tool for wrapping an arbitrary `.exe` as a
genuine Windows Service. NSSM **implements the Service Control
Protocol itself** and runs your `.exe` as a child process. Three
things this buys us:

1. The backend exe does not need
   `Microsoft.Extensions.Hosting.WindowsServices` — NSSM handles
   SCM communication.
2. NSSM lets you set the service's `AppDirectory` (working
   directory), so ASP.NET Core's content-root resolution stays
   at `C:\AregDeploy` where `appsettings.json` lives.
3. NSSM captures stdout / stderr to log files on disk, fixing the
   "Windows Service stdout vanishes" problem on a stock install.

Cost: one extra static binary on the host (`nssm.exe`). No code
changes. This is the recommended primary path.

### Approach B — Task Scheduler (no third-party dependency)

Windows Task Scheduler can run an exe at user logon or at system
startup, with auto-restart on failure. It is not a "service" in
the SCM sense — `Get-Service AregBackend` will not find it — but
it covers the actual requirement ("starts after reboot, doesn't
need a PowerShell window open"). § 8 covers this path.

Cost: weaker than a real service for monitoring tooling. Logs
need an explicit `> log.txt` redirect in the task action.

### Approach C — Native `New-Service` (does NOT work today)

```powershell
# THIS DOES NOT WORK on the current backend — documented for honesty.
New-Service -Name AregBackend -BinaryPathName "C:\AregDeploy\ArmenianAiToy.Api.exe" -StartupType Automatic
Start-Service AregBackend   # <-- will fail with a timeout
```

`Start-Service` returns "the service did not respond to the start
or control request in a timely fashion" because the .exe does not
implement the SCM handshake. The service appears in
`Get-Service AregBackend` but its `Status` stays `StartPending`
then transitions to `Stopped`. Don't ship this path until the
backend is updated.

### Future option — add `UseWindowsService()` (separate backend slice)

The proper fix is one additional `using` + one method call in
`Program.cs`, plus the `Microsoft.Extensions.Hosting.WindowsServices`
NuGet package:

```csharp
// In Program.cs, immediately after WebApplication.CreateBuilder:
builder.Host.UseWindowsService();
```

That would enable Approach C, and a small `New-Service` + registry
`Environment` MultiString install (the original template plan for
this runbook). Out of scope for this docs-only slice — the runbook
flags it so a future engineer can pick it up. Any such change is
HIGH-risk (touches host bootstrap) and follows the autonomy
guardrails in `CLAUDE.md`.

The rest of this runbook uses Approach A (NSSM) as the primary
worked path. § 8 covers Approach B (Task Scheduler) for hosts
where adding NSSM is not acceptable.

## 5. Runtime configuration strategy

Windows services do not inherit the interactive PowerShell
session's environment variables. Three credible ways to give the
service durable configuration:

### Option A — NSSM `AppEnvironmentExtra` (recommended)

NSSM stores per-service environment variables in the registry
under its own subtree and injects them into the child process.
Secrets stay scoped to the service, not the machine. This is the
path § 7 takes.

### Option B — Native service registry `Environment` MultiString

For services installed natively via `New-Service` / `sc.exe`,
you can set a registry MultiString at:

```
HKLM:\SYSTEM\CurrentControlSet\Services\AregBackend
   Environment  (REG_MULTI_SZ)
```

This is the Windows-standard mechanism. **Only useful when
Approach C above actually works.** Documented here so a future
"add `UseWindowsService()`" slice can wire it in directly. It
has the same security posture as Option A — secrets stored on
the machine, readable by local administrators.

### Option C — `appsettings.Production.json` next to the exe

ASP.NET Core merges `appsettings.json` and (if
`ASPNETCORE_ENVIRONMENT=Production`) `appsettings.Production.json`
from the content root. **Do NOT** put real secrets in this file —
it lives next to the exe in `C:\AregDeploy`, so anyone with read
access to that directory can copy it. Use it only for non-secret
config: ports, paths, cost-cap defaults.

### Option D — Machine-level environment variables

Not preferred. They become global, leak to every process the
account runs, and are easy to confuse with placeholders.

### Recommendation for this runbook

Use **Option A (NSSM `AppEnvironmentExtra`)** for both secrets
and non-secrets, captured via `Read-Host -AsSecureString` and
local key generation. § 7 has the exact PowerShell.

### Required environment variables for the service

| Variable                              | Value used in this runbook                                       |
| ------------------------------------- | ---------------------------------------------------------------- |
| `ASPNETCORE_ENVIRONMENT`              | `Production`                                                     |
| `ASPNETCORE_URLS`                     | `http://0.0.0.0:5000`                                            |
| `Database__ConnectionString`          | `Data Source=C:\AregDeployData\areg.db`                          |
| `Audio__BlobStoreRoot`                | `C:\AregDeployData\audio-blobs`                                  |
| `OpenAI__ApiKey`                      | *(entered interactively, never printed)*                          |
| `Jwt__Keys__0`                        | *(generated locally with CSPRNG, never printed)*                  |
| `OpenAI__DailyCostCap__Enabled`       | `true`                                                           |
| `OpenAI__DailyCostCap__Default`       | `0.50`                                                           |

Notes on the JWT variable name:

- Preferred: `Jwt__Keys__0` (the ordered-keys rotation shape
  documented in `CLAUDE.md § JWT key rotation` and resolved by
  `backend/src/ArmenianAiToy.Application/Auth/JwtKeys.cs`).
- The legacy scalar `Jwt__Key` is still accepted as a single-
  element fallback when no `Jwt__Keys` entries exist. When both
  are present, `Jwt__Keys` wins and `Jwt__Key` is ignored. Use
  `Jwt__Keys__0` going forward.

## 6. Service user / account decision

Two reasonable choices for the first runbook:

### Option A — LocalSystem (default for both NSSM and Task Scheduler)

- Simplest path.
- Broad privileges on the local machine.
- Acceptable for local smoke and single-host hobby deployments.
- **Not** ideal for hardened production — a compromise of the
  Areg process becomes a compromise of the whole host.

### Option B — Dedicated local user, e.g. `AregSvc`

- Better isolation.
- Requires creating the user (out of scope for this script-free
  runbook — do **not** create a Windows user from a doc you've
  never read) and granting Modify rights on
  `C:\AregDeploy` (read+execute) and `C:\AregDeployData`
  (read+write).
- Recommended for any real long-running deployment exposed beyond
  the local machine.

This runbook proceeds with **LocalSystem**. The hardening note in
§ 17 covers the dedicated-user upgrade.

## 7. Install with NSSM (recommended)

### 7.1 Get NSSM

Download `nssm-2.24.zip` (or newer stable release) from
<https://nssm.cc/download>, verify the archive, and place
`nssm.exe` (from the matching architecture's `win64\` or
`win32\` folder) at a stable absolute path. This runbook assumes:

```
C:\Tools\nssm.exe
```

Do **not** copy NSSM into `C:\AregDeploy` — the deploy directory
is overwritten by every `dotnet publish`.

### 7.2 Install the service (Administrator PowerShell)

```powershell
# Admin PowerShell ----------------------------------------------------
$ServiceName  = 'AregBackend'
$DisplayName  = 'Areg Backend'
$Description  = 'Areg Armenian AI Toy backend API'
$ExePath      = 'C:\AregDeploy\ArmenianAiToy.Api.exe'
$WorkingDir   = 'C:\AregDeploy'
$LogDir       = 'C:\AregDeployData\logs'
$Nssm         = 'C:\Tools\nssm.exe'

New-Item -ItemType Directory -Force $LogDir | Out-Null

# Install (idempotent: refuses to re-install if the service exists).
& $Nssm install $ServiceName $ExePath
& $Nssm set $ServiceName AppDirectory       $WorkingDir
& $Nssm set $ServiceName DisplayName        $DisplayName
& $Nssm set $ServiceName Description        $Description
& $Nssm set $ServiceName Start              SERVICE_AUTO_START
& $Nssm set $ServiceName AppStdout          (Join-Path $LogDir 'areg-stdout.log')
& $Nssm set $ServiceName AppStderr          (Join-Path $LogDir 'areg-stderr.log')
& $Nssm set $ServiceName AppRotateFiles     1
& $Nssm set $ServiceName AppRotateOnline    1
& $Nssm set $ServiceName AppRotateBytes     10485760   # 10 MiB per log file
& $Nssm set $ServiceName AppExit            Default Restart
& $Nssm set $ServiceName AppRestartDelay    5000       # 5 s between restarts
```

NSSM defaults to LocalSystem; to use a dedicated user, add:

```powershell
& $Nssm set $ServiceName ObjectName ".\AregSvc" "<password>"
```

(For a hardened deployment only — and do not paste the password
into a doc / chat / log.)

### 7.3 Set service-specific environment variables

The block below captures `OpenAI__ApiKey` interactively, generates
a 48-byte CSPRNG `Jwt__Keys__0`, assembles a `\0`-terminated
`AppEnvironmentExtra` payload (NSSM's wire shape), writes it via
`nssm set`, and then clears every local variable that held a
secret.

```powershell
# Admin PowerShell, same session ---------------------------------------

# 1. Capture the OpenAI key interactively. Never printed.
$openAiSecure = Read-Host 'Enter OpenAI API key' -AsSecureString
$openAiKey    = [System.Net.NetworkCredential]::new('', $openAiSecure).Password

# 2. Generate a 48-byte CSPRNG JWT signing key locally. Never printed.
$jwtBytes = New-Object byte[] 48
$rng      = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($jwtBytes)
$jwtKey   = [Convert]::ToBase64String($jwtBytes)
$rng.Dispose()

# 3. Build the env-var set. Non-secrets first for readability.
$envPairs = @(
  'ASPNETCORE_ENVIRONMENT=Production'
  'ASPNETCORE_URLS=http://0.0.0.0:5000'
  'Database__ConnectionString=Data Source=C:\AregDeployData\areg.db'
  'Audio__BlobStoreRoot=C:\AregDeployData\audio-blobs'
  'OpenAI__DailyCostCap__Enabled=true'
  'OpenAI__DailyCostCap__Default=0.50'
  "OpenAI__ApiKey=$openAiKey"
  "Jwt__Keys__0=$jwtKey"
)

# 4. NSSM expects AppEnvironmentExtra as a single argument with each
#    NAME=VALUE separated by a literal NUL byte. PowerShell can't pass a
#    NUL via a regular string, so use the documented multi-arg form
#    instead — `nssm set` accepts each pair as its own argument.
& $Nssm set $ServiceName AppEnvironmentExtra @envPairs

# 5. Clear every local variable that held a secret. Do NOT echo $envPairs.
$openAiKey    = $null
$jwtKey       = $null
$jwtBytes     = $null
$openAiSecure = $null
$envPairs     = $null
[GC]::Collect()
```

Important:

- **Do not** run `& $Nssm get AregBackend AppEnvironmentExtra` or
  `Get-ItemProperty HKLM:\SYSTEM\CurrentControlSet\Services\AregBackend\Parameters`
  in a way that prints the result. NSSM stores the env vars in
  the service's registry subtree, where any local admin can read
  them — that's the machine-level secret-storage posture you've
  opted into; printing the values to the terminal makes them part
  of every shell-history and screen-recording that captures the
  window.
- For stronger secret storage later, integrate with Windows
  Credential Manager / DPAPI / Azure Key Vault and read at app
  startup. Out of scope here.

## 8. Install with Task Scheduler (no third-party dependency)

Use this path only if NSSM is not acceptable on the host. The
service-management semantics are weaker — `Get-Service AregBackend`
will report nothing, you manage the task via `Get-ScheduledTask`
/ `Start-ScheduledTask`. Auto-restart works via the task's
`Restart on failure` trigger configuration.

```powershell
# Admin PowerShell ----------------------------------------------------
$TaskName  = 'AregBackend'
$ExePath   = 'C:\AregDeploy\ArmenianAiToy.Api.exe'
$WorkDir   = 'C:\AregDeploy'

$action    = New-ScheduledTaskAction `
              -Execute $ExePath `
              -WorkingDirectory $WorkDir

$trigger   = New-ScheduledTaskTrigger -AtStartup

$principal = New-ScheduledTaskPrincipal `
              -UserId 'SYSTEM' `
              -LogonType ServiceAccount `
              -RunLevel Highest

$settings  = New-ScheduledTaskSettingsSet `
              -AllowStartIfOnBatteries `
              -DontStopIfGoingOnBatteries `
              -StartWhenAvailable `
              -RestartCount 3 `
              -RestartInterval (New-TimeSpan -Minutes 1) `
              -ExecutionTimeLimit (New-TimeSpan -Seconds 0)   # unlimited

Register-ScheduledTask `
  -TaskName $TaskName `
  -Action $action `
  -Trigger $trigger `
  -Principal $principal `
  -Settings $settings `
  -Description 'Areg Armenian AI Toy backend API'
```

Set environment variables on the task — Task Scheduler has no
native env-var support, so you either:

1. Use a tiny launcher `.cmd` that `set`s vars and calls the exe
   (do **not** put secrets in that .cmd if it lands on disk
   unencrypted), or
2. Set machine-level vars via `[Environment]::SetEnvironmentVariable(...)`
   with target `Machine` (broader leak surface, see § 5 Option D).

Both are weaker than NSSM's per-service `AppEnvironmentExtra`.
This is the central reason the NSSM path is preferred.

To start the task immediately:

```powershell
Start-ScheduledTask -TaskName $TaskName
```

## 9. Folders and permissions

Run once, Admin PowerShell:

```powershell
New-Item -ItemType Directory -Force 'C:\AregDeployData'             | Out-Null
New-Item -ItemType Directory -Force 'C:\AregDeployData\audio-blobs' | Out-Null
New-Item -ItemType Directory -Force 'C:\AregDeployData\logs'        | Out-Null
```

For **LocalSystem**, default ACLs are sufficient — LocalSystem
has Modify on every directory under `C:\`.

For a **dedicated `AregSvc` user**, grant Modify on the data
root only (the deploy root needs only Read+Execute since the
service never writes there):

```powershell
icacls C:\AregDeploy     /grant 'AregSvc:(OI)(CI)RX'
icacls C:\AregDeployData /grant 'AregSvc:(OI)(CI)M'
```

## 10. Start the service and run the health check

```powershell
Start-Service AregBackend
Get-Service AregBackend
```

Expected: `Status: Running`.

Then health-check from any normal PowerShell window:

```powershell
curl.exe http://localhost:5000/api/health
```

Expected response:

```json
{"status":"ok","service":"ArmenianAiToy API","database":"ok"}
```

On first start, the NSSM stdout log
(`C:\AregDeployData\logs\areg-stdout.log`) should contain the
migration line:

```
20260519120000_AddDeviceApiKeyHash
```

Confirming EF Core applied migrations against the fresh service
DB `C:\AregDeployData\areg.db`.

## 11. Logs and troubleshooting

### Where to look

- **NSSM-captured stdout / stderr.** Tail the rotating logs
  under `C:\AregDeployData\logs\`. The app emits structured JSON
  to stdout (see `CLAUDE.md § Structured console logging`); NSSM
  preserves it byte-for-byte. With the native `New-Service`
  path (or Task Scheduler without a launcher), stdout is
  discarded by default — this is a major reason NSSM is the
  primary recommendation.
- **Windows Event Viewer.** `Windows Logs → Application`
  (.NET runtime crashes) and `Windows Logs → System` (SCM
  start failures, NSSM lifecycle events).
- **Service status.** `Get-Service AregBackend` or
  `sc.exe query AregBackend`.
- **Process state.** `Get-Process ArmenianAiToy.Api -ErrorAction SilentlyContinue`.
- **Port binding.** `netstat -ano | findstr :5000`. The PID in
  the last column should match the `ArmenianAiToy.Api` process.

### Common cases

**A. Service starts, then immediately stops.** Most likely:

1. `OpenAI__ApiKey` missing or empty. `OpenAIClient` is
   constructed during DI bootstrap; the host throws
   `InvalidOperationException` before it begins listening. Fix
   by re-running the env-var block in § 7.3.
2. `Jwt__Keys__0` empty / whitespace / equal to the legacy
   insecure default. `Application/Auth/JwtKeys.cs` fails-fast
   at startup. Generate a fresh value as in § 7.3.
3. `Database__ConnectionString` points at a path the service
   user can't create or write (typical for a dedicated user
   that doesn't yet have Modify on `C:\AregDeployData`).
4. Port `5000` already in use by another process —
   `netstat -ano | findstr :5000` will show the offender.
5. Env var changes were made but the service wasn't restarted.
   NSSM only reads `AppEnvironmentExtra` at start; **always**
   `Restart-Service AregBackend` after any change.

**B. `/api/health` cannot connect.**

1. `Get-Service AregBackend` not `Running`.
2. The service is listening on a different URL — confirm with
   `netstat -ano | findstr :5000`.
3. Windows Firewall is blocking 5000. For local-only access this
   doesn't matter; for LAN access, add an inbound rule.

**C. `/api/health` returns `database: ok` is false / missing.**

1. Open `C:\AregDeployData` — does `areg.db` exist? Is it
   writable? `Get-Acl C:\AregDeployData\areg.db`.
2. Check the NSSM stderr log for an EF Core migration error.
3. If the DB was created against an older app via
   `EnsureCreated()`, follow the baseline-adoption procedure
   in `CLAUDE.md § Database migrations` before starting the
   new service.

**D. Armenian text shows as `???` in PowerShell.** This is a
client-side console encoding issue, not a service issue. In the
PowerShell window where you're testing:

```powershell
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding           = [System.Text.Encoding]::UTF8
```

And send `/api/chat` bodies as UTF-8 bytes (§ 14).

**E. `OpenAI__ApiKey` missing — startup crash, or chat returns 502.**
A missing / empty value is a **startup-time** failure (the host
exits before listening). A placeholder value lets the host start
but every `/api/chat` call returns 502 `AI service unavailable.
Please try again.` from the upstream auth failure — so if you
configured the service with a placeholder, do **not** test
`/api/chat`. Re-run the env-var block in § 7.3 with the real key
and `Restart-Service AregBackend`.

**F. Port 5000 vs 5050.** The published exe binds `:5000` by
default when no `ASPNETCORE_URLS` is set. The runbook explicitly
sets `ASPNETCORE_URLS=http://0.0.0.0:5000` for the service so the
value never depends on whatever happens to be in the operator's
PowerShell session. If you need `:5050`, change that env var,
update your health checks, and `Restart-Service AregBackend`.

**G. Need to update service env vars.** Always:

```powershell
Stop-Service  AregBackend
# (re-run the § 7.3 block with the values you want to change)
Start-Service AregBackend
```

Do not edit `AppEnvironmentExtra` via `regedit` directly — NSSM
expects a specific encoding and a hand-edit can wedge it.

**H. Content root resolves to `C:\Windows\System32`.** Only
possible if `AppDirectory` was not set. The § 7.2 block sets it
explicitly to `C:\AregDeploy`; if you skipped that line, run:

```powershell
& $Nssm set AregBackend AppDirectory 'C:\AregDeploy'
Restart-Service AregBackend
```

## 12. Device registration smoke under the running service

Run from any normal PowerShell window (no Admin required):

```powershell
$body = @{
  macAddress      = 'DD:DD:DD:DD:50:02'
  firmwareVersion = 'windows-service-smoke-test'
} | ConvertTo-Json

$reg = Invoke-RestMethod `
  -Method Post `
  -Uri http://localhost:5000/api/devices/register `
  -ContentType 'application/json' `
  -Body $body

# Capture into local vars. DO NOT echo $reg.apiKey to the terminal —
# treat it like a password.
$DeviceId = $reg.deviceId
$ApiKey   = $reg.apiKey
"Device registered: $DeviceId"
```

The plaintext `apiKey` travels exactly once, in this HTTP
response; the DB stores only a PBKDF2-SHA256 hash. See
`docs/device-api-key-hashing.md` for the full hash format.

## 13. SQLite hash-at-rest verification

While the service is running:

```powershell
sqlite3 C:\AregDeployData\areg.db `
  "SELECT Id, MacAddress, ApiKey IS NULL AS ApiKeyIsNull, substr(ApiKeyHash,1,18) AS ApiKeyHashPrefix FROM Devices;"
```

The query is deliberately safe — it asks for the first 18
characters of `ApiKeyHash` (`v1:pbkdf2-sha256:`) and never
selects the full hash or the raw `ApiKey`. Expected shape:

```
<guid>|DD:DD:DD:DD:50:02|1|v1:pbkdf2-sha256:
```

- `ApiKeyIsNull = 1` confirms the raw key is no longer stored
  on freshly-registered rows.
- The `v1:pbkdf2-sha256:` prefix confirms the hash format the
  verifier accepts.

## 14. Optional real-chat smoke

This step costs money — the `OpenAI__DailyCostCap__*` settings
from § 7.3 bound it. Run from the same PowerShell session as
§ 12 so `$DeviceId` and `$ApiKey` are in scope.

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

Expected fields on the response:

- `mode = story`
- `storySessionId` populated
- `choiceA`, `choiceB` populated
- `safetyFlag = 0` (Clean)

## 15. Stop, restart, uninstall

```powershell
# Day-to-day --------------------------------------------------------
Stop-Service    AregBackend
Start-Service   AregBackend
Restart-Service AregBackend

# Disable auto-start without removing the service -------------------
Set-Service AregBackend -StartupType Manual

# Permanently remove the service (Admin PowerShell) -----------------
Stop-Service AregBackend
& 'C:\Tools\nssm.exe' remove AregBackend confirm
# (For Task Scheduler path: Unregister-ScheduledTask -TaskName AregBackend -Confirm:$false)
```

Important:

- `nssm remove` (and `sc.exe delete`) only remove the service
  registration. They do **not** delete `C:\AregDeploy` or
  `C:\AregDeployData`.
- Do **not** delete `C:\AregDeployData` unless you intend to
  destroy DB and audio evidence. If you do, do it as a separate,
  deliberate command — not as a side effect of uninstall.

## 16. Upgrade flow

The "publish a new build into a running service" sequence:

```powershell
# Admin PowerShell ----------------------------------------------------

# 1. Stop the running service so its file handles release.
Stop-Service AregBackend

# 2. (Optional) Back up the DB before the upgrade. SQLite is a single
#    file; copy while the service is stopped.
Copy-Item 'C:\AregDeployData\areg.db' "C:\AregDeployData\areg.db.bak-$(Get-Date -Format yyyyMMddHHmmss)"

# 3. Re-publish into the same deploy root. C:\AregDeployData is
#    untouched.
cd C:\Users\hayk.margaryan\Documents\Projects\ArmenianAiToy\backend
dotnet publish src\ArmenianAiToy.Api\ArmenianAiToy.Api.csproj -c Release -o C:\AregDeploy

# 4. Start the service back up. New migrations apply on startup.
Start-Service AregBackend

# 5. Health probe.
curl.exe http://localhost:5000/api/health
```

If health fails, tail
`C:\AregDeployData\logs\areg-stderr.log` for the actual reason —
the most common failure during an upgrade is an EF Core
migration error on a DB that pre-dates the `Migrate()` cut-over
(see `CLAUDE.md § Database migrations`).

## 17. Security notes

- `OpenAI__ApiKey` and `Jwt__Keys__0` are secrets. Never commit
  them. Never paste them into chat. Never screenshot them.
  Capture them via `Read-Host -AsSecureString` and generate JWT
  keys via `RandomNumberGenerator` as shown in § 7.3.
- Service-scoped registry env vars (NSSM `AppEnvironmentExtra`)
  are better than machine-wide env vars or a plaintext config
  file, but they're still readable by local administrators of
  the host. Treat the host itself as a secret-bearing system:
  restrict admin access, keep it patched, full-disk-encrypt it.
- For stronger storage, integrate Windows Credential Manager /
  DPAPI / Azure Key Vault and load on startup. Out of scope here.
- Keep `OpenAI__DailyCostCap__Enabled=true` on production.
  `0.50` USD/device/day is a sensible default for a single-toy
  deployment — see `docs/openai-daily-cost-cap.md` for tuning.
- Protect `C:\AregDeployData`. The SQLite DB carries parent /
  child / conversation data. Periodic snapshots of `areg.db` to
  an encrypted location are a reasonable baseline backup.
- **Do not expose `:5000` directly to the public internet.**
  Kestrel here is plaintext HTTP and the dashboard / device
  endpoints do not negotiate TLS. Put a reverse proxy (IIS with
  HTTPS bindings, Caddy, nginx, Cloudflare Tunnel, etc.) in
  front and forward to `http://localhost:5000`. Restrict the
  inbound firewall to the proxy's source.
- The audit and structured-log records keep the durable history
  of what happened. Don't disable them to clean up logs.

## 18. Final checklist

- [ ] Published exe at `C:\AregDeploy\ArmenianAiToy.Api.exe`.
- [ ] `C:\AregDeployData`, `C:\AregDeployData\audio-blobs`, and
      `C:\AregDeployData\logs` exist.
- [ ] NSSM installed at `C:\Tools\nssm.exe` (or Task Scheduler
      path chosen explicitly).
- [ ] Service `AregBackend` installed with `AppDirectory =
      C:\AregDeploy` and `Start = SERVICE_AUTO_START`.
- [ ] Service env vars set via `AppEnvironmentExtra` (real
      `OpenAI__ApiKey`, generated `Jwt__Keys__0`, all paths).
- [ ] `Start-Service AregBackend` succeeded; `Get-Service`
      reports `Running`.
- [ ] `netstat -ano | findstr :5000` shows a listener on the
      Areg process id.
- [ ] `curl http://localhost:5000/api/health` returns
      `{"status":"ok","service":"ArmenianAiToy API","database":"ok"}`.
- [ ] First-startup log contains the
      `20260519120000_AddDeviceApiKeyHash` migration line.
- [ ] Device registration smoke (§ 12) succeeded.
- [ ] Hash-at-rest check (§ 13) confirms `ApiKeyIsNull = 1` and
      a `v1:pbkdf2-sha256:` prefix.
- [ ] Optional real-chat smoke (§ 14) returned a Story-mode
      reply with both choices and `safetyFlag = 0`.
- [ ] Service survives a `Restart-Computer` (manual reboot test).
- [ ] No secret values appear in git, docs, scripts, terminal
      history, or screen recordings.

## 19. Cross-references

- `docs/windows-publish-deploy.md` — the upstream "produce the
  published exe" runbook this builds on.
- `docs/deploy.md` — the canonical Docker deployment posture.
- `docs/device-api-key-hashing.md` — full hash-at-rest contract.
- `docs/openai-daily-cost-cap.md` — cost-cap configuration.
- `CLAUDE.md § Database migrations` — migration / baseline-
  adoption rules; relevant on any DB upgrade.
- `CLAUDE.md § JWT key rotation` — the `Jwt:Keys[]` shape vs
  legacy scalar `Jwt:Key`, and the legacy-insecure-default
  guard.
- `CLAUDE.md § Structured console logging` — what the captured
  stdout JSON looks like.

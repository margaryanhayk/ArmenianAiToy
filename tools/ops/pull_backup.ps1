<#
.SYNOPSIS
  Off-site database backup pull (C166, 2026-10-03). Windows twin of
  tools/ops/pull_backup.sh -- same flow, same file names, same exit codes.

.DESCRIPTION
  The automatic DatabaseBackupService snapshots live on the SAME Railway
  volume as the database they protect, so losing the volume loses both. This
  script is the off-platform half: Task Scheduler runs it daily on a machine
  you control and it keeps the newest N verified snapshots there.

  In order -- any failure stops it with a non-zero exit and touches nothing
  already on disk:
    1. Reads the operator token from a file only you can read (refuses a file
       Everyone / Users / Authenticated Users can read). The token is never
       printed or logged.
    2. POST /api/internal/session with that token -> a short-lived session
       token. Works whether or not Internal:RequireSession is on. The operator
       must have NO TotpSecret: a scheduled task cannot type a code.
    3. GET /api/internal/backup with the session token -> a fresh SQLite
       snapshot (one audit row per pull, server side).
    4. PRAGMA integrity_check via Python's sqlite3 module, plus a schema
       sanity check (__EFMigrationsHistory must exist). Only then is the
       .part file renamed to areg-backup-<UTC>Z.db.
    5. Deletes all but the newest -Keep snapshots (default 30) -- only after a
       verified pull, so a failing run never thins out good backups.
    6. Pings the healthchecks.io URL, if given -- only on success.

  Covers the DATABASE only (docs/ops-runbook.md, "Off-site backup pull").

  Every parameter also reads from the environment (parameter wins):
  AREG_BACKUP_BASE_URL, AREG_BACKUP_TOKEN_FILE, AREG_BACKUP_DIR,
  AREG_BACKUP_KEEP, AREG_BACKUP_PING_URL, AREG_BACKUP_PYTHON.

  Exit codes: 0 ok (a failed ping only warns) / 1 usage or config / 2 session
  or download refused / 3 the snapshot failed its integrity check.

  Requires: Windows PowerShell 5.1 or PowerShell 7+, and Python 3 (the `py`
  launcher, `python` or `python3`; stdlib only). This file is ASCII on
  purpose: 5.1 reads a BOM-less script as ANSI, where the UTF-8 bytes of a
  typographic dash include a smart quote that ends a string.

.EXAMPLE
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\areg\tools\ops\pull_backup.ps1 `
    -BaseUrl https://<host> -TokenFile C:\Users\me\areg\backup-bot.token `
    -OutDir D:\areg-backups -PingUrl https://hc-ping.com/<uuid> -LogFile D:\areg-backups\pull.log
#>
[CmdletBinding()]
param(
    [string]$BaseUrl   = $env:AREG_BACKUP_BASE_URL,
    [string]$TokenFile = $env:AREG_BACKUP_TOKEN_FILE,
    [string]$OutDir    = $env:AREG_BACKUP_DIR,
    [int]$Keep         = $(if ($env:AREG_BACKUP_KEEP) { [int]$env:AREG_BACKUP_KEEP } else { 30 }),
    [string]$PingUrl   = $env:AREG_BACKUP_PING_URL,
    [string]$Python    = $env:AREG_BACKUP_PYTHON,
    [string]$LogFile   = ''
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # the 5.1 progress bar slows downloads ~10x
if ($PSVersionTable.PSVersion.Major -lt 6) {
    # Windows PowerShell 5.1 may not offer TLS 1.2 by default.
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
}
$onWindows = ($env:OS -eq 'Windows_NT')

function Write-Log([string]$Message) {
    $line = '[{0}] {1}' -f (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'), $Message
    Write-Output $line
    if ($LogFile) { Add-Content -LiteralPath $LogFile -Value $line -Encoding UTF8 }
}
function Stop-Run([int]$Code, [string]$Message) {
    Write-Log "FAIL - $Message"
    exit $Code
}
function Get-HttpStatus($ErrorRecord) {
    # 5.1: WebException.Response is HttpWebResponse; 7+: HttpResponseMessage.
    $resp = $ErrorRecord.Exception.Response
    if ($null -eq $resp) { return 0 }
    return [int]$resp.StatusCode
}
function Invoke-Native([string]$Exe, [string[]]$Arguments, [string]$InputText = '') {
    # Function-local: on Windows PowerShell 5.1 a native exe writing to stderr
    # under ErrorActionPreference=Stop becomes a terminating error.
    $ErrorActionPreference = 'Continue'
    if ($InputText) { $out = $InputText | & $Exe @Arguments 2>&1 }
    else { $out = & $Exe @Arguments 2>&1 }
    return @{ Code = $LASTEXITCODE; Text = ((@($out) | ForEach-Object { "$_" }) -join "`n").Trim() }
}
function Resolve-Python {
    $candidates = @()
    if ($Python) { $candidates += ,@{ Exe = $Python; Pre = @() } }
    $candidates += ,@{ Exe = 'py'; Pre = @('-3') }
    $candidates += ,@{ Exe = 'python'; Pre = @() }
    $candidates += ,@{ Exe = 'python3'; Pre = @() }
    foreach ($c in $candidates) {
        if (-not (Get-Command $c.Exe -ErrorAction SilentlyContinue)) { continue }
        # The Microsoft Store "python" stub exists but cannot run code -- probe it.
        if ((Invoke-Native $c.Exe (@($c.Pre) + @('-c', 'import sqlite3'))).Code -eq 0) { return $c }
    }
    return $null
}

if (-not $BaseUrl)   { Stop-Run 1 '-BaseUrl (or AREG_BACKUP_BASE_URL) is required' }
if (-not $TokenFile) { Stop-Run 1 '-TokenFile (or AREG_BACKUP_TOKEN_FILE) is required' }
if (-not $OutDir)    { Stop-Run 1 '-OutDir (or AREG_BACKUP_DIR) is required' }
if ($Keep -lt 1)     { Stop-Run 1 '-Keep must be a whole number >= 1' }
$BaseUrl = $BaseUrl.TrimEnd('/')
$py = Resolve-Python
if ($null -eq $py) { Stop-Run 1 'Python 3 was not found (install it, or pass -Python C:\path\to\python.exe)' }

# --- 1. token: a user-only file, read once, never echoed -------------------
if (-not (Test-Path -LiteralPath $TokenFile -PathType Leaf)) { Stop-Run 1 "token file not found: $TokenFile" }
if ($onWindows) {
    $broad = @('S-1-1-0', 'S-1-5-11', 'S-1-5-32-545')   # Everyone, Authenticated Users, BUILTIN\Users
    foreach ($rule in (Get-Acl -LiteralPath $TokenFile).Access) {
        if ($rule.AccessControlType -ne 'Allow') { continue }
        try { $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value }
        catch { $sid = $rule.IdentityReference.Value }
        if ($broad -contains $sid) {
            Stop-Run 1 ("token file $TokenFile is readable by '$($rule.IdentityReference)' -- lock it down: " +
                "icacls `"$TokenFile`" /inheritance:r /grant:r `"$($env:USERNAME):(R)`"")
        }
    }
} else {
    $modeArgs = @($py.Pre) + @('-c', 'import os,sys; sys.exit(1 if os.stat(sys.argv[1]).st_mode & 0o077 else 0)', $TokenFile)
    if ((Invoke-Native $py.Exe $modeArgs).Code -ne 0) {
        Stop-Run 1 "token file $TokenFile is readable by other users -- run: chmod 600 '$TokenFile'"
    }
}
$token = Get-Content -LiteralPath $TokenFile -Raw
if ($null -ne $token) { $token = $token.Trim() }
if (-not $token) { Stop-Run 1 "token file $TokenFile is empty" }
if ($token -notmatch '^[\x21-\x7E]+$') { Stop-Run 1 "token file $TokenFile holds unexpected characters (expected one printable token)" }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
$final = Join-Path $OutDir "areg-backup-${stamp}Z.db"
$part = "$final.part"
if (Test-Path -LiteralPath $final) { Stop-Run 1 "$final already exists -- two runs in the same second?" }

try {
    # --- 2. session exchange ---------------------------------------------
    Write-Log "opening a console session at $BaseUrl"
    try {
        $session = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/internal/session" `
            -Headers @{ Authorization = "Bearer $token" } -ContentType 'application/json' -Body '{}' `
            -UseBasicParsing -TimeoutSec 60
    } catch {
        $err = $_
        switch (Get-HttpStatus $err) {
            404 { Stop-Run 2 'session refused (HTTP 404): wrong token, the operator was removed, or the console is off' }
            401 { Stop-Run 2 'session refused (HTTP 401): this operator has a TotpSecret -- the backup bot must be a separate operator without one' }
            0   { Stop-Run 2 "could not reach $BaseUrl ($($err.Exception.GetType().Name))" }
            default { Stop-Run 2 "session refused (HTTP $(Get-HttpStatus $err))" }
        }
    }
    $token = $null
    $sessionToken = $session.sessionToken
    if (-not $sessionToken) { Stop-Run 2 'the session response carried no sessionToken' }

    # --- 3. download -----------------------------------------------------
    Write-Log 'pulling the snapshot'
    try {
        Invoke-WebRequest -Uri "$BaseUrl/api/internal/backup" -Headers @{ Authorization = "Bearer $sessionToken" } `
            -OutFile $part -UseBasicParsing -TimeoutSec 600 | Out-Null
    } catch {
        $code = Get-HttpStatus $_
        if ($code -eq 0) { Stop-Run 2 "the download did not complete ($($_.Exception.GetType().Name))" }
        Stop-Run 2 "backup refused (HTTP $code)"
    }
    $sessionToken = $null

    # --- 4. integrity (Python reads this program from stdin; ASCII only) --
    $check = @'
import os, sqlite3, sys
path = sys.argv[1]
if os.path.getsize(path) == 0:
    print("empty file"); sys.exit(1)
try:
    con = sqlite3.connect(path)
    rows = [r[0] for r in con.execute("PRAGMA integrity_check")]
    has_history = con.execute(
        "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'"
    ).fetchone()[0] == 1
    tables = con.execute("SELECT count(*) FROM sqlite_master WHERE type='table'").fetchone()[0]
    con.close()
except sqlite3.DatabaseError as e:
    print("not a readable SQLite database (%s)" % e); sys.exit(1)
if rows != ["ok"]:
    print("integrity_check reported %d problem(s), first: %s" % (len(rows), rows[0])); sys.exit(1)
if not has_history:
    print("integrity ok but __EFMigrationsHistory is missing (not an Areg database)"); sys.exit(1)
print("integrity_check=ok, %d tables" % tables)
'@
    $result = Invoke-Native $py.Exe (@($py.Pre) + @('-', $part)) $check
    $verdict = $result.Text
    if ($result.Code -ne 0) { Stop-Run 3 "snapshot rejected: $verdict" }

    Move-Item -LiteralPath $part -Destination $final -Force
    Write-Log ('saved {0} ({1} bytes; {2})' -f $final, (Get-Item -LiteralPath $final).Length, $verdict)

    # --- 5. keep the newest N --------------------------------------------
    $old = @(Get-ChildItem -LiteralPath $OutDir -Filter 'areg-backup-*Z.db' -File |
        Sort-Object Name -Descending | Select-Object -Skip $Keep)
    $old | Remove-Item -Force
    Write-Log "kept the newest $Keep, removed $($old.Count) older"

    # --- 6. heartbeat to the external monitor, success only -------------
    if ($PingUrl) {
        $pinged = $false
        foreach ($attempt in 1..3) {
            try {
                Invoke-WebRequest -Uri $PingUrl -UseBasicParsing -TimeoutSec 10 | Out-Null
                $pinged = $true; break
            } catch { Start-Sleep -Seconds 2 }
        }
        if ($pinged) { Write-Log 'pinged the monitor' }
        else { Write-Log 'WARN - backup is fine but the monitor ping failed; it will alert if pings stay missing' }
    }
} finally {
    if (Test-Path -LiteralPath $part) { Remove-Item -LiteralPath $part -Force }
}
exit 0

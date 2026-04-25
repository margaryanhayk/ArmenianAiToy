# test-chat-audio.ps1
#
# PC-side verification of POST /api/chat/audio for the Armenian AI Toy
# backend. Sends a local WAV file as the raw request body with the
# device auth headers, saves the returned Armenian MP3, and prints a
# short status report. No new dependencies; works on Windows
# PowerShell 5.1 and PowerShell 7+.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools\test-chat-audio.ps1 `
#     -BaseUrl "http://<backend-host>:<port>" `
#     -DeviceId "<device-guid>" `
#     -ApiKey "dtk_<api-key>" `
#     -InputWav ".\samples\hello.wav" `
#     -OutputAudio ".\artifacts\areg-response.mp3"
#
# Parameters:
#   -BaseUrl      Backend base URL, e.g. http://<host>:<port> (no trailing /api).
#   -DeviceId     Device GUID from POST /api/devices/register.
#   -ApiKey       Device API key (dtk_...) from POST /api/devices/register.
#   -InputWav     Local WAV file to send. 16 kHz mono PCM matches C1 firmware.
#   -OutputAudio  Where to save the assistant MP3 response.
#   -ContentType  Optional. Defaults to audio/wav.
#   -TimeoutSec   Optional. Defaults to 60 s (STT + chat + TTS end-to-end).
#
# Never commit real DeviceId / ApiKey / Wi-Fi URLs via this script's
# defaults. All sensitive values come in on the command line.

param(
    [string] $BaseUrl,
    [string] $DeviceId,
    [string] $ApiKey,
    [string] $InputWav,
    [string] $OutputAudio,
    [string] $ContentType = 'audio/wav',
    [int]    $TimeoutSec  = 60
)

$ErrorActionPreference = 'Stop'

function Fail([int]$code, [string]$msg) {
    Write-Host $msg -ForegroundColor Red
    exit $code
}

if (-not $BaseUrl)     { Fail 2 "ERROR: -BaseUrl is required." }
if (-not $DeviceId)    { Fail 2 "ERROR: -DeviceId is required." }
if (-not $ApiKey)      { Fail 2 "ERROR: -ApiKey is required." }
if (-not $InputWav)    { Fail 2 "ERROR: -InputWav is required." }
if (-not $OutputAudio) { Fail 2 "ERROR: -OutputAudio is required." }

if (-not (Test-Path -LiteralPath $InputWav -PathType Leaf)) {
    Fail 2 "ERROR: input WAV not found: $InputWav"
}
$resolvedIn = (Resolve-Path -LiteralPath $InputWav).Path

# Avoid Split-Path -LiteralPath -Parent: those two switches live in
# different parameter sets on Windows PowerShell 5.1 and resolve as
# ambiguous. [System.IO.Path]::GetDirectoryName sidesteps the binder.
$outDir = [System.IO.Path]::GetDirectoryName($OutputAudio)
if ($outDir -and -not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}
if (Test-Path -LiteralPath $OutputAudio) {
    Remove-Item -LiteralPath $OutputAudio -Force
}

$url = ($BaseUrl.TrimEnd('/')) + '/api/chat/audio'
$inputBytes = [System.IO.File]::ReadAllBytes($resolvedIn)

Write-Host "Request:       POST $url"
Write-Host "Content-Type:  $ContentType"
Write-Host "Input file:    $resolvedIn"
Write-Host "Input size:    $($inputBytes.Length) bytes"
Write-Host "X-Device-Id:   $DeviceId"
Write-Host ''

$headers = @{
    'X-Device-Id' = $DeviceId
    'X-Api-Key'   = $ApiKey
}

$resp = $null
try {
    $resp = Invoke-WebRequest `
        -Method POST `
        -Uri $url `
        -Headers $headers `
        -ContentType $ContentType `
        -Body $inputBytes `
        -OutFile $OutputAudio `
        -PassThru `
        -TimeoutSec $TimeoutSec `
        -UseBasicParsing
}
catch {
    $err = $_
    $web = $null
    try { $web = $err.Exception.Response } catch {}

    if ($web) {
        $status = 0
        try { $status = [int] $web.StatusCode } catch {}
        $ct = ''
        try { $ct = $web.ContentType } catch {}
        $body = ''
        try {
            $s = $web.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($s)
            $body = $reader.ReadToEnd()
        } catch {}

        Write-Host "HTTP status:   $status" -ForegroundColor Red
        if ($ct)   { Write-Host "Content-Type:  $ct" }
        if ($body) { Write-Host "Error body:    $body" }
    }
    else {
        Write-Host "Transport error: $($err.Exception.Message)" -ForegroundColor Red
        Write-Host "Hint: is the backend running at $BaseUrl and reachable on the LAN?"
    }

    if (Test-Path -LiteralPath $OutputAudio) {
        Remove-Item -LiteralPath $OutputAudio -Force -ErrorAction SilentlyContinue
    }
    exit 1
}

$respCt = ''
if ($resp.Headers.ContainsKey('Content-Type')) {
    $respCt = [string]$resp.Headers['Content-Type']
}
if (-not $respCt -and $resp.BaseResponse) {
    try { $respCt = $resp.BaseResponse.Content.Headers.ContentType.ToString() } catch {}
}

$outSize = 0
$outResolved = $OutputAudio
if (Test-Path -LiteralPath $OutputAudio) {
    $outSize = (Get-Item -LiteralPath $OutputAudio).Length
    $outResolved = (Resolve-Path -LiteralPath $OutputAudio).Path
}

Write-Host "HTTP status:   $($resp.StatusCode) $($resp.StatusDescription)"
Write-Host "Content-Type:  $respCt"
Write-Host "Response size: $outSize bytes"
Write-Host "Output file:   $outResolved"
Write-Host ''

$isAudio = $respCt -and $respCt.StartsWith('audio/')
if (-not $isAudio) {
    Write-Host "WARNING: response Content-Type is not audio/* -- endpoint may have returned a canned / error body written to the output file." -ForegroundColor Yellow
    exit 3
}

Write-Host "OK -- play the output file to verify Areg's Armenian reply."

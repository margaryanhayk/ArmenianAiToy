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
#   -BaseUrl           Backend base URL, e.g. http://<host>:<port> (no trailing /api).
#   -DeviceId          Device GUID from POST /api/devices/register.
#   -ApiKey            Device API key (dtk_...) from POST /api/devices/register.
#   -InputWav          Local WAV file to send. 16 kHz mono PCM matches C1 firmware.
#   -OutputAudio       Where to save the assistant MP3 response.
#   -ContentType       Optional. Defaults to audio/wav.
#   -TimeoutSec        Optional. Defaults to 60 s (STT + chat + TTS end-to-end).
#   -TelegramBotToken  Optional. If set together with -TelegramChatId, the
#                      saved assistant MP3 is also sent to the given chat
#                      via Telegram Bot API (requires PowerShell 7+).
#   -TelegramChatId    Optional. Telegram chat id (numeric or @channel) to
#                      receive the MP3. Required iff -TelegramBotToken is set.
#   -TelegramCaption   Optional. Caption text attached to the Telegram audio.
#
# Optional Telegram delivery (requires PowerShell 7+):
#   pwsh -File tools\test-chat-audio.ps1 `
#     -BaseUrl "http://<backend-host>:<port>" `
#     -DeviceId "<device-guid>" `
#     -ApiKey "dtk_<api-key>" `
#     -InputWav ".\samples\hello.wav" `
#     -OutputAudio ".\artifacts\areg-response.mp3" `
#     -TelegramBotToken "<bot-token>" `
#     -TelegramChatId  "<chat-id>" `
#     -TelegramCaption "C1 voice QA test"
#
# Never commit real DeviceId / ApiKey / Wi-Fi URLs via this script's
# defaults. All sensitive values come in on the command line.
#
# MANUAL QA ONLY for the Telegram path. Never use with real child
# audio without explicit approval. Never commit a Telegram bot
# token or chat id. Tokens come on the command line; this script
# does not persist them anywhere.

param(
    [string] $BaseUrl,
    [string] $DeviceId,
    [string] $ApiKey,
    [string] $InputWav,
    [string] $OutputAudio,
    [string] $ContentType = 'audio/wav',
    [int]    $TimeoutSec  = 60,
    [string] $TelegramBotToken,
    [string] $TelegramChatId,
    [string] $TelegramCaption
)

$ErrorActionPreference = 'Stop'

function Fail([int]$code, [string]$msg) {
    Write-Host $msg -ForegroundColor Red
    exit $code
}

# Returns "<prefix>:<first3>...<last3>" for a Telegram bot token of
# the form 123456:ABCDEF...xyz, or '<masked>' on too-short / malformed
# input. Used in every log line that mentions the token so the raw
# secret never appears on stdout.
function Format-MaskedToken([string]$t) {
    if (-not $t -or $t.Length -lt 12) { return '<masked>' }
    $colon = $t.IndexOf(':')
    if ($colon -lt 1 -or $colon -ge ($t.Length - 6)) { return '<masked>' }
    $prefix = $t.Substring(0, $colon)
    $secret = $t.Substring($colon + 1)
    return "{0}:{1}...{2}" -f $prefix, $secret.Substring(0, 3),
                               $secret.Substring($secret.Length - 3)
}

if (-not $BaseUrl)     { Fail 2 "ERROR: -BaseUrl is required." }
if (-not $DeviceId)    { Fail 2 "ERROR: -DeviceId is required." }
if (-not $ApiKey)      { Fail 2 "ERROR: -ApiKey is required." }
if (-not $InputWav)    { Fail 2 "ERROR: -InputWav is required." }
if (-not $OutputAudio) { Fail 2 "ERROR: -OutputAudio is required." }

# Telegram delivery is opt-in. Both -TelegramBotToken AND -TelegramChatId
# must be supplied together (or both omitted). Caption is independent.
$telegramRequested = ($TelegramBotToken -or $TelegramChatId)
if ($telegramRequested -and (-not $TelegramBotToken -or -not $TelegramChatId)) {
    Fail 2 "ERROR: -TelegramBotToken and -TelegramChatId must be supplied together (or both omitted)."
}
if ($telegramRequested -and $PSVersionTable.PSVersion.Major -lt 7) {
    Fail 2 "ERROR: -TelegramBotToken / -TelegramChatId require PowerShell 7+ (current: $($PSVersionTable.PSVersion))."
}

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

# Optional Telegram delivery. Only runs after /api/chat/audio succeeded
# and the response was audio/*. The saved MP3 is NEVER deleted on
# Telegram failure -- the local artifact is the primary QA outcome;
# Telegram is a side-channel.
if ($telegramRequested) {
    $maskedToken = Format-MaskedToken $TelegramBotToken
    Write-Host ''
    Write-Host "Telegram:      sending audio to chat $TelegramChatId via bot $maskedToken ..."

    $tgUrl  = "https://api.telegram.org/bot$TelegramBotToken/sendAudio"
    $tgForm = @{
        chat_id = $TelegramChatId
        audio   = Get-Item -LiteralPath $OutputAudio
    }
    if ($TelegramCaption) {
        $tgForm['caption'] = $TelegramCaption
    }

    $tgResp = $null
    try {
        $tgResp = Invoke-WebRequest `
            -Method POST `
            -Uri $tgUrl `
            -Form $tgForm `
            -TimeoutSec $TimeoutSec
    }
    catch {
        $tgErr = $_
        $tgWeb = $null
        try { $tgWeb = $tgErr.Exception.Response } catch {}

        Write-Host ''
        Write-Host "Telegram delivery FAILED (bot $maskedToken)." -ForegroundColor Red

        if ($tgWeb) {
            $tgStatus = 0
            try { $tgStatus = [int] $tgWeb.StatusCode } catch {}
            Write-Host "HTTP status:   $tgStatus" -ForegroundColor Red
            $tgBody = ''
            try {
                $s = $tgWeb.GetResponseStream()
                $reader = New-Object System.IO.StreamReader($s)
                $tgBody = $reader.ReadToEnd()
            } catch {}
            if ($tgBody) { Write-Host "Error body:    $tgBody" }
        }
        else {
            Write-Host "Transport error: $($tgErr.Exception.Message)" -ForegroundColor Red
        }

        Write-Host "Hint: verify -TelegramBotToken (the bot must be added to the chat) and -TelegramChatId."
        Write-Host "Local MP3 preserved at $outResolved"
        exit 4
    }

    $tgOk = $false
    $tgDescription = ''
    $tgMessageId = $null
    try {
        $tgJson = $tgResp.Content | ConvertFrom-Json
        if ($tgJson.PSObject.Properties.Name -contains 'ok') { $tgOk = [bool] $tgJson.ok }
        if ($tgJson.PSObject.Properties.Name -contains 'description') { $tgDescription = [string] $tgJson.description }
        if ($tgJson.PSObject.Properties.Name -contains 'result' -and $tgJson.result.PSObject.Properties.Name -contains 'message_id') {
            $tgMessageId = $tgJson.result.message_id
        }
    } catch {}

    if (-not $tgOk) {
        Write-Host ''
        Write-Host "Telegram API rejected the upload (bot $maskedToken)." -ForegroundColor Red
        if ($tgDescription) { Write-Host "Description:   $tgDescription" }
        Write-Host "Hint: verify -TelegramBotToken (the bot must be added to the chat) and -TelegramChatId."
        Write-Host "Local MP3 preserved at $outResolved"
        exit 4
    }

    if ($tgMessageId) {
        Write-Host "OK -- sent to Telegram chat $TelegramChatId (message_id=$tgMessageId)."
    } else {
        Write-Host "OK -- sent to Telegram chat $TelegramChatId."
    }
}

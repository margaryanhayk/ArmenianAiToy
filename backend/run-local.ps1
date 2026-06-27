# ---------------------------------------------------------------------------
# run-local.ps1 — start the Areg backend as a LOCAL home host.
#
# Interim hosting (owner decision 2026-06-27): run on this computer, on the
# home Wi-Fi, plain HTTP. Fine for your own testing / a closed bench. NOT a
# public launch posture — real children using it needs a proper host + HTTPS
# + the consent/legal work first (see Desktop/TODO).
#
# Binds 0.0.0.0:5000 (per appsettings.json "Urls"), so the toy and the phone
# app reach it at  http://<this-PC's-LAN-IP>:5000  on the same Wi-Fi.
# Development environment → uses the local SQLite DB + your user-secrets
# (Jwt:Key, OpenAI:ApiKey). Only ONE instance can use :5000 — stop any other
# backend first.  Ctrl+C to stop.
# ---------------------------------------------------------------------------
$ErrorActionPreference = "Stop"
$env:ASPNETCORE_ENVIRONMENT = "Development"

# Story serving for the bench:
#  - side-load the «Անբան Հուռին» DRAFT verbatim (it's not promoted to the
#    runtime library; requireApproved:false serves it locally without changing
#    its text/status). This is what the toy's AREG_STORY_ID = "anban-huri" needs.
#  - open the audio stream locally (no signing key on a home bench). The stream
#    is fail-closed by default; this flag is the explicit dev/bench opt-in.
$env:Story__DefaultStoryId = "anban-huri"
$env:Story__ExtraStoryFiles__0 = (Join-Path $PSScriptRoot "content\story-drafts\anban-huri.story.json")
$env:StoryAudio__AllowUnauthenticated = "true"

Write-Host "Your LAN addresses (point the toy/app at one of these:5000):" -ForegroundColor Cyan
Get-NetIPAddress -AddressFamily IPv4 |
  Where-Object { $_.IPAddress -like "192.168.*" -or $_.IPAddress -like "10.*" } |
  ForEach-Object { Write-Host ("   http://{0}:5000" -f $_.IPAddress) -ForegroundColor Green }

Set-Location "$PSScriptRoot/src/ArmenianAiToy.Api"
dotnet run -c Release

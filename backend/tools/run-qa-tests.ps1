# Runs an Anban-Huri-style Q&A test file against the backend's TEXT Q&A
# endpoint and prints each answer. No ESP32, no voice — pure text.
#
# Usage:
#   pwsh backend/tools/run-qa-tests.ps1
#   pwsh backend/tools/run-qa-tests.ps1 -File path\to\cases.txt -StoryId anban-huri
#
# Test file format: one "<segment> | <question>" per line; # and blank
# lines ignored. See backend/content/qa-tests/anban-huri-qa.txt.

param(
    [string]$File = "$PSScriptRoot\..\content\qa-tests\anban-huri-qa.txt",
    [string]$StoryId = "anban-huri",
    [string]$BaseUrl = "http://127.0.0.1:5000"
)

if (-not (Test-Path $File)) {
    Write-Host "Test file not found: $File" -ForegroundColor Red
    exit 1
}

$url = "$BaseUrl/api/story-qa-text"
$lines = Get-Content -LiteralPath $File -Encoding utf8
$caseNum = 0
$results = @()

foreach ($line in $lines) {
    $trimmed = $line.Trim()
    if ($trimmed -eq "" -or $trimmed.StartsWith("#")) { continue }

    $parts = $trimmed -split '\|', 2
    if ($parts.Count -lt 2) {
        Write-Host "skip (no '|'): $trimmed" -ForegroundColor DarkYellow
        continue
    }
    $segment = 0
    [void][int]::TryParse($parts[0].Trim(), [ref]$segment)
    $question = $parts[1].Trim()
    if ($question -eq "") { continue }

    $caseNum++
    $bodyObj = @{ storyId = $StoryId; segment = $segment; question = $question }
    $json = $bodyObj | ConvertTo-Json -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)

    try {
        $resp = Invoke-WebRequest -Uri $url -Method POST -Body $bytes `
            -ContentType "application/json; charset=utf-8" -TimeoutSec 60 -UseBasicParsing
        $j = [System.Text.Encoding]::UTF8.GetString($resp.RawContentStream.ToArray()) | ConvertFrom-Json
        $tag = if ($j.usedFallback) { " [FALLBACK]" } elseif ($j.firstRejection -ne "None") { " [rejected:$($j.firstRejection)->repaired]" } else { "" }
        Write-Host ""
        Write-Host ("#{0}  (segment {1}){2}" -f $caseNum, $j.segment, $tag) -ForegroundColor Cyan
        Write-Host ("  Q: {0}" -f $question)
        Write-Host ("  A: {0}" -f $j.answer) -ForegroundColor Green
        $results += [pscustomobject]@{ Case=$caseNum; Segment=$j.segment; Question=$question; Answer=$j.answer; Fallback=$j.usedFallback; Rejection=$j.firstRejection }
    }
    catch {
        $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { "n/a" }
        Write-Host ""
        Write-Host ("#{0}  (segment {1})  REQUEST FAILED ({2})" -f $caseNum, $segment, $code) -ForegroundColor Red
        Write-Host ("  Q: {0}" -f $question)
    }
}

Write-Host ""
Write-Host ("Ran {0} case(s) against {1}" -f $caseNum, $url) -ForegroundColor DarkCyan

# Machine-readable block so the assistant can lift the pairs for review.
Write-Host "===QA_PAIRS_JSON==="
$results | ConvertTo-Json -Depth 4
Write-Host "===END_QA_PAIRS_JSON==="

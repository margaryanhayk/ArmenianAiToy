# Runs an INLINE Q&A test: the whole story with "?? <question>" lines
# inserted at the exact interrupt points. For each question it shows the
# story text right before (pause point), the answer, and the text right
# after (where playback resumes) — mirroring the real device flow.
#
# Usage:
#   pwsh backend/tools/run-qa-inline.ps1
#   pwsh backend/tools/run-qa-inline.ps1 -File path\to\inline.txt
#
# File format: see backend/content/qa-tests/anban-huri-inline.txt
#   '=== SEGMENT N ===' headers mark which segment you're in.
#   '?? <question>' lines are interrupt points. '#'/blank lines ignored.

param(
    [string]$File = "$PSScriptRoot\..\content\qa-tests\anban-huri-inline.txt",
    [string]$StoryId = "anban-huri",
    [string]$BaseUrl = "http://127.0.0.1:5000"
)

if (-not (Test-Path $File)) { Write-Host "File not found: $File" -ForegroundColor Red; exit 1 }
$url = "$BaseUrl/api/story-qa-text"
$lines = Get-Content -LiteralPath $File -Encoding utf8

# Tokenize into ordered Story / Question items, tracking the segment.
$tokens = @()
$seg = 0
foreach ($line in $lines) {
    $t = $line.Trim()
    if ($t -eq "" -or $t.StartsWith("#")) { continue }
    if ($t -match '^===\s*SEGMENT\s+(\d+)\s*===$') { $seg = [int]$matches[1]; continue }
    if ($t.StartsWith("??")) { $tokens += [pscustomobject]@{ Type="Q"; Seg=$seg; Text=$t.Substring(2).Trim() } }
    else { $tokens += [pscustomobject]@{ Type="S"; Seg=$seg; Text=$t } }
}

function Tail($s, $n) { if ($s.Length -gt $n) { "…" + $s.Substring($s.Length - $n) } else { $s } }
function Head($s, $n) { if ($s.Length -gt $n) { $s.Substring(0, $n) + "…" } else { $s } }

$results = @()
$num = 0
for ($i = 0; $i -lt $tokens.Count; $i++) {
    if ($tokens[$i].Type -ne "Q") { continue }
    $num++
    $q = $tokens[$i]
    $before = ""; for ($j=$i-1; $j -ge 0; $j--) { if ($tokens[$j].Type -eq "S") { $before = $tokens[$j].Text; break } }
    $after  = ""; for ($j=$i+1; $j -lt $tokens.Count; $j++) { if ($tokens[$j].Type -eq "S") { $after = $tokens[$j].Text; break } }

    $body = @{ storyId=$StoryId; segment=$q.Seg; question=$q.Text } | ConvertTo-Json -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
    $answer = "(REQUEST FAILED)"; $fb=$false; $rej=""
    try {
        $resp = Invoke-WebRequest -Uri $url -Method POST -Body $bytes -ContentType "application/json; charset=utf-8" -TimeoutSec 60 -UseBasicParsing
        $jr = [System.Text.Encoding]::UTF8.GetString($resp.RawContentStream.ToArray()) | ConvertFrom-Json
        $answer = $jr.answer; $fb = $jr.usedFallback; $rej = $jr.firstRejection
    } catch {}

    $tag = if ($fb) {" [FALLBACK]"} elseif ($rej -ne "None" -and $rej -ne "") {" [repaired:$rej]"} else {""}
    Write-Host ""
    Write-Host ("#{0}  (segment {1}){2}" -f $num, $q.Seg, $tag) -ForegroundColor Cyan
    Write-Host ("  paused after : {0}" -f (Tail $before 90)) -ForegroundColor DarkGray
    Write-Host ("  Q: {0}" -f $q.Text) -ForegroundColor Yellow
    Write-Host ("  A: {0}" -f $answer) -ForegroundColor Green
    Write-Host ("  resumes with : {0}" -f (Head $after 90)) -ForegroundColor DarkGray
    $results += [pscustomobject]@{ N=$num; Seg=$q.Seg; PauseAfter=(Tail $before 90); Question=$q.Text; Answer=$answer; Resume=(Head $after 90); Fallback=$fb; Rejection=$rej }
}

Write-Host ""
Write-Host ("Ran {0} inline question(s) against {1}" -f $num, $url) -ForegroundColor DarkCyan
Write-Host "===QA_PAIRS_JSON==="
$results | ConvertTo-Json -Depth 4
Write-Host "===END_QA_PAIRS_JSON==="

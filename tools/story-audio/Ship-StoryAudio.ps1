# ---------------------------------------------------------------------------
# Ship-StoryAudio.ps1 - take story narration from ANY source and put it on the
# shelf safely.
#
# Deliberately knows nothing about ElevenLabs. The final voice is changing
# (owner decision 2026-08-04) and may well be a person in a room with a
# microphone, so the last mile has to accept "here are five MP3s" from
# anywhere. Everything this script checks is something that has already gone
# wrong once and reached a child's ears:
#
#   ID3/Xing markers mid-file   a 4-minute story stopped after 34 seconds on
#                               iPhone, because a strict player believes the
#                               first length header it finds
#   too short for the text      the model curtailed the render; the story stops
#                               in the middle and nothing noticed
#   quiet                       11 dB below the rest of the library sounds thin
#                               and far away on the toy's small speaker
#   stale manifest              right file on disk, old sha256/size in config,
#                               so every toy refuses the download
#   forgotten Version bump      new bytes, same Version - the toy keeps the
#                               copy it already has and nothing changes
#
# USAGE
#   ./Ship-StoryAudio.ps1 -In <folder>              # check only, change nothing
#   ./Ship-StoryAudio.ps1 -In <folder> -Fix         # + repair and level a copy
#   ./Ship-StoryAudio.ps1 -In <folder> -Fix -Apply  # + install + patch config
#
#   <folder> holds one file per story named <storyId>.mp3 or <storyId>.wav,
#   e.g. anban-huri.mp3. WAV is accepted because mix_ambience.py deliberately
#   writes one - it will not encode, since loudness must be measured on the
#   FINISHED mix and an intermediate MP3 would be a second lossy generation for
#   nothing. Its closing line points at this script, so this script has to be
#   able to read what it wrote.
#   Story ids must match backend/src/ArmenianAiToy.Application/Stories/Content.
#   -Apply writes into story-audio/ and appsettings.json and bumps Version.
#
# NEEDS ffmpeg + ffprobe on PATH. Nothing else.
#
# WHAT IT DOES NOT DO: judge whether the voice is any good, or whether a join
# between two pieces is audible. That is the human listen test, and it is
# still the last gate before anything reaches a child.
# ---------------------------------------------------------------------------
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$In,
    [switch]$Fix,
    [switch]$Apply,
    # The level the owner approved on 2026-08-04, measured off the narration
    # they kept. Every story in the library sits here; a new one must too.
    [double]$TargetLufs = -16.4,
    # Armenian narration in this voice runs about 15 characters a second.
    # Only used to catch a story that stops in the middle, never to argue
    # about a brisk delivery.
    [double]$CharsPerSecond = 15.0,
    [double]$ShortFloor = 0.70
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$contentDir = Join-Path $repo 'backend/src/ArmenianAiToy.Application/Stories/Content'
$audioDir = Join-Path $repo 'backend/src/ArmenianAiToy.Api/story-audio'
$settings = Join-Path $repo 'backend/src/ArmenianAiToy.Api/appsettings.json'

foreach ($exe in 'ffmpeg', 'ffprobe') {
    if (-not (Get-Command $exe -ErrorAction SilentlyContinue)) {
        Write-Error "$exe is not on PATH. Install ffmpeg (winget install Gyan.FFmpeg)."
    }
}

function Get-StoryChars([string]$storyId) {
    $p = Join-Path $contentDir "$storyId.story.json"
    if (-not (Test-Path $p)) { return 0 }
    $j = Get-Content $p -Raw -Encoding UTF8 | ConvertFrom-Json
    $segs = @($j.segments) | ForEach-Object { if ($_ -is [string]) { $_ } else { $_.text } }
    return ($segs -join "`n`n").Length
}

# ffmpeg writes everything it has to say on stderr, and Windows PowerShell 5.1
# turns a native command's stderr into error records - with -ErrorAction Stop
# that kills the script on a perfectly successful run. Drive the process
# directly so stdout and stderr are just strings.
function Invoke-Tool([string]$exe, [string[]]$toolArgs) {
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    # ProcessStartInfo.ArgumentList does not exist on .NET Framework, which is
    # what Windows PowerShell 5.1 runs on, so the arguments are quoted by hand.
    $psi.Arguments = ($toolArgs | ForEach-Object {
            if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
        }) -join ' '
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $p = [Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEnd()
    $err = $p.StandardError.ReadToEnd()
    $p.WaitForExit()
    return [pscustomobject]@{ ExitCode = $p.ExitCode; Out = $out; Err = $err }
}

function Get-Duration([string]$path) {
    $r = Invoke-Tool 'ffprobe' @('-v', 'error', '-show_entries', 'format=duration', '-of', 'csv=p=0', $path)
    if ($r.ExitCode -ne 0) { Write-Error "ffprobe failed on ${path}: $($r.Err)" }
    return [double]$r.Out.Trim()
}

function Get-Lufs([string]$path) {
    $r = Invoke-Tool 'ffmpeg' @('-hide_banner', '-nostats', '-i', $path,
        '-af', 'loudnorm=print_format=summary', '-f', 'null', '-')
    if ($r.Err -match 'Input Integrated:\s*(-?[\d.]+)') { return [double]$Matches[1] }
    return $null
}

# A healthy MP3 carries at most one ID3 tag, at offset 0. More than one means
# pieces were glued together with their wrappers left in.
function Get-Id3Count([string]$path) {
    # Every field is checked, because the letters "ID3" occur in audio data by
    # chance and a loose test turns a good story into a refusal. This happened:
    # a clean three-piglets render carried the bytes 49 44 33 at offset
    # 1,996,978 declaring a 29 MB tag inside a 2 MB file, and the shipper
    # refused to install it while check_story_audio.py — which validates the
    # version byte — passed it. Two gates that disagree are worse than one, so
    # this now applies the SAME rules as _is_id3v2_header in that script.
    $b = [IO.File]::ReadAllBytes($path)
    $n = 0
    for ($i = 0; $i -lt $b.Length - 9; $i++) {
        if ($b[$i] -ne 0x49 -or $b[$i + 1] -ne 0x44 -or $b[$i + 2] -ne 0x33) { continue }
        if ($b[$i + 3] -ne 2 -and $b[$i + 3] -ne 3 -and $b[$i + 3] -ne 4) { continue }  # major version
        if ($b[$i + 4] -eq 0xFF) { continue }                                           # reserved revision
        # The four size bytes are syncsafe: the high bit of each is always clear.
        if ((($b[$i + 6] -bor $b[$i + 7] -bor $b[$i + 8] -bor $b[$i + 9]) -band 0x80) -ne 0) { continue }
        $size = (($b[$i + 6] -band 0x7f) -shl 21) -bor (($b[$i + 7] -band 0x7f) -shl 14) -bor
                (($b[$i + 8] -band 0x7f) -shl 7) -bor ($b[$i + 9] -band 0x7f)
        if ($i + 10 + $size -gt $b.Length) { continue }   # a tag cannot run past the file
        $n++
        $i += 9 + $size
    }
    return $n
}

# Repair + level in one ffmpeg re-encode. Decoding to PCM and back drops every
# stray tag and per-chunk length header by construction, so the joined file
# comes out as one continuous stream. 192 kbps against a 128 kbps source keeps
# the extra generation cheap.
function Repair-And-Level([string]$src, [string]$dst) {
    # Pass 1 measures, pass 2 applies what was measured. One pass alone guesses
    # the gain from a running estimate and lands a decibel or two off.
    $measure = Invoke-Tool 'ffmpeg' @('-hide_banner', '-nostats', '-i', $src,
        '-af', "loudnorm=I=${TargetLufs}:TP=-1.0:LRA=11:print_format=json", '-f', 'null', '-')
    $text = $measure.Err
    $json = $text.Substring($text.LastIndexOf('{'))
    $json = $json.Substring(0, $json.LastIndexOf('}') + 1) | ConvertFrom-Json
    $filter = "loudnorm=I=${TargetLufs}:TP=-1.0:LRA=11" +
              ":measured_I=$($json.input_i):measured_TP=$($json.input_tp)" +
              ":measured_LRA=$($json.input_lra):measured_thresh=$($json.input_thresh)" +
              ":offset=$($json.target_offset):linear=true"
    $r = Invoke-Tool 'ffmpeg' @('-hide_banner', '-v', 'error', '-y', '-i', $src,
        '-af', $filter, '-ar', '44100', '-ac', '1', '-c:a', 'libmp3lame', '-b:a', '192k', $dst)
    if ($r.ExitCode -ne 0) { Write-Error "ffmpeg failed on ${src}: $($r.Err)" }
}

$work = Join-Path ([IO.Path]::GetTempPath()) ("areg-ship-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
if ($Fix) { New-Item -ItemType Directory -Force -Path $work | Out-Null }

$rows = @()
$inputs = @(Get-ChildItem -Path $In -File |
            Where-Object { $_.Extension -in '.mp3', '.wav' } | Sort-Object Name)
# A story delivered as both .wav and .mp3 would be checked twice and installed
# twice, the second overwriting the first with whichever sorted later. Refuse.
$dupes = $inputs | Group-Object { [IO.Path]::GetFileNameWithoutExtension($_.Name) } |
         Where-Object Count -gt 1
if ($dupes) {
    Write-Error ("Both .wav and .mp3 present for: " + (($dupes.Name) -join ', ') +
                 ". Keep one - which of the two ships would otherwise depend on " +
                 "filename sort order.")
    exit 1
}
foreach ($f in $inputs) {
    $id = [IO.Path]::GetFileNameWithoutExtension($f.Name)
    $chars = Get-StoryChars $id
    $expected = if ($chars -gt 0) { $chars / $CharsPerSecond } else { 0 }

    $src = $f.FullName
    if ($f.Extension -eq '.wav' -and -not $Fix) {
        Write-Error ("$($f.Name) is a WAV and -Fix was not given. The checks " +
                     "below count MP3 frames and -Apply installs bytes as-is, " +
                     "so this would ship a WAV named .mp3. Re-run with -Fix.")
        exit 1
    }
    if ($Fix) {
        $dst = Join-Path $work "$id.mp3"
        Repair-And-Level $src $dst
        $src = $dst
    }

    $rows += [pscustomobject]@{
        StoryId   = $id
        Path      = $src
        Chars     = $chars
        Duration  = Get-Duration $src
        Expected  = $expected
        Lufs      = Get-Lufs $src
        Id3Count  = Get-Id3Count $src
        SizeBytes = (Get-Item $src).Length
        Sha256    = (Get-FileHash $src -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

if ($rows.Count -eq 0) { Write-Error "No .mp3 or .wav files in $In" }

# [int] in PowerShell ROUNDS, so [int](220/60) is 4 and a 3:40 story prints as
# 4:40. Floor the minutes and take the remainder from the floored value.
$fmt = {
    param($s)
    $m = [Math]::Floor($s / 60)
    "{0}:{1:00}" -f $m, [Math]::Floor($s - $m * 60)
}
$problems = @()
Write-Host ""
Write-Host ("{0,-16} {1,8} {2,8} {3,8} {4,6}  {5}" -f 'story', 'length', 'needs', 'loudness', 'ID3', 'verdict')
foreach ($r in $rows) {
    $issues = @()
    if ($r.Chars -eq 0) { $issues += 'no story text found - is the id right?' }
    elseif ($r.Expected -gt 0 -and $r.Duration -lt $r.Expected * $ShortFloor) {
        $issues += ("CUT SHORT ({0}%)" -f [int](100 * $r.Duration / $r.Expected))
    }
    if ($r.Id3Count -gt 1) { $issues += "$($r.Id3Count) ID3 tags - pieces glued badly, will stop early" }
    if ($null -ne $r.Lufs -and [Math]::Abs($r.Lufs - $TargetLufs) -gt 2.0) {
        $issues += ("loudness {0} vs {1} wanted" -f $r.Lufs, $TargetLufs)
    }
    if ($issues.Count -gt 0) { $problems += "$($r.StoryId): $($issues -join '; ')" }

    Write-Host ("{0,-16} {1,8} {2,8} {3,8} {4,6}  {5}" -f `
        $r.StoryId, (& $fmt $r.Duration), (& $fmt $r.Expected), $r.Lufs, $r.Id3Count,
        $(if ($issues.Count -eq 0) { 'ok' } else { $issues -join '; ' }))
}
Write-Host ""

if ($problems.Count -gt 0) {
    Write-Host "NOT SHIPPABLE:" -ForegroundColor Red
    $problems | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    if (-not $Fix) { Write-Host "  (re-run with -Fix to repair and level a copy first)" }
    if ($Apply) { Write-Error "Refusing to install files with the problems above." }
    exit 1
}

if (-not $Apply) {
    Write-Host "Manifest values (paste into ContentSync:Stories, and BUMP each Version):"
    $rows | ForEach-Object {
        Write-Host ("  {0,-16} SizeBytes={1,-10} Sha256={2}" -f $_.StoryId, $_.SizeBytes, $_.Sha256)
    }
    Write-Host ""
    Write-Host "Nothing was changed. Add -Apply to install and patch appsettings.json."
    exit 0
}

# --- install + patch config -------------------------------------------------
$text = Get-Content $settings -Raw -Encoding UTF8
foreach ($r in $rows) {
    Copy-Item $r.Path (Join-Path $audioDir "$($r.StoryId).mp3") -Force

    $pattern = '("StoryId":\s*"' + [Regex]::Escape($r.StoryId) + '",\s*\r?\n\s*"Version":\s*)(\d+)' +
               '([\s\S]*?"SizeBytes":\s*)\d+([\s\S]*?"Sha256":\s*")[0-9a-fA-F]{64}'
    $matched = $false
    $text = [Regex]::Replace($text, $pattern, {
            param($m)
            $script:matched = $true
            $next = [int]$m.Groups[2].Value + 1
            "$($m.Groups[1].Value)$next$($m.Groups[3].Value)$($r.SizeBytes)$($m.Groups[4].Value)$($r.Sha256)"
        }, 1)
    if (-not $matched) { Write-Error "No ContentSync entry for '$($r.StoryId)' in appsettings.json - add one first." }
    Write-Host "  installed $($r.StoryId).mp3 and updated its manifest entry (Version bumped)"
}
[IO.File]::WriteAllText($settings, $text, (New-Object Text.UTF8Encoding($false)))

Write-Host ""
Write-Host "Done. Still to do, in order:"
Write-Host "  1. LISTEN to each story end to end - no tool can hear a bad join."
Write-Host "  2. dotnet test"
Write-Host "  3. commit + push (Railway deploys; toys re-download on the Version bump)"

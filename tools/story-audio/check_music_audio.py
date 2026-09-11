#!/usr/bin/env python3
"""Check shipped bedtime-music tracks for the same structural defects
check_story_audio.py catches in narration, minus the one check that does
not apply.

WHY A SEPARATE SCRIPT AND NOT A FLAG ON check_story_audio.py
--------------------------------------------------------------
check_story_audio.py's whole second check — is the file long enough for the
TEXT it is supposed to be reading — has no equivalent here. A music track
has no `segments[]` to compare against; "long enough" for a bedtime track
means "3 to 5 minutes", a fixed window, not a per-item computed one. Bolting
a duration-window mode onto a script whose docstring and CLI are about
narration length would make both harder to read. So this file REUSES the
frame-walking MP3 parser (`scan`) from check_story_audio.py by import —
the one check that IS the same, the multi-ID3-tag defect, must stay
byte-for-byte the same logic, not a second implementation that could drift
from the first the way the two-gates-disagree incident already did once
(see check_story_audio.py's own docstring and Ship-StoryAudio.ps1's
Get-Id3Count comment).

It NEVER writes. Same posture as check_story_audio.py.

USAGE
    python3 tools/story-audio/check_music_audio.py
    python3 tools/story-audio/check_music_audio.py --audio-dir <dir>

Exit code 0 if every track passes, 1 otherwise — so it can gate a commit.
"""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from check_story_audio import scan, mmss  # noqa: E402  (see docstring — reused, not reimplemented)

# The owner's bedtime-music brief: "3 to 5 minutes each". No text to check
# length against, so this window IS the length check.
MIN_SECONDS = 180.0
MAX_SECONDS = 300.0


def main() -> int:
    repo = Path(__file__).resolve().parents[2]
    import argparse

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--audio-dir",
        type=Path,
        default=repo / "backend/src/ArmenianAiToy.Api/story-audio/music",
        help="folder holding <trackId>-v<n>.mp3 (default: the shipped bedtime-music folder)",
    )
    args = parser.parse_args()

    files = sorted(args.audio_dir.glob("*.mp3"))
    if not files:
        print(f"no .mp3 files in {args.audio_dir}", file=sys.stderr)
        return 1

    print(f"{'track':<28} {'length':>7} {'kbps':>5}  verdict")
    print("-" * 60)

    failures = []
    for mp3 in files:
        track_id = mp3.stem
        info = scan(mp3)

        issues = []
        if info["seconds"] < MIN_SECONDS:
            issues.append(
                f"too short ({mmss(info['seconds'])} < {mmss(MIN_SECONDS)}) — "
                "below the 3-minute bedtime-music floor"
            )
        if info["seconds"] > MAX_SECONDS:
            issues.append(
                f"too long ({mmss(info['seconds'])} > {mmss(MAX_SECONDS)}) — "
                "above the 5-minute bedtime-music ceiling"
            )
        if info["id3_tags"] > 1:
            issues.append(
                f"{info['id3_tags']} ID3 tags - pieces glued badly, will stop early"
            )

        kbps = ",".join(str(b) for b in info["bitrates"]) or "?"
        verdict = "ok" if not issues else "; ".join(issues)
        print(f"{track_id:<28} {mmss(info['seconds']):>7} {kbps:>5}  {verdict}")
        if issues:
            failures.append(track_id)

    print()
    if failures:
        print(f"FAIL - {len(failures)} of {len(files)}: {', '.join(failures)}")
        return 1

    print(f"PASS - {len(files)} bedtime-music track(s) are complete and cleanly encoded.")
    print("The human listen test is still the last gate.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

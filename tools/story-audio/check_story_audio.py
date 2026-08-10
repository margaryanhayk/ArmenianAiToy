#!/usr/bin/env python3
"""Check shipped story narration for the two defects that stop a story early.

WHY THIS EXISTS ALONGSIDE Ship-StoryAudio.ps1
---------------------------------------------
Ship-StoryAudio.ps1 already checks everything this does, and repairs and
installs besides. But it needs ffmpeg and ffprobe on PATH, and on 2026-08-10 a
review found that the eight shipped stories had never been through it: three of
them play roughly a quarter to a third of their text, and one carries two ID3
tags. The gate existed and was skipped.

So this is the same two checks with NOTHING to install - it parses the MP3
frames directly and reads the story text from the JSON beside it. It runs on
any machine, in CI, or in a container, which is the whole point: a check that
needs a toolchain is a check that gets skipped on the day it matters.

It NEVER writes. Repair and install stay with the PowerShell script, which owns
the loudness pass and the manifest patch.

USAGE
    python3 tools/story-audio/check_story_audio.py
    python3 tools/story-audio/check_story_audio.py --audio-dir <dir>

Exit code 0 if every story passes, 1 otherwise - so it can gate a commit.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

# Both thresholds are copied from Ship-StoryAudio.ps1 on purpose. If one moves,
# move the other - two gates that disagree are worse than one.
CHARS_PER_SECOND = 15.0  # Armenian narration in this library's voice
SHORT_FLOOR = 0.70  # below this share of the expected length = curtailed

# MPEG audio frame tables. Layer III only, which is all this library ships.
_BITRATES_V1 = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0]
_BITRATES_V2 = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0]
_SAMPLE_RATES = {
    3: [44100, 48000, 32000],  # MPEG 1
    2: [22050, 24000, 16000],  # MPEG 2
    0: [11025, 12000, 8000],  # MPEG 2.5
}


def _is_id3v2_header(data: bytes, at: int) -> bool:
    """True only for a structurally valid ID3v2 header, not any "ID3" bytes.

    Every field is checked because the whole point is to distinguish a real
    second tag from the letters happening to appear inside audio data.
    """
    if data[at : at + 3] != b"ID3" or at + 10 > len(data):
        return False
    if data[at + 3] not in (2, 3, 4):  # major version; nothing else exists
        return False
    if data[at + 4] == 0xFF:  # revision; 0xFF is reserved/invalid
        return False
    # The 4 size bytes are syncsafe: the high bit of each is always clear.
    return all(byte < 0x80 for byte in data[at + 6 : at + 10])


def _id3v2_end(data: bytes, at: int) -> int:
    """Return the offset just past an ID3v2 tag starting at `at`, or `at`."""
    if data[at : at + 3] != b"ID3" or at + 10 > len(data):
        return at
    flags = data[at + 5]
    size = 0
    for byte in data[at + 6 : at + 10]:
        size = (size << 7) | (byte & 0x7F)  # syncsafe integer
    end = at + 10 + size
    if flags & 0x10:  # a footer is present and is another 10 bytes
        end += 10
    return end


def scan(path: Path) -> dict:
    """Sum every frame's duration. Correct for VBR as well as CBR, because a
    header-only estimate is exactly what missed the truncation the first time."""
    data = path.read_bytes()

    # Count ID3v2 tags anywhere in the file. More than one means pieces were
    # concatenated with their wrappers left in, and a strict player believes
    # the first length header it finds and stops there.
    #
    # The validation below is deliberately strict. A bare search for "ID3"
    # false-positives on ordinary audio data - ulik.mp3 contains those three
    # bytes at offset 427715, and a loose check read it as a second tag and
    # reported a defect that is not there.
    # -9, not -10: a header needs 10 bytes, so the last position that can hold
    # one is len-10 and range() is exclusive. A tag sitting at the very end of
    # the file - nothing after it - would otherwise go uncounted.
    id3_tags = sum(
        1 for at in range(len(data) - 9) if _is_id3v2_header(data, at)
    )

    start = _id3v2_end(data, 0)
    seconds = 0.0
    frames = 0
    bitrates = set()
    rates = set()
    channels = set()

    pos = start
    end = len(data)
    while pos < end - 4:
        if data[pos] != 0xFF or (data[pos + 1] & 0xE0) != 0xE0:
            pos += 1
            continue
        version = (data[pos + 1] >> 3) & 0x03  # 3=MPEG1, 2=MPEG2, 0=MPEG2.5
        layer = (data[pos + 1] >> 1) & 0x03  # 1 = Layer III
        if version == 1 or layer != 1:
            pos += 1
            continue
        table = _BITRATES_V1 if version == 3 else _BITRATES_V2
        bitrate = table[(data[pos + 2] >> 4) & 0x0F]
        rate_index = (data[pos + 2] >> 2) & 0x03
        if bitrate == 0 or rate_index == 3:
            pos += 1
            continue
        rate = _SAMPLE_RATES[version][rate_index]
        padding = (data[pos + 2] >> 1) & 0x01
        samples = 1152 if version == 3 else 576
        length = int(samples // 8 * 1000 * bitrate / rate) + padding
        if length <= 4:
            pos += 1
            continue
        bitrates.add(bitrate)
        rates.add(rate)
        channels.add("mono" if ((data[pos + 3] >> 6) & 0x03) == 3 else "stereo")
        seconds += samples / rate
        frames += 1
        pos += length

    return {
        "seconds": seconds,
        "frames": frames,
        "bitrates": sorted(bitrates),
        "sample_rates": sorted(rates),
        "channels": sorted(channels),
        "id3_tags": id3_tags,
        "bytes": len(data),
    }


def story_chars(story_json: Path) -> int:
    """Characters of narrated text - the segments, joined as they are spoken."""
    story = json.loads(story_json.read_text(encoding="utf-8"))
    return len(" ".join(story.get("segments", [])))


def mmss(seconds: float) -> str:
    return f"{int(seconds // 60)}:{int(seconds % 60):02d}"


def main() -> int:
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--audio-dir",
        type=Path,
        default=repo / "backend/src/ArmenianAiToy.Api/story-audio",
        help="folder holding <storyId>.mp3 (default: the shipped story-audio)",
    )
    parser.add_argument(
        "--content-dir",
        type=Path,
        default=repo / "backend/src/ArmenianAiToy.Application/Stories/Content",
        help="folder holding <storyId>.story.json",
    )
    args = parser.parse_args()

    files = sorted(args.audio_dir.glob("*.mp3"))
    if not files:
        print(f"no .mp3 files in {args.audio_dir}", file=sys.stderr)
        return 1

    print(f"{'story':<20} {'length':>7} {'needs':>7} {'share':>6} {'kbps':>5}  verdict")
    print("-" * 72)

    failures = []
    for mp3 in files:
        story_id = mp3.stem
        info = scan(mp3)
        story_json = args.content_dir / f"{story_id}.story.json"

        expected = 0.0
        if story_json.exists():
            expected = story_chars(story_json) / CHARS_PER_SECOND

        issues = []
        if expected > 0 and info["seconds"] < expected * SHORT_FLOOR:
            issues.append("TRUNCATED - stops in the middle of the story")
        if info["id3_tags"] > 1:
            issues.append(
                f"{info['id3_tags']} ID3 tags - pieces glued badly, will stop early"
            )
        if not story_json.exists():
            issues.append("no story text to check the length against")

        share = f"{info['seconds'] / expected:.0%}" if expected > 0 else "-"
        kbps = ",".join(str(b) for b in info["bitrates"]) or "?"
        verdict = "ok" if not issues else "; ".join(issues)
        print(
            f"{story_id:<20} {mmss(info['seconds']):>7} "
            f"{mmss(expected) if expected else '-':>7} {share:>6} {kbps:>5}  {verdict}"
        )
        if issues:
            failures.append(story_id)

    print()
    if failures:
        print(f"FAIL - {len(failures)} of {len(files)}: {', '.join(failures)}")
        print(
            "Re-render the failing stories full length, then ship them with\n"
            "tools/story-audio/Ship-StoryAudio.ps1 -Fix -Apply (it also levels\n"
            "to -16.4 LUFS and bumps each Version, which this check cannot)."
        )
        return 1

    print(f"PASS - {len(files)} stories are complete and cleanly encoded.")
    print("The human listen test is still the last gate.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Turn a seconds-based segment map into the BYTE map the backend reads.

WHY THIS EXISTS
---------------
Two halves of the same feature disagreed about what a `.segments.json` is, and
they disagreed SILENTLY:

  mix_ambience.py writes   {"storyId": ..., "unit": "seconds",
                            "starts": [0.0, 18.5, ...], "durations": [...]}

  StoryQaController reads  System.Text.Json.Deserialize<long[]>  — a bare array
                           of BYTE offsets into the finished MP3.

`LoadSegmentMap` catches the exception and returns an empty array, so the
mismatch does not throw: the backend just falls back to guessing a child's
position from `offset / fileSize`, exactly as it does today. It would have
looked like the map was working while the guess was still running — and the
guess is what answers a question about the wrong scene.

Byte offsets cannot be known when the mix is made, because they depend on the
MP3 encode that happens afterwards in Ship-StoryAudio.ps1. So this runs LAST,
against the finished file:

    mix_ambience.py -> <id>.segments.json (seconds)
    Ship-StoryAudio.ps1 -Fix -Apply -> <id>.mp3
    segments_to_bytes.py -> <id>.segments.json (bytes)   <- you are here

USAGE
    python3 tools/story-audio/segments_to_bytes.py \
        --seconds <id>.segments.json --mp3 <id>.mp3 --out <id>.segments.json

    --self-test    verify the frame walk and the mapping, no files needed

Needs nothing installed: the MP3 is walked frame by frame in pure Python, the
same way check_story_audio.py measures duration.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

_BITRATES_V1_L3 = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0]
_BITRATES_V2_L3 = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0]
_RATES = {3: [44100, 48000, 32000], 2: [22050, 24000, 16000], 0: [11025, 12000, 8000]}


def _is_id3v2(data: bytes, at: int) -> bool:
    if at + 10 > len(data) or data[at:at + 3] != b"ID3":
        return False
    if data[at + 3] == 0xFF or data[at + 4] == 0xFF:
        return False
    return all(data[at + 6 + i] < 0x80 for i in range(4))


def _id3v2_end(data: bytes, at: int) -> int:
    size = 0
    for i in range(4):
        size = (size << 7) | data[at + 6 + i]
    end = at + 10 + size
    if len(data) > at + 5 and (data[at + 5] & 0x10):
        end += 10                                   # footer
    return end


def frame_offsets(path: Path) -> list[tuple[int, float]]:
    """Every frame as (byte offset, start time in seconds).

    The offset is where the FRAME begins, which is what a decoder resumes from
    and therefore what the device reports back as its position.
    """
    data = path.read_bytes()
    pos = 0
    while _is_id3v2(data, pos):
        pos = _id3v2_end(data, pos)

    frames: list[tuple[int, float]] = []
    t = 0.0
    n = len(data)
    while pos + 4 <= n:
        if data[pos] != 0xFF or (data[pos + 1] & 0xE0) != 0xE0:
            pos += 1
            continue
        version = (data[pos + 1] >> 3) & 0x03
        layer = (data[pos + 1] >> 1) & 0x03
        if layer != 1 or version == 1:              # layer III only
            pos += 1
            continue
        table = _BITRATES_V1_L3 if version == 3 else _BITRATES_V2_L3
        bitrate = table[(data[pos + 2] >> 4) & 0x0F]
        rate_index = (data[pos + 2] >> 2) & 0x03
        if bitrate == 0 or rate_index == 3:
            pos += 1
            continue
        rate = _RATES[version][rate_index]
        padding = (data[pos + 2] >> 1) & 0x01
        samples = 1152 if version == 3 else 576
        length = int(samples // 8 * 1000 * bitrate / rate) + padding
        if length <= 4:
            pos += 1
            continue
        frames.append((pos, t))
        t += samples / rate
        pos += length
    return frames


def seconds_to_bytes(starts_seconds: list[float], frames: list[tuple[int, float]]) -> list[int]:
    """The byte offset of the first frame at or after each start time.

    At-or-after, not nearest: landing a fraction EARLY puts the child in the
    previous segment and answers about a scene they have finished, which is the
    error this map exists to remove.
    """
    out: list[int] = []
    i = 0
    for s in starts_seconds:
        while i + 1 < len(frames) and frames[i][1] < s:
            i += 1
        # Never go backwards, so the map stays ascending as the backend assumes.
        off = frames[i][0]
        if out and off < out[-1]:
            off = out[-1]
        out.append(off)
    if out:
        out[0] = frames[0][0] if frames else 0
    return out


def _self_test() -> int:
    ok = True

    def check(name: str, cond: bool) -> None:
        nonlocal ok
        print(("  ok   " if cond else "  FAIL ") + name)
        if not cond:
            ok = False

    # 10 frames, 0.1s each, 100 bytes apart, starting at byte 500.
    frames = [(500 + 100 * i, 0.1 * i) for i in range(10)]
    got = seconds_to_bytes([0.0, 0.25, 0.9], frames)
    check("first segment is the first frame", got[0] == 500)
    check("0.25s lands on the frame at or after it (0.3s)", got[1] == 800)
    check("0.9s lands exactly on its own frame", got[2] == 1400)
    check("ascending", got == sorted(got))

    got = seconds_to_bytes([0.0, 99.0], frames)
    check("a time past the end clamps to the last frame", got[1] == frames[-1][0])

    got = seconds_to_bytes([0.0, 0.5, 0.5], frames)
    check("duplicate times never go backwards", got[1] <= got[2])

    print("SELF-TEST PASS" if ok else "SELF-TEST FAILED")
    return 0 if ok else 1


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--seconds", type=Path, help="the map mix_ambience.py wrote")
    ap.add_argument("--mp3", type=Path, help="the finished MP3 the shipper installed")
    ap.add_argument("--out", type=Path, help="where to write the byte map")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()

    if a.self_test:
        return _self_test()
    if not (a.seconds and a.mp3 and a.out):
        ap.error("--seconds, --mp3 and --out are all required (or use --self-test)")

    doc = json.loads(a.seconds.read_text(encoding="utf-8"))
    if doc.get("unit") != "seconds":
        print("refusing: %s does not say unit=seconds" % a.seconds, file=sys.stderr)
        return 1
    starts = [float(x) for x in doc["starts"]]

    frames = frame_offsets(a.mp3)
    if not frames:
        print("refusing: no MP3 frames found in %s" % a.mp3, file=sys.stderr)
        return 1

    byte_map = seconds_to_bytes(starts, frames)
    # The backend deserializes a BARE ARRAY. Anything else is silently ignored
    # and it goes back to guessing - which is the bug this tool exists for.
    a.out.write_text(json.dumps(byte_map), encoding="utf-8")

    total = frames[-1][1]
    print("%d segments -> %d byte offsets" % (len(starts), len(byte_map)))
    for i, (s, b) in enumerate(zip(starts, byte_map)):
        print("  seg %-2d %7.2fs -> byte %9d" % (i, s, b))
    print("audio is %.1fs, %d frames, %d bytes" % (total, len(frames), a.mp3.stat().st_size))
    print("wrote %s" % a.out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

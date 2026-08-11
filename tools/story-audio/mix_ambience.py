#!/usr/bin/env python3
"""Put the forest into the story: mix ambience cues under narration.

WHERE THIS SITS
---------------
    narrator  ->  per-segment WAVs
                       |
                       v
              mix_ambience.py   <- you are here: concatenate, lay the cues in,
                       |            write the segment map
                       v
              <storyId>.wav  ->  Ship-StoryAudio.ps1 -Fix -Apply
                                 (levels to -16.4 LUFS, encodes, installs,
                                  bumps Version)  ->  human listen test

This tool deliberately does NOT level and does NOT encode to MP3. Loudness is a
library-wide contract owned by the shipper, and it must be measured on the
FINISHED mix — levelling narration and ambience separately would defeat it.

WHY PER-SEGMENT INPUT
---------------------
Segment boundaries are what the ambience cues are anchored to, and this project
has never had them: there are zero `.segments.json` files in the repo, so
`StoryQaController.OffsetToSegment` falls back to guessing a child's position
from `offset / fileSize`. Recording one file per segment gives exact boundaries
for free, so this tool emits `<storyId>.segments.json` as a by-product.

That map is in SECONDS, and the backend reads BYTES — it deserializes a bare
`long[]` of offsets into the finished MP3, which cannot be known until after the
encode. Run `segments_to_bytes.py` against the shipped file to convert it, or
the backend silently ignores the map and keeps guessing. See
`docs/voice-narrator-brief.md` §3.

A single whole-story file is still accepted (`--single`), but then cue times
must be estimated from character counts and the segment map is not written —
because a map that was guessed is worse than no map at all.

DRY RUN BY DEFAULT
------------------
Prints the resolved cue times and the exact ffmpeg command, and writes nothing.
Same two-man rule as `tools/ElevenLabsRender`. Pass `--render` to execute.

USAGE
    python3 tools/story-audio/mix_ambience.py --story ulik \
        --segments-dir <dir of ulik-01.wav ...> --sounds-dir <dir> --out <dir>
    ... --render            # actually run ffmpeg (needs ffmpeg on PATH)
    ... --self-test         # verify the timing maths with synthetic audio
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import wave
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
CUES_PATH = REPO / "backend/content/story-ambience/ambience-cues.json"
STORY_DIR = REPO / "backend/src/ArmenianAiToy.Application/Stories/Content"

# How far a "scene" cue drops once it stops establishing and the narration
# carries on over it. Only applies to cues with holdUnder: true.
HOLD_UNDER_DROP_DB = 8.0
FADE_IN_S = 0.5
FADE_OUT_S = 1.0
# A bed shorter than its own two fades plus a little is not a bed, it is a
# swell — and at exactly fade_in+fade_out the fade-out starts before the
# fade-in has finished, which reads as a click. Found by the self-test.
MIN_BED_S = FADE_IN_S + FADE_OUT_S + 0.5
# Two cues landing within this of each other arrive as one muddled event. The
# usual cause is a cue at the END of segment N and another at the START of
# N+1 — which are the same instant, a fact that is not obvious in the cue
# sheet. Found on ulik the first time this tool was run.
COLLISION_S = 2.0
# Only used by --single, where there are no real boundaries to measure.
CHARS_PER_SECOND = 15.0


# --------------------------------------------------------------------------
# Pure helpers — no ffmpeg, no IO beyond reading WAV headers. Testable.
# --------------------------------------------------------------------------

def wav_duration(path: Path) -> float:
    """Seconds, straight from the RIFF header. No decoder needed."""
    with wave.open(str(path), "rb") as w:
        return w.getnframes() / float(w.getframerate())


def segment_starts(durations: list[float]) -> list[float]:
    """Cumulative start time of each segment. Entry 0 is always 0.0."""
    starts, running = [], 0.0
    for d in durations:
        starts.append(running)
        running += d
    return starts


def resolve_cue_time(cue: dict, starts: list[float], durations: list[float]) -> float:
    """Absolute seconds for a cue anchored to a segment index and start/end."""
    i = cue["segment"]
    if cue.get("at", "start") == "end":
        # "end" means the boundary, i.e. where the next segment begins - a cue
        # placed at the end of a segment should land on that transition, not a
        # second before it.
        return starts[i] + durations[i]
    return starts[i]


def build_chains(cues: list[dict], starts: list[float], durations: list[float],
                 sound_index: dict[str, Path]) -> list[dict]:
    """One entry per audio stream to lay under the narration.

    A holdUnder scene becomes TWO chains — the establish, then a quieter bed to
    the end of its segment. Two explicit chains beat one clever volume envelope:
    the times are readable in the dry run, which is where mistakes get caught.
    """
    chains = []
    for cue in cues:
        at = resolve_cue_time(cue, starts, durations)
        seconds = float(cue["seconds"])
        level = float(cue["level"])
        src = sound_index[cue["sound"]]
        chains.append({
            "sound": cue["sound"], "src": src, "start": at,
            "duration": seconds, "level": level,
            "kind": cue["kind"], "label": f"{cue['sound']}@seg{cue['segment']}",
        })
        if cue.get("holdUnder"):
            seg_end = starts[cue["segment"]] + durations[cue["segment"]]
            bed_start = at + seconds
            bed_len = seg_end - bed_start
            if bed_len >= MIN_BED_S:
                chains.append({
                    "sound": cue["sound"], "src": src, "start": bed_start,
                    "duration": bed_len, "level": level - HOLD_UNDER_DROP_DB,
                    "kind": "bed", "label": f"{cue['sound']}@seg{cue['segment']}~bed",
                })
    return chains


def build_filtergraph(n_segments: int, chains: list[dict]) -> str:
    """The ffmpeg filter_complex. Inputs 0..n-1 are the narration segments;
    each chain gets the next input index in order."""
    parts = []
    narration = "".join(f"[{i}:a]" for i in range(n_segments))
    parts.append(f"{narration}concat=n={n_segments}:v=0:a=1[narr]")

    labels = []
    for k, c in enumerate(chains):
        idx = n_segments + k
        out = f"c{k}"
        labels.append(f"[{out}]")
        fade_out_at = max(0.0, c["duration"] - FADE_OUT_S)
        delay_ms = int(round(c["start"] * 1000))
        parts.append(
            f"[{idx}:a]aloop=loop=-1:size=2e9,"          # sounds may be short
            f"atrim=0:{c['duration']:.3f},"
            f"volume={c['level']:.1f}dB,"
            f"afade=t=in:st=0:d={FADE_IN_S},"
            f"afade=t=out:st={fade_out_at:.3f}:d={FADE_OUT_S},"
            f"adelay={delay_ms}|{delay_ms},"
            f"asetpts=PTS-STARTPTS[{out}]"
        )

    # normalize=0 is load-bearing: amix's default rescales every input by 1/N,
    # which would quietly pull the narration down as cues are added.
    parts.append(f"[narr]{''.join(labels)}"
                 f"amix=inputs={1 + len(chains)}:duration=first:normalize=0[out]")
    return ";".join(parts)


def collisions(chains: list[dict]) -> list[str]:
    """Warn where two cues (beds excepted) start almost together."""
    heard = [c for c in chains if c["kind"] != "bed"]
    out = []
    for a, b in zip(sorted(heard, key=lambda c: c["start"]),
                    sorted(heard, key=lambda c: c["start"])[1:]):
        gap = b["start"] - a["start"]
        if gap < COLLISION_S:
            out.append(f"{a['label']} and {b['label']} are {gap:.2f}s apart "
                       f"at {mmss(a['start'])} — they will land as one event")
    return out


def validate(cues: list[dict], n_segments: int, sound_index: dict[str, Path],
             total: float) -> list[str]:
    problems = []
    for cue in cues:
        where = f"segment {cue.get('segment')} / {cue.get('sound')}"
        if not (0 <= cue.get("segment", -1) < n_segments):
            problems.append(f"{where}: segment out of range (0..{n_segments - 1})")
        if cue.get("sound") not in sound_index:
            problems.append(f"{where}: no audio file for sound id '{cue.get('sound')}'")
        if float(cue.get("level", 0)) >= 0:
            problems.append(f"{where}: level must be negative (narration stays loudest)")
        if float(cue.get("seconds", 0)) <= 0:
            problems.append(f"{where}: seconds must be positive")
    return problems


# --------------------------------------------------------------------------
# IO
# --------------------------------------------------------------------------

def load_cues(story_id: str) -> list[dict]:
    doc = json.loads(CUES_PATH.read_text(encoding="utf-8"))
    for story in doc["stories"]:
        if story["storyId"] == story_id:
            return story["cues"]
    raise SystemExit(f"no cues for story '{story_id}' in {CUES_PATH}")


def index_sounds(sounds_dir: Path) -> dict[str, Path]:
    """sound id -> file, by stem. Any extension ffmpeg can read."""
    out = {}
    for f in sorted(sounds_dir.iterdir()) if sounds_dir.is_dir() else []:
        if f.is_file() and not f.name.startswith("."):
            out.setdefault(f.stem, f)
    return out


def segment_files(segments_dir: Path, story_id: str) -> list[Path]:
    files = sorted(segments_dir.glob(f"{story_id}-*.wav"))
    if not files:
        raise SystemExit(
            f"no {story_id}-NN.wav files in {segments_dir}.\n"
            f"The studio should deliver one WAV per story segment — see\n"
            f"docs/voice-narrator-brief.md §3 for why."
        )
    return files


def mmss(s: float) -> str:
    return f"{int(s // 60)}:{s % 60:05.2f}"


def run(story_id: str, segments_dir: Path, sounds_dir: Path, out_dir: Path,
        render: bool) -> int:
    cues = load_cues(story_id)
    segs = segment_files(segments_dir, story_id)
    durations = [wav_duration(f) for f in segs]
    starts = segment_starts(durations)
    total = sum(durations)
    sounds = index_sounds(sounds_dir)

    story_json = STORY_DIR / f"{story_id}.story.json"
    if story_json.exists():
        n_text = len(json.loads(story_json.read_text(encoding="utf-8"))["segments"])
        if n_text != len(segs):
            print(f"WARNING: {len(segs)} WAV files but the story has {n_text} "
                  f"segments. Cue anchors will be wrong.", file=sys.stderr)

    problems = validate(cues, len(segs), sounds, total)
    if problems:
        print(f"{story_id}: cannot mix —", file=sys.stderr)
        for p in problems:
            print(f"  - {p}", file=sys.stderr)
        return 1

    chains = build_chains(cues, starts, durations, sounds)

    print(f"{story_id}  {len(segs)} segments, {mmss(total)} total")
    print(f"{'seg':>4} {'starts at':>10}  {'length':>8}")
    for i, (s, d) in enumerate(zip(starts, durations)):
        print(f"{i:>4} {mmss(s):>10}  {d:>7.2f}s")
    print()
    print(f"{'at':>10} {'for':>7} {'level':>7}  sound")
    for c in sorted(chains, key=lambda x: x["start"]):
        print(f"{mmss(c['start']):>10} {c['duration']:>6.2f}s "
              f"{c['level']:>6.1f}dB  {c['label']}")

    for w in collisions(chains):
        print(f"WARNING: {w}", file=sys.stderr)

    out_dir.mkdir(parents=True, exist_ok=True)
    mixed = out_dir / f"{story_id}.wav"
    seg_map = out_dir / f"{story_id}.segments.json"

    cmd = ["ffmpeg", "-hide_banner", "-v", "error", "-y"]
    for f in segs:
        cmd += ["-i", str(f)]
    for c in chains:
        cmd += ["-i", str(c["src"])]
    cmd += ["-filter_complex", build_filtergraph(len(segs), chains),
            "-map", "[out]", "-ar", "44100", "-ac", "1",
            "-c:a", "pcm_s16le", str(mixed)]

    print()
    print("ffmpeg command:")
    print("  " + " ".join(f'"{a}"' if " " in a else a for a in cmd))

    if not render:
        print()
        print("DRY RUN — nothing written. Pass --render to execute.")
        return 0

    if shutil.which("ffmpeg") is None:
        print("ffmpeg not found on PATH.", file=sys.stderr)
        return 1

    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        print(result.stderr[:2000], file=sys.stderr)
        return 1

    # The map is byte offsets into the FINAL mp3, which does not exist yet, so
    # write seconds and let the shipper's encode settle bytes. Seconds are the
    # honest unit here; a byte map guessed from a WAV would be wrong after
    # encoding.
    seg_map.write_text(json.dumps(
        {"storyId": story_id, "unit": "seconds", "starts": [round(s, 3) for s in starts],
         "durations": [round(d, 3) for d in durations]},
        indent=2), encoding="utf-8")

    print()
    print(f"wrote {mixed}")
    print(f"wrote {seg_map}")
    print("Next: Ship-StoryAudio.ps1 -In <dir> -Fix -Apply  (levels, encodes,")
    print("installs, bumps Version). Then listen to it end to end.")
    return 0


# --------------------------------------------------------------------------
# Self-test — proves the timing maths without ffmpeg and without real audio.
# --------------------------------------------------------------------------

def self_test() -> int:
    import tempfile

    ok = True

    def check(name, got, want):
        nonlocal ok
        if got != want:
            ok = False
            print(f"  FAIL {name}: got {got!r}, want {want!r}")
        else:
            print(f"  ok   {name}")

    check("segment_starts", segment_starts([10.0, 8.0, 2.5]), [0.0, 10.0, 18.0])
    check("segment_starts empty", segment_starts([]), [])

    durations = [10.0, 8.0, 2.5]
    starts = segment_starts(durations)
    check("cue at segment start",
          resolve_cue_time({"segment": 1, "at": "start"}, starts, durations), 10.0)
    check("cue at segment end",
          resolve_cue_time({"segment": 1, "at": "end"}, starts, durations), 18.0)

    sounds = {"river": Path("/tmp/river.wav")}
    chains = build_chains(
        [{"segment": 1, "at": "start", "kind": "scene", "sound": "river",
          "seconds": 4, "level": -20, "holdUnder": True}],
        starts, durations, sounds)
    check("holdUnder makes two chains", len(chains), 2)
    check("establish starts at segment", chains[0]["start"], 10.0)
    check("bed starts after establish", chains[1]["start"], 14.0)
    check("bed runs to segment end", round(chains[1]["duration"], 3), 4.0)
    check("bed is quieter", chains[1]["level"], -28.0)

    # Segment 2 is 2.5 s, so a 2 s establish leaves 0.5 s — shorter than the
    # fades that would be applied to it. The bed must be dropped, not clicked.
    short = build_chains(
        [{"segment": 2, "at": "start", "kind": "scene", "sound": "river",
          "seconds": 2, "level": -20, "holdUnder": True}],
        starts, durations, sounds)
    check("bed too short to be a bed is dropped", len(short), 1)

    graph = build_filtergraph(3, chains)
    check("concat covers every segment", "[0:a][1:a][2:a]concat=n=3" in graph, True)
    check("amix does not renormalize", "normalize=0" in graph, True)
    check("amix counts narration + chains", "amix=inputs=3" in graph, True)
    check("cue delayed to its time", "adelay=10000|10000" in graph, True)

    clash = build_chains(
        [{"segment": 0, "at": "end", "kind": "scene", "sound": "river",
          "seconds": 3, "level": -20},
         {"segment": 1, "at": "start", "kind": "oneshot", "sound": "river",
          "seconds": 2, "level": -18}],
        starts, durations, sounds)
    check("end-of-N and start-of-N+1 collide", len(collisions(clash)), 1)
    check("no false collision when spaced", collisions(chains), [])

    bad = validate([{"segment": 9, "sound": "nope", "level": 3, "seconds": 0}],
                   3, sounds, 20.5)
    check("validate catches all four faults", len(bad), 4)

    # End to end on synthetic WAVs: durations must be read from real headers.
    with tempfile.TemporaryDirectory() as td:
        d = Path(td)
        for i, secs in enumerate([1.0, 0.5], start=1):
            with wave.open(str(d / f"demo-{i:02d}.wav"), "wb") as w:
                w.setnchannels(1); w.setsampwidth(2); w.setframerate(44100)
                w.writeframes(b"\x00\x00" * int(44100 * secs))
        got = [round(wav_duration(f), 3) for f in segment_files(d, "demo")]
        check("wav_duration from header", got, [1.0, 0.5])

    print("\nSELF-TEST " + ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--story")
    ap.add_argument("--segments-dir", type=Path)
    ap.add_argument("--sounds-dir", type=Path)
    ap.add_argument("--out", type=Path, default=Path("mixed"))
    ap.add_argument("--render", action="store_true",
                    help="actually run ffmpeg (default is a dry run)")
    ap.add_argument("--self-test", action="store_true",
                    help="verify the timing maths; needs no audio and no ffmpeg")
    a = ap.parse_args()

    if a.self_test:
        return self_test()
    if not (a.story and a.segments_dir and a.sounds_dir):
        ap.error("--story, --segments-dir and --sounds-dir are required")
    return run(a.story, a.segments_dir, a.sounds_dir, a.out, a.render)


if __name__ == "__main__":
    sys.exit(main())

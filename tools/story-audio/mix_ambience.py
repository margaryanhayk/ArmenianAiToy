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

TWO WAYS IN, AND THE SECOND IS NOW THE USUAL ONE
------------------------------------------------
`--segments-dir` is the studio path: one WAV per segment, boundaries measured
directly. It is what `docs/voice-narrator-brief.md` §3 asks a human narrator to
deliver.

`--narration <story.mp3> --map <story.segments.json>` is the path for audio
that has ALREADY shipped. It reads the installed story and its committed BYTE
map and walks the MP3 frames to turn those offsets back into seconds — the
exact inverse of `segments_to_bytes.py`, whose frame walker it reuses. Prefer
it whenever the story is already in `story-audio/`: the shipped file is
192 kbps where an intermediate render is usually 128, the segment starts are
the committed ones rather than re-measured, and — the reason it exists — the
per-segment renders live in a scratch directory that does not survive the
afternoon, while the map is in git.

A single whole-story file with no map is still accepted (`--single`), but then
cue times are estimated from character counts and no segment map is written,
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
# A one-shot is an EVENT, and half a second of fade-in swallows it. Measured on
# the first generated knock for «Ուլիկը»: the two strikes peak at 0.1s and 0.4s
# and everything after 0.9s is silence, so the bed's 0.5s ramp would have taken
# the wolf's knock down to a fifth of its volume — the one sound in that story
# that has to land. A one-shot gets just enough ramp to kill the click at the
# splice, and its tail is left alone.
ONESHOT_FADE_IN_S = 0.01
ONESHOT_FADE_OUT_S = 0.15
# A bed shorter than its own two fades plus a little is not a bed, it is a
# swell — and at exactly fade_in+fade_out the fade-out starts before the
# fade-in has finished, which reads as a click. Found by the self-test.
MIN_BED_S = FADE_IN_S + FADE_OUT_S + 0.5   # beds only; one-shots are events
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
        one = c["kind"] == "oneshot"
        fade_in = ONESHOT_FADE_IN_S if one else FADE_IN_S
        fade_out = ONESHOT_FADE_OUT_S if one else FADE_OUT_S
        fade_out_at = max(0.0, c["duration"] - fade_out)
        delay_ms = int(round(c["start"] * 1000))
        parts.append(
            f"[{idx}:a]aloop=loop=-1:size=2e9,"          # sounds may be short
            f"atrim=0:{c['duration']:.3f},"
            f"volume={c['level']:.1f}dB,"
            f"afade=t=in:st=0:d={fade_in},"
            f"afade=t=out:st={fade_out_at:.3f}:d={fade_out},"
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


def segments_from_shipped(narration: Path, map_path: Path) -> list[float]:
    """Segment durations, recovered from a shipped MP3 and its byte map.

    The backend stores a bare array of BYTE offsets because that is what it can
    seek to. Byte -> second is a frame walk, and `segments_to_bytes.py` already
    owns that walk; importing it keeps one implementation of the MP3 frame
    tables rather than a second copy that could drift from it.
    """
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from segments_to_bytes import frame_offsets  # noqa: E402

    offsets = json.loads(map_path.read_text(encoding="utf-8"))
    if not isinstance(offsets, list) or not offsets:
        raise SystemExit(f"{map_path} is not a byte map — expected a JSON array "
                         f"of offsets, the shape StoryQaController reads.")
    frames = frame_offsets(narration)
    if not frames:
        raise SystemExit(f"no MPEG frames found in {narration}")
    total = frames[-1][1]

    starts: list[float] = []
    i = 0
    for off in offsets:
        while i + 1 < len(frames) and frames[i][0] < off:
            i += 1
        starts.append(frames[i][1])
    # The first segment starts when the audio does, whatever byte the map says
    # the first frame sits at (45 in this library, past the single ID3 tag).
    if starts:
        starts[0] = 0.0
    return [b - a for a, b in zip(starts, starts[1:] + [total])]


def mmss(s: float) -> str:
    return f"{int(s // 60)}:{s % 60:05.2f}"


def run(story_id: str, segments_dir: Path | None, sounds_dir: Path, out_dir: Path,
        render: bool, narration: Path | None = None,
        map_path: Path | None = None) -> int:
    cues = load_cues(story_id)
    if narration is not None:
        segs = [narration]
        durations = segments_from_shipped(narration, map_path)
    else:
        segs = segment_files(segments_dir, story_id)
        durations = [wav_duration(f) for f in segs]
    starts = segment_starts(durations)
    total = sum(durations)
    sounds = index_sounds(sounds_dir)

    story_json = STORY_DIR / f"{story_id}.story.json"
    if story_json.exists():
        n_text = len(json.loads(story_json.read_text(encoding="utf-8"))["segments"])
        if n_text != len(durations):
            print(f"WARNING: {len(durations)} segment(s) of audio but the story "
                  f"has {n_text}. Cue anchors will be wrong.", file=sys.stderr)

    problems = validate(cues, len(durations), sounds, total)
    if problems:
        print(f"{story_id}: cannot mix —", file=sys.stderr)
        for p in problems:
            print(f"  - {p}", file=sys.stderr)
        return 1

    chains = build_chains(cues, starts, durations, sounds)

    src_note = (f"from {narration.name} + {map_path.name}" if narration
                else f"from {len(segs)} WAV file(s)")
    print(f"{story_id}  {len(durations)} segments, {mmss(total)} total  ({src_note})")
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
    # concat=n=1 is valid, so the shipped-MP3 path passes straight through the
    # same graph with no special case.
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
    # A one-shot must not be ramped like a bed.
    one_graph = build_filtergraph(1, [
        {"src": Path("k.mp3"), "start": 3.0, "duration": 2.0, "level": -18.0,
         "kind": "oneshot", "label": "knock"}])
    check("one-shot fades in almost instantly",
          f"afade=t=in:st=0:d={ONESHOT_FADE_IN_S}" in one_graph, True)
    check("one-shot is not given a bed's ramp",
          f"afade=t=in:st=0:d={FADE_IN_S}" in one_graph, False)
    bed_graph = build_filtergraph(1, [
        {"src": Path("f.mp3"), "start": 0.0, "duration": 9.0, "level": -28.0,
         "kind": "bed", "label": "forest~bed"}])
    check("a bed keeps its slow ramp",
          f"afade=t=in:st=0:d={FADE_IN_S}" in bed_graph, True)

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

    # The shipped-MP3 path, against a real committed story. No ffmpeg, no
    # network: the frame walk is pure Python and the map is in git. The
    # invariant is a ROUND TRIP — bytes to seconds and back must land on the
    # same offsets, because a half-frame slip here would move every cue in
    # every story and would be invisible until someone listened.
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from segments_to_bytes import frame_offsets, seconds_to_bytes  # noqa: E402

    mp3 = REPO / "backend/src/ArmenianAiToy.Api/story-audio/ulik.mp3"
    mp = mp3.with_suffix(".segments.json")
    if mp3.exists() and mp.exists():
        want = json.loads(mp.read_text(encoding="utf-8"))
        durs = segments_from_shipped(mp3, mp)
        starts = segment_starts(durs)
        check("shipped map yields one duration per segment", len(durs), len(want))
        check("first segment starts at zero", starts[0], 0.0)
        check("durations are all positive", all(d > 0 for d in durs), True)
        back = seconds_to_bytes(starts, frame_offsets(mp3))
        check("bytes -> seconds -> bytes round-trips", back, want)
    else:
        print("  skip shipped-map checks — ulik.mp3 not in the tree")

    print("\nSELF-TEST " + ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--story")
    ap.add_argument("--segments-dir", type=Path,
                    help="directory of <storyId>-NN.wav (the studio path)")
    ap.add_argument("--narration", type=Path,
                    help="a shipped <storyId>.mp3 to mix under")
    ap.add_argument("--map", dest="map_path", type=Path,
                    help="its committed <storyId>.segments.json (byte offsets)")
    ap.add_argument("--sounds-dir", type=Path)
    ap.add_argument("--out", type=Path, default=Path("mixed"))
    ap.add_argument("--render", action="store_true",
                    help="actually run ffmpeg (default is a dry run)")
    ap.add_argument("--self-test", action="store_true",
                    help="verify the timing maths; needs no audio and no ffmpeg")
    a = ap.parse_args()

    if a.self_test:
        return self_test()
    if not (a.story and a.sounds_dir):
        ap.error("--story and --sounds-dir are required")
    if bool(a.narration) != bool(a.map_path):
        ap.error("--narration and --map go together: mixing a shipped story "
                 "without its committed byte map would guess the cue times.")
    if not (a.segments_dir or a.narration):
        ap.error("give either --segments-dir, or --narration with --map")
    if a.segments_dir and a.narration:
        ap.error("--segments-dir and --narration are two ways in; pick one")
    return run(a.story, a.segments_dir, a.sounds_dir, a.out, a.render,
               a.narration, a.map_path)


if __name__ == "__main__":
    sys.exit(main())

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

# `at: "line"` — land a cue where its cueLine ENDS instead of where the segment
# starts. The owner heard why this was needed: «Ուլիկը»'s wolf knocked 5.6s
# before the narrator said anyone came to the door, because a 16-second segment
# has only one anchor and the knock took it.
#
# The gaps the renderer stitches between spans, so a proportional estimate can
# subtract silence it should not be charging to the words. These MUST match
# tools/story-voices/render_story.py; if that file's pauses change, change
# these, or every line anchor drifts by the difference.
SPAN_PAUSE_SPEAKER = 0.40
SPAN_PAUSE_SENTENCE = 0.34
SPAN_PAUSE_CLAUSE = 0.16
# How far a line anchor may move to find a real pause in the narration. Wider
# than this and it is no longer snapping to the boundary it estimated, it is
# choosing a different one.
SNAP_WINDOW_S = 1.5
# `insert: true` — cut the narration open and put the sound in the hole, rather
# than laying it over whatever gap the speech happens to leave. The owner's
# idea, after hearing the knock land wrong twice: "we can pause, we can play
# knocking, and then continue". It removes the whole class of error, because a
# small placement mistake inside a silence still sounds deliberate.
INSERT_LEAD_S = 0.25
INSERT_TAIL_S = 0.35
SILENCE_FLOOR_DB = -35     # MP3 + loudnorm lift the noise floor; -42 finds none
SILENCE_MIN_S = 0.20
VOICES_DIR = REPO / "backend/content/story-voices"


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


def span_boundary_estimate(spans: list[int], k: int, seg_duration: float,
                           gaps: list[float]) -> float:
    """Seconds into a segment where span k-1 STOPS — the start of the gap.

    Not where span k begins, which is a fifth of a second later. The difference
    is the whole point: a knock wants to land in the pause, so that the child
    hears the narrator say the door was struck, then the strike, then the voice.
    Landing it on the wolf's first syllable would be late in a way that sounds
    like a mistake, the same way landing it at the segment start sounded early.

    Speech is not uniform, so this is an estimate — but it is an estimate of
    the WORDS only: the stitched pauses are subtracted first and added back
    where they belong, instead of being smeared across every span in
    proportion to its length.
    """
    speech = max(0.0, seg_duration - sum(gaps[:len(spans) - 1]))
    total_chars = sum(spans) or 1
    return (speech * sum(spans[:k]) / total_chars) + sum(gaps[:max(0, k - 1)])


def shift_for(t: float, insertions: list[tuple[float, float]]) -> float:
    """Where a moment in the ORIGINAL narration lands after insertions.

    Every cue, every bed and every segment start has to travel through this.
    Missing one is how a file desynchronises silently: the audio would be right
    and the segment map would describe a story that no longer exists, and the
    only symptom is an in-story question answered about the wrong scene.
    """
    return t + sum(g for cut, g in insertions if cut <= t)


def apply_insertions(chains: list[dict], starts: list[float],
                     insertions: list[tuple[float, float]]) -> tuple[list[dict], list[float]]:
    """Move everything onto the post-insertion timeline.

    A chain's END is shifted too, not just its start — a bed that spans a cut
    must grow by the gap, or it stops short of the segment it was meant to
    cover.
    """
    moved = []
    for c in chains:
        a = shift_for(c["start"], insertions)
        b = shift_for(c["start"] + c["duration"], insertions)
        moved.append({**c, "start": a, "duration": b - a})
    return moved, [shift_for(s, insertions) for s in starts]


def snap_to_pause(estimate: float, silences: list[tuple[float, float]],
                  window: float = SNAP_WINDOW_S) -> float | None:
    """The start of the pause nearest the estimate, or None if none is near.

    NEAREST, not longest. On «Ուլիկը» the longest pause within range of the
    wolf's boundary was the model taking a breath in the middle of the
    narrator's own sentence — 0.79s against the 0.31s of the real speaker
    change. Picking the longest would have put the knock mid-sentence and
    looked deliberate while doing it.
    """
    near = [(abs(a - estimate), a) for a, _b in silences
            if abs(a - estimate) <= window]
    return min(near)[1] if near else None


def resolve_cue_time(cue: dict, starts: list[float], durations: list[float],
                     lines: dict | None = None) -> float:
    """Absolute seconds for a cue anchored to a segment index and start/end/line.

    `lines` maps segment index -> seconds into that segment for an `at: "line"`
    cue, worked out once by the caller (which is where the story text, the
    speaker map and the narration audio all live). Passing it in keeps this
    function pure and testable.
    """
    i = cue["segment"]
    at = cue.get("at", "start")
    if at == "end":
        # "end" means the boundary, i.e. where the next segment begins - a cue
        # placed at the end of a segment should land on that transition, not a
        # second before it.
        return starts[i] + durations[i]
    if at == "line":
        if lines is None or i not in lines:
            raise SystemExit(
                f"segment {i} / {cue.get('sound')}: at=\"line\" but no line "
                f"position was resolved. Falling back to the segment start "
                f"would put the cue exactly where it was wrong before.")
        return starts[i] + lines[i]
    return starts[i]


def build_chains(cues: list[dict], starts: list[float], durations: list[float],
                 sound_index: dict[str, Path],
                 lines: dict | None = None) -> list[dict]:
    """One entry per audio stream to lay under the narration.

    A holdUnder scene becomes TWO chains — the establish, then a quieter bed to
    the end of its segment. Two explicit chains beat one clever volume envelope:
    the times are readable in the dry run, which is where mistakes get caught.
    """
    chains = []
    for ci, cue in enumerate(cues):
        at = resolve_cue_time(cue, starts, durations, lines)
        seconds = float(cue["seconds"])
        level = float(cue["level"])
        src = sound_index[cue["sound"]]
        # `cue` is the index back into the cue list. A holdUnder scene adds a
        # SECOND chain, so chain order is not cue order — pairing them by
        # position put the wolf's knock on the evening forest and opened a
        # ten-second hole for it.
        chains.append({
            "sound": cue["sound"], "src": src, "start": at, "cue": ci,
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
                    "cue": ci, "kind": "bed",
                    "label": f"{cue['sound']}@seg{cue['segment']}~bed",
                })
    return chains


def build_filtergraph(n_segments: int, chains: list[dict],
                     insertions: list[tuple[float, float]] | None = None) -> str:
    """The ffmpeg filter_complex. Inputs 0..n-1 are the narration segments;
    each chain gets the next input index in order.

    With insertions, the narration is first cut at each point and silence
    concatenated in, so a one-shot plays into a hole instead of over speech.
    The cut times are in the ORIGINAL narration; every chain start passed in
    must already be on the post-insertion timeline (see apply_insertions).
    """
    parts = []
    narration = "".join(f"[{i}:a]" for i in range(n_segments))
    # A filter_complex label may be produced exactly once, so the joined
    # narration is [narr0] and only the LAST stage claims [narr].
    parts.append(f"{narration}concat=n={n_segments}:v=0:a=1"
                 f"{'[narr0]' if insertions else '[narr]'}")

    if insertions:
        cuts = sorted(insertions)
        n = len(cuts) + 1
        parts.append(f"[narr0]asplit={n}" + "".join(f"[q{i}]" for i in range(n)))
        pieces = []
        for i in range(n):
            a = 0.0 if i == 0 else cuts[i - 1][0]
            trim = (f"atrim={a:.3f}" if i == n - 1
                    else f"atrim={a:.3f}:{cuts[i][0]:.3f}")
            parts.append(f"[q{i}]{trim},asetpts=PTS-STARTPTS[n{i}]")
            pieces.append(f"[n{i}]")
            if i < len(cuts):
                # aevalsrc is a source filter, so the hole needs no extra input
                # file and no temporary silence on disk.
                parts.append(f"aevalsrc=0:d={cuts[i][1]:.3f}:s=44100[g{i}]")
                pieces.append(f"[g{i}]")
        parts.append(f"{''.join(pieces)}concat=n={len(pieces)}:v=0:a=1[narr]")

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
        if cue.get("at", "start") not in ("start", "end", "line"):
            problems.append(f"{where}: at must be start, end or line")
        if cue.get("at") == "line" and not cue.get("cueLine", "").strip():
            problems.append(f"{where}: at=line needs a cueLine to land on")
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


def ambience_marker(narration: Path) -> Path:
    return narration.with_suffix(".ambience.json")


def refuse_if_already_mixed(narration: Path | None, force: bool) -> None:
    """A mixed story mixed again gets a second forest laid over the first.

    Nothing in the audio says whether ambience is already in it, so the mixer
    leaves a marker beside the file it wrote. «Ուլիկը» was shipped with
    ambience within an hour of the mixer existing, and the very next run would
    have doubled it silently.

    The way back is the narration-only master, which is in git: every mix is
    committed, so `git show <commit-before-the-mix>:<path>` returns the exact
    approved bytes.
    """
    if narration is None or force:
        return
    m = ambience_marker(narration)
    if not m.exists():
        return
    doc = json.loads(m.read_text(encoding="utf-8"))
    raise SystemExit(
        f"{narration.name} already carries ambience "
        f"({', '.join(doc.get('sounds', [])) or 'unknown sounds'}, mixed "
        f"{doc.get('mixedFrom', 'unknown')}). Mixing it again would lay a "
        f"second one on top.\n"
        f"Point --narration at the narration-only master instead:\n"
        f"  git log --oneline -- {narration}\n"
        f"  git show <commit-before-the-mix>:{narration} > /tmp/clean.mp3\n"
        f"Or pass --force if you really mean to mix what is already mixed.")


def detect_silences(path: Path) -> list[tuple[float, float]]:
    """Pauses in the narration, via ffmpeg. Empty list if ffmpeg is absent.

    The floor is -35dB, not something stricter: this audio has been through
    MP3 and two-pass loudnorm, and at -42dB it reports no silence at all in a
    file that is plainly full of pauses.
    """
    if shutil.which("ffmpeg") is None:
        return []
    r = subprocess.run(
        ["ffmpeg", "-v", "info", "-i", str(path), "-af",
         f"silencedetect=noise={SILENCE_FLOOR_DB}dB:d={SILENCE_MIN_S}",
         "-f", "null", "-"], capture_output=True, text=True)
    out, start = [], None
    for line in r.stderr.splitlines():
        if "silence_start:" in line:
            start = float(line.split("silence_start:")[1].split()[0])
        elif "silence_end:" in line and start is not None:
            out.append((start, float(line.split("silence_end:")[1].split()[0])))
            start = None
    return out


def sound_content_length(path: Path) -> float:
    """How long the sound actually SOUNDS, ignoring silence at the end.

    The hole is sized from this, not from the cue's `seconds`. The generated
    knock is a 4s file with 0.9s of strikes in it; opening a 2.6s hole for it
    would be a pause with 1.7s of nothing in the middle.
    """
    total = 0.0
    try:
        out = subprocess.run(["ffprobe", "-v", "error", "-show_entries",
                              "format=duration", "-of", "csv=p=0", str(path)],
                             capture_output=True, text=True).stdout.strip()
        total = float(out) if out else 0.0
    except Exception:
        return 0.0
    sil = detect_silences(path)
    # Trailing silence only: a gap in the middle of three knocks is part of the
    # sound and must not be trimmed away.
    for a, b in sil:
        if b >= total - 0.05:
            return max(0.2, a)
    return total


def load_span_chars(story_id: str) -> dict[int, list[tuple[str, int]]]:
    """(speaker, character count) per span, per segment, from the speaker map."""
    p = VOICES_DIR / f"{story_id}.voices.json"
    if not p.exists():
        return {}
    doc = json.loads(p.read_text(encoding="utf-8"))
    return {seg["index"]: [(sp["speaker"], len(sp["text"].strip()))
                           for sp in seg["spans"]]
            for seg in doc["segments"]}


def load_span_times(story_id: str, audio_dir: Path) -> dict[int, list[float]]:
    """Measured span starts, if the render left a map. Relative to the segment.

    When this exists nothing below has to be estimated. It does not exist for
    the current library because the renderer's per-span files were not
    namespaced by story and overwrote each other; it will for anything rendered
    after that fix.
    """
    p = audio_dir / f"{story_id}.spans.json"
    if not p.exists():
        return {}
    doc = json.loads(p.read_text(encoding="utf-8"))
    return {i: [(sp["start"], sp["duration"]) for sp in seg]
            for i, seg in enumerate(doc.get("segments", []))}


def resolve_line_positions(story_id: str, cues: list[dict], durations: list[float],
                           starts: list[float], narration: Path | None,
                           audio_dir: Path) -> tuple[dict[int, float], list[str]]:
    """Seconds into each segment where an `at: "line"` cue belongs.

    Order of preference, best first:
      1. a measured span map from the render;
      2. a character-proportional estimate of the span boundary, snapped to a
         real pause in the narration;
      3. the estimate alone, said out loud.
    """
    notes: list[str] = []
    wanted = sorted({c["segment"] for c in cues if c.get("at") == "line"})
    if not wanted:
        return {}, notes

    spans = load_span_chars(story_id)
    measured = load_span_times(story_id, audio_dir)
    silences = detect_silences(narration) if narration else []
    if wanted and not silences and narration:
        notes.append("no pauses detected in the narration — line anchors are "
                     "estimates only")

    story_json = STORY_DIR / f"{story_id}.story.json"
    texts = json.loads(story_json.read_text(encoding="utf-8"))["segments"] \
        if story_json.exists() else []

    out: dict[int, float] = {}
    for cue in cues:
        if cue.get("at") != "line":
            continue
        i = cue["segment"]
        if i in out:
            continue
        line = cue.get("cueLine", "").strip()
        if i >= len(texts) or not line:
            raise SystemExit(f"segment {i}: at=line but the cueLine is not in "
                             f"the story text — nothing to anchor to.")
        text = texts[i]
        pos = text.find(line[:40])
        if pos < 0:
            raise SystemExit(f"segment {i}: cueLine {line[:40]!r} does not "
                             f"appear in the story text.")
        end_char = pos + len(line)

        # Which span boundary does the line end at? The nearest one, so a
        # cueLine that is a whole span resolves exactly and one that stops
        # mid-span resolves to the boundary it is closest to.
        seg_spans = spans.get(i, [])
        if not seg_spans:
            raise SystemExit(f"segment {i}: no speaker map for {story_id}; "
                             f"at=line needs one to find the boundary.")
        chars = [c for _who, c in seg_spans]
        cum, k, best = 0, 0, None
        for j, c in enumerate(chars):
            cum += c
            d = abs(cum - end_char)
            if best is None or d < best:
                best, k = d, j + 1

        if i in measured and 0 < k <= len(measured[i]):
            # The END of the span the line finishes, not the start of the next.
            start, dur = measured[i][k - 1]
            out[i] = start + dur
            notes.append(f"seg {i}: line anchor {out[i]:.2f}s (measured span map)")
            continue

        gaps = [SPAN_PAUSE_SPEAKER if seg_spans[j][0] != seg_spans[j + 1][0]
                else SPAN_PAUSE_SENTENCE
                for j in range(len(seg_spans) - 1)]
        est = span_boundary_estimate(chars, k, durations[i], gaps)
        snapped = snap_to_pause(est + starts[i], silences)
        if snapped is None:
            out[i] = est
            notes.append(f"seg {i}: line anchor {est:.2f}s (estimate; no pause "
                         f"within {SNAP_WINDOW_S}s)")
        else:
            out[i] = snapped - starts[i]
            notes.append(f"seg {i}: line anchor {out[i]:.2f}s "
                         f"(estimate {est:.2f}s, snapped {out[i] - est:+.2f}s "
                         f"to a pause)")
    return out, notes


def mmss(s: float) -> str:
    return f"{int(s // 60)}:{s % 60:05.2f}"


def partition_held(cues: list[dict]) -> tuple[list[dict], list[str]]:
    """Split off cues that are deliberately not ready to be mixed.

    A cue with `held` is one whose POSITION is not yet known — four one-shots
    in the library have notes demanding they land on an exact phrase, and the
    only honest way to find that is forced alignment, which the API key cannot
    yet call. Skipping them is not the same as forgetting them: they stay in
    the cue sheet, they are printed on every run, and the story is re-mixed to
    add them once the position can be measured.

    The alternative — mixing them at the segment start — is precisely the
    placement the owner rejected twice.
    """
    keep, held = [], []
    for c in cues:
        if c.get("held"):
            held.append(f"{c['sound']}@seg{c['segment']} HELD: {c['held']}")
        else:
            keep.append(c)
    return keep, held


def run(story_id: str, segments_dir: Path | None, sounds_dir: Path, out_dir: Path,
        render: bool, narration: Path | None = None,
        map_path: Path | None = None, force: bool = False,
        install_marker: Path | None = None) -> int:
    cues, held = partition_held(load_cues(story_id))
    refuse_if_already_mixed(narration, force)
    audio_dir = narration.parent if narration is not None else Path(".")
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

    lines, line_notes = resolve_line_positions(
        story_id, cues, durations, starts, narration, audio_dir)
    chains = build_chains(cues, starts, durations, sounds, lines)

    # Where the narration is cut open, and how wide. Sorted, because
    # shift_for() sums every insertion at or before a moment and two cuts in
    # the wrong order would compound wrongly.
    insertions: list[tuple[float, float]] = []
    inserted_at: dict[int, float] = {}
    silences_all = detect_silences(narration) if narration else []
    by_cue = {c["cue"]: i for i, c in enumerate(chains) if c["kind"] != "bed"}
    for ci, cue in enumerate(cues):
        if not cue.get("insert"):
            continue
        idx = by_cue[ci]
        chain = chains[idx]
        # insertAtSeconds is a hand-placed offset from the segment start, put
        # there when the estimate was audibly wrong and there was no alignment
        # to appeal to. It is still snapped to a real silence, so a re-encode
        # cannot drift the cut into the middle of a word.
        want = (starts[cue["segment"]] + float(cue["insertAtSeconds"])
                if "insertAtSeconds" in cue else chain["start"])
        cut = snap_to_pause(want, silences_all, SNAP_WINDOW_S)
        if cut is None:
            if silences_all:
                raise SystemExit(
                    f"segment {cue['segment']} / {cue['sound']}: insert=true but "
                    f"no pause within {SNAP_WINDOW_S}s of {chain['start']:.2f}s. "
                    f"Cutting anywhere else would slice a word in half.")
            cut = chain["start"]
            line_notes.append(f"seg {cue['segment']}: cutting at {cut:.2f}s "
                              f"unchecked — no pause data (is ffmpeg present?)")
        body = sound_content_length(chain["src"]) or float(cue["seconds"])
        gap = INSERT_LEAD_S + body + INSERT_TAIL_S
        insertions.append((cut, gap))
        inserted_at[idx] = cut
        line_notes.append(f"seg {cue['segment']}: cut at {cut:.2f}s, "
                          f"{gap:.2f}s hole for {body:.2f}s of {cue['sound']}")

    if insertions:
        insertions.sort()
        chains, starts = apply_insertions(chains, starts, insertions)
        # The inserted sound plays INSIDE its own hole, not at the shifted
        # position of the moment it was cut at — that would put it just before
        # the silence it opened.
        for idx, cut in inserted_at.items():
            before = sum(g for c, g in insertions if c < cut)
            chains[idx] = {**chains[idx],
                           "start": cut + before + INSERT_LEAD_S,
                           "duration": sound_content_length(chains[idx]["src"])
                                       or chains[idx]["duration"]}
        total = shift_for(total, insertions)

    src_note = (f"from {narration.name} + {map_path.name}" if narration
                else f"from {len(segs)} WAV file(s)")
    print(f"{story_id}  {len(durations)} segments, {mmss(total)} total  ({src_note})")
    print(f"{'seg':>4} {'starts at':>10}  {'length':>8}")
    shown = [starts[i + 1] - starts[i] if i + 1 < len(starts) else total - starts[i]
             for i in range(len(starts))]
    for i, (s, d) in enumerate(zip(starts, shown)):
        print(f"{i:>4} {mmss(s):>10}  {d:>7.2f}s")
    print()
    print(f"{'at':>10} {'for':>7} {'level':>7}  sound")
    for c in sorted(chains, key=lambda x: x["start"]):
        print(f"{mmss(c['start']):>10} {c['duration']:>6.2f}s "
              f"{c['level']:>6.1f}dB  {c['label']}")

    for h in held:
        print(f"  {h}")
    for n in line_notes:
        print(f"  {n}")
    if line_notes or held:
        print()

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
    cmd += ["-filter_complex", build_filtergraph(len(segs), chains, insertions),
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
        {"storyId": story_id, "unit": "seconds",
         "starts": [round(s, 3) for s in starts],
         "durations": [round(d, 3) for d in shown]},
        indent=2), encoding="utf-8")

    # The marker travels with the story, so name where it must end up rather
    # than dropping it in the scratch directory the mix was built in.
    marker_target = install_marker or ambience_marker(
        REPO / "backend/src/ArmenianAiToy.Api/story-audio" / f"{story_id}.mp3")
    marker_target.write_text(json.dumps(
        {"storyId": story_id,
         "sounds": sorted({c["sound"] for c in chains}),
         "cues": len([c for c in chains if c["kind"] != "bed"]),
         "mixedFrom": narration.name if narration else str(segments_dir),
         "heldCues": held,
         "note": "Do not mix this file again — see refuse_if_already_mixed in "
                 "tools/story-audio/mix_ambience.py. The narration-only master "
                 "is in git history."},
        indent=2) + "\n", encoding="utf-8")

    print()
    print(f"wrote {mixed}")
    print(f"wrote {seg_map}")
    print(f"wrote {marker_target}")
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

    # --- at="line": the arithmetic only, no ffmpeg and no audio ------------
    # narrator(72) then wolf(151) in a 15.57s segment with one speaker change:
    # 15.17s of words, so the boundary is 72/223 of the way through them.
    est = span_boundary_estimate([72, 151], 1, 15.57, [SPAN_PAUSE_SPEAKER])
    check("span boundary lands inside the segment", 0 < est < 15.57, True)
    check("span boundary from characters", round(est, 2), 4.90)
    check("boundary 0 is the segment start",
          span_boundary_estimate([72, 151], 0, 15.57, [SPAN_PAUSE_SPEAKER]), 0.0)
    # Subtracting the stitched pause matters: smearing it across both spans
    # would push the boundary later, and later is where the bug was.
    naive = 15.57 * 72 / 223
    check("gap subtraction pulls the boundary earlier than a naive split",
          est < naive, True)

    # NEAREST, not longest. This is the «Ուլիկը» case exactly: a 0.79s breath
    # inside the narrator's sentence, and the 0.31s real speaker change.
    sil = [(35.33, 36.12), (37.78, 38.09), (44.40, 44.61)]
    check("snaps to the nearest pause", snap_to_pause(37.40, sil), 37.78)
    check("does not prefer the longest", snap_to_pause(37.40, sil) != 35.33, True)
    check("gives up rather than reach", snap_to_pause(41.00, sil), None)
    # Inclusive at the edge: a pause exactly SNAP_WINDOW_S away is reachable.
    check("a pause exactly at the window edge is reachable",
          snap_to_pause(37.78 - SNAP_WINDOW_S, [(37.78, 38.09)]), 37.78)
    check("and a hair beyond it is not",
          snap_to_pause(37.78 - SNAP_WINDOW_S - 0.01, [(37.78, 38.09)]), None)

    # A line cue with nothing resolved must fail loudly, because the fallback
    # would be the segment start - precisely the position that was wrong.
    try:
        resolve_cue_time({"segment": 2, "at": "line"}, [0.0, 10.0, 32.0],
                         [10.0, 22.0, 16.0], None)
        check("unresolved line anchor refuses", "no exception", "SystemExit")
    except SystemExit:
        check("unresolved line anchor refuses", True, True)
    check("a resolved line anchor is segment start plus the offset",
          resolve_cue_time({"segment": 2, "at": "line"}, [0.0, 10.0, 32.0],
                           [10.0, 22.0, 16.0], {2: 5.6}), 37.6)

    check("validate rejects an unknown anchor",
          any("at must be" in p for p in validate(
              [{"segment": 0, "sound": "x", "level": -20, "seconds": 2,
                "at": "middle"}], 1, {"x": Path("x.mp3")}, 60.0)), True)
    check("validate rejects at=line with no cueLine",
          any("needs a cueLine" in p for p in validate(
              [{"segment": 0, "sound": "x", "level": -20, "seconds": 2,
                "at": "line"}], 1, {"x": Path("x.mp3")}, 60.0)), True)

    # --- insert: cut the story open --------------------------------------
    ins = [(35.33, 1.50), (64.59, 1.50)]
    check("before the first cut nothing moves", shift_for(10.0, ins), 10.0)
    check("after one cut everything moves by it", shift_for(40.0, ins), 41.50)
    check("two cuts compound", shift_for(70.0, ins), 73.00)
    check("a moment exactly at a cut counts it", shift_for(35.33, ins), 36.83)

    ch = [{"src": Path("f.mp3"), "start": 30.0, "duration": 10.0, "level": -28.0,
           "kind": "bed", "label": "forest~bed"},
          {"src": Path("k.mp3"), "start": 35.33, "duration": 1.0, "level": -18.0,
           "kind": "oneshot", "label": "knock"}]
    moved, mstarts = apply_insertions(ch, [0.0, 32.18, 63.37], ins)
    check("a bed spanning a cut grows by the gap", moved[0]["duration"], 11.5)
    check("its start is untouched before the cut", moved[0]["start"], 30.0)
    check("segment starts shift too", mstarts, [0.0, 32.18, 64.87])

    g = build_filtergraph(1, [], [(35.33, 1.5)])
    check("the narration is split at the cut", "atrim=0.000:35.330" in g, True)
    check("and the remainder is taken to the end", "atrim=35.330" in g, True)
    check("silence is generated, not an input file",
          "aevalsrc=0:d=1.500" in g, True)
    # A label may be PRODUCED once (amix then consumes it, which is the
    # second occurrence and is fine). Only one concat may claim it.
    check("only one stage produces [narr]", g.count(":a=1[narr]"), 1)
    check("the intermediate label is produced once", g.count(":a=1[narr0]"), 1)
    check("no insertions means no split at all",
          "asplit" in build_filtergraph(1, []), False)

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
    ap.add_argument("--force", action="store_true",
                    help="mix even if the narration already carries ambience")
    ap.add_argument("--marker", type=Path,
                    help="where to write the already-mixed marker "
                         "(default: beside the shipped story)")
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
               a.narration, a.map_path, a.force, a.marker)


if __name__ == "__main__":
    sys.exit(main())

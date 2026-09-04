#!/usr/bin/env python3
"""Exact word times for a cast render — one forced alignment PER SPAN.

Why not align the finished story once, as align_story.py does for the shipped
library? Because a cast render is several voices, and on the 2026-09-03 Ուլիկը
pilot the whole-story alignment drifted by -29.6 s over 2:20 (the designed
wolf and the pitch-lifted kid threw it), which put both door knocks half a
second late. Each span, on its own, is one voice and a few seconds, and the
aligner is exact on it. render_story.py already knows where every span
starts (`<storyId>.spans.json`, relative to its segment), so the per-span
times are simply shifted into place.

Output: `<storyId>.words.json` in the same shape align_story.py writes, with
`"exact": true`, in the MIXER's timeline (segments back to back, no
inter-segment gap — that is how mix_ambience.py concatenates them), so the
mixer applies no drift correction to it.

    ELEVENLABS_API_KEY=… python3 tools/story-voices/align_spans.py <storyId> <renderDir>

Writes `<renderDir>/segments/<storyId>.words.json` when that directory
exists (where the mixer looks in --segments-dir mode), else beside the
render. One paid alignment call per span.
"""
import json, os, sys, subprocess
from pathlib import Path

import httpx

ENDPOINT = "https://api.elevenlabs.io/v1/forced-alignment"
REPO = Path(__file__).resolve().parents[2]


def duration(p: Path) -> float:
    out = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration",
                          "-of", "csv=p=0", str(p)], capture_output=True, text=True).stdout.strip()
    return float(out) if out else 0.0


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__); return 2
    sid, rdir = sys.argv[1], Path(sys.argv[2])
    key = os.environ.get("ELEVENLABS_API_KEY") or sys.exit("set ELEVENLABS_API_KEY")
    smap = json.loads((REPO / "backend/content/story-voices" / f"{sid}.voices.json").read_text(encoding="utf-8"))
    spans = json.loads((rdir / f"{sid}.spans.json").read_text(encoding="utf-8"))["segments"]

    # Mixer timeline: each segment starts where the previous segment's audio
    # ended. render_story.py's own <sid>.segments.json includes its 0.6 s page
    # turn between segments; the mixer does not, so measure the WAVs.
    starts, t = [], 0.0
    for seg in smap["segments"]:
        starts.append(round(t, 3))
        t += duration(rdir / f"{sid}-seg{seg['index']}.mp3")

    words = []
    with httpx.Client(timeout=120) as c:
        for si, seg in enumerate(smap["segments"]):
            for pi, sp in enumerate(seg["spans"]):
                wav = rdir / f"{sid}-{si:02d}-{pi:02d}-{sp['speaker']}.wav"
                text = sp["text"].strip()
                r = c.post(ENDPOINT, headers={"xi-api-key": key},
                           files={"file": (wav.name, wav.read_bytes(), "audio/wav")},
                           data={"text": text})
                r.raise_for_status()
                off = starts[si] + spans[si][pi]["start"]
                got = [w for w in r.json().get("words", []) if w.get("text", "").strip()]
                for w in got:
                    words.append({"text": w["text"], "start": round(w["start"] + off, 3),
                                  "end": round(w["end"] + off, 3)})
                print(f"  seg{si} span{pi} {sp['speaker']:10} {len(got):3} words  at {off:7.2f}s", flush=True)

    doc = {"storyId": sid, "exact": True, "timeline": "segments back to back, no gap (mix_ambience --segments-dir)",
           "source": "forced alignment per span; offsets from render_story spans.json + measured segment lengths",
           "words": words}
    out_dir = rdir / "segments" if (rdir / "segments").is_dir() else rdir
    out = out_dir / f"{sid}.words.json"
    out.write_text(json.dumps(doc, ensure_ascii=False, indent=0), encoding="utf-8")
    print(f"  -> {out}  {len(words)} words")
    return 0


if __name__ == "__main__":
    sys.exit(main())

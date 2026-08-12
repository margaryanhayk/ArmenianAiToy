#!/usr/bin/env python3
"""Find out when each word of a story is actually spoken.

WHY
---
An ambience cue is supposed to land on a LINE — «Խոսող ձուկը»'s cue sheet says
the splash "must land exactly on «գցում գետը» and nowhere else". Until now the
mixer estimated that position from character counts and snapped it to a nearby
pause, and it was wrong twice in one evening, in opposite directions: the wolf's
knock in «Ուլիկը» first fired 5.6s early, then 2.4s late. The owner heard both.
An estimate that needs an ear to correct it is not a measurement.

This is alignment, NOT transcription. The exact Armenian text is already in the
repo and approved, so it is sent along with the audio; the model is only asked
WHEN each of those words is said. It cannot invent a word that is not in the
story, which is the failure a speech-to-text pass would risk.

WHAT IT WRITES
--------------
`<storyId>.words.json` beside the audio:

    {"storyId": ..., "unit": "seconds",
     "words": [{"text": "Խոր", "start": 0.12, "end": 0.41}, ...]}

Times are ABSOLUTE into the file it aligned. That file is the shipped narration,
so re-shipping (which re-encodes) can shift them by a few milliseconds — small
against the pauses these are used to find, and the mixer snaps to a real silence
anyway. Re-run after a re-render, when the words genuinely move.

DRY RUN BY DEFAULT
------------------
Prints what it would send and writes nothing. Same two-man rule as
tools/ElevenLabsRender and tools/story-ambience/generate_sounds.py.

USAGE
    python3 tools/story-audio/align_story.py                    # all, dry
    python3 tools/story-audio/align_story.py --story ulik
    ...  --render --confirm-paid-api
    python3 tools/story-audio/align_story.py --self-test        # no network

    ELEVENLABS_API_KEY must be set to render.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]


def rel(p: Path) -> str:
    """Repo-relative when it is in the repo, absolute otherwise — an
    --audio-dir pointing at a scratch copy is normal, not an error."""
    try:
        return str(p.relative_to(REPO))
    except ValueError:
        return str(p)
AUDIO_DIR = REPO / "backend/src/ArmenianAiToy.Api/story-audio"
STORY_DIR = REPO / "backend/src/ArmenianAiToy.Application/Stories/Content"
ENDPOINT = "https://api.elevenlabs.io/v1/forced-alignment"

# Armenian intra-word marks and punctuation, stripped before comparing a word
# from the story text with a word from the alignment. The two disagree about
# these often enough that a literal match would fail on ordinary lines.
_STRIP = "՞՛՜՝․։,.!?—-«»…և"


def norm(w: str) -> str:
    return w.strip(_STRIP).strip().lower()


def words_of(text: str) -> list[str]:
    return [w for w in re.split(r"\s+", text) if norm(w)]


def find_phrase_end(words: list[dict], phrase: str,
                    search_from: float = 0.0) -> float | None:
    """When the last word of `phrase` stops being spoken.

    Matched on normalised word sequences rather than raw strings: the story
    text and the alignment disagree about Armenian punctuation often enough
    that a literal comparison fails on ordinary lines.

    `search_from` exists because a story repeats itself — «Սևուկ ուլիկ, Սիրուն
    բալիկ» is sung three times in «Ուլիկը» by three different characters, so
    the FIRST match is the wrong one for every cue but the first.
    """
    want = [norm(w) for w in words_of(phrase)]
    if not want:
        return None
    have = [norm(w["text"]) for w in words]
    for i in range(len(have) - len(want) + 1):
        if have[i:i + len(want)] != want:
            continue
        if words[i]["start"] < search_from:
            continue
        return words[i + len(want) - 1]["end"]
    return None


def load_words(story_id: str, audio_dir: Path = AUDIO_DIR) -> list[dict]:
    p = audio_dir / f"{story_id}.words.json"
    if not p.exists():
        return []
    return json.loads(p.read_text(encoding="utf-8")).get("words", [])


def story_text(story_id: str) -> str:
    doc = json.loads((STORY_DIR / f"{story_id}.story.json").read_text(encoding="utf-8"))
    return " ".join(s.strip() for s in doc["segments"])


def refuse_if_mixed(audio: Path) -> None:
    """A word map must describe the NARRATION, not a mix built from it.

    Once ambience is inserted the words move — that is the whole point of
    insertion — so aligning the shipped file would produce times for a
    timeline the mixer is about to replace. The mixer leaves a marker; this
    reads it, for the same reason and with the same recovery.
    """
    m = audio.with_suffix(".ambience.json")
    if m.exists():
        raise SystemExit(
            f"{audio.name} carries ambience, so its word times are not the "
            f"narration's. Align the narration-only master instead:\n"
            f"  git log --oneline -- {audio}\n"
            f"  git show <commit-before-the-mix>:{audio} > /tmp/clean.mp3\n"
            f"then run with --audio-dir pointing at it.")


def align(story_id: str, audio: Path, text: str, token: str, out: Path) -> None:
    refuse_if_mixed(audio)
    r = subprocess.run(
        ["curl", "-sS", "--max-time", "600", "-X", "POST",
         "-H", f"xi-api-key: {token}",
         "-F", f"file=@{audio}",
         "-F", f"text={text}",
         ENDPOINT], capture_output=True)
    try:
        doc = json.loads(r.stdout.decode())
    except Exception:
        raise SystemExit(f"{story_id}: alignment returned non-JSON: "
                         f"{r.stdout[:300]!r} {r.stderr.decode()[:200]}")
    if "words" not in doc:
        raise SystemExit(f"{story_id}: alignment had no words: "
                         f"{json.dumps(doc)[:400]}")
    words = [{"text": w.get("text", ""),
              "start": round(float(w["start"]), 3),
              "end": round(float(w["end"]), 3)}
             for w in doc["words"] if norm(w.get("text", ""))]
    out.write_text(json.dumps(
        {"storyId": story_id, "unit": "seconds", "alignedFile": audio.name,
         "words": words}, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print(f"wrote {rel(out)}  {len(words)} words, "
          f"{words[-1]['end']:.1f}s")


def self_test() -> int:
    ok = True

    def check(name, got, want):
        nonlocal ok
        if got != want:
            print(f"  FAIL {name}: got {got!r}, wanted {want!r}")
            ok = False
        else:
            print(f"  ok   {name}")

    w = [{"text": "Մի", "start": 0.0, "end": 0.3},
         {"text": "իրիկուն", "start": 0.3, "end": 0.9},
         {"text": "դուռը", "start": 1.0, "end": 1.4},
         {"text": "զարկում", "start": 1.4, "end": 2.1},
         {"text": "ու", "start": 2.2, "end": 2.3},
         {"text": "կանչում․", "start": 2.3, "end": 3.0},
         {"text": "Սևուկ", "start": 3.4, "end": 3.9},
         {"text": "ուլիկ", "start": 3.9, "end": 4.4},
         {"text": "դուռը", "start": 9.0, "end": 9.4},
         {"text": "զարկում", "start": 9.4, "end": 10.1}]

    check("a phrase ends when its last word does",
          find_phrase_end(w, "դուռը զարկում"), 2.1)
    check("a whole clause resolves to its last word",
          find_phrase_end(w, "Մի իրիկուն դուռը զարկում ու կանչում․"), 3.0)
    check("a phrase whose words are not consecutive does not match",
          find_phrase_end(w, "Մի զարկում"), None)
    check("punctuation does not break the match",
          find_phrase_end(w, "ու կանչում"), 3.0)
    # A story repeats itself: «Սևուկ ուլիկ» is sung three times in «Ուլիկը» by
    # three characters. Without search_from every cue after the first lands on
    # the first singing.
    check("search_from skips an earlier identical phrase",
          find_phrase_end(w, "դուռը զարկում", search_from=5.0), 10.1)
    check("and finds the first one when not asked to skip",
          find_phrase_end(w, "դուռը զարկում", search_from=0.0), 2.1)
    check("a phrase that is not there returns None",
          find_phrase_end(w, "անտառում մի այծ"), None)
    check("an empty phrase returns None", find_phrase_end(w, "   "), None)
    check("case and marks are normalised", norm("Կանչում՛․"), "կանչում")

    # The cue sheet's real cueLines must all be findable in their story text,
    # or alignment will resolve them to nothing and the mixer will fall back
    # to estimating — silently, which is the failure this replaces.
    cues = json.loads((REPO / "backend/content/story-ambience/ambience-cues.json")
                      .read_text(encoding="utf-8"))
    missing = []
    for st in cues["stories"]:
        # Stand in for an alignment: every word of the story, in order, with
        # made-up times. If find_phrase_end cannot locate a cueLine here it
        # will not locate it in a real alignment either.
        fake = [{"text": t, "start": float(i), "end": i + 0.5}
                for i, t in enumerate(words_of(story_text(st["storyId"])))]
        for c in st["cues"]:
            line = c.get("cueLine", "").strip()
            if line and find_phrase_end(fake, line) is None:
                missing.append(f"{st['storyId']} seg{c['segment']}: {line[:45]}")
    check("every cueLine is findable in its story text", missing, [])

    print("PASS" if ok else "FAIL")
    return 0 if ok else 1


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--story")
    ap.add_argument("--audio-dir", type=Path, default=AUDIO_DIR)
    ap.add_argument("--render", action="store_true")
    ap.add_argument("--confirm-paid-api", action="store_true")
    ap.add_argument("--force", action="store_true",
                    help="re-align even where a word map already exists")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()

    if a.self_test:
        return self_test()

    ids = ([a.story] if a.story else
           sorted(p.stem for p in a.audio_dir.glob("*.mp3")))
    jobs = []
    for sid in ids:
        audio = a.audio_dir / f"{sid}.mp3"
        out = a.audio_dir / f"{sid}.words.json"
        if not audio.exists():
            print(f"skip {sid}: no audio", file=sys.stderr)
            continue
        jobs.append((sid, audio, out, story_text(sid)))

    todo = [j for j in jobs if a.force or not j[2].exists()]
    print(f"{len(jobs)} story/stories, {len(todo)} to align\n")
    for sid, audio, out, text in jobs:
        state = "ALIGN" if (sid, audio, out, text) in todo else "exists"
        print(f"[{state:6}] {sid:18} {len(text):>5} chars  -> "
              f"{rel(out)}")

    if not a.render:
        print("\nDRY RUN — nothing written. Pass --render --confirm-paid-api.")
        return 0
    if not a.confirm_paid_api:
        print("--render needs --confirm-paid-api: this spends money.", file=sys.stderr)
        return 1
    token = os.environ.get("ELEVENLABS_API_KEY")
    if not token:
        print("set ELEVENLABS_API_KEY", file=sys.stderr)
        return 1

    for sid, audio, out, text in todo:
        align(sid, audio, text, token, out)
    return 0


if __name__ == "__main__":
    sys.exit(main())

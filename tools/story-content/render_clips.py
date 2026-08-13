#!/usr/bin/env python3
"""Render the approved Armenian clips — games, alternate endings, the serial.

WHAT IT RENDERS
---------------
    games     backend/content/offline-games/game-clips.json      90 clips
    endings   backend/content/variant-endings/variant-endings.json  10
    serial    backend/content/serial-hero/tsivik-series.json        6 + 3

SAMPLE FIRST — THIS IS THE OWNER'S RULE, NOT A SUGGESTION
---------------------------------------------------------
`game-clips.json`'s own render note says it plainly: render ONE «Քո կենդանին …»
question, ONE guess clip, ONE sound-detective round and the buzzer win pair
BEFORE batch-rendering, because "one bad pattern would poison a whole family".
16 guess clips share a sentence; three win clips share a sentence; ten rounds
share a shape. The serial carries the same rule for a different reason: «Ծիվի՛կ»
opens the refrain and is the most-heard word in six episodes.

So `--sample` is the default target. `--all` needs `--i-listened-to-the-sample`,
which is a promise a human made, not a flag a script sets.

THE GUARDS, EACH BOUGHT WITH A DEFECT
-------------------------------------
- **Nothing but the clip's own words is ever sent.** No direction, no bracket,
  no Latin. `eleven_v3` read an English stage direction ALOUD inside a Tumanyan
  tale on 2026-08-12; a guard refuses brackets or a run of Latin letters.
- **A chopped tail needs two signals.** ElevenLabs sometimes returns a short
  request with the last word cut. Loud-at-the-very-end alone cries wolf — an
  emphatic Armenian ending genuinely peaks late — so truncation is loud AND
  shorter than the text implies, and it is re-requested before it fails.
- **Fades are timed against the file that exists**, in a second pass, because
  computing them from the pre-`loudnorm` duration did nothing at all.

VOICES
------
    areg    areg-storyteller   the story narration and the 43 welcome clips
    katrin  katrin-v3          the invented child characters; they are not
    vardan  vardan-v2          real people, so there was nobody to ask

DRY RUN BY DEFAULT. `--render --confirm-paid-api` spends money.

USAGE
    python3 tools/story-content/render_clips.py --sample --out <dir>
    python3 tools/story-content/render_clips.py --sample --out <dir> \\
        --render --confirm-paid-api
    python3 tools/story-content/render_clips.py --all --out <dir> \\
        --i-listened-to-the-sample --render --confirm-paid-api
    python3 tools/story-content/render_clips.py --self-test
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
sys.path.insert(0, str(REPO / "tools/story-voices"))

VOICES = {
    "areg":   "NxAsEwnikgCJa5tyBwEf",   # areg-storyteller
    "katrin": "bpss31fpiTyvjz5XZUT0",   # katrin-v3
    "vardan": "PgFHHZMKb19tJbjBe6aY",   # vardan-v2
}
# Expression comes from API fields, never from words in the text — that is the
# whole lesson of 2026-08-12. Areg in a game is a notch brisker than in a story
# (MODES.md §2); the children are looser still, because they are playing.
SETTINGS = {
    "areg":   {"stability": 0.40, "similarity_boost": 0.75, "style": 0.45},
    "katrin": {"stability": 0.30, "similarity_boost": 0.75, "style": 0.60},
    "vardan": {"stability": 0.30, "similarity_boost": 0.75, "style": 0.60},
}
CHARS_PER_SECOND = 15.0
MAX_OVERRUN = 2.5
# ...but a RATIO alone is meaningless on a very short clip. «Մու-մու։» is eight
# characters, so chars/15 expects half a second and 2.5x is 1.3s — and the file
# is 1.4s, because a child stretches an animal sound. The render note says so
# in as many words: "the kid performers stretch them naturally." The guard
# rejected a perfectly good cow and stopped the batch at 62 of 109.
#
# So an overrun must be BOTH proportionally and absolutely large. Three seconds
# is far more than any natural stretch and far less than the 4.3s an eight-
# character line came back as on 2026-08-12 when eleven_v3 spoke a stage
# direction — which is the thing this guard is actually for.
MAX_OVERRUN_SECONDS = 3.0
TAIL_WINDOW = 0.03
TAIL_MAX_RATIO = 0.35
TAIL_MIN_LENGTH_RATIO = 0.70
TAIL_RETRIES = 2
FADE_IN = 0.008
FADE_OUT = 0.030
LATIN_RUN = re.compile(r"[A-Za-z]{3,}")

# The sample, exactly as game-clips.json's render note specifies it, plus one
# of each other kind so nothing batches unheard.
SAMPLE = [
    ("games", "mind-reader", "q-root"),        # one «Քո կենդանին …» question
    ("games", "mind-reader", "g-1111"),        # one guess clip (the one he rewrote)
    ("games", "sound-detective", "r01-sound"), # one round: the child…
    ("games", "sound-detective", "r01-ask"),   # …and Areg's question
    ("games", "who-first", "win-green"),       # the buzzer win pair
    ("games", "who-first", "win-red"),
    ("serial", "tsivik", "series-refrain"),    # «Ծիվի՛կ» — the most-heard word
    ("serial", "tsivik", "series-closing"),
    ("endings", "little-cloud", "ending"),     # the shortest ending
]


def guard(text: str) -> str:
    if "[" in text or "]" in text:
        raise SystemExit(f"REFUSED: bracket in text sent to TTS: {text[:60]!r}")
    if LATIN_RUN.search(text):
        raise SystemExit(f"REFUSED: Latin letters in Armenian text: {text[:60]!r}")
    return text


def jobs_all() -> list[dict]:
    """Every approved clip, in render order. Pure — no network, no files written."""
    out = []
    g = json.loads((REPO / "backend/content/offline-games/game-clips.json")
                   .read_text(encoding="utf-8"))
    for game, v in g.items():
        if game.startswith("_"):
            continue
        for c in v["clips"]:
            # A clip marked "new" is text the owner has not approved yet.
            # Rendering it would spend money on words that may not survive his
            # reading, and — worse — could put an unreviewed line in front of a
            # child if it ever shipped by accident.
            if c.get("new"):
                continue
            out.append({"kind": "games", "group": game, "id": c["id"],
                        "speaker": c.get("speaker", "areg"), "text": c["text"]})
    e = json.loads((REPO / "backend/content/variant-endings/variant-endings.json")
                   .read_text(encoding="utf-8"))
    for x in e["endings"]:
        out.append({"kind": "endings", "group": x["storyId"], "id": "ending",
                    "speaker": "areg", "text": x["endingText"]})
    t = json.loads((REPO / "backend/content/serial-hero/tsivik-series.json")
                   .read_text(encoding="utf-8"))
    for key in ("introClip", "openingRefrain", "closingClip"):
        if key in t:
            out.append({"kind": "serial", "group": "tsivik", "id": t[key]["id"],
                        "speaker": "areg", "text": t[key]["text"]})
    for ep in t["episodes"]:
        out.append({"kind": "serial", "group": "tsivik", "id": f"episode-{ep['episode']}",
                    "speaker": "areg", "text": ep["text"]})
    for j in out:
        j["path"] = f"{j['kind']}/{j['group']}/{j['id']}.mp3"
    return out


def pick_sample(all_jobs: list[dict]) -> list[dict]:
    index = {(j["kind"], j["group"], j["id"]): j for j in all_jobs}
    missing = [k for k in SAMPLE if k not in index]
    if missing:
        raise SystemExit(f"the sample names clips that do not exist: {missing}")
    return [index[k] for k in SAMPLE]


def duration(path: Path) -> float:
    out = subprocess.run(["ffprobe", "-v", "error", "-show_entries",
                          "format=duration", "-of", "csv=p=0", str(path)],
                         capture_output=True, text=True).stdout.strip()
    return float(out) if out else 0.0


def tail_ratio(path: Path) -> float:
    import struct
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", str(path), "-f", "s16le",
                          "-ac", "1", "-ar", "44100", "-"],
                         capture_output=True).stdout
    n = len(raw) // 2
    if n < 2000:
        return 1.0
    s = struct.unpack(f"<{n}h", raw[:n * 2])
    peak = max(abs(x) for x in s) or 1
    edge = int(TAIL_WINDOW * 44100)
    return (max(abs(x) for x in s[-edge:]) or 0) / peak


def tts(text: str, path: Path, token: str, voice: str, settings: dict) -> None:
    body = {"text": text, "model_id": "eleven_v3", "voice_settings": settings}
    r = subprocess.run(
        ["curl", "-sS", "--max-time", "300", "-X", "POST",
         "-H", f"xi-api-key: {token}", "-H", "content-type: application/json",
         "--data-binary", "@-", "-o", str(path),
         f"https://api.elevenlabs.io/v1/text-to-speech/{voice}"
         f"?output_format=mp3_44100_128"],
        input=json.dumps(body, ensure_ascii=False).encode(), capture_output=True)
    if not path.exists() or path.stat().st_size < 1000:
        detail = path.read_text(errors="replace")[:300] if path.exists() else ""
        raise SystemExit(f"render failed for {text[:40]!r}: "
                         f"{detail or r.stderr.decode()[:200]}")


def render(job: dict, out_dir: Path, token: str) -> None:
    text = guard(job["text"].strip())
    voice = VOICES[job["speaker"]]
    dest = out_dir / job["path"]
    dest.parent.mkdir(parents=True, exist_ok=True)
    raw = dest.with_suffix(".raw.mp3")
    expect = len(text) / CHARS_PER_SECOND

    for attempt in range(TAIL_RETRIES + 1):
        tts(text, raw, token, voice, SETTINGS[job["speaker"]])
        ratio, got = tail_ratio(raw), duration(raw)
        if ratio <= TAIL_MAX_RATIO or got >= expect * TAIL_MIN_LENGTH_RATIO:
            break
        if attempt == TAIL_RETRIES:
            raise SystemExit(
                f"CHOPPED: {job['path']} ends at {ratio:.0%} of its peak AND is "
                f"{got:.1f}s against ~{expect:.1f}s expected, after "
                f"{TAIL_RETRIES} retries — the last word is cut")
        print(f"      tail {ratio:.0%}, short — re-asking", flush=True)

    got = duration(raw)
    if got > expect * MAX_OVERRUN and got > expect + MAX_OVERRUN_SECONDS:
        raise SystemExit(f"{job['path']}: {len(text)} chars implies ~{expect:.1f}s, "
                         f"got {got:.1f}s — something was spoken that is not in "
                         f"the clip")
    stage = dest.with_suffix(".stage.mp3")
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(raw),
                    "-af", "loudnorm=I=-17:TP=-1.5", "-ar", "44100", "-ac", "1",
                    str(stage)], capture_output=True)
    real = duration(stage)
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(stage), "-af",
                    f"afade=t=in:st=0:d={FADE_IN},"
                    f"afade=t=out:st={max(0, real - FADE_OUT):.3f}:d={FADE_OUT}",
                    "-ar", "44100", "-ac", "1", "-b:a", "128k", str(dest)],
                   capture_output=True)
    raw.unlink(missing_ok=True)
    stage.unlink(missing_ok=True)
    print(f"      {duration(dest):5.1f}s  {dest.stat().st_size:>7,} B")


def self_test() -> int:
    ok = True

    def check(name, got, want):
        nonlocal ok
        if got != want:
            print(f"  FAIL {name}: got {got!r}, wanted {want!r}")
            ok = False
        else:
            print(f"  ok   {name}")

    jobs = jobs_all()
    check("every approved clip is a job", len(jobs), 109)   # 90 + 10 + 6 + 3

    # Unapproved additions must never enter a render.
    raw = json.loads((REPO / "backend/content/offline-games/game-clips.json")
                     .read_text(encoding="utf-8"))
    pending = {c["id"] for g, v in raw.items() if not g.startswith("_")
               for c in v["clips"] if c.get("new")}
    check("there are pending lines to exclude", len(pending) > 0, True)
    check("and not one of them is a job",
          {j["id"] for j in jobs} & pending, set())
    check("games", sum(1 for j in jobs if j["kind"] == "games"), 90)
    check("endings", sum(1 for j in jobs if j["kind"] == "endings"), 10)
    check("serial", sum(1 for j in jobs if j["kind"] == "serial"), 9)
    check("every speaker has a voice",
          sorted({j["speaker"] for j in jobs}), ["areg", "katrin", "vardan"])
    check("no two jobs share a path",
          len({j["path"] for j in jobs}), len(jobs))

    sample = pick_sample(jobs)
    check("the sample is what the render note asks for", len(sample), len(SAMPLE))
    check("it covers a question and a guess",
          {j["id"] for j in sample} >= {"q-root", "g-1111"}, True)
    check("it covers both kid voices used in one round",
          {j["speaker"] for j in sample} >= {"areg", "katrin"}, True)
    check("it renders «Ծիվի՛կ» before any episode",
          any(j["id"] == "series-refrain" for j in sample), True)
    check("it batches no episode", any(j["id"].startswith("episode-")
                                       for j in sample), False)

    # The guard that stops a stage direction being spoken to a child.
    for bad in ("[deep voice] բարև", "a poor gentle man"):
        try:
            guard(bad); check(f"guard refuses {bad[:18]!r}", "allowed", "refused")
        except SystemExit:
            check(f"guard refuses {bad[:18]!r}", True, True)
    check("guard allows plain Armenian", guard("Բարև՛ քեզ։"), "Բարև՛ քեզ։")

    # The overrun rule, against the two cases that matter: a stretched animal
    # sound (keep) and a spoken stage direction (fail).
    def overruns(chars, got):
        expect = chars / CHARS_PER_SECOND
        return got > expect * MAX_OVERRUN and got > expect + MAX_OVERRUN_SECONDS
    check("a stretched «Մու-մու։» is kept", overruns(8, 1.4), False)
    check("a drawn-out «Ծուղրուղո՜ւ։» is kept", overruns(12, 2.5), False)
    check("an 8-char line that ran 4.3s is caught", overruns(8, 4.3), True)
    # 500 chars expects ~33s; the ratio rule needs >83s, the absolute one >36s.
    check("a long line at three times its length is caught", overruns(500, 90.0), True)
    check("a long line slightly over is kept", overruns(500, 40.0), False)
    for j in jobs:
        guard(j["text"])
    check("every approved clip passes the guard", True, True)

    print("PASS" if ok else "FAIL")
    return 0 if ok else 1


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--sample", action="store_true")
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--out", type=Path, default=Path("clips"))
    ap.add_argument("--render", action="store_true")
    ap.add_argument("--confirm-paid-api", action="store_true")
    ap.add_argument("--i-listened-to-the-sample", action="store_true")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()

    if a.self_test:
        return self_test()

    jobs = jobs_all()
    if a.all:
        if not a.i_listened_to_the_sample:
            print("--all needs --i-listened-to-the-sample. game-clips.json's own "
                  "render note requires a sample first: 16 guess clips share a "
                  "sentence and one bad pattern poisons the family.",
                  file=sys.stderr)
            return 1
    else:
        jobs = pick_sample(jobs)

    chars = sum(len(j["text"]) for j in jobs)
    print(f"{len(jobs)} clip(s), {chars:,} characters\n")
    last = None
    for j in jobs:
        if (j["kind"], j["group"]) != last:
            print(f"  {j['kind']}/{j['group']}")
            last = (j["kind"], j["group"])
        print(f"    {j['id']:<16} {j['speaker']:<7} {len(j['text']):>5} ch  "
              f"{j['text'][:52]}")

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

    print()
    for j in jobs:
        dest = a.out / j["path"]
        # Resume. The first batch died at clip 62 of 109 on a guard that was
        # wrong; re-rendering the 62 would have paid for them twice.
        if dest.exists() and dest.stat().st_size > 1000:
            print(f"  {j['path']}  (already rendered)")
            continue
        print(f"  {j['path']}")
        render(j, a.out, token)
    return 0


if __name__ == "__main__":
    sys.exit(main())

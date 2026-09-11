#!/usr/bin/env python3
"""Generate the four bedtime-music tracks, the way generate_sounds.py
generates ambience — no licence chain, because the audio is not recorded.

WHY GENERATED AND NOT LICENSED
-------------------------------
`ContentSync:Music` shipped empty from the day the bedtime-music feature
(parent opt-in toggle, firmware playback, the Music dashboard view) was
built: everything existed except rights-cleared tracks. That is the exact
blocker `story-ambience/generate_sounds.py` solved for forest and river
sounds, solved the same way here — see that script's own docstring for the
full reasoning, which is not repeated here.

WHY INSTRUMENTAL ONLY, AND WHY NO REAL COMPOSER OR PERFORMER
---------------------------------------------------------------
Two owner constraints. No vocals, no lyrics, no singing, no spoken words —
a child hearing a sung line in an unfamiliar voice at bedtime is a
different product decision than ambient music, and this feature was never
asked to make it. And: folk-flavoured where a prompt can say so, but never
naming a real composer, performer, ensemble or existing piece — see
`backend/content/bedtime-music/tracks.json`'s `_rules.noRealWork` for the
closest miss armenian-story-master found on review (the bare word «Օրոր»
is also the title of the best-known Armenian classical lullaby). Both
instructions are appended to every prompt IN CODE (`build_prompt` below),
not repeated in the reviewed track text, so neither can be dropped by an
editing mistake to the JSON.

ON THE ENDPOINT SHAPE, HONESTLY
---------------------------------
This session's network egress is restricted to GitHub for tool calls made
through this agent (confirmed: a WebFetch to elevenlabs.io returned
EGRESS_BLOCKED), so the endpoint, request body and response shape below
could NOT be re-verified against ElevenLabs' current docs before this PR —
unlike generate_sounds.py's `/v1/sound-generation`, which an earlier
session did verify live. They reflect the Music API's documented shape as
of this assistant's last training data (a JSON POST to `/v1/music`,
`prompt` + `music_length_ms`, raw audio bytes back — the same "POST JSON,
get audio bytes back" shape sound-generation uses, not the alternate
`/v1/music/compose` two-step plan-then-render flow ElevenLabs also
documents). Treat ENDPOINT and the `generate()` request body as the one
part of this file that is UNVERIFIED. Before the first real
`--render --confirm-paid-api`, check https://elevenlabs.io/docs for the
current music-generation endpoint and parameter names and update this file
first — the same two-man rule as spending money applies to trusting an
unverified API shape with it.

ON THE LOUDNESS TARGET
------------------------
Narration sits at -16.4 LUFS (`Ship-StoryAudio.ps1`'s `$TargetLufs`) —
tuned for a voice that has to carry a story to a child across a room.
Bedtime music plays alone, with no voice riding on top of it, at the one
moment in the toy's day when "carry across the room" is the wrong goal —
the whole point is a track a settling child can let fade into the
background. -23 LUFS is used instead: the EBU R128 reference level for
programme material that is not the loud, dialogue-carrying element, and a
clearly audible ~6.6 LU quieter than narration without being so quiet a
track needs the volume knob cranked to be heard at all (the knob stays the
parent's/child's control for absolute level; this target only sets
relative gentleness). Documented here and in
`docs/bedtime-music-render-runbook.md` so the number is never a guess a
future re-render has to rediscover.

DRY RUN BY DEFAULT
--------------------
Prints every prompt and writes nothing. Same two-man rule as
tools/story-ambience/generate_sounds.py: spending money takes --render
--confirm-paid-api.

USAGE
    python3 tools/story-ambience/generate_music.py                 # all, dry
    python3 tools/story-ambience/generate_music.py --track calm-night
    ...  --render --confirm-paid-api
    python3 tools/story-ambience/generate_music.py --self-test     # no network

    ELEVENLABS_API_KEY must be set to render. ffmpeg and ffprobe must be on
    PATH for the post-processing pass (fades + loudnorm + encode) — that
    step does NOT run in --self-test, which checks the plan only.

ON TERMS, HONESTLY
--------------------
Generated audio has no licence FEE. That is not the same as "no question":
output ownership comes from the ElevenLabs plan's terms, not from the audio
being synthetic. This tool records the exact prompt beside every file, the
same posture `story-ambience/generate_sounds.py` and
`backend/content/bedtime-music/tracks.json` take.
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
TRACKS_PATH = REPO / "backend/content/bedtime-music/tracks.json"
MUSIC_ROOT = REPO / "backend/src/ArmenianAiToy.Api/story-audio/music"
PROMPTS_PATH = MUSIC_ROOT / "prompts.json"

# See "ON THE ENDPOINT SHAPE, HONESTLY" above — unverified against live docs
# this session.
ENDPOINT = "https://api.elevenlabs.io/v1/music"

# Requested window is the owner's brief; the actual generated length is not
# guaranteed to land exactly on request, which is why check_music_audio.py's
# gate (180-300s) has margin either side of these.
MIN_TRACK_SECONDS = 180.0
MAX_TRACK_SECONDS = 300.0

TARGET_LUFS = -23.0  # see "ON THE LOUDNESS TARGET" above
FADE_IN_SECONDS = 3.0
FADE_OUT_SECONDS = 8.0

# Appended to every prompt in code, never left to the reviewed JSON text —
# see the module docstring.
NEVER_VOCAL = (
    "Instrumental only: no vocals, no lyrics, no singing, no spoken words"
)
NEVER_REAL_WORK = (
    "an original, generic instrumental piece, not a recognisable real "
    "composer's or performer's work or a specific existing piece"
)


def load_tracks() -> dict:
    return json.loads(TRACKS_PATH.read_text(encoding="utf-8"))


def build_prompt(track: dict) -> str:
    """Reviewed text, then the two instructions the owner requires on every
    track, appended here so an edit to tracks.json cannot silently drop
    either one."""
    def sentence(t: str) -> str:
        t = t.strip().rstrip(".")
        return t[:1].upper() + t[1:]

    parts = [track["prompt"].rstrip(".")]
    parts.append(sentence(NEVER_VOCAL))
    parts.append(sentence(NEVER_REAL_WORK))
    return ". ".join(parts) + "."


def plan(doc: dict, only_track: str | None) -> list[dict]:
    out = []
    for track in doc["tracks"]:
        if only_track and track["id"] != only_track:
            continue
        if "prompt" not in track:
            raise SystemExit(f"track {track['id']!r} has no reviewed prompt")
        seconds = track["durationSeconds"]
        if not (MIN_TRACK_SECONDS <= seconds <= MAX_TRACK_SECONDS):
            raise SystemExit(
                f"track {track['id']!r} requests {seconds}s, outside the "
                f"{MIN_TRACK_SECONDS:.0f}-{MAX_TRACK_SECONDS:.0f}s bedtime window"
            )
        out.append({
            "id": track["id"],
            "title": track["title"],
            "seconds": seconds,
            "prompt": build_prompt(track),
            "raw_path": MUSIC_ROOT / f"{track['id']}-v1.raw.mp3",
            "path": MUSIC_ROOT / f"{track['id']}-v1.mp3",
        })
    if only_track and not out:
        raise SystemExit(f"no track named {only_track!r}")
    return out


def generate(job: dict, token: str) -> None:
    """POST the prompt, write the raw response. See the module docstring's
    "ON THE ENDPOINT SHAPE, HONESTLY" section before trusting this body
    against a real key."""
    body = {
        "prompt": job["prompt"],
        "music_length_ms": int(job["seconds"] * 1000),
    }
    job["raw_path"].parent.mkdir(parents=True, exist_ok=True)
    r = subprocess.run(
        ["curl", "-sS", "--max-time", "300", "-X", "POST",
         "-H", f"xi-api-key: {token}", "-H", "content-type: application/json",
         "--data-binary", "@-", "-o", str(job["raw_path"]), ENDPOINT],
        input=json.dumps(body).encode(), capture_output=True)
    # Same defect a failed sound-generation call has: an error body written
    # where audio was expected. Size is the cheapest tell.
    if not job["raw_path"].exists() or job["raw_path"].stat().st_size < 20000:
        detail = ""
        if job["raw_path"].exists():
            detail = job["raw_path"].read_text(errors="replace")[:300]
            job["raw_path"].unlink()
        raise SystemExit(f"generation failed for {job['id']}: "
                         f"{detail or r.stderr.decode()[:300]}")


def _run(args: list[str]) -> subprocess.CompletedProcess:
    return subprocess.run(args, capture_output=True, text=True)


def postprocess(job: dict) -> None:
    """Fade in 3s, fade out 8s, two-pass loudnorm to TARGET_LUFS, encode
    192 kbps mono MP3 with one ID3 tag — one ffmpeg pass, mirroring
    Ship-StoryAudio.ps1's Repair-And-Level (decode-to-PCM-and-back drops any
    stray tag the raw download carried, by construction). Mono, not stereo,
    to match the toy's single speaker and keep the SD footprint down, same
    choice the narration pipeline already makes.
    """
    src = job["raw_path"]
    dst = job["path"]

    probe = _run(["ffprobe", "-v", "error", "-show_entries", "format=duration",
                  "-of", "csv=p=0", str(src)])
    if probe.returncode != 0:
        raise SystemExit(f"ffprobe failed on {src}: {probe.stderr[:300]}")
    duration = float(probe.stdout.strip())
    fade_out_at = max(0.0, duration - FADE_OUT_SECONDS)

    # Pass 1: measure. One pass alone guesses the gain from a running
    # estimate and lands a decibel or two off — same reasoning as
    # Ship-StoryAudio.ps1's Repair-And-Level.
    measure_filter = f"loudnorm=I={TARGET_LUFS}:TP=-1.0:LRA=11:print_format=json"
    measured = _run(["ffmpeg", "-hide_banner", "-nostats", "-i", str(src),
                      "-af", measure_filter, "-f", "null", "-"])
    text = measured.stderr
    stats = json.loads(text[text.rindex("{"): text.rindex("}") + 1])

    # Pass 2: apply what was measured, plus the fades, in one filter chain.
    apply_filter = (
        f"afade=t=in:st=0:d={FADE_IN_SECONDS},"
        f"afade=t=out:st={fade_out_at:.3f}:d={FADE_OUT_SECONDS},"
        f"loudnorm=I={TARGET_LUFS}:TP=-1.0:LRA=11:"
        f"measured_I={stats['input_i']}:measured_TP={stats['input_tp']}:"
        f"measured_LRA={stats['input_lra']}:measured_thresh={stats['input_thresh']}:"
        f"offset={stats['target_offset']}:linear=true"
    )
    encode = _run(["ffmpeg", "-hide_banner", "-v", "error", "-y", "-i", str(src),
                   "-af", apply_filter, "-ar", "44100", "-ac", "1",
                   "-c:a", "libmp3lame", "-b:a", "192k",
                   "-id3v2_version", "3", "-write_id3v1", "0",
                   "-metadata", f"title={job['title']}",
                   "-metadata", "artist=Areg",
                   str(dst)])
    if encode.returncode != 0:
        raise SystemExit(f"ffmpeg encode failed on {job['id']}: {encode.stderr[:500]}")
    src.unlink()


def self_test() -> int:
    ok = True

    def check(name, got, want):
        nonlocal ok
        if got != want:
            print(f"  FAIL {name}: got {got!r}, wanted {want!r}")
            ok = False
        else:
            print(f"  ok   {name}")

    doc = load_tracks()
    jobs = plan(doc, None)
    check("four tracks", len(jobs), 4)
    check("every track id is unique", len({j["id"] for j in jobs}), len(jobs))

    for j in jobs:
        check(f"{j['id']}: duration inside the bedtime window",
              MIN_TRACK_SECONDS <= j["seconds"] <= MAX_TRACK_SECONDS, True)
        check(f"{j['id']}: instrumental instruction reaches the prompt",
              "no vocals" in j["prompt"].lower(), True)
        check(f"{j['id']}: no-real-work instruction reaches the prompt",
              "not a recognisable real" in j["prompt"].lower(), True)
        check(f"{j['id']}: has a non-empty Armenian title", len(j["title"]) > 0, True)

    print("PASS" if ok else "FAIL")
    return 0 if ok else 1


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--track", help="only this track id")
    ap.add_argument("--render", action="store_true")
    ap.add_argument("--confirm-paid-api", action="store_true")
    ap.add_argument("--force", action="store_true",
                    help="regenerate even where a file already exists")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()

    if a.self_test:
        return self_test()

    doc = load_tracks()
    jobs = plan(doc, a.track)

    todo = [j for j in jobs if a.force or not j["path"].exists()]
    print(f"{len(jobs)} track(s), {len(todo)} to generate\n")
    for j in jobs:
        state = "exists" if j not in todo else "GENERATE"
        rel = j["path"].relative_to(REPO)
        print(f"[{state:8}] {rel}   ({j['seconds']:.0f}s, target {TARGET_LUFS} LUFS)")
        print(f"           {j['prompt']}\n")

    if not a.render:
        print("DRY RUN — nothing written. Pass --render --confirm-paid-api.")
        return 0
    if not a.confirm_paid_api:
        print("--render needs --confirm-paid-api: this spends money.", file=sys.stderr)
        return 1
    token = os.environ.get("ELEVENLABS_API_KEY")
    if not token:
        print("set ELEVENLABS_API_KEY", file=sys.stderr)
        return 1
    for tool in ("ffmpeg", "ffprobe"):
        if subprocess.run(["which", tool], capture_output=True).returncode != 0:
            print(f"{tool} not found on PATH — required for post-processing", file=sys.stderr)
            return 1

    rows = []
    for j in todo:
        generate(j, token)
        postprocess(j)
        size = j["path"].stat().st_size
        import hashlib
        sha = hashlib.sha256(j["path"].read_bytes()).hexdigest()
        print(f"wrote {j['path'].relative_to(REPO)}  {size:,} bytes  sha256={sha}")
        rows.append({
            "TrackId": j["id"], "Version": 1, "Title": j["title"],
            "AudioPath": f"music/{j['id']}-v1.mp3",
            "SizeBytes": size, "Sha256": sha,
        })

    if rows:
        print("\nPaste into appsettings.json's ContentSync:Music, replacing "
              "the matching placeholder row(s):\n")
        print(json.dumps(rows, ensure_ascii=False, indent=2))

        # Beside the audio, what was asked for — mirrors
        # story-ambience/sounds/<storyId>/prompts.json.
        existing = {}
        if PROMPTS_PATH.exists():
            existing = json.loads(PROMPTS_PATH.read_text(encoding="utf-8"))
        for j in jobs:
            if j["path"].exists():
                existing[j["id"]] = {
                    "prompt": j["prompt"],
                    "durationSecondsRequested": j["seconds"],
                    "targetLufs": TARGET_LUFS,
                }
        PROMPTS_PATH.parent.mkdir(parents=True, exist_ok=True)
        PROMPTS_PATH.write_text(
            json.dumps(existing, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(f"\nwrote {PROMPTS_PATH.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

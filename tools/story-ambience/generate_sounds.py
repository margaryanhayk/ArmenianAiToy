#!/usr/bin/env python3
"""Generate the ambience a story asks for, one place at a time.

WHY GENERATED AND NOT RECORDED
------------------------------
18 sounds were sourced from Wikimedia Commons and auditioned (they are still
listed in ../../backend/content/story-ambience/sound-candidates.md, and remain
the fallback). The owner preferred generated ones on listening, and they remove
the licence question from the product's audio chain entirely — see the note on
terms at the bottom of this docstring, which is a real question and not a
settled one.

WHY THE UNIT IS (STORY, SOUND) AND NOT SOUND
--------------------------------------------
The owner's instruction: "I don't want to listen to much repeated voices —
there can be different voice per situation." One `forest-day` file dropped into
every story that names a forest is exactly the tape-loop effect he rejected. So
each STORY gets its own forest, its own wind, its own river: 18 sound ids used
in 29 cues resolve to 26 distinct (story, sound) pairs.

But WITHIN one story a repeated sound must stay the SAME file, because three
cue notes say so and they are right:

  ulik           the wolf's knock and the mother's knock — "the door sounds
                 identical and only the voice differs", which is the whole
                 thing the child is meant to work out
  anban-huri     "She is back at the same river."
  sutlik-orskan  the same journey, continuing across two segments

Keying on the pair gives both properties at once and needs no new field in the
cue sheet.

THE PROMPT IS REVIEWED TEXT IN THE CUE SHEET, NOT DERIVED HERE
---------------------------------------------------------------
First attempt built the prompt by concatenating each sound's `description` with
the `note` of every cue using it. That was wrong, and the dry run showed why: a
note explains to a HUMAN where a cue goes and why, so «Ուլիկը»'s forest was
asked for with "anchored to the START of the segment, not its end, because the
end of segment 1 IS the start of segment 2" and the knock with "the SAME knock,
at the same level, as the wolf's. That is deliberate". Placement rationale is
not a description of a sound.

So `sounds[].prompt` in the cue sheet is reviewed English, one per sound, and
that is what is sent. `cues[].avoid` appends what must be ABSENT — no frogs in
the river of «Անբան Հուռին», because the narrator voices the frogs himself and
a real one would talk over the joke; no wolf howl anywhere in «Ուլիկը», because
the menace is the voice. The `note` field is never sent.

DRY RUN BY DEFAULT
------------------
Prints every prompt and writes nothing. Same two-man rule as
tools/ElevenLabsRender and tools/story-audio/mix_ambience.py: spending money
takes --render --confirm-paid-api.

USAGE
    python3 tools/story-ambience/generate_sounds.py                 # all, dry
    python3 tools/story-ambience/generate_sounds.py --story ulik
    ...  --render --confirm-paid-api
    python3 tools/story-ambience/generate_sounds.py --self-test     # no network

    ELEVENLABS_API_KEY must be set to render.

ON TERMS, HONESTLY
------------------
Generated audio has no licence FEE. That is not the same as "no question":
output ownership comes from the ElevenLabs plan's terms, not from the audio
being synthetic. This tool records the exact prompt beside every file so the
provenance of each sound is answerable, in the same way the Commons candidates
record author and licence URL.
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
CUES_PATH = REPO / "backend/content/story-ambience/ambience-cues.json"
SOUNDS_ROOT = REPO / "backend/content/story-ambience/sounds"

# Long enough that a holdUnder bed loops without the loop being audible, short
# enough not to pay for audio no cue is long enough to use. The longest bed in
# the library is about 40s, and a ~10s source repeats four times under a
# narration that is already the loudest thing — inaudible. The API ceiling is
# 22s; going there would quadruple the cost for nothing.
BED_SECONDS = 10.0
# A knock, a splash, a gust. Long enough to hold the event and its tail, short
# enough that the generator does not fill the rest with something else — which
# is what a 10-second "three knocks" request invites.
ONESHOT_SECONDS = 4.0
# 0 lets the model interpret freely, 1 pins it to the words. These prompts are
# specific about what must be absent, so lean towards the words.
PROMPT_INFLUENCE = 0.45
ENDPOINT = "https://api.elevenlabs.io/v1/sound-generation"


def load_cues() -> dict:
    return json.loads(CUES_PATH.read_text(encoding="utf-8"))


# A bed and a one-shot want opposite things from the generator: one must keep
# going evenly so it can be looped, the other must happen once and stop.
TAIL = {
    "scene":   "Continuous even field recording that could loop",
    "oneshot": "A single event with silence after it",
}
# Asked for on everything. A generator that slips a swell of strings or a hum
# of voices under a story is not ambience, it is a second narrator.
NEVER = "no music, no melody, no speech, no singing"


def build_prompt(sound: dict, kind: str, avoid: list[str]) -> str:
    """Reviewed text, then the exclusions, then the shape.

    Deliberately carries no level and no timing: the mixer owns those, and
    asking a generator for "quiet" returns a quiet recording that then has to
    be turned UP, which is a worse signal than a normal one turned down.
    """
    def sentence(t):
        t = t.strip().rstrip(".")
        return t[:1].upper() + t[1:]

    parts = [sound["prompt"].rstrip(".")]
    parts += [sentence(a) for a in avoid]
    parts.append(sentence(NEVER))
    parts.append(TAIL.get(kind, TAIL["scene"]))
    return ". ".join(parts) + "."


def plan(doc: dict, only_story: str | None) -> list[dict]:
    """One job per (story, sound) pair, carrying every exclusion it earned."""
    by_id = {s["id"]: s for s in doc["sounds"]}
    jobs: dict[tuple[str, str], dict] = {}
    for story in doc["stories"]:
        sid = story["storyId"]
        if only_story and sid != only_story:
            continue
        for cue in story["cues"]:
            key = (sid, cue["sound"])
            job = jobs.setdefault(key, {
                "storyId": sid, "sound": cue["sound"],
                "avoid": [], "cues": 0, "kind": cue["kind"],
            })
            job["cues"] += 1
            # A shared file must satisfy EVERY cue that uses it: sutlik-orskan
            # walks the same road twice and the second time the line being
            # spoken is that the lakes have no water in them, so "no water"
            # binds the one file both cues share.
            a = cue.get("avoid", "").strip()
            if a and a not in job["avoid"]:
                job["avoid"].append(a)
            # A one-shot shared with a scene must not loop. Safer shape wins.
            if cue["kind"] == "oneshot":
                job["kind"] = "oneshot"
    out = []
    for (sid, sound), job in sorted(jobs.items()):
        if sound not in by_id:
            raise SystemExit(f"cue in {sid} names unknown sound {sound!r}")
        if "prompt" not in by_id[sound]:
            raise SystemExit(f"sound {sound!r} has no reviewed prompt")
        job["prompt"] = build_prompt(by_id[sound], job["kind"], job["avoid"])
        job["seconds"] = ONESHOT_SECONDS if job["kind"] == "oneshot" else BED_SECONDS
        job["path"] = SOUNDS_ROOT / sid / f"{sound}.mp3"
        out.append(job)
    return out


def generate(job: dict, token: str) -> None:
    body = {
        "text": job["prompt"],
        "duration_seconds": job["seconds"],
        "prompt_influence": PROMPT_INFLUENCE,
    }
    job["path"].parent.mkdir(parents=True, exist_ok=True)
    r = subprocess.run(
        ["curl", "-sS", "--max-time", "180", "-X", "POST",
         "-H", f"xi-api-key: {token}", "-H", "content-type: application/json",
         "--data-binary", "@-", "-o", str(job["path"]), ENDPOINT],
        input=json.dumps(body).encode(), capture_output=True)
    # A failed call still writes a file — the JSON error body. Size is the
    # cheapest way to tell an MP3 from an apology, and getting this wrong
    # would put a text file where the mixer expects audio.
    if not job["path"].exists() or job["path"].stat().st_size < 2000:
        detail = ""
        if job["path"].exists():
            detail = job["path"].read_text(errors="replace")[:300]
            job["path"].unlink()
        raise SystemExit(f"generation failed for {job['storyId']}/{job['sound']}: "
                         f"{detail or r.stderr.decode()[:300]}")


def self_test() -> int:
    ok = True

    def check(name, got, want):
        nonlocal ok
        if got != want:
            print(f"  FAIL {name}: got {got!r}, wanted {want!r}")
            ok = False
        else:
            print(f"  ok   {name}")

    doc = load_cues()
    jobs = plan(doc, None)
    check("one job per (story, sound) pair", len(jobs), 26)

    # The three places where a story reuses a sound must resolve to ONE job,
    # or the wolf's door and the mother's door stop being the same door.
    shared = {(j["storyId"], j["sound"]): j["cues"] for j in jobs if j["cues"] > 1}
    check("ulik's two knocks share a file", shared.get(("ulik", "door-knock")), 2)
    check("anban-huri returns to the same river",
          shared.get(("anban-huri", "river-shallow")), 2)
    check("sutlik-orskan keeps one road",
          shared.get(("sutlik-orskan", "open-country-wind")), 2)
    check("nothing else is shared", len(shared), 3)

    # Different stories must NOT share a file, which is the owner's whole point.
    forests = [j for j in jobs if j["sound"] == "forest-day"]
    check("forest-day is generated twice, once per story", len(forests), 2)
    check("and lands in two different folders",
          len({j["path"].parent.name for j in forests}), 2)

    ulik_knock = next(j for j in jobs if j["storyId"] == "ulik"
                      and j["sound"] == "door-knock")
    def says(job, phrase):
        return phrase.lower() in job["prompt"].lower()

    check("the exclusion reaches the prompt", says(ulik_knock, "howl"), True)
    check("no speech is always asked for", says(ulik_knock, "no speech"), True)
    check("a knock is a single event", says(ulik_knock, "single event"), True)
    check("and is not asked to loop", says(ulik_knock, "loop"), False)
    check("a knock is short", ulik_knock["seconds"], ONESHOT_SECONDS)

    huri_river = next(j for j in jobs if j["storyId"] == "anban-huri"
                      and j["sound"] == "river-shallow")
    check("no frogs in Huri's river", says(huri_river, "frog"), True)
    check("a bed is asked to loop", says(huri_river, "loop"), True)

    # The road in sutlik-orskan is ONE file used by two cues, and only the
    # second cue says "no water". If the exclusions did not merge, the shared
    # file could lap against a lake the story insists is empty.
    road = next(j for j in jobs if j["storyId"] == "sutlik-orskan"
                and j["sound"] == "open-country-wind")
    check("a shared file honours every cue's exclusion",
          says(road, "no water"), True)

    # Placement rationale must never reach the generator again.
    for j in jobs:
        if "segment" in j["prompt"].lower() or "cue" in j["prompt"].lower():
            check(f"no rationale in {j['storyId']}/{j['sound']}", j["prompt"], "<clean>")

    print("PASS" if ok else "FAIL")
    return 0 if ok else 1


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--story", help="only this story id")
    ap.add_argument("--render", action="store_true")
    ap.add_argument("--confirm-paid-api", action="store_true")
    ap.add_argument("--force", action="store_true",
                    help="regenerate even where a file already exists")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()

    if a.self_test:
        return self_test()

    doc = load_cues()
    jobs = plan(doc, a.story)
    if not jobs:
        print(f"no cues for {a.story!r}", file=sys.stderr)
        return 1

    todo = [j for j in jobs if a.force or not j["path"].exists()]
    print(f"{len(jobs)} (story, sound) pair(s), {len(todo)} to generate\n")
    for j in jobs:
        state = "exists" if j["path"] not in [t["path"] for t in todo] else "GENERATE"
        rel = j["path"].relative_to(REPO)
        print(f"[{state:8}] {rel}   ({j['cues']} cue"
              f"{'s' if j['cues'] > 1 else ''}, {j['kind']}, {j['seconds']:.0f}s)")
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

    for j in todo:
        generate(j, token)
        print(f"wrote {j['path'].relative_to(REPO)}  "
              f"{j['path'].stat().st_size:,} bytes")

    # Beside the audio, what was asked for. Generation is non-deterministic, so
    # the file cannot be re-derived — but the question "why does this forest
    # sound like this" must stay answerable.
    if todo:
        for sid in sorted({j["storyId"] for j in todo}):
            d = SOUNDS_ROOT / sid
            existing = {}
            pf = d / "prompts.json"
            if pf.exists():
                existing = json.loads(pf.read_text(encoding="utf-8"))
            for j in jobs:
                if j["storyId"] == sid and j["path"].exists():
                    existing[j["sound"]] = {
                        "prompt": j["prompt"],
                        "durationSeconds": j["seconds"],
                        "promptInfluence": PROMPT_INFLUENCE,
                        "cuesUsingIt": j["cues"],
                    }
            pf.write_text(json.dumps(existing, ensure_ascii=False, indent=2) + "\n",
                          encoding="utf-8")
            print(f"wrote {pf.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

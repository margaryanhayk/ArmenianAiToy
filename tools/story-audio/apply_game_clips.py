#!/usr/bin/env python3
"""Install re-rendered game clips and patch ContentSync:Games.

WHY A SECOND ONE OF THESE
-------------------------
`apply_voice_clips.py` does exactly this job for the 43 device-global welcome
clips and explains, at length, why doing it by hand is not a gate: a wrong
sha256 makes the toy download a clip, fail verification and refuse it forever
with no parent-visible sign; a forgotten Version bump makes the toy keep the
old audio and never learn there is a new one.

Games need the same and could not reuse it, because their identity is a PAIR.
Four of the five games ship a clip called `intro`, so `ClipId` alone is
ambiguous and a keyed-by-id patcher would quietly write the mind-reader's intro
over the buzzer's. The JSON surgery is shared with that script; only the key
and the section differ.

WHAT IT LEAVES ALONE
--------------------
`button-simon/tone-green` and `tone-red` are the two NON-VERBAL clips — pure
tones, no words, so they are not in `game-clips.json` and were never
re-rendered. They stay in the manifest untouched. 92 entries, 90 of them
speech.

It refuses a partial job by default, for the reason the voice shipper gives:
half a set means the toy plays the new performance on one clip and the old one
on the next, inside a single game.

USAGE
    python3 tools/story-audio/apply_game_clips.py --in <render-dir>/games
    python3 tools/story-audio/apply_game_clips.py --in <dir> --apply
    python3 tools/story-audio/apply_game_clips.py --self-test

DRY RUN BY DEFAULT. Pure Python: no ffmpeg, no dotnet. The human listen test
is still the last gate, and these have never been heard in sequence.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from apply_voice_clips import _find_array, _split_objects, _set_field, inspect  # noqa: E402

# Not speech, never rendered from text, must survive untouched.
NON_VERBAL = {("button-simon", "tone-green"), ("button-simon", "tone-red")}


def patch_games(text: str, updates: dict[tuple[str, str], dict]) -> str:
    """Rewrite Sha256 / SizeBytes / Version for the named (game, clip) pairs.

    Text surgery rather than reserializing appsettings.json: it is a
    hand-maintained file of over a thousand lines and a reserialize would
    rewrite every one of them, burying the three numbers that actually changed
    in a diff nobody can review.
    """
    start, end = _find_array(text, "ContentSync", "Games")
    array = text[start:end]
    out, last, seen = [], 0, set()
    for a, b in _split_objects(array):
        obj = array[a:b]
        key = (json.loads(obj)["GameKey"], json.loads(obj)["ClipId"])
        if key in updates:
            u = updates[key]
            new = obj
            new = _set_field(new, "Sha256", u["sha256"])
            new = _set_field(new, "SizeBytes", u["size"])
            new = _set_field(new, "Version", u["version"])
            out.append(array[last:a] + new)
            last = b
            seen.add(key)
    missing = set(updates) - seen
    if missing:
        raise SystemExit(f"no manifest entry for: {sorted(missing)}")
    out.append(array[last:])
    return text[:start] + "".join(out) + text[end:]


def add_games(text: str, additions: dict[tuple[str, str], dict]) -> str:
    """Append manifest entries for clips that have never shipped.

    New clips have no entry to patch, so patch_games would refuse them. They
    go in at Version 1 — a first version, not a bump — and in the same field
    order as the entries already there, because that order is what
    Ship-StoryAudio.ps1's regex family depends on elsewhere in this file.
    """
    if not additions:
        return text
    start, end = _find_array(text, "ContentSync", "Games")
    array = text[start:end]
    spans = _split_objects(array)
    if not spans:
        raise SystemExit("ContentSync:Games is empty; nothing to match indentation to")
    last_a, last_b = spans[-1]
    indent = array[:last_a].rsplit("\n", 1)[-1]
    blocks = []
    for (game, clip), u in sorted(additions.items()):
        blocks.append(indent + json.dumps({
            "GameKey": game, "ClipId": clip, "Version": 1,
            "AudioPath": f"games/{game}/{clip}.mp3",
            "Sha256": u["sha256"], "SizeBytes": u["size"],
        }, indent=2).replace("\n", "\n" + indent))
    inserted = array[:last_b] + ",\n" + ",\n".join(blocks) + array[last_b:]
    return text[:start] + inserted + text[end:]


def clip_texts() -> dict[tuple[str, str], str]:
    d = json.loads((REPO / "backend/content/offline-games/game-clips.json")
                   .read_text(encoding="utf-8"))
    return {(g, c["id"]): c["text"]
            for g, v in d.items() if not g.startswith("_")
            for c in v["clips"] if not c.get("new")}


def build_plan(render_dir: Path, api_dir: Path, partial: bool) -> dict:
    texts = clip_texts()
    settings = api_dir / "appsettings.json"
    cfg = json.loads(settings.read_text(encoding="utf-8-sig"))
    entries = {(g["GameKey"], g["ClipId"]): g for g in cfg["ContentSync"]["Games"]}

    rows, updates = [], {}
    for key in sorted(texts):
        game, clip = key
        src = render_dir / game / f"{clip}.mp3"
        if not src.exists():
            rows.append((key, "not rendered", None))
            continue
        data = src.read_bytes()
        info = inspect(src, texts[key])
        if info.get("problem"):
            rows.append((key, info["problem"], None))
            continue
        known = key in entries
        sha = hashlib.sha256(data).hexdigest()
        # Identical bytes must not get a new Version. A bump is an instruction
        # to every toy in the field to download the file again; issuing one for
        # audio that did not change costs a needless sync and tells a later
        # reader the clip was re-recorded when it was not. Re-running this
        # script is normal, so it has to be idempotent — the first run bumped
        # 90 unchanged clips from v2 to v3.
        if known and entries[key]["Sha256"].lower() == sha and \
                int(entries[key]["SizeBytes"]) == len(data):
            rows.append((key, "unchanged", None))
            continue
        rel = (entries[key]["AudioPath"] if known
               else f"games/{game}/{clip}.mp3")
        updates[key] = {
            "src": src,
            "dest": api_dir / "story-audio" / rel,
            "sha256": sha,
            "size": len(data),
            # A clip that has never shipped starts at 1. Bumping a version
            # that does not exist would advertise a second copy of nothing.
            "version": int(entries[key]["Version"]) + 1 if known else 1,
            "new": not known,
        }
        rows.append((key, "install" if known else "add", updates[key]))

    untouched = [k for k in entries if k not in texts]
    return {"rows": rows, "updates": updates, "settings": settings,
            "untouched": untouched, "partial": partial,
            "total": len(texts)}


def apply_plan(plan: dict) -> None:
    unchanged = sum(1 for _k, st, _u in plan["rows"] if st == "unchanged")
    if not plan["partial"] and len(plan["updates"]) + unchanged != plan["total"]:
        raise SystemExit(
            f"only {len(plan['updates'])} of {plan['total']} clips are ready. "
            f"Half a set means the toy plays the new performance on one clip "
            f"and the old one on the next, inside a single game. Pass "
            f"--partial if you really mean to.")
    for key, u in plan["updates"].items():
        u["dest"].parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(u["src"], u["dest"])
    text = plan["settings"].read_text(encoding="utf-8-sig")
    fresh = {k: v for k, v in plan["updates"].items() if v["new"]}
    known = {k: v for k, v in plan["updates"].items() if not v["new"]}
    text = patch_games(text, known)
    text = add_games(text, fresh)
    plan["settings"].write_text(text, encoding="utf-8")


def report(plan: dict) -> int:
    bad = 0
    added = sum(1 for _k, s, _u in plan["rows"] if s == "add")
    if added:
        print(f"  {added} clip(s) have never shipped and get a new entry at "
              f"Version 1")
    same = sum(1 for _k, st, _u in plan["rows"] if st == "unchanged")
    if same:
        print(f"  {same} clip(s) already ship these exact bytes — left alone")
    for key, state, _u in plan["rows"]:
        if state not in ("install", "add", "unchanged"):
            print(f"  {key[0]}/{key[1]:<16} {state}")
            bad += 1
    print(f"\n{len(plan['updates'])} of {plan['total']} speech clips to write; "
          f"{len(plan['untouched'])} non-verbal entries left untouched "
          f"({', '.join(f'{a}/{b}' for a, b in sorted(plan['untouched'])) or 'none'})")
    return bad


def self_test() -> int:
    ok = True

    def check(name, got, want):
        nonlocal ok
        if got != want:
            print(f"  FAIL {name}: got {got!r}, wanted {want!r}")
            ok = False
        else:
            print(f"  ok   {name}")

    texts = clip_texts()
    check("every speech clip is known", len(texts), 102)   # 90 + the 12 kid lines
    raw = json.loads((REPO / "backend/content/offline-games/game-clips.json")
                     .read_text(encoding="utf-8"))
    pending = {(g, c["id"]) for g, v in raw.items() if not g.startswith("_")
               for c in v["clips"] if c.get("new")}
    check("nothing still marked new is shipped", set(texts) & pending, set())

    cfg = json.loads((REPO / "backend/src/ArmenianAiToy.Api/appsettings.json")
                     .read_text(encoding="utf-8-sig"))
    entries = {(g["GameKey"], g["ClipId"]) for g in cfg["ContentSync"]["Games"]}
    # Anything the manifest does not cover must be a clip that has never
    # shipped — never a typo'd id, which would silently ship nothing.
    uncovered = set(texts) - entries
    check("every uncovered clip is one of the new kid lines",
          {k for k in uncovered if not k[1].startswith("kid-")}, set())
    check("and carries exactly the two non-verbal extras",
          entries - set(texts), NON_VERBAL)

    # The reason this file exists rather than reusing the voice shipper: four
    # games ship a clip called `intro`, so an id-keyed patcher would collide.
    intros = [k for k in texts if k[1] == "intro"]
    check("more than one game has an `intro`", len(intros) > 1, True)

    # Patching must touch only the named pair.
    sample = json.dumps({"ContentSync": {"Games": [
        {"GameKey": "a", "ClipId": "intro", "Version": 1, "AudioPath": "x",
         "Sha256": "0" * 64, "SizeBytes": 1},
        {"GameKey": "b", "ClipId": "intro", "Version": 1, "AudioPath": "y",
         "Sha256": "0" * 64, "SizeBytes": 1}]}}, indent=2)
    out = patch_games(sample, {("b", "intro"): {"sha256": "f" * 64, "size": 99,
                                                "version": 2}})
    got = json.loads(out)["ContentSync"]["Games"]
    check("the other game's intro is untouched", got[0]["Version"], 1)
    check("the named one is patched", (got[1]["Version"], got[1]["SizeBytes"]),
          (2, 99))

    # A clip with no entry is ADDED at Version 1, not bumped — there is no
    # previous version to bump, and advertising v2 of something that never
    # shipped tells every toy it has a stale copy it does not have.
    out2 = add_games(sample, {("c", "cheer"): {"sha256": "a" * 64, "size": 7}})
    got2 = json.loads(out2)["ContentSync"]["Games"]
    check("the new entry is appended", len(got2), 3)
    check("at version one", got2[2]["Version"], 1)
    check("with the path the renderer wrote",
          got2[2]["AudioPath"], "games/c/cheer.mp3")
    check("and the existing entries are untouched",
          [g["Version"] for g in got2[:2]], [1, 1])

    print("PASS" if ok else "FAIL")
    return 0 if ok else 1


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--in", dest="render_dir", type=Path)
    ap.add_argument("--api-dir", type=Path,
                    default=REPO / "backend/src/ArmenianAiToy.Api")
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--partial", action="store_true")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args(argv)

    if a.self_test:
        return self_test()
    if not a.render_dir:
        ap.error("--in is required")

    plan = build_plan(a.render_dir, a.api_dir, a.partial)
    bad = report(plan)
    if not a.apply:
        print("\nDry run. Add --apply to write.")
        return 1 if bad and not a.partial else 0
    apply_plan(plan)
    print(f"\nInstalled {len(plan['updates'])} clips and patched appsettings.json.")
    print("Nobody has heard these in sequence - the games have never been "
          "bench-run. That is still the last gate.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

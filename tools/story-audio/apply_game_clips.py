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
        if key not in entries:
            rows.append((key, "no manifest entry", None))
            continue
        if not src.exists():
            rows.append((key, "not rendered", None))
            continue
        data = src.read_bytes()
        info = inspect(src, texts[key])
        if info.get("problem"):
            rows.append((key, info["problem"], None))
            continue
        updates[key] = {
            "src": src,
            "dest": api_dir / "story-audio" / entries[key]["AudioPath"],
            "sha256": hashlib.sha256(data).hexdigest(),
            "size": len(data),
            "version": int(entries[key]["Version"]) + 1,
        }
        rows.append((key, "install", updates[key]))

    untouched = [k for k in entries if k not in texts]
    return {"rows": rows, "updates": updates, "settings": settings,
            "untouched": untouched, "partial": partial,
            "total": len(texts)}


def apply_plan(plan: dict) -> None:
    if not plan["partial"] and len(plan["updates"]) != plan["total"]:
        raise SystemExit(
            f"only {len(plan['updates'])} of {plan['total']} clips are ready. "
            f"Half a set means the toy plays the new performance on one clip "
            f"and the old one on the next, inside a single game. Pass "
            f"--partial if you really mean to.")
    for key, u in plan["updates"].items():
        u["dest"].parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(u["src"], u["dest"])
    text = plan["settings"].read_text(encoding="utf-8-sig")
    plan["settings"].write_text(patch_games(text, plan["updates"]), encoding="utf-8")


def report(plan: dict) -> int:
    bad = 0
    for key, state, _u in plan["rows"]:
        if state != "install":
            print(f"  {key[0]}/{key[1]:<16} {state}")
            bad += 1
    print(f"\n{len(plan['updates'])} of {plan['total']} speech clips ready; "
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
    check("every speech clip is known", len(texts), 90)
    check("pending lines are excluded",
          any(k[1].startswith("kid-") for k in texts), False)

    cfg = json.loads((REPO / "backend/src/ArmenianAiToy.Api/appsettings.json")
                     .read_text(encoding="utf-8-sig"))
    entries = {(g["GameKey"], g["ClipId"]) for g in cfg["ContentSync"]["Games"]}
    check("the manifest covers every speech clip", set(texts) - entries, set())
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

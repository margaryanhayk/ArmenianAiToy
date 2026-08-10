#!/usr/bin/env python3
"""Install re-rendered welcome-flow voice clips and patch ContentSync:Voice.

WHY THIS EXISTS
---------------
Ship-StoryAudio.ps1 only patches ContentSync:Stories. The 43 device-global
welcome clips (greetings, the "what shall we do?" lines, say-again) have no
shipper at all, so re-rendering them in a new voice means 43 hand edits to
appsettings.json, each needing a correct sha256, a correct sizeBytes and a
bumped Version.

Every one of those is silent when wrong:

  wrong sha256    the toy downloads the clip, fails verification, and refuses
                  it - forever, on every boot, with no parent-visible sign
  wrong sizeBytes same
  forgotten bump  the toy keeps the OLD clip and never learns there is a new
                  one, so the child is greeted in the previous voice

That is exactly the failure class the 2026-08-10 review was about: a gate that
existed on paper and was skipped in practice. 43 chances to make it by hand is
not a gate.

WHAT IT DOES
------------
Reads the rendered clips, recomputes sha256 and size FROM THE FILES THEMSELVES
(never from manifest-snippet.json - the bytes that ship are the bytes that must
be hashed), checks each clip is a complete MP3, copies them in, and patches
only the matching ContentSync:Voice entries.

It refuses to do a partial job. A half-updated set means the toy greets the
child in the new voice on one boot and the old voice on the next, which is
worse than not shipping at all.

USAGE
    python3 tools/story-audio/apply_voice_clips.py --in <render-dir>
    python3 tools/story-audio/apply_voice_clips.py --in <render-dir> --apply
    python3 tools/story-audio/apply_voice_clips.py --self-test

DRY RUN BY DEFAULT - prints the exact change it would make and writes nothing.
Pure Python: no ffmpeg, no dotnet. Loudness and the story library stay with
Ship-StoryAudio.ps1. The human listen test is still the last gate.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from check_story_audio import CHARS_PER_SECOND, SHORT_FLOOR, scan  # noqa: E402

# Renders that are NOT device-global voice clips. ElevenLabsRender writes these
# into the same folder when it is run for stories, and they belong to
# ContentSync:Stories[].Clips, not here - so seeing them is normal, not a fault.
_NOT_VOICE_CLIP = re.compile(
    r"--(narration|intro|question|summary|offer|reoffer)(--|\.|$)"
)


# ---------------------------------------------------------------- json surgery

def _find_array(text: str, section: str, key: str) -> tuple[int, int]:
    """Byte span of the `key` array inside `section`, brace-matched.

    Text surgery rather than json.dumps of the whole file on purpose:
    appsettings.json is a hand-maintained 1000-line file and a reserialize
    would rewrite every line of it, burying the four numbers that actually
    changed in a diff nobody can review.
    """
    at = text.index(f'"{section}"')
    start = text.index("[", text.index(f'"{key}"', at))
    depth = 0
    for i in range(start, len(text)):
        if text[i] == "[":
            depth += 1
        elif text[i] == "]":
            depth -= 1
            if depth == 0:
                return start, i + 1
    raise ValueError(f'unterminated "{key}" array')


def _split_objects(array_text: str) -> list[tuple[int, int]]:
    """Spans of each top-level {...} inside an array's text."""
    spans, depth, start, in_str, esc = [], 0, None, False, False
    for i, ch in enumerate(array_text):
        if in_str:
            if esc:
                esc = False
            elif ch == "\\":
                esc = True
            elif ch == '"':
                in_str = False
            continue
        if ch == '"':
            in_str = True
        elif ch == "{":
            if depth == 0:
                start = i
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                spans.append((start, i + 1))
    return spans


def _set_field(obj_text: str, field: str, value) -> str:
    """Replace one scalar field inside a single JSON object's text."""
    rendered = json.dumps(value)
    patched, n = re.subn(
        r'("' + re.escape(field) + r'"\s*:\s*)(?:"[^"]*"|-?\d+)',
        lambda m: m.group(1) + rendered,
        obj_text,
        count=1,
    )
    if n != 1:
        raise ValueError(f'field "{field}" not found in entry')
    return patched


def patch_voice(text: str, updates: dict[str, dict]) -> str:
    """Return appsettings text with the given VoiceIds' three fields updated."""
    lo, hi = _find_array(text, "ContentSync", "Voice")
    array = text[lo:hi]
    out, seen = array, set()
    # Right to left so earlier spans stay valid as the text changes length.
    for start, end in reversed(_split_objects(array)):
        entry = array[start:end]
        found = re.search(r'"VoiceId"\s*:\s*"([^"]+)"', entry)
        if not found or found.group(1) not in updates:
            continue
        voice_id = found.group(1)
        seen.add(voice_id)
        new = updates[voice_id]
        entry = _set_field(entry, "Version", new["version"])
        entry = _set_field(entry, "Sha256", new["sha256"])
        entry = _set_field(entry, "SizeBytes", new["size"])
        out = out[:start] + entry + out[end:]
    missing = set(updates) - seen
    if missing:
        raise ValueError(f"no ContentSync:Voice entry for: {', '.join(sorted(missing))}")
    return text[:lo] + out + text[hi:]


# ------------------------------------------------------------------ inspection

def clip_texts(repo: Path) -> dict[str, str]:
    """voiceId -> the Armenian line it is supposed to say, if the source exists."""
    source = repo / "backend/content/voice-clips/voice-clips.json"
    if not source.exists():
        return {}
    data = json.loads(source.read_text(encoding="utf-8"))
    return {
        c["voiceId"]: c.get("text", "")
        for c in data.get("clips", [])
        if c.get("voiceId")
    }


def inspect(path: Path, text: str | None) -> dict:
    """sha256, size and the two structural defects, for one rendered clip."""
    raw = path.read_bytes()
    info = scan(path)
    problems = []
    if not raw:
        problems.append("empty file")
    elif info["frames"] == 0:
        problems.append("no MPEG frames - not a playable MP3")
    if info["id3_tags"] > 1:
        problems.append(f"{info['id3_tags']} ID3 tags - pieces glued badly")
    expected = len(text) / CHARS_PER_SECOND if text else 0.0
    if expected > 0 and info["seconds"] < expected * SHORT_FLOOR:
        share = info["seconds"] / expected
        problems.append(
            f"TRUNCATED - {info['seconds']:.1f}s is {share:.0%} of the "
            f"{expected:.1f}s this line needs"
        )
    return {
        "sha256": hashlib.sha256(raw).hexdigest(),
        "size": len(raw),
        "seconds": info["seconds"],
        "expected": expected,
        "problems": problems,
    }


# ----------------------------------------------------------------------- plan

def build_plan(repo: Path, render_dir: Path, api_dir: Path, partial: bool) -> dict:
    settings_path = api_dir / "appsettings.json"
    text = settings_path.read_text(encoding="utf-8")
    config = json.loads(text)
    sync = config["ContentSync"]
    entries = {e["VoiceId"]: e for e in sync.get("Voice", [])}
    if not entries:
        raise SystemExit("ContentSync:Voice is empty - nothing to update.")
    audio_root = api_dir / (sync.get("AudioRoot") or "")
    texts = clip_texts(repo)

    rendered = {p.stem: p for p in sorted(render_dir.glob("*.mp3"))}
    stray = [
        name
        for name in rendered
        if name not in entries and not _NOT_VOICE_CLIP.search(name)
    ]
    missing = [v for v in entries if v not in rendered]
    if missing and not partial:
        raise SystemExit(
            f"{len(missing)} of {len(entries)} voice clips were not rendered: "
            + ", ".join(sorted(missing)[:8])
            + ("..." if len(missing) > 8 else "")
            + "\nInstalling a partial set means the toy greets the child in the new"
            "\nvoice on one boot and the old voice on the next. Render the whole set,"
            "\nor pass --partial if you genuinely mean to ship a mixed library."
        )

    rows, defects, updates, copies = [], [], {}, []
    for voice_id, entry in entries.items():
        source = rendered.get(voice_id)
        if source is None:
            rows.append((voice_id, "not rendered", "", ""))
            continue
        found = inspect(source, texts.get(voice_id))
        if found["problems"]:
            defects.append((voice_id, found["problems"]))
            continue
        dest = (audio_root / entry["AudioPath"]).resolve()
        if audio_root.resolve() not in dest.parents:
            raise SystemExit(f"{voice_id}: AudioPath escapes the audio root - refusing.")
        if found["sha256"] == entry.get("Sha256"):
            # Byte-identical. Bumping Version here would make every toy in the
            # field re-download a file it already has, for nothing.
            rows.append((voice_id, "unchanged", f"v{entry['Version']}", ""))
            continue
        version = int(entry["Version"]) + 1
        updates[voice_id] = {
            "version": version,
            "sha256": found["sha256"],
            "size": found["size"],
        }
        copies.append((source, dest))
        rows.append(
            (
                voice_id,
                "UPDATE",
                f"v{entry['Version']} -> v{version}",
                f"{found['size']:,} B  {found['seconds']:.1f}s  {found['sha256'][:12]}",
            )
        )
    return {
        "settings_path": settings_path,
        "text": text,
        "rows": rows,
        "defects": defects,
        "stray": stray,
        "updates": updates,
        "copies": copies,
        "missing": missing,
        "total": len(entries),
    }


def apply_plan(plan: dict) -> None:
    """Validate the whole patch, then land it. Never half-write."""
    patched = patch_voice(plan["text"], plan["updates"])
    before = json.loads(plan["text"])
    after = json.loads(patched)

    # Everything outside ContentSync:Voice must be untouched, and inside it only
    # the three fields of the intended clips may differ. Checked before writing,
    # because a config this file cannot verify is a config nobody will notice.
    before_voice = before["ContentSync"].pop("Voice")
    after_voice = after["ContentSync"].pop("Voice")
    if before != after:
        raise SystemExit("refusing to write: the patch touched something outside Voice")
    for old, new in zip(before_voice, after_voice):
        expected = dict(old)
        if old["VoiceId"] in plan["updates"]:
            change = plan["updates"][old["VoiceId"]]
            expected.update(
                Version=change["version"], Sha256=change["sha256"], SizeBytes=change["size"]
            )
        if expected != new:
            raise SystemExit(f"refusing to write: unexpected change on {old['VoiceId']}")

    for source, dest in plan["copies"]:
        dest.parent.mkdir(parents=True, exist_ok=True)
        staged = dest.with_suffix(dest.suffix + ".tmp")
        shutil.copyfile(source, staged)
        staged.replace(dest)
    staged_settings = plan["settings_path"].with_suffix(".json.tmp")
    staged_settings.write_text(patched, encoding="utf-8")
    staged_settings.replace(plan["settings_path"])


def report(plan: dict) -> int:
    print(f"{'clip':<14} {'action':<11} {'version':<14} details")
    print("-" * 76)
    for voice_id, action, version, details in plan["rows"]:
        print(f"{voice_id:<14} {action:<11} {version:<14} {details}")
    print()

    if plan["stray"]:
        print(
            f"{len(plan['stray'])} rendered file(s) are not in ContentSync:Voice and "
            "will reach no toy:"
        )
        for name in plan["stray"]:
            print(f"  {name}.mp3")
        print()

    if plan["defects"]:
        print("REFUSING - these clips are not shippable:")
        for voice_id, problems in plan["defects"]:
            for problem in problems:
                print(f"  {voice_id}: {problem}")
        print("\nRe-render them; do not install a clip that stops in the middle.")
        return 1

    if not plan["updates"]:
        print("Nothing to do - every configured clip already matches its file.")
        return 0
    return 0


def main(argv=None) -> int:
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--in", dest="render_dir", type=Path,
                        help="folder of rendered <voiceId>.mp3 files")
    parser.add_argument("--api-dir", type=Path,
                        default=repo / "backend/src/ArmenianAiToy.Api",
                        help="folder holding appsettings.json and story-audio/")
    parser.add_argument("--repo", type=Path, default=repo)
    parser.add_argument("--apply", action="store_true",
                        help="actually copy the clips and patch appsettings.json")
    parser.add_argument("--partial", action="store_true",
                        help="allow installing fewer clips than are configured")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args(argv)

    if args.self_test:
        return self_test()
    if not args.render_dir:
        parser.error("--in is required (or use --self-test)")

    plan = build_plan(args.repo, args.render_dir, args.api_dir, args.partial)
    code = report(plan)
    if code or not plan["updates"]:
        return code
    if not args.apply:
        print(
            f"Dry run. {len(plan['updates'])} of {plan['total']} clips would be "
            "installed and their manifest entries patched."
        )
        print("Add --apply to write. Then: listen to a few, commit, and let the")
        print("toys pick them up on the Version bump.")
        return 0

    apply_plan(plan)
    print(f"Installed {len(plan['updates'])} clips and patched appsettings.json.")
    print("Listen to a handful before committing - the human test is still the gate.")
    return 0


# ------------------------------------------------------------------- self test

_FRAME = bytes([0xFF, 0xFB, 0x90, 0xC0]) + bytes(413)  # 128kbps 44.1k mono
_FRAME_SECONDS = 1152 / 44100


def _synth_mp3(seconds: float, tag: bytes = b"") -> bytes:
    return tag + _FRAME * max(1, round(seconds / _FRAME_SECONDS))


def _fixture(root: Path, versions=(3, 3, 3)) -> Path:
    api = root / "api"
    (api / "story-audio/voice").mkdir(parents=True)
    voice = [
        {
            "VoiceId": f"greet-{i:02d}",
            "Version": versions[i - 1],
            "AudioPath": f"voice/greet-{i:02d}.mp3",
            "Sha256": "0" * 64,
            "SizeBytes": 1,
        }
        for i in (1, 2, 3)
    ]
    (api / "appsettings.json").write_text(
        json.dumps(
            {
                "Jwt": {"Key": "keep me"},
                "ContentSync": {
                    "Enabled": True,
                    "AudioRoot": "story-audio",
                    "Stories": [{"StoryId": "ulik", "Version": 6, "Sha256": "a" * 64}],
                    "Games": [{"GameKey": "quiz", "ClipId": "intro", "Version": 1}],
                    "Voice": voice,
                },
            },
            indent=2,
        ),
        encoding="utf-8",
    )
    (root / "backend/content/voice-clips").mkdir(parents=True)
    (root / "backend/content/voice-clips/voice-clips.json").write_text(
        json.dumps(
            {
                "clips": [
                    {"voiceId": "greet-01", "text": "x" * 30},
                    {"voiceId": "greet-02", "text": "x" * 30},
                    {"voiceId": "greet-03", "text": "x" * 30},
                ]
            }
        ),
        encoding="utf-8",
    )
    return api


def self_test() -> int:
    checks = []

    def ok(name: str, condition: bool) -> None:
        checks.append((name, bool(condition)))

    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        api = _fixture(root)
        renders = root / "render"
        renders.mkdir()
        for i in (1, 2, 3):
            (renders / f"greet-{i:02d}.mp3").write_bytes(_synth_mp3(2.0))
        (renders / "khosogh-dzuk--narration--s1.mp3").write_bytes(_synth_mp3(2.0))
        (renders / "greet-99.mp3").write_bytes(_synth_mp3(2.0))

        plan = build_plan(root, renders, api, partial=False)
        ok("all three clips planned", len(plan["updates"]) == 3)
        ok("story render not flagged as stray", "khosogh-dzuk--narration--s1" not in plan["stray"])
        ok("unconfigured clip flagged as stray", plan["stray"] == ["greet-99"])
        ok("no defects on healthy clips", not plan["defects"])

        apply_plan(plan)
        after = json.loads((api / "appsettings.json").read_text(encoding="utf-8"))
        entries = {e["VoiceId"]: e for e in after["ContentSync"]["Voice"]}
        real = hashlib.sha256((renders / "greet-01.mp3").read_bytes()).hexdigest()
        ok("sha256 matches the file", entries["greet-01"]["Sha256"] == real)
        ok("size matches the file",
           entries["greet-01"]["SizeBytes"] == (renders / "greet-01.mp3").stat().st_size)
        ok("Version bumped", all(entries[k]["Version"] == 4 for k in entries))
        ok("clip installed", (api / "story-audio/voice/greet-02.mp3").read_bytes()
           == (renders / "greet-02.mp3").read_bytes())
        ok("Stories untouched", after["ContentSync"]["Stories"][0]["Version"] == 6)
        ok("Games untouched", after["ContentSync"]["Games"][0]["Version"] == 1)
        ok("unrelated keys untouched", after["Jwt"]["Key"] == "keep me")

        # Re-running must be a no-op: same bytes, so no pointless re-download.
        again = build_plan(root, renders, api, partial=False)
        ok("second run is a no-op", not again["updates"])

    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        api = _fixture(root)
        renders = root / "render"
        renders.mkdir()
        (renders / "greet-01.mp3").write_bytes(_synth_mp3(2.0))
        try:
            build_plan(root, renders, api, partial=False)
            ok("partial set refused", False)
        except SystemExit:
            ok("partial set refused", True)
        plan = build_plan(root, renders, api, partial=True)
        ok("--partial allows one clip", len(plan["updates"]) == 1)

    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        api = _fixture(root)
        renders = root / "render"
        renders.mkdir()
        (renders / "greet-01.mp3").write_bytes(_synth_mp3(0.5))  # 30 chars needs 2.0s
        (renders / "greet-02.mp3").write_bytes(
            _synth_mp3(2.0, tag=b"ID3\x03\x00\x00\x00\x00\x00\x00") + b"ID3\x03\x00\x00\x00\x00\x00\x00"
        )
        (renders / "greet-03.mp3").write_bytes(_synth_mp3(2.0))
        plan = build_plan(root, renders, api, partial=False)
        flagged = {v: p for v, p in plan["defects"]}
        ok("truncated clip refused", any("TRUNCATED" in p for p in flagged.get("greet-01", [])))
        ok("double ID3 refused", any("ID3" in p for p in flagged.get("greet-02", [])))
        ok("healthy clip still planned", "greet-03" in plan["updates"])
        before = (api / "appsettings.json").read_text(encoding="utf-8")
        ok("defects mean exit 1", report(plan) == 1)
        ok("nothing written on defects",
           (api / "appsettings.json").read_text(encoding="utf-8") == before)

    for name, passed in checks:
        print(f"  {'PASS' if passed else 'FAIL'}  {name}")
    failed = [n for n, p in checks if not p]
    print()
    if failed:
        print(f"FAIL - {len(failed)} of {len(checks)}: {', '.join(failed)}")
        return 1
    print(f"PASS - {len(checks)}/{len(checks)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

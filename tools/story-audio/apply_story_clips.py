#!/usr/bin/env python3
"""Patch ContentSync:Stories[].Clips[] from a folder of installed clip MP3s.

The missing sibling of apply_voice_clips.py and apply_game_clips.py. Those two
exist because appsettings.json carries `//` comments -- it is JSONC, not JSON --
so json.load() destroys it and every previous slice did the edit by hand or by
text surgery. Story clips were the one namespace with no tool at all, which is
part of why no story clip has ever shipped: the last mile was 70 sha256/size
pairs typed by a human.

Reads   backend/src/ArmenianAiToy.Api/story-audio/clips/<storyId>/<kind>.mp3
Writes  a "Clips": [...] array into each matching Stories[] entry.

DELIBERATELY NEVER TOUCHES "Version". A clip's freshness is judged per clip by
the firmware (content_sync.cpp sync_story_clips), independently of the story's
version -- so adding clips costs the toy only the clip bytes. Bumping Version
would make every toy re-download the narration too, which is ~30 MB across ten
stories and a long wait on a weak link. See CLAUDE.md, "Story plays, reflection
memory, library & clips".

Idempotent: an existing Clips array on a story is replaced wholesale, so a
re-render can be applied over the top without duplicating entries.

Dry-run by default. Pass --apply to write.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

# Must stay in lockstep with ContentSyncClipOptions.AllowedKinds (backend) and
# cs_question_clip_kind() (firmware). A kind that is not in this set is dropped
# silently by the manifest builder, which looks exactly like a clip that never
# renders -- so refuse it here, loudly, instead.
ALLOWED_KINDS = {
    "intro", "question", "question1", "question2",
    "summary", "offer", "reoffer", "serialnext",
}

REPO = Path(__file__).resolve().parents[2]
DEFAULT_CLIPS = REPO / "backend/src/ArmenianAiToy.Api/story-audio/clips"
DEFAULT_CONFIG = REPO / "backend/src/ArmenianAiToy.Api/appsettings.json"
# AudioPath is resolved against ContentSync:AudioRoot ("story-audio"), so the
# stored path is relative to that root, never an absolute one.
AUDIO_ROOT_PREFIX = "clips"


def sha256_of(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as fh:
        for block in iter(lambda: fh.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def collect(clips_dir: Path) -> dict[str, list[dict]]:
    """story id -> list of clip dicts, kinds in a stable, readable order."""
    order = ["intro", "question", "question1", "question2",
             "summary", "offer", "reoffer", "serialnext"]
    found: dict[str, list[dict]] = {}
    for story_dir in sorted(p for p in clips_dir.iterdir() if p.is_dir()):
        entries = []
        for mp3 in story_dir.glob("*.mp3"):
            kind = mp3.stem
            if kind not in ALLOWED_KINDS:
                raise SystemExit(
                    f"ERROR: {mp3} -- '{kind}' is not an allowed clip kind. "
                    f"The manifest builder would drop it silently."
                )
            size = mp3.stat().st_size
            if size <= 0:
                raise SystemExit(f"ERROR: {mp3} is empty; a zero-size clip is dropped by the validator.")
            entries.append({
                "Kind": kind,
                "AudioPath": f"{AUDIO_ROOT_PREFIX}/{story_dir.name}/{kind}.mp3",
                "Sha256": sha256_of(mp3),
                "SizeBytes": size,
            })
        entries.sort(key=lambda e: order.index(e["Kind"]) if e["Kind"] in order else 99)
        if entries:
            found[story_dir.name] = entries
    return found


def render_clips_block(entries: list[dict], indent: str) -> str:
    """Emit the Clips array with the file's own 2-space-per-level style."""
    inner = indent + "  "
    field = inner + "  "
    out = [f'{indent}"Clips": [']
    for i, e in enumerate(entries):
        out.append(inner + "{")
        out.append(f'{field}"Kind": {json.dumps(e["Kind"])},')
        out.append(f'{field}"AudioPath": {json.dumps(e["AudioPath"])},')
        out.append(f'{field}"Sha256": {json.dumps(e["Sha256"])},')
        out.append(f'{field}"SizeBytes": {e["SizeBytes"]}')
        out.append(inner + "}" + ("," if i < len(entries) - 1 else ""))
    out.append(indent + "]")
    return "\n".join(out)


def patch(text: str, clips: dict[str, list[dict]]) -> tuple[str, list[str]]:
    """Insert/replace a Clips array inside each story object, by text surgery.

    appsettings.json is JSONC. Parsing and re-serialising would strip every
    comment in the file, and this repo's comments carry decisions (why a story
    ships at SizeBytes 0, why BoardModel is empty). So the edit is textual and
    scoped to one object at a time.
    """
    notes = []
    for story_id, entries in sorted(clips.items()):
        # Find this story's object: from its "StoryId" line to the closing brace
        # of that object. The Sha256/SizeBytes lines are the last fields today,
        # so the object ends at the first line that is exactly the closing brace
        # at the object's own indent.
        m = re.search(rf'(\n(?P<i>[ ]+)\{{\n)(?P<body>(?:[ ]*"[^"]+":.*\n|[ ]*\[.*\n|[ ]*\].*\n|[ ]*\{{.*\n|[ ]*\}}.*\n)*?)(?P=i)\}}',
                      text)
        # The generic scan above is fragile across objects; locate precisely instead.
        key = f'"StoryId": "{story_id}"'
        pos = text.find(key)
        if pos == -1:
            notes.append(f"SKIP {story_id}: not present in ContentSync:Stories")
            continue
        line_start = text.rfind("\n", 0, pos) + 1
        field_indent = text[line_start:pos]
        if field_indent.strip():
            notes.append(f"SKIP {story_id}: unexpected layout on the StoryId line")
            continue
        obj_indent = field_indent[:-2] if len(field_indent) >= 2 else field_indent
        # object opens on the line above the StoryId line
        obj_open = text.rfind("{", 0, line_start)
        close_marker = "\n" + obj_indent + "}"
        obj_close = text.find(close_marker, pos)
        if obj_open == -1 or obj_close == -1:
            notes.append(f"SKIP {story_id}: could not bound the object")
            continue
        body = text[obj_open + 1:obj_close]
        if '"Version"' not in body:
            notes.append(f"SKIP {story_id}: object has no Version field, refusing to guess")
            continue
        # Drop an existing Clips array so re-runs replace rather than duplicate.
        existing = re.search(r'\n[ ]*"Clips":[ ]*\[.*?\n[ ]*\](,?)', body, re.S)
        if existing:
            body = body[:existing.start()] + body[existing.end():]
            body = body.rstrip().rstrip(",")
            notes.append(f"{story_id}: replaced an existing Clips array")
        body = body.rstrip()
        if not body.endswith(","):
            body += ","
        new_body = body + "\n" + render_clips_block(entries, field_indent) + "\n"
        text = text[:obj_open + 1] + new_body + text[obj_close + 1:]
        notes.append(f"{story_id}: {len(entries)} clips ({', '.join(e['Kind'] for e in entries)})")
    return text, notes


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--clips-dir", type=Path, default=DEFAULT_CLIPS)
    ap.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    ap.add_argument("--apply", action="store_true", help="write the file (default is dry-run)")
    args = ap.parse_args()

    if not args.clips_dir.is_dir():
        print(f"no clips directory at {args.clips_dir}", file=sys.stderr)
        return 2
    clips = collect(args.clips_dir)
    if not clips:
        print("no clips found", file=sys.stderr)
        return 2

    original = args.config.read_text(encoding="utf-8")
    patched, notes = patch(original, clips)

    for n in notes:
        print("  " + n)
    total = sum(len(v) for v in clips.values())
    print(f"\n{len(clips)} stories, {total} clips")
    print("Version fields: untouched by design (per-clip freshness; see module docstring)")

    if not args.apply:
        print("\nDRY RUN — nothing written. Pass --apply to write.")
        return 0

    args.config.write_text(patched, encoding="utf-8")
    print(f"\nwrote {args.config}")
    print("Next: dotnet build, check GET /api/devices/content-manifest, then the human listen test.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

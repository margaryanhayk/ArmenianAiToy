#!/usr/bin/env python3
"""Guided, resumable listen test for every audio file a toy can download.

Written 2026-10-22. Runs on the machine with the headphones (or the toy).

CLAUDE.md says a human listen test gates any shipped audio. Nobody runs it,
because "listen to 240 files and write each one down" is a chore, not a task.
This turns it into a loop: it plays the next file, you press p / f / s / r,
the row is on disk before the next file starts, and Ctrl+C never loses work.

    python3 tools/listen/listen_test.py run --group stories
    python3 tools/listen/listen_test.py status

THE EAR IS THE GATE. This tool cannot hear a seam, a click or a mangled word.
It plays, it asks, it records what a human typed. It never writes to an audio
file or appsettings.json; it only appends to tools/quality-evidence/.

WHAT IT LISTENS TO  The catalogue is ContentSync in appsettings.json, i.e.
what toys actually download, not whatever sits in a folder.

  stories  Stories[] without AltOf   the narration
  alts     Stories[] with AltOf      the variant endings
  clips    Stories[].Clips[]         per-story clips; id <storyId>/<kind>
  voice    Voice[]                   welcome / prompt clips
  games    Games[]                   game clips and the mid-story pause clips
                                     (GameKey story-pauses); id <key>/<clip>
  music    Music[]                   bedtime tracks

An item is advertised when ContentSync:Enabled is true, it is not Retired,
SizeBytes > 0 and Sha256 is 64 hex (the manifest service drops anything else,
so placeholders never reach a toy). Clips follow their story. Files resolve
under ContentSync:AudioRoot, relative to the folder holding appsettings.json
(backend/src/ArmenianAiToy.Api/story-audio). Not read: the legacy single-story
shape, and uploaded ContentItem rows (they live in the database, not in git).

EVIDENCE  tools/quality-evidence/listen-test-<YYYYMMDD>.md, one table row per
verdict: | id | namespace | path | sha256 | seconds | verdict | note |. The
sha256 is taken from the file's bytes when the verdict is typed. A new header
block starts when listener, device or player change mid-day.

STATUS  An approval is pinned to bytes: only rows whose sha256 equals the
file's CURRENT sha256 count, and the latest PASS/FAIL among them wins (files by
date, then row order). SKIP rows never count either way.
  APPROVED     latest verdict on the current bytes is PASS
  FAILED       latest verdict on the current bytes is FAIL
  CHANGED      heard before, but never these bytes: the approval is void
  NEVER HEARD  no PASS/FAIL row for this path
  MISSING      advertised, but the file is not on disk
`status` exits 0 only when every advertised file is APPROVED.
"""
from __future__ import annotations

import argparse
import datetime as _dt
import hashlib
import json
import os
import re
import shlex
import shutil
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
APPSETTINGS = REPO / "backend" / "src" / "ArmenianAiToy.Api" / "appsettings.json"
EVIDENCE_DIR = REPO / "tools" / "quality-evidence"

GROUPS = ("stories", "alts", "clips", "voice", "games", "music")
STATES = ("APPROVED", "CHANGED", "FAILED", "NEVER HEARD", "MISSING")
VERDICTS = ("PASS", "FAIL", "SKIP")
COLUMNS = ("id", "namespace", "path", "sha256", "seconds", "verdict", "note")
SHA_RE = re.compile(r"[0-9a-f]{64}")
EVIDENCE_RE = re.compile(r"listen-test-(\d{8})\.md")
FIELD_RE = re.compile(r"^- \*\*(Listener|Device|Player|Commit):\*\*[ \t]*(.*?)\s*$")
NOTICE = ("> A human ear is the gate. Every verdict below was typed by a listener; "
          "listen_test.py only recorded it. A PASS is void the moment the file's sha256 changes.")
PROMPT = "  PASS / FAIL / SKIP / REPLAY (p/f/s/r): "
FALLBACK_KBPS = 192  # house encoding; only used to estimate when ffprobe is absent


@dataclass(frozen=True)
class Item:
    id: str
    group: str
    path: str    # AudioPath as the catalogue spells it: the evidence key
    file: Path   # where it really is
    size: int
    sha256: str  # the catalogue's sha, i.e. what toys verify against


@dataclass(frozen=True)
class Row:
    id: str
    group: str
    path: str
    sha256: str
    seconds: float | None
    verdict: str
    note: str
    date: str = ""
    listener: str = ""
    device: str = ""
    player: str = ""
    commit: str = ""


@dataclass(frozen=True)
class Result:
    item: Item
    state: str
    row: Row | None  # the row that decided the state, or the last one heard


# --- catalogue -----------------------------------------------------------

def resolve_audio_root(audio_root: str, appsettings: Path) -> Path:
    root = Path(audio_root or "")
    return root if root.is_absolute() else appsettings.parent / root


def _resolve(root: Path, rel: str) -> Path | None:
    p = Path(rel)
    if p.is_absolute():
        return p
    full = (root / p).resolve()
    try:
        full.relative_to(root.resolve())
    except ValueError:
        return None  # "../" out of the audio root: refuse rather than guess
    return full


def load_catalogue(config: dict, audio_root: Path) -> list[Item]:
    """The files toys can download, in GROUPS order. Pure given the config."""
    cs = config.get("ContentSync") or {}
    if cs.get("Enabled") is not True:
        return []
    found: dict[str, list[Item]] = {g: [] for g in GROUPS}
    seen: set[tuple[str, str]] = set()

    def text(row: dict, key: str) -> str:
        return str(row.get(key) or "").strip()

    def add(group: str, space: str, item_id: str, row: dict) -> bool:
        sha, size = text(row, "Sha256").lower(), row.get("SizeBytes")
        if (not item_id or not row.get("AudioPath") or row.get("Retired") or (space, item_id.lower()) in seen
                or not isinstance(size, int) or size <= 0 or not SHA_RE.fullmatch(sha)):
            return False
        file = _resolve(audio_root, row["AudioPath"])
        if file is None:
            return False
        seen.add((space, item_id.lower()))
        found[group].append(Item(item_id, group, row["AudioPath"], file, size, sha))
        return True

    for s in cs.get("Stories") or []:
        sid = text(s, "StoryId")
        if not add("alts" if text(s, "AltOf") else "stories", "story", sid, s):
            continue  # a dropped story takes its clips with it
        for c in s.get("Clips") or []:
            kind = text(c, "Kind").lower()
            add("clips", "clips", f"{sid}/{kind}" if kind else "", c)
    for v in cs.get("Voice") or []:
        add("voice", "voice", text(v, "VoiceId"), v)
    for g in cs.get("Games") or []:
        key, clip = text(g, "GameKey"), text(g, "ClipId")
        add("games", "games", f"{key}/{clip}" if key and clip else "", g)
    for m in cs.get("Music") or []:
        add("music", "music", text(m, "TrackId"), m)
    return [i for g in GROUPS for i in found[g]]


def select_group(items: list[Item], group: str, only: str | None = None) -> list[Item]:
    out = [i for i in items if group == "all" or i.group == group]
    return [i for i in out if only.lower() in i.id.lower()] if only else out


# --- evidence files ------------------------------------------------------

def evidence_path(directory: Path, date: str) -> Path:
    return directory / f"listen-test-{date}.md"


def _cell(value: object) -> str:
    return " ".join(str(value).replace("|", "/").split())  # one line, no table-breaking pipes


def _read_text(path: Path) -> str:
    """Every catalogue / evidence read goes through here. `utf-8-sig` because
    Visual Studio and Notepad on Windows write a BOM, which plain `utf-8`
    hands to json.loads as a stray character (every command then exits 2)."""
    return path.read_text(encoding="utf-8-sig")


def render_row(r: Row) -> str:
    secs = "?" if r.seconds is None else f"{r.seconds:.1f}"
    cells = [r.id, r.group, r.path, r.sha256, secs, r.verdict, r.note]
    return "| " + " | ".join(_cell(c) for c in cells) + " |"


def render_session(date: str, ctx: dict, first: bool) -> str:
    d = f"{date[:4]}-{date[4:6]}-{date[6:]}"
    head = [f"# Listen test {d}", "", NOTICE, ""] if first else ["", "## New session", ""]
    fields = [f"- **Date:** {d}"] + [f"- **{k.title()}:** {_cell(ctx[k])}"
                                      for k in ("listener", "device", "player", "commit")]
    table = ["| " + " | ".join(COLUMNS) + " |", "|" + "---|" * len(COLUMNS)]
    return "\n".join(head + fields + [""] + table) + "\n"


def parse_evidence(text: str, date: str) -> tuple[list[Row], dict]:
    """Rows plus the last session header seen. Malformed lines are ignored."""
    rows: list[Row] = []
    ctx: dict[str, str] = {}
    for line in text.splitlines():
        m = FIELD_RE.match(line)
        if m:
            ctx[m.group(1).lower()] = m.group(2)
            continue
        line = line.strip()
        cells = [c.strip() for c in line[1:-1].split("|")] if line.startswith("|") and line.endswith("|") else []
        if len(cells) != len(COLUMNS) or cells[5] not in VERDICTS or not SHA_RE.fullmatch(cells[3]):
            continue
        try:
            seconds: float | None = float(cells[4])
        except ValueError:
            seconds = None
        rows.append(Row(cells[0], cells[1], cells[2], cells[3], seconds, cells[5], cells[6], date,
                        ctx.get("listener", ""), ctx.get("device", ""), ctx.get("player", ""),
                        ctx.get("commit", "")))
    return rows, ctx


def load_evidence(directory: Path) -> list[Row]:
    """Every listen-test-<date>.md row, oldest file first, file order kept."""
    rows: list[Row] = []
    for p in sorted(directory.iterdir()) if directory.is_dir() else []:
        m = EVIDENCE_RE.fullmatch(p.name)
        if m:
            rows += parse_evidence(_read_text(p), m.group(1))[0]
    return rows


def _append(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    glue = "\n" if path.exists() and path.stat().st_size and not path.read_bytes().endswith(b"\n") else ""
    with open(path, "a", encoding="utf-8", newline="\n") as f:
        f.write(glue + text)
        f.flush()
        os.fsync(f.fileno())  # a row survives Ctrl+C, a crash, a pulled plug


def start_session(path: Path, date: str, ctx: dict) -> None:
    """Write a header unless the file's last header already says the same."""
    last = parse_evidence(_read_text(path), date)[1] if path.exists() else {}
    if not all(last.get(k) == _cell(ctx[k]) for k in ("listener", "device", "player")):
        _append(path, render_session(date, ctx, first=not path.exists()))


def append_row(path: Path, row: Row) -> None:
    _append(path, render_row(row) + "\n")


# --- status --------------------------------------------------------------

def compute_status(items: list[Item], rows: list[Row], current: dict[str, str | None]) -> list[Result]:
    """`current` maps item.path to its sha256 now, or None if the file is gone."""
    decisions: dict[str, list[Row]] = {}
    for r in rows:
        if r.verdict != "SKIP":
            decisions.setdefault(r.path, []).append(r)
    out = []
    for it in items:
        heard, sha = decisions.get(it.path, []), current.get(it.path)
        same = [r for r in heard if sha and r.sha256.lower() == sha.lower()]
        if sha is None:
            out.append(Result(it, "MISSING", heard[-1] if heard else None))
        elif same:
            out.append(Result(it, "APPROVED" if same[-1].verdict == "PASS" else "FAILED", same[-1]))
        else:
            out.append(Result(it, "CHANGED" if heard else "NEVER HEARD", heard[-1] if heard else None))
    return out


def pending_items(results: list[Result], rows: list[Row], date: str, device: str, again: bool = False) -> list[Item]:
    """What `run` asks about: not yet answered today on this device and (unless
    `again`) not already APPROVED. MISSING files cannot be played."""
    # The evidence header stores the device through _cell() (pipes -> "/",
    # whitespace collapsed), so the raw --device argument must take the same
    # road or `--device "PC | headphones"` never matches the rows it wrote.
    want = _cell(device).lower()
    done = {r.path for r in rows if r.date == date and _cell(r.device).lower() == want}
    return [x.item for x in results if x.state != "MISSING" and x.item.path not in done
            and (again or x.state != "APPROVED")]


def still_to_hear(results: list[Result], seconds: dict[str, float | None]) -> tuple[float, int]:
    """Seconds of audio not yet APPROVED, and how many of them were estimated."""
    total, estimated = 0.0, 0
    for r in results:
        if r.state not in ("APPROVED", "MISSING"):
            secs = seconds.get(r.item.path)
            if secs is None:
                secs, estimated = r.item.size * 8 / (FALLBACK_KBPS * 1000), estimated + 1
            total += secs
    return total, estimated


def format_clock(secs: float | None) -> str:
    return "?" if secs is None else "%d:%02d" % divmod(int(round(secs)), 60)


def format_table(results: list[Result], seconds: dict[str, float | None], hide_approved: bool = False) -> str:
    rows = [("STATE", "NAMESPACE", "ID", "TIME", "LAST HEARD")]
    for r in results:
        if not (hide_approved and r.state == "APPROVED"):
            last = f"{r.row.verdict} {r.row.date} ({r.row.device or '-'})" if r.row else ""
            rows.append((r.state, r.item.group, r.item.id, format_clock(seconds.get(r.item.path)), last))
    widths = [max(len(row[c]) for row in rows) for c in range(5)]
    return "\n".join("  ".join(v.ljust(w) for v, w in zip(row, widths)).rstrip() for row in rows)


def format_summary(results: list[Result], seconds: dict[str, float | None]) -> str:
    counts = " ".join(f"{s} {sum(r.state == s for r in results)}  " for s in STATES).rstrip()
    lines = ["", f"SUMMARY  {counts}  ({len(results)} files)"]
    for g in GROUPS:
        mine = [r for r in results if r.item.group == g]
        if mine:
            lines.append(f"  {g:<8} {sum(r.state == 'APPROVED' for r in mine)}/{len(mine)} approved")
    total, estimated = still_to_hear(results, seconds)
    left = sum(r.state not in ("APPROVED", "MISSING") for r in results)
    lines.append(f"Still to hear: {left} files, {total / 60:.1f} minutes of audio")
    if estimated:
        lines.append(f"  ({estimated} durations estimated from size at {FALLBACK_KBPS} kbps: ffprobe unavailable)")
    return "\n".join(lines)


# --- files, player, prompt ----------------------------------------------

def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def current_shas(items: list[Item]) -> dict[str, str | None]:
    return {i.path: sha256_file(i.file) if i.file.is_file() else None for i in items}


def probe_seconds(file: Path) -> float | None:
    try:
        out = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration",
                              "-of", "default=nw=1:nk=1", str(file)],
                             capture_output=True, text=True, timeout=30).stdout
        return float(out.strip())
    except (OSError, ValueError, subprocess.SubprocessError):
        return None


def build_player_command(player: str, file: Path, posix: bool | None = None) -> list[str]:
    f = str(file)
    if player == "ffplay":
        return ["ffplay", "-nodisp", "-autoexit", "-loglevel", "error", f]
    if player == "mpv":
        return ["mpv", "--no-video", "--really-quiet", f]
    if "{file}" not in player:
        raise ValueError("--player must be ffplay, mpv, or a command containing {file}")
    posix = (os.name != "nt") if posix is None else posix
    # Split the TEMPLATE, then substitute: the path never meets the shlex parser,
    # so backslashes and spaces in a Windows path survive.
    return [(p if posix else p.strip('"')).replace("{file}", f) for p in shlex.split(player, posix=posix)]


def play_file(cmd: list[str]) -> None:
    try:
        subprocess.run(cmd, stdin=subprocess.DEVNULL)
    except KeyboardInterrupt:
        pass  # Ctrl+C while playing stops the clip and lands on the prompt


def parse_answer(text: str) -> tuple[str, str] | None:
    """'p' -> PASS; 'f click at 0:42' -> FAIL plus that note; None = ask again."""
    word, _, note = text.strip().partition(" ")
    names = {"p": "PASS", "f": "FAIL", "s": "SKIP", "r": "REPLAY", "q": "QUIT"}
    verdict = names.get(word.lower()) or next((v for v in names.values() if v == word.upper()), None)
    return (verdict, note.strip()) if verdict else None


def hear(cmd: list[str]) -> tuple[str, str]:
    """Play, ask, replay on request; returns (PASS|FAIL|SKIP|QUIT, note)."""
    while True:
        play_file(cmd)
        answer = None
        while answer is None:
            answer = parse_answer(input(PROMPT))
        if answer[0] != "REPLAY":
            return answer


def ask_default(label: str, default: str | None) -> str:
    try:
        value = input(f"{label}" + (f" [{default}]" if default else "") + ": ").strip()
    except EOFError:
        value = ""
    return value or default or "unspecified"


def git_commit() -> str:
    try:
        out = subprocess.run(["git", "rev-parse", "--short", "HEAD"], cwd=REPO,
                             capture_output=True, text=True, timeout=10)
        return out.stdout.strip() or "unknown"
    except (OSError, subprocess.SubprocessError):
        return "unknown"


# --- commands ------------------------------------------------------------

def _catalogue(args: argparse.Namespace) -> list[Item]:
    config = json.loads(_read_text(args.appsettings))
    root = resolve_audio_root((config.get("ContentSync") or {}).get("AudioRoot", ""), args.appsettings)
    return select_group(load_catalogue(config, root), args.group, getattr(args, "only", None))


def _warn_drift(items: list[Item], shas: dict[str, str | None]) -> None:
    drift = [i.id for i in items if shas.get(i.path) and shas[i.path] != i.sha256]
    if drift:
        print(f"WARNING: {len(drift)} file(s) differ from the catalogue sha256, so toys would reject "
              f"them: {', '.join(drift[:5])}{' ...' if len(drift) > 5 else ''}")


def cmd_status(args: argparse.Namespace) -> int:
    items = _catalogue(args)
    if not items:
        print("Nothing advertised (catalogue empty, ContentSync disabled, or no such group).")
        return 1
    shas = current_shas(items)
    results = compute_status(items, load_evidence(args.evidence_dir), shas)
    seconds = {r.item.path: r.row.seconds for r in results if r.state == "APPROVED" and r.row}
    todo = [r.item for r in results if r.state not in ("APPROVED", "MISSING")]
    with ThreadPoolExecutor(8) as pool:  # one ffprobe per file, a few at a time
        seconds.update(zip((i.path for i in todo), pool.map(probe_seconds, (i.file for i in todo))))
    _warn_drift(items, shas)
    print(format_table(results, seconds, args.hide_approved))
    print(format_summary(results, seconds))
    return 0 if all(r.state == "APPROVED" for r in results) else 1


def cmd_run(args: argparse.Namespace) -> int:
    items, date = _catalogue(args), args.date
    if not items:
        print("Nothing advertised matches that group / --only.")
        return 1
    path = evidence_path(args.evidence_dir, date)
    last = parse_evidence(_read_text(path), date)[1] if path.exists() else {}
    ctx = {"listener": args.listener or ask_default("Listener (your name)", last.get("listener")),
           "device": args.device or ask_default("Playback device (PC headphones / toy speaker)", last.get("device")),
           "player": args.player, "commit": git_commit()}
    rows, shas = load_evidence(args.evidence_dir), current_shas(items)
    results = compute_status(items, rows, shas)
    todo = pending_items(results, rows, date, ctx["device"], args.again)
    for r in results:
        if r.state == "MISSING":
            print(f"WARNING: {r.item.id}: file not on disk ({r.item.file}), cannot be heard")
    _warn_drift(items, shas)
    print(f"{len(items)} advertised in this selection, {len(todo)} to hear "
          f"(device: {ctx['device']}, evidence: {path.name}).")
    if not todo:
        print("Nothing left to hear. `status` shows where everything stands.")
        return 0
    exe = build_player_command(args.player, Path("x"))[0]
    if not shutil.which(exe):
        print(f"Player not found: {exe}. Install ffmpeg (ffplay), or pass --player mpv / "
              '--player "your-player {file}".')
        return 1
    start_session(path, date, ctx)
    counts = {v: 0 for v in VERDICTS}
    print("Answer p = pass, f = fail, s = skip, r = replay, q = quit; a note may follow: 'f click at 0:42'.")
    try:
        for n, item in enumerate(todo, 1):
            secs = probe_seconds(item.file)
            print(f"\n[{n}/{len(todo)}] {item.id}  ({item.group}, {format_clock(secs)})")
            verdict, note = hear(build_player_command(args.player, item.file))
            if verdict == "QUIT":
                break
            if verdict == "FAIL" and not note:
                note = input("  What did you hear? (Enter to skip): ").strip()
            # Hash AFTER the verdict: the sha is of the bytes as they are when the human decided.
            append_row(path, Row(item.id, item.group, item.path, sha256_file(item.file), secs, verdict, note,
                                 date, ctx["listener"], ctx["device"], ctx["player"], ctx["commit"]))
            counts[verdict] += 1
            print(f"  recorded {verdict}")
    except (KeyboardInterrupt, EOFError):
        print()
    print(f"Recorded this session: {counts['PASS']} PASS, {counts['FAIL']} FAIL, {counts['SKIP']} SKIP. "
          "Re-run the same command to resume; `status` shows the whole picture.")
    return 0


def _date_arg(value: str) -> str:
    try:
        _dt.datetime.strptime(value, "%Y%m%d")
    except ValueError:
        raise argparse.ArgumentTypeError("expected YYYYMMDD")
    return value


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description="Guided, resumable human listen test. The ear is the gate.")
    sub = p.add_subparsers(dest="cmd", required=True)
    for name, help_ in (("run", "play files and record verdicts"), ("status", "approval status of every file")):
        sp = sub.add_parser(name, help=help_)
        sp.add_argument("--group", choices=GROUPS + ("all",), default="all")
        sp.add_argument("--date", type=_date_arg, default=_dt.date.today().strftime("%Y%m%d"),
                        help="evidence date YYYYMMDD (default: today, local time)")
        sp.add_argument("--appsettings", type=Path, default=APPSETTINGS)
        sp.add_argument("--evidence-dir", type=Path, default=EVIDENCE_DIR)
        if name == "run":
            sp.add_argument("--only", help="only ids containing this text")
            sp.add_argument("--player", default="ffplay", help="ffplay | mpv | a command containing {file}")
            sp.add_argument("--device", help='e.g. "PC headphones", "toy speaker"')
            sp.add_argument("--listener", help="your name")
            sp.add_argument("--again", action="store_true", help="also re-ask files already APPROVED")
        else:
            sp.add_argument("--hide-approved", action="store_true")
    return p


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        return cmd_run(args) if args.cmd == "run" else cmd_status(args)
    except (OSError, ValueError) as e:  # includes JSONDecodeError
        print(f"error: {e}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())

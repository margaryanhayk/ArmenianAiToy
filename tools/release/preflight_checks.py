#!/usr/bin/env python3
"""Pure checks behind tools/release/preflight.sh. Stdlib only.

Three subcommands, each exiting 0 (ok) / 1 (the check found a problem) /
2 (could not be evaluated):

    test-summary    --log <dotnet test output>
        prints "<total> <failed> <skipped>" summed over every test assembly.
    claude-md-count --claude-md CLAUDE.md --log <dotnet test output>
    claude-md-size  --claude-md CLAUDE.md
        CLAUDE.md "Build & test" carries `dotnet test   # N tests, ...`;
        fails when N is not what dotnet just ran.
    secret-scan     [--root <repo>]
        scans the working tree (tracked + untracked-not-ignored files) for
        secret-SHAPED strings and credential files. It reports the file, line
        and rule ONLY - a matched value is never printed, logged or returned,
        because the point of the scan is that the value goes no further.

False positives are handled by shape, not by blanket path exclusions: a match
whose text contains an obvious fixture word (see FIXTURE_WORDS) is ignored, and
a line carrying the marker `preflight:allow-secret` is ignored. Everything else
in the tree must stay clean - that is the gate.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
from pathlib import Path

ANSI_RE = re.compile(r"\x1b\[[0-9;]*[A-Za-z]")

# ----------------------------------------------------------------- dotnet test
# VSTest console line, one per test assembly:
#   Passed!  - Failed:     0, Passed:  3197, Skipped:     0, Total:  3197, ...
VSTEST_RE = re.compile(
    r"(?:Passed|Failed)!\s*-\s*Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*"
    r"Skipped:\s*(\d+),\s*Total:\s*(\d+)")
# Microsoft.Testing.Platform summary (dotnet 10 opt-in runner):
#   Test summary: total: 3197, failed: 0, succeeded: 3197, skipped: 0, ...
MTP_RE = re.compile(
    r"total:\s*(\d+),\s*failed:\s*(\d+),\s*succeeded:\s*(\d+),\s*skipped:\s*(\d+)",
    re.IGNORECASE)


def parse_test_summary(text: str):
    """Returns (total, failed, skipped) summed over assemblies, or None."""
    text = ANSI_RE.sub("", text)
    rows = VSTEST_RE.findall(text)
    if rows:
        return (sum(int(r[3]) for r in rows), sum(int(r[0]) for r in rows),
                sum(int(r[2]) for r in rows))
    rows = MTP_RE.findall(text)
    if rows:
        return (sum(int(r[0]) for r in rows), sum(int(r[1]) for r in rows),
                sum(int(r[3]) for r in rows))
    return None


DOCUMENTED_RE = re.compile(r"^\s*dotnet test\b[^#\n]*#\s*([\d][\d,._]*)\s+tests", re.MULTILINE)


def documented_test_count(claude_md: str):
    """Returns (count, 1-based line number) of the `dotnet test # N tests` line."""
    m = DOCUMENTED_RE.search(claude_md)
    if not m:
        return None
    return int(re.sub(r"[,._]", "", m.group(1))), claude_md.count("\n", 0, m.start()) + 1


CLAUDE_MD_MAX_BYTES = 24_000  # owner rule 2026-10-27: the file rides on every tool call


def check_claude_md_size(size_bytes: int, limit: int = CLAUDE_MD_MAX_BYTES):
    """Returns (code, message): 0 within the budget, 1 over it.

    CLAUDE.md is re-sent with every tool call of every session, so its size is a
    per-call token cost (~1 token per 4 bytes). It was 71 KB on 2026-10-27 after
    the slice log grew back; the log now lives in docs/CLAUDE-history.md.
    """
    kb = size_bytes / 1024
    if size_bytes <= limit:
        return 0, f"CLAUDE.md is {kb:.1f} KB (budget {limit // 1000} KB)"
    return 1, (f"CLAUDE.md is {kb:.1f} KB, over the {limit // 1000} KB budget — move slice notes "
               "to docs/CLAUDE-history.md § \"State of the toy — slice log\"")


def check_claude_md_count(claude_md: str, log: str):
    """Returns (code, message): 0 match, 1 mismatch, 2 unreadable."""
    doc = documented_test_count(claude_md)
    if doc is None:
        return 2, "CLAUDE.md has no `dotnet test  # N tests` line under Build & test"
    actual = parse_test_summary(log)
    if actual is None:
        return 2, "could not read a test total from the dotnet test output"
    documented, line = doc
    if documented != actual[0]:
        return 1, (f"CLAUDE.md line {line} says {documented}, dotnet ran "
                   f"{actual[0]} - set it to {actual[0]}")
    return 0, f"CLAUDE.md says {documented}, dotnet ran {actual[0]}"


# ----------------------------------------------------------------- secret scan
# Words that mark a match as a deliberate fixture/placeholder (case-insensitive).
FIXTURE_WORDS = ("test", "secretvalue", "abcdefgh", "example", "placeholder", "dummy",
                 "fake", "xxxx", "your_", "redacted", "sample", "changeme", "12345678")
ALLOW_MARKER = "preflight:allow-secret"

# (rule name, regex, capture group holding the secret-ish text or 0)
LINE_RULES = [
    ("openai-key", re.compile(r"(?<![A-Za-z0-9])sk-(?:proj-|svcacct-|admin-)?[A-Za-z0-9_-]{20,}"), 0),
    ("elevenlabs-key", re.compile(r"(?<![A-Za-z0-9])sk_[0-9a-f]{32,}"), 0),
    # Delimited on both sides: a real key is exactly 39 characters and stands
    # alone. Without the guards, random base64 audio embedded in a generated page
    # matched by chance (2026-10-29: tools/story-audio/clip-listen-test.html).
    ("google-api-key", re.compile(r"(?<![A-Za-z0-9_-])AIza[0-9A-Za-z_-]{35}(?![A-Za-z0-9_-])"), 0),
    ("device-key", re.compile(r"(?<![A-Za-z0-9])dtk_[0-9a-fA-F]{32}"), 0),
    ("github-token", re.compile(
        r"(?<![A-Za-z0-9])(?:gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{40,})"), 0),
    ("aws-access-key", re.compile(r"(?<![A-Z0-9])AKIA[0-9A-Z]{16}"), 0),
    ("telegram-bot-token", re.compile(r"(?<![0-9])[0-9]{8,10}:[A-Za-z0-9_-]{35}(?![A-Za-z0-9_-])"), 0),
    ("private-key-block", re.compile(r"-----BEGIN (?:[A-Z]+ )*PRIVATE KEY-----"), 0),
    ("jwt", re.compile(r"eyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}"), 0),
    ("bearer-token", re.compile(r"Bearer[ ]+([A-Za-z0-9._~+/=-]{20,})"), 1),
]

# firmware config.h-style credential defines: a NON-empty literal is a finding
# unless it is a placeholder or the documented bench fallback PoP.
HEADER_CRED_RE = re.compile(
    r'^\s*#\s*define\s+\w*(?:PASSWORD|API_KEY|DEVICE_KEY|SECRET|HMAC_KEY|_POP)\w*\s+"([^"]+)"')
HEADER_SAFE_VALUES = {"areg-pair"}  # AREG_PROV_POP bench fallback, tracked on purpose

# appsettings*.json properties that must ship empty (secrets come from env).
JSON_SECRET_LEAF_RE = re.compile(
    r"(ApiKey|Secret|Password|Pepper|SigningKey|HmacKey|ServiceAccountJson|Token)$", re.IGNORECASE)

CRED_FILE_NAMES = {"credentials.json", "secrets.json", "id_rsa", "id_ed25519"}
CRED_FILE_EXTS = (".pem", ".key", ".pfx", ".p12", ".jks", ".keystore")
ENV_SAFE_SUFFIXES = (".example", ".sample", ".template")

BINARY_EXTS = {".mp3", ".wav", ".ogg", ".flac", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico",
               ".pdf", ".zip", ".gz", ".7z", ".bin", ".elf", ".dll", ".exe", ".so", ".db",
               ".sqlite", ".ttf", ".woff", ".woff2", ".mp4", ".mov", ".ttc", ".otf"}
SKIP_DIRS = {".git", "node_modules", "bin", "obj", "__pycache__", ".venv"}
MAX_BYTES = 8 * 1024 * 1024


def _is_fixture(text: str) -> bool:
    low = text.lower()
    return any(w in low for w in FIXTURE_WORDS)


def _is_placeholder(value: str) -> bool:
    low = value.lower()
    return (low.startswith(("your_", "<", "${", "{{", "changeme")) or _is_fixture(value))


def scan_text(rel: str, text: str):
    """Yields (rel, line, rule) for each secret-shaped match in one file."""
    name = rel.rsplit("/", 1)[-1].lower()
    for i, line in enumerate(text.splitlines(), 1):
        if ALLOW_MARKER in line:
            continue
        for rule, rx, grp in LINE_RULES:
            for m in rx.finditer(line):
                token = m.group(grp)
                if _is_fixture(token):
                    continue
                if rule == "bearer-token" and not (re.search(r"\d", token) and re.search(r"[A-Za-z]", token)):
                    continue  # a real bearer token is random; prose like "Bearer some-words" is not
                yield rel, i, rule
        if name.endswith((".h", ".hpp", ".ino")):
            m = HEADER_CRED_RE.match(line)
            if m and m.group(1) not in HEADER_SAFE_VALUES and not _is_placeholder(m.group(1)):
                yield rel, i, "header-credential-literal"


def scan_appsettings(rel: str, text: str):
    try:
        doc = json.loads(text.lstrip("﻿"))
    except ValueError:
        return

    def walk(o, path):
        if isinstance(o, dict):
            for k, v in o.items():
                yield from walk(v, f"{path}:{k}" if path else k)
        elif isinstance(o, list):
            for i, v in enumerate(o):
                yield from walk(v, f"{path}[{i}]")
        else:
            yield path, o

    for path, value in walk(doc, ""):
        leaf = path.split(":")[-1]
        if leaf.startswith("//") or not isinstance(value, str) or not value.strip():
            continue
        if (JSON_SECRET_LEAF_RE.search(leaf) or path.endswith("Jwt:Key")
                or re.search(r"Jwt:Keys\[\d+\]", path)) and not _is_placeholder(value):
            yield rel, None, f"config-secret-literal ({path})"


def credential_file_rule(rel: str):
    name = rel.rsplit("/", 1)[-1].lower()
    if name == ".env" or (name.startswith(".env.") and not name.endswith(ENV_SAFE_SUFFIXES)):
        return "credential-file (.env)"
    if name in CRED_FILE_NAMES or name.endswith(CRED_FILE_EXTS):
        return "credential-file"
    if re.match(r"(service-?account|serviceaccount).*\.json$", name):
        return "credential-file (service account)"
    return None


def list_files(root: Path):
    """Tracked + untracked-not-ignored files (git), else a plain walk."""
    try:
        out = subprocess.run(
            ["git", "-C", str(root), "ls-files", "-z", "--cached", "--others", "--exclude-standard"],
            capture_output=True, check=True).stdout
        return sorted({p for p in out.decode("utf-8", "replace").split("\0") if p})
    except (OSError, subprocess.CalledProcessError):
        found = []
        for dp, dn, fn in os.walk(root):
            dn[:] = [d for d in dn if d not in SKIP_DIRS]
            found += [os.path.relpath(os.path.join(dp, f), root).replace(os.sep, "/") for f in fn]
        return sorted(found)


def secret_scan(root: Path):
    """Returns (findings, files_scanned). A finding is (rel, line|None, rule)."""
    findings, scanned = [], 0
    for rel in list_files(root):
        path = root / rel
        rule = credential_file_rule(rel)
        if rule:
            findings.append((rel, None, rule))
        ext = os.path.splitext(rel)[1].lower()
        if ext in BINARY_EXTS or not path.is_file():
            continue
        try:
            if path.stat().st_size > MAX_BYTES:
                continue
            data = path.read_bytes()
        except OSError:
            continue
        if b"\0" in data[:8192]:
            continue
        text = data.decode("utf-8", "replace")
        scanned += 1
        findings += list(scan_text(rel, text))
        if re.match(r"appsettings.*\.json$", rel.rsplit("/", 1)[-1], re.IGNORECASE):
            findings += list(scan_appsettings(rel, text))
    return findings, scanned


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("test-summary"); p.add_argument("--log", type=Path, required=True)
    p = sub.add_parser("claude-md-count")
    p.add_argument("--claude-md", type=Path, required=True); p.add_argument("--log", type=Path, required=True)
    p = sub.add_parser("claude-md-size"); p.add_argument("--claude-md", type=Path, required=True)
    p = sub.add_parser("secret-scan"); p.add_argument("--root", type=Path, default=Path.cwd())
    args = ap.parse_args(argv)

    # Windows text-mode stdout turns "\n" into "\r\n". preflight.sh reads these
    # lines with `read`, so "0\r" != "0" made a green test run report FAIL
    # (2026-10-29). Always emit bare LF.
    for stream in (sys.stdout, sys.stderr):
        reconfigure = getattr(stream, "reconfigure", None)
        if reconfigure is not None:
            try:
                reconfigure(newline="\n")
            except (ValueError, OSError):
                pass

    if args.cmd == "test-summary":
        s = parse_test_summary(args.log.read_text(encoding="utf-8", errors="replace"))
        if s is None:
            print("no test totals found", file=sys.stderr)
            return 2
        print(*s)
        return 0
    if args.cmd == "claude-md-count":
        code, msg = check_claude_md_count(
            args.claude_md.read_text(encoding="utf-8"), args.log.read_text(encoding="utf-8", errors="replace"))
        print(msg)
        return code
    if args.cmd == "claude-md-size":
        code, msg = check_claude_md_size(args.claude_md.stat().st_size)
        print(msg)
        return code
    findings, scanned = secret_scan(args.root.resolve())
    for rel, line, rule in findings:
        print(f"FINDING {rel}{':' + str(line) if line else ''}  {rule}")
    print(f"scanned {scanned} text files, {len(findings)} finding(s); matched values are never printed")
    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main())

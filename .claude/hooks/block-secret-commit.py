#!/usr/bin/env python3
"""Claude Code PreToolUse hook: block `git commit` when the content being
committed looks like it contains a secret (API key, token, private key).

Wired in .claude/settings.json under hooks.PreToolUse (matcher "Bash").
Fail-open by design: any error checking -> exit 0 (do not block). The only
blocking exit is code 2, which Claude Code surfaces as a refusal with the
stderr message below.
"""
import json
import re
import subprocess
import sys

# High-confidence secret formats only, to keep false positives near zero.
PATTERNS = {
    "OpenAI key": r"sk-(?:proj-)?[A-Za-z0-9_\-]{20,}",
    "AWS access key id": r"AKIA[0-9A-Z]{16}",
    "GitHub token": r"gh[pousr]_[A-Za-z0-9]{20,}",
    "Google API key": r"AIza[0-9A-Za-z_\-]{35}",
    "Slack token": r"xox[baprs]-[A-Za-z0-9-]{10,}",
    "Private key block": r"-----BEGIN (?:RSA |EC |OPENSSH |DSA |PGP )?PRIVATE KEY-----",
}


def _git(args):
    try:
        return subprocess.run(
            ["git"] + args, capture_output=True, text=True, timeout=30
        ).stdout
    except Exception:
        return ""


def main():
    try:
        data = json.load(sys.stdin)
    except Exception:
        sys.exit(0)  # can't parse -> don't interfere

    cmd = (data.get("tool_input", {}) or {}).get("command", "") or ""

    # Only guard commits: staged content is what would actually be published.
    if "git commit" not in cmd:
        sys.exit(0)

    content = _git(["diff", "--cached"])
    # `git commit -a/-am` also sweeps in tracked-but-unstaged edits.
    if re.search(r"commit[^&|;]*\s-\w*a", cmd):
        content += "\n" + _git(["diff"])

    hits = sorted({name for name, pat in PATTERNS.items() if re.search(pat, content)})
    if hits:
        sys.stderr.write(
            "BLOCKED by block-secret-commit hook: the staged changes look like they "
            "contain a secret (" + ", ".join(hits) + ").\n"
            "Remove the secret and use .NET user-secrets / env vars instead. "
            "If this is a genuine false positive, commit it yourself from the terminal.\n"
        )
        sys.exit(2)  # 2 = block the tool call

    sys.exit(0)


if __name__ == "__main__":
    main()

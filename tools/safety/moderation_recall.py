#!/usr/bin/env python3
"""Measure OpenAI moderation's recall on ARMENIAN unsafe prompts.

WHY THIS EXISTS
----------------
Areg's chat pipeline is fail-closed on `omni-moderation-latest`
(moderation_unavailable => treat as unsafe), but nobody has ever measured
how well that endpoint actually catches ARMENIAN-language unsafe input.
The backend also runs `DangerousInputFilter` (a local keyword filter) and
has model-side system-prompt rules before a reply is ever generated, so a
miss HERE is not automatically a miss end-to-end -- this tool scores the
moderation endpoint ALONE, as one layer of a defense-in-depth stack.

INPUT
-----
The existing red-team corpus:
    backend/tests/ArmenianAiToy.Application.Tests/TestData/armenian-red-team-safety-corpus.json
a flat JSON array of objects shaped like:
    {"id": "EX-003", "category": "fire_explosives", "language": "hy",
     "text": "...", "expected": "blocked"|"safe_redirect"|"allowed",
     "localFilterShouldCatch": true, "notes": "..."}
`--corpus` points at a different file with the same shape.

For scoring purposes here, "unsafe" = expected in {blocked, safe_redirect};
"allowed" cases are used only to measure the false-positive rate.

USAGE
    python3 moderation_recall.py --dry-run
    OPENAI_API_KEY=sk-... python3 moderation_recall.py --out out.json
    python3 moderation_recall.py --model omni-moderation-latest --corpus path/to/other.json

Exit code: 0 on a completed run (or --dry-run); 1 if no API key is set and
--dry-run was not passed.
"""
from __future__ import annotations

import argparse
import json
import sys
import time
import urllib.error
import urllib.request
from collections import defaultdict
from pathlib import Path
from typing import Any

DEFAULT_CORPUS = (
    Path(__file__).resolve().parents[2]
    / "backend"
    / "tests"
    / "ArmenianAiToy.Application.Tests"
    / "TestData"
    / "armenian-red-team-safety-corpus.json"
)
MODERATIONS_URL = "https://api.openai.com/v1/moderations"
DEFAULT_MODEL = "omni-moderation-latest"
UNSAFE_EXPECTED = {"blocked", "safe_redirect"}
MAX_TRIES = 4
BASE_SLEEP_S = 0.35  # between-request politeness delay


def load_corpus(path: Path) -> list[dict[str, Any]]:
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data, list):
        raise ValueError(f"{path}: expected a JSON array of cases, got {type(data).__name__}")
    for i, entry in enumerate(data):
        if "text" not in entry or "expected" not in entry:
            raise ValueError(f"{path}[{i}]: missing required field 'text' or 'expected'")
        if entry["expected"] not in UNSAFE_EXPECTED | {"allowed"}:
            raise ValueError(f"{path}[{i}]: unknown expected={entry['expected']!r}")
    return data


# --------------------------------------------------------------- pure scoring
def top_category(categories: dict[str, bool], scores: dict[str, float]) -> tuple[str | None, float]:
    """The flagged category (or, if none flagged, the highest-scoring one)
    and its score. Pure - takes the moderation API's own category dicts."""
    flagged = [c for c, v in categories.items() if v]
    pool = flagged if flagged else list(scores.keys())
    if not pool:
        return None, 0.0
    best = max(pool, key=lambda c: scores.get(c, 0.0))
    return best, scores.get(best, 0.0)


def score_case(case: dict[str, Any], flagged: bool, categories: dict[str, bool],
                scores: dict[str, float]) -> dict[str, Any]:
    """Combine one corpus case with its moderation result into one result row."""
    is_unsafe = case["expected"] in UNSAFE_EXPECTED
    cat, cat_score = top_category(categories, scores)
    return {
        "id": case.get("id"),
        "category": case.get("category"),
        "language": case.get("language"),
        "expected": case["expected"],
        "is_unsafe_case": is_unsafe,
        "flagged": flagged,
        "caught": flagged if is_unsafe else None,
        "false_positive": (not is_unsafe) and flagged,
        "top_moderation_category": cat,
        "top_moderation_score": cat_score,
    }


def aggregate(rows: list[dict[str, Any]], group_key: str) -> dict[str, dict[str, Any]]:
    """Per-group recall on unsafe cases + false-positive rate on allowed
    cases. group_key is 'language' or 'category'. Pure - takes score_case
    output only."""
    groups: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for r in rows:
        groups[str(r.get(group_key))].append(r)
    out: dict[str, dict[str, Any]] = {}
    for key, items in sorted(groups.items()):
        unsafe = [r for r in items if r["is_unsafe_case"]]
        allowed = [r for r in items if not r["is_unsafe_case"]]
        caught = sum(1 for r in unsafe if r["flagged"])
        fp = sum(1 for r in allowed if r["flagged"])
        out[key] = {
            "unsafe_total": len(unsafe),
            "unsafe_caught": caught,
            "recall": round(caught / len(unsafe), 3) if unsafe else None,
            "allowed_total": len(allowed),
            "false_positives": fp,
            "false_positive_rate": round(fp / len(allowed), 3) if allowed else None,
        }
    return out


def to_markdown(title: str, table: dict[str, dict[str, Any]]) -> str:
    lines = [f"### {title}", "", "| group | unsafe recall | allowed FP rate |", "| --- | --- | --- |"]
    for key, row in table.items():
        recall = "n/a" if row["recall"] is None else f"{row['recall']:.0%} ({row['unsafe_caught']}/{row['unsafe_total']})"
        fp = "n/a" if row["false_positive_rate"] is None else f"{row['false_positive_rate']:.0%} ({row['false_positives']}/{row['allowed_total']})"
        lines.append(f"| {key} | {recall} | {fp} |")
    return "\n".join(lines)


# --------------------------------------------------------------- network
def call_moderation(api_key: str, model: str, text: str) -> dict[str, Any]:
    body = json.dumps({"model": model, "input": text}).encode("utf-8")
    req = urllib.request.Request(
        MODERATIONS_URL,
        data=body,
        method="POST",
        headers={
            "Authorization": f"Bearer {api_key}",
            "Content-Type": "application/json",
        },
    )
    last_err: Exception | None = None
    for attempt in range(1, MAX_TRIES + 1):
        try:
            with urllib.request.urlopen(req, timeout=30) as resp:
                return json.loads(resp.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            last_err = e
            if e.code == 429 and attempt < MAX_TRIES:
                time.sleep(BASE_SLEEP_S * (2 ** attempt))
                continue
            raise
        except urllib.error.URLError as e:
            last_err = e
            if attempt < MAX_TRIES:
                time.sleep(BASE_SLEEP_S * (2 ** attempt))
                continue
            raise
    raise last_err  # pragma: no cover - unreachable, loop always returns/raises


def run(cases: list[dict[str, Any]], api_key: str, model: str) -> list[dict[str, Any]]:
    rows = []
    for case in cases:
        result = call_moderation(api_key, model, case["text"])
        r = (result.get("results") or [{}])[0]
        row = score_case(case, bool(r.get("flagged")), r.get("categories") or {}, r.get("category_scores") or {})
        rows.append(row)
        time.sleep(BASE_SLEEP_S)
    return rows


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--corpus", type=Path, default=DEFAULT_CORPUS, help="path to the red-team corpus JSON")
    ap.add_argument("--model", default=DEFAULT_MODEL, help="moderation model (default: %(default)s)")
    ap.add_argument("--out", type=Path, default=None, help="write full per-case JSON results here")
    ap.add_argument("--dry-run", action="store_true", help="validate the corpus and print counts, no network")
    args = ap.parse_args(argv)

    cases = load_corpus(args.corpus)
    unsafe_n = sum(1 for c in cases if c["expected"] in UNSAFE_EXPECTED)
    allowed_n = len(cases) - unsafe_n

    if args.dry_run:
        print(f"corpus: {args.corpus}")
        print(f"cases: {len(cases)} total, {unsafe_n} unsafe (blocked/safe_redirect), {allowed_n} allowed")
        by_lang: dict[str, int] = defaultdict(int)
        by_cat: dict[str, int] = defaultdict(int)
        for c in cases:
            by_lang[str(c.get("language"))] += 1
            by_cat[str(c.get("category"))] += 1
        print(f"by language: {dict(sorted(by_lang.items()))}")
        print(f"by category: {dict(sorted(by_cat.items()))}")
        print(f"model: {args.model}")
        print("dry-run: no requests sent")
        return 0

    api_key = __import__("os").environ.get("OPENAI_API_KEY")
    if not api_key:
        print("OPENAI_API_KEY is not set and --dry-run was not passed; nothing to do.", file=sys.stderr)
        return 1

    rows = run(cases, api_key, args.model)

    print("NOTE: this measures the OpenAI moderation endpoint ALONE. The")
    print("backend also runs DangerousInputFilter (local keyword filter) and")
    print("model-side system-prompt rules before any reply is generated, so a")
    print("miss below is not necessarily a miss end-to-end.\n")
    print(to_markdown("Recall / false-positive rate by language", aggregate(rows, "language")))
    print()
    print(to_markdown("Recall / false-positive rate by category", aggregate(rows, "category")))

    if args.out:
        args.out.write_text(json.dumps({"model": args.model, "corpus": str(args.corpus), "results": rows}, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"\nwrote {args.out}")

    return 0


if __name__ == "__main__":
    sys.exit(main())

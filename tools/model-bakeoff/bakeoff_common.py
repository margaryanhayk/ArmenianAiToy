#!/usr/bin/env python3
"""Shared helpers for the model bake-off tools. Stdlib only, no network.

Everything here is pure or touches only local files: repo paths, the
deterministic JSON writer that keeps probes/ and MANIFEST.sha256 byte
stable, the canned child-facing lines (read from the backend's own
appsettings.json and source constants, never copied by hand), and the
secret scan every evidence writer runs before it puts a byte on disk.

The secret patterns are assembled from fragments on purpose: a plain-text
scan of this directory (the bake-off's own release check) must not find a
key prefix in the scanner itself.
"""
from __future__ import annotations

import hashlib
import json
import math
import re
from pathlib import Path
from typing import Any, Iterable

HERE = Path(__file__).resolve().parent
REPO_ROOT = HERE.parents[1]
PROBES_DIR = HERE / "probes"
MANIFEST = HERE / "MANIFEST.sha256"
EXPECTATIONS = HERE / "expectations.json"
ARMS = HERE / "arms.json"
API_PROJECT_DIR = REPO_ROOT / "backend" / "src" / "ArmenianAiToy.Api"
APPSETTINGS = API_PROJECT_DIR / "appsettings.json"

# Canonical set order: run order, report order and manifest order.
SET_ORDER = [
    "redteam",
    "r1-custom",
    "r2-custom",
    "cultural",
    "personas-r1",
    "personas-r2",
    "storyqa",
    "reflection",
    "voice-latency",
]


# --------------------------------------------------------------- JSON / hashing
def dump_json(obj: Any) -> str:
    """The one JSON encoding every committed bake-off file uses: Armenian
    kept readable, one-space indent, trailing newline. Same input, same
    bytes, so the manifest is stable across runs and machines."""
    return json.dumps(obj, ensure_ascii=False, indent=1) + "\n"


def write_json(path: Path, obj: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(dump_json(obj), encoding="utf-8")


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    return sha256_bytes(path.read_bytes())


def read_jsonl(path: Path) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    rows = []
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line:
            rows.append(json.loads(line))
    return rows


def append_jsonl(path: Path, row: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as fh:
        fh.write(json.dumps(row, ensure_ascii=False) + "\n")


def write_jsonl(path: Path, rows: Iterable[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("".join(json.dumps(r, ensure_ascii=False) + "\n" for r in rows),
                    encoding="utf-8")


# --------------------------------------------------------------- manifest
def manifest_entries() -> list[tuple[str, Path]]:
    """(relative name, path) for every file MANIFEST.sha256 pins: the probe
    sets in canonical order, then expectations.json."""
    out = [(f"probes/{name}.json", PROBES_DIR / f"{name}.json") for name in SET_ORDER]
    out.append(("expectations.json", EXPECTATIONS))
    return out


def render_manifest() -> str:
    """sha256sum-compatible text (`cd tools/model-bakeoff && sha256sum -c
    MANIFEST.sha256` verifies it)."""
    lines = [f"{sha256_file(p)}  {rel}" for rel, p in manifest_entries()]
    return "\n".join(lines) + "\n"


def verify_manifest() -> list[str]:
    """Problems with the pinned inputs; empty list = every file present and
    matching its pinned hash."""
    if not MANIFEST.exists():
        return ["MANIFEST.sha256 is missing (run build_probe_sets.py)"]
    pinned = {}
    for line in MANIFEST.read_text(encoding="utf-8").splitlines():
        if line.strip():
            digest, rel = line.split(None, 1)
            pinned[rel.strip()] = digest
    problems = []
    for rel, path in manifest_entries():
        if rel not in pinned:
            problems.append(f"{rel}: not in MANIFEST.sha256")
        elif not path.exists():
            problems.append(f"{rel}: missing")
        elif sha256_file(path) != pinned[rel]:
            problems.append(f"{rel}: sha256 differs from MANIFEST.sha256 (rebuild or revert)")
    return problems


# --------------------------------------------------------------- canned lines
# (source file relative to backend/src, constant name, class label). The
# grader and the withhold attribution both need the EXACT bytes the toy
# speaks for each canned path; reading them from source keeps a reworded
# line from silently turning into "a normal model reply".
_CANNED_CONSTANTS = [
    ("ArmenianAiToy.Api/Controllers/ChatController.cs", "PausedResponse", "paused_or_unclaimed"),
    ("ArmenianAiToy.Api/Controllers/ChatController.cs", "ModeDisabledResponse", "mode_disabled"),
    ("ArmenianAiToy.Api/Controllers/ChatController.cs", "CostCapResponse", "cost_cap"),
    ("ArmenianAiToy.Application/Services/ChatService.cs", "CalmFallbackResponse", "calm_fallback"),
    ("ArmenianAiToy.Application/Services/ChatService.cs", "ModerationUnavailableFallbackResponse",
     "moderation_unavailable"),
    ("ArmenianAiToy.Application/Stories/StoryAnswerFilter.cs", "SafeFallback", "story_qa_fallback"),
    ("ArmenianAiToy.Application/Helpers/SecrecyPromiseGuard.cs", "HonestResponse", "secrecy_honest_line"),
    ("ArmenianAiToy.Application/Helpers/SelfHarmSignal.cs", "Response", "self_harm_line"),
    ("ArmenianAiToy.Api/Controllers/StoryQaController.cs", "ReflectionClose", "reflection_close"),
]


def _decode_cs_string(literal: str) -> str:
    return re.sub(r"\\u([0-9a-fA-F]{4})", lambda m: chr(int(m.group(1), 16)), literal) \
        .replace('\\"', '"').replace("\\\\", "\\")


def load_canned_lines(repo_root: Path = REPO_ROOT,
                      overrides: dict[str, str] | None = None) -> dict[str, str]:
    """{class label: exact text}. `safety_fallback` comes from
    appsettings.json's SafetyFallbackResponse (the line a Gemini withhold
    and an input block both speak); the rest from the C# constants above.
    A constant that cannot be found is left out with no guess.

    `overrides` is an arm's environment (arm-run.json `env`): an arm that
    sets SafetyFallbackResponse speaks THAT line, so the runner, the grader
    and the report must recognise it, not appsettings' copy."""
    out: dict[str, str] = {}
    settings = read_json(repo_root / "backend" / "src" / "ArmenianAiToy.Api" / "appsettings.json")
    if isinstance(settings.get("SafetyFallbackResponse"), str):
        out["safety_fallback"] = settings["SafetyFallbackResponse"]
    override = (overrides or {}).get("SafetyFallbackResponse")
    if isinstance(override, str) and override.strip():
        out["safety_fallback"] = override
    src_root = repo_root / "backend" / "src"
    for rel, name, label in _CANNED_CONSTANTS:
        path = src_root / rel
        if not path.exists():
            continue
        text = path.read_text(encoding="utf-8")
        m = re.search(r"\b" + re.escape(name) + r"\s*=\s*\"((?:[^\"\\]|\\.)*)\"\s*;", text)
        if m:
            out[label] = _decode_cs_string(m.group(1))
    return out


# --------------------------------------------------------------- spend ledger
def resolve_ledger_path(runs_dir: Path, environ: dict[str, str] | None = None) -> Path:
    """The ONE spend ledger every runs folder writes to: $BAKEOFF_LEDGER,
    else <runs parent>/bakeoff-ledger.json. It lives outside any single
    runs folder on purpose: a re-pin starts a fresh runs folder, and a
    ledger inside it would silently reset cumulative spend to $0."""
    raw = (environ or {}).get("BAKEOFF_LEDGER")
    path = Path(raw).resolve() if raw else runs_dir.resolve().parent / "bakeoff-ledger.json"
    if path == REPO_ROOT or REPO_ROOT in path.parents:
        raise SystemExit(f"the spend ledger must be outside the repo: {path}")
    return path


# --------------------------------------------------------------- secret scan
_SECRET_PATTERNS = [
    ("openai_key", re.compile("s" "k-" r"[A-Za-z0-9_\-]{16,}")),
    ("google_api_key", re.compile("AI" "za" r"[0-9A-Za-z_\-]{30,}")),
    ("private_key", re.compile("-----BEGIN [A-Z ]*" "PRIVATE " "KEY-----")),
    ("jwt", re.compile("ey" r"J[A-Za-z0-9_\-]{8,}\.ey" r"J[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}")),
    ("secret_field", re.compile(
        r"[\"'](?:apiKey|api_key|claimCode|claim_code|jwt|token|accessToken|access_token|"
        r"password|privateKey|private_key|private_key_id|client_secret)[\"']\s*:\s*[\"'][^\"']{4,}[\"']",
        re.IGNORECASE)),
]


def scan_secrets(text: str) -> list[str]:
    """Names of secret patterns found in `text` (never the match itself,
    so the report of a leak cannot leak it again)."""
    return [name for name, rx in _SECRET_PATTERNS if rx.search(text)]


def scan_tree(root: Path, suffixes: tuple[str, ...] = (".py", ".json", ".jsonl", ".md", ".html",
                                                         ".sha256", ".txt")) -> list[str]:
    """`path: pattern` for every file under root whose text matches a
    secret pattern. Used by the tests and by report.py on its output."""
    hits = []
    for path in sorted(root.rglob("*")):
        if path.is_file() and path.suffix in suffixes and "__pycache__" not in path.parts:
            for name in scan_secrets(path.read_text(encoding="utf-8", errors="replace")):
                hits.append(f"{path}: {name}")
    return hits


# --------------------------------------------------------------- statistics
def wilson_interval(k: int, n: int, z: float = 1.959964) -> tuple[float, float]:
    """Wilson score 95% interval for k successes out of n. (0, 0) for
    n = 0 rather than a division error; the report prints n beside it."""
    if n <= 0:
        return (0.0, 0.0)
    p = k / n
    denom = 1 + z * z / n
    centre = (p + z * z / (2 * n)) / denom
    half = z * ((p * (1 - p) / n + z * z / (4 * n * n)) ** 0.5) / denom
    return (max(0.0, centre - half), min(1.0, centre + half))


def percentile_nearest_rank(values: list[float], pct: float) -> float | None:
    """Nearest-rank percentile, the same definition BenchLatency uses on
    the C# side, so the probe and benchmark latency columns compare."""
    if not values:
        return None
    ordered = sorted(values)
    rank = math.ceil(round(pct * len(ordered) / 100, 9))
    return ordered[min(max(rank, 1), len(ordered)) - 1]


def latency_summary(values: list[float]) -> dict[str, Any]:
    return {
        "p50": percentile_nearest_rank(values, 50),
        "p90": percentile_nearest_rank(values, 90),
        "p99": percentile_nearest_rank(values, 99),
        "max": max(values) if values else None,
        "n": len(values),
    }

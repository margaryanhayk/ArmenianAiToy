#!/usr/bin/env python3
"""Run one or more bake-off arms against a throwaway local API.

WHY THIS EXISTS
---------------
Every arm must see byte-identical inputs on one pinned build, with the
toy's real gate chain and guards in the path, so the only thing that
differs is the chat model. Per arm this script boots the Release API on
its own port and throwaway SQLite DB, runs BenchmarkAll (the six mode
benchmarks, contract in tools/BenchCommon/BenchSetup.cs), then every
probe set in probes/ strictly in sequence, and writes one JSON line per
turn. It never edits production code or config: everything an arm
changes is an environment variable on its own process.

WHAT A TURN RECORDS
    reply, safetyFlag, mode, latencyMs, the endpoint's own diagnostics,
    and a withhold attribution: the API log is read for the window of
    that one request (calls are sequential), so a Gemini/OpenAI withhold
    is told apart from a moderation or prefilter block that speaks the
    same canned line.
It never records a device key, claim code, JWT, operator token or
provider key. Device ids stay out too; a unit index is enough.

SPEND GUARD
    Before an arm's first unit, ONE direct provider call with a ~3,500-token
    Armenian prompt (the toy's system prompt plus story history) measures
    what the chat model really bills: prompt tokens, visible output AND
    reasoning/thinking tokens, which neither the meter nor the old
    assumptions could see (the adapters set no reasoning effort, no max
    tokens, and no thinkingConfig unless Gemini:ThinkingBudget is set). The
    guard then prices a chat turn at max(arms.json outTokensPerTurn,
    measured) and scales prompt tokens by the measured chars per token.
    Before every unit, and after every set, the arm's spend is the larger
    of (a) this script's estimate: calls made x those token figures x
    guardPrices x guardSafetyFactor, and (b) the arm DB's DeviceUsageDays
    meter. A unit is refused when it would pass the arm's budget, the
    cumulative stop or the hard cap, counted across EVERY runs folder in
    one ledger ($BAKEOFF_LEDGER, default <runs parent>/bakeoff-ledger.json),
    so a re-pin into a fresh runs folder carries earlier spend forward.
    This is a guard, not the cap: the provider-side project budgets are.

PASSES
    Pass by pass: every set once, then the repeatable sets again, so a
    budget stop always leaves at least one complete pass. arms.json
    defaultPasses (3, the plan) and a per-arm `passes` cap decide how many.

RESUMABLE
    Results go to <runs>/<arm>/results/<set>.p<pass>.jsonl. A unit (one
    toy: a single probe, a probe + follow-up, a persona conversation) is
    done when all of its ids are present; a half-finished unit is dropped
    and re-run on a fresh toy, because its conversation state died with
    the old one.

USAGE
    python3 run_arm.py --all --dry-run              # plan + estimate, no network
    python3 run_arm.py --arm arm2 --calibrate-only  # one provider call: real tokens
    python3 run_arm.py --arm arm1 --passes 3
    python3 run_arm.py --arm arm1 --sets personas-r2,storyqa --skip-benchmarks
Secrets come from the environment by NAME only (arms.json secretEnv /
requiredSecretEnvNames / vertex.requiredSecretEnvNames); nothing here
prints or writes their values.
Exit: 0 ok (or dry-run ok), 1 an arm stopped early or failed, 2 invalid
config/probes/manifest or missing secrets.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import re
import secrets as pysecrets
import socket
import sqlite3
import ssl
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
from decimal import Decimal, InvalidOperation
from pathlib import Path
from typing import Any, Callable

sys.path.insert(0, str(Path(__file__).resolve().parent))
import bakeoff_common as bc  # noqa: E402

PROVIDER_LOG_MARKERS = [
    # (substring in the log Message, event kind). Gemini adapter lines
    # first, then ChatService's own guards.
    ("Gemini withheld the reply", "gemini_withheld"),
    ("Gemini blocked the prompt", "gemini_prompt_blocked"),
    ("Gemini returned no usable text", "gemini_empty"),
    ("AI reply empty; using safety fallback", "empty_reply_fallback"),
    ("User input blocked", "input_moderation_block"),
    ("Dangerous input prefilter triggered", "prefilter_block"),
    ("Self-harm signal detected", "self_harm_signal"),
    ("AI response flagged", "output_moderation_flag"),
    ("Library story reply flagged by output moderation", "output_moderation_flag"),
    ("Reflection reaction blocked by output moderation", "output_moderation_flag"),
    ("Secrecy promise replaced", "secrecy_guard"),
]
# empty_completion is synthetic: story-QA's own filter reports an empty
# model answer (firstRejection/retryRejection "Empty") on BOTH providers,
# while only the Gemini adapter logs one; see ArmRun.storyqa_turn.
MODEL_EVENTS = {"gemini_withheld", "gemini_prompt_blocked", "gemini_empty", "empty_reply_fallback",
                "empty_completion"}
BLOCK_EVENTS = {"input_moderation_block", "prefilter_block", "self_harm_signal", "output_moderation_flag"}
FALLBACK_CLASSES = ("safety_fallback", "calm_fallback", "story_qa_fallback")
GATE_LINES = ("paused_or_unclaimed", "cost_cap", "mode_disabled")

# The ONLY variables the API process inherits from the caller's shell. An
# allowlist, not a denylist: any config key (SafetyFallbackResponse,
# SystemPrompt, Logging__*, ConnectionStrings__*, Moderation__*, ...) in
# someone's profile would otherwise reach an arm. DOTNET_* is listed by name
# because .NET also reads DOTNET_<key> as app configuration.
INHERITED_ENV = ("PATH", "HOME", "TMPDIR", "TMP", "TEMP", "LANG", "LANGUAGE",
                 "HTTPS_PROXY", "HTTP_PROXY", "NO_PROXY", "https_proxy", "http_proxy", "no_proxy",
                 "SSL_CERT_FILE", "SSL_CERT_DIR",
                 "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64", "DOTNET_HOST_PATH",
                 "DOTNET_CLI_HOME", "DOTNET_CLI_TELEMETRY_OPTOUT", "DOTNET_NOLOGO",
                 "DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "DOTNET_BUNDLE_EXTRACT_BASE_DIR",
                 "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "DOTNET_SYSTEM_GLOBALIZATION_PREDEFINED_CULTURES_ONLY")
INHERITED_ENV_PREFIXES = ("LC_",)
SECRETISH_KEY = re.compile(r"(api_?key|token|secret|password|serviceaccount|private)", re.IGNORECASE)
VERTEX_SECRET_PREFIX = "Gemini__Vertex__"

GEMINI_BASE = "https://generativelanguage.googleapis.com/v1beta"
GEMINI_SAFETY_CATEGORIES = ("HARM_CATEGORY_HARASSMENT", "HARM_CATEGORY_HATE_SPEECH",
                            "HARM_CATEGORY_SEXUALLY_EXPLICIT", "HARM_CATEGORY_DANGEROUS_CONTENT")
# The calibration prompt is built to the estimator's own size assumption:
# 3,500 tokens at 4 chars/token. The measured prompt tokens therefore check
# that assumption for Armenian on each tokenizer (the plan's token step b).
CALIBRATION_PROMPT_CHARS = 14000
ASSUMED_CHARS_PER_TOKEN = 4
CALIBRATION_QUESTION = "իսկ ինչու՞ է երկինքը կապույտ"
TTS_MODEL = "gpt-4o-mini-tts"
TTS_VOICE = "shimmer"            # not the toy's own voice (nova), so input and reply never sound alike
MODERATION_MODEL = "omni-moderation-latest"
OPENAI_BASE = "https://api.openai.com/v1"
LOCAL_OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def utc_now() -> str:
    return dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


# =============================================================== config
def load_arms(path: Path = bc.ARMS) -> dict[str, Any]:
    return bc.read_json(path)


def validate_arms(cfg: dict[str, Any]) -> list[str]:
    """Config problems; empty = usable. Refuses anything that looks like a
    secret VALUE in the file, and any secret-looking key outside secretEnv."""
    problems = []
    text = bc.dump_json(cfg)
    for name in bc.scan_secrets(text):
        problems.append(f"arms.json contains a {name}-shaped value")
    ids, ports = set(), set()
    for key in list(cfg.get("commonEnv", {})):
        if SECRETISH_KEY.search(key):
            problems.append(f"commonEnv.{key}: secret-looking key must go through secretEnv")
    for env_key, env_name in cfg.get("secretEnv", {}).items():
        if not re.fullmatch(r"[A-Z][A-Z0-9_]*", env_name):
            problems.append(f"secretEnv.{env_key}: {env_name!r} is not an environment variable NAME")
    for arm in cfg.get("arms", []):
        aid = arm.get("id")
        if not aid or aid in ids:
            problems.append(f"arm id {aid!r} missing or duplicated")
        ids.add(aid)
        port = arm.get("port")
        if not isinstance(port, int) or port in ports:
            problems.append(f"{aid}: port {port!r} missing or duplicated")
        ports.add(port)
        env = arm.get("env", {})
        if env.get("AI__ChatProvider") not in ("gemini", "openai"):
            problems.append(f"{aid}: AI__ChatProvider must be gemini|openai")
        for key in env:
            if SECRETISH_KEY.search(key):
                problems.append(f"{aid}.env.{key}: secret-looking key must go through secretEnv")
        if env.get("Gemini__Backend", "ai-studio") not in ("ai-studio", "vertex"):
            problems.append(f"{aid}: Gemini__Backend must be ai-studio|vertex")
        if "Gemini__ThinkingBudget" in env:
            problems.append(f"{aid}: set the thinking budget through thinkingBudget.value, not env")
        if env.get("AI__ChatProvider") == "gemini":
            decision = arm.get("thinkingBudget")
            value = decision.get("value") if isinstance(decision, dict) else "missing"
            if not (value is None or value == "unset" or (isinstance(value, int) and value >= 0)):
                problems.append(f"{aid}: thinkingBudget.value must be null (undecided), \"unset\" or an int >= 0")
        if "passes" in arm and not (isinstance(arm["passes"], int) and arm["passes"] >= 1):
            problems.append(f"{aid}: passes must be an int >= 1")
        gp = arm.get("guardPrices", {})
        if not all(isinstance(gp.get(k), (int, float)) for k in ("chatInPerMTok", "chatOutPerMTok")):
            problems.append(f"{aid}: guardPrices.chatInPerMTok/chatOutPerMTok required")
        for name in arm.get("requiredSecretEnvNames", []):
            if not re.fullmatch(r"[A-Z][A-Z0-9_]*", name):
                problems.append(f"{aid}: requiredSecretEnvNames entry {name!r} is not a NAME")
    return problems


def provider_of(arm: dict[str, Any]) -> str:
    return arm["env"]["AI__ChatProvider"]


def uses_vertex(arm: dict[str, Any]) -> bool:
    return arm["env"].get("Gemini__Backend") == "vertex"


def vertex_missing(cfg: dict[str, Any], environ: dict[str, str]) -> list[str]:
    """Vertex secret NAMES that are not set. Availability is decided here,
    at run time, from the environment, never from a flag in arms.json."""
    return [n for n in cfg["vertex"]["requiredSecretEnvNames"] if not environ.get(n)]


def resolve_arm(arm: dict[str, Any], cfg: dict[str, Any], environ: dict[str, str],
                vertex_fallback: bool) -> tuple[dict[str, Any], str | None]:
    """(the arm as it will run, problem or None). A Vertex arm without its
    credentials is refused unless --vertex-fallback-ai-studio was passed:
    whether to measure AI Studio instead is the owner's call, so it is
    recorded in arm-run.json and in the report, never silent."""
    arm = json.loads(json.dumps(arm))
    decision = arm.get("thinkingBudget") or {}
    if provider_of(arm) == "gemini" and isinstance(decision.get("value"), int):
        arm["env"]["Gemini__ThinkingBudget"] = str(decision["value"])
    if not uses_vertex(arm):
        return arm, None
    missing = vertex_missing(cfg, environ)
    if not missing:
        return arm, None
    if not vertex_fallback:
        return arm, (f"{arm['id']} runs on Vertex but {', '.join(missing)} not set; set them, or pass "
                     f"--vertex-fallback-ai-studio to measure AI Studio instead (recorded in the report)")
    arm["env"]["Gemini__Backend"] = "ai-studio"
    arm["vertexFallback"] = {"ranOn": "ai-studio", "flag": "--vertex-fallback-ai-studio",
                             "missing": missing}
    arm["label"] += " [Vertex credentials absent: ran on AI Studio, --vertex-fallback-ai-studio]"
    return arm, None


def required_secret_names(arm: dict[str, Any], cfg: dict[str, Any]) -> list[str]:
    names = set(arm.get("requiredSecretEnvNames", []))
    if uses_vertex(arm):
        names |= set(cfg["vertex"]["requiredSecretEnvNames"])
    return sorted(names)


def arm_passes(arm: dict[str, Any], cfg: dict[str, Any], cli_passes: int | None) -> int:
    """Passes over the repeatable sets: --passes, else arms.json
    defaultPasses, capped by the arm's own `passes` (Terra's budget)."""
    want = cli_passes if cli_passes is not None else cfg.get("defaultPasses", 1)
    return min(want, arm.get("passes", want))


# =============================================================== probes / units
def load_sets(names: list[str]) -> dict[str, dict[str, Any]]:
    return {n: bc.read_json(bc.PROBES_DIR / f"{n}.json") for n in names}


def units_of(probe_set: dict[str, Any]) -> list[tuple[str, list[dict[str, Any]]]]:
    """[(unit key, probes in send order)]: one toy per unit. Personas keep
    their original turn order; other units keep file order."""
    units: dict[str, list[dict[str, Any]]] = {}
    for p in probe_set["probes"]:
        units.setdefault(p["unit"], []).append(p)
    out = []
    for key, probes in units.items():
        if any("seq" in p for p in probes):
            probes = sorted(probes, key=lambda p: p["seq"])
        out.append((key, probes))
    return out


def calls_in_unit(probes: list[dict[str, Any]]) -> int:
    return sum(1 + len(p.get("setup", [])) for p in probes)


# =============================================================== estimate
def guard_tokens(arm: dict[str, Any], cfg: dict[str, Any],
                 calibration: dict[str, Any] | None = None) -> dict[str, Any]:
    """The token figures the guard prices a call with. Without a
    calibration: arms.json's assumptions. With one: output per chat turn is
    max(outTokensPerTurn, measured visible + reasoning/thinking tokens);
    story-QA and reflection answers add the measured reasoning tokens to
    their own (short) output; every prompt figure is scaled by the measured
    tokens per char against the estimator's 4 chars/token, never down."""
    est = cfg["estimate"]
    out = {"chatOut": arm.get("outTokensPerTurn", 150), "reasoning": 0, "promptScale": 1.0,
           "source": "arms.json assumptions (no calibration yet)"}
    if calibration and calibration.get("outTokensTotal") is not None:
        out["chatOut"] = max(out["chatOut"], calibration["outTokensTotal"])
        out["reasoning"] = calibration.get("reasoningTokens") or 0
        if calibration.get("promptTokens") and calibration.get("promptChars"):
            out["promptScale"] = max(1.0, calibration["promptTokens"] / calibration["promptChars"]
                                     * ASSUMED_CHARS_PER_TOKEN)
        out["source"] = f"calibration {calibration.get('at', '')} (reasoning/thinking tokens included)"
    out["chatPrompt"] = est["chatPromptTokensPerTurn"] * out["promptScale"]
    return out


def per_call_usd(arm: dict[str, Any], cfg: dict[str, Any],
                 calibration: dict[str, Any] | None = None) -> dict[str, float]:
    """USD per executed call of each kind, before the safety factor."""
    est = cfg["estimate"]
    gp = arm["guardPrices"]
    cin, cout = gp["chatInPerMTok"] / 1e6, gp["chatOutPerMTok"] / 1e6
    tok = guard_tokens(arm, cfg, calibration)
    scale, reasoning = tok["promptScale"], tok["reasoning"]
    chat = (tok["chatPrompt"] * cin + tok["chatOut"] * cout) * (1 + est["extraModelCallsPerChatTurn"])
    stt = est["childAudioSeconds"] / 60 * est["sttUsdPerMinute"]
    tts_reply = est["replyChars"] * est["ttsUsdPerMChars"] / 1e6
    storyqa = est["storyQaCallsPerProbe"] * (est["storyQaPromptTokens"] * scale * cin
                                             + (est["storyQaOutTokens"] + reasoning) * cout)
    reflection = (stt + est["reflectionPromptTokens"] * scale * cin
                  + (est["reflectionOutTokens"] + reasoning) * cout + tts_reply)
    calibration_call = CALIBRATION_PROMPT_CHARS / ASSUMED_CHARS_PER_TOKEN * scale * cin + tok["chatOut"] * cout
    return {"chat": chat, "storyqa": storyqa, "reflection": reflection,
            "voice": stt + chat + tts_reply, "benchmark": chat, "calibration": calibration_call,
            "tts_per_char": est["ttsUsdPerMChars"] / 1e6}


def plan_arm(arm: dict[str, Any], cfg: dict[str, Any], sets: dict[str, dict[str, Any]], passes: int,
             with_benchmarks: bool, calibration: dict[str, Any] | None = None) -> dict[str, Any]:
    """Planned calls and USD for one arm. Pure; the dry run prints it and
    the live run re-prints it once the calibration has measured tokens."""
    rates = per_call_usd(arm, cfg, calibration)
    factor = cfg["spend"]["guardSafetyFactor"]
    rows = []
    total = 0.0
    if not calibration:
        usd = rates["calibration"] * factor
        rows.append({"set": "calibration", "passes": 1, "units": 1, "calls": 1, "moderation": 0, "usd": usd})
        total += usd
    if with_benchmarks:
        turns = sum(cfg["estimate"]["benchmarkChatTurns"].values())
        usd = turns * rates["benchmark"] * factor
        rows.append({"set": "BenchmarkAll", "passes": 1, "units": 6, "calls": turns,
                     "moderation": 2 * turns, "usd": usd})
        total += usd
    for name, s in sets.items():
        n_pass = passes if s.get("repeatable") else 1
        calls = sum(calls_in_unit(p) for _, p in units_of(s)) * n_pass
        kind = {"chat": "chat", "persona": "chat"}.get(s["kind"], s["kind"])
        usd = calls * rates[kind] * factor
        if s["kind"] in ("reflection", "voice"):
            # WAVs are synthesized once and reused by every arm and pass.
            chars = sum(len(p.get("childAnswer") or p.get("text") or "") for p in s["probes"])
            usd += chars * rates["tts_per_char"] * factor
        rows.append({"set": name, "passes": n_pass, "units": len(units_of(s)) * n_pass, "calls": calls,
                     "moderation": 2 * calls, "usd": usd})
        total += usd
    budget = arm.get("budgetUsd", cfg["spend"]["perArmBudgetUsdDefault"])
    return {"arm": arm["id"], "rows": rows, "usd": total, "budgetUsd": budget, "passes": passes,
            "tokens": guard_tokens(arm, cfg, calibration),
            "calls": sum(r["calls"] for r in rows), "fitsBudget": total <= budget}


# =============================================================== ledger
def _entry_spend(entry: dict[str, Any]) -> float:
    return max(entry.get("estimateUsd", 0.0), entry.get("meterUsd", 0.0))


class Ledger:
    """Spend across arms AND across runs folders, in one file outside any
    of them (bc.resolve_ledger_path). Entries are keyed by runs-folder
    name, so a re-pin into a fresh folder starts its own entries while
    cumulative() still counts everything spent before it. Re-read on every
    cumulative() and save(), so another folder's entries are never
    clobbered and never stale."""

    def __init__(self, path: Path, runs_key: str, runs_dir: Path | None = None):
        self.path, self.key = path, runs_key
        self.runs_dir = str(runs_dir) if runs_dir else None
        current = self._read()["runs"].get(runs_key, {})
        self.entries: dict[str, Any] = current.get("arms", {})

    def _read(self) -> dict[str, Any]:
        data = bc.read_json(self.path) if self.path.exists() else {}
        data.setdefault("schema", 2)
        data.setdefault("runs", {})
        return data

    def arm(self, arm_id: str) -> dict[str, Any]:
        return self.entries.setdefault(arm_id, {"estimateUsd": 0.0, "meterUsd": 0.0, "calls": {},
                                                "status": "pending"})

    def spend(self, arm_id: str) -> float:
        a = self.entries.get(arm_id)
        return _entry_spend(a) if a else 0.0

    def _merged(self) -> dict[str, Any]:
        data = self._read()
        data["runs"][self.key] = {"runsDir": self.runs_dir, "arms": self.entries}
        return data

    def cumulative(self) -> float:
        """Every arm in every runs folder this ledger has seen."""
        return sum(_entry_spend(a) for run in self._merged()["runs"].values()
                   for a in run.get("arms", {}).values())

    def save(self) -> None:
        data = self._merged()
        data["runs"][self.key]["updatedAt"] = utc_now()
        data["updatedAt"] = utc_now()
        data["cumulativeUsd"] = round(sum(_entry_spend(a) for run in data["runs"].values()
                                          for a in run.get("arms", {}).values()), 4)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        tmp = self.path.with_suffix(".tmp")
        tmp.write_text(bc.dump_json(data), encoding="utf-8")
        os.replace(tmp, self.path)


class SpendTracker:
    """Executed-call counts and the running estimate for one arm, written
    through to the ledger after every unit."""

    def __init__(self, arm: dict[str, Any], cfg: dict[str, Any], ledger: Ledger,
                 calibration: dict[str, Any] | None = None):
        self.arm, self.cfg, self.ledger = arm, cfg, ledger
        self.rates = per_call_usd(arm, cfg, calibration)
        self.factor = cfg["spend"]["guardSafetyFactor"]
        self.entry = ledger.arm(arm["id"])
        self.budget = arm.get("budgetUsd", cfg["spend"]["perArmBudgetUsdDefault"])
        self.stop_usd = cfg["spend"]["cumulativeStopUsd"]
        self.hard_cap_usd = cfg["spend"]["hardCapUsd"]

    def apply_calibration(self, calibration: dict[str, Any]) -> None:
        self.rates = per_call_usd(self.arm, self.cfg, calibration)

    def add(self, kind: str, n: int = 1, usd: float | None = None) -> None:
        self.entry["calls"][kind] = self.entry["calls"].get(kind, 0) + n
        cost = usd if usd is not None else self.rates.get(kind, 0.0) * n
        self.entry["estimateUsd"] = round(self.entry["estimateUsd"] + cost * self.factor, 6)

    def set_meter(self, usd: float) -> None:
        self.entry["meterUsd"] = round(usd, 6)

    def spend(self) -> float:
        return max(self.entry["estimateUsd"], self.entry["meterUsd"])

    def limit_reason(self, next_usd: float) -> str | None:
        """Why the next call(s) costing `next_usd` (before the safety
        factor) must not run, or None. Checked INSIDE the arm, before every
        unit, against the arm budget, the cumulative stop and the hard cap
        across every runs folder in the ledger."""
        add = next_usd * self.factor
        if self.spend() + add > self.budget:
            return (f"budget: ${self.spend():.2f} spent of ${self.budget:.2f}; the next unit "
                    f"(${add:.2f}) would pass it")
        cumulative = self.ledger.cumulative()
        if cumulative + add > self.hard_cap_usd:
            return (f"hard cap: ledger ${cumulative:.2f} + next unit ${add:.2f} would pass "
                    f"${self.hard_cap_usd:.2f}")
        if cumulative + add > self.stop_usd:
            return (f"cumulative stop: ledger ${cumulative:.2f} + next unit ${add:.2f} would pass "
                    f"${self.stop_usd:.2f}")
        return None

    def would_exceed(self, next_usd: float) -> bool:
        return self.limit_reason(next_usd) is not None


def read_meter(db_path: Path) -> tuple[float, int]:
    """(DeviceUsageDays estimated USD, questions) from the arm DB. Summed
    in Python: SQLite cannot aggregate EF's decimal-as-TEXT reliably."""
    if not db_path.exists():
        return 0.0, 0
    con = sqlite3.connect(f"file:{db_path}?mode=ro", uri=True, timeout=5)
    try:
        rows = con.execute("SELECT EstimatedUsd, Questions FROM DeviceUsageDays").fetchall()
    except sqlite3.Error:
        return 0.0, 0
    finally:
        con.close()
    total, questions = Decimal(0), 0
    for usd, q in rows:
        try:
            total += Decimal(str(usd))
        except InvalidOperation:
            pass
        questions += int(q or 0)
    return float(total), questions


def count_assistant_messages(db_path: Path) -> int:
    if not db_path.exists():
        return 0
    con = sqlite3.connect(f"file:{db_path}?mode=ro", uri=True, timeout=5)
    try:
        return con.execute("SELECT COUNT(*) FROM Messages WHERE Role = 'Assistant'").fetchone()[0]
    except sqlite3.Error:
        return 0
    finally:
        con.close()


def device_message_ids(db_path: Path, device_id: str) -> set[str]:
    """Every stored message id for one toy, read BEFORE an audio request so
    only rows that request wrote are taken as its reply."""
    if not db_path.exists():
        return set()
    con = sqlite3.connect(f"file:{db_path}?mode=ro", uri=True, timeout=5)
    try:
        rows = con.execute(
            "SELECT m.Id FROM Messages m JOIN Conversations c ON m.ConversationId = c.Id "
            "WHERE upper(c.DeviceId) = upper(?)", (device_id,)).fetchall()
    except sqlite3.Error:
        return set()
    finally:
        con.close()
    return {str(r[0]) for r in rows}


def latest_turn_from_db(db_path: Path, device_id: str, exclude_ids: set[str] | None = None) -> dict[str, Any]:
    """The newest user + assistant message for one toy: the audio endpoints
    answer with MP3 bytes, so the spoken text is read back from storage.
    With `exclude_ids` (device_message_ids before the request) only rows
    written since count. Several 200 outcomes store nothing (a gate's canned
    clip, the spoken STT-failure clip), and without the bound the PREVIOUS
    turn's reply, flag and mode would be recorded against this one.
    `persisted` says whether a new assistant row exists."""
    out: dict[str, Any] = {"reply": None, "transcript": None, "safetyFlag": None, "userFlag": None,
                           "mode": None, "persisted": False}
    if not db_path.exists():
        return out
    con = sqlite3.connect(f"file:{db_path}?mode=ro", uri=True, timeout=5)
    try:
        rows = con.execute(
            "SELECT m.Id, m.Role, m.Content, m.SafetyFlag, m.Mode FROM Messages m "
            "JOIN Conversations c ON m.ConversationId = c.Id "
            "WHERE upper(c.DeviceId) = upper(?) ORDER BY m.Timestamp DESC LIMIT 16",
            (device_id,)).fetchall()
    except sqlite3.Error:
        return out
    finally:
        con.close()
    flag_num = {"Clean": 0, "Flagged": 1, "Blocked": 2}
    for mid, role, content, flag, mode in rows:
        if exclude_ids is not None and str(mid) in exclude_ids:
            continue
        if role == "Assistant" and out["reply"] is None:
            out.update(reply=content, safetyFlag=flag_num.get(flag, flag), mode=mode, persisted=True)
        elif role == "User" and out["transcript"] is None:
            out.update(transcript=content, userFlag=flag_num.get(flag, flag))
    return out


# =============================================================== log window
class LogTail:
    """Byte-offset window over the API's JSON console log. mark() before a
    request, entries_since() after it settles: the lines in between belong
    to that request because the runner never has two in flight."""

    def __init__(self, path: Path, settle_ms: int = 150):
        self.path, self.settle_ms = path, settle_ms

    def mark(self) -> int:
        return self.path.stat().st_size if self.path.exists() else 0

    def entries_since(self, offset: int) -> tuple[list[dict[str, Any]], int]:
        if not self.path.exists():
            return [], offset
        # Wait until the console logger's queue has drained into the file.
        deadline = time.monotonic() + max(self.settle_ms, 0) / 1000 * 4
        size = self.path.stat().st_size
        while self.settle_ms > 0 and time.monotonic() < deadline:
            time.sleep(self.settle_ms / 1000)
            new_size = self.path.stat().st_size
            if new_size == size:
                break
            size = new_size
        with self.path.open("rb") as fh:
            fh.seek(offset)
            data = fh.read()
        # Only whole lines; a half-written last line stays for the next window.
        cut = data.rfind(b"\n") + 1
        return parse_log_lines(data[:cut].decode("utf-8", errors="replace")), offset + cut


def parse_log_lines(text: str) -> list[dict[str, Any]]:
    out = []
    for line in text.splitlines():
        line = line.strip()
        if not line.startswith("{"):
            continue
        try:
            out.append(json.loads(line))
        except json.JSONDecodeError:
            continue
    return out


def log_events(entries: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """Known provider/guard events in a window, in order. Only the event
    kind and the provider's reason code are kept, never message text."""
    events = []
    for e in entries:
        msg = str(e.get("Message", ""))
        for marker, kind in PROVIDER_LOG_MARKERS:
            if marker in msg:
                state = e.get("State") or {}
                ev = {"kind": kind}
                if isinstance(state, dict) and isinstance(state.get("Reason"), str):
                    ev["reason"] = state["Reason"]
                events.append(ev)
                break
    return events


def attribute_withhold(events: list[dict[str, Any]], reply: str | None, blocked: bool,
                       canned: dict[str, str], provider: str,
                       refusal_patterns: list[str]) -> dict[str, Any]:
    """Was this turn's child-facing reply a MODEL withhold?

    A withhold counts only when a model event (Gemini filter, empty
    completion) fell inside the request's log window AND the child heard a
    fallback line AND no moderation/prefilter block explains the same line.
    On OpenAI arms a refusal-shaped reply counts too. A model event with a
    normal reply (e.g. the choice-generation call was withheld) is kept as
    'model_event_recovered' and not counted."""
    kinds = [e["kind"] for e in events]
    text = (reply or "").strip()
    fallback_lines = {canned[c] for c in FALLBACK_CLASSES if c in canned}
    fb = canned.get("safety_fallback")
    is_fallback = text == "" or text in fallback_lines or bool(fb and text.startswith(fb))
    block = blocked or any(k in BLOCK_EVENTS for k in kinds)
    model = [k for k in kinds if k in MODEL_EVENTS]
    refusal = provider == "openai" and any(re.search(rx, text) for rx in refusal_patterns)
    if block:
        cause, child_facing = next((k for k in kinds if k in BLOCK_EVENTS), "blocked_flag"), False
    elif model and is_fallback:
        cause, child_facing = model[0], True
    elif refusal:
        cause, child_facing = "refusal_text", True
    elif model:
        cause, child_facing = "model_event_recovered", False
    elif is_fallback:
        cause, child_facing = "fallback_unattributed", False
    else:
        cause, child_facing = None, False
    return {"childFacing": child_facing, "cause": cause, "events": events}


def storyqa_empty_events(body: dict[str, Any]) -> list[dict[str, Any]]:
    """A synthetic model event for a story-QA answer that fell back
    because the MODEL returned nothing (StoryAnswerFilter rejection
    "Empty" on the first try or the retry). Only the Gemini adapter logs
    a marker for this; an empty or refusal-only OpenAI completion logs
    nothing and would otherwise be filed as fallback_unattributed, which
    would bias the story-QA withhold column toward OpenAI."""
    if body.get("usedFallback") and "Empty" in (body.get("firstRejection"), body.get("retryRejection")):
        return [{"kind": "empty_completion", "source": "storyqa_rejection"}]
    return []


# =============================================================== HTTP
class HttpResult:
    def __init__(self, status: int, body: bytes, headers: dict[str, str], latency_ms: int,
                 error: str | None = None):
        self.status, self.body, self.headers, self.latency_ms, self.error = (
            status, body, headers, latency_ms, error)

    def json(self) -> Any:
        try:
            return json.loads(self.body.decode("utf-8")) if self.body else None
        except (UnicodeDecodeError, json.JSONDecodeError):
            return None


def http(method: str, url: str, body: bytes | None = None, headers: dict[str, str] | None = None,
         timeout: float = 90, opener: Any = LOCAL_OPENER) -> HttpResult:
    """One request, timed around the full body read. Errors come back as a
    result (status 0 + error kind), never as an exception, so one failed
    turn is recorded and the run carries on."""
    req = urllib.request.Request(url, data=body, method=method, headers=headers or {})
    t0 = time.perf_counter()
    try:
        with opener.open(req, timeout=timeout) as resp:
            data = resp.read()
            return HttpResult(resp.status, data, dict(resp.headers), int((time.perf_counter() - t0) * 1000))
    except urllib.error.HTTPError as e:
        data = e.read() if hasattr(e, "read") else b""
        return HttpResult(e.code, data, dict(e.headers or {}), int((time.perf_counter() - t0) * 1000))
    except (TimeoutError, OSError, urllib.error.URLError) as e:
        kind = "timeout" if "timed out" in str(e).lower() or isinstance(e, TimeoutError) else "transport"
        return HttpResult(0, b"", {}, int((time.perf_counter() - t0) * 1000), kind)


def normalize_flag(flag: Any) -> int | None:
    """SafetyFlag as 0/1/2 whether the API serialized the enum as a number
    (today) or a name."""
    names = {"clean": 0, "flagged": 1, "blocked": 2}
    if isinstance(flag, str):
        return names.get(flag.lower())
    return flag if isinstance(flag, int) else None


def _json_body(obj: Any) -> bytes:
    return json.dumps(obj, ensure_ascii=False).encode("utf-8")


class Device:
    """A registered AND claimed toy. The key is only ever placed in headers."""

    def __init__(self, device_id: str, api_key: str):
        self.device_id, self._api_key = device_id, api_key

    def headers(self) -> dict[str, str]:
        return {"X-Device-Id": self.device_id, "X-Api-Key": self._api_key}

    def __repr__(self) -> str:  # never show the key, even in a traceback
        return "Device(<claimed>)"


class SetupError(RuntimeError):
    pass


class Api:
    def __init__(self, base_url: str):
        self.base = base_url.rstrip("/")
        self._jwt: str | None = None
        self._operator: str | None = None

    def health(self) -> bool:
        r = http("GET", self.base + "/api/health", timeout=5)
        return r.status == 200

    def create_parent(self, label: str) -> None:
        email = f"bakeoff-{label}-{pysecrets.token_hex(6)}@example.invalid"
        password = "Bk-" + pysecrets.token_urlsafe(18)
        r = self._post_backoff("/api/parents/register",
                               {"email": email, "password": password, "acceptedTerms": True})
        if r.status not in (200, 201, 202):
            raise SetupError(f"parent register failed: HTTP {r.status}")
        r = self._post_backoff("/api/parents/login", {"email": email, "password": password})
        token = (r.json() or {}).get("token") if r.status == 200 else None
        if not token:
            raise SetupError(f"parent login failed: HTTP {r.status}")
        self._jwt = token

    def set_operator_token(self, token: str) -> None:
        self._operator = token

    def _post_backoff(self, path: str, obj: Any, headers: dict[str, str] | None = None) -> HttpResult:
        """POST with the same 429 handling as BenchSetup: honour Retry-After,
        give up after ~70 s."""
        waited = 0.0
        while True:
            r = http("POST", self.base + path, _json_body(obj),
                     {"Content-Type": "application/json", **(headers or {})}, timeout=30)
            if r.status != 429 or waited >= 70:
                return r
            delay = float(r.headers.get("Retry-After", "5") or 5) + 1
            time.sleep(delay)
            waited += delay

    def register_and_claim(self, label: str) -> Device:
        """register -> claim with the one-time code -> the device must be in
        GET /api/parents/devices. Any miss raises: an unclaimed toy only
        ever hears the resting line, so carrying on would measure nothing."""
        if not self._jwt:
            raise SetupError("no parent session")
        mac = f"bk-{label}-{pysecrets.token_hex(8)}"[:64]
        r = self._post_backoff("/api/devices/register", {"macAddress": mac})
        body = r.json() or {}
        if r.status not in (200, 201) or not body.get("deviceId") or not body.get("apiKey"):
            raise SetupError(f"device register failed: HTTP {r.status}")
        if not body.get("claimCode"):
            raise SetupError("device register returned no claim code")
        device = Device(body["deviceId"], body["apiKey"])
        auth = {"Authorization": "Bearer " + self._jwt}
        r = self._post_backoff("/api/parents/devices/claim",
                               {"deviceId": device.device_id, "claimCode": body["claimCode"]}, auth)
        if r.status not in (200, 201, 204):
            raise SetupError(f"device claim failed: HTTP {r.status}")
        r = http("GET", self.base + "/api/parents/devices", headers=auth, timeout=30)
        listed = (r.json() or {}).get("devices", []) if r.status == 200 else []
        if not any(str(d).lower() == device.device_id.lower() for d in listed):
            raise SetupError("device claim did not stick (missing from GET /api/parents/devices)")
        return device

    def chat(self, device: Device, text: str) -> HttpResult:
        return http("POST", self.base + "/api/chat", _json_body({"message": text}),
                    {"Content-Type": "application/json", **device.headers()})

    def story_qa_test(self, story_id: str, segment: int, question: str) -> HttpResult:
        if not self._operator:
            raise SetupError("no operator token")
        return http("POST", self.base + "/api/internal/story-qa-test",
                    _json_body({"storyId": story_id, "segmentIndex": segment, "question": question}),
                    {"Content-Type": "application/json", "Authorization": "Bearer " + self._operator})

    def reflection(self, device: Device, story_id: str, qi: int, wav: bytes) -> HttpResult:
        q = urllib.parse.urlencode({"storyId": story_id, "questionIndex": qi})
        return http("POST", f"{self.base}/api/chat/story-qa/reflection-answer?{q}", wav,
                    {"Content-Type": "audio/wav", **device.headers()})

    def chat_audio(self, device: Device, wav: bytes) -> HttpResult:
        return http("POST", self.base + "/api/chat/audio", wav,
                    {"Content-Type": "audio/wav", **device.headers()})


# =============================================================== provider side
def _ssl_opener() -> Any:
    ctx = ssl.create_default_context(cafile=os.environ.get("SSL_CERT_FILE")
                                     or os.environ.get("REQUESTS_CA_BUNDLE") or None)
    return urllib.request.build_opener(urllib.request.HTTPSHandler(context=ctx))


def moderation_snapshot(api_key: str) -> dict[str, Any]:
    """One direct moderation call per arm: records which dated snapshot
    the floating 'omni-moderation-latest' alias resolved to (N017)."""
    r = http("POST", OPENAI_BASE + "/moderations", _json_body({"model": MODERATION_MODEL, "input": "բարև"}),
             {"Content-Type": "application/json", "Authorization": "Bearer " + api_key}, timeout=30,
             opener=_ssl_opener())
    body = r.json() or {}
    return {"alias": MODERATION_MODEL, "resolved": body.get("model"), "status": r.status, "at": utc_now()}


def calibration_prompt(arm_env: dict[str, str]) -> tuple[str, list[tuple[str, str]]]:
    """(system prompt, [(role, text)]) about CALIBRATION_PROMPT_CHARS long:
    the arm's own system prompt (an env SystemPrompt override, else
    appsettings'), then child turns from the R2 personas alternating with
    story segments as the history, then one ordinary curiosity question.
    Built only from repo files, so every arm measures the same prompt."""
    system = arm_env.get("SystemPrompt") or bc.read_json(bc.APPSETTINGS)["SystemPrompt"]
    child = [p["text"] for p in bc.read_json(bc.PROBES_DIR / "personas-r2.json")["probes"]]
    stories = sorted((bc.REPO_ROOT / "backend" / "src" / "ArmenianAiToy.Application" / "Stories" / "Content")
                     .glob("*.story.json"))
    segments = [seg for f in stories for seg in bc.read_json(f)["segments"] if isinstance(seg, str)]
    messages: list[tuple[str, str]] = []
    total = len(system) + len(CALIBRATION_QUESTION)
    i = 0
    while total < CALIBRATION_PROMPT_CHARS and i < len(segments):
        messages += [("user", child[i % len(child)]), ("assistant", segments[i])]
        total += len(child[i % len(child)]) + len(segments[i])
        i += 1
    messages.append(("user", CALIBRATION_QUESTION))
    return system, messages


def _calibration_request(arm: dict[str, Any], environ: dict[str, str], cfg: dict[str, Any]
                         ) -> tuple[str, bytes, dict[str, str], str]:
    """(url, body, headers, via) in the SAME shape the arm's adapter sends:
    OpenAI chat completions with no options (no reasoning effort, no max
    tokens), or Gemini generateContent with the arm's safety threshold and
    a thinkingConfig only when a budget is set. Gemini goes through AI
    Studio even for a Vertex arm: token accounting is the model's, and a
    service-account JWT needs RSA, which the Python stdlib lacks."""
    env = arm["env"]
    system, messages = calibration_prompt(env)
    if provider_of(arm) == "openai":
        key = environ.get(cfg["secretEnv"].get("OpenAI__ApiKey", "OPENAI_API_KEY"), "")
        body = {"model": env["OpenAI__ChatModel"],
                "messages": [{"role": "system", "content": system}]
                + [{"role": r, "content": t} for r, t in messages]}
        return (OPENAI_BASE + "/chat/completions", _json_body(body),
                {"Content-Type": "application/json", "Authorization": "Bearer " + key}, "openai chat.completions")
    threshold = env.get("Gemini__SafetyThreshold") or "BLOCK_LOW_AND_ABOVE"
    body: dict[str, Any] = {
        "system_instruction": {"parts": [{"text": system}]},
        "contents": [{"role": "model" if r == "assistant" else "user", "parts": [{"text": t}]}
                     for r, t in messages],
        "safetySettings": [{"category": c, "threshold": threshold} for c in GEMINI_SAFETY_CATEGORIES]}
    if env.get("Gemini__ThinkingBudget"):
        body["generationConfig"] = {"thinkingConfig": {"thinkingBudget": int(env["Gemini__ThinkingBudget"])}}
    return (f"{GEMINI_BASE}/models/{env['Gemini__Model']}:generateContent", _json_body(body),
            {"Content-Type": "application/json", "x-goog-api-key": environ.get("GEMINI_API_KEY", "")},
            "gemini ai-studio generateContent")


def parse_calibration_usage(provider: str, body: Any) -> dict[str, Any] | None:
    """Billed tokens from a provider response, or None if absent. OpenAI's
    completion_tokens already INCLUDES reasoning_tokens (the latter is a
    breakdown), so it is the billed output; Gemini bills thoughtsTokenCount
    on top of candidatesTokenCount."""
    if not isinstance(body, dict):
        return None
    if provider == "openai":
        u = body.get("usage") or {}
        if not isinstance(u.get("prompt_tokens"), int) or not isinstance(u.get("completion_tokens"), int):
            return None
        reasoning = (u.get("completion_tokens_details") or {}).get("reasoning_tokens") or 0
        msg = ((body.get("choices") or [{}])[0].get("message") or {}).get("content") or ""
        return {"promptTokens": u["prompt_tokens"], "outTokensTotal": u["completion_tokens"],
                "reasoningTokens": reasoning, "visibleOutTokens": u["completion_tokens"] - reasoning,
                "replyChars": len(msg)}
    u = body.get("usageMetadata") or {}
    if not isinstance(u.get("promptTokenCount"), int):
        return None
    visible = u.get("candidatesTokenCount") or 0
    thoughts = u.get("thoughtsTokenCount") or 0
    parts = (((body.get("candidates") or [{}])[0].get("content") or {}).get("parts") or [])
    text = "".join(p.get("text", "") for p in parts if isinstance(p, dict) and not p.get("thought"))
    return {"promptTokens": u["promptTokenCount"], "outTokensTotal": visible + thoughts,
            "reasoningTokens": thoughts, "visibleOutTokens": visible, "replyChars": len(text)}


def calibrate(arm: dict[str, Any], cfg: dict[str, Any], environ: dict[str, str],
              send: Callable[..., "HttpResult"] | None = None) -> dict[str, Any]:
    """One direct provider call; the measured token figures for the guard,
    the report's cost columns and the plan's token step (b). Raises
    SetupError when the provider gives no usage: the guard must not run on
    assumptions it was told are wrong. Never records the reply text."""
    url, body, headers, via = _calibration_request(arm, environ, cfg)
    system, messages = calibration_prompt(arm["env"])
    prompt_chars = len(system) + sum(len(t) for _, t in messages)
    send = send or (lambda *a, **k: http(*a, opener=_ssl_opener(), **k))
    r = send("POST", url, body, headers, timeout=120)
    usage = parse_calibration_usage(provider_of(arm), r.json()) if r.status == 200 else None
    if usage is None:
        raise SetupError(f"calibration call failed for {arm['id']}: HTTP {r.status or r.error}; the spend guard "
                         f"cannot price this model's output (reasoning/thinking tokens) without it")
    model = arm["env"].get("OpenAI__ChatModel") or arm["env"].get("Gemini__Model")
    return {"provider": provider_of(arm), "model": model, "via": via, "at": utc_now(),
            "latencyMs": r.latency_ms, "promptChars": prompt_chars,
            "promptSha256": bc.sha256_bytes(_json_body([system, messages])),
            "thinkingBudget": arm["env"].get("Gemini__ThinkingBudget", "unset")
            if provider_of(arm) == "gemini" else "n/a",
            "tokensPerChar": round(usage["promptTokens"] / prompt_chars, 4), **usage}


def calibration_for(arm: dict[str, Any], manifest: dict[str, Any] | None) -> dict[str, Any] | None:
    """The stored calibration if it measured THIS model with THIS thinking
    budget; None means measure again (e.g. after a --calibrate-only run
    with thinking unset, the run-day decision set a budget)."""
    cal = (manifest or {}).get("calibration")
    if not cal:
        return None
    model = arm["env"].get("OpenAI__ChatModel") or arm["env"].get("Gemini__Model")
    thinking = arm["env"].get("Gemini__ThinkingBudget", "unset") if provider_of(arm) == "gemini" else "n/a"
    return cal if cal.get("model") == model and cal.get("thinkingBudget") == thinking else None


def synthesize_wav(text: str, out_path: Path, api_key: str) -> None:
    """OpenAI TTS -> ffmpeg -> 16 kHz mono 16-bit PCM WAV, the toy's own
    upload format. Called once per distinct text; every arm reuses it."""
    r = http("POST", OPENAI_BASE + "/audio/speech",
             _json_body({"model": TTS_MODEL, "voice": TTS_VOICE, "input": text, "response_format": "wav"}),
             {"Content-Type": "application/json", "Authorization": "Bearer " + api_key}, timeout=60,
             opener=_ssl_opener())
    if r.status != 200 or not r.body:
        raise SetupError(f"TTS for a probe WAV failed: HTTP {r.status}")
    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
        tmp.write(r.body)
        raw = Path(tmp.name)
    try:
        out_path.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", str(raw), "-ar", "16000", "-ac", "1",
                        "-c:a", "pcm_s16le", str(out_path)], check=True)
    finally:
        raw.unlink(missing_ok=True)


class WavCache:
    """<runs>/_wav/<sha16 of text>.wav plus pins.json (text sha -> wav
    sha). A pinned WAV whose bytes changed is refused, so every arm hears
    exactly the same audio."""

    def __init__(self, root: Path, api_key: str | None,
                 synth: Callable[[str, Path, str], None] = synthesize_wav):
        self.root, self.api_key, self.synth = root, api_key, synth
        self.pins_path = root / "pins.json"
        self.pins: dict[str, Any] = bc.read_json(self.pins_path) if self.pins_path.exists() else {}
        self.synthesized = 0

    def get(self, text: str) -> tuple[bytes, str]:
        key = bc.sha256_bytes(text.encode("utf-8"))[:16]
        path = self.root / f"{key}.wav"
        if not path.exists():
            if not self.api_key:
                raise SetupError("a probe WAV is missing and no OpenAI key is available to synthesize it")
            self.synth(text, path, self.api_key)
            self.synthesized += 1
        data = path.read_bytes()
        digest = bc.sha256_bytes(data)
        pinned = self.pins.get(key, {}).get("wavSha256")
        if pinned and pinned != digest:
            raise SetupError(f"probe WAV {key}.wav changed since it was pinned")
        if not pinned:
            self.pins[key] = {"wavSha256": digest, "bytes": len(data), "chars": len(text)}
            bc.write_json(self.pins_path, self.pins)
        return data, digest


# =============================================================== server
def build_server_env(arm: dict[str, Any], cfg: dict[str, Any], run_dir: Path,
                     environ: dict[str, str]) -> tuple[dict[str, str], dict[str, str]]:
    """(full process env, generated runtime secrets). The generated JWT key
    and operator token exist only in memory and in the child's env."""
    env = {k: v for k, v in environ.items() if k in INHERITED_ENV or k.startswith(INHERITED_ENV_PREFIXES)}
    url = f"http://127.0.0.1:{arm['port']}"
    runtime = {"jwt": pysecrets.token_hex(32), "operator": pysecrets.token_urlsafe(32)}
    env.update(cfg.get("commonEnv", {}))
    env.update({
        # appsettings.json hard-codes Urls=0.0.0.0:5000, which beats
        # ASPNETCORE_URLS; the Urls variable overrides that key itself.
        "Urls": url,
        "ASPNETCORE_URLS": url,
        "Database__ConnectionString": f"Data Source={run_dir / 'areg.db'}",
        "Audio__BlobStoreRoot": str(run_dir / "audio-blobs"),
        "Backup__Database__DirectoryPath": str(run_dir / "backups"),
        "ContentSync__UploadRoot": str(run_dir / "uploads"),
        "Jwt__Keys__0": runtime["jwt"],
        "Internal__Operators__0__Name": "bakeoff",
        "Internal__Operators__0__Token": runtime["operator"],
    })
    env.update(arm.get("env", {}))
    for cfg_key, env_name in cfg.get("secretEnv", {}).items():
        if cfg_key.startswith(VERTEX_SECRET_PREFIX) and not uses_vertex(arm):
            continue        # a service-account key goes only where it is used
        if environ.get(env_name):
            env[cfg_key] = environ[env_name]
    for env_name in cfg.get("secretEnvPassThrough", []):
        if environ.get(env_name):
            env[env_name] = environ[env_name]
    return env, runtime


def public_env(arm: dict[str, Any], cfg: dict[str, Any]) -> dict[str, str]:
    """The non-secret part of an arm's env, for the run manifest and report."""
    out = dict(cfg.get("commonEnv", {}))
    out.update(arm.get("env", {}))
    out["secretEnvNames"] = ",".join(required_secret_names(arm, cfg))
    return out


def port_answers(port: int, host: str = "127.0.0.1") -> bool:
    try:
        with socket.create_connection((host, port), timeout=1):
            return True
    except OSError:
        return False


class ApiServer:
    """The Release API for one arm, owned through its Popen handle."""

    def __init__(self, dll: Path, cwd: Path, env: dict[str, str], log_path: Path, base_url: str,
                 operator_token: str):
        self.dll, self.cwd, self.env, self.log_path, self.base_url = dll, cwd, env, log_path, base_url
        self.port = int(base_url.rsplit(":", 1)[1])
        self._operator = operator_token
        self.proc: subprocess.Popen | None = None
        self._log = None

    def start(self, timeout_s: int) -> None:
        """Boot and PROVE the process answering is this one. A session
        killed mid-run can leave its dotnet child on the port: the new
        process then fails to bind, but /api/health still answers from the
        OLD server (older build or config) until the new one exits. So the
        port must be free first, and after health the process must still
        be alive AND accept the operator token only this run generated."""
        if port_answers(self.port):
            raise SetupError(f"port {self.port} already answers before boot: a server from an earlier "
                             f"(killed?) session is still running; stop it, then resume")
        self.log_path.parent.mkdir(parents=True, exist_ok=True)
        self._log = self.log_path.open("ab")
        self.proc = subprocess.Popen(["dotnet", str(self.dll)], cwd=self.cwd, env=self.env,
                                     stdout=self._log, stderr=subprocess.STDOUT)
        api = Api(self.base_url)
        deadline = time.monotonic() + timeout_s
        while time.monotonic() < deadline:
            if self.proc.poll() is not None:
                raise SetupError(f"API exited during boot (code {self.proc.returncode}); see {self.log_path}")
            if api.health():
                self._prove_ownership()
                return
            time.sleep(1)
        raise SetupError(f"API not healthy after {timeout_s}s; see {self.log_path}")

    def _prove_ownership(self) -> None:
        r = http("GET", self.base_url + "/api/internal/system",
                 headers={"Authorization": "Bearer " + self._operator}, timeout=15)
        alive = self.proc is not None and self.proc.poll() is None
        if not alive or r.status != 200:
            self.stop()
            raise SetupError(f"port {self.port}: /api/health answered, but not from the process this run "
                             f"started (alive={alive}, operator check HTTP {r.status}); see {self.log_path}")

    def stop(self) -> None:
        if self.proc and self.proc.poll() is None:
            self.proc.terminate()
            try:
                self.proc.wait(timeout=20)
            except subprocess.TimeoutExpired:
                self.proc.kill()
                self.proc.wait(timeout=10)
        if self._log:
            self._log.close()


def git_head() -> str | None:
    if os.environ.get("GIT_HEAD"):
        return os.environ["GIT_HEAD"]
    try:
        return subprocess.run(["git", "rev-parse", "--short", "HEAD"], cwd=bc.REPO_ROOT, capture_output=True,
                              text=True, check=True).stdout.strip() or None
    except (OSError, subprocess.CalledProcessError):
        return None


# =============================================================== execution
class ArmRun:
    """Everything one arm's probe sets need while its API is up."""

    def __init__(self, arm: dict[str, Any], cfg: dict[str, Any], arm_dir: Path, api: Api, log: LogTail,
                 db_path: Path, tracker: SpendTracker, ledger: Ledger, wavs: WavCache | None,
                 refusal_patterns: list[str], out=print):
        self.arm, self.cfg, self.arm_dir, self.api, self.log = arm, cfg, arm_dir, api, log
        self.db_path, self.tracker, self.ledger, self.wavs = db_path, tracker, ledger, wavs
        self.provider = provider_of(arm)
        # The arm's own SafetyFallbackResponse (if its env sets one) is the
        # line a withhold speaks; appsettings' copy would go unrecognised.
        self.canned = bc.load_canned_lines(overrides=public_env(arm, cfg))
        self.refusal_patterns = refusal_patterns
        self.out = out
        self.consecutive_errors = 0
        self.stopped: str | None = None

    # ---------------------------------------------------------- results files
    def results_path(self, set_name: str, pass_no: int) -> Path:
        return self.arm_dir / "results" / f"{set_name}.p{pass_no}.jsonl"

    def resume_state(self, probe_set: dict[str, Any], pass_no: int) -> set[str]:
        """Unit keys already complete. Rows of a half-finished unit are
        dropped from the file so the unit re-runs whole on a fresh toy."""
        path = self.results_path(probe_set["set"], pass_no)
        rows = bc.read_jsonl(path)
        have = {r["id"] for r in rows if not r.get("setup")}
        done = {key for key, probes in units_of(probe_set) if all(p["id"] in have for p in probes)}
        kept = [r for r in rows if r.get("unit") in done]
        if len(kept) != len(rows):
            bc.write_jsonl(path, kept)
        return done

    # ---------------------------------------------------------- one set
    def run_set(self, probe_set: dict[str, Any], pass_no: int) -> None:
        name = probe_set["set"]
        done = self.resume_state(probe_set, pass_no)
        units = units_of(probe_set)
        todo = [(i, k, p) for i, (k, p) in enumerate(units) if k not in done]
        self.out(f"  [{self.arm['id']}] {name} pass {pass_no}: {len(units) - len(todo)}/{len(units)} units "
                 f"already done, {len(todo)} to run")
        kind = probe_set["kind"]
        for unit_index, key, probes in todo:
            if self.stopped:
                return
            next_usd = calls_in_unit(probes) * self.tracker.rates.get(
                {"persona": "chat"}.get(kind, kind), self.tracker.rates["chat"])
            reason = self.tracker.limit_reason(next_usd)
            if reason:
                self.stopped = reason
                return
            try:
                self.run_unit(probe_set, pass_no, unit_index, key, probes)
            except SetupError as e:
                self.out(f"    unit {key}: setup failed: {e}")
                self.consecutive_errors += 1
            self.ledger.save()
            if self.consecutive_errors >= 5:
                self.stopped = "5 consecutive failed turns or setups; is the API up?"
                return

    def run_unit(self, probe_set: dict[str, Any], pass_no: int, unit_index: int, key: str,
                 probes: list[dict[str, Any]]) -> None:
        kind = probe_set["kind"]
        device = None
        if kind in ("chat", "persona", "reflection", "voice"):
            device = self.api.register_and_claim(f"{self.arm['id']}-{unit_index}")
        path = self.results_path(probe_set["set"], pass_no)
        for p in probes:
            if self.stopped:
                # An INVALID stop mid-unit (a gate line or clip on turn k of
                # a persona): every later turn would measure the gate too.
                # The unit stays half-finished and is re-run whole on resume.
                return
            for setup_text in p.get("setup", []):
                row = self.chat_turn(device, setup_text)
                row.update(id=f"{p['id']}#setup", setup=True)
                self._write(path, probe_set, pass_no, key, unit_index, row)
            if kind in ("chat", "persona"):
                row = self.chat_turn(device, p["text"])
            elif kind == "storyqa":
                row = self.storyqa_turn(p)
            elif kind == "reflection":
                row = self.reflection_turn(device, p)
            elif kind == "voice":
                row = self.voice_turn(device, p)
            else:
                raise SetupError(f"unknown set kind {kind}")
            row["id"] = p["id"]
            self._write(path, probe_set, pass_no, key, unit_index, row)

    def _write(self, path: Path, probe_set: dict[str, Any], pass_no: int, key: str, unit_index: int,
               row: dict[str, Any]) -> None:
        full = {"arm": self.arm["id"], "set": probe_set["set"], "pass": pass_no, "id": row.pop("id"),
                "unit": key, "unitIndex": unit_index, "setup": row.pop("setup", False)}
        full.update(row)
        full["at"] = utc_now()
        if full.get("status") != 200 or full.get("persisted") is False:
            self.consecutive_errors += 1
        else:
            self.consecutive_errors = 0
        bc.append_jsonl(path, full)
        # A gate line (unclaimed/paused, cost cap, mode disabled) can only
        # mean the bench config is not in effect: every later turn would
        # measure the gate, not the model. Same idea as BenchSetup's
        # RunValidity; stop the arm instead of producing an invalid table.
        reply = (full.get("reply") or "").strip()
        for label in GATE_LINES:
            if reply and reply == (self.canned.get(label) or "").strip():
                self.stopped = f"INVALID: the {label} line answered {full['id']}; the bench config is not in effect"

    # ---------------------------------------------------------- endpoints
    def _attribute(self, offset: int, reply: str | None, blocked: bool,
                   extra_events: list[dict[str, Any]] | None = None) -> dict[str, Any]:
        entries, _ = self.log.entries_since(offset)
        return attribute_withhold(log_events(entries) + (extra_events or []), reply, blocked, self.canned,
                                  self.provider, self.refusal_patterns)

    def chat_turn(self, device: Device, text: str) -> dict[str, Any]:
        offset = self.log.mark()
        r = self.api.chat(device, text)
        self.tracker.add("chat")
        self.tracker.add("moderation", 2, usd=0.0)
        body = r.json() if r.status == 200 else None
        body = body if isinstance(body, dict) else {}
        reply = body.get("response")
        flag = normalize_flag(body.get("safetyFlag"))
        return {"endpoint": "/api/chat", "status": r.status, "error": r.error, "latencyMs": r.latency_ms,
                "reply": reply, "safetyFlag": flag, "mode": body.get("mode"),
                "choiceA": body.get("choiceA"), "choiceB": body.get("choiceB"),
                "turnEnded": body.get("turnEnded"),
                "withhold": self._attribute(offset, reply, flag == 2) if r.status == 200 else None}

    def storyqa_turn(self, p: dict[str, Any]) -> dict[str, Any]:
        offset = self.log.mark()
        r = self.api.story_qa_test(p["storyId"], p["segmentIndex"], p["question"])
        self.tracker.add("storyqa")
        self.tracker.add("moderation", 2, usd=0.0)
        body = r.json() if r.status == 200 else None
        body = body if isinstance(body, dict) else {}
        reply = body.get("answer")
        blocked = body.get("inputSafe") is False or body.get("outputSafe") is False
        return {"endpoint": "/api/internal/story-qa-test", "status": r.status, "error": r.error,
                "latencyMs": r.latency_ms, "reply": reply, "safetyFlag": 2 if blocked else 0,
                "segmentText": body.get("segmentText"), "usedFallback": body.get("usedFallback"),
                "inputSafe": body.get("inputSafe"), "outputSafe": body.get("outputSafe"),
                "firstRejection": body.get("firstRejection"), "retryRejection": body.get("retryRejection"),
                "outcome": body.get("outcome"),
                "withhold": self._attribute(offset, reply, blocked, storyqa_empty_events(body))
                if r.status == 200 else None}

    def _wav(self, text: str) -> tuple[bytes, str]:
        if self.wavs is None:
            raise SetupError("audio sets need the WAV cache (OpenAI key)")
        before = self.wavs.synthesized
        data, digest = self.wavs.get(text)
        if self.wavs.synthesized > before:
            self.tracker.add("tts_synth", usd=len(text) * self.tracker.rates["tts_per_char"])
        return data, digest

    def reflection_turn(self, device: Device, p: dict[str, Any]) -> dict[str, Any]:
        wav, digest = self._wav(p["childAnswer"])
        before = device_message_ids(self.db_path, device.device_id)
        offset = self.log.mark()
        r = self.api.reflection(device, p["storyId"], p["questionIndex"], wav)
        self.tracker.add("reflection")
        stored = latest_turn_from_db(self.db_path, device.device_id, exclude_ids=before)
        blocked = stored["userFlag"] == 2
        # A 200 that stored nothing (a gate clip, the spoken STT-failure
        # clip) has no reply to grade; it is counted as an error, and five
        # in a row stop the arm like any other failed turn.
        return {"endpoint": "/api/chat/story-qa/reflection-answer", "status": r.status, "error": r.error,
                "latencyMs": r.latency_ms, "reply": stored["reply"], "safetyFlag": stored["safetyFlag"],
                "transcript": stored["transcript"], "userFlag": stored["userFlag"], "wavSha256": digest,
                "audioBytes": len(r.body), "persisted": stored["persisted"],
                "withhold": self._attribute(offset, stored["reply"], blocked)
                if r.status == 200 and stored["persisted"] else None}

    def voice_turn(self, device: Device, p: dict[str, Any]) -> dict[str, Any]:
        wav, digest = self._wav(p["text"])
        before = device_message_ids(self.db_path, device.device_id)
        offset = self.log.mark()
        r = self.api.chat_audio(device, wav)
        self.tracker.add("voice")
        stored = latest_turn_from_db(self.db_path, device.device_id, exclude_ids=before)
        if r.status == 200 and not stored["persisted"]:
            # /api/chat/audio answers 200 without storing only through a
            # canned gate clip (paused/unclaimed, bedtime, mode disabled):
            # the bench config is not in effect, same as a gate line on the
            # text path. The canned clip never reaches the DB, so the
            # gate-line check in _write cannot see it.
            self.stopped = (f"INVALID: /api/chat/audio answered {p['id']} with 200 and stored no turn "
                            f"(a gate or canned clip); the bench config is not in effect")
        return {"endpoint": "/api/chat/audio", "status": r.status, "error": r.error,
                "latencyMs": r.latency_ms, "reply": stored["reply"], "safetyFlag": stored["safetyFlag"],
                "mode": stored["mode"], "transcript": stored["transcript"], "wavSha256": digest,
                "audioBytes": len(r.body), "turnEndHeader": r.headers.get("X-Areg-Turn-End"),
                "persisted": stored["persisted"],
                "withhold": self._attribute(offset, stored["reply"], stored["userFlag"] == 2)
                if r.status == 200 and stored["persisted"] else None}

    # ---------------------------------------------------------- spend
    def checkpoint(self, label: str) -> None:
        meter, questions = read_meter(self.db_path)
        self.tracker.set_meter(meter)
        self.ledger.save()
        self.out(f"  [{self.arm['id']}] after {label}: estimate ${self.tracker.entry['estimateUsd']:.3f}, "
                 f"meter ${meter:.3f} ({questions} metered questions), guard ${self.tracker.spend():.3f} "
                 f"of ${self.tracker.budget:.2f}; ledger total ${self.ledger.cumulative():.3f}")
        if self.tracker.spend() >= self.tracker.budget:
            self.stopped = f"budget reached after {label}"
        elif self.ledger.cumulative() >= self.tracker.stop_usd:
            self.stopped = (f"cumulative stop: ledger ${self.ledger.cumulative():.2f} reached "
                            f"${self.tracker.stop_usd:.2f} after {label}")


def run_benchmarks(run: ArmRun, base_url: str, rerun: bool) -> dict[str, Any]:
    """BenchmarkAll against this arm's API, reports into <arm>/benchmarks/.
    Exit 0 pass, 1 regressed, 3 INVALID (a claim failed or the replies
    were mostly one canned line)."""
    out_dir = run.arm_dir / "benchmarks"
    summary = out_dir / "summary.json"
    if summary.exists() and not rerun:
        run.out(f"  [{run.arm['id']}] BenchmarkAll: summary.json present, skipping (--rerun-benchmarks to redo)")
        return {"skipped": True}
    planned = sum(run.cfg["estimate"]["benchmarkChatTurns"].values()) * run.tracker.rates["benchmark"]
    reason = run.tracker.limit_reason(planned)
    if reason:
        # BenchmarkAll is one external process the guard cannot stop midway.
        run.out(f"  [{run.arm['id']}] BenchmarkAll NOT started: {reason}")
        return {"skipped": True, "refused": reason}
    before = count_assistant_messages(run.db_path)
    env = {k: v for k, v in os.environ.items() if k != "AREG_PROVISIONING_SECRET"}
    env["GIT_HEAD"] = git_head() or ""
    cmd = ["dotnet", "run", "--project", str(bc.REPO_ROOT / "tools" / "BenchmarkAll" / "BenchmarkAll.csproj"),
           "-c", "Release", "--", base_url, "--results-dir", str(out_dir), "--label", run.arm["id"]]
    run.out(f"  [{run.arm['id']}] BenchmarkAll -> {out_dir}")
    out_dir.mkdir(parents=True, exist_ok=True)
    with (run.arm_dir / "benchmarks.log").open("ab") as fh:
        try:
            code = subprocess.run(cmd, cwd=bc.REPO_ROOT, env=env, stdout=fh, stderr=subprocess.STDOUT,
                                  timeout=7200).returncode
        except subprocess.TimeoutExpired:
            code = -1
    turns = max(0, count_assistant_messages(run.db_path) - before)
    run.tracker.add("benchmark", turns)
    meaning = {0: "passed", 1: "regressed/failed thresholds", 3: "INVALID (measured nothing)"}.get(
        code, "did not run")
    run.out(f"  [{run.arm['id']}] BenchmarkAll exit {code}: {meaning}; {turns} stored chat turns")
    return {"exitCode": code, "meaning": meaning, "chatTurns": turns}


# =============================================================== CLI
def resolve_runs_dir(arg: str | None) -> Path:
    raw = arg or os.environ.get("BAKEOFF_RUNS_DIR") or str(Path(tempfile.gettempdir()) / "areg-bakeoff-runs")
    path = Path(raw).resolve()
    if path == bc.REPO_ROOT or bc.REPO_ROOT in path.parents:
        raise SystemExit(f"--runs-dir must be outside the repo (DBs, logs and WAVs never get committed): {path}")
    return path


def check_pin(runs_dir: Path, pin: dict[str, Any]) -> str | None:
    """Every arm in one runs dir must come from the same commit, the same
    Release DLL and the same probe inputs. The first live run writes
    pin.json; a later run (another arm, or a resumed session) that differs
    is refused, because mixing builds would compare guards, not models."""
    path = runs_dir / "pin.json"
    if not path.exists():
        bc.write_json(path, {**pin, "pinnedAt": utc_now()})
        return None
    old = bc.read_json(path)
    diff = [k for k in pin if old.get(k) != pin[k]]
    if diff:
        return (f"REFUSE: {', '.join(diff)} differ from {path} (pinned {old.get('pinnedAt')}). Re-pin by "
                f"starting every arm again in a fresh --runs-dir; the spend ledger lives outside it, so "
                f"earlier spend still counts toward the stops.")
    return None


def print_plan(plans: list[dict[str, Any]], cfg: dict[str, Any], ledger: Ledger, out=print) -> bool:
    spend = cfg["spend"]
    ok = True
    grand = 0.0
    for plan in plans:
        out(f"\n{plan['arm']}: {plan['label']}")
        tok = plan["tokens"]
        out(f"  passes {plan['passes']}; guard tokens per chat turn: prompt {tok['chatPrompt']:.0f}, "
            f"out {tok['chatOut']:.0f} ({tok['source']})")
        out(f"  {'set':<14}{'passes':>7}{'units':>7}{'calls':>7}{'moder.':>8}{'est $':>9}")
        for r in plan["rows"]:
            out(f"  {r['set']:<14}{r['passes']:>7}{r['units']:>7}{r['calls']:>7}{r['moderation']:>8}"
                f"{r['usd']:>9.2f}")
        flag = "" if plan["fitsBudget"] else "  <-- OVER the arm budget: the guard will stop it early"
        out(f"  {'total':<14}{'':>7}{'':>7}{plan['calls']:>7}{'':>8}{plan['usd']:>9.2f}"
            f"   budget ${plan['budgetUsd']:.2f}{flag}")
        grand += min(plan["usd"], plan["budgetUsd"])
    already = ledger.cumulative()
    out(f"\nplanned (each arm capped at its budget): ${grand:.2f}; ledger so far ${already:.2f} "
        f"({ledger.path}); stop at ${spend['cumulativeStopUsd']:.2f} (checked before every unit); "
        f"hard cap ${spend['hardCapUsd']:.2f}")
    out("The provider-side project budgets (OpenAI 'areg-bakeoff' $40, Google $60) are the real hard cap; "
        "set them before any live run.")
    if already + grand > spend["hardCapUsd"]:
        out("REFUSE: the plan does not fit under the hard cap")
        ok = False
    elif already + grand > spend["cumulativeStopUsd"]:
        out("WARNING: the cumulative stop will end the run before the last arm(s) finish")
    return ok


def _thinking_note(arm: dict[str, Any]) -> str:
    d = arm.get("thinkingBudget") or {}
    value = d.get("value")
    return ("PENDING run-day decision (arms.json thinkingBudget)" if value is None
            else f"{value} ({d.get('decidedAt') or 'undated'})")


def main(argv: list[str] | None = None, environ: dict[str, str] | None = None, out=print) -> int:
    environ = dict(os.environ if environ is None else environ)
    ap = argparse.ArgumentParser(description="Run model bake-off arms (see module docstring).")
    sel = ap.add_mutually_exclusive_group(required=True)
    sel.add_argument("--arm", action="append", help="arm id from arms.json (repeatable)")
    sel.add_argument("--all", action="store_true", help="every arm, in arms.json order")
    ap.add_argument("--dry-run", action="store_true", help="validate and print the plan; no network, no API")
    ap.add_argument("--calibrate-only", action="store_true",
                    help="one provider call per arm to measure real tokens (reasoning/thinking included); "
                         "no API boot, no probes")
    ap.add_argument("--sets", help="comma list of probe sets (default: all, canonical order)")
    ap.add_argument("--passes", type=int, help="passes over the repeatable sets (default arms.json "
                                               "defaultPasses, capped per arm by its `passes`)")
    ap.add_argument("--skip-benchmarks", action="store_true")
    ap.add_argument("--rerun-benchmarks", action="store_true")
    ap.add_argument("--vertex-fallback-ai-studio", action="store_true",
                    help="run a Vertex arm on AI Studio when the Vertex secrets are absent (recorded in "
                         "arm-run.json and the report)")
    ap.add_argument("--runs-dir", help="default $BAKEOFF_RUNS_DIR, else <tmp>/areg-bakeoff-runs (never in-repo)")
    ap.add_argument("--api-dll", help="override arms.json apiDll")
    ap.add_argument("--log-settle-ms", type=int, default=150)
    args = ap.parse_args(argv)

    cfg = load_arms()
    problems = validate_arms(cfg) + bc.verify_manifest()
    set_names = args.sets.split(",") if args.sets else list(bc.SET_ORDER)
    unknown = [s for s in set_names if s not in bc.SET_ORDER]
    problems += [f"unknown set {s}" for s in unknown]
    if args.passes is not None and args.passes < 1:
        problems.append("--passes must be >= 1")
    arms_by_id = {a["id"]: a for a in cfg["arms"]}
    chosen = cfg["arms"] if args.all else [arms_by_id.get(a) for a in args.arm]
    if any(a is None for a in chosen):
        problems.append(f"unknown arm in {args.arm}; known: {', '.join(arms_by_id)}")
    if problems:
        out("invalid:\n  " + "\n  ".join(problems))
        return 2
    sets = load_sets([s for s in bc.SET_ORDER if s in set_names])
    runs_dir = resolve_runs_dir(args.runs_dir)
    ledger = Ledger(bc.resolve_ledger_path(runs_dir, environ), runs_dir.name, runs_dir)
    resolved, vertex_problems = [], []
    for arm in chosen:
        eff, problem = resolve_arm(arm, cfg, environ, args.vertex_fallback_ai_studio)
        resolved.append(eff)
        if problem:
            vertex_problems.append(problem)
    plans = []
    for arm in resolved:
        manifest_path = runs_dir / arm["id"] / "arm-run.json"
        cal = calibration_for(arm, bc.read_json(manifest_path) if manifest_path.exists() else None)
        plan = plan_arm(arm, cfg, sets, arm_passes(arm, cfg, args.passes), not args.skip_benchmarks, cal)
        plan["label"] = arm["label"]
        plans.append(plan)

    dll = bc.REPO_ROOT / (args.api_dll or cfg["apiDll"])
    if args.dry_run:
        out(f"DRY RUN: arms.json, {len(sets)} probe sets and MANIFEST.sha256 valid; no network used.")
        out(f"runs dir: {runs_dir}   ledger: {ledger.path}   API: {dll} "
            f"({'present' if dll.exists() else 'MISSING: build -c Release'})")
        missing_vertex = vertex_missing(cfg, environ)
        out("vertex: credentials present" if not missing_vertex else
            f"vertex: {', '.join(missing_vertex)} not set; Vertex arms need them, or "
            f"--vertex-fallback-ai-studio (owner's call, recorded in the report)")
        for problem in vertex_problems:
            out(f"  WOULD REFUSE: {problem}")
        for arm in resolved:
            names = required_secret_names(arm, cfg)
            state = ", ".join(f"{n}={'set' if environ.get(n) else 'missing'}" for n in names)
            out(f"  {arm['id']} secrets by name: {state}")
            if provider_of(arm) == "gemini":
                out(f"  {arm['id']} Gemini thinking budget: {_thinking_note(arm)}")
        ok = print_plan(plans, cfg, ledger, out)
        return 0 if ok else 1

    if vertex_problems:
        out("REFUSE:\n  " + "\n  ".join(vertex_problems))
        return 2
    pending = [a["id"] for a in resolved if provider_of(a) == "gemini"
               and (a.get("thinkingBudget") or {}).get("value") is None]
    if pending and not args.calibrate_only:
        out(f"REFUSE: record the run-day Gemini thinking-budget decision in arms.json (thinkingBudget) for "
            f"{', '.join(pending)}. The adapter omits thinkingConfig unless Gemini:ThinkingBudget is set, so "
            f"the model's default thinking changes latency and cost; --calibrate-only measures it first.")
        return 2
    missing = sorted({n for a in resolved for n in required_secret_names(a, cfg) if not environ.get(n)})
    if missing:
        out(f"missing secret environment variables (by name): {', '.join(missing)}")
        return 2
    if args.calibrate_only:
        return run_calibrations(resolved, cfg, runs_dir, ledger, environ, out)
    if not print_plan(plans, cfg, ledger, out):
        return 1
    if not dll.exists():
        out(f"API DLL missing: {dll} (dotnet build -c Release in backend/)")
        return 2

    canned_patterns = bc.read_json(bc.EXPECTATIONS)["lexicons"]["refusalPatterns"]
    head = git_head()
    dll_sha = bc.sha256_file(dll)
    pin_problem = check_pin(runs_dir, {"gitHead": head, "apiDllSha256": dll_sha,
                                       "probesManifestSha256": bc.sha256_file(bc.MANIFEST)})
    if pin_problem:
        out(pin_problem)
        return 2
    status = 0
    for arm in resolved:
        if ledger.cumulative() >= cfg["spend"]["cumulativeStopUsd"]:
            out(f"REFUSE {arm['id']}: ledger ${ledger.cumulative():.2f} has reached "
                f"${cfg['spend']['cumulativeStopUsd']:.2f}")
            return 1
        passes = arm_passes(arm, cfg, args.passes)
        arm_dir = runs_dir / arm["id"]
        for sub in ("audio-blobs", "backups", "uploads", "results"):
            (arm_dir / sub).mkdir(parents=True, exist_ok=True)
        env, runtime = build_server_env(arm, cfg, arm_dir, environ)
        base = f"http://127.0.0.1:{arm['port']}"
        server = ApiServer(dll, bc.REPO_ROOT / cfg["apiWorkingDir"], env, arm_dir / "api.log", base,
                           runtime["operator"])
        manifest = load_arm_manifest(arm, cfg, arm_dir, head, dll_sha)
        tracker = SpendTracker(arm, cfg, ledger, calibration_for(arm, manifest))
        session: dict[str, Any] = {"passes": passes, "sets": list(sets), "startedAt": utc_now(),
                                   "vertexFallback": arm.get("vertexFallback")}
        manifest["sessions"].append(session)
        out(f"\n=== {arm['id']}: {arm['label']} on {base} (head {head}, passes {passes}) ===")
        tracker.entry["status"] = "running"
        ledger.save()
        try:
            if not calibration_for(arm, manifest):
                ensure_calibration(arm, cfg, manifest, tracker, environ, out)
            replan = plan_arm(arm, cfg, sets, passes, not args.skip_benchmarks, manifest["calibration"])
            out(f"  [{arm['id']}] re-estimate with measured tokens: ${replan['usd']:.2f} of "
                f"${replan['budgetUsd']:.2f}" + ("" if replan["fitsBudget"] else
                                                 " (OVER: the guard stops it; pass 1 completes first)"))
            server.start(cfg.get("healthTimeoutSeconds", 120))
            api = Api(base)
            api.create_parent(arm["id"])
            api.set_operator_token(runtime["operator"])
            openai_key = environ.get(cfg["secretEnv"].get("OpenAI__ApiKey", "OPENAI_API_KEY"))
            run = ArmRun(arm, cfg, arm_dir, api, LogTail(arm_dir / "api.log", args.log_settle_ms),
                         arm_dir / "areg.db", tracker, ledger, WavCache(runs_dir / "_wav", openai_key),
                         canned_patterns, out)
            session["moderation"] = moderation_snapshot(openai_key)
            tracker.add("moderation_snapshot", usd=0.0)
            if not args.skip_benchmarks:
                session["benchmarks"] = run_benchmarks(run, base, args.rerun_benchmarks)
                run.checkpoint("BenchmarkAll")
            run_passes(run, sets, passes)
            session["stopped"] = run.stopped
            tracker.entry["status"] = "stopped" if run.stopped else "done"
            if run.stopped:
                out(f"  [{arm['id']}] STOPPED: {run.stopped}")
                status = 1
        except SetupError as e:
            out(f"  [{arm['id']}] FAILED: {e}")
            tracker.entry["status"] = "failed"
            session["failed"] = str(e)
            status = 1
        finally:
            server.stop()
            session["finishedAt"] = utc_now()
            bc.write_json(arm_dir / "arm-run.json", manifest)
            ledger.save()
    return status


def run_passes(run: ArmRun, sets: dict[str, dict[str, Any]], passes: int) -> None:
    """Pass by pass: every set once, then the repeatable sets again. A
    budget stop therefore always leaves at least one complete pass, never
    three passes of the first sets and none of the last."""
    for pass_no in range(1, passes + 1):
        for name, s in sets.items():
            if run.stopped:
                return
            if pass_no > 1 and not s.get("repeatable"):
                continue
            run.run_set(s, pass_no)
            run.checkpoint(f"{name} pass {pass_no}")


def load_arm_manifest(arm: dict[str, Any], cfg: dict[str, Any], arm_dir: Path, head: str | None,
                      dll_sha: str | None) -> dict[str, Any]:
    """<arm>/arm-run.json: what this arm ran with (env NAMES only), its
    calibration and its sessions. The env and label are refreshed on every
    session so the grader and report read the config actually in effect."""
    path = arm_dir / "arm-run.json"
    manifest = bc.read_json(path) if path.exists() else {"arm": arm["id"], "sessions": []}
    manifest.setdefault("sessions", [])
    # pin.json guarantees one build per runs dir, so refreshing is safe; a
    # --calibrate-only write (no DLL needed) must not freeze a None here.
    for key, value in (("gitHead", head), ("apiDllSha256", dll_sha),
                       ("probesManifestSha256", bc.sha256_file(bc.MANIFEST))):
        if value is not None or key not in manifest:
            manifest[key] = value
    manifest.update(label=arm["label"], env=public_env(arm, cfg), vertexFallback=arm.get("vertexFallback"))
    if provider_of(arm) == "gemini":
        manifest["thinkingBudget"] = arm.get("thinkingBudget")
    return manifest


def ensure_calibration(arm: dict[str, Any], cfg: dict[str, Any], manifest: dict[str, Any],
                       tracker: SpendTracker, environ: dict[str, str], out=print) -> dict[str, Any]:
    reason = tracker.limit_reason(tracker.rates["calibration"])
    if reason:
        raise SetupError(f"calibration refused: {reason}")
    cal = calibrate(arm, cfg, environ)
    manifest["calibration"] = cal
    gp = arm["guardPrices"]
    tracker.add("calibration", usd=cal["promptTokens"] * gp["chatInPerMTok"] / 1e6
                + cal["outTokensTotal"] * gp["chatOutPerMTok"] / 1e6)
    tracker.apply_calibration(cal)
    out(f"  [{arm['id']}] calibration: prompt {cal['promptTokens']} tokens for {cal['promptChars']} chars "
        f"({cal['tokensPerChar']} tokens/char), output {cal['outTokensTotal']} tokens of which "
        f"{cal['reasoningTokens']} reasoning/thinking, {cal['latencyMs']} ms")
    return cal


def run_calibrations(arms: list[dict[str, Any]], cfg: dict[str, Any], runs_dir: Path, ledger: Ledger,
                     environ: dict[str, str], out=print) -> int:
    """--calibrate-only: the measurement alone, e.g. to decide a Gemini
    thinking budget on the run day before any probe is sent."""
    status = 0
    for arm in arms:
        arm_dir = runs_dir / arm["id"]
        arm_dir.mkdir(parents=True, exist_ok=True)
        manifest = load_arm_manifest(arm, cfg, arm_dir, git_head(), None)
        tracker = SpendTracker(arm, cfg, ledger)
        try:
            if calibration_for(arm, manifest):
                out(f"  [{arm['id']}] already calibrated for this model and thinking budget; nothing sent")
                continue
            ensure_calibration(arm, cfg, manifest, tracker, environ, out)
        except SetupError as e:
            out(f"  [{arm['id']}] FAILED: {e}")
            status = 1
        finally:
            bc.write_json(arm_dir / "arm-run.json", manifest)
            ledger.save()
    return status


if __name__ == "__main__":
    sys.exit(main())

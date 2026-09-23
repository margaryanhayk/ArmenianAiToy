#!/usr/bin/env python3
"""Score Armenian speech-to-text candidates on WER/CER + latency.

WHY THIS EXISTS
----------------
OpenAI is removing `gpt-4o-mini-transcribe` (and `gpt-4o-transcribe` /
`whisper-1`) on 2027-02-26, in favour of `gpt-transcribe` /
`gpt-live-transcribe`. Areg's voice pipeline depends on Armenian STT
accuracy for a 4-7-year-old's speech; this tool scores whatever candidate
providers/models are available on a directory of real recordings so a
replacement can be chosen on evidence.

INPUT
-----
A directory of `.wav` files, each with a sibling `.txt` reference
transcript (UTF-8 Armenian), e.g. `sample01.wav` + `sample01.txt`. A `.wav`
with no matching `.txt` (or vice versa) is skipped and reported, not fatal.

PROVIDERS  (repeat --provider to compare several)
--------------------------------------------------
  openai:<model>   POST https://api.openai.com/v1/audio/transcriptions
                   multipart (model, file, language=hy). OPENAI_API_KEY.
                   e.g. --provider openai:gpt-4o-mini-transcribe
                        --provider openai:gpt-transcribe

  elevenlabs       POST https://api.elevenlabs.io/v1/speech-to-text
                   multipart (model_id, file, language_code=hye).
                   ELEVENLABS_API_KEY. model_id defaults to scribe_v2,
                   override with --el-model.
                   REFUSES TO RUN unless --adult-voices-only is passed --
                   see the privacy note below.

  azure            Azure Speech "fast transcription" REST:
                   POST https://{region}.api.cognitive.microsoft.com/
                   speechtotext/transcriptions:transcribe?api-version=2024-11-15
                   multipart ('audio' file, 'definition' JSON
                   {"locales":["hy-AM"]}). AZURE_SPEECH_KEY +
                   AZURE_SPEECH_REGION. Written from documentation, NOT
                   live-verified against a real Azure endpoint this session.

  cmd:<command>    Run a shell command for a self-hosted model (e.g. Meta
                   Omnilingual ASR, a fine-tuned Whisper). `{wav}` in the
                   command is replaced with the wav file's path; stdout
                   (stripped) is taken as the transcript.
                   e.g. --provider "cmd:./my_asr.sh {wav}"

USAGE
    python3 stt_wer_bench.py --dry-run --audio-dir samples/
    python3 stt_wer_bench.py --audio-dir samples/ \\
        --provider openai:gpt-4o-mini-transcribe --provider openai:gpt-transcribe \\
        --out results.json

PRIVACY
-------
Recordings of children require parental consent to collect at all. Keep the
corpus directory OUTSIDE git; nothing under it is added to this repo by
this tool. See the ElevenLabs note below for a hard rule on top of that.

Exit code: 0 on a completed run (including --dry-run); 1 on a setup error
(missing keys for a requested provider, empty corpus, etc).
"""
from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import statistics
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid
from pathlib import Path
from typing import Any, Callable

MAX_TRIES = 4
BASE_SLEEP_S = 0.35

# ----------------------------------------------------------------- normalize
# Armenian punctuation: ։ (full stop) ՝ (comma) ՞ (question) ՜ (exclamation)
# ՛ (emphasis) « » (quotes), plus the usual ASCII punctuation.
_ARM_PUNCT = "։՝՞՜՛«»"
_PUNCT_RE = re.compile(r"[" + re.escape(_ARM_PUNCT) + r".,!?;:\"'()\[\]{}\-–—_/\\*&^%$#@~`]")
_WS_RE = re.compile(r"\s+")


def normalize(text: str) -> str:
    """Armenian-aware normalization: lowercase, strip punctuation (Armenian
    + ASCII), collapse whitespace. Pure."""
    t = text.lower()
    t = _PUNCT_RE.sub(" ", t)
    t = _WS_RE.sub(" ", t).strip()
    return t


# ----------------------------------------------------------------- edit distance
def levenshtein(a: list[str] | str, b: list[str] | str) -> int:
    """Pure-Python Levenshtein distance over any sequence (chars or
    tokens)."""
    if a == b:
        return 0
    la, lb = len(a), len(b)
    if la == 0:
        return lb
    if lb == 0:
        return la
    prev = list(range(lb + 1))
    for i in range(1, la + 1):
        cur = [i] + [0] * lb
        ai = a[i - 1]
        for j in range(1, lb + 1):
            cost = 0 if ai == b[j - 1] else 1
            cur[j] = min(
                prev[j] + 1,       # deletion
                cur[j - 1] + 1,    # insertion
                prev[j - 1] + cost,  # substitution
            )
        prev = cur
    return prev[lb]


def wer(reference: str, hypothesis: str) -> float:
    """Word error rate after normalization. 0.0 for two empty strings."""
    ref_n, hyp_n = normalize(reference), normalize(hypothesis)
    ref_words = ref_n.split()
    hyp_words = hyp_n.split()
    if not ref_words:
        return 0.0 if not hyp_words else 1.0
    return levenshtein(ref_words, hyp_words) / len(ref_words)


def cer(reference: str, hypothesis: str) -> float:
    """Character error rate after normalization (whitespace-collapsed,
    punctuation-stripped). 0.0 for two empty strings."""
    ref_n, hyp_n = normalize(reference), normalize(hypothesis)
    ref_n = ref_n.replace(" ", "")
    hyp_n = hyp_n.replace(" ", "")
    if not ref_n:
        return 0.0 if not hyp_n else 1.0
    return levenshtein(ref_n, hyp_n) / len(ref_n)


# ----------------------------------------------------------------- corpus
def find_pairs(audio_dir: Path) -> tuple[list[tuple[Path, Path]], list[str]]:
    """Return (pairs, warnings). A pair is (wav_path, reference_text)."""
    wavs = sorted(audio_dir.glob("*.wav"))
    pairs: list[tuple[Path, Path]] = []
    warnings: list[str] = []
    for wav in wavs:
        txt = wav.with_suffix(".txt")
        if not txt.exists():
            warnings.append(f"{wav.name}: no matching .txt reference, skipped")
            continue
        pairs.append((wav, txt))
    txts = {p.stem for p in audio_dir.glob("*.txt")}
    wav_stems = {w.stem for w in wavs}
    for stem in sorted(txts - wav_stems):
        warnings.append(f"{stem}.txt: no matching .wav audio, skipped")
    return pairs, warnings


# ----------------------------------------------------------------- multipart
def _multipart_body(fields: dict[str, str], files: dict[str, tuple[str, bytes, str]]) -> tuple[bytes, str]:
    boundary = f"----stt-bench-{uuid.uuid4().hex}"
    parts = []
    for name, value in fields.items():
        parts.append(f"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"\r\n\r\n{value}\r\n".encode("utf-8"))
    for name, (filename, data, content_type) in files.items():
        header = (
            f"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"; filename=\"{filename}\"\r\n"
            f"Content-Type: {content_type}\r\n\r\n"
        ).encode("utf-8")
        parts.append(header + data + b"\r\n")
    parts.append(f"--{boundary}--\r\n".encode("utf-8"))
    body = b"".join(parts)
    return body, f"multipart/form-data; boundary={boundary}"


def _post_multipart(url: str, headers: dict[str, str], fields: dict[str, str],
                     files: dict[str, tuple[str, bytes, str]]) -> dict[str, Any]:
    body, content_type = _multipart_body(fields, files)
    req = urllib.request.Request(url, data=body, method="POST", headers={**headers, "Content-Type": content_type})
    last_err: Exception | None = None
    for attempt in range(1, MAX_TRIES + 1):
        try:
            with urllib.request.urlopen(req, timeout=60) as resp:
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
    raise last_err  # pragma: no cover


# ----------------------------------------------------------------- providers
class ProviderError(RuntimeError):
    pass


def make_openai_transcriber(model: str) -> Callable[[Path], str]:
    key = os.environ.get("OPENAI_API_KEY")
    if not key:
        raise ProviderError("OPENAI_API_KEY is not set")

    def call(wav: Path) -> str:
        result = _post_multipart(
            "https://api.openai.com/v1/audio/transcriptions",
            {"Authorization": f"Bearer {key}"},
            {"model": model, "language": "hy"},
            {"file": (wav.name, wav.read_bytes(), "audio/wav")},
        )
        return result.get("text", "")

    return call


def make_elevenlabs_transcriber(el_model: str, adult_voices_only: bool) -> Callable[[Path], str]:
    if not adult_voices_only:
        raise ProviderError(
            "ElevenLabs' use policy forbids uploading voice data of anyone under 18. "
            "Pass --adult-voices-only to confirm every recording sent is an adult's voice; "
            "NEVER send a child's recording to this provider."
        )
    key = os.environ.get("ELEVENLABS_API_KEY")
    if not key:
        raise ProviderError("ELEVENLABS_API_KEY is not set")

    def call(wav: Path) -> str:
        result = _post_multipart(
            "https://api.elevenlabs.io/v1/speech-to-text",
            {"xi-api-key": key},
            {"model_id": el_model, "language_code": "hye"},
            {"file": (wav.name, wav.read_bytes(), "audio/wav")},
        )
        return result.get("text", "")

    return call


def make_azure_transcriber() -> Callable[[Path], str]:
    key = os.environ.get("AZURE_SPEECH_KEY")
    region = os.environ.get("AZURE_SPEECH_REGION")
    if not key or not region:
        raise ProviderError("AZURE_SPEECH_KEY and/or AZURE_SPEECH_REGION are not set")

    def call(wav: Path) -> str:
        url = f"https://{region}.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe?api-version=2024-11-15"
        definition = json.dumps({"locales": ["hy-AM"]})
        result = _post_multipart(
            url,
            {"Ocp-Apim-Subscription-Key": key},
            {"definition": definition},
            {"audio": (wav.name, wav.read_bytes(), "audio/wav")},
        )
        # Fast transcription response shape per docs: combinedPhrases[0].text
        phrases = result.get("combinedPhrases") or []
        if phrases:
            return phrases[0].get("text", "")
        return ""

    return call


def make_cmd_transcriber(command_template: str) -> Callable[[Path], str]:
    def call(wav: Path) -> str:
        cmd = command_template.replace("{wav}", str(wav))
        proc = subprocess.run(cmd, shell=True, capture_output=True, text=True, timeout=120)
        if proc.returncode != 0:
            raise ProviderError(f"cmd provider failed (exit {proc.returncode}): {proc.stderr.strip()[:300]}")
        return proc.stdout.strip()

    return call


def build_provider(spec: str, el_model: str, adult_voices_only: bool) -> tuple[str, Callable[[Path], str]]:
    if spec.startswith("openai:"):
        model = spec.split(":", 1)[1]
        return spec, make_openai_transcriber(model)
    if spec == "elevenlabs":
        return f"elevenlabs:{el_model}", make_elevenlabs_transcriber(el_model, adult_voices_only)
    if spec == "azure":
        return "azure", make_azure_transcriber()
    if spec.startswith("cmd:"):
        template = spec.split(":", 1)[1]
        return spec, make_cmd_transcriber(template)
    raise ProviderError(f"unknown provider spec: {spec!r}")


# ----------------------------------------------------------------- scoring / report
def score_file(reference: str, hypothesis: str, latency_s: float) -> dict[str, Any]:
    """Pure per-file scoring row."""
    return {
        "wer": round(wer(reference, hypothesis), 4),
        "cer": round(cer(reference, hypothesis), 4),
        "latency_s": round(latency_s, 3),
    }


def percentile(values: list[float], pct: float) -> float:
    """Nearest-rank percentile, pure. pct in [0, 100]."""
    if not values:
        return 0.0
    s = sorted(values)
    k = max(0, min(len(s) - 1, int(round(pct / 100 * (len(s) - 1)))))
    return s[k]


def summarize_provider(rows: list[dict[str, Any]], failures: int) -> dict[str, Any]:
    """Aggregate per-file rows (from score_file) into one provider summary.
    Pure."""
    wers = [r["wer"] for r in rows]
    cers = [r["cer"] for r in rows]
    lats = [r["latency_s"] for r in rows]
    return {
        "files_scored": len(rows),
        "failures": failures,
        "mean_wer": round(statistics.mean(wers), 4) if wers else None,
        "mean_cer": round(statistics.mean(cers), 4) if cers else None,
        "median_latency_s": round(statistics.median(lats), 3) if lats else None,
        "p90_latency_s": round(percentile(lats, 90), 3) if lats else None,
    }


def to_markdown(summaries: dict[str, dict[str, Any]]) -> str:
    lines = [
        "| provider | files | failures | mean WER | mean CER | median latency (s) | p90 latency (s) |",
        "| --- | --- | --- | --- | --- | --- | --- |",
    ]
    for name, s in summaries.items():
        wer_s = "n/a" if s["mean_wer"] is None else f"{s['mean_wer']:.1%}"
        cer_s = "n/a" if s["mean_cer"] is None else f"{s['mean_cer']:.1%}"
        med = "n/a" if s["median_latency_s"] is None else f"{s['median_latency_s']:.2f}"
        p90 = "n/a" if s["p90_latency_s"] is None else f"{s['p90_latency_s']:.2f}"
        lines.append(f"| {name} | {s['files_scored']} | {s['failures']} | {wer_s} | {cer_s} | {med} | {p90} |")
    return "\n".join(lines)


# ----------------------------------------------------------------- run
def run_provider(name: str, transcribe: Callable[[Path], str], pairs: list[tuple[Path, Path]]) -> dict[str, Any]:
    rows = []
    detail = []
    failures = 0
    for wav, txt in pairs:
        reference = txt.read_text(encoding="utf-8")
        t0 = time.perf_counter()
        try:
            hyp = transcribe(wav)
        except Exception as e:  # noqa: BLE001 - one failed file must not kill the run
            failures += 1
            detail.append({"file": wav.name, "error": str(e)[:300]})
            continue
        latency = time.perf_counter() - t0
        row = score_file(reference, hyp, latency)
        rows.append(row)
        detail.append({"file": wav.name, "hypothesis": hyp, "reference": reference, **row})
    summary = summarize_provider(rows, failures)
    return {"summary": summary, "detail": detail}


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--audio-dir", type=Path, required=True, help="directory of .wav + sibling .txt reference pairs")
    ap.add_argument("--provider", action="append", default=[], help="openai:<model> | elevenlabs | azure | cmd:<cmd with {wav}> (repeatable)")
    ap.add_argument("--el-model", default="scribe_v2", help="ElevenLabs model_id (default: %(default)s)")
    ap.add_argument("--adult-voices-only", action="store_true",
                     help="required to use --provider elevenlabs; confirms every recording is an adult's voice")
    ap.add_argument("--out", type=Path, default=None, help="write full per-file JSON results here")
    ap.add_argument("--dry-run", action="store_true", help="list pairs and providers, no network")
    args = ap.parse_args(argv)

    pairs, warnings = find_pairs(args.audio_dir)
    for w in warnings:
        print(f"warning: {w}", file=sys.stderr)

    if args.dry_run:
        print(f"audio-dir: {args.audio_dir}")
        print(f"pairs: {len(pairs)} (.wav + .txt)")
        for wav, txt in pairs:
            print(f"  {wav.name} <-> {txt.name}")
        print(f"providers requested: {args.provider or '(none)'}")
        print("dry-run: no requests sent")
        return 0

    if not pairs:
        print(f"no .wav/.txt pairs found under {args.audio_dir}", file=sys.stderr)
        return 1
    if not args.provider:
        print("no --provider given; nothing to run", file=sys.stderr)
        return 1

    results: dict[str, Any] = {}
    summaries: dict[str, dict[str, Any]] = {}
    for spec in args.provider:
        try:
            name, transcribe = build_provider(spec, args.el_model, args.adult_voices_only)
        except ProviderError as e:
            print(f"skipping provider {spec!r}: {e}", file=sys.stderr)
            continue
        print(f"running provider: {name} ({len(pairs)} files)...", file=sys.stderr)
        results[name] = run_provider(name, transcribe, pairs)
        summaries[name] = results[name]["summary"]

    if not summaries:
        print("no provider produced results", file=sys.stderr)
        return 1

    print(to_markdown(summaries))

    if args.out:
        args.out.write_text(json.dumps({"audio_dir": str(args.audio_dir), "results": results}, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"\nwrote {args.out}")

    return 0


if __name__ == "__main__":
    sys.exit(main())

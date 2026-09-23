#!/usr/bin/env python3
"""Offline unit tests for stt_wer_bench.py. No network.

Run:
    cd tools/stt-bench && python3 -m unittest
"""
from __future__ import annotations

import io
import struct
import subprocess
import sys
import tempfile
import unittest
import wave
from contextlib import redirect_stdout
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import stt_wer_bench as bench  # noqa: E402


class NormalizeTests(unittest.TestCase):
    def test_lowercases_and_strips_ascii_punctuation(self):
        self.assertEqual(bench.normalize("Hello, World!"), "hello world")

    def test_strips_armenian_punctuation(self):
        # ։ ՝ ՞ ՜ ՛ « »
        text = "Ողջույն։ «ինչպե՞ս ես»՝"
        norm = bench.normalize(text)
        for ch in "։՝՞«»":
            self.assertNotIn(ch, norm)

    def test_collapses_whitespace(self):
        self.assertEqual(bench.normalize("a   b\t\tc\n"), "a b c")


class LevenshteinTests(unittest.TestCase):
    def test_identical_sequences(self):
        self.assertEqual(bench.levenshtein("abc", "abc"), 0)

    def test_empty_vs_nonempty(self):
        self.assertEqual(bench.levenshtein("", "abc"), 3)
        self.assertEqual(bench.levenshtein("abc", ""), 3)

    def test_single_substitution(self):
        self.assertEqual(bench.levenshtein("cat", "cot"), 1)

    def test_word_list_distance(self):
        self.assertEqual(bench.levenshtein(["a", "b", "c"], ["a", "x", "c"]), 1)


class WerCerTests(unittest.TestCase):
    def test_exact_match_is_zero(self):
        ref = "Բարև, ինչպե՞ս ես դու այսօր։"
        self.assertEqual(bench.wer(ref, ref), 0.0)
        self.assertEqual(bench.cer(ref, ref), 0.0)

    def test_one_word_substitution(self):
        ref = "Ես շատ եմ սիրում իմ ընկերոջը"
        hyp = "Ես շատ եմ սիրում իմ ընկերուհուն"
        w = bench.wer(ref, hyp)
        # 6 reference words, 1 substituted
        self.assertAlmostEqual(w, 1 / 6)
        c = bench.cer(ref, hyp)
        self.assertGreater(c, 0.0)
        self.assertLess(c, 1.0)

    def test_wer_ignores_punctuation_and_case_differences(self):
        ref = "Բարև, աշխարհ։"
        hyp = "բարև աշխարհ"
        self.assertEqual(bench.wer(ref, hyp), 0.0)

    def test_both_empty_is_zero(self):
        self.assertEqual(bench.wer("", ""), 0.0)
        self.assertEqual(bench.cer("", ""), 0.0)

    def test_empty_reference_nonempty_hypothesis_is_one(self):
        self.assertEqual(bench.wer("", "hello"), 1.0)


class PercentileTests(unittest.TestCase):
    def test_median_via_percentile(self):
        self.assertEqual(bench.percentile([1, 2, 3, 4, 5], 50), 3)

    def test_p90(self):
        vals = list(range(1, 11))  # 1..10
        self.assertEqual(bench.percentile(vals, 90), 9)

    def test_empty_list(self):
        self.assertEqual(bench.percentile([], 50), 0.0)


class ScoreAndSummarizeTests(unittest.TestCase):
    def test_score_file_shape(self):
        row = bench.score_file("hello world", "hello world", 1.25)
        self.assertEqual(row["wer"], 0.0)
        self.assertEqual(row["cer"], 0.0)
        self.assertEqual(row["latency_s"], 1.25)

    def test_summarize_provider_aggregates(self):
        rows = [
            bench.score_file("a b c", "a b c", 1.0),
            bench.score_file("a b c", "a x c", 2.0),
        ]
        summary = bench.summarize_provider(rows, failures=1)
        self.assertEqual(summary["files_scored"], 2)
        self.assertEqual(summary["failures"], 1)
        self.assertAlmostEqual(summary["mean_wer"], (0 + 1 / 3) / 2, places=3)
        self.assertEqual(summary["median_latency_s"], 1.5)

    def test_summarize_provider_empty(self):
        summary = bench.summarize_provider([], failures=0)
        self.assertIsNone(summary["mean_wer"])
        self.assertIsNone(summary["median_latency_s"])

    def test_markdown_renders_providers(self):
        summaries = {"openai:gpt-transcribe": bench.summarize_provider(
            [bench.score_file("a", "a", 0.5)], failures=0)}
        md = bench.to_markdown(summaries)
        self.assertIn("openai:gpt-transcribe", md)


class ElevenLabsRefusalTests(unittest.TestCase):
    def test_refuses_without_adult_voices_only_flag(self):
        with self.assertRaises(bench.ProviderError) as ctx:
            bench.make_elevenlabs_transcriber("scribe_v2", adult_voices_only=False)
        self.assertIn("under 18", str(ctx.exception))

    def test_build_provider_refuses_elevenlabs_without_flag(self):
        with self.assertRaises(bench.ProviderError):
            bench.build_provider("elevenlabs", "scribe_v2", adult_voices_only=False)

    def test_allows_with_flag_and_key_present(self):
        import os
        os.environ["ELEVENLABS_API_KEY"] = "fake-key-for-test"
        try:
            transcribe = bench.make_elevenlabs_transcriber("scribe_v2", adult_voices_only=True)
            self.assertTrue(callable(transcribe))
        finally:
            del os.environ["ELEVENLABS_API_KEY"]

    def test_still_requires_key_even_with_flag(self):
        import os
        os.environ.pop("ELEVENLABS_API_KEY", None)
        with self.assertRaises(bench.ProviderError):
            bench.make_elevenlabs_transcriber("scribe_v2", adult_voices_only=True)


class CmdProviderTests(unittest.TestCase):
    def test_cmd_provider_runs_and_captures_stdout(self):
        with tempfile.TemporaryDirectory() as d:
            wav = Path(d) / "sample.wav"
            wav.write_bytes(b"")
            transcribe = bench.make_cmd_transcriber("echo hello-{wav}")
            out = transcribe(wav)
            self.assertIn("hello-", out)
            self.assertIn(str(wav), out)

    def test_cmd_provider_raises_on_nonzero_exit(self):
        transcribe = bench.make_cmd_transcriber("exit 1")
        with self.assertRaises(bench.ProviderError):
            transcribe(Path("/nonexistent.wav"))


def _write_silent_wav(path: Path, seconds: float = 0.2, rate: int = 16000) -> None:
    n = int(seconds * rate)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(struct.pack("<%dh" % n, *([0] * n)))


class FindPairsAndDryRunTests(unittest.TestCase):
    def test_find_pairs_matches_and_warns(self):
        with tempfile.TemporaryDirectory() as d:
            d = Path(d)
            _write_silent_wav(d / "one.wav")
            (d / "one.txt").write_text("Բարև", encoding="utf-8")
            _write_silent_wav(d / "orphan.wav")
            (d / "orphan2.txt").write_text("x", encoding="utf-8")
            pairs, warnings = bench.find_pairs(d)
            self.assertEqual(len(pairs), 1)
            self.assertEqual(pairs[0][0].name, "one.wav")
            self.assertTrue(any("orphan.wav" in w for w in warnings))
            self.assertTrue(any("orphan2.txt" in w for w in warnings))

    def test_dry_run_on_temp_dir(self):
        with tempfile.TemporaryDirectory() as d:
            d = Path(d)
            _write_silent_wav(d / "sample.wav")
            (d / "sample.txt").write_text("Բարև աշխարհ", encoding="utf-8")
            buf = io.StringIO()
            with redirect_stdout(buf):
                rc = bench.main(["--audio-dir", str(d), "--dry-run"])
            self.assertEqual(rc, 0)
            out = buf.getvalue()
            self.assertIn("pairs: 1", out)
            self.assertIn("dry-run: no requests sent", out)

    def test_cli_subprocess_dry_run(self):
        with tempfile.TemporaryDirectory() as d:
            d = Path(d)
            _write_silent_wav(d / "sample.wav")
            (d / "sample.txt").write_text("Բարև աշխարհ", encoding="utf-8")
            result = subprocess.run(
                [sys.executable, str(Path(__file__).resolve().parent / "stt_wer_bench.py"),
                 "--audio-dir", str(d), "--dry-run"],
                capture_output=True, text=True, timeout=30,
            )
            self.assertEqual(result.returncode, 0)
            self.assertIn("dry-run", result.stdout)

    def test_no_pairs_without_dry_run_exits_nonzero(self):
        with tempfile.TemporaryDirectory() as d:
            rc = bench.main(["--audio-dir", d, "--provider", "cmd:echo x"])
            self.assertEqual(rc, 1)

    def test_no_provider_without_dry_run_exits_nonzero(self):
        with tempfile.TemporaryDirectory() as d:
            d = Path(d)
            _write_silent_wav(d / "sample.wav")
            (d / "sample.txt").write_text("x", encoding="utf-8")
            rc = bench.main(["--audio-dir", str(d)])
            self.assertEqual(rc, 1)


class RunProviderWithCmdTests(unittest.TestCase):
    def test_run_provider_end_to_end_with_cmd(self):
        with tempfile.TemporaryDirectory() as d:
            d = Path(d)
            wav = d / "sample.wav"
            _write_silent_wav(wav)
            (d / "sample.txt").write_text("hello world", encoding="utf-8")
            pairs, _ = bench.find_pairs(d)
            name, transcribe = bench.build_provider("cmd:echo hello world", "scribe_v2", False)
            result = bench.run_provider(name, transcribe, pairs)
            self.assertEqual(result["summary"]["files_scored"], 1)
            self.assertEqual(result["summary"]["failures"], 0)
            self.assertEqual(result["summary"]["mean_wer"], 0.0)


if __name__ == "__main__":
    unittest.main()

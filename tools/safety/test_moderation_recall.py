#!/usr/bin/env python3
"""Offline unit tests for moderation_recall.py. No network.

Run:
    cd tools/safety && python3 -m unittest
"""
from __future__ import annotations

import io
import json
import subprocess
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import moderation_recall as mr  # noqa: E402


class TopCategoryTests(unittest.TestCase):
    def test_flagged_category_wins_even_if_not_highest_score(self):
        categories = {"violence": True, "sexual": False}
        scores = {"violence": 0.4, "sexual": 0.9}
        cat, score = mr.top_category(categories, scores)
        self.assertEqual(cat, "violence")
        self.assertEqual(score, 0.4)

    def test_no_flags_falls_back_to_highest_score(self):
        categories = {"violence": False, "sexual": False}
        scores = {"violence": 0.1, "sexual": 0.3}
        cat, score = mr.top_category(categories, scores)
        self.assertEqual(cat, "sexual")
        self.assertEqual(score, 0.3)

    def test_empty_scores(self):
        cat, score = mr.top_category({}, {})
        self.assertIsNone(cat)
        self.assertEqual(score, 0.0)


class ScoreCaseTests(unittest.TestCase):
    def test_unsafe_case_caught(self):
        case = {"id": "X1", "category": "violence", "language": "hy", "expected": "blocked"}
        row = mr.score_case(case, True, {"violence": True}, {"violence": 0.9})
        self.assertTrue(row["is_unsafe_case"])
        self.assertTrue(row["flagged"])
        self.assertTrue(row["caught"])
        self.assertFalse(row["false_positive"])

    def test_unsafe_case_missed(self):
        case = {"id": "X2", "category": "violence", "language": "hy", "expected": "safe_redirect"}
        row = mr.score_case(case, False, {}, {"violence": 0.01})
        self.assertTrue(row["is_unsafe_case"])
        self.assertFalse(row["caught"])
        self.assertFalse(row["false_positive"])

    def test_allowed_case_false_positive(self):
        case = {"id": "X3", "category": "benign_control", "language": "en", "expected": "allowed"}
        row = mr.score_case(case, True, {"harassment": True}, {"harassment": 0.6})
        self.assertFalse(row["is_unsafe_case"])
        self.assertIsNone(row["caught"])
        self.assertTrue(row["false_positive"])

    def test_allowed_case_correctly_unflagged(self):
        case = {"id": "X4", "category": "benign_control", "language": "en", "expected": "allowed"}
        row = mr.score_case(case, False, {}, {})
        self.assertFalse(row["false_positive"])


class AggregateTests(unittest.TestCase):
    def setUp(self):
        cases = [
            {"id": "A", "category": "violence", "language": "hy", "expected": "blocked"},
            {"id": "B", "category": "violence", "language": "hy", "expected": "blocked"},
            {"id": "C", "category": "violence", "language": "en", "expected": "allowed"},
        ]
        self.rows = [
            mr.score_case(cases[0], True, {"violence": True}, {"violence": 0.9}),
            mr.score_case(cases[1], False, {}, {"violence": 0.02}),
            mr.score_case(cases[2], True, {"violence": True}, {"violence": 0.5}),
        ]

    def test_recall_by_language(self):
        table = mr.aggregate(self.rows, "language")
        self.assertEqual(table["hy"]["unsafe_total"], 2)
        self.assertEqual(table["hy"]["unsafe_caught"], 1)
        self.assertAlmostEqual(table["hy"]["recall"], 0.5)
        self.assertIsNone(table["hy"]["false_positive_rate"])

    def test_false_positive_rate_by_language(self):
        table = mr.aggregate(self.rows, "language")
        self.assertEqual(table["en"]["allowed_total"], 1)
        self.assertEqual(table["en"]["false_positives"], 1)
        self.assertAlmostEqual(table["en"]["false_positive_rate"], 1.0)
        self.assertIsNone(table["en"]["recall"])

    def test_markdown_renders_all_groups(self):
        table = mr.aggregate(self.rows, "language")
        md = mr.to_markdown("Test", table)
        self.assertIn("hy", md)
        self.assertIn("en", md)


class CorpusLoadingTests(unittest.TestCase):
    def test_real_corpus_loads_and_validates(self):
        cases = mr.load_corpus(mr.DEFAULT_CORPUS)
        self.assertGreater(len(cases), 0)
        for c in cases:
            self.assertIn("text", c)
            self.assertIn(c["expected"], {"blocked", "safe_redirect", "allowed"})

    def test_rejects_non_array(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "bad.json"
            p.write_text(json.dumps({"not": "a list"}), encoding="utf-8")
            with self.assertRaises(ValueError):
                mr.load_corpus(p)

    def test_rejects_missing_field(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "bad.json"
            p.write_text(json.dumps([{"expected": "blocked"}]), encoding="utf-8")
            with self.assertRaises(ValueError):
                mr.load_corpus(p)

    def test_rejects_unknown_expected(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "bad.json"
            p.write_text(json.dumps([{"text": "x", "expected": "maybe"}]), encoding="utf-8")
            with self.assertRaises(ValueError):
                mr.load_corpus(p)


class DryRunTests(unittest.TestCase):
    def test_dry_run_exits_zero_and_prints_counts(self):
        buf = io.StringIO()
        with redirect_stdout(buf):
            rc = mr.main(["--dry-run"])
        self.assertEqual(rc, 0)
        out = buf.getvalue()
        self.assertIn("cases:", out)
        self.assertIn("dry-run: no requests sent", out)

    def test_no_key_and_no_dry_run_exits_nonzero(self):
        env_backup = __import__("os").environ.pop("OPENAI_API_KEY", None)
        try:
            rc = mr.main([])
            self.assertNotEqual(rc, 0)
        finally:
            if env_backup is not None:
                __import__("os").environ["OPENAI_API_KEY"] = env_backup

    def test_cli_subprocess_dry_run(self):
        result = subprocess.run(
            [sys.executable, str(Path(__file__).resolve().parent / "moderation_recall.py"), "--dry-run"],
            capture_output=True, text=True, timeout=30,
        )
        self.assertEqual(result.returncode, 0)
        self.assertIn("dry-run", result.stdout)


if __name__ == "__main__":
    unittest.main()

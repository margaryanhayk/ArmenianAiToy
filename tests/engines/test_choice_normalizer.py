"""Tests for the choice normalizer module (Phase B).

All tests are deterministic. Armenian inputs use Unicode escapes as the
canonical form so test correctness does not depend on terminal rendering.
"""

import sys
import os
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', '..'))

from backend.engines.choice_normalizer import normalize_choice, ChoiceResult

# Default option labels chosen so no content words accidentally match
# test inputs like "help him", "the dog one", "I want the fox".
OPT_A = "Cross the river on the log"
OPT_B = "Climb the mountain trail"


class TestPositionalEnglish(unittest.TestCase):
    """Tests 1-4: multi-word positional English."""

    def test_01_left(self):
        r = normalize_choice("left", OPT_A, OPT_B)
        self.assertEqual(r.normalized, "option_a")
        self.assertEqual(r.confidence, "high")
        self.assertEqual(r.method, "positional_en")
        self.assertEqual(r.raw_input, "left")

    def test_02_right(self):
        r = normalize_choice("right", OPT_A, OPT_B)
        self.assertEqual(r.normalized, "option_b")
        self.assertEqual(r.confidence, "high")

    def test_03_the_first_one(self):
        r = normalize_choice("the first one", OPT_A, OPT_B)
        self.assertEqual(r.normalized, "option_a")
        self.assertEqual(r.confidence, "high")
        self.assertEqual(r.method, "positional_en")

    def test_04_second(self):
        r = normalize_choice("second", OPT_A, OPT_B)
        self.assertEqual(r.normalized, "option_b")
        self.assertEqual(r.confidence, "high")


class TestSingleLetterPositional(unittest.TestCase):
    """Tests 11-12: bare "a" and "b" as quiz-style selectors.

    These only match when the entire stripped input is exactly "a" or "b".
    A child starting a sentence ("a dog...") will not match because the
    full input is longer than one token.
    """

    def test_11_a(self):
        r = normalize_choice("a", OPT_A, OPT_B)
        self.assertEqual(r.normalized, "option_a")
        self.assertEqual(r.confidence, "high")

    def test_12_b(self):
        r = normalize_choice("b", OPT_A, OPT_B)
        self.assertEqual(r.normalized, "option_b")
        self.assertEqual(r.confidence, "high")

    def test_a_in_sentence_does_not_match(self):
        # "a dog" should NOT match as positional "a" -- not a bare input.
        r = normalize_choice("a dog", OPT_A, OPT_B)
        self.assertEqual(r.normalized, "unknown")


class TestArmenianPositional(unittest.TestCase):
    # Armenian tokens (codepoint audit trail):
    #   option_a: \u0561\u057c\u0561\u057b\u056b\u0576\u0568 ("first")
    #             = U+0561 U+057C U+0561 U+057B U+056B U+0576 U+0568
    #   option_a: \u0574\u0565\u056f\u0568 ("one")
    #             = U+0574 U+0565 U+056F U+0568
    #   option_b: \u0565\u0580\u056f\u0580\u0578\u0580\u0564\u0568 ("second")
    #             = U+0565 U+0580 U+056F U+0580 U+0578 U+0580 U+0564 U+0568
    #   option_b: \u0565\u0580\u056f\u0578\u0582\u057d\u0568 ("two")
    #             = U+0565 U+0580 U+056F U+0578 U+0582 U+057D U+0568

    # Use escape forms so tests are unambiguous regardless of rendering.
    MEK = "\u0574\u0565\u056f\u0568"              # "one"
    YERKRORD = "\u0565\u0580\u056f\u0580\u0578\u0580\u0564\u0568"  # "second"

    def test_14_mek(self):
        r = normalize_choice(self.MEK, OPT_A, OPT_B)
        self.assertEqual(r.normalized, "option_a")
        self.assertEqual(r.confidence, "high")
        self.assertEqual(r.method, "positional_hy")

    def test_15_yerkrord(self):
        r = normalize_choice(self.YERKRORD, OPT_A, OPT_B)
        self.assertEqual(r.normalized, "option_b")
        self.assertEqual(r.confidence, "high")
        self.assertEqual(r.method, "positional_hy")

    def test_14_mek_codepoints(self):
        """Verify the literal matches expected codepoint sequence."""
        self.assertEqual(
            [hex(ord(c)) for c in self.MEK],
            ["0x574", "0x565", "0x56f", "0x568"],
        )

    def test_15_yerkrord_codepoints(self):
        """Verify the literal matches expected codepoint sequence."""
        self.assertEqual(
            [hex(ord(c)) for c in self.YERKRORD],
            ["0x565", "0x580", "0x56f", "0x580", "0x578", "0x580", "0x564", "0x568"],
        )


class TestUnknown(unittest.TestCase):
    """Tests 5-7, 9-10: inputs with no clear positional signal.

    ayo/voch are intentionally unknown: too ambiguous in a choice context.
    """

    def test_05_this_one(self):
        r = normalize_choice("this one", OPT_A, OPT_B)
        self.assertEqual(r.normalized, "unknown")

    def test_06_that_one(self):
        r = normalize_choice("that one", OPT_A, OPT_B)
        self.assertEqual(r.normalized, "unknown")

    def test_07_help_him(self):
        r = normalize_choice("help him", OPT_A, OPT_B)
        self.assertEqual(r.normalized, "unknown")

    def test_09_ayo_is_unknown(self):
        r = normalize_choice("ayo", OPT_A, OPT_B)
        self.assertEqual(r.normalized, "unknown")
        self.assertEqual(r.method, "no_match")

    def test_10_voch_is_unknown(self):
        r = normalize_choice("voch", OPT_A, OPT_B)
        self.assertEqual(r.normalized, "unknown")
        self.assertEqual(r.method, "no_match")

    def test_gibberish(self):
        r = normalize_choice("asdfghjkl", OPT_A, OPT_B)
        self.assertEqual(r.normalized, "unknown")
        self.assertEqual(r.confidence, "low")
        self.assertEqual(r.method, "no_match")

    def test_empty_input(self):
        r = normalize_choice("", OPT_A, OPT_B)
        self.assertEqual(r.normalized, "unknown")

    def test_whitespace_input(self):
        r = normalize_choice("   ", OPT_A, OPT_B)
        self.assertEqual(r.normalized, "unknown")


class TestKeywordMatch(unittest.TestCase):
    """Tests 8, 13: keyword matching against option labels.
    Ambiguous matches (both labels contain the keyword) return unknown."""

    def test_08_fox_matches_option_a(self):
        r = normalize_choice("I want the fox", "Help the fox", "Follow the bird")
        self.assertEqual(r.normalized, "option_a")
        self.assertEqual(r.confidence, "low")
        self.assertEqual(r.method, "keyword_match")

    def test_08_fox_unknown_when_not_in_labels(self):
        r = normalize_choice("I want the fox", OPT_A, OPT_B)
        self.assertEqual(r.normalized, "unknown")

    def test_13_dog_matches_label(self):
        r = normalize_choice("the dog one", "Pet the dog", "Feed the cat")
        self.assertEqual(r.normalized, "option_a")
        self.assertEqual(r.confidence, "low")
        self.assertEqual(r.method, "keyword_match")

    def test_13_dog_unknown_when_not_in_labels(self):
        r = normalize_choice("the dog one", OPT_A, OPT_B)
        self.assertEqual(r.normalized, "unknown")

    def test_both_labels_match_returns_unknown(self):
        r = normalize_choice("the big river", "Cross the river", "Swim the river")
        self.assertEqual(r.normalized, "unknown")


class TestRawInputPreserved(unittest.TestCase):
    """Raw input must always be preserved exactly as given,
    regardless of normalization outcome."""

    def test_raw_preserved_with_whitespace(self):
        r = normalize_choice("  Left  ", OPT_A, OPT_B)
        self.assertEqual(r.raw_input, "  Left  ")
        self.assertEqual(r.normalized, "option_a")

    def test_raw_preserved_on_unknown(self):
        r = normalize_choice("blah blah", OPT_A, OPT_B)
        self.assertEqual(r.raw_input, "blah blah")

    def test_raw_preserved_on_armenian(self):
        mek = "\u0574\u0565\u056f\u0568"
        r = normalize_choice(mek, OPT_A, OPT_B)
        self.assertEqual(r.raw_input, mek)

    def test_raw_preserved_on_keyword_match(self):
        r = normalize_choice("I want the fox", "Help the fox", "Follow the bird")
        self.assertEqual(r.raw_input, "I want the fox")


if __name__ == "__main__":
    unittest.main()

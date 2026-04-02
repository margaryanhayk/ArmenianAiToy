"""
Choice normalization for the story engine (Phase B).

Standalone helper module. Maps messy child input to a structured choice
result using simple heuristics. No ML, no embeddings, no external APIs.

API contract
------------
Function:
    normalize_choice(raw_input, option_a_label, option_b_label) -> ChoiceResult

Arguments:
    raw_input        : str  -- the child's verbatim input (any string)
    option_a_label   : str  -- display text of the first offered option
    option_b_label   : str  -- display text of the second offered option

Return (ChoiceResult dataclass, frozen):
    raw_input   : str  -- always == the raw_input argument, never modified
    normalized  : "option_a" | "option_b" | "unknown"
    confidence  : "high" | "low"
    method      : str describing which heuristic fired

Rules:
    - raw_input is ALWAYS preserved verbatim in the result.
    - If no heuristic matches clearly, normalized is "unknown".
    - "unknown" is not an error; callers must handle it gracefully.
    - Keyword matching returns "unknown" when input matches BOTH labels.
"""

from dataclasses import dataclass
from typing import Literal

NormalizedChoice = Literal["option_a", "option_b", "unknown"]
Confidence = Literal["high", "low"]


@dataclass(frozen=True)
class ChoiceResult:
    raw_input: str
    normalized: NormalizedChoice
    confidence: Confidence
    method: str


# ---------------------------------------------------------------------------
# Heuristic word sets (applied in priority order)
# ---------------------------------------------------------------------------

_POSITIONAL_A = {"first", "one", "left", "first one", "1"}
_POSITIONAL_B = {"second", "two", "right", "second one", "2"}

# "a" and "b" as bare single-word inputs are standard quiz-style option
# selectors ("pick a or b"). They only match when the ENTIRE stripped input
# equals "a" or "b", so a child starting a sentence with "a dog..." will
# NOT match -- that input has length > 1 word and won't be in the set.
_POSITIONAL_A_SINGLE = {"a"}
_POSITIONAL_B_SINGLE = {"b"}

# Armenian positional words (codepoint audit trail):
#   \u0561\u057c\u0561\u057b\u056b\u0576\u0568         = "first"  (7 chars)
#   \u0574\u0565\u056f\u0568                             = "one"    (4 chars)
#   \u0565\u0580\u056f\u0580\u0578\u0580\u0564\u0568   = "second" (8 chars)
#   \u0565\u0580\u056f\u0578\u0582\u057d\u0568         = "two"    (7 chars)
_ARMENIAN_POSITIONAL_A = {
    "\u0561\u057c\u0561\u057b\u056b\u0576\u0568",  # first
    "\u0574\u0565\u056f\u0568",                      # one
}
_ARMENIAN_POSITIONAL_B = {
    "\u0565\u0580\u056f\u0580\u0578\u0580\u0564\u0568",  # second
    "\u0565\u0580\u056f\u0578\u0582\u057d\u0568",        # two
}

# NOTE: "ayo" (yes) and "voch" (no) are intentionally NOT mapped.
# In a two-option context, yes/no is ambiguous -- a child may mean
# "yes I'm listening" rather than selecting the first option.

# Stop words for keyword matching -- common child speech that carries
# no option-selection signal.
_STOP_WORDS = {
    "the", "a", "an", "one", "i", "want", "like", "choose", "pick",
    "help", "him", "her", "it", "them", "go", "do", "let", "yes", "no",
    "that", "this", "me", "my", "is", "to", "in", "on", "of",
}


def _words_overlap(text: str, label: str) -> bool:
    """Check if any content word (>= 3 chars, not a stop word) in text
    appears as a substring in label. Conservative by design."""
    text_words = {
        w for w in text.lower().split()
        if w not in _STOP_WORDS and len(w) >= 3
    }
    label_lower = label.lower()
    return any(w in label_lower for w in text_words)


def normalize_choice(
    raw_input: str,
    option_a_label: str,
    option_b_label: str,
) -> ChoiceResult:
    """Normalize a child's raw choice input.

    See module docstring for full API contract.

    Heuristic priority:
    1. English positional words        -> high confidence
    2. Armenian positional words       -> high confidence
    3. Keyword overlap with labels     -> low confidence
       (returns unknown if BOTH labels match)
    4. Fallback                        -> unknown, low confidence
    """
    text = raw_input.strip().lower()

    # 1a. English positional (multi-char)
    if text in _POSITIONAL_A:
        return ChoiceResult(raw_input, "option_a", "high", "positional_en")
    if text in _POSITIONAL_B:
        return ChoiceResult(raw_input, "option_b", "high", "positional_en")

    # 1b. Single-letter "a"/"b" (exact bare input only)
    if text in _POSITIONAL_A_SINGLE:
        return ChoiceResult(raw_input, "option_a", "high", "positional_en")
    if text in _POSITIONAL_B_SINGLE:
        return ChoiceResult(raw_input, "option_b", "high", "positional_en")

    # 1c. "the first one" / "the second one" variants
    if "first" in text and "second" not in text:
        return ChoiceResult(raw_input, "option_a", "high", "positional_en")
    if "second" in text and "first" not in text:
        return ChoiceResult(raw_input, "option_b", "high", "positional_en")

    # 2. Armenian positional
    if text in _ARMENIAN_POSITIONAL_A:
        return ChoiceResult(raw_input, "option_a", "high", "positional_hy")
    if text in _ARMENIAN_POSITIONAL_B:
        return ChoiceResult(raw_input, "option_b", "high", "positional_hy")

    # 3. Keyword overlap (conservative: ambiguous both-match -> unknown)
    match_a = _words_overlap(text, option_a_label)
    match_b = _words_overlap(text, option_b_label)
    if match_a and not match_b:
        return ChoiceResult(raw_input, "option_a", "low", "keyword_match")
    if match_b and not match_a:
        return ChoiceResult(raw_input, "option_b", "low", "keyword_match")

    # 4. Unknown
    return ChoiceResult(raw_input, "unknown", "low", "no_match")

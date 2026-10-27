namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Normalizes messy child input into a structured choice result.
///
/// API contract:
///   ChoiceNormalizer.Normalize(rawInput, optionALabel, optionBLabel) -> ChoiceResult
///
///   rawInput       : the child's verbatim input (any string)
///   optionALabel   : display text of the first offered option
///   optionBLabel   : display text of the second offered option
///
/// Return (ChoiceResult record):
///   RawInput    : always == rawInput argument, never modified
///   Normalized  : "option_a" | "option_b" | "unknown"
///   Confidence  : "high" | "low"
///   Method      : which heuristic fired
///
/// Rules:
///   - RawInput is ALWAYS preserved verbatim.
///   - If no heuristic matches clearly, Normalized is "unknown".
///   - "unknown" is not an error; callers must handle it gracefully.
///   - Keyword matching returns "unknown" when input matches BOTH labels equally;
///     the label with strictly more distinct matching words wins.
///   - Armenian yes/no ("ayo", "voch") are NOT mapped — too ambiguous.
/// </summary>
public static class ChoiceNormalizer
{
    // English positional (multi-char)
    private static readonly HashSet<string> PositionalA =
        ["first", "one", "left", "first one", "1"];

    private static readonly HashSet<string> PositionalB =
        ["second", "two", "right", "second one", "2"];

    // Single-letter "a"/"b" — only match exact bare input.
    // A child starting a sentence ("a dog...") won't match because the
    // full stripped input has more than one character/word.
    private static readonly HashSet<string> PositionalASingle = ["a"];
    private static readonly HashSet<string> PositionalBSingle = ["b"];

    // Armenian positional words (codepoint audit trail):
    //   \u0561\u057c\u0561\u057b\u056b\u0576\u0568         = "first"  (7 chars)
    //   \u0574\u0565\u056f\u0568                             = "one"    (4 chars)
    //   \u0565\u0580\u056f\u0580\u0578\u0580\u0564\u0568   = "second" (8 chars)
    //   \u0565\u0580\u056f\u0578\u0582\u057d\u0568         = "two"    (7 chars)
    private static readonly HashSet<string> ArmenianPositionalA =
    [
        "\u0561\u057c\u0561\u057b\u056b\u0576\u0568",  // first
        "\u0574\u0565\u056f\u0568",                      // one
    ];

    private static readonly HashSet<string> ArmenianPositionalB =
    [
        "\u0565\u0580\u056f\u0580\u0578\u0580\u0564\u0568",  // second
        "\u0565\u0580\u056f\u0578\u0582\u057d\u0568",        // two
    ];

    // NOTE: "ayo" (yes) and "voch" (no) are intentionally NOT mapped.
    // In a two-option context, yes/no is ambiguous — a child may mean
    // "yes I'm listening" rather than selecting the first option.

    // Stop words for keyword matching — common child speech with no
    // option-selection signal.
    private static readonly HashSet<string> StopWords =
    [
        "the", "a", "an", "one", "i", "want", "like", "choose", "pick",
        "help", "him", "her", "it", "them", "go", "do", "let", "yes", "no",
        "that", "this", "me", "my", "is", "to", "in", "on", "of",
    ];

    // Punctuation that children commonly append to short replies. The Armenian
    // verjaket ։ (U+0589) and the marks ՞ ՜ ՝ (U+055E/055C/055D) are included
    // (AI logic review 2026-10-27, D4: «Առաջինը։» used to miss the positional check).
    private static readonly char[] TrailingPunctuation =
        ['.', ',', '!', '?', ';', ':', '\u0589', '\u055E', '\u055C', '\u055D'];

    public static ChoiceResult Normalize(
        string rawInput, string optionALabel, string optionBLabel)
    {
        var text = rawInput.Trim().ToLowerInvariant().TrimEnd(TrailingPunctuation);

        // 0. Exact label repeat (AI logic review 2026-10-27, D3). A child who repeats one label
        //    verbatim shares words with the OTHER label too, which used to make it "unknown".
        if (text.Length > 0)
        {
            var exactA = text == LabelKey(optionALabel);
            var exactB = text == LabelKey(optionBLabel);
            if (exactA && !exactB)
                return new ChoiceResult(rawInput, "option_a", "high", "exact_label");
            if (exactB && !exactA)
                return new ChoiceResult(rawInput, "option_b", "high", "exact_label");
        }

        // 1a. English positional (multi-char)
        if (PositionalA.Contains(text))
            return new ChoiceResult(rawInput, "option_a", "high", "positional_en");
        if (PositionalB.Contains(text))
            return new ChoiceResult(rawInput, "option_b", "high", "positional_en");

        // 1b. Single-letter "a"/"b" (exact bare input only)
        if (PositionalASingle.Contains(text))
            return new ChoiceResult(rawInput, "option_a", "high", "positional_en");
        if (PositionalBSingle.Contains(text))
            return new ChoiceResult(rawInput, "option_b", "high", "positional_en");

        // 1c. "the first one" / "the second one" variants (word-level, not substring)
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.TrimEnd(TrailingPunctuation)).ToArray();
        var hasFirst = words.Contains("first");
        var hasSecond = words.Contains("second");
        if (hasFirst && !hasSecond)
            return new ChoiceResult(rawInput, "option_a", "high", "positional_en");
        if (hasSecond && !hasFirst)
            return new ChoiceResult(rawInput, "option_b", "high", "positional_en");

        // 2. Armenian positional
        if (ArmenianPositionalA.Contains(text))
            return new ChoiceResult(rawInput, "option_a", "high", "positional_hy");
        if (ArmenianPositionalB.Contains(text))
            return new ChoiceResult(rawInput, "option_b", "high", "positional_hy");

        // 2b. Armenian positional, word-level (AI logic review 2026-10-27, D4): «Առաջինը, խնդրում եմ».
        //     Mirrors 1c; one-sided only, so «առաջինը երկրորդը» stays unknown.
        var hasArmenianA = words.Any(w => ArmenianPositionalA.Contains(w));
        var hasArmenianB = words.Any(w => ArmenianPositionalB.Contains(w));
        if (hasArmenianA && !hasArmenianB)
            return new ChoiceResult(rawInput, "option_a", "high", "positional_hy");
        if (hasArmenianB && !hasArmenianA)
            return new ChoiceResult(rawInput, "option_b", "high", "positional_hy");

        // 3. Keyword overlap (conservative: a tie, including both-match 1:1 -> unknown;
        //    AI logic review 2026-10-27, D3: strictly more distinct matching words wins)
        var matchA = DistinctOverlapCount(text, optionALabel);
        var matchB = DistinctOverlapCount(text, optionBLabel);
        if (matchA > matchB)
            return new ChoiceResult(rawInput, "option_a", "low", "keyword_match");
        if (matchB > matchA)
            return new ChoiceResult(rawInput, "option_b", "low", "keyword_match");

        // 4. Unknown
        return new ChoiceResult(rawInput, "unknown", "low", "no_match");
    }

    private static string LabelKey(string label) =>
        label.Trim().ToLowerInvariant().TrimEnd(TrailingPunctuation);

    private static int DistinctOverlapCount(string text, string label)
    {
        var labelLower = label.ToLowerInvariant();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var seen = new HashSet<string>();
        foreach (var w in words)
        {
            if (w.Length >= 3 && !StopWords.Contains(w) && labelLower.Contains(w))
                seen.Add(w);
        }
        return seen.Count;
    }
}

/// <summary>
/// Result of normalizing a child's choice input.
/// RawInput is always preserved exactly as given.
/// </summary>
public record ChoiceResult(
    string RawInput,
    string Normalized,
    string Confidence,
    string Method);

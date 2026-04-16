namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Conservative Eastern Armenian answer matcher for Riddle Mode v2.
///
/// A 5-year-old's spoken guess often comes with a definite article or a
/// case suffix («կատու» / «կատուն» / «կատվին» / «կատվից»). The matcher
/// strips those endings on both sides and then checks for equality, with
/// a 1-character edit-distance allowance for words ≥ 4 characters.
///
/// No synonym dictionary is used. Lenient stemming + edit distance is
/// enough to catch the common forms; richer matching is the model's job
/// (it owns the celebrate/hint phrasing).
/// </summary>
public static class RiddleAnswerMatcher
{
    public enum Result { Match, Wrong }

    // Common Armenian noun-case endings the matcher peels off, longest first.
    // The list is intentionally short — it covers definite article, genitive,
    // ablative, instrumental, locative, and plural endings that a child
    // commonly produces.
    private static readonly string[] Suffixes =
    [
        "ներին", "ներից", "ների", "ները",
        "ից", "ով", "ում", "ին", "ի",
        "ն", "ը",
    ];

    private static readonly char[] TrailingPunctuation = ['.', ',', '!', '?', ';', ':', '\u0589', '\u055C', '\u055E'];

    public static Result Match(string? guess, string? answer)
    {
        if (string.IsNullOrWhiteSpace(guess) || string.IsNullOrWhiteSpace(answer))
            return Result.Wrong;

        var normGuess = Normalize(guess);
        var normAnswer = Normalize(answer);
        if (normGuess.Length == 0 || normAnswer.Length == 0)
            return Result.Wrong;

        if (normGuess == normAnswer) return Result.Match;

        // Word-by-word: child may say "it's a cat" / «դա կատու է» — check each token
        // against the normalized answer.
        foreach (var token in normGuess.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token == normAnswer) return Result.Match;
            if (normAnswer.Length >= 4 && token.Length >= 4
                && Math.Abs(token.Length - normAnswer.Length) <= 1
                && EditDistanceAtMost1(token, normAnswer))
            {
                return Result.Match;
            }
        }

        // Whole-string edit distance fallback (handles short non-tokenised guesses).
        if (normAnswer.Length >= 4 && normGuess.Length >= 4
            && Math.Abs(normGuess.Length - normAnswer.Length) <= 1
            && EditDistanceAtMost1(normGuess, normAnswer))
        {
            return Result.Match;
        }

        return Result.Wrong;
    }

    /// <summary>
    /// Lowercase, trim trailing punctuation, strip a single Armenian noun-case
    /// suffix (longest match wins). Public for tests.
    /// </summary>
    public static string Normalize(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        t = t.TrimEnd(TrailingPunctuation).Trim();
        // Strip surrounding quotes the model might emit around the answer word.
        t = t.Trim('"', '\'', '«', '»');

        var parts = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = StripSuffix(parts[i].TrimEnd(TrailingPunctuation).Trim('"', '\'', '«', '»'));
        }
        return string.Join(' ', parts);
    }

    private static string StripSuffix(string word)
    {
        // Don't peel suffixes off short words — risk of stripping the whole stem.
        if (word.Length < 4) return word;
        foreach (var s in Suffixes)
        {
            if (word.Length - s.Length >= 3 && word.EndsWith(s, StringComparison.Ordinal))
                return word[..^s.Length];
        }
        return word;
    }

    // Single-substitution / single-insert / single-delete check. Cheaper than
    // full Levenshtein and sufficient for length-1 differences.
    private static bool EditDistanceAtMost1(string a, string b)
    {
        if (a.Length == b.Length)
        {
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i] && ++diff > 1) return false;
            }
            return true;
        }

        // Ensure a is the shorter
        if (a.Length > b.Length) (a, b) = (b, a);
        if (b.Length - a.Length != 1) return false;

        int ai = 0, bi = 0;
        bool skipped = false;
        while (ai < a.Length && bi < b.Length)
        {
            if (a[ai] == b[bi]) { ai++; bi++; continue; }
            if (skipped) return false;
            skipped = true;
            bi++;
        }
        return true;
    }
}

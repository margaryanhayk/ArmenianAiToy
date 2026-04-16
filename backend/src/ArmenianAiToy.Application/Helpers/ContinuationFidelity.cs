using System.Text.RegularExpressions;

namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Checks whether a story continuation visibly references the chosen label.
/// Mirrors the benchmark's continuation_no_label_reference signal: extract
/// all >=4-char Armenian-only tokens from the label, then check whether at
/// least one appears anywhere in the continuation text (case-insensitive).
/// Returns true when the continuation is MISSING the reference (= needs retry).
/// </summary>
public static class ContinuationFidelity
{
    private const int MinTokenLen = 4;

    // Matches a string that is entirely Armenian letters (U+0530..U+058F).
    private static readonly Regex ArmenianOnly = new(
        @"^[\u0530-\u058F]+$", RegexOptions.Compiled);

    // Characters used to split and trim label tokens — matches the benchmark.
    private static readonly char[] Separators =
    [
        ' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':',
        '\u0589', '\u055E', '\u055C', '\u055D', '\u055B',
        '\u00AB', '\u00BB', '"', '(', ')', '\u2014', '-',
    ];

    private static readonly char[] TrailingPunct =
    [
        '.', ',', '!', '?', ';', ':',
        '\u0589', '\u055E', '\u055C', '\u055D',
    ];

    /// <summary>
    /// Returns true when <paramref name="continuation"/> does NOT contain any
    /// >=4-char Armenian token from <paramref name="chosenLabel"/>. Returns false
    /// (no problem) when the label has no extractable tokens or the continuation
    /// references at least one.
    /// </summary>
    public static bool IsMissingReference(string? chosenLabel, string? continuation)
    {
        if (string.IsNullOrWhiteSpace(chosenLabel) || string.IsNullOrWhiteSpace(continuation))
            return false;

        var tokens = ExtractArmenianTokens(chosenLabel);
        if (tokens.Count == 0)
            return false;

        var contLower = continuation.ToLowerInvariant();
        return !tokens.Any(t => contLower.Contains(t));
    }

    private static HashSet<string> ExtractArmenianTokens(string s)
    {
        var set = new HashSet<string>();
        foreach (var raw in s.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = raw.TrimEnd(TrailingPunct);
            if (clean.Length < MinTokenLen) continue;
            if (!ArmenianOnly.IsMatch(clean)) continue;
            set.Add(clean.ToLowerInvariant());
        }
        return set;
    }
}

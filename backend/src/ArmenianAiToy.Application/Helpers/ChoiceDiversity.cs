namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Narrow structural guard for weak CHOICE_A / CHOICE_B pairs.
///
/// Catches the two most common visible failure modes of the E1 prompt rules:
///   1. Same first verb token (e.g. «Բացենք տուփը» / «Բացենք դուռը»).
///   2. Same first verb with a tiny inflectional difference
///      (e.g. «Բացենք…» / «Բացեմ…»).
///
/// Deliberately does NOT attempt semantic synonym detection. The goal is a
/// low-false-positive structural filter; E1's prompt rules handle the rest.
/// </summary>
public static class ChoiceDiversity
{
    // Trailing punctuation to strip from each label before tokenizing.
    // Includes Latin and Armenian marks a model may append.
    private static readonly char[] TrailingPunctuation =
    [
        '.', ',', '!', '?', ';', ':',
        '\u0589', // ։ Armenian full stop (verjaket)
        '\u055E', // ՞ Armenian question mark
        '\u055C', // ՜ Armenian exclamation
        '\u055D', // ՝ Armenian comma
    ];

    /// <summary>
    /// Returns true when <paramref name="a"/> and <paramref name="b"/> are
    /// structurally too similar to present to a child as a meaningful choice.
    /// Conservative — returns false in any ambiguous case.
    /// </summary>
    public static bool AreTooSimilar(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;

        var aTokens = Tokenize(a);
        var bTokens = Tokenize(b);

        if (aTokens.Length < 2 || bTokens.Length < 2)
            return false;

        var firstA = aTokens[0];
        var firstB = bTokens[0];

        // Rule 1: identical first token — same verb, swapped noun pattern.
        if (firstA == firstB)
            return true;

        // Rule 2: first tokens share a ≥4-char prefix with small length
        // difference and neither is itself ≤4 chars. Catches inflectional
        // variants («Բացենք» / «Բացեմ») without flagging short common stems.
        if (firstA.Length > 4 && firstB.Length > 4
            && Math.Abs(firstA.Length - firstB.Length) <= 2
            && SharedPrefixLength(firstA, firstB) >= 4)
        {
            return true;
        }

        return false;
    }

    private static string[] Tokenize(string label)
    {
        var trimmed = label.Trim().TrimEnd(TrailingPunctuation).ToLowerInvariant();
        return trimmed.Split(
            [' ', '\t'],
            StringSplitOptions.RemoveEmptyEntries);
    }

    private static int SharedPrefixLength(string x, string y)
    {
        var max = Math.Min(x.Length, y.Length);
        int i = 0;
        while (i < max && x[i] == y[i]) i++;
        return i;
    }
}

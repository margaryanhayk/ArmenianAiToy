using System.Text;

namespace ArmenianAiToy.Application.Stories;

/// <summary>
/// Deterministic continue-cue check for paused library-story sessions
/// (MODES.md §1A): a normalized match against a FIXED cue list resumes
/// playback with no GPT involvement in routing. «հետո՞» is a pacing
/// cue, not a question — it must never reach the Q&amp;A handler.
/// Matching is whole-utterance: the normalized input must equal a cue
/// exactly, so «հետո ինչ եղավ գորտերի հետ» is a question, not a cue.
/// Pure function; no state, no DI.
/// </summary>
internal static class ContinueCueDetector
{
    /// <summary>Fixed cue vocabulary from MODES.md §1A. Extending it
    /// is a reviewed code change by design.</summary>
    private static readonly string[] Cues =
    [
        "շարունակիր",
        "շարունակի",
        "հետո",
        "հա",
        "էլի",
    ];

    /// <summary>True when the whole utterance, normalized (lowercased,
    /// Armenian intonation marks and terminal punctuation stripped),
    /// equals one of the fixed cues. «Շարունակի՛ր», «հետո՞», «Հա՛»
    /// all match; sentences merely containing a cue do not.</summary>
    public static bool IsContinueCue(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var normalized = Normalize(input);
        return Cues.Contains(normalized, StringComparer.Ordinal);
    }

    private static string Normalize(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (var c in input.Trim().ToLowerInvariant())
        {
            // Armenian intonation marks sit INSIDE words (հետո՞, հա՛);
            // terminal/framing punctuation surrounds the cue. Both are
            // noise for cue identity.
            if (c is '՛' or '՜' or '՞' or '՟' or '։' or '.' or '!' or '?' or ',' or '«' or '»' or '՝' or '…')
            {
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}

/// <summary>Code-owned canned Armenian lines for the library-story
/// flow. Pre-written and reviewed (armenian-story-master) — never
/// generated, never edited at runtime.</summary>
internal static class LibraryStoryCannedLines
{
    /// <summary>Spoken before re-serving the current verbatim segment
    /// when the child gives a continue cue. Reviewed 2026-06-13.</summary>
    public const string ResumeLeadIn = "Ուրեմն, շարունակում ենք հեքիաթը։";
}

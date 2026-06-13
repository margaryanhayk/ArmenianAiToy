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

    /// <summary>Shared cue normalization (also used by
    /// <see cref="StoryStartCueDetector"/>): lowercase, strip Armenian
    /// intonation marks and terminal/framing punctuation, trim.</summary>
    internal static string Normalize(string input)
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

/// <summary>
/// Deterministic story-START cue check for the library engine (W3).
/// While NO library session is active and the engine flag is on, a
/// normalized whole-utterance match against this fixed request list
/// starts an approved library story — code routing, never model
/// judgment, and never GPT. Anything else falls through to the
/// unchanged legacy pipeline (including the legacy free-generation
/// Story mode, which keeps serving all non-matching story requests
/// until the engine flag flips and richer detection lands).
/// </summary>
internal static class StoryStartCueDetector
{
    /// <summary>Fixed start-request vocabulary. Whole-utterance match
    /// after <see cref="ContinueCueDetector.Normalize"/> — extending it
    /// is a reviewed code change by design. English entries exist
    /// because the text-API test/QA surface uses them as canonical
    /// triggers; the voice path is pinned Language=hy.</summary>
    private static readonly string[] Cues =
    [
        "հեքիաթ պատմիր",
        "պատմիր հեքիաթ",
        "մի հեքիաթ պատմիր",
        "արի հեքիաթ",
        "հեքիաթ ուզում եմ",
        "ուզում եմ հեքիաթ",
        // W4: common natural request forms — still whole-utterance,
        // still a fixed list (intonation marks like «կպատմե՞ս» are
        // stripped by normalization, so the question form matches too).
        "հեքիաթ պատմի",
        "հեքիաթ կպատմես",
        "մի հեքիաթ կպատմես",
        "ուզում եմ հեքիաթ լսել",
        "արի մի հեքիաթ լսենք",
        "tell me a story",
    ];

    /// <summary>True when the whole normalized utterance equals one of
    /// the fixed start requests.</summary>
    public static bool IsStoryStartCue(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var normalized = ContinueCueDetector.Normalize(input);
        return Cues.Contains(normalized, StringComparer.Ordinal);
    }
}

/// <summary>
/// Deterministic post-end REPEAT cue check (W4, MODES.md §1A "After
/// the end"). Consulted ONLY when no session is active AND the
/// tracker carries a recently-ended marker — so «էլի» right after a
/// story's ending restarts a story, while the same word with no story
/// context (or mid-story, where it is a continue cue) never reaches
/// this path. Whole-utterance normalized match against a fixed list;
/// no GPT anywhere.
/// </summary>
internal static class RepeatCueDetector
{
    /// <summary>Fixed repeat-request vocabulary (MODES.md §1A names
    /// «էլի», «նորից պատմիր», «էդ նորից»; the short bare forms are
    /// included because the ending turn just asked the reflection
    /// question and a bare «էլի» / «նորից» is the natural child
    /// reply). Extending it is a reviewed code change.</summary>
    private static readonly string[] Cues =
    [
        "էլի",
        "կրկին",
        "նորից",
        "մեկ էլ",
        "նորից պատմիր",
        "էլի պատմիր",
        "էդ նորից",
        "մեկ էլ պատմիր",
        "մեկ ուրիշ",
        "ուրիշ հեքիաթ",
    ];

    /// <summary>True when the whole normalized utterance equals one of
    /// the fixed repeat cues.</summary>
    public static bool IsRepeatCue(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var normalized = ContinueCueDetector.Normalize(input);
        return Cues.Contains(normalized, StringComparer.Ordinal);
    }
}

/// <summary>Code-owned canned Armenian lines for the library-story
/// flow. Pre-written and reviewed (armenian-story-master) — never
/// generated, never edited at runtime.</summary>
internal static class LibraryStoryCannedLines
{
    /// <summary>Spoken before serving the next verbatim segment when
    /// the child gives a continue cue. Reviewed 2026-06-13.</summary>
    public const string ResumeLeadIn = "Ուրեմն, շարունակում ենք հեքիաթը։";

    /// <summary>Spoken before the verbatim first segment when a story
    /// start cue opens a library story. Deliberate minimal pair with
    /// <see cref="ResumeLeadIn"/> (same particle, same frame).
    /// Reviewed 2026-06-13.</summary>
    public const string StartLeadIn = "Ուրեմն, սկսում ենք հեքիաթը։";
}

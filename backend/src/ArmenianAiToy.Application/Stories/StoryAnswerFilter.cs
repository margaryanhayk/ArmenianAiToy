namespace ArmenianAiToy.Application.Stories;

/// <summary>Why a GPT story-question answer was rejected before TTS.
/// <see cref="None"/> means the answer passed every check.</summary>
internal enum StoryAnswerRejection
{
    None,
    Empty,
    TooLong,
    NotArmenian,
    PromptLeakage,
    Insult,
    DeceptionEncouragement,
    ScaryContent,
    UnknownProperNoun,
}

/// <summary>
/// Deterministic post-validation for GPT answers to in-story child
/// questions (MODES.md §1A: length cap, Armenian-only, no structural
/// tokens, no new proper nouns; on failure one retry, then a canned
/// fallback). This filter validates ANSWERS only — it never touches
/// the authored story text, which is served verbatim.
/// <para>
/// Every check is a bounded, code-owned rule: a prompt instruction is
/// a suggestion to a model; this filter is the guarantee. The final
/// text handed to TTS is always either an answer that passed
/// <see cref="Check"/> or <see cref="SafeFallback"/> — nothing else
/// can reach the child (see <see cref="FilterOrFallback"/>).
/// </para>
/// </summary>
internal static class StoryAnswerFilter
{
    /// <summary>Canned Armenian fallback when the answer (and its one
    /// repair retry) fails validation. Pre-written, reviewed, byte-
    /// exact — never generated.</summary>
    public const string SafeFallback =
        "Եկ միասին հիշենք հեքիաթը․ այս մասում պատմությունը հենց այդպես պարզ չի ասում։ Շարունակե՞նք լսել։";

    /// <summary>Hard character cap (1–3 short child sentences fit far
    /// below this).</summary>
    public const int MaxLength = 400;

    /// <summary>Maximum sentence terminators (Armenian «։» or . ! ?).</summary>
    public const int MaxSentences = 3;

    // Bounded vocabularies. Matching is ordinal-contains on the
    // lower-cased answer. Lists are deliberately small and explicit —
    // fail-closed to the fallback is the safe direction, so a rare
    // false positive costs only one canned line.

    /// <summary>Prompt/structural leakage markers. Latin terms cover
    /// system text; «---», CHOICE_A/B, STORY_MEMORY are the legacy
    /// engine's structural tokens that must never reach library Q&A
    /// output; the Armenian terms cover translated leakage.</summary>
    private static readonly string[] LeakageMarkers =
    [
        "system", "prompt", "gpt", "openai", "assistant", "policy",
        "instruction", "language model", "as an ai",
        "---", "choice_a", "choice_b", "story_memory",
        "հրահանգ", "համակարգային", "ֆիլտր",
    ];

    /// <summary>Harsh insults / shaming aimed at a child or character.
    /// Deliberately excludes «անբան» (it is part of the story's own
    /// title/nickname) and bare «ծույլ» (legitimately describes Huri).</summary>
    private static readonly string[] InsultMarkers =
    [
        "հիմար", "տխմար", "ապուշ", "դեբիլ", "անպիտան",
        "ամոթ քեզ", "ամոթ է քեզ", "վատ երեխա ես", "դու վատն ես",
    ];

    /// <summary>Normative/imperative encouragement of deception or
    /// avoidance. Descriptive forms («խաբեց», «հնարք էր») that merely
    /// retell the plot do not match.</summary>
    private static readonly string[] DeceptionMarkers =
    [
        "ստելը լավ է", "խաբելը լավ է", "լավ է ստել", "լավ է խաբել",
        "դու էլ ստիր", "դու էլ խաբիր", "խաբիր", "ստիր",
        "կարելի է ստել", "կարելի է խաբել", "պետք է ստել", "պետք է խաբել",
        "մի աշխատիր", "գործից փախիր",
    ];

    /// <summary>Scary or adult content that has no place in a 4–7
    /// answer about this story.</summary>
    private static readonly string[] ScaryMarkers =
    [
        "սպանե", "արյուն", "մեռավ", "մեռնե", "մահացավ", "դանակ",
        "հրեշ", "սարսափ", "գինի", "օղի", "հարբած",
    ];

    /// <summary>Runs every deterministic check; returns the first
    /// rejection reason or <see cref="StoryAnswerRejection.None"/>.
    /// <paramref name="story"/>/<paramref name="guide"/> supply the
    /// proper-noun grounding corpus; when null that check is skipped
    /// (the other checks still run).</summary>
    public static StoryAnswerRejection Check(
        string? answer, CuratedStory? story = null, StoryQuestionGuide? guide = null)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return StoryAnswerRejection.Empty;
        }

        var trimmed = answer.Trim();
        var lower = trimmed.ToLowerInvariant();

        foreach (var marker in LeakageMarkers)
        {
            if (lower.Contains(marker, StringComparison.Ordinal))
            {
                return StoryAnswerRejection.PromptLeakage;
            }
        }

        // Armenian-only: every letter must be Armenian; digits are
        // forbidden (TTS rule); at least one Armenian letter required.
        var hasArmenian = false;
        foreach (var c in trimmed)
        {
            if (char.IsDigit(c))
            {
                return StoryAnswerRejection.NotArmenian;
            }
            if (char.IsLetter(c))
            {
                if (IsArmenianLetter(c))
                {
                    hasArmenian = true;
                }
                else
                {
                    return StoryAnswerRejection.NotArmenian;
                }
            }
        }
        if (!hasArmenian)
        {
            return StoryAnswerRejection.NotArmenian;
        }

        if (trimmed.Length > MaxLength || CountSentences(trimmed) > MaxSentences)
        {
            return StoryAnswerRejection.TooLong;
        }

        foreach (var marker in InsultMarkers)
        {
            if (lower.Contains(marker, StringComparison.Ordinal))
            {
                return StoryAnswerRejection.Insult;
            }
        }

        foreach (var marker in DeceptionMarkers)
        {
            if (lower.Contains(marker, StringComparison.Ordinal))
            {
                return StoryAnswerRejection.DeceptionEncouragement;
            }
        }

        foreach (var marker in ScaryMarkers)
        {
            if (lower.Contains(marker, StringComparison.Ordinal))
            {
                return StoryAnswerRejection.ScaryContent;
            }
        }

        if (story is not null && HasUnknownProperNoun(trimmed, story, guide))
        {
            return StoryAnswerRejection.UnknownProperNoun;
        }

        return StoryAnswerRejection.None;
    }

    /// <summary>The single seam future wiring hands to TTS: returns
    /// the trimmed answer when it passes every check, otherwise
    /// <see cref="SafeFallback"/>. TTS can never receive unfiltered
    /// model output through this path.</summary>
    public static string FilterOrFallback(
        string? answer, CuratedStory? story = null, StoryQuestionGuide? guide = null) =>
        Check(answer, story, guide) == StoryAnswerRejection.None
            ? answer!.Trim()
            : SafeFallback;

    private static bool IsArmenianLetter(char c) =>
        (c >= 'Ա' && c <= '֏') || (c >= 'ﬓ' && c <= 'ﬗ');

    private static int CountSentences(string text)
    {
        var count = 0;
        foreach (var c in text)
        {
            if (c is '։' or '.' or '!' or '?')
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// MODES.md §1A "no new proper nouns": a capitalized token that is
    /// not sentence-initial must be grounded in the story (title,
    /// segment text, or guide character list). Armenian is inflected,
    /// so grounding uses the token's first four letters as a stem
    /// probe («Հուռիին» matches «Հուռին») — conservative; a false
    /// positive only costs the canned fallback.
    /// </summary>
    private static bool HasUnknownProperNoun(
        string answer, CuratedStory story, StoryQuestionGuide? guide)
    {
        var corpus = new System.Text.StringBuilder();
        corpus.Append(story.Title);
        foreach (var segment in story.Segments)
        {
            corpus.Append('\n').Append(segment.Text);
        }
        if (guide is not null)
        {
            foreach (var character in guide.Characters)
            {
                corpus.Append('\n').Append(character);
            }
        }
        var corpusLower = corpus.ToString().ToLowerInvariant();

        var sentenceStart = true;
        var i = 0;
        while (i < answer.Length)
        {
            var c = answer[i];
            if (char.IsLetter(c))
            {
                var start = i;
                while (i < answer.Length &&
                       (char.IsLetter(answer[i]) || IsInnerWordMark(answer[i])))
                {
                    i++;
                }
                var token = StripInnerMarks(answer[start..i]);
                if (token.Length > 0 && IsArmenianUpper(token[0]) && !sentenceStart)
                {
                    var probeLength = Math.Min(4, token.Length);
                    var probe = token[..probeLength].ToLowerInvariant();
                    if (!corpusLower.Contains(probe, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                sentenceStart = false;
            }
            else
            {
                if (c is '։' or '.' or '!' or '?' or '՜')
                {
                    sentenceStart = true;
                }
                i++;
            }
        }
        return false;
    }

    /// <summary>Armenian intonation marks that sit INSIDE words
    /// (բութ/շեշտ/հարցական/բացականչական).</summary>
    private static bool IsInnerWordMark(char c) =>
        c is '՛' or '՜' or '՞' or '՟';

    private static string StripInnerMarks(string token)
    {
        Span<char> buffer = stackalloc char[token.Length];
        var n = 0;
        foreach (var c in token)
        {
            if (!IsInnerWordMark(c))
            {
                buffer[n++] = c;
            }
        }
        return new string(buffer[..n]);
    }

    private static bool IsArmenianUpper(char c) =>
        c >= 'Ա' && c <= 'Ֆ';
}

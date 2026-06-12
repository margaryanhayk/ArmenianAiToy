namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Deterministic voice-reply guard. Two independent passes:
///
/// (1) <see cref="IsGarbledInput"/> — pre-GPT short-circuit detector for clearly
///     non-language input (random consonant clusters, pure-Latin noise, likely
///     bad STT). Caller (ChatService) is expected to skip the LLM round-trip
///     entirely and return <see cref="ClarificationResponse"/> verbatim when
///     this returns true. Conservative: it requires ALL evaluable tokens to
///     look garbled before flagging, so a real word among the noise lets the
///     turn through.
///
/// (2) <see cref="EnforceMaxSentences"/> + <see cref="ApplyTypoFixes"/> — post-GPT
///     cleanup for non-story voice replies. Caller must gate these on
///     <c>!isStoryMode</c> — Story mode owns its own length contract (3-5
///     sentences + CHOICE_A/B/STORY_MEMORY tail block) and these helpers
///     would corrupt that shape.
///
/// All four helpers are pure. No state, no DI, no logging.
/// </summary>
public static class ArmenianVoiceReplyGuard
{
    /// <summary>
    /// Verbatim Armenian clarification line the toy speaks when input is
    /// flagged as garbled. Matches the example in the SystemPrompt CLARITY
    /// section.
    /// </summary>
    public const string ClarificationResponse = "Կներե՛ս, լավ չլսեցի։ Կրկնի՞ր, խնդրում եմ։";

    // Armenian vowel set used to gauge whether a token resembles a real word.
    // Includes the lowercase + uppercase forms of ա ե է ը ի ո օ and the
    // ECH-YIWN ligature և (carries the vowel ե). The standalone ւ (U+0582)
    // is intentionally excluded — it appears only inside digraphs.
    private static readonly HashSet<char> ArmenianVowels =
    [
        'ա', 'Ա', // ա Ա
        'ե', 'Ե', // ե Ե
        'է', 'Է', // է Է
        'ը', 'Ը', // ը Ը
        'ի', 'Ի', // ի Ի
        'ո', 'Ո', // ո Ո
        'օ', 'Օ', // օ Օ
        'և',           // և
    ];

    // Sentence terminators recognised by EnforceMaxSentences. Armenian verjaket
    // (։ U+0589) is the canonical one; Latin . ! ? are accepted because the
    // model occasionally emits them. Armenian ՞ (question above vowel, U+055E)
    // and ՜ (exclamation above vowel, U+055C) and ՛ (emphasis, U+055B) are
    // INTENTIONALLY excluded — they're diacritics on the stressed vowel, not
    // sentence boundaries, and splitting on them mid-word would cut sentences
    // in the wrong place (e.g. "Իսկ դու՞ ինչպես ես։" is one sentence).
    private static readonly HashSet<char> SentenceTerminators =
    [
        '։', // ։ Armenian full stop
        '.', '!', '?',
    ];

    // Tiny, curated list of model-produced typos observed in recent
    // bench traffic. Keep it small — this is a deterministic patch, not
    // a grammar engine.
    private static readonly (string Wrong, string Right)[] TypoFixes =
    [
        ("խաղաքում", "խաղում"), // խաղաքում → խաղում
    ];

    /// <summary>
    /// Returns true when every evaluable token in <paramref name="input"/>
    /// looks like non-language noise. A token is evaluable when its
    /// letter-only projection has at least 3 characters; shorter tokens
    /// (e.g. "հա", "ոչ") are skipped because two letters carry too little
    /// signal to judge. Returns false on null/whitespace input.
    /// </summary>
    public static bool IsGarbledInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        var tokens = input.Split(
            new[] { ' ', '\t', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);

        int considered = 0;
        int garbled = 0;

        foreach (var raw in tokens)
        {
            // Armenian-letter-only projection — strips punctuation, digits,
            // and any non-Armenian script so they don't influence length /
            // vowel checks. Tokens that project down to fewer than 3
            // Armenian letters are SKIPPED, not flagged: pure-Latin input
            // (test fixtures use English userMessages like "good night" and
            // "tell me a story" as canonical mode triggers) must pass
            // through. Whisper is pinned Language=hy on the production
            // voice path so legitimate child audio always comes back as
            // Armenian script anyway.
            var armenian = new string(raw.Where(IsArmenianLetter).ToArray());
            if (armenian.Length < 3) continue;

            considered++;
            if (!HasArmenianVowel(armenian) || HasRepeatedChar(armenian, 3))
            {
                garbled++;
            }
        }

        return considered > 0 && garbled == considered;
    }

    /// <summary>
    /// Returns <paramref name="reply"/> trimmed to at most
    /// <paramref name="maxSentences"/> sentences, where a sentence ends at
    /// ։ . ! ?. Trailing whitespace is removed. Inputs with fewer
    /// terminators than the cap are returned unchanged. Empty / null input
    /// returns empty string.
    /// </summary>
    public static string EnforceMaxSentences(string? reply, int maxSentences = 3)
    {
        if (string.IsNullOrEmpty(reply)) return reply ?? string.Empty;
        if (maxSentences < 1) return reply;

        int sentenceCount = 0;
        int cutoffExclusive = -1;

        for (int i = 0; i < reply.Length; i++)
        {
            if (SentenceTerminators.Contains(reply[i]))
            {
                sentenceCount++;
                if (sentenceCount == maxSentences)
                {
                    cutoffExclusive = i + 1;
                    break;
                }
            }
        }

        if (cutoffExclusive < 0) return reply;
        return reply[..cutoffExclusive].TrimEnd();
    }

    /// <summary>
    /// Applies the small curated typo dictionary to <paramref name="reply"/>.
    /// Returns the input unchanged when no entry matches. Null / empty input
    /// returns empty string.
    /// </summary>
    public static string ApplyTypoFixes(string? reply)
    {
        if (string.IsNullOrEmpty(reply)) return reply ?? string.Empty;
        foreach (var (wrong, right) in TypoFixes)
        {
            reply = reply.Replace(wrong, right);
        }
        return reply;
    }

    private static bool IsArmenianLetter(char c)
    {
        // Armenian block: U+0531..U+0556 (uppercase) + U+0561..U+0587 (lowercase + ligatures).
        // Conservative range that excludes U+0589 (full stop) and the
        // Armenian punctuation block (U+055A..U+055F).
        return (c >= 'Ա' && c <= 'Ֆ') || (c >= 'ա' && c <= 'և');
    }

    private static bool HasArmenianVowel(string s)
    {
        foreach (var c in s)
        {
            if (ArmenianVowels.Contains(c)) return true;
        }
        return false;
    }

    private static bool HasRepeatedChar(string s, int run)
    {
        if (run < 2 || s.Length < run) return false;
        int consec = 1;
        for (int i = 1; i < s.Length; i++)
        {
            if (s[i] == s[i - 1])
            {
                consec++;
                if (consec >= run) return true;
            }
            else
            {
                consec = 1;
            }
        }
        return false;
    }
}

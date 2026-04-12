namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// The five product modes Areg can be in. See .claude/MODES.md for the
/// canonical specification of tone, allowed/forbidden behavior, and
/// transition rules per mode.
/// </summary>
public enum DetectedMode
{
    /// <summary>No mode signal detected. Caller decides the default.</summary>
    None,
    Story,
    Game,
    Riddle,
    /// <summary>One-turn overlay: a real off-topic question from the child.</summary>
    Curiosity,
    /// <summary>Calm / bedtime — terminal mode for the session.</summary>
    Calm,
}

/// <summary>
/// Pure-function detection of the conversation mode from a user message and
/// recent history. This is additive infrastructure — it is NOT yet wired into
/// <c>ChatService</c>. Wiring is gated on human approval per ROADMAP.md Phase 4.
///
/// Priority (highest first), per .claude/MODES.md:
///   1. Calm cues in the current message (safety + parent trust).
///   2. Curiosity Window — a real off-topic question.
///   3. Active story session continuation (when <c>hasActiveStorySession</c>).
///   4. Explicit mode trigger in the current message (story / game / riddle).
///   5. Mode trigger in the last 2 user messages of history.
///   6. <see cref="DetectedMode.None"/> — no signal.
///
/// Story-cue gating: a message that contains a story trigger word
/// (e.g. "tell me a story about sleeping") is NEVER classified as Calm or
/// Curiosity. This prevents topic words from hijacking mode detection.
/// </summary>
public static class ModeDetector
{
    private static readonly string[] StoryTriggers =
    [
        "tell me a story",
        "tell a story",
        "what happens next",
        " story",                                                 // leading space avoids "history"
        "\u057a\u0561\u057f\u0574\u056b\u0580",                   // patmir (Armenian)
        "\u057a\u0561\u057f\u0574\u0578\u0582\u0569\u0575\u0578\u0582\u0576", // patmutyun
        "\u0570\u0565\u0584\u056b\u0561\u0569",                   // heqiat
        "\u056b\u0576\u0579 \u056f\u056c\u056b\u0576\u056b",      // inch klini
        "patmir",
        "patmutyun",
        "heqiat",
        "hekiat",
        "heto",
    ];

    private static readonly string[] GameTriggers =
    [
        "let's play",
        "lets play",
        "play a game",
        "play with me",
        "let's play a game",
        "\u056d\u0561\u0572\u0561\u0576\u0584",                   // խաղանք (let's play)
        "\u056d\u0561\u0572\u0561\u056c",                          // խաղալ (to play)
        "\u056d\u0561\u0572 \u056f\u0561",                         // խաղ կա (there is a game)
        "khaghank",
        "khaghal",
        "khagha",
    ];

    private static readonly string[] RiddleTriggers =
    [
        "riddle",
        "give me a riddle",
        "ask me a riddle",
        "guess what",
        "\u0570\u0561\u0576\u0565\u056c\u0578\u0582\u056f",       // հանելուկ (riddle)
        "haneluk",
    ];

    // Calm triggers must be first-person tiredness or explicit bedtime cues.
    // The story-cue gate prevents these from firing on "story about sleeping".
    private static readonly string[] CalmTriggers =
    [
        "i'm tired",
        "im tired",
        "i am tired",
        "i'm sleepy",
        "im sleepy",
        "i am sleepy",
        "good night",
        "goodnight",
        "bedtime",
        "time for bed",
        "go to sleep",
        "sleep now",
        "kpnem",
        "knem",
        "\u0584\u0576\u0565\u056c",                                // քնել (sleep)
        "\u0570\u0578\u0563\u0576\u0561\u056e",                    // հոգնած (tired)
        "\u0563\u056b\u0577\u0565\u0580 \u0562\u0561\u0580\u056b", // գիշեր բարի (good night)
        "\u0576\u0576\u057b\u0565\u056c",                          // ննջել (to sleep / rest)
    ];

    // Curiosity starters open a real off-topic question. These are matched as
    // word-leading tokens (start of message or after a space) so we don't
    // false-trigger on substrings inside other words.
    private static readonly string[] CuriosityStarters =
    [
        "why",
        "how come",
        "what is",
        "what's",
        "what are",
        "where is",
        "where are",
        "when is",
        "when does",
        "\u056b\u0576\u0579\u0578\u0582",                          // ինչու (why)
        "\u056b\u0576\u0579\u057a\u0565\u057d",                    // ինչպես (how)
    ];

    /// <summary>
    /// Detect the current mode from the user message and recent history.
    /// </summary>
    /// <param name="userMessage">The current child input.</param>
    /// <param name="history">Conversation history, oldest first. Items use ("user"/"assistant", content).</param>
    /// <param name="hasActiveStorySession">True if the conversation has a pending story choice (active story mode).</param>
    public static DetectedMode Detect(
        string? userMessage,
        IReadOnlyList<(string Role, string Content)>? history,
        bool hasActiveStorySession = false)
    {
        var lower = (userMessage ?? string.Empty).ToLowerInvariant();
        history ??= [];

        bool hasStoryCue = ContainsAny(lower, StoryTriggers);

        // Priority 1: Calm — only if NOT also a story topic.
        if (!hasStoryCue && ContainsAny(lower, CalmTriggers))
            return DetectedMode.Calm;

        // Priority 2: Curiosity Window — real off-topic question.
        // Excluded if message contains a story cue (e.g. "what happens next" is story).
        if (!hasStoryCue && StartsWithWord(lower, CuriosityStarters))
            return DetectedMode.Curiosity;

        // Priority 3: Active story session continues.
        if (hasActiveStorySession)
            return DetectedMode.Story;

        // Priority 4: Explicit triggers in the current message.
        if (hasStoryCue) return DetectedMode.Story;
        if (ContainsAny(lower, GameTriggers)) return DetectedMode.Game;
        if (ContainsAny(lower, RiddleTriggers)) return DetectedMode.Riddle;

        // Priority 5: Mode trigger in the last 2 user messages.
        int checkedCount = 0;
        for (int i = history.Count - 1; i >= 0 && checkedCount < 2; i--)
        {
            if (!string.Equals(history[i].Role, "user", StringComparison.OrdinalIgnoreCase))
                continue;

            var hLower = history[i].Content.ToLowerInvariant();
            if (ContainsAny(hLower, StoryTriggers)) return DetectedMode.Story;
            if (ContainsAny(hLower, GameTriggers)) return DetectedMode.Game;
            if (ContainsAny(hLower, RiddleTriggers)) return DetectedMode.Riddle;
            checkedCount++;
        }

        return DetectedMode.None;
    }

    private static bool ContainsAny(string lower, string[] needles)
    {
        for (int i = 0; i < needles.Length; i++)
            if (lower.Contains(needles[i])) return true;
        return false;
    }

    /// <summary>
    /// True if any needle appears at the start of the message or immediately
    /// after a space. Prevents matching inside larger words (e.g. "whyever"
    /// should not match "why").
    /// </summary>
    private static bool StartsWithWord(string lower, string[] needles)
    {
        for (int i = 0; i < needles.Length; i++)
        {
            var n = needles[i];
            if (lower.StartsWith(n + " ", StringComparison.Ordinal)) return true;
            if (lower == n) return true;
            if (lower.StartsWith(n + "?", StringComparison.Ordinal)) return true;
            if (lower.Contains(" " + n + " ")) return true;
            if (lower.Contains(" " + n + "?")) return true;
        }
        return false;
    }
}

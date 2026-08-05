namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Sub-intent within Game Mode v2. Once ChatService routes a turn to Game
/// mode, this classifier picks one of four sub-states from the child's
/// message and whether a game round is currently in flight. ChatService
/// uses the result to inject the right turn-kind directive into the
/// system prompt.
/// </summary>
public enum GameIntent
{
    /// <summary>Open a fresh activity (start of session, first opener).</summary>
    StartNew,
    /// <summary>Child wants a different game — must avoid recent types.</summary>
    SwitchGame,
    /// <summary>Child wants to stop playing — warm goodbye, clear state.</summary>
    Stop,
    /// <summary>Child responded inside the active activity — celebrate + next.</summary>
    Continue,
}

public static class GameIntentDetector
{
    private static readonly string[] SwitchTriggers =
    [
        "another game", "different game", "new game", "switch game",
        "lets switch", "let's switch", "play something else",
        "give me another game",
        "ուրիշ խաղ", "նոր խաղ", "ուրիշ բան խաղանք", "ուրիշ խաղ խաղանք",
        "այլ խաղ",
        "urish khagh", "nor khagh",
    ];

    // «վերջ» is deliberately NOT in this list — it is matched as a whole
    // word below, because as a substring it fires inside «վերջին» /
    // «վերջապես» («one last time» must not stop the game).
    private static readonly string[] StopTriggers =
    [
        "stop", "stop playing", "i'm done", "im done", "i am done",
        "no more", "that's enough", "thats enough", "enough",
        "i don't want to play", "i dont want to play",
        "բավ է", "բավական է", "չեմ ուզում", "չեմ ուզում խաղալ",
        "այլևս չեմ ուզում", "հոգնեցի", "հերիք է",
        "bav e", "verj", "chem uzum",
    ];

    private static readonly string[] PlayTriggers =
    [
        "let's play", "lets play", "play with me", "play a game",
        "let's play a game", "lets play a game",
        "խաղանք", "խաղալ", "խաղ կա",
        "khaghank", "khaghal", "khagha",
    ];

    /// <summary>
    /// A switch phrase inside a refusal is a refusal: «չեմ ուզում ուրիշ
    /// խաղ» ("I don't want another game") must never be read as a request
    /// for another game. Mirrors WelcomeIntentDetector's negation-first
    /// contract.
    /// </summary>
    private static readonly string[] NegationMarkers =
    [
        "չեմ", "չենք",
        "don't", "dont", "do not",
    ];

    public static GameIntent Detect(string? userMessage, bool hasActiveRound)
    {
        var lower = NormalizeForMatch(userMessage);

        bool hasSwitch = ContainsAny(lower, SwitchTriggers);
        bool hasNegation = ContainsAny(lower, NegationMarkers);
        bool hasStop = ContainsAny(lower, StopTriggers)
            || ContainsWholeWord(lower, "վերջ");

        // Switch wins even mid-round — child wants something different.
        // Unless the switch phrase sits inside a negation, in which case
        // the child is declining, not requesting — that is a Stop.
        if (hasSwitch) return hasNegation ? GameIntent.Stop : GameIntent.SwitchGame;

        // Stop works with or without an active round. A child saying
        // "enough" or "I don't want to play" must get a warm goodbye —
        // never a new game started at them. (Pre-fix behavior gated Stop
        // on hasActiveRound, which made every no-round stop a StartNew.)
        if (hasStop) return GameIntent.Stop;

        // An explicit "let's play" with no active round opens a fresh game.
        // With an active round it's just engagement — let Continue handle it.
        if (!hasActiveRound && ContainsAny(lower, PlayTriggers))
            return GameIntent.StartNew;

        // With an active round, anything else is the child's response inside
        // the current activity. Without an active round, the loop is opening
        // and we treat the message as a request to start.
        return hasActiveRound ? GameIntent.Continue : GameIntent.StartNew;
    }

    /// <summary>
    /// Lowercase + trim + strip the Armenian intra-word marks ՞ ՛ ՜.
    /// Armenian writes emphasis and question marks INSIDE the word
    /// («Բա՛վ է», «Վե՛րջ»), so without this the most natural emphatic
    /// "enough!" never matches a plain trigger. Deliberately duplicated
    /// from ModeDetector's private normalizer rather than widening that
    /// HIGH-risk file's surface — same decision WelcomeIntentDetector
    /// documents. Keep the three marks in step across all three copies.
    /// </summary>
    private static string NormalizeForMatch(string? message)
    {
        var lower = (message ?? string.Empty).Trim().ToLowerInvariant();
        if (lower.IndexOf('՞') < 0
            && lower.IndexOf('՛') < 0
            && lower.IndexOf('՜') < 0)
        {
            return lower;
        }
        return lower
            .Replace("՞", string.Empty)
            .Replace("՛", string.Empty)
            .Replace("՜", string.Empty);
    }

    private static bool ContainsAny(string lower, string[] needles)
    {
        for (int i = 0; i < needles.Length; i++)
        {
            if (lower.Contains(needles[i])) return true;
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="word"/> occurs bounded by non-letters
    /// (or string edges). Used for triggers that are substrings of common
    /// unrelated words.
    /// </summary>
    private static bool ContainsWholeWord(string lower, string word)
    {
        int idx = 0;
        while ((idx = lower.IndexOf(word, idx, StringComparison.Ordinal)) >= 0)
        {
            bool startOk = idx == 0 || !char.IsLetter(lower[idx - 1]);
            int end = idx + word.Length;
            bool endOk = end >= lower.Length || !char.IsLetter(lower[end]);
            if (startOk && endOk) return true;
            idx = idx + 1;
        }
        return false;
    }
}

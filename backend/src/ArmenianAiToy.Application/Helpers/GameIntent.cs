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

    private static readonly string[] StopTriggers =
    [
        "stop", "stop playing", "i'm done", "im done", "i am done",
        "no more", "that's enough", "thats enough", "enough",
        "i don't want to play", "i dont want to play",
        "բավ է", "բավական է", "վերջ", "չեմ ուզում", "չեմ ուզում խաղալ",
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

    public static GameIntent Detect(string? userMessage, bool hasActiveRound)
    {
        var lower = (userMessage ?? string.Empty).Trim().ToLowerInvariant();

        // Switch wins even mid-round — child wants something different.
        if (ContainsAny(lower, SwitchTriggers)) return GameIntent.SwitchGame;

        // Stop wins mid-round only — without an active round there is
        // nothing to stop.
        if (hasActiveRound && ContainsAny(lower, StopTriggers))
            return GameIntent.Stop;

        // An explicit "let's play" with no active round opens a fresh game.
        // With an active round it's just engagement — let Continue handle it.
        if (!hasActiveRound && ContainsAny(lower, PlayTriggers))
            return GameIntent.StartNew;

        // With an active round, anything else is the child's response inside
        // the current activity. Without an active round, the loop is opening
        // and we treat the message as a request to start.
        return hasActiveRound ? GameIntent.Continue : GameIntent.StartNew;
    }

    private static bool ContainsAny(string lower, string[] needles)
    {
        for (int i = 0; i < needles.Length; i++)
        {
            if (lower.Contains(needles[i])) return true;
        }
        return false;
    }
}

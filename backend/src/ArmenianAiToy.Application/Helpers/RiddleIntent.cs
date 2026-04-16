namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Sub-intent within Riddle Mode v2. Once ChatService has decided we're
/// in Riddle mode, this classifier picks one of four sub-states based
/// on the child's message and whether a riddle round is currently in
/// flight. ChatService uses the result to inject the right turn-kind
/// directive into the system prompt.
/// </summary>
public enum RiddleIntent
{
    /// <summary>Pose a fresh riddle (start of session, or "another one").</summary>
    StartNew,
    /// <summary>Child gave up — reveal the answer warmly.</summary>
    GiveUp,
    /// <summary>Child made a guess — ChatService runs the matcher next.</summary>
    Guess,
}

public static class RiddleIntentDetector
{
    private static readonly string[] StartNewTriggers =
    [
        "another", "another one", "next", "next one", "again", "more",
        "one more", "new riddle", "give me another", "give me a new",
        "ևս մեկ", "ևս մեկ հանելուկ",
        "նորից", "մի ուրիշ", "ուրիշ", "ուրիշ հանելուկ",
        "տուր էլի", "էլի մեկ", "էլի",
        "հաջորդը", "հաջորդ հանելուկ",
        "ուզում եմ ևս մեկ", "ուզում եմ ուրիշ",
        // Romanised forms.
        "el meky", "evs mek", "urish", "haneluk tur",
    ];

    private static readonly string[] GiveUpTriggers =
    [
        "i don't know", "i dont know", "i give up", "give up",
        "no idea", "tell me", "tell me the answer", "what is it",
        "what's the answer",
        "չգիտեմ", "չեմ իմանում", "չեմ կարող",
        "ասա", "ասա պատասխանը", "ասա ինչ է",
        "հանձնվում եմ", "հանձնվեցի",
        "ինչ է պատասխանը",
        // Romanised.
        "chgitem", "asa patasxan",
    ];

    private static readonly string[] RiddleWords =
    [
        "riddle", "haneluk",
        "հանելուկ", // հանելուկ stem catches inflections
    ];

    public static RiddleIntent Detect(string? userMessage, bool hasActiveRound)
    {
        var lower = (userMessage ?? string.Empty).Trim().ToLowerInvariant();

        // Explicit riddle trigger words ALWAYS mean "new riddle" — a child
        // never guesses with the word «հանելուկ» / "riddle" / "haneluk".
        // Also keeps ModeBenchmark's back-to-back riddle prompts on one
        // conversation each producing a fresh NEW_RIDDLE turn.
        if (ContainsAny(lower, RiddleWords)) return RiddleIntent.StartNew;
        if (ContainsAny(lower, StartNewTriggers)) return RiddleIntent.StartNew;

        if (hasActiveRound && ContainsAny(lower, GiveUpTriggers))
            return RiddleIntent.GiveUp;

        // Without an active round, an unrecognised message is still a request
        // to start (the loop is opening). With an active round, it's a guess.
        return hasActiveRound ? RiddleIntent.Guess : RiddleIntent.StartNew;
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

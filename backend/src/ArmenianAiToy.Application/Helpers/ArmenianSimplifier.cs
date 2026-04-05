namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Deterministic post-generation word replacement for Armenian story text.
/// Replaces formal/bookish Armenian words with simpler child-friendly alternatives.
/// Applied after TailBlockParser so parsers are not affected.
/// </summary>
public static class ArmenianSimplifier
{
    // Formal word → simple child-friendly replacement.
    // IMPORTANT: Longer keys must come before shorter keys that are substrings.
    // E.g. "delays delays delays" before "delays delays" to avoid partial replacement.
    private static readonly (string Formal, string Simple)[] Replacements =
    [
        // Formal verbs → simple verbs
        ("\u0578\u0582\u057d\u0578\u0582\u0574\u0576\u0561\u057d\u056b\u0580\u0565\u0576\u0584", "\u0563\u0576\u0561\u0576\u0584"),           // ուdelays delays → delays (explore → go)
        ("\u0578\u0582\u057d\u0578\u0582\u0574\u0576\u0561\u057d\u056b\u0580\u0565\u056c", "\u0576\u0561\u0575\u0565\u056c"),             // delays delays → delays (study → look)
        ("\u0570\u0565\u057f\u0561\u0566\u0578\u057f\u0565\u0576\u0584", "\u0583\u0576\u057f\u0580\u0565\u0576\u0584"),               // delays delays delays → delays (investigate → search)
        ("\u0570\u0565\u057f\u0561\u0566\u0578\u057f\u0565\u056c", "\u0583\u0576\u057f\u0580\u0565\u056c"),                 // delays delays → delays (investigate → search)
        ("\u0576\u056f\u0561\u057f\u0565\u0581", "\u057f\u0565\u057d\u0561\u057e"),                     // նdelays → delays (noticed → saw)
        ("\u0570\u0561\u0575\u057f\u0576\u0561\u0562\u0565\u0580\u0565\u0581\u056b\u0576", "\u0563\u057f\u0561\u0576"),             // delays delays delays → delays (discovered → found) [3rd person pl] — MUST be before singular form
        ("\u0570\u0561\u0575\u057f\u0576\u0561\u0562\u0565\u0580\u0565\u0581", "\u0563\u057f\u0561\u057e"),               // delays delays → delays (discovered → found) [3rd person sg]
        ("\u0562\u0561\u0581\u0561\u0570\u0561\u0575\u057f\u0565\u0581", "\u0563\u057f\u0561\u057e"),               // delays delays → delays (revealed → found)
        ("\u0562\u0561\u0581\u0561\u0570\u0561\u0575\u057f\u0565\u056c", "\u0563\u057f\u0576\u0565\u056c"),               // delays delays → delays (to reveal → to find)
        ("\u0562\u0561\u0581\u0561\u0570\u0561\u0575\u057f\u0565\u0576\u0584", "\u0563\u057f\u0576\u0565\u0576\u0584"),             // delays delays delays → delays (let's discover → let's find)
        ("\u0564\u056b\u057f\u0578\u0580\u0564\u0565\u0581", "\u0576\u0561\u0575\u0565\u0581"),                 // delays → delays (observed → looked)

        // Literary adjectives → simple adjectives
        ("\u057a\u057d\u057a\u0572\u0561\u0581\u0578\u0572", "\u0583\u0561\u0575\u056c\u0578\u0582\u0576"),                 // delays delays → delays (sparkling → shiny)

        // Formal nouns/phrases → simple versions
        ("\u0570\u0561\u0580\u0581\u0561\u057d\u056b\u0580\u0578\u0582\u0569\u0575\u0561\u0574\u0562", "\u0570\u0565\u057f\u0561\u0584\u0580\u0584\u0580\u057e\u0565\u056c\u0578\u057e"),   // delays delays delays → delays delays (with curiosity → curiously)
        ("\u056d\u0576\u0561\u0574\u0561\u0580\u056f\u057e\u0561\u056e", "\u0563\u0565\u0572\u0565\u0581\u056b\u056f"),               // delays delays → delays (cultivated → beautiful)
        ("\u057e\u0565\u0580\u0561\u056f\u0561\u0576\u0563\u0576\u0565\u056c", "\u0576\u0578\u0580\u056b\u0581 \u057d\u056f\u057d\u0565\u056c"),         // delays delays → delays delays (restore → start again)
        ("\u0561\u057c\u0561\u057b\u0576\u0578\u0580\u0564\u0578\u0582\u0574", "\u0561\u057c\u0561\u057b \u0563\u0576\u0561\u056c\u0578\u057e"),       // delays → delays delays (leading → going ahead)
        // Uppercase variants for choice-initial and sentence-initial position
        ("\u0548\u0582\u057d\u0578\u0582\u0574\u0576\u0561\u057d\u056b\u0580\u0565\u0576\u0584", "\u0533\u0576\u0561\u0576\u0584"),
        ("\u0548\u0582\u057d\u0578\u0582\u0574\u0576\u0561\u057d\u056b\u0580\u0565\u056c", "\u0546\u0561\u0575\u0565\u056c"),
        ("\u0540\u0565\u057f\u0561\u0566\u0578\u057f\u0565\u0576\u0584", "\u0553\u0576\u057f\u0580\u0565\u0576\u0584"),
        ("\u0540\u0565\u057f\u0561\u0566\u0578\u057f\u0565\u056c", "\u0553\u0576\u057f\u0580\u0565\u056c"),
        ("\u0540\u0565\u057f\u0561\u0584\u0576\u0576\u0565\u0576\u0584", "\u0553\u0576\u057f\u0580\u0565\u0576\u0584"),
        ("\u0532\u0561\u0581\u0561\u0570\u0561\u0575\u057f\u0565\u056c", "\u0533\u057f\u0576\u0565\u056c"),
        ("\u0540\u0561\u0575\u057f\u0576\u0561\u0562\u0565\u0580\u0565\u0581\u056b\u0576", "\u0533\u057f\u0561\u0576"),
        ("\u0540\u0561\u0575\u057f\u0576\u0561\u0562\u0565\u0580\u0565\u0581", "\u0533\u057f\u0561\u057e"),
        ("\u0532\u0561\u0581\u0561\u0570\u0561\u0575\u057f\u0565\u0576\u0584", "\u0533\u057f\u0576\u0565\u0576\u0584"),
        ("\u0532\u0561\u0581\u0561\u0570\u0561\u0575\u057f\u0565\u0581", "\u0533\u057f\u0561\u057e"),
        ("\u0546\u056f\u0561\u057f\u0565\u0581", "\u054f\u0565\u057d\u0561\u057e"),
        ("\u054a\u057d\u057a\u0572\u0561\u0581\u0578\u0572", "\u0553\u0561\u0575\u056c\u0578\u0582\u0576"),
    ];

    /// <summary>
    /// Applies all formal→simple word replacements to the given text.
    /// Safe to call on null/empty strings (returns input unchanged).
    /// </summary>
    public static string Simplify(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

        foreach (var (formal, simple) in Replacements)
        {
            text = text.Replace(formal, simple);
        }

        return text;
    }
}

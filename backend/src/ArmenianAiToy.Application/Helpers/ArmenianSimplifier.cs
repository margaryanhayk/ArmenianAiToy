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

        // Targeted phrase fixes from latest benchmark observations.
        // Each entry is a literal substring replacement — no regex, bounded
        // and order-independent.

        // " Եվ այսքանը:" / " Եվ այսքանը։" → "" — filler wrap-up phrase.
        // Leading space included so the deletion does not leave a double space.
        (" Եվ այսքանը:", ""),
        (" Եվ այսքանը։", ""),

        // "մեծական ականջները" → "մեծ ականջները" — "մեծական" isn't a real word.
        ("մեծական ականջները", "մեծ ականջները"),

        // " անվերապահորեն" → "" — adult/legalistic, redundant in story context.
        (" անվերապահորեն", ""),

        // "ծռվել ու բավարարվել" → "ծռվել" — drop nonsensical second verb.
        ("ծռվել ու բավարարվել", "ծռվել"),

        // "թռչյուն" → "թռչուն" — wrong stem; this literal also catches every
        // inflected form (թռչյունները, թռչյուններին, …) without regex.
        ("թռչյուն", "թռչուն"),
        ("Թռչյուն", "Թռչուն"),

        // "քաղցրածին ժպտաց" → "քաղցր ժպտաց" — invented compound adjective.
        ("քաղցրածին ժպտաց", "քաղցր ժպտաց"),

        // "դրախտյան այգում" → "գեղեցիկ այգում" — too literary/religious.
        ("դրախտյան այգում", "գեղեցիկ այգում"),

        // "քայլեցնելով փորձեց ուսուցանել" → "փորձեց սովորեցնել" — drop bookish
        // gerund and replace formal verb.
        ("քայլեցնելով փորձեց ուսուցանել", "փորձեց սովորեցնել"),

        // "լողափնյա ճյուղերի վրա" → "ճյուղերի վրա" — drop incoherent adjective.
        ("լողափնյա ճյուղերի վրա", "ճյուղերի վրա"),

        // "կային սպասված նրանց" → "սպասում էին նրանց" — broken syntax fix.
        ("կային սպասված նրանց", "սպասում էին նրանց"),
    

        // Phase A word-level quality fixes (evidence from live QA).

        // "մթնոլորտ" → "օդ" — scientific "atmosphere" → child-natural "air".
        ("մթնոլորտ", "օդ"),
        ("Մթնոլորտ", "Օդ"),

        // "կուտակվում/կուտակվել" → "հավաքվում/հավաքվել" — formal "accumulate" → simple "gather".
        ("կուտակվում", "հավաքվում"),
        ("կուտակվել", "հավաքվել"),
        ("Կուտակվում", "Հավաքվում"),

        // "ճամփորդել" → "գնալ" — formal "to travel" → simple "to go".
        ("ճամփորդել", "գնալ"),
        ("ճամփորդություն", "ճամփա"),
        ("Ճամփորդել", "Գնալ"),
        ("Ճամփորդություն", "Ճամփա"),

        // "հրավիրեց/հրավիրել" → "կանչեց/կանչել" — formal "invited" → child-natural "called".
        ("հրավիրեց", "կանչեց"),
        ("հրավիրել", "կանչել"),
        ("Հրավիրեց", "Կանչեց"),

        // "ցուցարկում" → "ցույց տալիս" — formal "exhibiting" → simple "showing".
        ("ցուցարկում", "ցույց տալիս"),
        ("ցուցարկել", "ցույց տալ"),
        ("Ցուցարկում", "Ցույց տալիս"),

        // "այս նպատակի համար" → "դրա համար" — formal phrase "for this purpose" → simple "for that".
        ("այս նպատակի համար", "դրա համար"),


        // Phase A follow-up word-level fixes (second-round QA on phase-a-polish).

        // "հմայիչ" → "գեղեցիկ" — adult aesthetic "charming" → child word "beautiful".
        ("հմայիչ", "գեղեցիկ"),
        ("Հմայիչ", "Գեղեցիկ"),

        // "փորձարկել" → "փորձել" — scientific "to experiment" → everyday "to try".
        ("փորձարկել", "փորձել"),
        ("փորձարկում", "փորձում"),
        ("փորձարկեց", "փորձեց"),
        ("Փորձարկել", "Փորձել"),

        // "վայրէջք կատարեց" → "իջավ" — aviation term "performed a landing" → natural "came down".
        ("վայրէջք կատարեց", "իջավ"),
        ("վայրէջք կատարել", "իջնել"),
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

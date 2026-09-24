namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Deterministic first-person self-harm / not-wanting-to-live detector, run
/// BEFORE moderation and the model. Found live on 2026-09-23
/// (<c>tools/quality-evidence/armenian-safety-live-20260923.md</c>): a
/// transliterated «chem uzum aprel» passed moderation (self-harm 0.004) and
/// the model answered it with a cheerful «Արի խաղանք», unflagged.
///
/// <para>
/// High precision on purpose: first-person phrases only (Armenian script,
/// the Latin transliterations Armenian speakers type, English), never bare
/// stems like «մեռն-» that fairy-tale narration uses («վիշապը մեռավ»).
/// Matched as a case-insensitive substring after whitespace is collapsed.
/// Supplements moderation — never replaces it.
/// </para>
/// </summary>
public static class SelfHarmSignal
{
    /// <summary>
    /// What the toy says for ANY self-harm block (this detector or the
    /// moderation <c>self-harm</c> category): calm, sends the child to a
    /// grown-up now, passes the Absence Test (no "I", no promise the toy
    /// cannot keep). Reviewed by armenian-story-master 2026-09-24.
    /// </summary>
    public const string Response =
        "Այն, ինչ ասացիր, շատ կարևոր է։ Հենց հիմա գնա մայրիկի, հայրիկի կամ մի մեծի մոտ, ում վստահում ես, և ասա նրան։ Նրանք քեզ անպայման կօգնեն։";

    // Reviewed by armenian-story-master 2026-09-24: only entries it marked
    // [keep]. Deliberately absent (risky): bare «մեռնեմ» (the affection
    // idiom «մեռնեմ ջանիդ»), «ինձ սպանել» («գայլն ուզում է ինձ սպանել»),
    // «չեմ ուզում լինել» («…լինել վիշապ»), "kill me", bare "want to die".
    internal static readonly string[] Phrases =
    [
        // ── Eastern Armenian script ──
        "չեմ ուզում ապրել",
        "չեմ ուզում էլ ապրել",
        "չեմ ուզում ապրեմ",
        "ապրել չեմ ուզում",
        "ուզում եմ մեռնել",
        "մեռնել եմ ուզում",
        "ուզում եմ մահանալ",
        "ավելի լավ է մեռնեմ",
        "ինքս ինձ սպանեմ",
        "ինքս ինձ վնասեմ",
        "ինձ վնաս տամ",

        // ── Latin transliteration ──
        "chem uzum aprel",
        "chem uzum el aprel",
        "chem uzum aprem",
        "aprel chem uzum",
        "uzum em mernel",
        "mernel em uzum",
        "uzum em mahanal",
        "aveli lav e mernem",
        "inqs indz spanem",
        "inqs indz vnasem",
        "indz vnas tam",

        // ── English ──
        "i don't want to live",
        "i dont want to live",
        "i do not want to live",
        "i want to die",
        "i wanna die",
        "kill myself",
        "hurt myself",
        "end my life",
    ];

    // Armenian emphasis/question/exclamation marks a model or STT can put
    // inside a word («ապրե՛լ»); stripped before matching.
    private static readonly char[] ArmenianMarks = ['\u055B', '\u055C', '\u055E'];

    /// <summary>True when the input contains a first-person self-harm phrase.</summary>
    public static bool IsPresent(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;

        var lowered = input.ToLowerInvariant().Replace('’', '\'');
        foreach (var mark in ArmenianMarks)
            lowered = lowered.Replace(mark.ToString(), string.Empty);
        var normalized = string.Join(' ',
            lowered.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        foreach (var phrase in Phrases)
        {
            if (normalized.Contains(phrase, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}

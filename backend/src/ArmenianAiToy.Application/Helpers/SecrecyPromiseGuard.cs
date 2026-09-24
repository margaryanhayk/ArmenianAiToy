namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Deterministic post-check on model replies: the toy must never promise a
/// child to keep a secret from their parents. Found live on 2026-09-23
/// (<c>tools/quality-evidence/armenian-safety-live-20260923.md</c>): "do not
/// tell my parents" got «Քո գաղտնիությունը միշտ ապահով է ինձ հետ» and «Մեր
/// զրույցը պահվում է միայն այստեղ» — false (parents see every transcript)
/// and the secrecy pattern child-safety guidance forbids. The system prompt
/// now says so too; this guard makes it hold regardless of the model.
/// </summary>
public static class SecrecyPromiseGuard
{
    /// <summary>
    /// The honest line spoken instead. Reviewed by armenian-story-master
    /// 2026-09-24; a fact about the toy, so it passes the Absence Test.
    /// </summary>
    public const string HonestResponse =
        "Ես մայրիկից ու հայրիկից գաղտնիք չեմ պահում։ Նրանք կարող են տեսնել, թե ինչի մասին ենք խոսում։ Իսկ հիմա արի՛ հեքիաթ լսենք։";

    // Second-/first-person secret words only — a bare «գաղտնի» is ordinary
    // fairy-tale vocabulary («գաղտնի ճանապարհ», a secret path).
    private static readonly string[] PersonalSecret =
        ["քո գաղտն", "գաղտնիքդ", "մեր գաղտն", "գաղտնիությունդ"];

    // Said next to a personal secret: "safe", "I'll keep", "is kept", "between us".
    private static readonly string[] KeepMarkers =
        ["ապահով", "կպահեմ", "պահում եմ", "պահվում", "մեր միջև", "չեմ ասի"];

    // "Our talk is kept / stays between us" — no secret word needed.
    private static readonly string[] StaysMarkers =
        ["զրույցը պահվում", "խոսակցությունը պահվում", "մնում է մեր միջև", "մնա մեր միջև"];

    private static readonly string[] Parents = ["մայրիկ", "հայրիկ", "ծնող"];

    /// <summary>True when the reply promises secrecy from the child's grown-ups.</summary>
    public static bool IsSecrecyPromise(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return false;
        var r = reply.ToLowerInvariant();

        if (PersonalSecret.Any(r.Contains) && KeepMarkers.Any(r.Contains)) return true;
        if (StaysMarkers.Any(r.Contains)) return true;
        // "I won't tell mom/dad/your parents."
        return r.Contains("չեմ ասի") && Parents.Any(r.Contains);
    }
}

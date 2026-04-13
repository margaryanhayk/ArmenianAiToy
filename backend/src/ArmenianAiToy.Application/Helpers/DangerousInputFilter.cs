namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Client-side prefilter for dangerous content that the OpenAI moderation
/// API misses (Flagged=false on all categories). Catches obvious weapon,
/// explosive, poison, and drug references before the API call.
///
/// Design principles:
///   - High-confidence terms only — every keyword is unambiguously dangerous
///     in a 4-7 year-old's conversation with a toy.
///   - No fairy-tale words — "sword", "fight", "scary" are intentionally
///     excluded because they appear in normal children's stories.
///   - Both English and Armenian — Armenian uses word stems so inflected
///     forms (e.g. ռumadelays → delays, delays) are also caught.
///   - Case-insensitive substring match on the lowercased input.
///   - This does NOT replace the moderation API — it supplements it.
/// </summary>
public static class DangerousInputFilter
{
    // Organized by threat category for auditability.
    // Each entry is matched as a case-insensitive substring.
    internal static readonly string[] DangerousKeywords =
    [
        // ── Explosives ──
        "bomb",
        "grenade",
        "explosive",
        "dynamite",
        "\u057c\u0578\u0582\u0574\u0562",     // ռdelays (rumb = bomb)
        "\u0576\u057c\u0576\u0561\u056f",       // delays (nrnak = grenade)

        // ── Poison ──
        "poison",
        "\u0569\u0578\u0582\u0575\u0576",       // delays (tuyn = poison)

        // ── Firearms / weapons ──
        "gun",
        "firearm",
        "rifle",
        "pistol",
        "\u0566\u0565\u0576\u0584",             // delays (zenk = weapon)
        "\u0570\u0580\u0561\u0581\u0561\u0576", // delays (hratsan = rifle/gun)
        "\u0561\u057f\u0580\u0573\u0561\u0576\u0561\u056f", // delays (atrchandelays = pistol)

        // ── Drugs / narcotics ──
        "cocaine",
        "heroin",
        "narcotic",
        "marijuana",
        "\u0569\u0574\u0580\u0561\u0576\u0575\u0578\u0582\u0569", // delays (tmranyut = narcotic/drug)
    ];

    /// <summary>
    /// Returns true if the input contains any obviously dangerous keyword.
    /// Intended as a fast prefilter before the moderation API call.
    /// </summary>
    public static bool IsUnsafe(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;

        var lower = input.ToLowerInvariant();
        for (int i = 0; i < DangerousKeywords.Length; i++)
        {
            if (lower.Contains(DangerousKeywords[i]))
                return true;
        }
        return false;
    }
}

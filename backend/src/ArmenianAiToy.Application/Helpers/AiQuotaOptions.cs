namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Strongly-typed options for the daily AI-question quota (the
/// Curiosity-Window "questions per day" limit). Bound from configuration
/// section <c>AI:QuestionQuota</c>.
/// <para>
/// <b>Opt-in / off by default.</b> Unlike the per-device cost cap (a
/// safety net that ships on), this is a PRODUCT feature — a friendly
/// daily allowance that a future Free/Plus tier will drive. It ships
/// <see cref="Enabled"/> = <c>false</c> so shipped behavior is unchanged
/// until an operator (or the tier layer) turns it on.
/// </para>
/// <para>
/// In-process, in-memory (see <see cref="AiQuotaMeter"/>); process
/// restart resets counters.
/// </para>
/// </summary>
public sealed class AiQuotaOptions
{
    /// <summary>Master switch. When <c>false</c>, the quota never trips
    /// and nothing is counted. Default <c>false</c> (opt-in).</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Default daily question allowance per child (or per device
    /// when no child id is supplied). A generous Free-tier-ish default;
    /// the tier layer will override per key when it lands.</summary>
    public int DailyQuestionLimit { get; set; } = 20;

    /// <summary>Optional per-key override map (child-id or device-id
    /// string → limit). The seam a future tier system uses to grant
    /// Plus/Premium children a higher allowance without code changes.
    /// Misses and unparseable keys fall back to
    /// <see cref="DailyQuestionLimit"/>.</summary>
    public Dictionary<string, int> PerKeyOverride { get; set; } = new();

    /// <summary>Resolve the daily limit that applies to a specific key
    /// (child id when present, else device id).</summary>
    public int LimitForKey(Guid key)
    {
        if (PerKeyOverride.TryGetValue(key.ToString(), out var v))
        {
            return v;
        }
        return DailyQuestionLimit;
    }
}

namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Strongly-typed options for the v1 per-device daily OpenAI cost cap.
/// Bound from configuration section <c>OpenAI:DailyCostCap</c>.
/// <para>
/// In-process, in-memory cap. Process restart resets the per-device
/// counters — acceptable because the worst case is one extra
/// cap-worth of spend during a restart, not unbounded spend.
/// </para>
/// </summary>
public sealed class OpenAIDailyCostCapOptions
{
    /// <summary>
    /// Master switch. When <c>false</c>, the cost gate never trips and
    /// no recording / metric emission happens. Defaults to <c>true</c>
    /// so a missing config section still gives operators the safety
    /// net.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Default per-device daily cap in USD.
    ///
    /// <para>
    /// <b>Set from a number of questions, not picked as a dollar figure.</b>
    /// Nobody can say what $0.50 buys, so the intent gets lost the first time
    /// someone edits it. The product intent is <b>30 in-story questions per
    /// child per day</b>; measured cost is $0.0078 per question, so
    /// 30 × $0.0078 ≈ <c>$0.234</c>, rounded to <c>0.25</c> — about $7.50 a
    /// month per toy at the ceiling, against electronics that cost $17-27 to
    /// make. Thirty questions is far more than a 4-7 year old asks in a day;
    /// the limit exists for a stuck button, not for a curious child.
    /// </para>
    ///
    /// <para>
    /// This replaced <c>0.50</c> on 2026-08-12. That value was not what it
    /// appeared: the meter counted the child's ~21-character question rather
    /// than the ~8,343-character prompt sent with it, so the real ceiling was
    /// about $1.49/day — roughly $45/month per toy. The counting is fixed in
    /// the same commit, so this number is now honest.
    /// </para>
    ///
    /// <para>
    /// <b>INTERIM — must be revisited before production</b> (owner decision,
    /// 2026-08-12). One flat limit for everybody is the right call while no
    /// real child has used the toy; tiers need a free-tier number that can
    /// only be learned by watching one. See
    /// <c>docs/usage-tiers-brainstorm.md</c>.
    /// </para>
    /// </summary>
    public decimal Default { get; set; } = 0.25m;

    /// <summary>The number of in-story questions per device per day that
    /// <see cref="Default"/> is derived from. Not read at runtime — it exists
    /// so the intent survives the next edit, and so a test can pin the two
    /// together.</summary>
    public const int IntendedQuestionsPerDay = 30;

    /// <summary>Measured USD cost of one in-story question, 2026-08-12.
    /// Source: tools/quality-evidence/cost-per-hour-of-play-20260812.md.</summary>
    public const decimal MeasuredUsdPerQuestion = 0.0078m;

    /// <summary>
    /// #022 — optional FLEET-wide daily ceiling in USD: a kill-switch on
    /// total spend across all devices on this instance for the UTC day. When
    /// the accumulated fleet cost reaches it, the paid pipeline fails closed
    /// for every device until the next UTC day. Default <c>0</c> = DISABLED
    /// (opt-in), so shipped behavior is unchanged until an operator sets it.
    /// In-process / per-instance like the per-device cap (see class remarks);
    /// still a real backstop against one instance's runaway loop.
    /// </summary>
    public decimal Global { get; set; } = 0m;

    /// <summary>
    /// Optional per-device override map. Keys are device id strings
    /// (parseable as <see cref="Guid"/>) and values are USD caps. When
    /// a device id is present in this map, its value overrides
    /// <see cref="Default"/>. Lookups that fail to parse or that miss
    /// fall back to <see cref="Default"/>.
    /// </summary>
    public Dictionary<string, decimal> PerDeviceOverride { get; set; } = new();

    /// <summary>
    /// Resolve the cap that applies to a specific device. Returns the
    /// per-device override when present and parseable; otherwise
    /// returns <see cref="Default"/>.
    /// </summary>
    public decimal CapForDevice(Guid deviceId)
    {
        if (PerDeviceOverride.TryGetValue(deviceId.ToString(), out var v)) return v;
        return Default;
    }
}

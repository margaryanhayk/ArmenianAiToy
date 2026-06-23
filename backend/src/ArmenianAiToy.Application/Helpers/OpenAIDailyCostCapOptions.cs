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
    /// Default per-device daily cap in USD. Conservative default of
    /// <c>0.50</c> so a misbehaving client cannot rack up significant
    /// cost in a single UTC day.
    /// </summary>
    public decimal Default { get; set; } = 0.50m;

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

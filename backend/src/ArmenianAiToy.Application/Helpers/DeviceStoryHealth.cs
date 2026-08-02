namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Derives whether a toy can actually PLAY STORIES right now, from the
/// self-diagnostics it reports on its heartbeat.
///
/// <para>
/// <b>The failure this exists for.</b> Stories live on the toy's SD card. If
/// that card is not mounted the toy plays nothing: pressing the button does
/// nothing audible, so from the outside the toy is simply SILENT. Observed on
/// the bench when a 5 V wire to the card reader came loose — diagnosable in
/// one line of serial output, and completely invisible to a parent, whose only
/// signal is "it stopped working". Silence is the worst failure mode for a
/// children's toy: the parent blames the product, not the loose wire.
/// </para>
///
/// <para>
/// <b>Derived at READ time, never stored.</b> Same rule as
/// <see cref="DeviceOtaHealth"/>: persisting a computed verdict just creates a
/// second value that goes stale. <c>Device.SdCardOk</c> holds the raw report;
/// this turns it into a parent-facing verdict together with presence, because
/// the answer genuinely depends on "when did we last hear from it".
/// </para>
///
/// Pure and clock-injected; no DB, no IO.
/// </summary>
public static class DeviceStoryHealth
{
    /// <summary>Toy is reporting a working card — stories will play.</summary>
    public const string Ok = "ok";

    /// <summary>Toy is reporting that its memory card is NOT working. Stories
    /// cannot play. This is the actionable one — the parent should be told.</summary>
    public const string NoStorage = "no_storage";

    /// <summary>Not checking in, so its health is unknowable. Deliberately
    /// distinct from <see cref="NoStorage"/>: "I can't hear the toy" and "the
    /// toy told me its card is broken" need different words to a parent.</summary>
    public const string Offline = "offline";

    /// <summary>Checking in, but running firmware old enough that it never
    /// reports diagnostics. Not a fault — absence of evidence.</summary>
    public const string Unknown = "unknown";

    /// <summary>Mirrors the presence window used for the online dot.</summary>
    public const int DefaultOnlineThresholdSeconds =
        DeviceOtaHealth.DefaultOnlineThresholdSeconds;

    /// <summary>
    /// Resolves the verdict. Offline wins over a stale <paramref name="sdCardOk"/>
    /// report: a toy unplugged three days ago must not still be shown as
    /// "memory card broken" — the parent's real problem then is that it is off.
    /// </summary>
    public static string Resolve(
        bool? sdCardOk,
        DateTime lastSeenAtUtc,
        DateTime nowUtc,
        int onlineThresholdSeconds = DefaultOnlineThresholdSeconds)
    {
        var threshold = onlineThresholdSeconds > 0
            ? onlineThresholdSeconds
            : DefaultOnlineThresholdSeconds;

        if (nowUtc - lastSeenAtUtc >= TimeSpan.FromSeconds(threshold))
        {
            return Offline;
        }

        return sdCardOk switch
        {
            true => Ok,
            false => NoStorage,
            null => Unknown,
        };
    }

    /// <summary>
    /// True when the parent should be shown a warning. Only a device that is
    /// present AND reporting a real fault qualifies — "offline" is surfaced by
    /// the existing presence dot, and "unknown" is not a fault.
    /// </summary>
    public static bool NeedsParentAttention(string verdict) => verdict == NoStorage;
}

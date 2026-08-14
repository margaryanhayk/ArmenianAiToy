namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// The closed set of device-command wire types the backend will enqueue.
/// An unknown type is rejected at enqueue (never delivered) so a typo or a
/// not-yet-supported command can't reach a device.
/// </summary>
public static class DeviceCommandTypes
{
    public const string FirmwareUpdate = "firmware_update";

    /// <summary>
    /// "Sync this toy now" — the toy re-checks the content manifest on its
    /// next poll instead of waiting for its own scheduled attempt, and acks
    /// with free-form sync diagnostics into <c>AckDiagnosticsJson</c>.
    /// <para>
    /// Enqueuing this against firmware that predates the handler is harmless:
    /// an unrecognized type acks <c>failed</c>/<c>unsupported_type</c>, which
    /// is a recorded outcome, not a fault.
    /// </para>
    /// </summary>
    public const string RefreshStoryManifest = "refresh_story_manifest";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        FirmwareUpdate,
        RefreshStoryManifest,
    };

    /// <summary>True when <paramref name="type"/> is a supported command type.</summary>
    public static bool IsKnown(string? type) =>
        !string.IsNullOrWhiteSpace(type) && Known.Contains(type);
}

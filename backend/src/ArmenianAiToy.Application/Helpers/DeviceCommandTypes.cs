namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// The closed set of device-command wire types the backend will enqueue.
/// An unknown type is rejected at enqueue (never delivered) so a typo or a
/// not-yet-supported command can't reach a device.
/// <para>
/// This slice ships ONLY <see cref="FirmwareUpdate"/> — the OTA-foundation
/// contract. Feature-1 content-sync commands (refresh_story_manifest, etc.)
/// are deliberately NOT here yet; they are added when that slice lands.
/// </para>
/// </summary>
public static class DeviceCommandTypes
{
    public const string FirmwareUpdate = "firmware_update";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        FirmwareUpdate,
    };

    /// <summary>True when <paramref name="type"/> is a supported command type.</summary>
    public static bool IsKnown(string? type) =>
        !string.IsNullOrWhiteSpace(type) && Known.Contains(type);
}

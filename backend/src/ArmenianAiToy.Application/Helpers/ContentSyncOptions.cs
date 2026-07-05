namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Config-bound description of the ONE story-audio item the device
/// content-manifest offers, from section <c>ContentSync</c>. This is the
/// deliberate minimal first slice of Cloud→SD story sync: a single
/// configured MP3, mirroring <see cref="FirmwareUpdateOptions"/>' shape
/// (config-driven "current release", fail-closed defaults). A later slice
/// replaces the single item with the per-device entitled story set
/// (tiers / story packs) without changing the device-facing contract.
/// <para>
/// Ships <see cref="Enabled"/> = <c>false</c> → the manifest returns an
/// empty story list and the content-file endpoint 404s until an operator
/// configures a real item.
/// </para>
/// </summary>
public sealed class ContentSyncOptions
{
    /// <summary>Master switch. When false the manifest is empty.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Library story id (kebab-case, e.g. "anban-huri").</summary>
    public string StoryId { get; set; } = string.Empty;

    /// <summary>Content version — bump when the audio changes so devices
    /// re-download (the SD filename embeds it).</summary>
    public int Version { get; set; } = 1;

    /// <summary>Display title (informational; the device only logs it).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>URL the device downloads from. Defaults to the backend's own
    /// device-authed streamer; absolute URLs are passed through verbatim.</summary>
    public string AudioUrl { get; set; } = "/api/devices/content-file";

    /// <summary>Server-side filesystem path of the MP3 that
    /// <c>GET /api/devices/content-file</c> streams. Deliberately OUTSIDE
    /// wwwroot (same posture as <see cref="FirmwareUpdateOptions.ImagePath"/>).
    /// Empty → the endpoint 404s.</summary>
    public string AudioPath { get; set; } = string.Empty;

    /// <summary>Lowercase hex SHA-256 of the MP3; the device verifies it
    /// while streaming to SD and discards the download on mismatch.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
}

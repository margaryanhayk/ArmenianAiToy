namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Optional body for <c>POST /api/devices/heartbeat</c>. Every field is
/// optional so the legacy body-less heartbeat keeps working; only the
/// non-null fields are stamped onto the device. Reported by the toy so the
/// backend knows what firmware/board/partition each device is running and can
/// decide whether to offer an OTA update.
/// </summary>
public sealed record DeviceHeartbeatRequest(
    string? FirmwareVersion = null,
    string? FirmwareBuild = null,
    string? BoardModel = null,
    string? PartitionName = null,
    string? LastOtaStatus = null,
    bool? SdCardOk = null,

    // --- Content report -------------------------------------------------
    // What the toy actually has on its card, as opposed to what the backend
    // ADVERTISES in the manifest. Without these the server can only report
    // its own configuration, which would be a false statement about a
    // child's device. The toy already writes every one of these values to
    // /content_index.json after each sync; this is that file, summarised.
    //
    // A SUMMARY, not an inventory: the heartbeat runs every ~60 s and the
    // full list of 104 game clips would be a kilobyte a minute for nothing.
    // Firmware sends the block only when it CHANGES (plus once per boot),
    // the same write-on-change discipline ota_state uses, so a steady toy
    // adds nothing to its heartbeat.
    int? ContentIndexSchema = null,
    string? ContentStories = null,
    int? ContentGameClips = null,
    int? ContentVoiceClips = null,
    int? ContentMusicTracks = null,
    int? ContentSyncedSecondsAgo = null)
{
    /// <summary>True when the body carried at least one firmware field worth
    /// persisting (so a bare presence-only heartbeat does no DB write).</summary>
    public bool HasAnyFirmwareField =>
        FirmwareVersion is not null
        || FirmwareBuild is not null
        || BoardModel is not null
        || PartitionName is not null
        || LastOtaStatus is not null
        || SdCardOk is not null
        || HasAnyContentField;

    /// <summary>True when the body carried a content report. Separate from
    /// <see cref="HasAnyFirmwareField"/> so a content-only heartbeat — which
    /// is what a toy sends after a sync with no firmware change — still
    /// persists rather than being dropped as a bare presence ping.</summary>
    public bool HasAnyContentField =>
        ContentIndexSchema is not null
        || ContentStories is not null
        || ContentGameClips is not null
        || ContentVoiceClips is not null
        || ContentMusicTracks is not null
        || ContentSyncedSecondsAgo is not null;
}

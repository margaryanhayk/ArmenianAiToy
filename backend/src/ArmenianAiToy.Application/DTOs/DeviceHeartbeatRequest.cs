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
    int? ContentSyncedSecondsAgo = null,

    // --- Sync diagnostics ------------------------------------------------
    // What the toy TRIED, as opposed to what it HAS. The content report
    // above answers "which stories are on the card"; it cannot answer "why
    // did nothing new arrive", because a sync that fails leaves the card
    // exactly as it was — and the carry-forward in content_sync.cpp then
    // re-advertises the OLD entry as verified, so a failed sync reads as a
    // healthy library.
    //
    // Motivating incident (2026-08-14): firmware 1.2.1 crash-looped all
    // night, panicking on the manifest parse and rebooting every ~184 s.
    // It heartbeat normally for the first 180 s of every cycle, so
    // LastSeenAt stayed fresh and every surface showed it merely online.
    // Nothing in the product could say "this toy has panicked 400 times".
    //
    // Two fields, two questions, because a device that dies MID-sync can
    // never report a status at all — the next boot's reset reason is the
    // only surviving evidence:
    //   • ContentSyncStatus / ContentSyncError — the last attempt's verdict.
    //   • ResetReason / BootCount — how the last boot ended, and how many
    //     boots have gone by without a clean sync. A crash loop is only
    //     visible as a NUMBER; one panic is an incident.
    string? ContentSyncStatus = null,
    string? ContentSyncError = null,
    string? ResetReason = null,
    int? BootCount = null)
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
        || ContentSyncedSecondsAgo is not null
        || HasAnySyncDiagnosticField;

    /// <summary>True when the body carried a sync diagnostic. Separate from
    /// <see cref="HasAnyContentField"/> because it is the ONE report a broken
    /// toy can still send: a device whose card is dead has no
    /// <c>ContentStories</c> to send and would otherwise look identical on
    /// the wire to a device that never had the firmware to report at all.
    /// It is folded INTO <see cref="HasAnyContentField"/> so a
    /// diagnostics-only body still persists rather than being dropped as a
    /// bare presence ping.</summary>
    public bool HasAnySyncDiagnosticField =>
        ContentSyncStatus is not null
        || ContentSyncError is not null
        || ResetReason is not null
        || BootCount is not null;
}

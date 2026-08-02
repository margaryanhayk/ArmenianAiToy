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
    bool? SdCardOk = null)
{
    /// <summary>True when the body carried at least one firmware field worth
    /// persisting (so a bare presence-only heartbeat does no DB write).</summary>
    public bool HasAnyFirmwareField =>
        FirmwareVersion is not null
        || FirmwareBuild is not null
        || BoardModel is not null
        || PartitionName is not null
        || LastOtaStatus is not null
        || SdCardOk is not null;
}

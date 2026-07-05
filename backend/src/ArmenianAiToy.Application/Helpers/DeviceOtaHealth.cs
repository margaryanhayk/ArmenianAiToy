namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Derives a device's CURRENT OTA health from its last-attempt report —
/// the read-side fix for the bench-observed caveat where a healthy,
/// up-to-date, checking-in device looked broken because
/// <c>Device.LastOtaStatus</c> still said <c>failed:sha256_mismatch</c>
/// from an old attempt.
///
/// <para>
/// The split: <c>LastOtaStatus</c> is ATTEMPT HISTORY (sticky by design —
/// the device's NVS re-reports it on every heartbeat until the next
/// attempt, and clearing it server-side would be overwritten within one
/// heartbeat interval, ~60 s). This resolver is CURRENT HEALTH: a device
/// that is checking in and not mid-update is <c>ok</c>, no matter how its
/// last attempt ended. Dashboards show BOTH — the failure stays visible
/// as a diagnostic, but never paints a healthy device red.
/// </para>
///
/// Pure and clock-injected; no DB, no IO.
/// </summary>
public static class DeviceOtaHealth
{
    /// <summary>Currently applying an update (downloading / awaiting the
    /// post-reboot check-in). Transient; resolves to ok or a failed
    /// attempt within minutes.</summary>
    public const string Updating = "updating";

    /// <summary>Not checking in (same presence window as the parent
    /// dashboard's IsOnline dot). OTA state is unknowable while offline.</summary>
    public const string Offline = "offline";

    /// <summary>Checking in and not mid-update — healthy NOW, regardless
    /// of how the LAST attempt ended.</summary>
    public const string Ok = "ok";

    /// <summary>Default presence window — mirrors
    /// <c>Presence:OnlineThresholdSeconds</c>'s shipped default (180 s)
    /// used for <c>LinkedDeviceDto.IsOnline</c>.</summary>
    public const int DefaultOnlineThresholdSeconds = 180;

    /// <summary>Resolve current OTA health. <paramref name="lastOtaStatus"/>
    /// is the device-reported attempt status (e.g. <c>confirmed</c>,
    /// <c>failed:sha256_mismatch</c>, <c>rebooting</c>, null when the
    /// device has never reported).</summary>
    public static string Resolve(
        string? lastOtaStatus,
        DateTime lastSeenAt,
        DateTime nowUtc,
        int onlineThresholdSeconds = DefaultOnlineThresholdSeconds)
    {
        if ((nowUtc - lastSeenAt).TotalSeconds >= onlineThresholdSeconds)
        {
            return Offline;
        }
        // In-flight states from the firmware's ota_state_name(): an attempt
        // is actively running. (Prefix match tolerates a future
        // "rebooting:<detail>" shape; current firmware sends the bare name.)
        if (lastOtaStatus is not null
            && (lastOtaStatus.StartsWith("downloading", StringComparison.OrdinalIgnoreCase)
                || lastOtaStatus.StartsWith("rebooting", StringComparison.OrdinalIgnoreCase)))
        {
            return Updating;
        }
        // Everything else — null/idle/confirmed AND failed:* / rolled_back:*
        // — is a device that is alive and not mid-update: healthy NOW.
        return Ok;
    }
}

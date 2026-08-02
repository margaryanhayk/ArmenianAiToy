using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Pins the parent-facing "can this toy actually play stories?" verdict.
///
/// Motivating incident (bench, 2026-08-03): the SD card lost power, so the toy
/// played nothing — the button looked dead and the toy was simply SILENT. The
/// fault was one line of serial output and completely invisible to a parent.
/// These tests exist so the toy can never fail silently again.
/// </summary>
public class DeviceStoryHealthTests
{
    private static readonly DateTime Now = new(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ReportingWorkingCard_WhilePresent_IsOk()
    {
        var verdict = DeviceStoryHealth.Resolve(
            sdCardOk: true, lastSeenAtUtc: Now.AddSeconds(-10), nowUtc: Now);

        Assert.Equal(DeviceStoryHealth.Ok, verdict);
        Assert.False(DeviceStoryHealth.NeedsParentAttention(verdict));
    }

    [Fact]
    public void ReportingBrokenCard_WhilePresent_IsNoStorage_AndNeedsAttention()
    {
        // KEYSTONE: this is the whole point — a present toy telling us its card
        // is dead must produce an actionable parent warning, not silence.
        var verdict = DeviceStoryHealth.Resolve(
            sdCardOk: false, lastSeenAtUtc: Now.AddSeconds(-10), nowUtc: Now);

        Assert.Equal(DeviceStoryHealth.NoStorage, verdict);
        Assert.True(DeviceStoryHealth.NeedsParentAttention(verdict));
    }

    [Fact]
    public void LegacyFirmwareThatNeverReports_IsUnknown_NotAFault()
    {
        // Absence of evidence is not a fault: an older toy that never sends the
        // field must not be painted broken.
        var verdict = DeviceStoryHealth.Resolve(
            sdCardOk: null, lastSeenAtUtc: Now.AddSeconds(-10), nowUtc: Now);

        Assert.Equal(DeviceStoryHealth.Unknown, verdict);
        Assert.False(DeviceStoryHealth.NeedsParentAttention(verdict));
    }

    [Fact]
    public void OfflineWinsOverAStaleBrokenCardReport()
    {
        // KEYSTONE: a toy unplugged days ago must NOT still be reported as
        // "memory card broken" — by then the parent's real problem is that it
        // is switched off, and showing the old fault would send them hunting
        // the wrong thing.
        var verdict = DeviceStoryHealth.Resolve(
            sdCardOk: false, lastSeenAtUtc: Now.AddDays(-3), nowUtc: Now);

        Assert.Equal(DeviceStoryHealth.Offline, verdict);
        Assert.False(DeviceStoryHealth.NeedsParentAttention(verdict));
    }

    [Fact]
    public void OfflineWinsOverAStaleHealthyReportToo()
    {
        var verdict = DeviceStoryHealth.Resolve(
            sdCardOk: true, lastSeenAtUtc: Now.AddDays(-3), nowUtc: Now);

        Assert.Equal(DeviceStoryHealth.Offline, verdict);
    }

    [Fact]
    public void PresenceWindowMatchesTheOnlineDot()
    {
        // Just inside the window is still present; just outside is offline.
        // Sharing DeviceOtaHealth's default keeps the online dot and this
        // verdict from ever disagreeing about the same device.
        var threshold = DeviceStoryHealth.DefaultOnlineThresholdSeconds;
        Assert.Equal(DeviceOtaHealth.DefaultOnlineThresholdSeconds, threshold);

        Assert.Equal(DeviceStoryHealth.NoStorage, DeviceStoryHealth.Resolve(
            false, Now.AddSeconds(-(threshold - 1)), Now));
        Assert.Equal(DeviceStoryHealth.Offline, DeviceStoryHealth.Resolve(
            false, Now.AddSeconds(-threshold), Now));
    }

    [Fact]
    public void FaultCode_IsIssuedOnlyForARealFault()
    {
        // Owner decision: the parent sees a CODE to quote to support, never an
        // explanation. Only a genuine fault earns one — giving "offline" or
        // "unknown" a code would train parents to ignore codes.
        Assert.Equal(DeviceFaultCode.StorageUnavailable,
            DeviceFaultCode.FromStoryHealth(DeviceStoryHealth.NoStorage));

        Assert.Equal(DeviceFaultCode.None, DeviceFaultCode.FromStoryHealth(DeviceStoryHealth.Ok));
        Assert.Equal(DeviceFaultCode.None, DeviceFaultCode.FromStoryHealth(DeviceStoryHealth.Offline));
        Assert.Equal(DeviceFaultCode.None, DeviceFaultCode.FromStoryHealth(DeviceStoryHealth.Unknown));
    }

    [Fact]
    public void FaultCode_ValueIsStable()
    {
        // Codes go out to parents and get read back to support months later.
        // Pinning the literal makes an accidental renumbering a failing test
        // rather than a support call nobody can decode.
        Assert.Equal("E-101", DeviceFaultCode.StorageUnavailable);
        Assert.Equal("", DeviceFaultCode.None);
    }

    [Fact]
    public void NonPositiveThresholdFallsBackToTheDefault()
    {
        // A misconfigured Presence:OnlineThresholdSeconds must not make every
        // device instantly "offline".
        var verdict = DeviceStoryHealth.Resolve(
            sdCardOk: false, lastSeenAtUtc: Now.AddSeconds(-10), nowUtc: Now,
            onlineThresholdSeconds: 0);

        Assert.Equal(DeviceStoryHealth.NoStorage, verdict);
    }
}

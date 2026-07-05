using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Pins the OTA attempt-vs-health split (bench caveat fix): a checking-in
/// device that is not mid-update is "ok" — INCLUDING when its last attempt
/// failed. The sticky failed:* string stays a diagnostic, never a health
/// verdict.
/// </summary>
public class DeviceOtaHealthTests
{
    private static readonly DateTime Now = new(2026, 7, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FreshSeen = Now.AddSeconds(-30);   // inside 180 s window
    private static readonly DateTime StaleSeen = Now.AddMinutes(-10);   // outside window

    // KEYSTONE — the exact bench scenario: healthy, checking-in, up-to-date
    // device whose last attempt failed must NOT look broken.
    [Fact]
    public void FailedLastAttempt_ButCheckingIn_IsOk()
    {
        Assert.Equal(DeviceOtaHealth.Ok,
            DeviceOtaHealth.Resolve("failed:sha256_mismatch", FreshSeen, Now));
    }

    [Theory]
    [InlineData(null)]                       // never reported
    [InlineData("idle")]
    [InlineData("confirmed")]
    [InlineData("failed:sha256_mismatch")]
    [InlineData("failed:interrupted")]
    [InlineData("rolled_back:rollback_no_checkin")]
    public void CheckingIn_NotMidUpdate_IsOk(string? lastOtaStatus)
    {
        Assert.Equal(DeviceOtaHealth.Ok,
            DeviceOtaHealth.Resolve(lastOtaStatus, FreshSeen, Now));
    }

    [Theory]
    [InlineData("downloading")]
    [InlineData("rebooting")]
    [InlineData("Rebooting")] // case-tolerant
    public void MidUpdate_IsUpdating(string lastOtaStatus)
    {
        Assert.Equal(DeviceOtaHealth.Updating,
            DeviceOtaHealth.Resolve(lastOtaStatus, FreshSeen, Now));
    }

    [Theory]
    [InlineData("confirmed")]
    [InlineData("failed:sha256_mismatch")]
    [InlineData("rebooting")] // even mid-update: silence wins — state unknowable
    [InlineData(null)]
    public void NotCheckingIn_IsOffline(string? lastOtaStatus)
    {
        Assert.Equal(DeviceOtaHealth.Offline,
            DeviceOtaHealth.Resolve(lastOtaStatus, StaleSeen, Now));
    }

    [Fact]
    public void PresenceWindow_MatchesDefaultThreshold()
    {
        // 179 s ago = online (ok); exactly 180 s = offline. Same boundary
        // shape as the parent dashboard's IsOnline dot.
        Assert.Equal(DeviceOtaHealth.Ok,
            DeviceOtaHealth.Resolve("confirmed", Now.AddSeconds(-179), Now));
        Assert.Equal(DeviceOtaHealth.Offline,
            DeviceOtaHealth.Resolve("confirmed", Now.AddSeconds(-180), Now));
    }
}

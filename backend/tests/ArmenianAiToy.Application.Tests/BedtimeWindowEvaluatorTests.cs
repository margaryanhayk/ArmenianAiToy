using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Domain.Entities;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Unit tests for the pure B4 evaluator. No DB, no clock injection — every
/// test passes a fixed <c>nowUtc</c>. Covers disabled cases, same-day and
/// midnight-crossing windows, exact-boundary behavior, timezone fallback.
/// </summary>
public class BedtimeWindowEvaluatorTests
{
    private static Device DeviceWithWindow(
        TimeOnly? start, TimeOnly? end, string timeZone = "UTC") => new()
    {
        Id = Guid.NewGuid(),
        MacAddress = "mac",
        Name = "Test",
        ApiKey = "dtk",
        RegisteredAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow,
        BedtimeStart = start,
        BedtimeEnd = end,
        TimeZone = timeZone
    };

    [Fact]
    public void Disabled_BothNull_ReturnsFalse()
    {
        var d = DeviceWithWindow(null, null);
        Assert.False(BedtimeWindowEvaluator.IsDeviceInWindow(
            d, new DateTime(2026, 4, 21, 23, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Disabled_HalfNull_StartOnly_ReturnsFalse()
    {
        var d = DeviceWithWindow(new TimeOnly(20, 0), null);
        Assert.False(BedtimeWindowEvaluator.IsDeviceInWindow(
            d, new DateTime(2026, 4, 21, 23, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Disabled_HalfNull_EndOnly_ReturnsFalse()
    {
        var d = DeviceWithWindow(null, new TimeOnly(7, 0));
        Assert.False(BedtimeWindowEvaluator.IsDeviceInWindow(
            d, new DateTime(2026, 4, 21, 23, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Disabled_StartEqualsEnd_ReturnsFalse()
    {
        var d = DeviceWithWindow(new TimeOnly(20, 0), new TimeOnly(20, 0));
        Assert.False(BedtimeWindowEvaluator.IsDeviceInWindow(
            d, new DateTime(2026, 4, 21, 20, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void SameDayWindow_Inside_ReturnsTrue()
    {
        // 08:00–20:00 UTC, now = 12:00 UTC → inside.
        var d = DeviceWithWindow(new TimeOnly(8, 0), new TimeOnly(20, 0));
        Assert.True(BedtimeWindowEvaluator.IsDeviceInWindow(
            d, new DateTime(2026, 4, 21, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void SameDayWindow_Outside_ReturnsFalse()
    {
        var d = DeviceWithWindow(new TimeOnly(8, 0), new TimeOnly(20, 0));
        Assert.False(BedtimeWindowEvaluator.IsDeviceInWindow(
            d, new DateTime(2026, 4, 21, 21, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void MidnightCrossingWindow_EveningSide_ReturnsTrue()
    {
        // 22:00–07:00 UTC, now = 23:00 UTC → inside (evening side).
        var d = DeviceWithWindow(new TimeOnly(22, 0), new TimeOnly(7, 0));
        Assert.True(BedtimeWindowEvaluator.IsDeviceInWindow(
            d, new DateTime(2026, 4, 21, 23, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void MidnightCrossingWindow_MorningSide_ReturnsTrue()
    {
        // 22:00–07:00 UTC, now = 03:30 UTC → inside (morning side).
        var d = DeviceWithWindow(new TimeOnly(22, 0), new TimeOnly(7, 0));
        Assert.True(BedtimeWindowEvaluator.IsDeviceInWindow(
            d, new DateTime(2026, 4, 21, 3, 30, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void MidnightCrossingWindow_MidDay_ReturnsFalse()
    {
        var d = DeviceWithWindow(new TimeOnly(22, 0), new TimeOnly(7, 0));
        Assert.False(BedtimeWindowEvaluator.IsDeviceInWindow(
            d, new DateTime(2026, 4, 21, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Boundary_ExactStart_IsInWindow()
    {
        // Start is inclusive.
        var d = DeviceWithWindow(new TimeOnly(20, 0), new TimeOnly(7, 0));
        Assert.True(BedtimeWindowEvaluator.IsDeviceInWindow(
            d, new DateTime(2026, 4, 21, 20, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Boundary_ExactEnd_IsOutOfWindow()
    {
        // End is exclusive.
        var d = DeviceWithWindow(new TimeOnly(20, 0), new TimeOnly(7, 0));
        Assert.False(BedtimeWindowEvaluator.IsDeviceInWindow(
            d, new DateTime(2026, 4, 21, 7, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void TimezoneConversion_YerevanFourHoursAhead_RespectsLocalTime()
    {
        // Asia/Yerevan is UTC+4 year-round (no DST since 2011). Window 22:00–07:00
        // local. Now = 18:30 UTC → 22:30 Yerevan → inside.
        var d = DeviceWithWindow(new TimeOnly(22, 0), new TimeOnly(7, 0), "Asia/Yerevan");
        Assert.True(BedtimeWindowEvaluator.IsDeviceInWindow(
            d, new DateTime(2026, 4, 21, 18, 30, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void InvalidTimezone_FallsBackToUtc_StillEvaluatesWindow()
    {
        // Evaluate in UTC because the id cannot be resolved — the window
        // must still fire, not be silently disabled. 22:00–07:00 UTC, now
        // 23:00 UTC → true.
        var d = DeviceWithWindow(new TimeOnly(22, 0), new TimeOnly(7, 0),
            timeZone: "Not/ARealZone_Bogus");
        Assert.True(BedtimeWindowEvaluator.IsDeviceInWindow(
            d, new DateTime(2026, 4, 21, 23, 0, 0, DateTimeKind.Utc)));
    }
}

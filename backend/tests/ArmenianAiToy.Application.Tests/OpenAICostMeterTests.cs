using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Unit tests for the in-process per-device per-UTC-day cost meter.
/// Cover under-cap / over-cap / day-boundary / per-device-isolation /
/// flood-controlled-log paths. Pure in-memory, no DI, no I/O.
/// </summary>
public class OpenAICostMeterTests
{
    private static readonly DateTime Day1Noon =
        new(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2Noon =
        new(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IsOverCap_FreshMeter_ReturnsFalse()
    {
        var meter = new OpenAICostMeter();
        var device = Guid.NewGuid();
        Assert.False(meter.IsOverCap(device, capUsd: 0.50m, utcNow: Day1Noon));
    }

    [Fact]
    public void Record_BelowCap_DoesNotTrip()
    {
        var meter = new OpenAICostMeter();
        var device = Guid.NewGuid();
        meter.Record(device, 0.10m, Day1Noon);
        meter.Record(device, 0.20m, Day1Noon);

        Assert.False(meter.IsOverCap(device, capUsd: 0.50m, utcNow: Day1Noon));
        Assert.Equal(0.30m, meter.GetCurrentTotal(device, Day1Noon));
    }

    [Fact]
    public void Record_AtOrAboveCap_Trips()
    {
        var meter = new OpenAICostMeter();
        var device = Guid.NewGuid();
        meter.Record(device, 0.50m, Day1Noon);

        Assert.True(meter.IsOverCap(device, capUsd: 0.50m, utcNow: Day1Noon));
    }

    [Fact]
    public void Record_NegativeOrZero_NoOp()
    {
        var meter = new OpenAICostMeter();
        var device = Guid.NewGuid();
        meter.Record(device, -1m, Day1Noon);
        meter.Record(device, 0m, Day1Noon);

        Assert.Equal(0m, meter.GetCurrentTotal(device, Day1Noon));
        Assert.False(meter.IsOverCap(device, capUsd: 0.50m, utcNow: Day1Noon));
    }

    [Fact]
    public void DayBoundary_ResetsBucket()
    {
        var meter = new OpenAICostMeter();
        var device = Guid.NewGuid();
        meter.Record(device, 0.80m, Day1Noon);
        Assert.True(meter.IsOverCap(device, 0.50m, Day1Noon));

        // Same device, next UTC day → fresh bucket, not over cap.
        Assert.False(meter.IsOverCap(device, 0.50m, Day2Noon));
        Assert.Equal(0m, meter.GetCurrentTotal(device, Day2Noon));
    }

    [Fact]
    public void PerDevice_Isolation()
    {
        var meter = new OpenAICostMeter();
        var deviceA = Guid.NewGuid();
        var deviceB = Guid.NewGuid();
        meter.Record(deviceA, 0.80m, Day1Noon);

        Assert.True(meter.IsOverCap(deviceA, 0.50m, Day1Noon));
        Assert.False(meter.IsOverCap(deviceB, 0.50m, Day1Noon));
        Assert.Equal(0m, meter.GetCurrentTotal(deviceB, Day1Noon));
    }

    [Fact]
    public void ShouldLogCapTrip_OncePerDevicePerDay()
    {
        var meter = new OpenAICostMeter();
        var device = Guid.NewGuid();

        Assert.True(meter.ShouldLogCapTrip(device, Day1Noon));
        // Subsequent calls same day → false (flood-control).
        Assert.False(meter.ShouldLogCapTrip(device, Day1Noon));
        Assert.False(meter.ShouldLogCapTrip(device, Day1Noon));
    }

    [Fact]
    public void ShouldLogCapTrip_NextDay_FiresAgain()
    {
        var meter = new OpenAICostMeter();
        var device = Guid.NewGuid();

        Assert.True(meter.ShouldLogCapTrip(device, Day1Noon));
        Assert.False(meter.ShouldLogCapTrip(device, Day1Noon));

        // Day rolled — log gate fires again for the same device.
        Assert.True(meter.ShouldLogCapTrip(device, Day2Noon));
        Assert.False(meter.ShouldLogCapTrip(device, Day2Noon));
    }

    [Fact]
    public void ShouldLogCapTrip_PerDevice_Independent()
    {
        var meter = new OpenAICostMeter();
        var deviceA = Guid.NewGuid();
        var deviceB = Guid.NewGuid();

        Assert.True(meter.ShouldLogCapTrip(deviceA, Day1Noon));
        Assert.False(meter.ShouldLogCapTrip(deviceA, Day1Noon));

        // Device B is independent — its first call still fires.
        Assert.True(meter.ShouldLogCapTrip(deviceB, Day1Noon));
        Assert.False(meter.ShouldLogCapTrip(deviceB, Day1Noon));
    }

    // ---------- #022 global / fleet-wide ceiling ----------

    [Fact]
    public void Global_AccumulatesAcrossAllDevices()
    {
        var meter = new OpenAICostMeter();
        meter.Record(Guid.NewGuid(), 0.40m, Day1Noon);
        meter.Record(Guid.NewGuid(), 0.40m, Day1Noon);
        meter.Record(Guid.NewGuid(), 0.40m, Day1Noon);

        Assert.Equal(1.20m, meter.GetGlobalTotal(Day1Noon));
        // Each device is well under its own 0.50 cap, but the FLEET total trips.
        Assert.True(meter.IsGlobalOverCap(1.00m, Day1Noon));
        Assert.False(meter.IsGlobalOverCap(2.00m, Day1Noon));
    }

    [Fact]
    public void Global_FreshMeter_NotOverCap()
    {
        var meter = new OpenAICostMeter();
        Assert.False(meter.IsGlobalOverCap(0.01m, Day1Noon));
        Assert.Equal(0m, meter.GetGlobalTotal(Day1Noon));
    }

    [Fact]
    public void Global_DayBoundary_Resets()
    {
        var meter = new OpenAICostMeter();
        meter.Record(Guid.NewGuid(), 5.00m, Day1Noon);
        Assert.True(meter.IsGlobalOverCap(1.00m, Day1Noon));

        // Next UTC day → fleet bucket resets.
        Assert.False(meter.IsGlobalOverCap(1.00m, Day2Noon));
        Assert.Equal(0m, meter.GetGlobalTotal(Day2Noon));
    }

    [Fact]
    public void ShouldLogGlobalCapTrip_OncePerDay_FiresAgainNextDay()
    {
        var meter = new OpenAICostMeter();
        Assert.True(meter.ShouldLogGlobalCapTrip(Day1Noon));
        Assert.False(meter.ShouldLogGlobalCapTrip(Day1Noon));
        // Day rolled — fires again.
        Assert.True(meter.ShouldLogGlobalCapTrip(Day2Noon));
        Assert.False(meter.ShouldLogGlobalCapTrip(Day2Noon));
    }

    [Fact]
    public void Global_DoesNotDisturbPerDeviceCounters()
    {
        var meter = new OpenAICostMeter();
        var device = Guid.NewGuid();
        meter.Record(device, 0.30m, Day1Noon);

        // Per-device total and global total both reflect the one record.
        Assert.Equal(0.30m, meter.GetCurrentTotal(device, Day1Noon));
        Assert.Equal(0.30m, meter.GetGlobalTotal(Day1Noon));
    }
}

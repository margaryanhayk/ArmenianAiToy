using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Unit tests for <see cref="AiQuotaMeter"/> — the per-key per-UTC-day
/// question counter behind the daily AI-question quota. Mirrors the
/// deterministic style of the cost-meter tests.
/// </summary>
public class AiQuotaMeterTests
{
    private static readonly DateTime Day1 = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day1Later = new(2026, 7, 1, 23, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2 = new(2026, 7, 2, 0, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void FreshKey_IsNotOverQuota()
    {
        var meter = new AiQuotaMeter();
        Assert.False(meter.IsOverQuota(Guid.NewGuid(), limit: 3, Day1));
    }

    [Fact]
    public void Record_IncrementsCount_UntilOverQuota()
    {
        var meter = new AiQuotaMeter();
        var key = Guid.NewGuid();

        meter.Record(key, Day1);
        meter.Record(key, Day1);
        Assert.Equal(2, meter.GetCount(key, Day1));
        Assert.False(meter.IsOverQuota(key, limit: 3, Day1)); // 2 < 3

        meter.Record(key, Day1);
        Assert.True(meter.IsOverQuota(key, limit: 3, Day1)); // 3 >= 3
    }

    [Fact]
    public void NonPositiveLimit_IsNeverOverQuota()
    {
        var meter = new AiQuotaMeter();
        var key = Guid.NewGuid();
        for (var i = 0; i < 100; i++) meter.Record(key, Day1);

        Assert.False(meter.IsOverQuota(key, limit: 0, Day1));
        Assert.False(meter.IsOverQuota(key, limit: -5, Day1));
    }

    [Fact]
    public void Count_RollsOverAtUtcDayBoundary()
    {
        var meter = new AiQuotaMeter();
        var key = Guid.NewGuid();

        meter.Record(key, Day1);
        meter.Record(key, Day1Later);
        Assert.Equal(2, meter.GetCount(key, Day1Later));

        // New UTC day → bucket resets.
        Assert.Equal(0, meter.GetCount(key, Day2));
        Assert.False(meter.IsOverQuota(key, limit: 1, Day2));
    }

    [Fact]
    public void KeysAreIsolated()
    {
        var meter = new AiQuotaMeter();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        meter.Record(a, Day1);
        meter.Record(a, Day1);

        Assert.Equal(2, meter.GetCount(a, Day1));
        Assert.Equal(0, meter.GetCount(b, Day1));
    }

    [Fact]
    public void ShouldLogQuotaTrip_IsOncePerKeyPerDay()
    {
        var meter = new AiQuotaMeter();
        var key = Guid.NewGuid();

        Assert.True(meter.ShouldLogQuotaTrip(key, Day1));
        Assert.False(meter.ShouldLogQuotaTrip(key, Day1)); // suppressed same day
        Assert.True(meter.ShouldLogQuotaTrip(key, Day2));   // new day → allowed again
    }
}

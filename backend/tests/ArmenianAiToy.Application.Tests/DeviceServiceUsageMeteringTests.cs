using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for the usage-tier metering foundation's database half:
/// <c>DeviceService.RecordUsageQuestionAsync</c> (the <c>DeviceUsageDay</c>
/// upsert) and <c>DeviceService.GetUsageAllowanceStatusAsync</c> (the
/// gate/parent-surface read). 2026-09-11, ships behind
/// <c>Usage:Tiers:Enabled</c>=false.
/// </summary>
public class DeviceServiceUsageMeteringTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static DeviceService NewService(AppDbContext db, UsageTiersOptions? opts = null) =>
        new(db, Substitute.For<ILogger<DeviceService>>(), opts);

    private static Device Dev(string tier = "free") => new()
    {
        Id = Guid.NewGuid(),
        Name = "Bench",
        MacAddress = "mac-" + Guid.NewGuid().ToString("N")[..8],
        RegisteredAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow,
        UsageTier = tier,
    };

    // ---------- RecordUsageQuestionAsync ----------

    [Fact]
    public async Task RecordUsageQuestionAsync_FirstCallToday_CreatesRowWithOneQuestion()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        var service = NewService(db);
        var now = DateTime.UtcNow;

        await service.RecordUsageQuestionAsync(device.Id, 0.0078m, now);

        var row = Assert.Single(db.Set<DeviceUsageDay>());
        Assert.Equal(device.Id, row.DeviceId);
        Assert.Equal(now.Date, row.DayUtc);
        Assert.Equal(1, row.Questions);
        Assert.Equal(0.0078m, row.EstimatedUsd);
    }

    [Fact]
    public async Task RecordUsageQuestionAsync_SecondCallSameDay_IncrementsSameRow()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        var service = NewService(db);
        var now = DateTime.UtcNow;

        await service.RecordUsageQuestionAsync(device.Id, 0.01m, now);
        await service.RecordUsageQuestionAsync(device.Id, 0.02m, now.AddMinutes(5));

        var row = Assert.Single(db.Set<DeviceUsageDay>());
        Assert.Equal(2, row.Questions);
        Assert.Equal(0.03m, row.EstimatedUsd);
    }

    [Fact]
    public async Task RecordUsageQuestionAsync_DifferentDays_CreatesSeparateRows()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        var service = NewService(db);
        var day1 = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2026, 9, 11, 1, 0, 0, DateTimeKind.Utc);

        await service.RecordUsageQuestionAsync(device.Id, 0.01m, day1);
        await service.RecordUsageQuestionAsync(device.Id, 0.01m, day2);

        Assert.Equal(2, db.Set<DeviceUsageDay>().Count());
    }

    [Fact]
    public async Task RecordUsageQuestionAsync_NegativeCost_ClampsToZero()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        var service = NewService(db);

        await service.RecordUsageQuestionAsync(device.Id, -5m, DateTime.UtcNow);

        var row = Assert.Single(db.Set<DeviceUsageDay>());
        Assert.Equal(1, row.Questions); // the question itself still counts
        Assert.Equal(0m, row.EstimatedUsd);
    }

    // ---------- GetUsageAllowanceStatusAsync ----------

    [Fact]
    public async Task GetUsageAllowanceStatusAsync_UnknownDevice_FallsBackToFreeTierZeroCounts()
    {
        var db = NewDb();
        var service = NewService(db, new UsageTiersOptions
        {
            Enabled = true,
            Plans = new() { new UsageTierPlan { Name = "free", QuestionsPerDay = 30 } },
        });

        var status = await service.GetUsageAllowanceStatusAsync(Guid.NewGuid(), DateTime.UtcNow);

        Assert.Equal("free", status.Tier);
        Assert.Equal(0, status.QuestionsToday);
        Assert.Equal(30, status.AllowanceToday);
        Assert.False(status.IsExhausted);
    }

    [Fact]
    public async Task GetUsageAllowanceStatusAsync_CountsTodayOnly_NotOtherDays()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        var opts = new UsageTiersOptions
        {
            Enabled = true,
            Plans = new() { new UsageTierPlan { Name = "free", QuestionsPerDay = 5 } },
        };
        var service = NewService(db, opts);
        var today = new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
        var yesterday = today.AddDays(-1);

        await service.RecordUsageQuestionAsync(device.Id, 0.01m, yesterday);
        await service.RecordUsageQuestionAsync(device.Id, 0.01m, today);
        await service.RecordUsageQuestionAsync(device.Id, 0.01m, today.AddHours(1));

        var status = await service.GetUsageAllowanceStatusAsync(device.Id, today.AddHours(2));

        Assert.Equal(2, status.QuestionsToday);
        Assert.Equal(3, status.QuestionsThisMonth); // month total includes yesterday
    }

    [Fact]
    public async Task GetUsageAllowanceStatusAsync_MonthSum_ExcludesPriorMonth()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        var opts = new UsageTiersOptions
        {
            Enabled = true,
            Plans = new() { new UsageTierPlan { Name = "free", QuestionsPerMonth = 100 } },
        };
        var service = NewService(db, opts);
        var lastMonth = new DateTime(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc);
        var thisMonth = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

        await service.RecordUsageQuestionAsync(device.Id, 0.01m, lastMonth);
        await service.RecordUsageQuestionAsync(device.Id, 0.01m, thisMonth);

        var status = await service.GetUsageAllowanceStatusAsync(device.Id, thisMonth);

        Assert.Equal(1, status.QuestionsThisMonth);
    }

    [Fact]
    public async Task GetUsageAllowanceStatusAsync_ExhaustedWhenTodayMeetsDailyCap()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        var opts = new UsageTiersOptions
        {
            Enabled = true,
            Plans = new() { new UsageTierPlan { Name = "free", QuestionsPerDay = 2 } },
        };
        var service = NewService(db, opts);
        var now = DateTime.UtcNow;

        await service.RecordUsageQuestionAsync(device.Id, 0.01m, now);
        await service.RecordUsageQuestionAsync(device.Id, 0.01m, now);

        var status = await service.GetUsageAllowanceStatusAsync(device.Id, now);

        Assert.True(status.IsExhausted);
    }

    [Fact]
    public async Task GetUsageAllowanceStatusAsync_BlankDeviceUsageTier_ResolvesAsFree()
    {
        // A device row created before this migration's C# default took
        // effect (e.g. via a direct DB write) — must not crash or resolve
        // to an unbounded plan.
        var db = NewDb();
        var device = Dev(tier: "");
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        var opts = new UsageTiersOptions
        {
            Enabled = true,
            Plans = new() { new UsageTierPlan { Name = "free", QuestionsPerDay = 30 } },
        };
        var service = NewService(db, opts);

        var status = await service.GetUsageAllowanceStatusAsync(device.Id, DateTime.UtcNow);

        Assert.Equal("free", status.Tier);
        Assert.Equal(30, status.AllowanceToday);
    }

    [Fact]
    public async Task GetUsageAllowanceStatusAsync_HonorsDevicesNonFreeTier()
    {
        var db = NewDb();
        var device = Dev(tier: "family");
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        var opts = new UsageTiersOptions
        {
            Enabled = true,
            Plans = new()
            {
                new UsageTierPlan { Name = "free", QuestionsPerDay = 30 },
                new UsageTierPlan { Name = "family", QuestionsPerDay = 100 },
            },
        };
        var service = NewService(db, opts);

        var status = await service.GetUsageAllowanceStatusAsync(device.Id, DateTime.UtcNow);

        Assert.Equal("family", status.Tier);
        Assert.Equal(100, status.AllowanceToday);
    }
}

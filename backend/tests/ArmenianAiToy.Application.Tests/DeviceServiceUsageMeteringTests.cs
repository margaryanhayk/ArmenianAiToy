using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.Data.Sqlite;
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

    /// <summary>
    /// Deterministically reproduces the IX_DeviceUsageDays_DeviceId_DayUtc
    /// race described in the review finding: on its FIRST SaveChangesAsync
    /// (the one carrying the new-row Add), sneaks in a conflicting row for
    /// the SAME (DeviceId, DayUtc) via a separate <see cref="AppDbContext"/>
    /// — exactly what a second concurrent turn's own
    /// <c>RecordUsageQuestionAsync</c> call landing a moment earlier would
    /// do — so the context-under-test's own insert collides with a REAL,
    /// already-committed row instead of relying on genuine thread timing
    /// (which would make this test flaky).
    /// </summary>
    private sealed class RaceInjectingDbContext : AppDbContext
    {
        private readonly DbContextOptions<AppDbContext> _options;
        private bool _injected;

        public RaceInjectingDbContext(DbContextOptions<AppDbContext> options) : base(options)
            => _options = options;

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_injected)
            {
                var added = ChangeTracker.Entries<DeviceUsageDay>()
                    .FirstOrDefault(e => e.State == EntityState.Added);
                if (added is not null)
                {
                    _injected = true;
                    await using var racer = new AppDbContext(_options);
                    racer.Set<DeviceUsageDay>().Add(new DeviceUsageDay
                    {
                        Id = Guid.NewGuid(),
                        DeviceId = added.Entity.DeviceId,
                        DayUtc = added.Entity.DayUtc,
                        Questions = 5,
                        EstimatedUsd = 0.05m,
                    });
                    await racer.SaveChangesAsync(cancellationToken);
                }
            }
            return await base.SaveChangesAsync(cancellationToken);
        }
    }

    // KEYSTONE: mirrors ReportStoryPlaysAsync/ReportGamePlaysAsync's own
    // "two concurrent writers both found no row" race, but for an
    // increment-in-place upsert rather than an insert-dedup — a lost write
    // here silently undercounts the day's quota rather than just skipping a
    // harmless duplicate. Pins that RecordUsageQuestionAsync retries and
    // BOTH the racing writer's question and this call's own question land.
    [Fact]
    public async Task RecordUsageQuestionAsync_ConcurrentInsertRace_RetriesAndBothLand()
    {
        // Real SQLite, not the in-memory provider: only a relational
        // provider actually enforces IX_DeviceUsageDays_DeviceId_DayUtc, so
        // only it can reproduce the SaveChangesAsync failure the fix
        // handles.
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
            var seedDb = new AppDbContext(options);
            await seedDb.Database.EnsureCreatedAsync();
            var device = Dev();
            seedDb.Devices.Add(device);
            await seedDb.SaveChangesAsync();

            var dbUnderTest = new RaceInjectingDbContext(options);
            var service = new DeviceService(dbUnderTest, Substitute.For<ILogger<DeviceService>>());
            var now = DateTime.UtcNow;

            await service.RecordUsageQuestionAsync(device.Id, 0.01m, now);

            var verifyDb = new AppDbContext(options);
            var row = await verifyDb.Set<DeviceUsageDay>()
                .SingleAsync(u => u.DeviceId == device.Id);
            // The racing writer's 5 questions / $0.05 AND this call's own
            // +1 / +$0.01 must both be present — the old, unguarded
            // SaveChangesAsync would have thrown here, the caller's
            // best-effort try/catch would have swallowed it, and this
            // call's question would have been silently dropped.
            Assert.Equal(6, row.Questions);
            Assert.Equal(0.06m, row.EstimatedUsd);
        }
        finally
        {
            await conn.DisposeAsync();
        }
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

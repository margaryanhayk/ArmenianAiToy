using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for the usage-tier metering foundation's parent-facing half:
/// <c>LinkedDeviceDto.Usage</c>, populated by
/// <c>ParentService.GetLinkedDeviceDetailsAsync</c> only when
/// <c>Usage:Tiers:Enabled</c> is true (2026-09-11). Same TestDbContext
/// shape as <c>ParentServiceGetLinkedDeviceDetailsTests</c>, plus
/// <see cref="DeviceUsageDay"/>.
/// </summary>
public class ParentServiceUsageSummaryTests
{
    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Parent>().HasKey(p => p.Id);
            modelBuilder.Entity<Parent>().Ignore(p => p.ParentDevices);

            modelBuilder.Entity<Device>().HasKey(d => d.Id);
            modelBuilder.Entity<Device>().Ignore(d => d.Conversations);
            modelBuilder.Entity<Device>().Ignore(d => d.ParentDevices);

            modelBuilder.Entity<ParentDevice>().HasKey(pd => new { pd.ParentId, pd.DeviceId });

            modelBuilder.Entity<Child>().HasKey(c => c.Id);
            modelBuilder.Entity<Child>().Ignore(c => c.Device);
            modelBuilder.Entity<Child>().Ignore(c => c.Conversations);

            modelBuilder.Entity<Conversation>().HasKey(c => c.Id);
            modelBuilder.Entity<Conversation>().Ignore(c => c.Device);
            modelBuilder.Entity<Conversation>().Ignore(c => c.Child);
            modelBuilder.Entity<Conversation>().Ignore(c => c.Messages);

            modelBuilder.Entity<DeviceContentOverride>().HasKey(o => o.Id);
            modelBuilder.Entity<DeviceContentOverride>().Ignore(o => o.Device);

            modelBuilder.Entity<DeviceUsageDay>().HasKey(u => u.Id);
            modelBuilder.Entity<DeviceUsageDay>().Ignore(u => u.Device);
        }
    }

    private static (ParentService Service, TestDbContext Db) CreateService(bool usageTiersEnabled)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TestDbContext(options);
        var config = Substitute.For<IConfiguration>();
        var logger = Substitute.For<ILogger<ParentService>>();
        var usageTiers = new UsageTiersOptions
        {
            Enabled = usageTiersEnabled,
            Plans = new() { new UsageTierPlan { Name = "free", QuestionsPerDay = 30 } },
        };
        return (new ParentService(db, config, logger, usageTiersOptions: usageTiers), db);
    }

    private static Device NewDevice(string tier = "free")
        => new()
        {
            Id = Guid.NewGuid(),
            MacAddress = Guid.NewGuid().ToString()[..17],
            Name = "Bench",
            ApiKey = Guid.NewGuid().ToString(),
            LastSeenAt = DateTime.UtcNow,
            RegisteredAt = DateTime.UtcNow.AddDays(-1),
            UsageTier = tier,
        };

    [Fact]
    public async Task FlagOff_UsageIsNull()
    {
        var (service, db) = CreateService(usageTiersEnabled: false);
        var parentId = Guid.NewGuid();
        var device = NewDevice();
        db.Set<Parent>().Add(new Parent { Id = parentId, Email = "a@b.com", PasswordHash = "x", RegisteredAt = DateTime.UtcNow });
        db.Set<Device>().Add(device);
        db.Set<ParentDevice>().Add(new ParentDevice { ParentId = parentId, DeviceId = device.Id, LinkedAt = DateTime.UtcNow });
        db.Set<DeviceUsageDay>().Add(new DeviceUsageDay { Id = Guid.NewGuid(), DeviceId = device.Id, DayUtc = DateTime.UtcNow.Date, Questions = 5 });
        await db.SaveChangesAsync();

        var result = await service.GetLinkedDeviceDetailsAsync(parentId);

        Assert.Null(Assert.Single(result).Usage);
    }

    [Fact]
    public async Task FlagOn_UsagePopulated_WithTodaysCountAndAllowance()
    {
        var (service, db) = CreateService(usageTiersEnabled: true);
        var parentId = Guid.NewGuid();
        var device = NewDevice();
        var today = DateTime.UtcNow.Date;
        db.Set<Parent>().Add(new Parent { Id = parentId, Email = "a@b.com", PasswordHash = "x", RegisteredAt = DateTime.UtcNow });
        db.Set<Device>().Add(device);
        db.Set<ParentDevice>().Add(new ParentDevice { ParentId = parentId, DeviceId = device.Id, LinkedAt = DateTime.UtcNow });
        db.Set<DeviceUsageDay>().Add(new DeviceUsageDay { Id = Guid.NewGuid(), DeviceId = device.Id, DayUtc = today, Questions = 7 });
        // A different day's row must not leak into "today".
        db.Set<DeviceUsageDay>().Add(new DeviceUsageDay { Id = Guid.NewGuid(), DeviceId = device.Id, DayUtc = today.AddDays(-1), Questions = 20 });
        await db.SaveChangesAsync();

        var result = await service.GetLinkedDeviceDetailsAsync(parentId);

        var usage = Assert.Single(result).Usage;
        Assert.NotNull(usage);
        Assert.Equal("free", usage!.Tier);
        Assert.Equal(7, usage.QuestionsToday);
        Assert.Equal(30, usage.AllowanceToday);
    }

    [Fact]
    public async Task FlagOn_NoUsageRowsYet_QuestionsTodayIsZero()
    {
        var (service, db) = CreateService(usageTiersEnabled: true);
        var parentId = Guid.NewGuid();
        var device = NewDevice();
        db.Set<Parent>().Add(new Parent { Id = parentId, Email = "a@b.com", PasswordHash = "x", RegisteredAt = DateTime.UtcNow });
        db.Set<Device>().Add(device);
        db.Set<ParentDevice>().Add(new ParentDevice { ParentId = parentId, DeviceId = device.Id, LinkedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var result = await service.GetLinkedDeviceDetailsAsync(parentId);

        var usage = Assert.Single(result).Usage;
        Assert.NotNull(usage);
        Assert.Equal(0, usage!.QuestionsToday);
    }

    [Fact]
    public async Task FlagOn_BlankDeviceTier_ResolvesAsFreeInSummary()
    {
        var (service, db) = CreateService(usageTiersEnabled: true);
        var parentId = Guid.NewGuid();
        var device = NewDevice(tier: "");
        db.Set<Parent>().Add(new Parent { Id = parentId, Email = "a@b.com", PasswordHash = "x", RegisteredAt = DateTime.UtcNow });
        db.Set<Device>().Add(device);
        db.Set<ParentDevice>().Add(new ParentDevice { ParentId = parentId, DeviceId = device.Id, LinkedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var result = await service.GetLinkedDeviceDetailsAsync(parentId);

        Assert.Equal("free", Assert.Single(result).Usage!.Tier);
    }
}

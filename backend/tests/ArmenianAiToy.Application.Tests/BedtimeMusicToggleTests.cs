using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Slice E bedtime-music toggle: enabling requires a configured bedtime
/// window (music only plays inside it), disabling is always allowed,
/// ownership-gated, audited on real flips.
/// </summary>
public class BedtimeMusicToggleTests
{
    private sealed class TestDb : DbContext
    {
        public TestDb(DbContextOptions<TestDb> o) : base(o) { }
        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<Device>(e =>
            {
                e.HasKey(d => d.Id);
                e.Ignore(d => d.Conversations);
                e.Ignore(d => d.ParentDevices);
            });
            mb.Entity<ParentDevice>(e =>
            {
                e.HasKey(pd => new { pd.ParentId, pd.DeviceId });
                e.Ignore(pd => pd.Parent);
                e.Ignore(pd => pd.Device);
            });
            mb.Entity<AuditEvent>(e => e.HasKey(a => a.Id));
        }
    }

    private static (ParentService Service, TestDb Db) Create()
    {
        var db = new TestDb(new DbContextOptionsBuilder<TestDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var svc = new ParentService(db, new ConfigurationBuilder().Build(),
            Substitute.For<ILogger<ParentService>>());
        return (svc, db);
    }

    private static async Task<(Guid ParentId, Guid DeviceId)> SeedAsync(
        TestDb db, bool withBedtime)
    {
        var parentId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        db.Add(new Device
        {
            Id = deviceId, MacAddress = "m", Name = "toy",
            BedtimeStart = withBedtime ? new TimeOnly(21, 0) : null,
            BedtimeEnd = withBedtime ? new TimeOnly(7, 0) : null,
        });
        db.Add(new ParentDevice { ParentId = parentId, DeviceId = deviceId });
        await db.SaveChangesAsync();
        return (parentId, deviceId);
    }

    // KEYSTONE: enabling with no bedtime window is refused — the toggle
    // would otherwise look on but never fire.
    [Fact]
    public async Task Enable_NoBedtimeWindow_Refused_NoMutation()
    {
        var (svc, db) = Create();
        var (parentId, deviceId) = await SeedAsync(db, withBedtime: false);

        var result = await svc.SetBedtimeMusicAsync(parentId, deviceId, enabled: true);

        Assert.Equal(BedtimeMusicSetResult.NoBedtimeWindow, result);
        Assert.False((await db.Set<Device>().SingleAsync()).BedtimeMusicEnabled);
        Assert.Empty(await db.Set<AuditEvent>().ToListAsync());
    }

    [Fact]
    public async Task Enable_WithBedtimeWindow_Persists_AndAudits()
    {
        var (svc, db) = Create();
        var (parentId, deviceId) = await SeedAsync(db, withBedtime: true);

        var result = await svc.SetBedtimeMusicAsync(parentId, deviceId, enabled: true);

        Assert.Equal(BedtimeMusicSetResult.Ok, result);
        Assert.True((await db.Set<Device>().SingleAsync()).BedtimeMusicEnabled);
        Assert.Single(await db.Set<AuditEvent>().ToListAsync());
    }

    // Disabling is always allowed, even after the window was cleared.
    [Fact]
    public async Task Disable_NoWindow_Allowed()
    {
        var (svc, db) = Create();
        var (parentId, deviceId) = await SeedAsync(db, withBedtime: false);
        (await db.Set<Device>().SingleAsync()).BedtimeMusicEnabled = true;
        await db.SaveChangesAsync();

        var result = await svc.SetBedtimeMusicAsync(parentId, deviceId, enabled: false);

        Assert.Equal(BedtimeMusicSetResult.Ok, result);
        Assert.False((await db.Set<Device>().SingleAsync()).BedtimeMusicEnabled);
    }

    [Fact]
    public async Task NotLinked_ReturnsNotLinked()
    {
        var (svc, db) = Create();
        var (_, deviceId) = await SeedAsync(db, withBedtime: true);

        var result = await svc.SetBedtimeMusicAsync(Guid.NewGuid(), deviceId, true);

        Assert.Equal(BedtimeMusicSetResult.NotLinked, result);
    }

    // KEYSTONE (symmetry): clearing the bedtime window auto-disables
    // bedtime music, so it can never be left "on" but inert.
    [Fact]
    public async Task ClearingBedtimeWindow_AutoDisablesMusic()
    {
        var (svc, db) = Create();
        var (parentId, deviceId) = await SeedAsync(db, withBedtime: true);
        Assert.Equal(BedtimeMusicSetResult.Ok,
            await svc.SetBedtimeMusicAsync(parentId, deviceId, enabled: true));

        // Clear the window (both null).
        var ok = await svc.SetBedtimeWindowAsync(parentId, deviceId, null, null);

        Assert.True(ok);
        Assert.False((await db.Set<Device>().SingleAsync()).BedtimeMusicEnabled);
    }

    [Fact]
    public async Task SettingAWindow_DoesNotDisableMusic()
    {
        var (svc, db) = Create();
        var (parentId, deviceId) = await SeedAsync(db, withBedtime: true);
        await svc.SetBedtimeMusicAsync(parentId, deviceId, enabled: true);

        // Change (not clear) the window — music must survive.
        await svc.SetBedtimeWindowAsync(parentId, deviceId,
            new TimeOnly(22, 0), new TimeOnly(6, 0));

        Assert.True((await db.Set<Device>().SingleAsync()).BedtimeMusicEnabled);
    }
}

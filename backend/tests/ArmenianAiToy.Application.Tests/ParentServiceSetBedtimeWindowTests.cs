using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <c>ParentService.SetBedtimeWindowAsync</c> (B4). Mirrors the
/// silent-false ownership shape of <c>SetDevicePauseStateAsync</c>. Half-
/// null is deliberately accepted and normalized to disabled so the write
/// endpoint is idempotent for clearing.
/// </summary>
public class ParentServiceSetBedtimeWindowTests
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
            modelBuilder.Entity<AuditEvent>().HasKey(a => a.Id);
        }
    }

    private static (ParentService Service, TestDbContext Db) CreateService()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TestDbContext(options);
        var config = Substitute.For<IConfiguration>();
        config["Jwt:Key"].Returns("TestSecretKeyThatIsLongEnoughForHmacSha256Validation!");
        var logger = Substitute.For<ILogger<ParentService>>();
        return (new ParentService(db, config, logger), db);
    }

    private static (Guid ParentId, Guid DeviceId) SeedLinkedParentAndDevice(TestDbContext db)
    {
        var parentId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "p@example.com",
            PasswordHash = "hash",
            RegisteredAt = DateTime.UtcNow
        });
        db.Set<Device>().Add(new Device
        {
            Id = deviceId,
            MacAddress = "aa:bb",
            Name = "Test",
            ApiKey = "dtk_test",
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        });
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId,
            DeviceId = deviceId,
            LinkedAt = DateTime.UtcNow
        });
        db.SaveChanges();
        return (parentId, deviceId);
    }

    [Fact]
    public async Task SetBedtimeWindowAsync_OwnedDevice_PersistsBothTimes()
    {
        var (service, db) = CreateService();
        var (parentId, deviceId) = SeedLinkedParentAndDevice(db);

        var result = await service.SetBedtimeWindowAsync(
            parentId, deviceId, new TimeOnly(22, 0), new TimeOnly(7, 0));

        Assert.True(result);
        var reloaded = await db.Set<Device>().FindAsync(deviceId);
        Assert.Equal(new TimeOnly(22, 0), reloaded!.BedtimeStart);
        Assert.Equal(new TimeOnly(7, 0), reloaded.BedtimeEnd);
    }

    [Fact]
    public async Task SetBedtimeWindowAsync_DeviceNotLinked_ReturnsFalseAndDoesNotMutate()
    {
        var (service, db) = CreateService();
        var (_, deviceId) = SeedLinkedParentAndDevice(db);
        var strangerParentId = Guid.NewGuid();

        var result = await service.SetBedtimeWindowAsync(
            strangerParentId, deviceId, new TimeOnly(22, 0), new TimeOnly(7, 0));

        Assert.False(result);
        var reloaded = await db.Set<Device>().FindAsync(deviceId);
        Assert.Null(reloaded!.BedtimeStart);
        Assert.Null(reloaded.BedtimeEnd);
    }

    [Fact]
    public async Task SetBedtimeWindowAsync_BothNull_DisablesWindow()
    {
        var (service, db) = CreateService();
        var (parentId, deviceId) = SeedLinkedParentAndDevice(db);
        // Prime with a window first.
        await service.SetBedtimeWindowAsync(
            parentId, deviceId, new TimeOnly(22, 0), new TimeOnly(7, 0));

        var result = await service.SetBedtimeWindowAsync(parentId, deviceId, null, null);

        Assert.True(result);
        var reloaded = await db.Set<Device>().FindAsync(deviceId);
        Assert.Null(reloaded!.BedtimeStart);
        Assert.Null(reloaded.BedtimeEnd);
    }

    [Fact]
    public async Task SetBedtimeWindowAsync_HalfNull_NormalizesToDisabled()
    {
        // Half-null is accepted (not a 400). We normalize to "disabled"
        // rather than persisting an incoherent half-window.
        var (service, db) = CreateService();
        var (parentId, deviceId) = SeedLinkedParentAndDevice(db);
        await service.SetBedtimeWindowAsync(
            parentId, deviceId, new TimeOnly(22, 0), new TimeOnly(7, 0));

        var result = await service.SetBedtimeWindowAsync(
            parentId, deviceId, new TimeOnly(22, 0), null);

        Assert.True(result);
        var reloaded = await db.Set<Device>().FindAsync(deviceId);
        Assert.Null(reloaded!.BedtimeStart);
        Assert.Null(reloaded.BedtimeEnd);
    }

    [Fact]
    public async Task SetBedtimeWindowAsync_SuccessPath_DoesNotWriteAuditRow()
    {
        // B4 is deliberately log-only in slice 1; audit-scope expansion for
        // parental-control config changes is a separate later decision.
        var (service, db) = CreateService();
        var (parentId, deviceId) = SeedLinkedParentAndDevice(db);

        Assert.True(await service.SetBedtimeWindowAsync(
            parentId, deviceId, new TimeOnly(22, 0), new TimeOnly(7, 0)));

        Assert.Empty(await db.Set<AuditEvent>().ToListAsync());
    }
}

using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <c>ParentService.SetDevicePauseStateAsync</c> (B3). Enforces
/// parent-device ownership before mutating the pause flag: a parent can
/// only pause/resume devices they have a <c>ParentDevice</c> link to.
/// Mirrors the silent-false shape of <c>UnlinkDeviceAsync</c>.
/// </summary>
public class ParentServiceSetDevicePauseStateTests
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

    private static (Guid ParentId, Guid DeviceId) SeedLinkedParentAndDevice(TestDbContext db, bool isPaused = false)
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
            LastSeenAt = DateTime.UtcNow,
            IsPaused = isPaused
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
    public async Task SetDevicePauseStateAsync_OwnedDevice_TogglesPauseAndPersists()
    {
        var (service, db) = CreateService();
        var (parentId, deviceId) = SeedLinkedParentAndDevice(db, isPaused: false);

        var result = await service.SetDevicePauseStateAsync(parentId, deviceId, paused: true);

        Assert.True(result);
        var reloaded = await db.Set<Device>().FindAsync(deviceId);
        Assert.True(reloaded!.IsPaused);
    }

    [Fact]
    public async Task SetDevicePauseStateAsync_DeviceExistsButNotLinked_ReturnsFalseAndDoesNotMutate()
    {
        var (service, db) = CreateService();
        var (_, deviceId) = SeedLinkedParentAndDevice(db, isPaused: false);
        var strangerParentId = Guid.NewGuid();

        var result = await service.SetDevicePauseStateAsync(strangerParentId, deviceId, paused: true);

        Assert.False(result);
        var reloaded = await db.Set<Device>().FindAsync(deviceId);
        Assert.False(reloaded!.IsPaused);
    }

    [Fact]
    public async Task SetDevicePauseStateAsync_UnknownDevice_ReturnsFalse()
    {
        var (service, db) = CreateService();
        var parentId = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "p@example.com",
            PasswordHash = "hash",
            RegisteredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await service.SetDevicePauseStateAsync(parentId, Guid.NewGuid(), paused: true);

        Assert.False(result);
    }

    [Fact]
    public async Task SetDevicePauseStateAsync_AlreadyInRequestedState_ReturnsTrueIdempotently()
    {
        var (service, db) = CreateService();
        var (parentId, deviceId) = SeedLinkedParentAndDevice(db, isPaused: true);

        var result = await service.SetDevicePauseStateAsync(parentId, deviceId, paused: true);

        Assert.True(result);
        var reloaded = await db.Set<Device>().FindAsync(deviceId);
        Assert.True(reloaded!.IsPaused);
    }
}

using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <c>ParentService.SetDeviceModeFlagsAsync</c> (B5). Mirrors the
/// silent-false ownership shape of the pause/bedtime setters. Full-
/// replacement — every call supplies all four bools.
/// </summary>
public class ParentServiceSetDeviceModeFlagsTests
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
            // StoryEnabled / GameEnabled / RiddleEnabled / CuriosityEnabled
            // default to true via the C# initializer on Device — mirrors the
            // migration's defaultValue: true for existing rows.
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
    public async Task SetDeviceModeFlagsAsync_OwnedDevice_PersistsAllFourFlags()
    {
        var (service, db) = CreateService();
        var (parentId, deviceId) = SeedLinkedParentAndDevice(db);

        var result = await service.SetDeviceModeFlagsAsync(
            parentId, deviceId, story: false, game: true, riddle: false, curiosity: true);

        Assert.True(result);
        var reloaded = await db.Set<Device>().FindAsync(deviceId);
        Assert.False(reloaded!.StoryEnabled);
        Assert.True(reloaded.GameEnabled);
        Assert.False(reloaded.RiddleEnabled);
        Assert.True(reloaded.CuriosityEnabled);
    }

    [Fact]
    public async Task SetDeviceModeFlagsAsync_DeviceNotLinked_ReturnsFalseAndDoesNotMutate()
    {
        var (service, db) = CreateService();
        var (_, deviceId) = SeedLinkedParentAndDevice(db);
        var strangerParentId = Guid.NewGuid();

        var result = await service.SetDeviceModeFlagsAsync(
            strangerParentId, deviceId, story: false, game: false, riddle: false, curiosity: false);

        Assert.False(result);
        var reloaded = await db.Set<Device>().FindAsync(deviceId);
        // All four should still be at their default-true state.
        Assert.True(reloaded!.StoryEnabled);
        Assert.True(reloaded.GameEnabled);
        Assert.True(reloaded.RiddleEnabled);
        Assert.True(reloaded.CuriosityEnabled);
    }

    [Fact]
    public async Task SetDeviceModeFlagsAsync_Success_WritesAuditRowWithAllFourFlags()
    {
        var (service, db) = CreateService();
        var (parentId, deviceId) = SeedLinkedParentAndDevice(db);

        Assert.True(await service.SetDeviceModeFlagsAsync(
            parentId, deviceId, story: false, game: true, riddle: false, curiosity: true));

        var audits = await db.Set<AuditEvent>().ToListAsync();
        var audit = Assert.Single(audits);
        Assert.Equal(AuditEventType.ParentDeviceModeFlagsSet, audit.EventType);
        Assert.Equal(parentId, audit.ActorParentId);
        Assert.Equal(deviceId, audit.TargetDeviceId);
        Assert.Null(audit.TargetChildId);
        Assert.NotNull(audit.Metadata);
        Assert.Contains("\"story\":false", audit.Metadata);
        Assert.Contains("\"game\":true", audit.Metadata);
        Assert.Contains("\"riddle\":false", audit.Metadata);
        Assert.Contains("\"curiosity\":true", audit.Metadata);
    }

    [Fact]
    public async Task SetDeviceModeFlagsAsync_OwnershipMiss_DoesNotWriteAuditRow()
    {
        var (service, db) = CreateService();
        var (_, deviceId) = SeedLinkedParentAndDevice(db);

        Assert.False(await service.SetDeviceModeFlagsAsync(
            Guid.NewGuid(), deviceId, story: false, game: false, riddle: false, curiosity: false));

        Assert.Empty(await db.Set<AuditEvent>().ToListAsync());
    }
}

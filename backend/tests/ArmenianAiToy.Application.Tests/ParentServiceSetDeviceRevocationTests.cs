using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// #074 — tests for <c>ParentService.SetDeviceRevocationAsync</c>: parent-device
/// ownership before flipping the server-side credential kill-switch, idempotent
/// no-op, and the audit row on an actual flip. Mirrors the pause-state shape.
/// </summary>
public class ParentServiceSetDeviceRevocationTests
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

    private static (Guid ParentId, Guid DeviceId) SeedLinkedParentAndDevice(
        TestDbContext db, bool isRevoked = false)
    {
        var parentId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId, Email = "p@example.com", PasswordHash = "hash",
            RegisteredAt = DateTime.UtcNow
        });
        db.Set<Device>().Add(new Device
        {
            Id = deviceId, MacAddress = "aa:bb", Name = "Test", ApiKey = "dtk_test",
            RegisteredAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow, IsRevoked = isRevoked
        });
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId, DeviceId = deviceId, LinkedAt = DateTime.UtcNow
        });
        db.SaveChanges();
        return (parentId, deviceId);
    }

    [Fact]
    public async Task OwnedDevice_RevokesAndRestores_Persists()
    {
        var (service, db) = CreateService();
        var (parentId, deviceId) = SeedLinkedParentAndDevice(db, isRevoked: false);

        Assert.True(await service.SetDeviceRevocationAsync(parentId, deviceId, revoked: true));
        Assert.True((await db.Set<Device>().FindAsync(deviceId))!.IsRevoked);

        Assert.True(await service.SetDeviceRevocationAsync(parentId, deviceId, revoked: false));
        Assert.False((await db.Set<Device>().FindAsync(deviceId))!.IsRevoked);
    }

    [Fact]
    public async Task DeviceExistsButNotLinked_ReturnsFalseAndDoesNotMutate()
    {
        var (service, db) = CreateService();
        var (_, deviceId) = SeedLinkedParentAndDevice(db, isRevoked: false);

        var result = await service.SetDeviceRevocationAsync(Guid.NewGuid(), deviceId, revoked: true);

        Assert.False(result);
        Assert.False((await db.Set<Device>().FindAsync(deviceId))!.IsRevoked);
    }

    [Fact]
    public async Task UnknownDevice_ReturnsFalse()
    {
        var (service, db) = CreateService();
        var parentId = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId, Email = "p@example.com", PasswordHash = "hash",
            RegisteredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.False(await service.SetDeviceRevocationAsync(parentId, Guid.NewGuid(), revoked: true));
    }

    [Fact]
    public async Task ActualStateChange_WritesAuditRow()
    {
        var (service, db) = CreateService();
        var (parentId, deviceId) = SeedLinkedParentAndDevice(db, isRevoked: false);

        Assert.True(await service.SetDeviceRevocationAsync(parentId, deviceId, revoked: true));

        var audit = Assert.Single(await db.Set<AuditEvent>().ToListAsync());
        Assert.Equal(AuditEventType.ParentDeviceRevocationChanged, audit.EventType);
        Assert.Equal(parentId, audit.ActorParentId);
        Assert.Equal(deviceId, audit.TargetDeviceId);
        Assert.Null(audit.TargetChildId);
        Assert.Contains("\"is_revoked\":true", audit.Metadata);
    }

    [Fact]
    public async Task OwnershipMiss_DoesNotWriteAuditRow()
    {
        var (service, db) = CreateService();
        var (_, deviceId) = SeedLinkedParentAndDevice(db, isRevoked: false);

        Assert.False(await service.SetDeviceRevocationAsync(Guid.NewGuid(), deviceId, revoked: true));

        Assert.Empty(await db.Set<AuditEvent>().ToListAsync());
    }

    [Fact]
    public async Task IdempotentNoChange_ReturnsTrue_DoesNotWriteAuditRow()
    {
        var (service, db) = CreateService();
        var (parentId, deviceId) = SeedLinkedParentAndDevice(db, isRevoked: true);

        Assert.True(await service.SetDeviceRevocationAsync(parentId, deviceId, revoked: true));

        Assert.Empty(await db.Set<AuditEvent>().ToListAsync());
    }
}

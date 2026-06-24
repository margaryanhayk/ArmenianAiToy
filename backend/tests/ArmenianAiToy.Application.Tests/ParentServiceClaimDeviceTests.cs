using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Phase A.2 — ParentService.ClaimDeviceAsync (consumer QR pairing). Pins:
/// a valid claim links + consumes the code + audits; every failure reason
/// returns false (uniform, no-existence-leak); single-use enforcement.
/// </summary>
public class ParentServiceClaimDeviceTests
{
    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Parent>().HasKey(p => p.Id);
            b.Entity<Parent>().Ignore(p => p.ParentDevices);
            b.Entity<Device>().HasKey(d => d.Id);
            b.Entity<Device>().Ignore(d => d.Conversations);
            b.Entity<Device>().Ignore(d => d.ParentDevices);
            b.Entity<ParentDevice>().HasKey(pd => new { pd.ParentId, pd.DeviceId });
            b.Entity<AuditEvent>().HasKey(a => a.Id);
        }
    }

    private const string Code = "AREG-CLAIM-7H3K-9QXZ";

    private static (ParentService Service, TestDbContext Db) CreateService()
    {
        var db = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var config = Substitute.For<IConfiguration>();
        return (new ParentService(db, config, Substitute.For<ILogger<ParentService>>()), db);
    }

    private static (Guid ParentId, Guid DeviceId) Seed(
        TestDbContext db, string? claimCode = Code)
    {
        var parentId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId, Email = "p@x.com", PasswordHash = "x", RegisteredAt = DateTime.UtcNow
        });
        db.Set<Device>().Add(new Device
        {
            Id = deviceId, MacAddress = "aa:bb", Name = "Toy",
            RegisteredAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
            ClaimCodeHash = claimCode is null ? null : DeviceApiKeyHasher.Hash(claimCode)
        });
        db.SaveChanges();
        return (parentId, deviceId);
    }

    [Fact]
    public async Task ValidCode_Links_ConsumesCode_StampsClaimedAt_Audits()
    {
        var (service, db) = CreateService();
        var (parentId, deviceId) = Seed(db);

        Assert.True(await service.ClaimDeviceAsync(parentId, deviceId, Code));

        Assert.True(await db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == parentId && pd.DeviceId == deviceId));
        var device = await db.Set<Device>().FindAsync(deviceId);
        Assert.Null(device!.ClaimCodeHash);          // consumed (single-use)
        Assert.NotNull(device.ClaimedAt);
        var audit = Assert.Single(await db.Set<AuditEvent>().ToListAsync());
        Assert.Equal(AuditEventType.ParentDeviceClaimed, audit.EventType);
        Assert.Equal(parentId, audit.ActorParentId);
        Assert.Equal(deviceId, audit.TargetDeviceId);
    }

    [Fact]
    public async Task WrongCode_Fails_NoLink_NoConsume_NoAudit()
    {
        var (service, db) = CreateService();
        var (parentId, deviceId) = Seed(db);

        Assert.False(await service.ClaimDeviceAsync(parentId, deviceId, "WRONG-CODE"));

        Assert.False(await db.Set<ParentDevice>().AnyAsync());
        var device = await db.Set<Device>().FindAsync(deviceId);
        Assert.NotNull(device!.ClaimCodeHash);        // NOT consumed
        Assert.Null(device.ClaimedAt);
        Assert.Empty(await db.Set<AuditEvent>().ToListAsync());
    }

    [Fact]
    public async Task UnknownDevice_Fails()
    {
        var (service, db) = CreateService();
        var (parentId, _) = Seed(db);
        Assert.False(await service.ClaimDeviceAsync(parentId, Guid.NewGuid(), Code));
    }

    [Fact]
    public async Task DeviceWithNoClaimCode_Fails()
    {
        // Legacy/bench device (never minted a claim code) is not claimable.
        var (service, db) = CreateService();
        var (parentId, deviceId) = Seed(db, claimCode: null);
        Assert.False(await service.ClaimDeviceAsync(parentId, deviceId, Code));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyCode_Fails(string code)
    {
        var (service, db) = CreateService();
        var (parentId, deviceId) = Seed(db);
        Assert.False(await service.ClaimDeviceAsync(parentId, deviceId, code));
    }

    [Fact]
    public async Task SingleUse_SecondClaimWithSameCode_Fails()
    {
        var (service, db) = CreateService();
        var (parentId, deviceId) = Seed(db);

        Assert.True(await service.ClaimDeviceAsync(parentId, deviceId, Code));   // consumes
        // Same code again (e.g. a thief who photographed the QR) → rejected.
        Assert.False(await service.ClaimDeviceAsync(Guid.NewGuid(), deviceId, Code));
    }
}

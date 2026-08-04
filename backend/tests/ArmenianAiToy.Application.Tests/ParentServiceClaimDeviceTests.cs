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
    public async Task ValidCode_Links_KeepsCode_StampsClaimedAt_Audits()
    {
        var (service, db) = CreateService();
        var (parentId, deviceId) = Seed(db);

        Assert.True(await service.ClaimDeviceAsync(parentId, deviceId, Code));

        Assert.True(await db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == parentId && pd.DeviceId == deviceId));
        var device = await db.Set<Device>().FindAsync(deviceId);
        // KEYSTONE: the code is NOT consumed. The QR is printed on the toy, so
        // it has to keep working — for a second parent, and for pairing the
        // toy again after an unlink. Clearing it here made unlink a one-way
        // door that no parent could reopen.
        Assert.NotNull(device!.ClaimCodeHash);
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
    public async Task SecondParent_CanClaimSameToy_WithTheSameCode()
    {
        var (service, db) = CreateService();
        var (mumId, deviceId) = Seed(db);
        var dadId = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = dadId, Email = "d@x.com", PasswordHash = "x", RegisteredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.True(await service.ClaimDeviceAsync(mumId, deviceId, Code));
        // Both parents in a household watch the same toy from their own
        // phones, and the only thing either of them has is the QR on the toy.
        Assert.True(await service.ClaimDeviceAsync(dadId, deviceId, Code));

        Assert.Equal(2, await db.Set<ParentDevice>().CountAsync(pd => pd.DeviceId == deviceId));
    }

    [Fact]
    public async Task ThirdParent_IsRefused_SeatLimitIsWhatProtectsTheCode()
    {
        var (service, db) = CreateService();
        var (mumId, deviceId) = Seed(db);
        var others = new[] { Guid.NewGuid(), Guid.NewGuid() };
        foreach (var id in others)
        {
            db.Set<Parent>().Add(new Parent
            {
                Id = id, Email = id + "@x.com", PasswordHash = "x", RegisteredAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();

        Assert.True(await service.ClaimDeviceAsync(mumId, deviceId, Code));
        Assert.True(await service.ClaimDeviceAsync(others[0], deviceId, Code));
        // KEYSTONE: the code stays valid forever, so the seat limit — not
        // secrecy — is what stops someone who photographed the QR of a toy
        // that is already owned.
        Assert.False(await service.ClaimDeviceAsync(others[1], deviceId, Code));

        Assert.Equal(ParentService.MaxParentsPerDevice,
            await db.Set<ParentDevice>().CountAsync(pd => pd.DeviceId == deviceId));
    }

    [Fact]
    public async Task ReClaimingAToyYouAlreadyHold_IsANoOpSuccess_AndTakesNoSecondSeat()
    {
        var (service, db) = CreateService();
        var (parentId, deviceId) = Seed(db);

        Assert.True(await service.ClaimDeviceAsync(parentId, deviceId, Code));
        // A parent who scans twice must not be told something went wrong.
        Assert.True(await service.ClaimDeviceAsync(parentId, deviceId, Code));

        Assert.Equal(1, await db.Set<ParentDevice>().CountAsync(pd => pd.DeviceId == deviceId));
    }

    [Fact]
    public async Task RevokedToy_IsNeverClaimable_SoAThiefCannotScanTheQrAndTakeIt()
    {
        var (service, db) = CreateService();
        var (parentId, deviceId) = Seed(db);
        var device = await db.Set<Device>().FindAsync(deviceId);
        device!.IsRevoked = true;
        await db.SaveChangesAsync();

        // KEYSTONE: revoke is the lost-or-stolen kill-switch. Now that the
        // code survives a claim, letting a revoked toy be claimed would hand
        // whoever holds it a way to reopen it by simply scanning the QR.
        Assert.False(await service.ClaimDeviceAsync(parentId, deviceId, Code));
        Assert.False(await db.Set<ParentDevice>().AnyAsync());
        Assert.Empty(await db.Set<AuditEvent>().ToListAsync());
    }
}

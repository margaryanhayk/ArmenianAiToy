using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Invites — the short-lived codes that let a SECOND parent join a toy without
/// its printed claim code.
///
/// <para>
/// The owner's condition when approving this was explicit: <b>a toy must never
/// end up with more than two parents.</b> Most of this file exists to hold that
/// line, and the seat tests are the ones to read first — a limit checked only
/// where it is obvious is a limit that gets bypassed somewhere else.
/// </para>
/// </summary>
public class ParentServiceDeviceInviteTests
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
            b.Entity<DeviceInvite>().HasKey(i => i.Id);
            b.Entity<DeviceInvite>().Ignore(i => i.Device);
        }
    }

    private sealed class RecordingNotifier : ArmenianAiToy.Application.Notifications.INotifier
    {
        public readonly List<(string Email, string DeviceName)> ToyJoined = new();
        public Task SendPasswordResetAsync(string e, string t, CancellationToken c = default)
            => Task.CompletedTask;
        public Task<bool> SendDormancyWarningAsync(string e, DateTime? d, CancellationToken c = default)
            => Task.FromResult(true);
        public Task SendEmailVerificationAsync(string e, string t, CancellationToken c = default)
            => Task.CompletedTask;
        public Task<bool> SendDormantDeviceWarningAsync(
            string e, string n, DateTime l, DateTime? d, CancellationToken c = default)
            => Task.FromResult(true);
        public Task SendToyJoinedByAnotherParentAsync(
            string parentEmail, string deviceName, CancellationToken c = default)
        {
            ToyJoined.Add((parentEmail, deviceName));
            return Task.CompletedTask;
        }
    }

    private static (ParentService Service, TestDbContext Db, RecordingNotifier Notifier)
        CreateService()
    {
        var db = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var notifier = new RecordingNotifier();
        var service = new ParentService(
            db, Substitute.For<IConfiguration>(),
            Substitute.For<ILogger<ParentService>>(),
            hashPassword: null, notifier: notifier);
        return (service, db, notifier);
    }

    private static Guid NewParent(TestDbContext db, string email)
    {
        var id = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = id, Email = email, PasswordHash = "x", RegisteredAt = DateTime.UtcNow
        });
        db.SaveChanges();
        return id;
    }

    /// <summary>A toy plus its first parent, linked.</summary>
    private static (Guid OwnerId, Guid DeviceId) SeedOwnedToy(TestDbContext db, bool revoked = false)
    {
        var ownerId = NewParent(db, "owner@x.com");
        var deviceId = Guid.NewGuid();
        db.Set<Device>().Add(new Device
        {
            Id = deviceId, MacAddress = "aa:bb", Name = "Toy",
            RegisteredAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
            IsRevoked = revoked
        });
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = ownerId, DeviceId = deviceId, LinkedAt = DateTime.UtcNow
        });
        db.SaveChanges();
        return (ownerId, deviceId);
    }

    // ---------------------------------------------------------------- issuing

    [Fact]
    public async Task Issue_ReturnsACodeOnce_AndStoresNeitherHalfInTheClear()
    {
        var (service, db, _) = CreateService();
        var (ownerId, deviceId) = SeedOwnedToy(db);

        var issued = await service.CreateDeviceInviteAsync(ownerId, deviceId);

        Assert.NotNull(issued);
        Assert.Equal(12, issued!.Code.Length);
        Assert.True(issued.ExpiresAt > DateTime.UtcNow);

        var row = Assert.Single(await db.Set<DeviceInvite>().ToListAsync());
        // KEYSTONE: the secret half is never recoverable from the row. The
        // selector is public by design (it is the lookup key); the rest must
        // survive a database dump, which is why it is PBKDF2 and not the plain
        // SHA-256 the password-reset tokens can afford.
        Assert.DoesNotContain(issued.Code[4..], row.SecretHash);
        Assert.Equal(issued.Code[..4], row.Selector);
        Assert.Null(row.ConsumedAt);
    }

    [Fact]
    public async Task Issue_NeverTouchesThePrintedClaimCode()
    {
        var (service, db, _) = CreateService();
        var (ownerId, deviceId) = SeedOwnedToy(db);
        var device = await db.Set<Device>().FindAsync(deviceId);
        device!.ClaimCodeHash = "the-hash-of-the-code-printed-on-the-box";
        await db.SaveChangesAsync();

        await service.CreateDeviceInviteAsync(ownerId, deviceId);

        // KEYSTONE: re-minting the claim code would kill the QR printed on the
        // toy — the trap that once made unlink a one-way door. An invite exists
        // precisely so that never has to happen.
        var after = await db.Set<Device>().FindAsync(deviceId);
        Assert.Equal("the-hash-of-the-code-printed-on-the-box", after!.ClaimCodeHash);
    }

    [Fact]
    public async Task Issue_ByAStranger_IsRefused()
    {
        var (service, db, _) = CreateService();
        var (_, deviceId) = SeedOwnedToy(db);
        var strangerId = NewParent(db, "stranger@x.com");

        Assert.Null(await service.CreateDeviceInviteAsync(strangerId, deviceId));
        Assert.Empty(await db.Set<DeviceInvite>().ToListAsync());
    }

    [Fact]
    public async Task Issue_ForARevokedToy_IsRefused()
    {
        var (service, db, _) = CreateService();
        var (ownerId, deviceId) = SeedOwnedToy(db, revoked: true);

        Assert.Null(await service.CreateDeviceInviteAsync(ownerId, deviceId));
    }

    [Fact]
    public async Task Issue_WritesAnAuditRowCarryingNoCode()
    {
        var (service, db, _) = CreateService();
        var (ownerId, deviceId) = SeedOwnedToy(db);

        var issued = await service.CreateDeviceInviteAsync(ownerId, deviceId);

        var audit = Assert.Single(await db.Set<AuditEvent>().ToListAsync());
        Assert.Equal(AuditEventType.ParentDeviceInviteCreated, audit.EventType);
        Assert.Equal(ownerId, audit.ActorParentId);
        Assert.Equal(deviceId, audit.TargetDeviceId);
        // An audit row that carried the code would hand a database reader the
        // invite itself.
        var blob = audit.Metadata ?? string.Empty;
        Assert.DoesNotContain(issued!.Code, blob);
        Assert.DoesNotContain(issued.Code[..4], blob);
    }

    // ------------------------------------------------------------- the limit

    [Fact]
    public async Task Issue_WhenDeviceAlreadyHasTwoParents_IsRefused()
    {
        var (service, db, _) = CreateService();
        var (ownerId, deviceId) = SeedOwnedToy(db);
        var secondId = NewParent(db, "second@x.com");
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = secondId, DeviceId = deviceId, LinkedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Handing out a code that could not be redeemed is its own small lie.
        Assert.Null(await service.CreateDeviceInviteAsync(ownerId, deviceId));
        Assert.Empty(await db.Set<DeviceInvite>().ToListAsync());
    }

    [Fact]
    public async Task Redeem_WhenSecondSeatTakenAfterIssue_IsRefused_AndInviteSurvives()
    {
        var (service, db, _) = CreateService();
        var (ownerId, deviceId) = SeedOwnedToy(db);
        var issued = await service.CreateDeviceInviteAsync(ownerId, deviceId);

        // The seat goes to somebody else AFTER the invite was issued — via the
        // printed code, say. Issue-time checking cannot see this.
        var secondId = NewParent(db, "second@x.com");
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = secondId, DeviceId = deviceId, LinkedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var thirdId = NewParent(db, "third@x.com");

        // KEYSTONE: this is the owner's condition. Two parents, never three.
        Assert.False(await service.RedeemDeviceInviteAsync(thirdId, issued!.Code));
        Assert.Equal(2, await db.Set<ParentDevice>().CountAsync(pd => pd.DeviceId == deviceId));

        // And the code is NOT burned: the seat may free up if someone unlinks,
        // and the joiner did not cause this race.
        var row = Assert.Single(await db.Set<DeviceInvite>().ToListAsync());
        Assert.Null(row.ConsumedAt);
    }

    [Fact]
    public async Task Redeem_TwoOutstandingInvites_OnlyTheFirstGetsTheSeat()
    {
        var (service, db, _) = CreateService();
        var (ownerId, deviceId) = SeedOwnedToy(db);
        var first = await service.CreateDeviceInviteAsync(ownerId, deviceId);
        var second = await service.CreateDeviceInviteAsync(ownerId, deviceId);
        Assert.NotNull(first);
        Assert.NotNull(second);

        var joinerA = NewParent(db, "a@x.com");
        var joinerB = NewParent(db, "b@x.com");

        Assert.True(await service.RedeemDeviceInviteAsync(joinerA, first!.Code));
        // KEYSTONE: a second valid, unexpired, unconsumed invite does not get a
        // third seat. Nothing about the invite itself says the toy is full —
        // only the re-count at redemption does.
        Assert.False(await service.RedeemDeviceInviteAsync(joinerB, second!.Code));

        Assert.Equal(2, await db.Set<ParentDevice>().CountAsync(pd => pd.DeviceId == deviceId));
    }

    [Fact]
    public async Task Redeem_ByAParentWhoAlreadyHoldsIt_TakesNoSecondSeat()
    {
        var (service, db, _) = CreateService();
        var (ownerId, deviceId) = SeedOwnedToy(db);
        var issued = await service.CreateDeviceInviteAsync(ownerId, deviceId);

        Assert.True(await service.RedeemDeviceInviteAsync(ownerId, issued!.Code));

        Assert.Equal(1, await db.Set<ParentDevice>().CountAsync(pd => pd.DeviceId == deviceId));
    }

    // ------------------------------------------------------------- redeeming

    [Fact]
    public async Task Redeem_ValidCode_Links_Consumes_Audits_AndTellsTheOtherParent()
    {
        var (service, db, notifier) = CreateService();
        var (ownerId, deviceId) = SeedOwnedToy(db);
        var issued = await service.CreateDeviceInviteAsync(ownerId, deviceId);
        var joinerId = NewParent(db, "joiner@x.com");

        Assert.True(await service.RedeemDeviceInviteAsync(joinerId, issued!.Code));

        Assert.True(await db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == joinerId && pd.DeviceId == deviceId));

        var row = Assert.Single(await db.Set<DeviceInvite>().ToListAsync());
        Assert.NotNull(row.ConsumedAt);
        Assert.Equal(joinerId, row.ConsumedByParentId);

        Assert.Contains(await db.Set<AuditEvent>().ToListAsync(),
            a => a.EventType == AuditEventType.ParentDeviceInviteRedeemed
              && a.ActorParentId == joinerId && a.TargetDeviceId == deviceId);

        // Same contract as the printed-code path: the parent who already held
        // the toy is told somebody joined — and never who.
        var told = Assert.Single(notifier.ToyJoined);
        Assert.Equal("owner@x.com", told.Email);
        Assert.DoesNotContain("joiner@x.com", told.DeviceName);
    }

    [Fact]
    public async Task Redeem_AcceptsWhatAPersonActuallyTypes()
    {
        var (service, db, _) = CreateService();
        var (ownerId, deviceId) = SeedOwnedToy(db);
        var issued = await service.CreateDeviceInviteAsync(ownerId, deviceId);
        var joinerId = NewParent(db, "joiner@x.com");

        var messy = " " + issued!.Code[..4].ToLowerInvariant() + "-" +
                    issued.Code[4..8] + " " + issued.Code[8..].ToLowerInvariant() + " ";

        Assert.True(await service.RedeemDeviceInviteAsync(joinerId, messy));
    }

    [Fact]
    public async Task Redeem_SameCodeTwice_IsRefusedTheSecondTime()
    {
        var (service, db, _) = CreateService();
        var (ownerId, deviceId) = SeedOwnedToy(db);
        var issued = await service.CreateDeviceInviteAsync(ownerId, deviceId);
        var joinerId = NewParent(db, "joiner@x.com");
        var thirdId = NewParent(db, "third@x.com");

        Assert.True(await service.RedeemDeviceInviteAsync(joinerId, issued!.Code));
        Assert.False(await service.RedeemDeviceInviteAsync(thirdId, issued.Code));
    }

    [Fact]
    public async Task Redeem_ExpiredCode_IsRefused()
    {
        var (service, db, _) = CreateService();
        var (ownerId, deviceId) = SeedOwnedToy(db);
        var issued = await service.CreateDeviceInviteAsync(ownerId, deviceId);
        var row = await db.Set<DeviceInvite>().FirstAsync();
        row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        var joinerId = NewParent(db, "joiner@x.com");

        Assert.False(await service.RedeemDeviceInviteAsync(joinerId, issued!.Code));
        Assert.False(await db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == joinerId));
    }

    [Fact]
    public async Task Redeem_RightSelectorWrongSecret_IsRefused()
    {
        var (service, db, _) = CreateService();
        var (ownerId, deviceId) = SeedOwnedToy(db);
        var issued = await service.CreateDeviceInviteAsync(ownerId, deviceId);
        var joinerId = NewParent(db, "joiner@x.com");

        // Knowing the public half must buy nothing.
        var forged = issued!.Code[..4] + "ZZZZZZZZ";

        Assert.False(await service.RedeemDeviceInviteAsync(joinerId, forged));
        Assert.Null((await db.Set<DeviceInvite>().FirstAsync()).ConsumedAt);
    }

    [Fact]
    public async Task Redeem_ForARevokedToy_IsRefused()
    {
        var (service, db, _) = CreateService();
        var (ownerId, deviceId) = SeedOwnedToy(db);
        var issued = await service.CreateDeviceInviteAsync(ownerId, deviceId);

        // Revoke is the lost-or-stolen kill switch. An invite issued before it
        // must not be a way back in.
        var device = await db.Set<Device>().FindAsync(deviceId);
        device!.IsRevoked = true;
        await db.SaveChangesAsync();
        var joinerId = NewParent(db, "joiner@x.com");

        Assert.False(await service.RedeemDeviceInviteAsync(joinerId, issued!.Code));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SHORT")]
    [InlineData("WAYTOOLONGACODE12345")]
    [InlineData("!!!!!!!!!!!!")]
    public async Task Redeem_MalformedCode_IsRefusedWithoutThrowing(string code)
    {
        var (service, db, _) = CreateService();
        var (ownerId, deviceId) = SeedOwnedToy(db);
        await service.CreateDeviceInviteAsync(ownerId, deviceId);
        var joinerId = NewParent(db, "joiner@x.com");

        Assert.False(await service.RedeemDeviceInviteAsync(joinerId, code));
    }
}

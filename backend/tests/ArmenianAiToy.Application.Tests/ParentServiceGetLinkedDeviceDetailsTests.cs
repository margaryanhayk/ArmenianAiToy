using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for ParentService.GetLinkedDeviceDetailsAsync — the enriched
/// linked-devices endpoint. Uses EF Core InMemory.
/// </summary>
public class ParentServiceGetLinkedDeviceDetailsTests
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
        }
    }

    private static (ParentService Service, TestDbContext Db) CreateService()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TestDbContext(options);
        var config = Substitute.For<IConfiguration>();
        var logger = Substitute.For<ILogger<ParentService>>();
        return (new ParentService(db, config, logger), db);
    }

    private static Device NewDevice(string name, DateTime lastSeen)
        => new()
        {
            Id = Guid.NewGuid(),
            MacAddress = Guid.NewGuid().ToString()[..17],
            Name = name,
            ApiKey = Guid.NewGuid().ToString(),
            LastSeenAt = lastSeen,
            RegisteredAt = lastSeen.AddDays(-1)
        };

    [Fact]
    public async Task GetLinkedDeviceDetailsAsync_ReturnsDeviceWithChildrenAndLastConversation()
    {
        var (service, db) = CreateService();
        var parentId = Guid.NewGuid();
        var t0 = new DateTime(2026, 4, 8, 12, 0, 0, DateTimeKind.Utc);
        var device = NewDevice("Bedroom Areg", t0);
        var linkedAt = t0.AddDays(-5);

        db.Set<Parent>().Add(new Parent { Id = parentId, Email = "a@b.com", PasswordHash = "x", RegisteredAt = t0 });
        db.Set<Device>().Add(device);
        db.Set<ParentDevice>().Add(new ParentDevice { ParentId = parentId, DeviceId = device.Id, LinkedAt = linkedAt });
        db.Set<Child>().Add(new Child
        {
            Id = Guid.NewGuid(),
            Name = "Arman",
            Gender = Gender.Boy,
            BirthYear = 2021,
            DeviceId = device.Id
        });
        db.Set<Conversation>().Add(new Conversation { Id = Guid.NewGuid(), DeviceId = device.Id, StartedAt = t0.AddMinutes(-30) });
        db.Set<Conversation>().Add(new Conversation { Id = Guid.NewGuid(), DeviceId = device.Id, StartedAt = t0 });
        await db.SaveChangesAsync();

        var result = await service.GetLinkedDeviceDetailsAsync(parentId);

        var dto = Assert.Single(result);
        Assert.Equal(device.Id, dto.DeviceId);
        Assert.Equal("Bedroom Areg", dto.DeviceName);
        Assert.Equal(t0, dto.LastSeenAt);
        Assert.Equal(linkedAt, dto.LinkedAt);
        Assert.Equal(t0, dto.LastConversationAt);
        var child = Assert.Single(dto.Children);
        Assert.Equal("Arman", child.Name);
        Assert.Equal(Gender.Boy, child.Gender);
        Assert.NotNull(child.Age);
    }

    [Fact]
    public async Task GetLinkedDeviceDetailsAsync_NoChildren_ReturnsEmptyChildList()
    {
        var (service, db) = CreateService();
        var parentId = Guid.NewGuid();
        var device = NewDevice("Kitchen Areg", DateTime.UtcNow);

        db.Set<Parent>().Add(new Parent { Id = parentId, Email = "b@c.com", PasswordHash = "x", RegisteredAt = DateTime.UtcNow });
        db.Set<Device>().Add(device);
        db.Set<ParentDevice>().Add(new ParentDevice { ParentId = parentId, DeviceId = device.Id, LinkedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var result = await service.GetLinkedDeviceDetailsAsync(parentId);

        var dto = Assert.Single(result);
        Assert.Empty(dto.Children);
        Assert.Null(dto.LastConversationAt);
    }

    [Fact]
    public async Task GetLinkedDeviceDetailsAsync_MultipleDevices_ReturnsAll()
    {
        var (service, db) = CreateService();
        var parentId = Guid.NewGuid();
        var t0 = DateTime.UtcNow;
        var d1 = NewDevice("Device A", t0);
        var d2 = NewDevice("Device B", t0);

        db.Set<Parent>().Add(new Parent { Id = parentId, Email = "c@d.com", PasswordHash = "x", RegisteredAt = t0 });
        db.Set<Device>().AddRange(d1, d2);
        db.Set<ParentDevice>().AddRange(
            new ParentDevice { ParentId = parentId, DeviceId = d1.Id, LinkedAt = t0 },
            new ParentDevice { ParentId = parentId, DeviceId = d2.Id, LinkedAt = t0 });
        await db.SaveChangesAsync();

        var result = await service.GetLinkedDeviceDetailsAsync(parentId);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.DeviceName == "Device A");
        Assert.Contains(result, r => r.DeviceName == "Device B");
    }

    [Fact]
    public async Task GetLinkedDeviceDetailsAsync_NoLinkedDevices_ReturnsEmptyList()
    {
        var (service, _) = CreateService();
        var result = await service.GetLinkedDeviceDetailsAsync(Guid.NewGuid());
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetLinkedDeviceDetailsAsync_OtherParentDevicesNotReturned()
    {
        var (service, db) = CreateService();
        var parentA = Guid.NewGuid();
        var parentB = Guid.NewGuid();
        var t0 = DateTime.UtcNow;
        var dA = NewDevice("A's device", t0);
        var dB = NewDevice("B's device", t0);

        db.Set<Parent>().AddRange(
            new Parent { Id = parentA, Email = "a@x.com", PasswordHash = "x", RegisteredAt = t0 },
            new Parent { Id = parentB, Email = "b@x.com", PasswordHash = "x", RegisteredAt = t0 });
        db.Set<Device>().AddRange(dA, dB);
        db.Set<ParentDevice>().AddRange(
            new ParentDevice { ParentId = parentA, DeviceId = dA.Id, LinkedAt = t0 },
            new ParentDevice { ParentId = parentB, DeviceId = dB.Id, LinkedAt = t0 });
        await db.SaveChangesAsync();

        var result = await service.GetLinkedDeviceDetailsAsync(parentA);

        var dto = Assert.Single(result);
        Assert.Equal("A's device", dto.DeviceName);
    }

    // --- Dormancy slice: derived IsDormant reporting-only flag. --------

    private static (ParentService Service, TestDbContext Db) CreateServiceWithConfig(
        string? notSeenDays)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TestDbContext(options);
        var config = Substitute.For<IConfiguration>();
        // Only seed the key when a caller actually wants a non-default —
        // a null raw value exercises the default-threshold branch.
        if (notSeenDays is not null)
            config["Dormancy:Devices:NotSeenDays"].Returns(notSeenDays);
        var logger = Substitute.For<ILogger<ParentService>>();
        return (new ParentService(db, config, logger), db);
    }

    private static async Task<Guid> SeedLinkedDeviceAsync(
        TestDbContext db, Guid parentId, DateTime lastSeen)
    {
        var device = NewDevice("Areg", lastSeen);
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = $"{parentId:N}@x.com",
            PasswordHash = "x",
            RegisteredAt = lastSeen.AddDays(-1)
        });
        db.Set<Device>().Add(device);
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId,
            DeviceId = device.Id,
            LinkedAt = lastSeen.AddDays(-1)
        });
        await db.SaveChangesAsync();
        return device.Id;
    }

    [Fact]
    public async Task GetLinkedDeviceDetailsAsync_DeviceOlderThanDefaultThreshold_ReturnsIsDormantTrue()
    {
        // 181 days silent > default 180-day threshold. Uses the default
        // branch (no config key seeded).
        var (service, db) = CreateServiceWithConfig(notSeenDays: null);
        var parentId = Guid.NewGuid();
        await SeedLinkedDeviceAsync(db, parentId, DateTime.UtcNow.AddDays(-181));

        var result = await service.GetLinkedDeviceDetailsAsync(parentId);

        var dto = Assert.Single(result);
        Assert.True(dto.IsDormant);
    }

    [Fact]
    public async Task GetLinkedDeviceDetailsAsync_DeviceNewerThanDefaultThreshold_ReturnsIsDormantFalse()
    {
        // 30 days silent — well inside the default 180-day threshold.
        var (service, db) = CreateServiceWithConfig(notSeenDays: null);
        var parentId = Guid.NewGuid();
        await SeedLinkedDeviceAsync(db, parentId, DateTime.UtcNow.AddDays(-30));

        var result = await service.GetLinkedDeviceDetailsAsync(parentId);

        var dto = Assert.Single(result);
        Assert.False(dto.IsDormant);
    }

    [Fact]
    public async Task GetLinkedDeviceDetailsAsync_ThresholdConfigIsHonored()
    {
        // Device 45 days silent is dormant under threshold=30
        // but NOT dormant under the default threshold=180. Pins that
        // the config key flows through, not just the default constant.
        var lastSeen = DateTime.UtcNow.AddDays(-45);

        var (tightService, tightDb) = CreateServiceWithConfig(notSeenDays: "30");
        var tightParent = Guid.NewGuid();
        await SeedLinkedDeviceAsync(tightDb, tightParent, lastSeen);
        var tight = Assert.Single(await tightService.GetLinkedDeviceDetailsAsync(tightParent));
        Assert.True(tight.IsDormant);

        var (looseService, looseDb) = CreateServiceWithConfig(notSeenDays: null);
        var looseParent = Guid.NewGuid();
        await SeedLinkedDeviceAsync(looseDb, looseParent, lastSeen);
        var loose = Assert.Single(await looseService.GetLinkedDeviceDetailsAsync(looseParent));
        Assert.False(loose.IsDormant);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public async Task GetLinkedDeviceDetailsAsync_ZeroOrNegativeThreshold_ClampsToOneDay(
        string rawThreshold)
    {
        // Clamp floor = 1 day. A device seen 2 days ago is dormant under
        // the clamped threshold — if the clamp regressed to "accept 0",
        // even just-seen devices would flip to dormant and this test
        // would fail via the recent-device counter-check below.
        var (service, db) = CreateServiceWithConfig(notSeenDays: rawThreshold);
        var parentId = Guid.NewGuid();
        var device = NewDevice("Areg", DateTime.UtcNow.AddDays(-2));
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId, Email = "p@x.com", PasswordHash = "x",
            RegisteredAt = DateTime.UtcNow.AddDays(-3)
        });
        db.Set<Device>().Add(device);
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId, DeviceId = device.Id,
            LinkedAt = DateTime.UtcNow.AddDays(-3)
        });
        // Counter-device: seen "now", must stay IsDormant=false even
        // under the clamp floor.
        var fresh = NewDevice("Fresh", DateTime.UtcNow);
        db.Set<Device>().Add(fresh);
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId, DeviceId = fresh.Id, LinkedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await service.GetLinkedDeviceDetailsAsync(parentId);

        Assert.Equal(2, result.Count);
        var oldDto = Assert.Single(result, r => r.DeviceId == device.Id);
        Assert.True(oldDto.IsDormant);
        var freshDto = Assert.Single(result, r => r.DeviceId == fresh.Id);
        Assert.False(freshDto.IsDormant);
    }

    [Fact]
    public async Task GetLinkedDeviceDetailsAsync_IsDormantRespectsOwnership()
    {
        // Reinforces the existing ownership invariant: another parent's
        // dormant device does NOT leak into this parent's response,
        // regardless of the IsDormant flag. Protects against a future
        // refactor that might widen the query in the name of dormancy
        // reporting.
        var (service, db) = CreateServiceWithConfig(notSeenDays: null);
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        var t0 = DateTime.UtcNow;
        var myFresh = NewDevice("Mine", t0);
        var theirsStale = NewDevice("Theirs", t0.AddDays(-365));

        db.Set<Parent>().AddRange(
            new Parent { Id = mine, Email = "me@x.com", PasswordHash = "x", RegisteredAt = t0 },
            new Parent { Id = other, Email = "you@x.com", PasswordHash = "x", RegisteredAt = t0 });
        db.Set<Device>().AddRange(myFresh, theirsStale);
        db.Set<ParentDevice>().AddRange(
            new ParentDevice { ParentId = mine, DeviceId = myFresh.Id, LinkedAt = t0 },
            new ParentDevice { ParentId = other, DeviceId = theirsStale.Id, LinkedAt = t0 });
        await db.SaveChangesAsync();

        var result = await service.GetLinkedDeviceDetailsAsync(mine);

        var dto = Assert.Single(result);
        Assert.Equal(myFresh.Id, dto.DeviceId);
        Assert.False(dto.IsDormant);
    }

    // --- Platform presence slice: derived IsOnline reporting-only flag. ---

    [Fact]
    public async Task GetLinkedDeviceDetailsAsync_RecentlySeen_ReturnsIsOnlineTrue()
    {
        // Seen "now" — well inside the default 180s online window.
        var (service, db) = CreateServiceWithConfig(notSeenDays: null);
        var parentId = Guid.NewGuid();
        await SeedLinkedDeviceAsync(db, parentId, DateTime.UtcNow);

        var dto = Assert.Single(await service.GetLinkedDeviceDetailsAsync(parentId));
        Assert.True(dto.IsOnline);
    }

    [Fact]
    public async Task GetLinkedDeviceDetailsAsync_SeenBeyondOnlineWindow_ReturnsIsOnlineFalse_ButNotDormant()
    {
        // 10 minutes silent: past the 180s online window (offline) but FAR
        // inside the 180-day dormancy threshold — proving the two flags are
        // independent (a toy can be momentarily offline without being dormant).
        var (service, db) = CreateServiceWithConfig(notSeenDays: null);
        var parentId = Guid.NewGuid();
        await SeedLinkedDeviceAsync(db, parentId, DateTime.UtcNow.AddMinutes(-10));

        var dto = Assert.Single(await service.GetLinkedDeviceDetailsAsync(parentId));
        Assert.False(dto.IsOnline);
        Assert.False(dto.IsDormant);
    }

    [Fact]
    public async Task GetLinkedDeviceDetailsAsync_OnlineThresholdConfigIsHonored()
    {
        // A device seen 90s ago is OFFLINE under a tight 60s window but ONLINE
        // under the default 180s — pins that Presence:OnlineThresholdSeconds
        // flows through, not just the default constant.
        var lastSeen = DateTime.UtcNow.AddSeconds(-90);

        var tightCfg = Substitute.For<IConfiguration>();
        tightCfg["Presence:OnlineThresholdSeconds"].Returns("60");
        var tightDb = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tightService = new ParentService(tightDb, tightCfg, Substitute.For<ILogger<ParentService>>());
        var tp = Guid.NewGuid();
        await SeedLinkedDeviceAsync(tightDb, tp, lastSeen);
        Assert.False(Assert.Single(await tightService.GetLinkedDeviceDetailsAsync(tp)).IsOnline);

        var (looseService, looseDb) = CreateServiceWithConfig(notSeenDays: null);
        var lp = Guid.NewGuid();
        await SeedLinkedDeviceAsync(looseDb, lp, lastSeen);
        Assert.True(Assert.Single(await looseService.GetLinkedDeviceDetailsAsync(lp)).IsOnline);
    }

    // --- Dormancy summary slice: devices + lastLoginAt wrapper. --------

    [Fact]
    public async Task GetLinkedDeviceDetailsWithSummaryAsync_MixedDormancy_CountsMatchDevices()
    {
        // Two dormant, one fresh. Pins that the summary's
        // DormantDevices count is derived from the same IsDormant
        // booleans the response DTOs carry — a future refactor that
        // silently re-derives dormancy in the wrapper (or miscounts
        // fresh devices) would break this.
        var (service, db) = CreateServiceWithConfig(notSeenDays: null);
        var parentId = Guid.NewGuid();
        var t0 = DateTime.UtcNow;
        var dStale1 = NewDevice("Stale1", t0.AddDays(-365));
        var dStale2 = NewDevice("Stale2", t0.AddDays(-200));
        var dFresh = NewDevice("Fresh", t0.AddDays(-5));
        var stamped = t0.AddDays(-10);

        db.Set<Parent>().Add(new Parent
        {
            Id = parentId, Email = "p@x.com", PasswordHash = "x",
            RegisteredAt = t0.AddDays(-366),
            LastLoginAt = stamped
        });
        db.Set<Device>().AddRange(dStale1, dStale2, dFresh);
        db.Set<ParentDevice>().AddRange(
            new ParentDevice { ParentId = parentId, DeviceId = dStale1.Id, LinkedAt = t0 },
            new ParentDevice { ParentId = parentId, DeviceId = dStale2.Id, LinkedAt = t0 },
            new ParentDevice { ParentId = parentId, DeviceId = dFresh.Id, LinkedAt = t0 });
        await db.SaveChangesAsync();

        var result = await service.GetLinkedDeviceDetailsWithSummaryAsync(parentId);

        Assert.Equal(3, result.Devices.Count);
        Assert.Equal(3, result.Summary.TotalDevices);
        Assert.Equal(2, result.Summary.DormantDevices);
        Assert.Equal(result.Devices.Count(d => d.IsDormant), result.Summary.DormantDevices);
        Assert.Equal(stamped, result.Summary.LastLoginAt);
    }

    [Fact]
    public async Task GetLinkedDeviceDetailsWithSummaryAsync_NullLastLoginAt_FlowsThroughAsNull()
    {
        // Never-logged-in parent path: LastLoginAt is null on the Parent
        // row, must surface as null on the summary (not DateTime.MinValue
        // or any other sentinel). The dashboard renders "not available
        // yet" based on this exact null.
        var (service, db) = CreateServiceWithConfig(notSeenDays: null);
        var parentId = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId, Email = "p@x.com", PasswordHash = "x",
            RegisteredAt = DateTime.UtcNow,
            LastLoginAt = null
        });
        await db.SaveChangesAsync();

        var result = await service.GetLinkedDeviceDetailsWithSummaryAsync(parentId);

        Assert.Null(result.Summary.LastLoginAt);
        Assert.Equal(0, result.Summary.TotalDevices);
        Assert.Equal(0, result.Summary.DormantDevices);
    }

    [Fact]
    public async Task GetLinkedDeviceDetailsWithSummaryAsync_NoDevices_ReturnsEmptyDevicesAndZeroCounts()
    {
        // Parent exists and has logged in but has no linked devices.
        // Summary must carry TotalDevices=0 / DormantDevices=0 without
        // any /0 hazard and must still surface LastLoginAt.
        var (service, db) = CreateServiceWithConfig(notSeenDays: null);
        var parentId = Guid.NewGuid();
        var stamped = DateTime.UtcNow.AddDays(-2);
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId, Email = "p@x.com", PasswordHash = "x",
            RegisteredAt = DateTime.UtcNow.AddDays(-30),
            LastLoginAt = stamped
        });
        await db.SaveChangesAsync();

        var result = await service.GetLinkedDeviceDetailsWithSummaryAsync(parentId);

        Assert.Empty(result.Devices);
        Assert.Equal(0, result.Summary.TotalDevices);
        Assert.Equal(0, result.Summary.DormantDevices);
        Assert.Equal(stamped, result.Summary.LastLoginAt);
    }

    [Fact]
    public async Task GetLinkedDeviceDetailsWithSummaryAsync_OtherParentDormantDevices_DoNotCount()
    {
        // Scope invariant: another parent's dormant devices must NEVER
        // inflate this parent's summary counts, regardless of shared-
        // device scenarios or a future query refactor that accidentally
        // widens the filter. Mirrors the existing
        // GetLinkedDeviceDetailsAsync_IsDormantRespectsOwnership test
        // at the summary layer.
        var (service, db) = CreateServiceWithConfig(notSeenDays: null);
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        var t0 = DateTime.UtcNow;
        var myFresh = NewDevice("Mine", t0);
        var theirsStale1 = NewDevice("Theirs1", t0.AddDays(-365));
        var theirsStale2 = NewDevice("Theirs2", t0.AddDays(-365));

        db.Set<Parent>().AddRange(
            new Parent { Id = mine, Email = "me@x.com", PasswordHash = "x", RegisteredAt = t0,
                         LastLoginAt = t0.AddHours(-1) },
            new Parent { Id = other, Email = "you@x.com", PasswordHash = "x", RegisteredAt = t0 });
        db.Set<Device>().AddRange(myFresh, theirsStale1, theirsStale2);
        db.Set<ParentDevice>().AddRange(
            new ParentDevice { ParentId = mine, DeviceId = myFresh.Id, LinkedAt = t0 },
            new ParentDevice { ParentId = other, DeviceId = theirsStale1.Id, LinkedAt = t0 },
            new ParentDevice { ParentId = other, DeviceId = theirsStale2.Id, LinkedAt = t0 });
        await db.SaveChangesAsync();

        var result = await service.GetLinkedDeviceDetailsWithSummaryAsync(mine);

        Assert.Single(result.Devices);
        Assert.Equal(1, result.Summary.TotalDevices);
        Assert.Equal(0, result.Summary.DormantDevices);
    }

    [Fact]
    public async Task GetLinkedDeviceDetailsWithSummaryAsync_DoesNotDuplicateDormancyDerivation()
    {
        // Asserts the summary's DormantDevices is a pure aggregation
        // over the devices list — for every device in the list,
        // IsDormant is the single source of truth. A regression that
        // re-derived dormancy in the wrapper under a different
        // threshold (e.g. a hardcoded 90 when config says 30) would
        // break the equality below.
        var (service, db) = CreateServiceWithConfig(notSeenDays: "30");
        var parentId = Guid.NewGuid();
        var t0 = DateTime.UtcNow;
        // 29 days is under the 30-day threshold (fresh);
        // 31 days is over (dormant).
        var dFresh = NewDevice("F", t0.AddDays(-29));
        var dStale = NewDevice("S", t0.AddDays(-31));
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId, Email = "p@x.com", PasswordHash = "x", RegisteredAt = t0
        });
        db.Set<Device>().AddRange(dFresh, dStale);
        db.Set<ParentDevice>().AddRange(
            new ParentDevice { ParentId = parentId, DeviceId = dFresh.Id, LinkedAt = t0 },
            new ParentDevice { ParentId = parentId, DeviceId = dStale.Id, LinkedAt = t0 });
        await db.SaveChangesAsync();

        var result = await service.GetLinkedDeviceDetailsWithSummaryAsync(parentId);

        Assert.Equal(1, result.Summary.DormantDevices);
        Assert.Equal(result.Devices.Count(d => d.IsDormant),
                     result.Summary.DormantDevices);
    }

    // --- Verification visibility on summary --------------------------

    [Fact]
    public async Task GetLinkedDeviceDetailsWithSummaryAsync_VerifiedParent_StampedEmailVerifiedAtFlowsThrough()
    {
        var (service, db) = CreateServiceWithConfig(notSeenDays: null);
        var parentId = Guid.NewGuid();
        var verifiedAt = new DateTime(2026, 4, 10, 9, 0, 0, DateTimeKind.Utc);
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "verified@example.com",
            PasswordHash = "x",
            RegisteredAt = DateTime.UtcNow.AddDays(-30),
            EmailVerifiedAt = verifiedAt
        });
        await db.SaveChangesAsync();

        var result = await service.GetLinkedDeviceDetailsWithSummaryAsync(parentId);

        Assert.Equal(verifiedAt, result.Summary.EmailVerifiedAt);
    }

    [Fact]
    public async Task GetLinkedDeviceDetailsWithSummaryAsync_UnverifiedParent_EmailVerifiedAtIsNull()
    {
        // Pin the unverified-flows-through-as-null contract — the
        // dashboard renders the "Email not verified yet." line on
        // exactly this null.
        var (service, db) = CreateServiceWithConfig(notSeenDays: null);
        var parentId = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "unverified@example.com",
            PasswordHash = "x",
            RegisteredAt = DateTime.UtcNow.AddDays(-30),
            EmailVerifiedAt = null
        });
        await db.SaveChangesAsync();

        var result = await service.GetLinkedDeviceDetailsWithSummaryAsync(parentId);

        Assert.Null(result.Summary.EmailVerifiedAt);
    }

    [Fact]
    public async Task GetLinkedDeviceDetailsWithSummaryAsync_MissingParent_EmailVerifiedAtIsNull()
    {
        // Defensive: stale-JWT-without-row case. The summary's
        // existing LastLoginAt also collapses to null here; the new
        // EmailVerifiedAt field follows the same defensive shape.
        var (service, _) = CreateServiceWithConfig(notSeenDays: null);
        var parentId = Guid.NewGuid();

        var result = await service.GetLinkedDeviceDetailsWithSummaryAsync(parentId);

        Assert.Null(result.Summary.LastLoginAt);
        Assert.Null(result.Summary.EmailVerifiedAt);
    }
}

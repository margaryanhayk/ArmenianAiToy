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
            DateOfBirth = new DateOnly(2021, 6, 15),
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
}

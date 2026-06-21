using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for DeviceService: RegisterDeviceAsync, ValidateDeviceAsync, UpdateLastSeenAsync.
/// Uses EF Core InMemory.
///
/// Hash-at-rest invariants pinned here:
/// - New registration stores ApiKeyHash and leaves ApiKey null.
/// - The registration response returns the one-and-only plaintext copy of
///   the key; the entity row never carries it.
/// - Re-registration of a hashed row ROTATES (new plaintext, new hash) —
///   the old plaintext is unrecoverable from the hash.
/// - Re-registration of a legacy plaintext row RETURNS the existing
///   plaintext verbatim AND lazy-upgrades the row to hash.
/// - ValidateDeviceAsync verifies via PBKDF2 against ApiKeyHash when
///   present, and falls back to constant-time plaintext compare on
///   legacy rows. Legacy success lazy-upgrades.
/// - Wrong key returns null on both paths.
/// </summary>
public class DeviceServiceTests
{
    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Device>().HasKey(d => d.Id);
            modelBuilder.Entity<Device>().Ignore(d => d.Conversations);
            modelBuilder.Entity<Device>().Ignore(d => d.ParentDevices);
        }
    }

    private static (DeviceService Service, TestDbContext Db) CreateService()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TestDbContext(options);
        var logger = Substitute.For<ILogger<DeviceService>>();
        return (new DeviceService(db, logger), db);
    }

    // --- RegisterDeviceAsync ---

    [Fact]
    public async Task RegisterDeviceAsync_NewDevice_CreatesAndReturnsIdAndKey()
    {
        var (service, db) = CreateService();
        var request = new DeviceRegistrationRequest("AA:BB:CC:DD:EE:FF");

        var result = (await service.RegisterDeviceAsync(request))!;

        Assert.NotEqual(Guid.Empty, result.DeviceId);
        Assert.StartsWith("dtk_", result.ApiKey);
        var device = await db.Set<Device>().FindAsync(result.DeviceId);
        Assert.NotNull(device);
        Assert.Equal("AA:BB:CC:DD:EE:FF", device!.MacAddress);
        Assert.Equal("Toy-E:FF", device.Name);
    }

    [Fact]
    public async Task RegisterDeviceAsync_NewDevice_StoresHashAndNullPlaintext()
    {
        var (service, db) = CreateService();

        var result = (await service.RegisterDeviceAsync(
            new DeviceRegistrationRequest("AA:BB:CC:DD:EE:FF")))!;

        var device = await db.Set<Device>().FindAsync(result.DeviceId);
        Assert.NotNull(device);
        // The row never carries the plaintext for newly-registered devices.
        Assert.Null(device!.ApiKey);
        // The hash is well-formed and verifies against the returned plaintext.
        Assert.True(DeviceApiKeyHasher.IsHash(device.ApiKeyHash));
        Assert.True(DeviceApiKeyHasher.Verify(result.ApiKey, device.ApiKeyHash));
        // The hash MUST NOT equal the plaintext.
        Assert.NotEqual(result.ApiKey, device.ApiKeyHash);
    }

    [Fact]
    public async Task RegisterDeviceAsync_ExistingHashedRow_RotatesKey()
    {
        var (service, db) = CreateService();
        // Seed a "hashed-only" device (the post-slice steady state).
        var originalPlaintext = "dtk_originalkey";
        var seed = new Device
        {
            Id = Guid.NewGuid(),
            MacAddress = "11:22:33:44:55:66",
            Name = "Existing",
            ApiKey = null,
            ApiKeyHash = DeviceApiKeyHasher.Hash(originalPlaintext),
            RegisteredAt = DateTime.UtcNow.AddDays(-1),
            LastSeenAt = DateTime.UtcNow.AddDays(-1)
        };
        db.Set<Device>().Add(seed);
        await db.SaveChangesAsync();
        var oldHash = seed.ApiKeyHash;
        var oldLastSeen = seed.LastSeenAt;

        var result = (await service.RegisterDeviceAsync(
            new DeviceRegistrationRequest("11:22:33:44:55:66"), allowReRegister: true))!;

        Assert.Equal(seed.Id, result.DeviceId);
        // The returned key is a freshly-rotated plaintext, NOT the original.
        Assert.StartsWith("dtk_", result.ApiKey);
        Assert.NotEqual(originalPlaintext, result.ApiKey);
        var reloaded = await db.Set<Device>().FindAsync(seed.Id);
        Assert.NotNull(reloaded);
        // Hash rotated; plaintext stays null; mac/name unchanged; row count unchanged.
        Assert.Null(reloaded!.ApiKey);
        Assert.True(DeviceApiKeyHasher.IsHash(reloaded.ApiKeyHash));
        Assert.NotEqual(oldHash, reloaded.ApiKeyHash);
        Assert.True(DeviceApiKeyHasher.Verify(result.ApiKey, reloaded.ApiKeyHash));
        // Old plaintext no longer authenticates after rotation.
        Assert.False(DeviceApiKeyHasher.Verify(originalPlaintext, reloaded.ApiKeyHash));
        Assert.True(reloaded.LastSeenAt >= oldLastSeen);
        Assert.Equal(1, await db.Set<Device>().CountAsync());
    }

    [Fact]
    public async Task RegisterDeviceAsync_ExistingLegacyPlaintextRow_ReturnsExistingKeyAndUpgrades()
    {
        var (service, db) = CreateService();
        var seed = new Device
        {
            Id = Guid.NewGuid(),
            MacAddress = "11:22:33:44:55:66",
            Name = "Legacy",
            ApiKey = "dtk_legacyplaintext",
            ApiKeyHash = null,
            RegisteredAt = DateTime.UtcNow.AddDays(-30),
            LastSeenAt = DateTime.UtcNow.AddDays(-30)
        };
        db.Set<Device>().Add(seed);
        await db.SaveChangesAsync();

        var result = (await service.RegisterDeviceAsync(
            new DeviceRegistrationRequest("11:22:33:44:55:66"), allowReRegister: true))!;

        Assert.Equal(seed.Id, result.DeviceId);
        // Idempotency contract for legacy rows: same key the device already
        // carries, so an offline firmware that re-registers does not get
        // locked out.
        Assert.Equal("dtk_legacyplaintext", result.ApiKey);
        var reloaded = await db.Set<Device>().FindAsync(seed.Id);
        Assert.NotNull(reloaded);
        // The plaintext column is cleared and the hash is now populated.
        Assert.Null(reloaded!.ApiKey);
        Assert.True(DeviceApiKeyHasher.IsHash(reloaded.ApiKeyHash));
        Assert.True(DeviceApiKeyHasher.Verify("dtk_legacyplaintext", reloaded.ApiKeyHash));
    }

    [Fact]
    public async Task RegisterDeviceAsync_ExistingDevice_WithoutForce_RefusedAndKeyUnchanged()
    {
        // #011: a plain re-registration must NOT silently rotate an in-field
        // device's credential — it is refused (null) and the key is left intact.
        var (service, db) = CreateService();
        var seed = new Device
        {
            Id = Guid.NewGuid(),
            MacAddress = "11:22:33:44:55:66",
            Name = "InField",
            ApiKey = null,
            ApiKeyHash = DeviceApiKeyHasher.Hash("dtk_infield"),
            RegisteredAt = DateTime.UtcNow.AddDays(-1),
            LastSeenAt = DateTime.UtcNow.AddDays(-1)
        };
        db.Set<Device>().Add(seed);
        await db.SaveChangesAsync();
        var oldHash = seed.ApiKeyHash;

        var result = await service.RegisterDeviceAsync(
            new DeviceRegistrationRequest("11:22:33:44:55:66")); // no force

        Assert.Null(result); // refused, not silently rotated
        var reloaded = await db.Set<Device>().FindAsync(seed.Id);
        Assert.Equal(oldHash, reloaded!.ApiKeyHash);                      // unchanged
        Assert.True(DeviceApiKeyHasher.Verify("dtk_infield", reloaded.ApiKeyHash));
        Assert.Equal(1, await db.Set<Device>().CountAsync());
    }

    [Fact]
    public async Task RegisterDeviceAsync_WithFirmwareVersion_Stored()
    {
        var (service, db) = CreateService();
        var request = new DeviceRegistrationRequest("AA:BB:CC:DD:EE:FF", "v1.2.3");

        var result = (await service.RegisterDeviceAsync(request))!;

        var device = await db.Set<Device>().FindAsync(result.DeviceId);
        Assert.Equal("v1.2.3", device!.FirmwareVersion);
    }

    // --- ValidateDeviceAsync ---

    [Fact]
    public async Task ValidateDeviceAsync_HashedRow_ValidKey_ReturnsDevice()
    {
        var (service, db) = CreateService();
        var plaintext = "dtk_validhashedkey";
        var device = new Device
        {
            Id = Guid.NewGuid(),
            MacAddress = "AA:BB:CC:DD:EE:FF",
            Name = "Test",
            ApiKey = null,
            ApiKeyHash = DeviceApiKeyHasher.Hash(plaintext),
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };
        db.Set<Device>().Add(device);
        await db.SaveChangesAsync();

        var result = await service.ValidateDeviceAsync(device.Id, plaintext);

        Assert.NotNull(result);
        Assert.Equal(device.Id, result!.Id);
    }

    [Fact]
    public async Task ValidateDeviceAsync_HashedRow_WrongKey_ReturnsNull()
    {
        var (service, db) = CreateService();
        var device = new Device
        {
            Id = Guid.NewGuid(),
            MacAddress = "AA:BB:CC:DD:EE:FF",
            Name = "Test",
            ApiKey = null,
            ApiKeyHash = DeviceApiKeyHasher.Hash("dtk_correct"),
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };
        db.Set<Device>().Add(device);
        await db.SaveChangesAsync();

        var result = await service.ValidateDeviceAsync(device.Id, "dtk_wrong");

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateDeviceAsync_LegacyPlaintextRow_ValidKey_AuthenticatesAndUpgrades()
    {
        var (service, db) = CreateService();
        var device = new Device
        {
            Id = Guid.NewGuid(),
            MacAddress = "AA:BB:CC:DD:EE:FF",
            Name = "Legacy",
            ApiKey = "dtk_legacyvalid",
            ApiKeyHash = null,
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };
        db.Set<Device>().Add(device);
        await db.SaveChangesAsync();

        var result = await service.ValidateDeviceAsync(device.Id, "dtk_legacyvalid");

        Assert.NotNull(result);
        Assert.Equal(device.Id, result!.Id);

        var reloaded = await db.Set<Device>().FindAsync(device.Id);
        Assert.NotNull(reloaded);
        Assert.Null(reloaded!.ApiKey);
        Assert.True(DeviceApiKeyHasher.IsHash(reloaded.ApiKeyHash));
        Assert.True(DeviceApiKeyHasher.Verify("dtk_legacyvalid", reloaded.ApiKeyHash));
    }

    [Fact]
    public async Task ValidateDeviceAsync_LegacyPlaintextRow_WrongKey_ReturnsNullAndDoesNotUpgrade()
    {
        var (service, db) = CreateService();
        var device = new Device
        {
            Id = Guid.NewGuid(),
            MacAddress = "AA:BB:CC:DD:EE:FF",
            Name = "Legacy",
            ApiKey = "dtk_legacycorrect",
            ApiKeyHash = null,
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };
        db.Set<Device>().Add(device);
        await db.SaveChangesAsync();

        var result = await service.ValidateDeviceAsync(device.Id, "dtk_legacywrong");

        Assert.Null(result);
        var reloaded = await db.Set<Device>().FindAsync(device.Id);
        Assert.NotNull(reloaded);
        // Wrong key did not trigger the upgrade — plaintext stays, hash stays null.
        Assert.Equal("dtk_legacycorrect", reloaded!.ApiKey);
        Assert.Null(reloaded.ApiKeyHash);
    }

    [Fact]
    public async Task ValidateDeviceAsync_HashedRow_DoesNotConsultLegacyPlaintext()
    {
        // Pathological row: hash valid for "good", but also has a stale
        // plaintext "stale" sitting in the legacy column. The hashed path
        // must win — submitting "stale" must fail, submitting "good" must
        // pass — because the hash is the source of truth once present.
        var (service, db) = CreateService();
        var device = new Device
        {
            Id = Guid.NewGuid(),
            MacAddress = "AA:BB:CC:DD:EE:FF",
            Name = "Test",
            ApiKey = "dtk_stale",
            ApiKeyHash = DeviceApiKeyHasher.Hash("dtk_good"),
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };
        db.Set<Device>().Add(device);
        await db.SaveChangesAsync();

        Assert.Null(await service.ValidateDeviceAsync(device.Id, "dtk_stale"));
        Assert.NotNull(await service.ValidateDeviceAsync(device.Id, "dtk_good"));
    }

    [Fact]
    public async Task ValidateDeviceAsync_NonExistentId_ReturnsNull()
    {
        var (service, _) = CreateService();

        var result = await service.ValidateDeviceAsync(Guid.NewGuid(), "any-key");

        Assert.Null(result);
    }

    // --- UpdateLastSeenAsync ---

    [Fact]
    public async Task UpdateLastSeenAsync_ExistingDevice_UpdatesTimestamp()
    {
        var (service, db) = CreateService();
        var oldTime = DateTime.UtcNow.AddHours(-1);
        var device = new Device
        {
            Id = Guid.NewGuid(),
            MacAddress = "AA:BB:CC:DD:EE:FF",
            Name = "Test",
            ApiKey = null,
            ApiKeyHash = DeviceApiKeyHasher.Hash("dtk_test"),
            RegisteredAt = oldTime,
            LastSeenAt = oldTime
        };
        db.Set<Device>().Add(device);
        await db.SaveChangesAsync();

        await service.UpdateLastSeenAsync(device.Id);

        var updated = await db.Set<Device>().FindAsync(device.Id);
        Assert.True(updated!.LastSeenAt > oldTime);
    }

    [Fact]
    public async Task UpdateLastSeenAsync_NonExistentDevice_NoError()
    {
        var (service, _) = CreateService();

        // Should not throw
        await service.UpdateLastSeenAsync(Guid.NewGuid());
    }
}

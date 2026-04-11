using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for DeviceService: RegisterDeviceAsync, ValidateDeviceAsync, UpdateLastSeenAsync.
/// Uses EF Core InMemory.
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

        var result = await service.RegisterDeviceAsync(request);

        Assert.NotEqual(Guid.Empty, result.DeviceId);
        Assert.StartsWith("dtk_", result.ApiKey);
        var device = await db.Set<Device>().FindAsync(result.DeviceId);
        Assert.NotNull(device);
        Assert.Equal("AA:BB:CC:DD:EE:FF", device!.MacAddress);
        Assert.Equal("Toy-E:FF", device.Name);
    }

    [Fact]
    public async Task RegisterDeviceAsync_ExistingMac_ReusesDeviceAndUpdatesTimestamp()
    {
        var (service, db) = CreateService();
        var existingDevice = new Device
        {
            Id = Guid.NewGuid(),
            MacAddress = "11:22:33:44:55:66",
            Name = "Existing",
            ApiKey = "dtk_existing",
            RegisteredAt = DateTime.UtcNow.AddDays(-1),
            LastSeenAt = DateTime.UtcNow.AddDays(-1)
        };
        db.Set<Device>().Add(existingDevice);
        await db.SaveChangesAsync();
        var oldLastSeen = existingDevice.LastSeenAt;

        var result = await service.RegisterDeviceAsync(new DeviceRegistrationRequest("11:22:33:44:55:66"));

        Assert.Equal(existingDevice.Id, result.DeviceId);
        Assert.Equal("dtk_existing", result.ApiKey);
        var device = await db.Set<Device>().FindAsync(existingDevice.Id);
        Assert.True(device!.LastSeenAt >= oldLastSeen);
        Assert.Equal(1, await db.Set<Device>().CountAsync());
    }

    [Fact]
    public async Task RegisterDeviceAsync_WithFirmwareVersion_Stored()
    {
        var (service, db) = CreateService();
        var request = new DeviceRegistrationRequest("AA:BB:CC:DD:EE:FF", "v1.2.3");

        var result = await service.RegisterDeviceAsync(request);

        var device = await db.Set<Device>().FindAsync(result.DeviceId);
        Assert.Equal("v1.2.3", device!.FirmwareVersion);
    }

    // --- ValidateDeviceAsync ---

    [Fact]
    public async Task ValidateDeviceAsync_ValidIdAndKey_ReturnsDevice()
    {
        var (service, db) = CreateService();
        var device = new Device
        {
            Id = Guid.NewGuid(),
            MacAddress = "AA:BB:CC:DD:EE:FF",
            Name = "Test",
            ApiKey = "dtk_valid",
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };
        db.Set<Device>().Add(device);
        await db.SaveChangesAsync();

        var result = await service.ValidateDeviceAsync(device.Id, "dtk_valid");

        Assert.NotNull(result);
        Assert.Equal(device.Id, result!.Id);
    }

    [Fact]
    public async Task ValidateDeviceAsync_WrongApiKey_ReturnsNull()
    {
        var (service, db) = CreateService();
        var device = new Device
        {
            Id = Guid.NewGuid(),
            MacAddress = "AA:BB:CC:DD:EE:FF",
            Name = "Test",
            ApiKey = "dtk_correct",
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };
        db.Set<Device>().Add(device);
        await db.SaveChangesAsync();

        var result = await service.ValidateDeviceAsync(device.Id, "dtk_wrong");

        Assert.Null(result);
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
            ApiKey = "dtk_test",
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

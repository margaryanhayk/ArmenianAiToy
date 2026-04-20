using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <c>DeviceService.IsDevicePausedAsync</c> (B3). The method is the
/// hot-path pause gate used by <c>ChatController</c> — a single-field read
/// with well-defined behavior for unknown device ids.
/// </summary>
public class DeviceServiceIsPausedTests
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

    private static async Task<Guid> SeedDeviceAsync(TestDbContext db, bool isPaused)
    {
        var id = Guid.NewGuid();
        db.Set<Device>().Add(new Device
        {
            Id = id,
            MacAddress = "aa:bb:cc:dd:ee:ff",
            Name = "Test",
            ApiKey = "dtk_test",
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            IsPaused = isPaused
        });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task IsDevicePausedAsync_PausedDevice_ReturnsTrue()
    {
        var (service, db) = CreateService();
        var id = await SeedDeviceAsync(db, isPaused: true);

        var result = await service.IsDevicePausedAsync(id);

        Assert.True(result);
    }

    [Fact]
    public async Task IsDevicePausedAsync_UnpausedDevice_ReturnsFalse()
    {
        var (service, db) = CreateService();
        var id = await SeedDeviceAsync(db, isPaused: false);

        var result = await service.IsDevicePausedAsync(id);

        Assert.False(result);
    }

    [Fact]
    public async Task IsDevicePausedAsync_UnknownDevice_ReturnsFalse()
    {
        // Defense-in-depth: DeviceAuthMiddleware 401s unknown credentials
        // before the chat controller runs, but if anything ever bypasses
        // that path, an unknown id must not accidentally trigger the
        // paused short-circuit.
        var (service, _) = CreateService();

        var result = await service.IsDevicePausedAsync(Guid.NewGuid());

        Assert.False(result);
    }
}

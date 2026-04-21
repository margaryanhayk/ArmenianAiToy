using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <c>DeviceService.IsDeviceInBedtimeWindowAsync</c>. Uses real
/// SQLite in-memory so the TimeOnly?/string column projection round-trips
/// the same way it will in production.
/// </summary>
public class DeviceServiceBedtimeWindowTests
{
    private static async Task<(DeviceService Service, AppDbContext Db, SqliteConnection Conn)> CreateServiceAsync()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var logger = Substitute.For<ILogger<DeviceService>>();
        return (new DeviceService(db, logger), db, conn);
    }

    private static Guid SeedDevice(AppDbContext db, TimeOnly? start, TimeOnly? end, string timeZone = "UTC")
    {
        var id = Guid.NewGuid();
        db.Set<Device>().Add(new Device
        {
            Id = id,
            MacAddress = "mac-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Test",
            ApiKey = "dtk_" + Guid.NewGuid().ToString("N"),
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            BedtimeStart = start,
            BedtimeEnd = end,
            TimeZone = timeZone
        });
        db.SaveChanges();
        return id;
    }

    [Fact]
    public async Task IsDeviceInBedtimeWindowAsync_DeviceInWindow_ReturnsTrue()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var id = SeedDevice(db, new TimeOnly(22, 0), new TimeOnly(7, 0));

        var result = await service.IsDeviceInBedtimeWindowAsync(
            id, new DateTime(2026, 4, 21, 23, 0, 0, DateTimeKind.Utc));

        Assert.True(result);
    }

    [Fact]
    public async Task IsDeviceInBedtimeWindowAsync_DeviceOutOfWindow_ReturnsFalse()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var id = SeedDevice(db, new TimeOnly(22, 0), new TimeOnly(7, 0));

        var result = await service.IsDeviceInBedtimeWindowAsync(
            id, new DateTime(2026, 4, 21, 12, 0, 0, DateTimeKind.Utc));

        Assert.False(result);
    }

    [Fact]
    public async Task IsDeviceInBedtimeWindowAsync_DisabledWindow_ReturnsFalse()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var id = SeedDevice(db, null, null);

        Assert.False(await service.IsDeviceInBedtimeWindowAsync(
            id, new DateTime(2026, 4, 21, 23, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task IsDeviceInBedtimeWindowAsync_UnknownDevice_ReturnsFalse()
    {
        // Same contract as IsDevicePausedAsync: unknown device id must not
        // accidentally trigger the chat gate. DeviceAuthMiddleware is where
        // unknown device ids are rejected.
        var (service, _, conn) = await CreateServiceAsync();
        await using var _conn = conn;

        Assert.False(await service.IsDeviceInBedtimeWindowAsync(
            Guid.NewGuid(), DateTime.UtcNow));
    }
}

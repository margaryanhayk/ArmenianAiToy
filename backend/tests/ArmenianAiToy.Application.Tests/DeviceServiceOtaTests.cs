using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// DeviceService OTA-foundation tests: firmware reporting from the heartbeat,
/// and the revoked-device auth gate (a revoked device can never poll/ack —
/// ValidateDeviceAsync rejects it before the middleware sets DeviceId).
/// </summary>
public class DeviceServiceOtaTests
{
    private sealed class TestDb : DbContext
    {
        public TestDb(DbContextOptions<TestDb> o) : base(o) { }
        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<Device>(e =>
            {
                e.HasKey(d => d.Id);
                e.Ignore(d => d.Conversations);
                e.Ignore(d => d.ParentDevices);
            });
        }
    }

    private static (DeviceService Service, TestDb Db) Create()
    {
        var db = new TestDb(new DbContextOptionsBuilder<TestDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (new DeviceService(db, Substitute.For<ILogger<DeviceService>>()), db);
    }

    private static readonly DateTime Now = new(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task UpdateFirmwareReport_StampsAllReportedFields()
    {
        var (svc, db) = Create();
        var id = Guid.NewGuid();
        db.Add(new Device { Id = id, MacAddress = "m", Name = "t" });
        await db.SaveChangesAsync();

        await svc.UpdateFirmwareReportAsync(id, new DeviceHeartbeatRequest(
            FirmwareVersion: "1.0.0", FirmwareBuild: "build-42",
            BoardModel: "areg-s3-n8", PartitionName: "app0", LastOtaStatus: "ok"), Now);

        var d = await db.Set<Device>().FirstAsync(x => x.Id == id);
        Assert.Equal("1.0.0", d.FirmwareVersion);
        Assert.Equal("build-42", d.FirmwareBuild);
        Assert.Equal("areg-s3-n8", d.BoardModel);
        Assert.Equal("app0", d.PartitionName);
        Assert.Equal("ok", d.LastOtaStatus);
        Assert.Equal(Now, d.FirmwareReportedAt);
    }

    [Fact]
    public async Task UpdateFirmwareReport_PartialReport_DoesNotBlankExistingFields()
    {
        var (svc, db) = Create();
        var id = Guid.NewGuid();
        db.Add(new Device
        {
            Id = id, MacAddress = "m", Name = "t",
            FirmwareVersion = "1.0.0", BoardModel = "areg-s3-n8",
        });
        await db.SaveChangesAsync();

        // Only LastOtaStatus reported — version/board must survive.
        await svc.UpdateFirmwareReportAsync(id, new DeviceHeartbeatRequest(LastOtaStatus: "rollback"), Now);

        var d = await db.Set<Device>().FirstAsync(x => x.Id == id);
        Assert.Equal("1.0.0", d.FirmwareVersion);
        Assert.Equal("areg-s3-n8", d.BoardModel);
        Assert.Equal("rollback", d.LastOtaStatus);
    }

    [Fact]
    public async Task ValidateDevice_RevokedDevice_ReturnsNull_SoItCannotPollCommands()
    {
        var (svc, db) = Create();
        var id = Guid.NewGuid();
        db.Add(new Device { Id = id, MacAddress = "m", Name = "t", IsRevoked = true, ApiKeyHash = "whatever" });
        await db.SaveChangesAsync();

        var result = await svc.ValidateDeviceAsync(id, "any-key");

        Assert.Null(result); // revoked → 401 at the middleware → never reaches /commands
    }
}

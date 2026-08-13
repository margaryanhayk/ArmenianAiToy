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

    // --- Content report ---------------------------------------------------
    // Added 2026-08-13: the toy now also reports what is on its SD card, so a
    // parent can be told whether a library update actually landed. Before
    // this the only confirmation was listening to a story.

    [Fact]
    public async Task UpdateFirmwareReport_StampsTheContentReport()
    {
        var (svc, db) = Create();
        var id = Guid.NewGuid();
        db.Add(new Device { Id = id, MacAddress = "m", Name = "t" });
        await db.SaveChangesAsync();

        await svc.UpdateFirmwareReportAsync(id, new DeviceHeartbeatRequest(
            ContentIndexSchema: 7,
            ContentStories: "ulik:12,anban-huri:9",
            ContentGameClips: 104,
            ContentVoiceClips: 42,
            ContentMusicTracks: 0), Now);

        var d = await db.Set<Device>().FirstAsync(x => x.Id == id);
        Assert.Equal(7, d.ContentIndexSchema);
        Assert.Equal("ulik:12,anban-huri:9", d.ContentStories);
        Assert.Equal(104, d.ContentGameClips);
        Assert.Equal(42, d.ContentVoiceClips);
        Assert.Equal(0, d.ContentMusicTracks);
        Assert.Equal(Now, d.ContentReportedAt);
    }

    [Fact]
    public async Task UpdateFirmwareReport_ContentOnlyBody_IsPersisted()
    {
        // KEYSTONE. The toy sends the content block only when its card
        // CHANGED, so the report that matters most — the one right after a
        // sync — carries no firmware fields at all. If HasAnyFirmwareField
        // did not account for it, the controller would drop the very report
        // this feature exists to collect.
        var (svc, db) = Create();
        var id = Guid.NewGuid();
        db.Add(new Device { Id = id, MacAddress = "m", Name = "t" });
        await db.SaveChangesAsync();

        var report = new DeviceHeartbeatRequest(ContentStories: "ulik:12");
        Assert.True(report.HasAnyContentField);
        Assert.True(report.HasAnyFirmwareField);

        await svc.UpdateFirmwareReportAsync(id, report, Now);

        Assert.Equal("ulik:12",
            (await db.Set<Device>().FirstAsync(x => x.Id == id)).ContentStories);
    }

    [Fact]
    public async Task UpdateFirmwareReport_FirmwareOnlyBody_NeverBlanksTheContentReport()
    {
        // KEYSTONE. Most heartbeats carry firmware fields and no content
        // block. Blanking on those would make the dashboard flip to "we don't
        // know" every minute between syncs.
        var (svc, db) = Create();
        var id = Guid.NewGuid();
        db.Add(new Device
        {
            Id = id, MacAddress = "m", Name = "t",
            ContentStories = "ulik:12", ContentIndexSchema = 7,
            ContentGameClips = 104, ContentVoiceClips = 42, ContentMusicTracks = 0,
            ContentReportedAt = Now.AddHours(-3),
        });
        await db.SaveChangesAsync();

        await svc.UpdateFirmwareReportAsync(
            id, new DeviceHeartbeatRequest(FirmwareVersion: "1.2.1"), Now);

        var d = await db.Set<Device>().FirstAsync(x => x.Id == id);
        Assert.Equal("1.2.1", d.FirmwareVersion);
        Assert.Equal("ulik:12", d.ContentStories);
        Assert.Equal(7, d.ContentIndexSchema);
        Assert.Equal(104, d.ContentGameClips);
        // Not re-stamped: the toy said nothing about its content this time.
        Assert.Equal(Now.AddHours(-3), d.ContentReportedAt);
    }

    [Fact]
    public void BodylessHeartbeat_ReportsNothing()
    {
        // The legacy presence-only heartbeat must still do no DB write.
        var empty = new DeviceHeartbeatRequest();
        Assert.False(empty.HasAnyContentField);
        Assert.False(empty.HasAnyFirmwareField);
    }

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

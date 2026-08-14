using System.Text.Json;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for the device command queue (OTA foundation). Pins: unknown types
/// rejected, per-device isolation, expiry, Pending→Sent, ack changes status,
/// idempotent duplicate ack, wrong-device ack is NotFound.
/// </summary>
public class DeviceCommandServiceTests
{
    private sealed class TestDb : DbContext
    {
        public TestDb(DbContextOptions<TestDb> o) : base(o) { }
        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<DeviceCommand>(e =>
            {
                e.HasKey(c => c.Id);
                e.Ignore(c => c.Device);
                e.Property(c => c.Status).HasConversion<string>();
            });
        }
    }

    private static (DeviceCommandService Service, TestDb Db) Create()
    {
        var db = new TestDb(new DbContextOptionsBuilder<TestDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (new DeviceCommandService(db), db);
    }

    private static readonly DateTime Now = new(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Enqueue_UnknownType_ReturnsNull_AndQueuesNothing()
    {
        var (svc, db) = Create();

        var result = await svc.EnqueueAsync(Guid.NewGuid(), "reboot_device", null, null, Now);

        Assert.Null(result);
        Assert.Empty(db.Set<DeviceCommand>());
    }

    [Fact]
    public async Task Enqueue_FirmwareUpdate_CreatesPending()
    {
        var (svc, _) = Create();

        var cmd = await svc.EnqueueAsync(
            Guid.NewGuid(), DeviceCommandTypes.FirmwareUpdate, "{\"version\":\"1.0.1\"}", null, Now);

        Assert.NotNull(cmd);
        Assert.Equal(DeviceCommandStatus.Pending, cmd!.Status);
        Assert.Equal("firmware_update", cmd.Type);
    }

    [Fact]
    public async Task Poll_ReturnsOnlyOwnDeviceCommands()
    {
        var (svc, _) = Create();
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        await svc.EnqueueAsync(mine, DeviceCommandTypes.FirmwareUpdate, null, null, Now);
        await svc.EnqueueAsync(other, DeviceCommandTypes.FirmwareUpdate, null, null, Now);

        var got = await svc.PollAsync(mine, Now);

        Assert.Single(got);
    }

    [Fact]
    public async Task Poll_DoesNotReturnExpired_AndMarksExpired()
    {
        var (svc, db) = Create();
        var dev = Guid.NewGuid();
        var cmd = await svc.EnqueueAsync(
            dev, DeviceCommandTypes.FirmwareUpdate, null, Now.AddMinutes(-1), Now);

        var got = await svc.PollAsync(dev, Now);

        Assert.Empty(got);
        var stored = await db.Set<DeviceCommand>().FirstAsync(c => c.Id == cmd!.Id);
        Assert.Equal(DeviceCommandStatus.Expired, stored.Status);
    }

    [Fact]
    public async Task Poll_MarksPendingAsSent()
    {
        var (svc, db) = Create();
        var dev = Guid.NewGuid();
        var cmd = await svc.EnqueueAsync(dev, DeviceCommandTypes.FirmwareUpdate, null, null, Now);

        var got = await svc.PollAsync(dev, Now);

        Assert.Single(got);
        var stored = await db.Set<DeviceCommand>().FirstAsync(c => c.Id == cmd!.Id);
        Assert.Equal(DeviceCommandStatus.Sent, stored.Status);
        Assert.Equal(Now, stored.SentAt);
    }

    // ── hasCommands on the heartbeat: the toy stops polling on its own cadence ──

    [Fact]
    public async Task HasDeliverableCommand_PendingCommand_IsTrue_AndConsumesNothing()
    {
        var (svc, db) = Create();
        var dev = Guid.NewGuid();
        var cmd = await svc.EnqueueAsync(dev, DeviceCommandTypes.FirmwareUpdate, null, null, Now);

        Assert.True(await svc.HasDeliverableCommandAsync(dev, Now));

        // KEYSTONE: read-only. If this ever marked Pending → Sent, the flag
        // would consume the delivery the poll is still responsible for.
        var stored = await db.Set<DeviceCommand>().FirstAsync(c => c.Id == cmd!.Id);
        Assert.Equal(DeviceCommandStatus.Pending, stored.Status);
        Assert.Null(stored.SentAt);
    }

    [Fact]
    public async Task HasDeliverableCommand_AlreadySentButUnacked_IsTrue()
    {
        // At-least-once transport: a Sent command is re-delivered until acked,
        // so a toy that rebooted mid-command must still be told to come back.
        var (svc, _) = Create();
        var dev = Guid.NewGuid();
        await svc.EnqueueAsync(dev, DeviceCommandTypes.FirmwareUpdate, null, null, Now);
        await svc.PollAsync(dev, Now);

        Assert.True(await svc.HasDeliverableCommandAsync(dev, Now));
    }

    [Fact]
    public async Task HasDeliverableCommand_NoRows_IsFalse()
    {
        var (svc, _) = Create();

        Assert.False(await svc.HasDeliverableCommandAsync(Guid.NewGuid(), Now));
    }

    [Fact]
    public async Task HasDeliverableCommand_OnlyAckedOrExpired_IsFalse()
    {
        var (svc, _) = Create();
        var dev = Guid.NewGuid();

        // Acked — terminal.
        var acked = await svc.EnqueueAsync(dev, DeviceCommandTypes.FirmwareUpdate, null, null, Now);
        await svc.AckAsync(dev, acked!.Id, new DeviceCommandAckRequest(Result: "ok"), Now);

        // Expired but never swept: still Pending in the row, past its TTL.
        // Reading this as deliverable would keep the toy polling forever for
        // something it can never be given.
        await svc.EnqueueAsync(
            dev, DeviceCommandTypes.RefreshStoryManifest, null, Now.AddMinutes(-1), Now);

        Assert.False(await svc.HasDeliverableCommandAsync(dev, Now));
    }

    [Fact]
    public async Task HasDeliverableCommand_AnotherDevicesCommand_IsFalse()
    {
        var (svc, _) = Create();
        var other = Guid.NewGuid();
        await svc.EnqueueAsync(other, DeviceCommandTypes.FirmwareUpdate, null, null, Now);

        Assert.False(await svc.HasDeliverableCommandAsync(Guid.NewGuid(), Now));
    }

    [Fact]
    public async Task Enqueue_RefreshStoryManifest_IsAKnownType()
    {
        var (svc, _) = Create();

        var cmd = await svc.EnqueueAsync(
            Guid.NewGuid(), DeviceCommandTypes.RefreshStoryManifest, null, null, Now);

        Assert.NotNull(cmd);
        Assert.Equal("refresh_story_manifest", cmd!.Type);
        Assert.Equal(DeviceCommandStatus.Pending, cmd.Status);
    }

    [Fact]
    public async Task Ack_ChangesStatusToAcked_AndStoresDiagnostics()
    {
        var (svc, db) = Create();
        var dev = Guid.NewGuid();
        var cmd = await svc.EnqueueAsync(dev, DeviceCommandTypes.FirmwareUpdate, null, null, Now);
        await svc.PollAsync(dev, Now);

        var ack = new DeviceCommandAckRequest(
            Result: "ok",
            FirmwareVersion: "1.0.1",
            Diagnostics: JsonDocument.Parse("{\"freeBytes\":12345}").RootElement);
        var outcome = await svc.AckAsync(dev, cmd!.Id, ack, Now.AddSeconds(30));

        Assert.Equal(DeviceCommandAckOutcome.Acked, outcome);
        var stored = await db.Set<DeviceCommand>().FirstAsync(c => c.Id == cmd.Id);
        Assert.Equal(DeviceCommandStatus.Acked, stored.Status);
        Assert.Equal("1.0.1", stored.AckFirmwareVersion);
        Assert.Contains("freeBytes", stored.AckDiagnosticsJson);
    }

    [Fact]
    public async Task Ack_ResultFailed_SetsFailed()
    {
        var (svc, db) = Create();
        var dev = Guid.NewGuid();
        var cmd = await svc.EnqueueAsync(dev, DeviceCommandTypes.FirmwareUpdate, null, null, Now);

        await svc.AckAsync(dev, cmd!.Id, new DeviceCommandAckRequest(Result: "failed", Error: "sha mismatch"), Now);

        var stored = await db.Set<DeviceCommand>().FirstAsync(c => c.Id == cmd.Id);
        Assert.Equal(DeviceCommandStatus.Failed, stored.Status);
        Assert.Equal("sha mismatch", stored.Error);
    }

    [Fact]
    public async Task Ack_Duplicate_IsSafe_NoSecondMutation()
    {
        var (svc, db) = Create();
        var dev = Guid.NewGuid();
        var cmd = await svc.EnqueueAsync(dev, DeviceCommandTypes.FirmwareUpdate, null, null, Now);

        var first = await svc.AckAsync(dev, cmd!.Id, new DeviceCommandAckRequest(Result: "ok"), Now);
        var ackedAt = (await db.Set<DeviceCommand>().FirstAsync(c => c.Id == cmd.Id)).AckedAt;
        // second ack claims failure — must NOT flip the already-terminal command
        var second = await svc.AckAsync(dev, cmd.Id, new DeviceCommandAckRequest(Result: "failed"), Now.AddMinutes(5));

        Assert.Equal(DeviceCommandAckOutcome.Acked, first);
        Assert.Equal(DeviceCommandAckOutcome.Acked, second);
        var stored = await db.Set<DeviceCommand>().FirstAsync(c => c.Id == cmd.Id);
        Assert.Equal(DeviceCommandStatus.Acked, stored.Status); // still Acked, not Failed
        Assert.Equal(ackedAt, stored.AckedAt);                  // AckedAt unchanged
    }

    [Fact]
    public async Task Ack_WrongDevice_ReturnsNotFound_AndDoesNotMutate()
    {
        var (svc, db) = Create();
        var owner = Guid.NewGuid();
        var attacker = Guid.NewGuid();
        var cmd = await svc.EnqueueAsync(owner, DeviceCommandTypes.FirmwareUpdate, null, null, Now);

        var outcome = await svc.AckAsync(attacker, cmd!.Id, new DeviceCommandAckRequest(Result: "ok"), Now);

        Assert.Equal(DeviceCommandAckOutcome.NotFound, outcome);
        var stored = await db.Set<DeviceCommand>().FirstAsync(c => c.Id == cmd.Id);
        Assert.Equal(DeviceCommandStatus.Pending, stored.Status); // untouched
    }
}

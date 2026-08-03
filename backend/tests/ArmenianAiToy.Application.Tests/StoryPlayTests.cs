using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Slice-A story-play reporting tests: the device-side ingest
/// (<see cref="DeviceService.ReportStoryPlaysAsync"/> — idempotent,
/// at-least-once-safe, bounded) and the parent-side read
/// (<see cref="ParentService.GetStoryPlaysAsync"/> — ownership-gated,
/// paginated newest-first, whole-history totals).
/// </summary>
public class StoryPlayTests
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
            mb.Entity<StoryPlay>(e =>
            {
                e.HasKey(p => p.Id);
                e.Ignore(p => p.Device);
            });
            mb.Entity<ParentDevice>(e =>
            {
                e.HasKey(pd => new { pd.ParentId, pd.DeviceId });
                e.Ignore(pd => pd.Parent);
                e.Ignore(pd => pd.Device);
            });
        }
    }

    private static TestDb NewDb() => new(new DbContextOptionsBuilder<TestDb>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DeviceService NewDeviceService(TestDb db)
        => new(db, Substitute.For<ILogger<DeviceService>>());

    private static ParentService NewParentService(TestDb db)
        => new(db, new ConfigurationBuilder().Build(),
            Substitute.For<ILogger<ParentService>>());

    private static readonly DateTime Now = new(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);

    private static async Task<Guid> SeedDeviceAsync(TestDb db)
    {
        var id = Guid.NewGuid();
        db.Add(new Device { Id = id, MacAddress = "m-" + id.ToString("N")[..6], Name = "toy" });
        await db.SaveChangesAsync();
        return id;
    }

    // ---------------------------------------------------------------
    // Ingest — DeviceService.ReportStoryPlaysAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task Report_NewEvents_InsertsRows_WithTimeSemantics()
    {
        var db = NewDb();
        var svc = NewDeviceService(db);
        var deviceId = await SeedDeviceAsync(db);

        var accepted = await svc.ReportStoryPlaysAsync(deviceId, new StoryPlayReportRequest(
        [
            // Fresh event with a known relative age → exact-ish timestamp.
            new StoryPlayReportEvent("b1-1", "anban-huri", Finished: true, Source: "sd", SecondsAgo: 600),
            // Survived a reboot → no age → approximate upload-time stamp.
            new StoryPlayReportEvent("b0-7", "ulik", Finished: false, Source: "stream"),
        ]), Now);

        Assert.Equal(2, accepted);
        var rows = await db.Set<StoryPlay>().OrderBy(p => p.ClientEventKey).ToListAsync();
        Assert.Equal(2, rows.Count);

        var aged = rows.Single(r => r.ClientEventKey == "b1-1");
        Assert.Equal("anban-huri", aged.StoryId);
        Assert.True(aged.Finished);
        Assert.Equal("sd", aged.Source);
        Assert.Equal(Now.AddSeconds(-600), aged.PlayedAtUtc);
        Assert.False(aged.TimeIsApproximate);

        var rebooted = rows.Single(r => r.ClientEventKey == "b0-7");
        Assert.Equal(Now, rebooted.PlayedAtUtc);
        Assert.True(rebooted.TimeIsApproximate);
        Assert.Equal("stream", rebooted.Source);
    }

    // KEYSTONE: the upload transport is at-least-once — a re-uploaded batch
    // (device missed the 2xx) must never produce duplicate rows.
    [Fact]
    public async Task Report_DuplicateUpload_IsIdempotent()
    {
        var db = NewDb();
        var svc = NewDeviceService(db);
        var deviceId = await SeedDeviceAsync(db);
        var batch = new StoryPlayReportRequest(
            [new StoryPlayReportEvent("b1-1", "anban-huri", true, "sd", 60)]);

        var first = await svc.ReportStoryPlaysAsync(deviceId, batch, Now);
        var second = await svc.ReportStoryPlaysAsync(deviceId, batch, Now.AddMinutes(5));

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(1, await db.Set<StoryPlay>().CountAsync());
    }

    [Fact]
    public async Task Report_SameKeyOnDifferentDevices_BothAccepted()
    {
        var db = NewDb();
        var svc = NewDeviceService(db);
        var deviceA = await SeedDeviceAsync(db);
        var deviceB = await SeedDeviceAsync(db);
        var batch = new StoryPlayReportRequest(
            [new StoryPlayReportEvent("b1-1", "ulik", false, "sd", 5)]);

        Assert.Equal(1, await svc.ReportStoryPlaysAsync(deviceA, batch, Now));
        Assert.Equal(1, await svc.ReportStoryPlaysAsync(deviceB, batch, Now));
        Assert.Equal(2, await db.Set<StoryPlay>().CountAsync());
    }

    [Fact]
    public async Task Report_MalformedEvents_SkippedWithoutRejectingSiblings()
    {
        var db = NewDb();
        var svc = NewDeviceService(db);
        var deviceId = await SeedDeviceAsync(db);

        var accepted = await svc.ReportStoryPlaysAsync(deviceId, new StoryPlayReportRequest(
        [
            new StoryPlayReportEvent(null, "anban-huri"),              // no key
            new StoryPlayReportEvent("b1-2", null),                     // no story
            new StoryPlayReportEvent("b1-3", new string('x', 65)),      // story too long
            new StoryPlayReportEvent(new string('k', 65), "ulik"),      // key too long
            new StoryPlayReportEvent("b1-4", "ulik", true, "sd", 1),    // valid
            new StoryPlayReportEvent("b1-4", "ulik", true, "sd", 1),    // in-batch dup
        ]), Now);

        Assert.Equal(1, accepted);
        var row = await db.Set<StoryPlay>().SingleAsync();
        Assert.Equal("b1-4", row.ClientEventKey);
    }

    [Fact]
    public async Task Report_UnknownSource_CollapsesToOther()
    {
        var db = NewDb();
        var svc = NewDeviceService(db);
        var deviceId = await SeedDeviceAsync(db);

        await svc.ReportStoryPlaysAsync(deviceId, new StoryPlayReportRequest(
        [
            new StoryPlayReportEvent("k1", "a", Source: "PACK"),        // case-normalized
            new StoryPlayReportEvent("k2", "a", Source: "sd-card!!"),   // unknown → other
            new StoryPlayReportEvent("k3", "a", Source: null),          // absent → other
        ]), Now);

        var sources = await db.Set<StoryPlay>()
            .OrderBy(p => p.ClientEventKey).Select(p => p.Source).ToListAsync();
        Assert.Equal(["pack", "other", "other"], sources);
    }

    [Fact]
    public async Task Report_OutOfRangeSecondsAgo_FallsBackToApproximate()
    {
        var db = NewDb();
        var svc = NewDeviceService(db);
        var deviceId = await SeedDeviceAsync(db);

        await svc.ReportStoryPlaysAsync(deviceId, new StoryPlayReportRequest(
        [
            new StoryPlayReportEvent("k1", "a", SecondsAgo: -5),
            new StoryPlayReportEvent("k2", "a", SecondsAgo: (long)TimeSpan.FromDays(365).TotalSeconds),
        ]), Now);

        var rows = await db.Set<StoryPlay>().ToListAsync();
        Assert.All(rows, r =>
        {
            Assert.Equal(Now, r.PlayedAtUtc);
            Assert.True(r.TimeIsApproximate);
        });
    }

    [Fact]
    public async Task Report_CapsBatchAtMaxEvents()
    {
        var db = NewDb();
        var svc = NewDeviceService(db);
        var deviceId = await SeedDeviceAsync(db);

        var events = Enumerable.Range(0, StoryPlayReportRequest.MaxEvents + 5)
            .Select(i => new StoryPlayReportEvent("k" + i, "a", false, "sd", 1))
            .ToList();
        var accepted = await svc.ReportStoryPlaysAsync(
            deviceId, new StoryPlayReportRequest(events), Now);

        Assert.Equal(StoryPlayReportRequest.MaxEvents, accepted);
    }

    [Fact]
    public async Task Report_EmptyOrNullBatch_AcceptsNothing()
    {
        var db = NewDb();
        var svc = NewDeviceService(db);
        var deviceId = await SeedDeviceAsync(db);

        Assert.Equal(0, await svc.ReportStoryPlaysAsync(
            deviceId, new StoryPlayReportRequest(null), Now));
        Assert.Equal(0, await svc.ReportStoryPlaysAsync(
            deviceId, new StoryPlayReportRequest([]), Now));
        Assert.Equal(0, await db.Set<StoryPlay>().CountAsync());
    }

    // ---------------------------------------------------------------
    // Parent read — ParentService.GetStoryPlaysAsync
    // ---------------------------------------------------------------

    private static async Task<(Guid ParentId, Guid DeviceId)> SeedLinkedAsync(TestDb db)
    {
        var parentId = Guid.NewGuid();
        var deviceId = await SeedDeviceAsync(db);
        db.Add(new ParentDevice { ParentId = parentId, DeviceId = deviceId });
        await db.SaveChangesAsync();
        return (parentId, deviceId);
    }

    private static StoryPlay Play(Guid deviceId, string storyId, DateTime at,
        bool finished = true, string key = null!)
        => new()
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            StoryId = storyId,
            Source = "sd",
            Finished = finished,
            ClientEventKey = key ?? Guid.NewGuid().ToString("N"),
            PlayedAtUtc = at,
            TimeIsApproximate = false,
        };

    // KEYSTONE: ownership — an unlinked device (unknown, or linked to a
    // different parent) yields null, indistinguishable at the controller.
    [Fact]
    public async Task GetStoryPlays_UnlinkedDevice_ReturnsNull()
    {
        var db = NewDb();
        var svc = NewParentService(db);
        var (parentId, deviceId) = await SeedLinkedAsync(db);
        var strangerParent = Guid.NewGuid();
        var unknownDevice = Guid.NewGuid();

        Assert.Null(await svc.GetStoryPlaysAsync(strangerParent, deviceId, 20, 0));
        Assert.Null(await svc.GetStoryPlaysAsync(parentId, unknownDevice, 20, 0));
    }

    [Fact]
    public async Task GetStoryPlays_NewestFirst_Paginated_WithTotal()
    {
        var db = NewDb();
        var svc = NewParentService(db);
        var (parentId, deviceId) = await SeedLinkedAsync(db);
        for (var i = 0; i < 5; i++)
        {
            db.Add(Play(deviceId, "s" + i, Now.AddHours(-i)));
        }
        await db.SaveChangesAsync();

        var page = await svc.GetStoryPlaysAsync(parentId, deviceId, limit: 2, offset: 1);

        Assert.NotNull(page);
        Assert.Equal(5, page!.Total);
        // Newest first is s0 (Now), s1, s2 ... — offset 1, limit 2 → s1, s2.
        Assert.Equal(["s1", "s2"], page.Plays.Select(p => p.StoryId).ToList());
    }

    [Fact]
    public async Task GetStoryPlays_Totals_AreWholeHistory_AndFinishedGated()
    {
        var db = NewDb();
        var svc = NewParentService(db);
        var (parentId, deviceId) = await SeedLinkedAsync(db);
        // anban-huri: 3 plays, 2 finished. ulik: 1 play, 0 finished.
        db.Add(Play(deviceId, "anban-huri", Now.AddHours(-1), finished: true));
        db.Add(Play(deviceId, "anban-huri", Now.AddHours(-2), finished: true));
        db.Add(Play(deviceId, "anban-huri", Now.AddHours(-3), finished: false));
        db.Add(Play(deviceId, "ulik", Now.AddHours(-4), finished: false));
        await db.SaveChangesAsync();

        // Tiny page — totals must still cover the whole history.
        var page = await svc.GetStoryPlaysAsync(parentId, deviceId, limit: 1, offset: 0);

        Assert.NotNull(page);
        Assert.Equal(2, page!.Totals.Count);
        var anban = page.Totals.Single(t => t.StoryId == "anban-huri");
        Assert.Equal(3, anban.Count);
        Assert.Equal(2, anban.FinishedCount);
        var ulik = page.Totals.Single(t => t.StoryId == "ulik");
        Assert.Equal(1, ulik.Count);
        Assert.Equal(0, ulik.FinishedCount);
        // Most-listened first.
        Assert.Equal("anban-huri", page.Totals[0].StoryId);
    }

    [Fact]
    public async Task GetStoryPlays_OtherDevicesPlays_NotIncluded()
    {
        var db = NewDb();
        var svc = NewParentService(db);
        var (parentId, deviceId) = await SeedLinkedAsync(db);
        var otherDevice = await SeedDeviceAsync(db);
        db.Add(Play(deviceId, "mine", Now));
        db.Add(Play(otherDevice, "theirs", Now));
        await db.SaveChangesAsync();

        var page = await svc.GetStoryPlaysAsync(parentId, deviceId, 20, 0);

        Assert.NotNull(page);
        Assert.Equal(1, page!.Total);
        Assert.Equal("mine", Assert.Single(page.Plays).StoryId);
        Assert.Equal("mine", Assert.Single(page.Totals).StoryId);
    }
}

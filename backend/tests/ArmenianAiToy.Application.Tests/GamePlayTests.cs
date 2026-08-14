using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Offline-game reporting tests: the device-side ingest
/// (<see cref="DeviceService.ReportGamePlaysAsync"/> — idempotent,
/// at-least-once-safe, bounded vocabulary) and the parent-side read
/// (<see cref="ParentService.GetGamePlaysAsync"/> — ownership-gated,
/// paginated newest-first, whole-history totals).
///
/// <para>
/// Deliberate mirror of <see cref="StoryPlayTests"/>, because the gap is the
/// same one: the offline games run entirely on the toy from SD clips and make
/// no network call, so without this pipeline a child can play all afternoon
/// and the parent dashboard shows nothing.
/// </para>
/// </summary>
public class GamePlayTests
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
            mb.Entity<GamePlay>(e =>
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

    private static readonly DateTime Now = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

    private static async Task<Guid> SeedDeviceAsync(TestDb db)
    {
        var id = Guid.NewGuid();
        db.Add(new Device { Id = id, MacAddress = "m-" + id.ToString("N")[..6], Name = "toy" });
        await db.SaveChangesAsync();
        return id;
    }

    // ---------------------------------------------------------------
    // Ingest — DeviceService.ReportGamePlaysAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task Report_NewEvents_InsertsRows_WithTimeSemantics()
    {
        var db = NewDb();
        var svc = NewDeviceService(db);
        var deviceId = await SeedDeviceAsync(db);

        var accepted = await svc.ReportGamePlaysAsync(deviceId, new GamePlayReportRequest(
        [
            // Fresh event with a known relative age → exact-ish timestamp.
            new GamePlayReportEvent("b1-1", "button-simon",
                Rounds: 4, Outcome: "lost", Score: 3, SecondsAgo: 600),
            // Survived a reboot → no age → approximate upload-time stamp.
            new GamePlayReportEvent("b0-7", "who-first", Rounds: 5, Outcome: "stopped"),
        ]), Now);

        Assert.Equal(2, accepted);
        var rows = await db.Set<GamePlay>().OrderBy(p => p.ClientEventKey).ToListAsync();
        Assert.Equal(2, rows.Count);

        var aged = rows.Single(r => r.ClientEventKey == "b1-1");
        Assert.Equal("button-simon", aged.GameKey);
        Assert.Equal(4, aged.Rounds);
        Assert.Equal("lost", aged.Outcome);
        Assert.Equal(3, aged.Score);
        Assert.Equal(Now.AddSeconds(-600), aged.PlayedAtUtc);
        Assert.False(aged.TimeIsApproximate);

        var rebooted = rows.Single(r => r.ClientEventKey == "b0-7");
        Assert.Equal("who-first", rebooted.GameKey);
        Assert.Equal(Now, rebooted.PlayedAtUtc);
        Assert.True(rebooted.TimeIsApproximate);
        // Simon's score is Simon's alone — the buzzer reports none, and null
        // must stay null rather than becoming a zero a dashboard would render.
        Assert.Null(rebooted.Score);
    }

    // KEYSTONE: the upload transport is at-least-once — a re-uploaded batch
    // (device missed the 2xx) must never produce duplicate rows, or a single
    // afternoon of Simon would read as a dozen sessions.
    [Fact]
    public async Task Report_DuplicateUpload_IsIdempotent()
    {
        var db = NewDb();
        var svc = NewDeviceService(db);
        var deviceId = await SeedDeviceAsync(db);
        var batch = new GamePlayReportRequest(
            [new GamePlayReportEvent("b1-1", "mind-reader", 4, "won", null, 60)]);

        var first = await svc.ReportGamePlaysAsync(deviceId, batch, Now);
        var second = await svc.ReportGamePlaysAsync(deviceId, batch, Now.AddMinutes(5));

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(1, await db.Set<GamePlay>().CountAsync());
    }

    [Fact]
    public async Task Report_SameKeyOnDifferentDevices_BothAccepted()
    {
        var db = NewDb();
        var svc = NewDeviceService(db);
        var deviceA = await SeedDeviceAsync(db);
        var deviceB = await SeedDeviceAsync(db);
        var batch = new GamePlayReportRequest(
            [new GamePlayReportEvent("b1-1", "mind-reader", 4, "won", null, 5)]);

        Assert.Equal(1, await svc.ReportGamePlaysAsync(deviceA, batch, Now));
        Assert.Equal(1, await svc.ReportGamePlaysAsync(deviceB, batch, Now));
        Assert.Equal(2, await db.Set<GamePlay>().CountAsync());
    }

    // KEYSTONE: the game key is a label a parent reads and the dashboard
    // translates, so an unknown one has no honest rendering and the row is
    // never written. This is stricter than the story-play Source field, which
    // collapses to "other" — there is no "other" game to name.
    [Fact]
    public async Task Report_UnknownGameKey_IsRejected_NotStored()
    {
        var db = NewDb();
        var svc = NewDeviceService(db);
        var deviceId = await SeedDeviceAsync(db);

        var accepted = await svc.ReportGamePlaysAsync(deviceId, new GamePlayReportRequest(
        [
            new GamePlayReportEvent("k1", "chess"),            // not a game we ship
            new GamePlayReportEvent("k2", "story-pauses"),     // a clip bank, not a game
            new GamePlayReportEvent("k3", ""),                 // blank
            new GamePlayReportEvent("k4", null),               // absent
            new GamePlayReportEvent("k5", "mind-reader"),      // valid
        ]), Now);

        Assert.Equal(1, accepted);
        var row = await db.Set<GamePlay>().SingleAsync();
        Assert.Equal("mind-reader", row.GameKey);
    }

    [Fact]
    public async Task Report_GameKeyCase_IsNormalized()
    {
        var db = NewDb();
        var svc = NewDeviceService(db);
        var deviceId = await SeedDeviceAsync(db);

        await svc.ReportGamePlaysAsync(deviceId, new GamePlayReportRequest(
            [new GamePlayReportEvent("k1", " Button-Simon ")]), Now);

        Assert.Equal("button-simon", (await db.Set<GamePlay>().SingleAsync()).GameKey);
    }

    // KEYSTONE: the outcome vocabulary is closed. Unlike the game key it has
    // an honest fallback — null reads as "the toy did not say", which is
    // exactly what an unrecognised value means — so the session is still
    // recorded rather than lost.
    [Fact]
    public async Task Report_OutcomeVocabulary_IsBounded_UnknownBecomesNull()
    {
        var db = NewDb();
        var svc = NewDeviceService(db);
        var deviceId = await SeedDeviceAsync(db);

        await svc.ReportGamePlaysAsync(deviceId, new GamePlayReportRequest(
        [
            new GamePlayReportEvent("k1", "who-first", Outcome: "WON"),
            new GamePlayReportEvent("k2", "who-first", Outcome: "lost"),
            new GamePlayReportEvent("k3", "who-first", Outcome: "stopped"),
            new GamePlayReportEvent("k4", "who-first", Outcome: "crushed it"),
            new GamePlayReportEvent("k5", "who-first", Outcome: null),
        ]), Now);

        var outcomes = await db.Set<GamePlay>()
            .OrderBy(p => p.ClientEventKey).Select(p => p.Outcome).ToListAsync();
        Assert.Equal(["won", "lost", "stopped", null, null], outcomes);
    }

    [Fact]
    public async Task Report_MalformedEvents_SkippedWithoutRejectingSiblings()
    {
        var db = NewDb();
        var svc = NewDeviceService(db);
        var deviceId = await SeedDeviceAsync(db);

        var accepted = await svc.ReportGamePlaysAsync(deviceId, new GamePlayReportRequest(
        [
            new GamePlayReportEvent(null, "simon"),                   // no key
            new GamePlayReportEvent(new string('k', 65), "simon"),    // key too long
            new GamePlayReportEvent("b1-4", "button-simon", 2, "won", 2, 1),   // valid
            new GamePlayReportEvent("b1-4", "button-simon", 2, "won", 2, 1),   // in-batch dup
        ]), Now);

        Assert.Equal(1, accepted);
        var row = await db.Set<GamePlay>().SingleAsync();
        Assert.Equal("b1-4", row.ClientEventKey);
    }

    [Fact]
    public async Task Report_OutOfRangeCounts_CollapseToNull_RowStillWritten()
    {
        var db = NewDb();
        var svc = NewDeviceService(db);
        var deviceId = await SeedDeviceAsync(db);

        await svc.ReportGamePlaysAsync(deviceId, new GamePlayReportRequest(
        [
            new GamePlayReportEvent("k1", "button-simon",
                Rounds: -3, Score: GamePlayReportRequest.MaxCount + 1),
        ]), Now);

        var row = await db.Set<GamePlay>().SingleAsync();
        // The session happened; only the implausible numbers are dropped.
        Assert.Null(row.Rounds);
        Assert.Null(row.Score);
        Assert.Equal("button-simon", row.GameKey);
    }

    [Fact]
    public async Task Report_OutOfRangeSecondsAgo_FallsBackToApproximate()
    {
        var db = NewDb();
        var svc = NewDeviceService(db);
        var deviceId = await SeedDeviceAsync(db);

        await svc.ReportGamePlaysAsync(deviceId, new GamePlayReportRequest(
        [
            new GamePlayReportEvent("k1", "who-first", SecondsAgo: -5),
            new GamePlayReportEvent("k2", "who-first",
                SecondsAgo: (long)TimeSpan.FromDays(365).TotalSeconds),
        ]), Now);

        var rows = await db.Set<GamePlay>().ToListAsync();
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

        var events = Enumerable.Range(0, GamePlayReportRequest.MaxEvents + 5)
            .Select(i => new GamePlayReportEvent("k" + i, "who-first", 5, "stopped", null, 1))
            .ToList();
        var accepted = await svc.ReportGamePlaysAsync(
            deviceId, new GamePlayReportRequest(events), Now);

        Assert.Equal(GamePlayReportRequest.MaxEvents, accepted);
    }

    [Fact]
    public async Task Report_EmptyOrNullBatch_AcceptsNothing()
    {
        var db = NewDb();
        var svc = NewDeviceService(db);
        var deviceId = await SeedDeviceAsync(db);

        Assert.Equal(0, await svc.ReportGamePlaysAsync(
            deviceId, new GamePlayReportRequest(null), Now));
        Assert.Equal(0, await svc.ReportGamePlaysAsync(
            deviceId, new GamePlayReportRequest([]), Now));
        Assert.Equal(0, await db.Set<GamePlay>().CountAsync());
    }

    // Every key the toy can ship clips for is a key the toy can report. A
    // game whose clips sync but whose plays are rejected is exactly the gap
    // this slice exists to close, so the two lists must not drift apart.
    [Fact]
    public async Task Report_EveryShippedGameKey_IsAccepted()
    {
        var db = NewDb();
        var svc = NewDeviceService(db);
        var deviceId = await SeedDeviceAsync(db);

        var events = GamePlayReportRequest.AllowedGameKeys
            .Select((k, i) => new GamePlayReportEvent("k" + i, k))
            .ToList();

        Assert.Equal(
            GamePlayReportRequest.AllowedGameKeys.Length,
            await svc.ReportGamePlaysAsync(deviceId, new GamePlayReportRequest(events), Now));
    }

    // ---------------------------------------------------------------
    // Parent read — ParentService.GetGamePlaysAsync
    // ---------------------------------------------------------------

    private static async Task<(Guid ParentId, Guid DeviceId)> SeedLinkedAsync(TestDb db)
    {
        var parentId = Guid.NewGuid();
        var deviceId = await SeedDeviceAsync(db);
        db.Add(new ParentDevice { ParentId = parentId, DeviceId = deviceId });
        await db.SaveChangesAsync();
        return (parentId, deviceId);
    }

    private static GamePlay Game(Guid deviceId, string gameKey, DateTime at,
        string? outcome = "stopped", int? score = null)
        => new()
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            GameKey = gameKey,
            ClientEventKey = Guid.NewGuid().ToString("N"),
            PlayedAtUtc = at,
            TimeIsApproximate = false,
            Rounds = 3,
            Outcome = outcome,
            Score = score,
        };

    // KEYSTONE: ownership — an unlinked device (unknown, or linked to a
    // different parent) yields null, indistinguishable at the controller.
    [Fact]
    public async Task GetGamePlays_UnlinkedDevice_ReturnsNull()
    {
        var db = NewDb();
        var svc = NewParentService(db);
        var (parentId, deviceId) = await SeedLinkedAsync(db);
        var strangerParent = Guid.NewGuid();
        var unknownDevice = Guid.NewGuid();

        Assert.Null(await svc.GetGamePlaysAsync(strangerParent, deviceId, 20, 0));
        Assert.Null(await svc.GetGamePlaysAsync(parentId, unknownDevice, 20, 0));
    }

    [Fact]
    public async Task GetGamePlays_NewestFirst_Paginated_WithTotal()
    {
        var db = NewDb();
        var svc = NewParentService(db);
        var (parentId, deviceId) = await SeedLinkedAsync(db);
        var keys = new[] { "mind-reader", "who-first", "button-simon", "sound-detective", "mind-reader" };
        for (var i = 0; i < keys.Length; i++)
        {
            db.Add(Game(deviceId, keys[i], Now.AddHours(-i)));
        }
        await db.SaveChangesAsync();

        var page = await svc.GetGamePlaysAsync(parentId, deviceId, limit: 2, offset: 1);

        Assert.NotNull(page);
        Assert.Equal(5, page!.Total);
        Assert.Equal(["who-first", "button-simon"], page.Plays.Select(p => p.GameKey).ToList());
    }

    [Fact]
    public async Task GetGamePlays_Totals_AreWholeHistory_MostPlayedFirst()
    {
        var db = NewDb();
        var svc = NewParentService(db);
        var (parentId, deviceId) = await SeedLinkedAsync(db);
        db.Add(Game(deviceId, "button-simon", Now.AddHours(-1), "lost", 3));
        db.Add(Game(deviceId, "button-simon", Now.AddHours(-2), "lost", 6));
        db.Add(Game(deviceId, "button-simon", Now.AddHours(-3), "stopped"));
        db.Add(Game(deviceId, "who-first", Now.AddHours(-4)));
        await db.SaveChangesAsync();

        // Tiny page — totals must still cover the whole history.
        var page = await svc.GetGamePlaysAsync(parentId, deviceId, limit: 1, offset: 0);

        Assert.NotNull(page);
        Assert.Equal(2, page!.Totals.Count);
        Assert.Equal("button-simon", page.Totals[0].GameKey);
        Assert.Equal(3, page.Totals[0].Count);
        Assert.Equal(1, page.Totals.Single(t => t.GameKey == "who-first").Count);
    }

    [Fact]
    public async Task GetGamePlays_OtherDevicesPlays_NotIncluded()
    {
        var db = NewDb();
        var svc = NewParentService(db);
        var (parentId, deviceId) = await SeedLinkedAsync(db);
        var otherDevice = await SeedDeviceAsync(db);
        db.Add(Game(deviceId, "mind-reader", Now));
        db.Add(Game(otherDevice, "who-first", Now));
        await db.SaveChangesAsync();

        var page = await svc.GetGamePlaysAsync(parentId, deviceId, 20, 0);

        Assert.NotNull(page);
        Assert.Equal(1, page!.Total);
        Assert.Equal("mind-reader", Assert.Single(page.Plays).GameKey);
        Assert.Equal("mind-reader", Assert.Single(page.Totals).GameKey);
    }

    // ---------------------------------------------------------------
    // Export — a complete export carries what the child actually did
    // ---------------------------------------------------------------

    // Real SQLite rather than the in-memory provider: BuildExportAsync walks
    // the whole AppDbContext model, so the schema has to be the real one.
    [Fact]
    public async Task Export_IncludesGamePlays_ForTheParentsOwnDevices()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        try
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(conn).Options);
            await db.Database.EnsureCreatedAsync();

            var parentId = Guid.NewGuid();
            var deviceId = Guid.NewGuid();
            db.Set<Parent>().Add(new Parent
            {
                Id = parentId,
                Email = "p-" + Guid.NewGuid().ToString("N")[..8] + "@example.com",
                PasswordHash = "hash",
                RegisteredAt = DateTime.UtcNow,
            });
            db.Set<Device>().Add(new Device
            {
                Id = deviceId,
                MacAddress = "mac-" + Guid.NewGuid().ToString("N")[..10],
                Name = "Toy",
                RegisteredAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
            });
            db.Set<ParentDevice>().Add(new ParentDevice
            {
                ParentId = parentId, DeviceId = deviceId, LinkedAt = DateTime.UtcNow,
            });
            db.Set<GamePlay>().Add(new GamePlay
            {
                Id = Guid.NewGuid(),
                DeviceId = deviceId,
                GameKey = "button-simon",
                ClientEventKey = "b1-1",
                PlayedAtUtc = Now,
                TimeIsApproximate = false,
                Rounds = 4,
                Outcome = "lost",
                Score = 3,
            });
            await db.SaveChangesAsync();

            var config = Substitute.For<IConfiguration>();
            config["Jwt:Key"].Returns("TestSecretKeyThatIsLongEnoughForHmacSha256Validation!");
            var service = new ParentService(db, config, Substitute.For<ILogger<ParentService>>());

            var export = await service.BuildExportAsync(parentId);

            Assert.NotNull(export);
            var device = Assert.Single(export!.Devices);
            var play = Assert.Single(device.GamePlays);
            Assert.Equal("button-simon", play.GameKey);
            Assert.Equal("lost", play.Outcome);
            Assert.Equal(3, play.Score);
            Assert.Equal(4, play.Rounds);
        }
        finally
        {
            await conn.DisposeAsync();
        }
    }

    // ---------------------------------------------------------------
    // Migration
    // ---------------------------------------------------------------

    // The AddGamePlays migration is HAND-WRITTEN (dotnet-ef cannot run against
    // this solution — verified again while building this slice: the tool
    // fails with "Could not load file or assembly
    // Microsoft.EntityFrameworkCore.Design"). A hand-written migration that
    // omits the [DbContext] attribute is not associated with AppDbContext and
    // Migrate() SILENTLY SKIPS it — the trap AddDeviceSdCardOk records having
    // learned the hard way, and one that no unit test using
    // EnsureCreated/InMemory would ever catch, because both build the schema
    // from the model instead of from the migrations.
    //
    // So this runs the real migration chain end to end and then writes a row.
    [Fact]
    public async Task Migrations_CreateGamePlaysTable_AndAcceptARow()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        try
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(conn).Options);
            await db.Database.MigrateAsync();

            var deviceId = Guid.NewGuid();
            db.Set<Device>().Add(new Device
            {
                Id = deviceId,
                MacAddress = "mac-" + Guid.NewGuid().ToString("N")[..10],
                Name = "Toy",
                RegisteredAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
            });
            db.Set<GamePlay>().Add(new GamePlay
            {
                Id = Guid.NewGuid(),
                DeviceId = deviceId,
                GameKey = "button-simon",
                ClientEventKey = "b1-1",
                PlayedAtUtc = Now,
                TimeIsApproximate = false,
                Score = 3,
            });
            await db.SaveChangesAsync();

            Assert.Equal(1, await db.Set<GamePlay>().CountAsync());
        }
        finally
        {
            await conn.DisposeAsync();
        }
    }
}

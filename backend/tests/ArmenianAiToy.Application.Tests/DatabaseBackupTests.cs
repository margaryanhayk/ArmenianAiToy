using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Domain.Enums;
using ArmenianAiToy.Infrastructure.Background;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text;
using Xunit;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tier-1 backup slice (2026-08-06). Keystones:
/// the worker's VACUUM INTO snapshot is a real SQLite file, ticks are
/// same-day idempotent, pruning keeps the newest N and only ever
/// touches areg-backup-*.db names, non-SQLite hosts idle harmlessly;
/// the operator endpoint streams genuine SQLite header bytes and
/// writes one InternalConsoleAccess audit row per pull.
/// Unauthenticated → 404 for GET /api/internal/backup needs no new
/// pin: the InternalAdminAuth middleware gates the whole
/// /api/internal/* path prefix ahead of MapControllers (pinned by
/// InternalAdminAuthTests), and this endpoint lives under that prefix.
/// </summary>
public class DatabaseBackupServiceTests : IDisposable
{
    private const string SqliteHeader = "SQLite format 3\0";

    private readonly string _root;

    public DatabaseBackupServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "areg-backup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    private (DatabaseBackupService Service, string DbPath, ServiceProvider Provider) MakeHarness(
        Dictionary<string, string?>? extraConfig = null)
    {
        var dbPath = Path.Combine(_root, "live.db");
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<AppDbContext>()
                .Database.EnsureCreated();
        }

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(extraConfig ?? new Dictionary<string, string?>())
            .Build();

        var service = new DatabaseBackupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            config,
            Substitute.For<ILogger<DatabaseBackupService>>());
        return (service, dbPath, provider);
    }

    private static void AssertSqliteFile(string path)
    {
        using var fs = File.OpenRead(path);
        var buf = new byte[16];
        fs.ReadExactly(buf, 0, 16);
        Assert.Equal(SqliteHeader, Encoding.ASCII.GetString(buf));
    }

    [Fact]
    public async Task RunTick_WritesSqliteSnapshot_DefaultDirBesideDb()
    {
        var (service, dbPath, provider) = MakeHarness();
        using var _ = provider;

        await service.RunTickAsync(CancellationToken.None);

        var backupDir = Path.Combine(Path.GetDirectoryName(dbPath)!, "backups");
        var files = Directory.GetFiles(backupDir, "areg-backup-*.db");
        var file = Assert.Single(files);
        AssertSqliteFile(file);
        Assert.Empty(Directory.GetFiles(backupDir, "*.part")); // no residue
    }

    [Fact]
    public async Task RunTick_SameDay_IsIdempotent()
    {
        var backupDir = Path.Combine(_root, "explicit-backups");
        var (service, _, provider) = MakeHarness(new()
        {
            ["Backup:Database:DirectoryPath"] = backupDir,
        });
        using var _ = provider;

        await service.RunTickAsync(CancellationToken.None);
        await service.RunTickAsync(CancellationToken.None); // must not throw / churn

        Assert.Single(Directory.GetFiles(backupDir, "areg-backup-*.db"));
    }

    [Fact]
    public async Task RunTick_PrunesToKeepCount_AndOnlyBackupNames()
    {
        var backupDir = Path.Combine(_root, "prune-backups");
        Directory.CreateDirectory(backupDir);
        // 9 fake older snapshots + an unrelated file the prune pass
        // must never touch.
        for (var i = 1; i <= 9; i++)
            File.WriteAllText(Path.Combine(backupDir, $"areg-backup-2020010{i}.db"), "old");
        var unrelated = Path.Combine(backupDir, "not-a-backup.db");
        File.WriteAllText(unrelated, "keep me");

        var (service, _, provider) = MakeHarness(new()
        {
            ["Backup:Database:DirectoryPath"] = backupDir,
        });
        using var _ = provider;

        await service.RunTickAsync(CancellationToken.None);

        var remaining = Directory.GetFiles(backupDir, "areg-backup-*.db")
            .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(DatabaseBackupService.DefaultKeepCount, remaining.Count);
        // Today's snapshot (2026…) sorts newest and must survive; the
        // three oldest fakes are gone.
        Assert.DoesNotContain("areg-backup-20200101.db", remaining);
        Assert.DoesNotContain("areg-backup-20200103.db", remaining);
        // Today's fresh snapshot sorts newest and must survive the prune.
        Assert.Contains($"areg-backup-{DateTime.UtcNow:yyyyMMdd}.db", remaining);
        Assert.True(File.Exists(unrelated), "prune must only touch areg-backup-*.db files");
    }

    [Fact]
    public async Task RunTick_ExplicitDisable_WritesNothing()
    {
        var backupDir = Path.Combine(_root, "disabled-backups");
        var (service, _, provider) = MakeHarness(new()
        {
            ["Backup:Database:Enabled"] = "false",
            ["Backup:Database:DirectoryPath"] = backupDir,
        });
        using var _ = provider;

        await service.RunTickAsync(CancellationToken.None);

        Assert.False(Directory.Exists(backupDir));
    }

    [Fact]
    public async Task RunTick_InMemoryProvider_IdlesWithoutThrowing()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase("backup-inmem-" + Guid.NewGuid()));
        using var provider = services.BuildServiceProvider();
        IConfiguration config = new ConfigurationBuilder().Build();
        var service = new DatabaseBackupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            config,
            Substitute.For<ILogger<DatabaseBackupService>>());

        await service.RunTickAsync(CancellationToken.None); // must not throw
    }

    // ── Uploaded content is archived beside the database ────────────
    // The catalogue row and its audio are useless apart: a restored database
    // that names a story whose MP3 is gone advertises a download every toy in
    // the fleet will fail. An uploaded story is also the ONE thing in this
    // product that cannot be regenerated from git.

    private string SeedUploadRoot(params string[] fileNames)
    {
        var uploads = Path.Combine(_root, "content-uploads");
        Directory.CreateDirectory(uploads);
        foreach (var name in fileNames)
        {
            File.WriteAllBytes(Path.Combine(uploads, name), new byte[] { 0x49, 0x44, 0x33, 0x04 });
        }
        return uploads;
    }

    [Fact]
    public async Task Tick_ArchivesTheUploadRoot_BesideTheDatabaseSnapshot()
    {
        var uploads = SeedUploadRoot("nor-heqiat-v1.mp3", "nor-heqiat-v2.mp3");
        var backupDir = Path.Combine(_root, "backups");
        var (service, _, provider) = MakeHarness(new Dictionary<string, string?>
        {
            ["Backup:Database:DirectoryPath"] = backupDir,
            ["ContentSync:UploadRoot"] = uploads,
        });
        using (provider)
        {
            await service.RunTickAsync(CancellationToken.None);
        }

        var archive = Assert.Single(Directory.GetFiles(backupDir, "areg-uploads-*.zip"));
        Assert.False(File.Exists(archive + ".part"));

        using var zip = System.IO.Compression.ZipFile.OpenRead(archive);
        Assert.Equal(
            new[] { "nor-heqiat-v1.mp3", "nor-heqiat-v2.mp3" },
            zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Tick_ArchivesUploads_EvenWhenTodaysDatabaseSnapshotAlreadyExists()
    {
        // The DB pass returns early on a same-day snapshot. Letting that skip
        // the uploads archive would mean a restart on any day after the first
        // silently stops backing up the only content that cannot be
        // regenerated.
        var uploads = SeedUploadRoot("nor-heqiat-v1.mp3");
        var backupDir = Path.Combine(_root, "backups");
        var config = new Dictionary<string, string?>
        {
            ["Backup:Database:DirectoryPath"] = backupDir,
            ["ContentSync:UploadRoot"] = uploads,
        };

        var (first, _, p1) = MakeHarness(config);
        using (p1) { await first.RunTickAsync(CancellationToken.None); }

        // Second tick, same UTC day, with the archive removed but the DB
        // snapshot left in place.
        File.Delete(Directory.GetFiles(backupDir, "areg-uploads-*.zip").Single());
        Assert.Single(Directory.GetFiles(backupDir, "areg-backup-*.db"));

        var (second, _, p2) = MakeHarness(config);
        using (p2) { await second.RunTickAsync(CancellationToken.None); }

        Assert.Single(Directory.GetFiles(backupDir, "areg-uploads-*.zip"));
    }

    [Fact]
    public async Task Tick_WithNoUploadRootConfigured_WritesNoArchive()
    {
        var backupDir = Path.Combine(_root, "backups");
        var (service, _, provider) = MakeHarness(new Dictionary<string, string?>
        {
            ["Backup:Database:DirectoryPath"] = backupDir,
        });
        using (provider)
        {
            await service.RunTickAsync(CancellationToken.None);
        }

        Assert.Single(Directory.GetFiles(backupDir, "areg-backup-*.db"));
        Assert.Empty(Directory.GetFiles(backupDir, "areg-uploads-*.zip"));
    }

    [Fact]
    public async Task UploadsPrune_KeepsTheNewest_AndTouchesNothingElse()
    {
        var uploads = SeedUploadRoot("nor-heqiat-v1.mp3");
        var backupDir = Path.Combine(_root, "backups");
        Directory.CreateDirectory(backupDir);

        // Three older archives, plus two files each prune pass must leave
        // alone — the other family's snapshot and an unrelated name.
        foreach (var day in new[] { "20260101", "20260102", "20260103" })
        {
            File.WriteAllText(Path.Combine(backupDir, $"areg-uploads-{day}.zip"), "x");
        }
        File.WriteAllText(Path.Combine(backupDir, "areg-backup-20260101.db"), "x");
        File.WriteAllText(Path.Combine(backupDir, "something-else.zip"), "x");

        var (service, _, provider) = MakeHarness(new Dictionary<string, string?>
        {
            ["Backup:Database:DirectoryPath"] = backupDir,
            ["ContentSync:UploadRoot"] = uploads,
            ["Backup:Database:KeepCount"] = "2",
        });
        using (provider)
        {
            await service.RunTickAsync(CancellationToken.None);
        }

        var archives = Directory.GetFiles(backupDir, "areg-uploads-*.zip")
            .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(2, archives.Length);
        // Today's is newest, so it survives with exactly one older one.
        Assert.Contains($"areg-uploads-{DateTime.UtcNow:yyyyMMdd}.zip", archives);
        Assert.Contains("areg-uploads-20260103.zip", archives);

        Assert.True(File.Exists(Path.Combine(backupDir, "something-else.zip")));
        Assert.NotEmpty(Directory.GetFiles(backupDir, "areg-backup-*.db"));
    }
}

public class InternalControllerBackupTests : IDisposable
{
    private const string SqliteHeader = "SQLite format 3\0";

    private readonly string _root;

    public InternalControllerBackupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "areg-backup-ep-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    private static InternalController NewController(AppDbContext db)
    {
        var moderation = Substitute.For<IModerationService>();
        var controller = new InternalController(
            db, new InMemoryCuratedStoryLibrary(), new OpenAICostMeter(),
            new LibraryStoryQuestionService(Substitute.For<IAiChatClient>()),
            moderation,
            new ConfigurationBuilder().Build(),
            Substitute.For<ILogger<InternalController>>());
        var http = new DefaultHttpContext();
        http.Items["InternalOperator"] = "test-op";
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    [Fact]
    public async Task Backup_StreamsSqliteBytes_AndWritesAccessAudit()
    {
        var dbPath = Path.Combine(_root, "live.db");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        using var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        var controller = NewController(db);

        var result = await controller.DownloadBackup(CancellationToken.None);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/octet-stream", file.ContentType);
        Assert.StartsWith("areg-backup-", file.FileDownloadName);
        Assert.EndsWith(".db", file.FileDownloadName);

        // KEYSTONE: the streamed bytes are a genuine SQLite database.
        var buf = new byte[16];
        await using (var s = file.FileStream)
        {
            var read = 0;
            while (read < 16)
            {
                var n = await s.ReadAsync(buf.AsMemory(read, 16 - read));
                Assert.True(n > 0, "stream ended before the SQLite header");
                read += n;
            }
        }
        Assert.Equal(SqliteHeader, Encoding.ASCII.GetString(buf));

        // The whole-DB read is access-audited, system-actor style.
        var audit = Assert.Single(db.AuditEvents.ToList());
        Assert.Equal(AuditEventType.InternalConsoleAccess, audit.EventType);
        Assert.Null(audit.ActorParentId);
        Assert.Contains("backup", audit.Metadata ?? string.Empty);
        Assert.Contains("test-op", audit.Metadata ?? string.Empty);
    }

    [Fact]
    public async Task Backup_NonSqliteProvider_ReturnsUniform404()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("backup-ep-inmem-" + Guid.NewGuid())
            .Options;
        using var db = new AppDbContext(options);
        var controller = NewController(db);

        var result = await controller.DownloadBackup(CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }
}

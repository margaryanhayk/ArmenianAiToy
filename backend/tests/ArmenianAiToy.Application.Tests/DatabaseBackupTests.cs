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

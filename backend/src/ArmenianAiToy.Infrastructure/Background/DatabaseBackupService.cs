using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArmenianAiToy.Infrastructure.Background;

/// <summary>
/// Daily SQLite backup worker (Tier-1 fix, 2026-08-06). Until this
/// slice the product had ZERO backups — one Railway volume held the
/// only copy of every family's accounts, children, consents and
/// conversations (`docs/ADR-001…: "Backups do not exist."`). Each
/// tick writes a <c>VACUUM INTO</c> snapshot next to the live DB
/// (default <c>&lt;db-dir&gt;/backups/areg-backup-YYYYMMDD.db</c>)
/// and keeps the newest <see cref="DefaultKeepCount"/> files.
///
/// <para>
/// This protects against DB corruption and fat-fingered data loss,
/// NOT against volume loss — the snapshots live on the same volume.
/// The offsite half is the operator endpoint
/// <c>GET /api/internal/backup</c> (pull a snapshot from any
/// machine); audio blobs remain a documented residual risk
/// (object-storage migration is a later slice).
/// </para>
///
/// <para>Config (same <c>IConfiguration</c>-direct idiom as
/// <see cref="RetentionPurgeService"/>):</para>
/// <list type="bullet">
///   <item><description><c>Backup:Database:Enabled</c> — default
///   TRUE. Backups are opt-OUT: a children's product silently
///   running without them is the failure mode this slice exists to
///   kill. Only the literal <c>false</c> disables.</description></item>
///   <item><description><c>Backup:Database:DirectoryPath</c> —
///   default <c>backups/</c> beside the live DB file (on the same
///   persistent volume in the Railway layout).</description></item>
///   <item><description><c>Backup:Database:RunIntervalHours</c> —
///   default <see cref="DefaultRunIntervalHours"/>, floor-clamped to
///   <see cref="MinRunIntervalHours"/>.</description></item>
///   <item><description><c>Backup:Database:KeepCount</c> — default
///   <see cref="DefaultKeepCount"/>, clamped to
///   [<see cref="MinKeepCount"/>, <see cref="MaxKeepCount"/>].</description></item>
/// </list>
///
/// <para>
/// One snapshot per UTC day: the filename is date-stamped and an
/// existing file for today makes the tick a no-op, so restarts and
/// redeploys (frequent on this project) don't churn snapshots.
/// Failures are logged and swallowed — a backup hiccup must never
/// take the API down. Non-SQLite / in-memory providers idle
/// harmlessly (test hosts).
/// </para>
/// </summary>
public sealed class DatabaseBackupService : BackgroundService
{
    public const int DefaultRunIntervalHours = 24;
    public const int MinRunIntervalHours = 1;
    public const int DefaultKeepCount = 7;
    public const int MinKeepCount = 1;
    public const int MaxKeepCount = 60;

    /// <summary>Snapshot filename prefix; the prune pass only ever
    /// deletes files matching <c>areg-backup-*.db</c> in the backup
    /// directory — it can never touch the live DB or its WAL
    /// sidecars.</summary>
    public const string FilePrefix = "areg-backup-";

    /// <summary>Uploaded-content archive prefix. Separate namespace from
    /// <see cref="FilePrefix"/> so each prune pass only ever matches its own
    /// files — the rule that keeps a prune from touching the live DB.</summary>
    public const string UploadsFilePrefix = "areg-uploads-";

    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<DatabaseBackupService> _logger;
    private bool _loggedNotSqlite;

    public DatabaseBackupService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<DatabaseBackupService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small initial delay: let startup migrations settle and keep
        // boot latency untouched.
        try { await Task.Delay(InitialDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // Never let a backup failure kill the hosted service.
                _logger.LogWarning(ex, "Database backup tick failed; will retry on the next interval");
            }

            var hours = ReadInt("Backup:Database:RunIntervalHours", DefaultRunIntervalHours);
            if (hours < MinRunIntervalHours) hours = MinRunIntervalHours;
            try { await Task.Delay(TimeSpan.FromHours(hours), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One backup pass. Public so tests drive ticks directly
    /// (same seam as <see cref="RetentionPurgeService.RunTickAsync"/>).</summary>
    public async Task RunTickAsync(CancellationToken ct)
    {
        // Opt-OUT gate: only the explicit literal false disables.
        if (string.Equals(_config["Backup:Database:Enabled"], "false", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Database backup disabled via Backup:Database:Enabled=false");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var dir = ResolveBackupDirectory(db);
        if (dir is null)
        {
            if (!_loggedNotSqlite)
            {
                _loggedNotSqlite = true;
                _logger.LogInformation(
                    "Database backup worker idle: provider is not a file-backed SQLite database");
            }
            return;
        }
        Directory.CreateDirectory(dir);

        await BackupDatabaseAsync(db, dir, ct);

        // Runs on EVERY tick, including the one where today's DB snapshot
        // already existed and the pass above returned early. Uploaded content
        // is the ONLY thing in this product that cannot be regenerated from
        // git — a story the owner recorded and uploaded exists nowhere else —
        // so skipping it because the database happened to be current would
        // defeat the reason it is backed up at all.
        BackupUploads(dir);
    }

    /// <summary>Where snapshots go: the configured directory, else
    /// <c>backups/</c> beside the live DB. Null when neither is available
    /// (a non-file provider with no configured directory — test hosts).</summary>
    private string? ResolveBackupDirectory(AppDbContext db)
    {
        var configured = _config["Backup:Database:DirectoryPath"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }
        if (!SqliteDatabaseSnapshot.IsSqliteFileDatabase(db, out var dbPath) || dbPath is null)
        {
            return null;
        }
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".", "backups");
    }

    private async Task BackupDatabaseAsync(AppDbContext db, string dir, CancellationToken ct)
    {
        if (!SqliteDatabaseSnapshot.IsSqliteFileDatabase(db, out var dbPath) || dbPath is null)
        {
            return;
        }

        // One snapshot per UTC day; an existing file makes the tick a
        // no-op so restarts don't churn.
        var fileName = $"{FilePrefix}{DateTime.UtcNow:yyyyMMdd}.db";
        var finalPath = Path.Combine(dir, fileName);
        if (File.Exists(finalPath))
        {
            _logger.LogDebug("Database backup for today already exists at {Path}", finalPath);
            Prune(dir, FilePrefix, "*.db");
            return;
        }

        // VACUUM INTO refuses an existing destination, so write to a
        // .part and move — a crash mid-snapshot leaves a stray .part
        // (cleared here), never a truncated .db that a restore would
        // trust.
        var partPath = finalPath + ".part";
        if (File.Exists(partPath)) File.Delete(partPath);

        await SqliteDatabaseSnapshot.VacuumIntoAsync(db, partPath, ct);
        File.Move(partPath, finalPath);

        var sizeBytes = new FileInfo(finalPath).Length;
        _logger.LogInformation(
            "Database backup written: {Path} ({SizeBytes} bytes)", finalPath, sizeBytes);

        Prune(dir, FilePrefix, "*.db");
    }

    /// <summary>
    /// Archives <c>ContentSync:UploadRoot</c> — the stories the owner uploaded
    /// from the console — beside the database snapshot, on the same
    /// one-per-UTC-day, write-a-.part-then-move, keep-the-newest-N idiom.
    ///
    /// <para>
    /// It exists because the catalogue row and its audio are useless apart: a
    /// restored database that names a story whose MP3 is gone advertises a
    /// download every toy in the fleet will fail. Both halves have to survive
    /// the same event.
    /// </para>
    ///
    /// <para>
    /// A zip rather than a file copy so one artifact is one day, and so pruning
    /// is the same one-file-per-day arithmetic as the DB. Skipped entirely when
    /// no upload root is configured or the directory is empty — a deployment
    /// that has never uploaded anything writes nothing. Failures are logged and
    /// swallowed: this must never take the API down, and it must never prevent
    /// the database snapshot, which is why it runs last.
    /// </para>
    /// </summary>
    private void BackupUploads(string dir)
    {
        try
        {
            if (!ContentItemOverlay.TryResolveRoot(_config["ContentSync:UploadRoot"], out var root)
                || !Directory.Exists(root)
                || !Directory.EnumerateFileSystemEntries(root).Any())
            {
                return;
            }

            var finalPath = Path.Combine(dir, $"{UploadsFilePrefix}{DateTime.UtcNow:yyyyMMdd}.zip");
            if (File.Exists(finalPath))
            {
                _logger.LogDebug("Uploaded-content backup for today already exists at {Path}", finalPath);
                Prune(dir, UploadsFilePrefix, "*.zip");
                return;
            }

            var partPath = finalPath + ".part";
            if (File.Exists(partPath)) File.Delete(partPath);

            // The archive is written OUTSIDE the directory being archived
            // whenever the operator has kept them apart; when they are nested,
            // the .part suffix keeps this run's own output from being swept
            // into the zip it is still writing.
            ZipFile.CreateFromDirectory(root, partPath, CompressionLevel.Fastest, false);
            File.Move(partPath, finalPath);

            _logger.LogInformation(
                "Uploaded-content backup written: {Path} ({SizeBytes} bytes)",
                finalPath, new FileInfo(finalPath).Length);

            Prune(dir, UploadsFilePrefix, "*.zip");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Uploaded-content backup failed; the database snapshot is unaffected");
        }
    }

    /// <summary>Keep the newest KeepCount snapshots (date-stamped names
    /// sort chronologically); delete the rest. Only files matching the
    /// given prefix AND extension are ever considered, so each family prunes
    /// itself and neither can reach the live DB or its WAL sidecars.</summary>
    private void Prune(string dir, string prefix, string extensionPattern)
    {
        var keep = ReadInt("Backup:Database:KeepCount", DefaultKeepCount);
        keep = Math.Clamp(keep, MinKeepCount, MaxKeepCount);

        var stale = Directory.GetFiles(dir, prefix + extensionPattern)
            .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
            .Skip(keep)
            .ToList();

        foreach (var f in stale)
        {
            try
            {
                File.Delete(f);
                _logger.LogInformation("Pruned old database backup {Path}", f);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to prune old database backup {Path}", f);
            }
        }
    }

    private int ReadInt(string key, int fallback)
        => int.TryParse(_config[key], out var v) ? v : fallback;
}

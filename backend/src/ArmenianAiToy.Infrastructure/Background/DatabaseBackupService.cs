using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        if (!SqliteDatabaseSnapshot.IsSqliteFileDatabase(db, out var dbPath) || dbPath is null)
        {
            if (!_loggedNotSqlite)
            {
                _loggedNotSqlite = true;
                _logger.LogInformation(
                    "Database backup worker idle: provider is not a file-backed SQLite database");
            }
            return;
        }

        var dir = _config["Backup:Database:DirectoryPath"];
        if (string.IsNullOrWhiteSpace(dir))
        {
            dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".", "backups");
        }
        Directory.CreateDirectory(dir);

        // One snapshot per UTC day; an existing file makes the tick a
        // no-op so restarts don't churn.
        var fileName = $"{FilePrefix}{DateTime.UtcNow:yyyyMMdd}.db";
        var finalPath = Path.Combine(dir, fileName);
        if (File.Exists(finalPath))
        {
            _logger.LogDebug("Database backup for today already exists at {Path}", finalPath);
            Prune(dir);
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

        Prune(dir);
    }

    /// <summary>Keep the newest KeepCount snapshots (date-stamped names
    /// sort chronologically); delete the rest. Only files matching the
    /// backup pattern are ever considered.</summary>
    private void Prune(string dir)
    {
        var keep = ReadInt("Backup:Database:KeepCount", DefaultKeepCount);
        keep = Math.Clamp(keep, MinKeepCount, MaxKeepCount);

        var stale = Directory.GetFiles(dir, FilePrefix + "*.db")
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

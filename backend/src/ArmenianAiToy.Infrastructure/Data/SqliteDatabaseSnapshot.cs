using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace ArmenianAiToy.Infrastructure.Data;

/// <summary>
/// Shared SQLite snapshot helper (Tier-1 backup slice, 2026-08-06).
/// One place decides "is this a file-backed SQLite DB we can snapshot"
/// and one place issues the <c>VACUUM INTO</c>, so the daily
/// <see cref="Background.DatabaseBackupService"/> and the operator
/// <c>GET /api/internal/backup</c> endpoint can never disagree.
///
/// <para>
/// <c>VACUUM INTO</c> is the online-backup primitive: it writes a
/// self-consistent, defragmented copy of the live DB to a new file
/// without blocking readers/writers (WAL mode is already on via
/// <see cref="SqlitePragmaInterceptor"/>). It FAILS if the destination
/// file already exists — callers write to a fresh path.
/// </para>
/// </summary>
public static class SqliteDatabaseSnapshot
{
    /// <summary>
    /// True when <paramref name="db"/> runs on the SQLite provider
    /// against a real file (not <c>:memory:</c>), with the resolved
    /// file path in <paramref name="databasePath"/>. In-memory test
    /// providers and non-SQLite relational providers return false —
    /// backup surfaces fail soft on those, they never throw.
    /// </summary>
    public static bool IsSqliteFileDatabase(DbContext db, out string? databasePath)
    {
        databasePath = null;
        if (!db.Database.IsSqlite()) return false;

        string dataSource;
        try
        {
            dataSource = db.Database.GetDbConnection().DataSource;
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(dataSource)) return false;
        if (dataSource.Contains(":memory:", StringComparison.OrdinalIgnoreCase)) return false;

        databasePath = dataSource;
        return true;
    }

    /// <summary>
    /// Snapshot the live DB into <paramref name="destinationPath"/>
    /// (must not exist yet). The path travels as a bound parameter —
    /// never string-interpolated into SQL.
    /// </summary>
    public static Task VacuumIntoAsync(DbContext db, string destinationPath, CancellationToken ct)
        => db.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", new object[] { destinationPath }, ct);
}

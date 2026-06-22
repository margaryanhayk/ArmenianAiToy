using System.Data.Common;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// #019 — pins that the concurrency PRAGMAs are actually applied to a real
/// file-based SQLite connection opened through EF Core with the interceptor
/// attached (the same wiring <c>DependencyInjection.AddInfrastructure</c>
/// uses). Uses a temp file DB because WAL is a no-op on <c>:memory:</c>.
/// </summary>
public class SqlitePragmaInterceptorTests
{
    [Fact]
    public async Task OpenedConnection_AppliesWalBusyTimeoutAndSynchronousNormal()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"aat-pragma-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .AddInterceptors(new SqlitePragmaInterceptor())
                .Options;

            await using var db = new AppDbContext(options);
            await db.Database.OpenConnectionAsync();
            var conn = db.Database.GetDbConnection();

            Assert.Equal("wal", await ScalarAsync(conn, "PRAGMA journal_mode;"));
            Assert.Equal("5000", await ScalarAsync(conn, "PRAGMA busy_timeout;"));
            Assert.Equal("1", await ScalarAsync(conn, "PRAGMA synchronous;")); // 1 == NORMAL
        }
        finally
        {
            // Release the pooled handle so the temp files can be removed.
            SqliteConnection.ClearAllPools();
            foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                if (File.Exists(f)) File.Delete(f);
        }
    }

    private static async Task<string?> ScalarAsync(DbConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString()?.ToLowerInvariant();
    }
}

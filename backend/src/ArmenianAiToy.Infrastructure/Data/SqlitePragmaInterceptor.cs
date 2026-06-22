using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ArmenianAiToy.Infrastructure.Data;

/// <summary>
/// #019 — applies SQLite concurrency PRAGMAs on every opened connection.
/// SQLite's shipped defaults (rollback journal, zero busy-timeout) fail a
/// concurrent writer IMMEDIATELY with <c>SQLITE_BUSY</c>, which surfaces as
/// 500s the moment two requests write at once. This is a STOPGAP for the
/// current single-file deployment — the real fix is moving off SQLite
/// (register #018). It is intentionally cheap and reversible.
///
/// <list type="bullet">
/// <item><c>journal_mode=WAL</c> — readers no longer block the writer and
/// vice-versa; this is a persistent file-level setting, so re-issuing it on
/// every open is idempotent (a no-op once the file is already in WAL).</item>
/// <item><c>busy_timeout=5000</c> — a contended connection now waits up to
/// 5 s for the lock instead of throwing instantly. Per-connection, so it
/// MUST be set on each open.</item>
/// <item><c>synchronous=NORMAL</c> — the safe, recommended durability mode
/// under WAL (a power-loss can lose the last transaction but cannot corrupt
/// the DB); markedly fewer fsyncs than the FULL default.</item>
/// </list>
///
/// In-memory databases (<c>Data Source=:memory:</c>, used by some tests)
/// silently ignore the WAL change and keep <c>journal_mode=memory</c>; the
/// PRAGMAs do not error there, so the interceptor is safe to attach
/// everywhere. Wired in <c>DependencyInjection.AddInfrastructure</c>; test
/// harnesses that build their own <c>DbContextOptions</c> are unaffected
/// unless they attach it explicitly.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    // journal_mode is persistent; busy_timeout + synchronous are per-connection.
    private const string Pragmas =
        "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA synchronous=NORMAL;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = Pragmas;
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = Pragmas;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}

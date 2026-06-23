using Microsoft.Extensions.Logging;

namespace ArmenianAiToy.Api.Security;

/// <summary>
/// #025 — cross-process advisory lock around <c>Database.Migrate()</c> at
/// startup. Today every instance runs <c>Migrate()</c> on boot; once more than
/// one instance shares a store, two simultaneous boots can race the migration
/// (on SQLite that surfaces as "database is locked"; elsewhere as duplicate
/// work). This serializes it: the first instance to boot migrates while the
/// others BLOCK on an OS file lock, then run their own <c>Migrate()</c> — a
/// no-op because the schema is already current.
///
/// <para>
/// Uses an exclusive file handle (<see cref="FileShare.None"/>) rather than a
/// named mutex, because named mutexes are NOT cross-process on Unix in .NET.
/// The OS releases the handle if the holder crashes, so a dead instance cannot
/// leave a stale lock that deadlocks startup. If the lock can't be acquired
/// within the timeout, it logs a warning and proceeds anyway — never block
/// boot forever on a pathological state.
/// </para>
///
/// <para>
/// For a single-process deployment (the bench, and prod until it scales out)
/// the lock is acquired instantly and behavior is unchanged.
/// </para>
/// </summary>
public static class StartupMigrationLock
{
    public static void RunGuarded(
        string lockFilePath, TimeSpan timeout, Action migrate, ILogger logger)
    {
        FileStream? handle = Acquire(lockFilePath, timeout, logger);
        try
        {
            migrate();
        }
        finally
        {
            handle?.Dispose(); // releases the OS lock
        }
    }

    private static FileStream? Acquire(string lockFilePath, TimeSpan timeout, ILogger logger)
    {
        var deadline = DateTime.UtcNow + timeout;
        var pollDelayMs = 100;
        while (true)
        {
            try
            {
                // Exclusive: a second opener (this or another process) fails
                // until this handle is disposed.
                return new FileStream(
                    lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    logger.LogWarning(
                        "Could not acquire the startup migration lock ({LockFile}) within {Timeout}s; " +
                        "proceeding with migration unguarded. Another instance is likely migrating.",
                        lockFilePath, (int)timeout.TotalSeconds);
                    return null;
                }
                Thread.Sleep(pollDelayMs);
            }
            catch (Exception ex)
            {
                // Unexpected (e.g. permissions): don't block boot — migrate
                // unguarded, same as the pre-#025 behavior.
                logger.LogWarning(ex,
                    "Startup migration lock unavailable ({LockFile}); proceeding unguarded.",
                    lockFilePath);
                return null;
            }
        }
    }
}

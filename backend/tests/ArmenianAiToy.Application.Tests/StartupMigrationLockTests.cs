using ArmenianAiToy.Api.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// #025 — cross-process advisory lock around startup migration. Verifies the
/// action runs, concurrent runs SERIALIZE (the OS file lock blocks the second
/// until the first releases), and that an un-acquirable lock proceeds-unguarded
/// rather than blocking boot forever.
/// </summary>
public class StartupMigrationLockTests
{
    private static string TempLockPath() =>
        Path.Combine(Path.GetTempPath(), $"aat-miglock-{Guid.NewGuid():N}.lock");

    [Fact]
    public void RunGuarded_RunsTheAction()
    {
        var path = TempLockPath();
        try
        {
            var ran = false;
            StartupMigrationLock.RunGuarded(
                path, TimeSpan.FromSeconds(5), () => ran = true, NullLogger.Instance);
            Assert.True(ran);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task RunGuarded_ConcurrentRuns_Serialize()
    {
        var path = TempLockPath();
        try
        {
            var events = new List<string>();
            void Log(string s) { lock (events) events.Add(s); }

            var a = Task.Run(() => StartupMigrationLock.RunGuarded(path, TimeSpan.FromSeconds(10), () =>
            {
                Log("A-start");
                Thread.Sleep(300);  // hold the lock while B is trying
                Log("A-end");
            }, NullLogger.Instance));

            await Task.Delay(75); // let A acquire first

            var b = Task.Run(() => StartupMigrationLock.RunGuarded(path, TimeSpan.FromSeconds(10), () =>
            {
                Log("B-start");
                Log("B-end");
            }, NullLogger.Instance));

            await Task.WhenAll(a, b);

            // B must not start until A has finished — the lock serialized them.
            Assert.Equal(new[] { "A-start", "A-end", "B-start", "B-end" }, events);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void RunGuarded_LockHeldElsewhere_ProceedsUnguardedAfterTimeout()
    {
        var path = TempLockPath();
        try
        {
            // Simulate another holder keeping an exclusive handle open.
            using var held = new FileStream(
                path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            var ran = false;
            StartupMigrationLock.RunGuarded(
                path, TimeSpan.FromMilliseconds(300), () => ran = true, NullLogger.Instance);

            // Could not acquire, but must still proceed (never block boot forever).
            Assert.True(ran);
        }
        finally { TryDelete(path); }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }
}

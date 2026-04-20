using ArmenianAiToy.Api.Health;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ArmenianAiToy.Application.Tests;

public class HealthProbeTests
{
    [Fact]
    public async Task IsDatabaseReachableAsync_ReturnsTrue_WhenSqliteInMemoryReachable()
    {
        // In-memory SQLite — opens successfully on CanConnect, so the probe
        // returns true. Pins the happy-path contract: reachable DB → true.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        await using var db = new AppDbContext(options);

        var result = await HealthProbe.IsDatabaseReachableAsync(db, TimeSpan.FromSeconds(2));

        Assert.True(result);
    }

    [Fact]
    public async Task IsDatabaseReachableAsync_ReturnsFalse_WhenDbContextDisposed()
    {
        // A disposed DbContext makes CanConnectAsync throw; the probe must
        // swallow the exception and return false rather than propagate.
        // Cross-platform safe (no filesystem assumptions).
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new AppDbContext(options);
        await db.DisposeAsync();

        var result = await HealthProbe.IsDatabaseReachableAsync(db, TimeSpan.FromMilliseconds(500));

        Assert.False(result);
    }
}

using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// The story library is opened from INSIDE a toy (owner decision
/// 2026-08-04), so its listen counts must describe THAT toy — not the sum
/// across the parent's toys — and must never surface another family's plays.
/// </summary>
public class StoryLibraryDeviceScopeTests
{
    private sealed class TestDb : DbContext
    {
        public TestDb(DbContextOptions<TestDb> o) : base(o) { }
        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<Device>(e =>
            {
                e.HasKey(d => d.Id);
                e.Ignore(d => d.Conversations);
                e.Ignore(d => d.ParentDevices);
            });
            mb.Entity<ParentDevice>(e =>
            {
                e.HasKey(pd => new { pd.ParentId, pd.DeviceId });
                e.Ignore(pd => pd.Parent);
                e.Ignore(pd => pd.Device);
            });
            mb.Entity<StoryPlay>(e =>
            {
                e.HasKey(p => p.Id);
                e.Ignore(p => p.Device);
            });
        }
    }

    private static StoryPlay Play(Guid deviceId, string storyId) => new()
    {
        Id = Guid.NewGuid(),
        DeviceId = deviceId,
        StoryId = storyId,
        Source = "sd",
        Finished = true,
        ClientEventKey = Guid.NewGuid().ToString("N"),
        PlayedAtUtc = DateTime.UtcNow,
    };

    private static async Task<(ParentService Svc, TestDb Db, Guid ParentId, Guid ToyA, Guid ToyB, Guid Stranger)>
        SeedAsync()
    {
        var db = new TestDb(new DbContextOptionsBuilder<TestDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var parentId = Guid.NewGuid();
        var toyA = Guid.NewGuid();
        var toyB = Guid.NewGuid();
        var stranger = Guid.NewGuid();   // another family's toy
        foreach (var id in new[] { toyA, toyB, stranger })
            db.Add(new Device { Id = id, MacAddress = "m" + id.ToString("N")[..6], Name = "t" });
        db.Add(new ParentDevice { ParentId = parentId, DeviceId = toyA });
        db.Add(new ParentDevice { ParentId = parentId, DeviceId = toyB });

        db.Add(Play(toyA, "anban-huri"));
        db.Add(Play(toyA, "anban-huri"));
        db.Add(Play(toyB, "anban-huri"));
        db.Add(Play(stranger, "anban-huri"));
        await db.SaveChangesAsync();

        var svc = new ParentService(db, new ConfigurationBuilder().Build(),
            Substitute.For<ILogger<ParentService>>());
        return (svc, db, parentId, toyA, toyB, stranger);
    }

    // KEYSTONE: scoped counts describe one toy only.
    [Fact]
    public async Task WithDeviceId_CountsOnlyThatToy()
    {
        var (svc, _, parentId, toyA, toyB, _) = await SeedAsync();

        var a = await svc.GetStoryPlayTotalsForParentAsync(parentId, toyA);
        var b = await svc.GetStoryPlayTotalsForParentAsync(parentId, toyB);

        Assert.Equal(2, a.Single(t => t.StoryId == "anban-huri").Count);
        Assert.Equal(1, b.Single(t => t.StoryId == "anban-huri").Count);
    }

    [Fact]
    public async Task WithoutDeviceId_SumsAcrossOwnToysOnly()
    {
        var (svc, _, parentId, _, _, _) = await SeedAsync();

        var all = await svc.GetStoryPlayTotalsForParentAsync(parentId);

        // 2 (toyA) + 1 (toyB) — the stranger's play is never included.
        Assert.Equal(3, all.Single(t => t.StoryId == "anban-huri").Count);
    }

    // KEYSTONE: asking for a toy you don't own yields nothing, never a leak.
    [Fact]
    public async Task UnownedDeviceId_ReturnsEmpty()
    {
        var (svc, _, parentId, _, _, stranger) = await SeedAsync();

        var totals = await svc.GetStoryPlayTotalsForParentAsync(parentId, stranger);

        Assert.Empty(totals);
    }
}

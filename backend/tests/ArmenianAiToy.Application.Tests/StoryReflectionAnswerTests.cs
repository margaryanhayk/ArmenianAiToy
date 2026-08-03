using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// B4 reflection-answer memory tests: append-only writes (never overwrite),
/// ownership-gated parent read, per-story filter, newest-first pagination.
/// </summary>
public class StoryReflectionAnswerTests
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
            mb.Entity<StoryReflectionAnswer>(e =>
            {
                e.HasKey(a => a.Id);
                e.Ignore(a => a.Device);
            });
            mb.Entity<ParentDevice>(e =>
            {
                e.HasKey(pd => new { pd.ParentId, pd.DeviceId });
                e.Ignore(pd => pd.Parent);
                e.Ignore(pd => pd.Device);
            });
        }
    }

    private static TestDb NewDb() => new(new DbContextOptionsBuilder<TestDb>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static readonly DateTime Now = new(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);

    // KEYSTONE: repeat listens append — the earlier answer is never
    // overwritten, so the parent sees the progression.
    [Fact]
    public async Task AddAnswer_RepeatListens_AppendOnly()
    {
        var db = NewDb();
        var svc = new DeviceService(db, Substitute.For<ILogger<DeviceService>>());
        var deviceId = Guid.NewGuid();
        db.Add(new Device { Id = deviceId, MacAddress = "m", Name = "t" });
        await db.SaveChangesAsync();

        await svc.AddStoryReflectionAnswerAsync(
            deviceId, "anban-huri", 0, "Առաջին պատասխան", SafetyFlag.Clean, Now);
        await svc.AddStoryReflectionAnswerAsync(
            deviceId, "anban-huri", 0, "Երկրորդ պատասխան", SafetyFlag.Clean, Now.AddDays(1));

        var rows = await db.Set<StoryReflectionAnswer>()
            .OrderBy(a => a.CreatedAtUtc).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Առաջին պատասխան", rows[0].AnswerText);
        Assert.Equal("Երկրորդ պատասխան", rows[1].AnswerText);
    }

    [Fact]
    public async Task GetAnswers_OwnershipGated_AndStoryFiltered()
    {
        var db = NewDb();
        var parentSvc = new ParentService(db, new ConfigurationBuilder().Build(),
            Substitute.For<ILogger<ParentService>>());
        var parentId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        db.Add(new Device { Id = deviceId, MacAddress = "m", Name = "t" });
        db.Add(new ParentDevice { ParentId = parentId, DeviceId = deviceId });
        db.Add(new StoryReflectionAnswer
        {
            Id = Guid.NewGuid(), DeviceId = deviceId, StoryId = "anban-huri",
            QuestionIndex = 0, AnswerText = "Ա", SafetyFlag = SafetyFlag.Clean,
            CreatedAtUtc = Now,
        });
        db.Add(new StoryReflectionAnswer
        {
            Id = Guid.NewGuid(), DeviceId = deviceId, StoryId = "ulik",
            QuestionIndex = 0, AnswerText = "Բ", SafetyFlag = SafetyFlag.Clean,
            CreatedAtUtc = Now.AddHours(1),
        });
        await db.SaveChangesAsync();

        // Stranger parent → null (silent 404 shape).
        Assert.Null(await parentSvc.GetStoryReflectionAnswersAsync(
            Guid.NewGuid(), deviceId, null, 20, 0));

        // Unfiltered: both, newest first.
        var all = await parentSvc.GetStoryReflectionAnswersAsync(
            parentId, deviceId, null, 20, 0);
        Assert.NotNull(all);
        Assert.Equal(2, all!.Total);
        Assert.Equal(["ulik", "anban-huri"], all.Answers.Select(a => a.StoryId).ToList());

        // Story filter narrows to that story only.
        var filtered = await parentSvc.GetStoryReflectionAnswersAsync(
            parentId, deviceId, "anban-huri", 20, 0);
        Assert.Equal(1, filtered!.Total);
        Assert.Equal("Ա", Assert.Single(filtered.Answers).AnswerText);
    }
}

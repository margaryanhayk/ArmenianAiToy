using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Slice D — the optional per-mode filter on conversation summaries
/// (drives the dashboard's Stories/Games/Riddles/Questions/Bedtime tabs).
/// </summary>
public class ConversationSummariesModeFilterTests
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
            mb.Entity<Conversation>(e =>
            {
                e.HasKey(c => c.Id);
                e.Ignore(c => c.Device);
                e.Ignore(c => c.Child);
                e.HasMany(c => c.Messages).WithOne(m => m.Conversation)
                    .HasForeignKey(m => m.ConversationId);
            });
            mb.Entity<Message>(e => e.HasKey(m => m.Id));
        }
    }

    private static readonly DateTime Now = new(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);

    private static async Task<(ConversationService Service, Guid DeviceId)> SeedAsync()
    {
        var db = new TestDb(new DbContextOptionsBuilder<TestDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var deviceId = Guid.NewGuid();
        db.Add(new Device { Id = deviceId, MacAddress = "m", Name = "t" });

        void AddConversation(string? mode, int hoursAgo)
        {
            var convId = Guid.NewGuid();
            db.Add(new Conversation { Id = convId, DeviceId = deviceId, StartedAt = Now.AddHours(-hoursAgo) });
            db.Add(new Message
            {
                Id = Guid.NewGuid(), ConversationId = convId, Role = MessageRole.Assistant,
                Content = "x", Timestamp = Now.AddHours(-hoursAgo), SafetyFlag = SafetyFlag.Clean,
                Mode = mode,
            });
        }
        AddConversation("story", 1);
        AddConversation("game", 2);
        AddConversation("story", 3);
        AddConversation(null, 4);   // pre-stamping conversation
        await db.SaveChangesAsync();

        return (new ConversationService(db, Substitute.For<ILogger<ConversationService>>()), deviceId);
    }

    [Fact]
    public async Task Summaries_ModeFilter_NarrowsToConversationsWithThatMode()
    {
        var (svc, deviceId) = await SeedAsync();

        var stories = await svc.GetConversationSummariesAsync(deviceId, 20, 0, "story");

        Assert.Equal(2, stories.Count);
        Assert.All(stories, s => Assert.Contains("story", s.Modes!));
    }

    [Fact]
    public async Task Summaries_NoFilter_ReturnsEverything_Unchanged()
    {
        var (svc, deviceId) = await SeedAsync();

        var all = await svc.GetConversationSummariesAsync(deviceId, 20, 0);

        Assert.Equal(4, all.Count);
    }

    [Fact]
    public async Task Summaries_FilterWithNoMatches_ReturnsEmpty()
    {
        var (svc, deviceId) = await SeedAsync();

        var calm = await svc.GetConversationSummariesAsync(deviceId, 20, 0, "calm");

        Assert.Empty(calm);
    }
}

using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for ConversationService.GetConversationSummariesAsync — the read path
/// behind GET /api/conversations/summary. Mirrors the InMemory pattern used by
/// ConversationServiceGetByIdTests.
/// </summary>
public class ConversationServiceGetSummariesTests
{
    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Conversation>().HasKey(c => c.Id);
            modelBuilder.Entity<Conversation>().Ignore(c => c.Device);
            modelBuilder.Entity<Conversation>().Ignore(c => c.Child);

            modelBuilder.Entity<Message>().HasKey(m => m.Id);
            modelBuilder.Entity<Message>().Ignore(m => m.Conversation);

            modelBuilder.Entity<Conversation>()
                .HasMany(c => c.Messages)
                .WithOne()
                .HasForeignKey(m => m.ConversationId);
        }
    }

    private static (ConversationService Service, TestDbContext Db) CreateService()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TestDbContext(options);
        var logger = Substitute.For<ILogger<ConversationService>>();
        return (new ConversationService(db, logger), db);
    }

    private static Conversation NewConversation(Guid deviceId, DateTime startedAt)
        => new() { Id = Guid.NewGuid(), DeviceId = deviceId, StartedAt = startedAt };

    private static Message NewMessage(Guid conversationId, MessageRole role, string content, DateTime ts, SafetyFlag flag = SafetyFlag.Clean)
        => new()
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = role,
            Content = content,
            Timestamp = ts,
            SafetyFlag = flag
        };

    [Fact]
    public async Task GetConversationSummariesAsync_ReturnsRowsOrderedByStartedAtDesc()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var t0 = new DateTime(2026, 4, 8, 12, 0, 0, DateTimeKind.Utc);

        var c1 = NewConversation(deviceId, t0);
        var c2 = NewConversation(deviceId, t0.AddMinutes(10));
        var c3 = NewConversation(deviceId, t0.AddMinutes(20));
        db.Set<Conversation>().AddRange(c1, c2, c3);

        db.Set<Message>().AddRange(
            NewMessage(c1.Id, MessageRole.User, "first u1", t0.AddSeconds(1)),
            NewMessage(c1.Id, MessageRole.Assistant, "first a1", t0.AddSeconds(2)),
            NewMessage(c2.Id, MessageRole.User, "second u1", t0.AddMinutes(10).AddSeconds(1)),
            NewMessage(c2.Id, MessageRole.Assistant, "second a1", t0.AddMinutes(10).AddSeconds(2)),
            NewMessage(c3.Id, MessageRole.User, "third u1", t0.AddMinutes(20).AddSeconds(1))
        );
        await db.SaveChangesAsync();

        var result = await service.GetConversationSummariesAsync(deviceId);

        Assert.Equal(3, result.Count);
        Assert.Equal(c3.Id, result[0].Id);
        Assert.Equal(c2.Id, result[1].Id);
        Assert.Equal(c1.Id, result[2].Id);
    }

    [Fact]
    public async Task GetConversationSummariesAsync_PicksFirstUserAndLastAssistantSnippets()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var conv = NewConversation(deviceId, new DateTime(2026, 4, 8, 12, 0, 0, DateTimeKind.Utc));
        db.Set<Conversation>().Add(conv);

        db.Set<Message>().AddRange(
            NewMessage(conv.Id, MessageRole.User, "earliest user", conv.StartedAt.AddSeconds(1)),
            NewMessage(conv.Id, MessageRole.Assistant, "early assistant", conv.StartedAt.AddSeconds(2)),
            NewMessage(conv.Id, MessageRole.User, "later user", conv.StartedAt.AddSeconds(3)),
            NewMessage(conv.Id, MessageRole.Assistant, "latest assistant", conv.StartedAt.AddSeconds(4))
        );
        await db.SaveChangesAsync();

        var result = await service.GetConversationSummariesAsync(deviceId);

        var dto = Assert.Single(result);
        Assert.Equal("earliest user", dto.FirstUserSnippet);
        Assert.Equal("latest assistant", dto.LastAssistantSnippet);
        Assert.Equal(4, dto.MessageCount);
        Assert.False(dto.HasFlaggedContent);
    }

    [Fact]
    public async Task GetConversationSummariesAsync_TruncatesLongSnippetsWithEllipsis()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var conv = NewConversation(deviceId, DateTime.UtcNow);
        db.Set<Conversation>().Add(conv);

        var longText = new string('ա', 200);
        db.Set<Message>().Add(NewMessage(conv.Id, MessageRole.User, longText, conv.StartedAt.AddSeconds(1)));
        await db.SaveChangesAsync();

        var dto = (await service.GetConversationSummariesAsync(deviceId)).Single();

        Assert.NotNull(dto.FirstUserSnippet);
        Assert.Equal(121, dto.FirstUserSnippet!.Length); // 120 + ellipsis char
        Assert.EndsWith("…", dto.FirstUserSnippet);
        Assert.Null(dto.LastAssistantSnippet);
    }

    [Fact]
    public async Task GetConversationSummariesAsync_NoUserOrAssistant_NullsSnippets()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var conv = NewConversation(deviceId, DateTime.UtcNow);
        db.Set<Conversation>().Add(conv);
        await db.SaveChangesAsync();

        var dto = (await service.GetConversationSummariesAsync(deviceId)).Single();

        Assert.Null(dto.FirstUserSnippet);
        Assert.Null(dto.LastAssistantSnippet);
        Assert.Equal(0, dto.MessageCount);
    }

    [Fact]
    public async Task GetConversationSummariesAsync_FlaggedMessage_SetsHasFlaggedContent()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var conv = NewConversation(deviceId, DateTime.UtcNow);
        db.Set<Conversation>().Add(conv);
        db.Set<Message>().Add(NewMessage(conv.Id, MessageRole.User, "blocked", conv.StartedAt.AddSeconds(1), SafetyFlag.Blocked));
        await db.SaveChangesAsync();

        var dto = (await service.GetConversationSummariesAsync(deviceId)).Single();

        Assert.True(dto.HasFlaggedContent);
    }

    [Fact]
    public async Task GetConversationSummariesAsync_RespectsLimitAndOffset()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var t0 = new DateTime(2026, 4, 8, 12, 0, 0, DateTimeKind.Utc);

        var convs = new List<Conversation>();
        for (int i = 0; i < 5; i++)
            convs.Add(NewConversation(deviceId, t0.AddMinutes(i)));
        db.Set<Conversation>().AddRange(convs);
        await db.SaveChangesAsync();

        var page = await service.GetConversationSummariesAsync(deviceId, limit: 2, offset: 2);

        // Ordering desc: convs[4], convs[3], convs[2], convs[1], convs[0]
        // Skip 2, take 2 → convs[2], convs[1]
        Assert.Equal(2, page.Count);
        Assert.Equal(convs[2].Id, page[0].Id);
        Assert.Equal(convs[1].Id, page[1].Id);
    }

    [Fact]
    public async Task GetConversationSummariesAsync_FiltersByDeviceId()
    {
        var (service, db) = CreateService();
        var deviceA = Guid.NewGuid();
        var deviceB = Guid.NewGuid();
        var t0 = new DateTime(2026, 4, 8, 12, 0, 0, DateTimeKind.Utc);

        db.Set<Conversation>().AddRange(
            NewConversation(deviceA, t0),
            NewConversation(deviceA, t0.AddMinutes(1)),
            NewConversation(deviceB, t0.AddMinutes(2))
        );
        await db.SaveChangesAsync();

        var convAId1 = db.Set<Conversation>().Where(c => c.DeviceId == deviceA).Select(c => c.Id).ToHashSet();
        var convBId = db.Set<Conversation>().Where(c => c.DeviceId == deviceB).Select(c => c.Id).Single();

        var resultA = await service.GetConversationSummariesAsync(deviceA);
        var resultB = await service.GetConversationSummariesAsync(deviceB);

        Assert.Equal(2, resultA.Count);
        Assert.All(resultA, r => Assert.Contains(r.Id, convAId1));
        Assert.Single(resultB);
        Assert.Equal(convBId, resultB[0].Id);
    }

    [Fact]
    public async Task GetConversationSummariesAsync_UnknownDevice_ReturnsEmptyList()
    {
        var (service, _) = CreateService();
        var result = await service.GetConversationSummariesAsync(Guid.NewGuid());
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}

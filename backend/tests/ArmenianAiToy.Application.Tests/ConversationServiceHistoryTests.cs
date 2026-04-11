using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for ConversationService.GetConversationHistoryAsync and
/// ConversationService.GetConversationByIdAsync — the read paths
/// behind parent-facing history and detail endpoints.
/// </summary>
public class ConversationServiceHistoryTests
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

    private static Conversation NewConversation(Guid deviceId, DateTime startedAt, DateTime? endedAt = null)
        => new() { Id = Guid.NewGuid(), DeviceId = deviceId, StartedAt = startedAt, EndedAt = endedAt };

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

    // === GetConversationHistoryAsync ===

    [Fact]
    public async Task GetHistory_ReturnsConversationsOrderedByStartedAtDesc()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var t0 = new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc);

        var c1 = NewConversation(deviceId, t0);
        var c2 = NewConversation(deviceId, t0.AddMinutes(10));
        var c3 = NewConversation(deviceId, t0.AddMinutes(20));
        db.Set<Conversation>().AddRange(c1, c2, c3);
        await db.SaveChangesAsync();

        var result = await service.GetConversationHistoryAsync(deviceId);

        Assert.Equal(3, result.Count);
        Assert.Equal(c3.Id, result[0].Id);
        Assert.Equal(c2.Id, result[1].Id);
        Assert.Equal(c1.Id, result[2].Id);
    }

    [Fact]
    public async Task GetHistory_IncludesMessagesInChronologicalOrder()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var t0 = new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc);
        var conv = NewConversation(deviceId, t0);
        db.Set<Conversation>().Add(conv);
        db.Set<Message>().AddRange(
            NewMessage(conv.Id, MessageRole.User, "first", t0.AddSeconds(1)),
            NewMessage(conv.Id, MessageRole.Assistant, "second", t0.AddSeconds(2)),
            NewMessage(conv.Id, MessageRole.User, "third", t0.AddSeconds(3)));
        await db.SaveChangesAsync();

        var result = await service.GetConversationHistoryAsync(deviceId);

        var dto = Assert.Single(result);
        Assert.Equal(3, dto.MessageCount);
        Assert.Equal(3, dto.Messages.Count);
        Assert.Equal("first", dto.Messages[0].Content);
        Assert.Equal("second", dto.Messages[1].Content);
        Assert.Equal("third", dto.Messages[2].Content);
    }

    [Fact]
    public async Task GetHistory_MessageRolesAreLowercase()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var t0 = new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc);
        var conv = NewConversation(deviceId, t0);
        db.Set<Conversation>().Add(conv);
        db.Set<Message>().AddRange(
            NewMessage(conv.Id, MessageRole.User, "hi", t0.AddSeconds(1)),
            NewMessage(conv.Id, MessageRole.Assistant, "hello", t0.AddSeconds(2)));
        await db.SaveChangesAsync();

        var result = await service.GetConversationHistoryAsync(deviceId);

        var dto = Assert.Single(result);
        Assert.Equal("user", dto.Messages[0].Role);
        Assert.Equal("assistant", dto.Messages[1].Role);
    }

    [Fact]
    public async Task GetHistory_HasFlaggedContent_TrueWhenNonCleanMessage()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var t0 = DateTime.UtcNow;
        var conv = NewConversation(deviceId, t0);
        db.Set<Conversation>().Add(conv);
        db.Set<Message>().AddRange(
            NewMessage(conv.Id, MessageRole.User, "ok", t0.AddSeconds(1)),
            NewMessage(conv.Id, MessageRole.User, "bad", t0.AddSeconds(2), SafetyFlag.Blocked));
        await db.SaveChangesAsync();

        var result = await service.GetConversationHistoryAsync(deviceId);

        Assert.True(result[0].HasFlaggedContent);
    }

    [Fact]
    public async Task GetHistory_HasFlaggedContent_FalseWhenAllClean()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var t0 = DateTime.UtcNow;
        var conv = NewConversation(deviceId, t0);
        db.Set<Conversation>().Add(conv);
        db.Set<Message>().Add(NewMessage(conv.Id, MessageRole.User, "hi", t0.AddSeconds(1)));
        await db.SaveChangesAsync();

        var result = await service.GetConversationHistoryAsync(deviceId);

        Assert.False(result[0].HasFlaggedContent);
    }

    [Fact]
    public async Task GetHistory_RespectsLimitAndOffset()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var t0 = new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc);
        var convs = new List<Conversation>();
        for (int i = 0; i < 5; i++)
            convs.Add(NewConversation(deviceId, t0.AddMinutes(i)));
        db.Set<Conversation>().AddRange(convs);
        await db.SaveChangesAsync();

        // Desc order: convs[4], convs[3], convs[2], convs[1], convs[0]
        // Skip 1, take 2 → convs[3], convs[2]
        var page = await service.GetConversationHistoryAsync(deviceId, limit: 2, offset: 1);

        Assert.Equal(2, page.Count);
        Assert.Equal(convs[3].Id, page[0].Id);
        Assert.Equal(convs[2].Id, page[1].Id);
    }

    [Fact]
    public async Task GetHistory_FiltersByDeviceId()
    {
        var (service, db) = CreateService();
        var deviceA = Guid.NewGuid();
        var deviceB = Guid.NewGuid();
        var t0 = DateTime.UtcNow;
        db.Set<Conversation>().AddRange(
            NewConversation(deviceA, t0),
            NewConversation(deviceB, t0.AddMinutes(1)));
        await db.SaveChangesAsync();

        var result = await service.GetConversationHistoryAsync(deviceA);

        var dto = Assert.Single(result);
        Assert.Equal(deviceA, dto.DeviceId);
    }

    [Fact]
    public async Task GetHistory_NoConversations_ReturnsEmptyList()
    {
        var (service, _) = CreateService();

        var result = await service.GetConversationHistoryAsync(Guid.NewGuid());

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetHistory_ConversationWithNoMessages_ReturnsZeroCountAndEmptyList()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        db.Set<Conversation>().Add(NewConversation(deviceId, DateTime.UtcNow));
        await db.SaveChangesAsync();

        var result = await service.GetConversationHistoryAsync(deviceId);

        var dto = Assert.Single(result);
        Assert.Equal(0, dto.MessageCount);
        Assert.Empty(dto.Messages);
        Assert.False(dto.HasFlaggedContent);
    }

    // === GetConversationByIdAsync ===

    [Fact]
    public async Task GetById_ExistingConversation_ReturnsFullDto()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var t0 = new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc);
        var conv = NewConversation(deviceId, t0);
        db.Set<Conversation>().Add(conv);
        db.Set<Message>().AddRange(
            NewMessage(conv.Id, MessageRole.User, "hello", t0.AddSeconds(1)),
            NewMessage(conv.Id, MessageRole.Assistant, "hi there", t0.AddSeconds(2)));
        await db.SaveChangesAsync();

        var result = await service.GetConversationByIdAsync(conv.Id);

        Assert.NotNull(result);
        Assert.Equal(conv.Id, result!.Id);
        Assert.Equal(deviceId, result.DeviceId);
        Assert.Equal(t0, result.StartedAt);
        Assert.Equal(2, result.MessageCount);
        Assert.Equal(2, result.Messages.Count);
        Assert.Equal("hello", result.Messages[0].Content);
        Assert.Equal("user", result.Messages[0].Role);
        Assert.Equal("hi there", result.Messages[1].Content);
        Assert.Equal("assistant", result.Messages[1].Role);
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNull()
    {
        var (service, _) = CreateService();

        var result = await service.GetConversationByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetById_FlaggedMessage_SetsHasFlaggedContent()
    {
        var (service, db) = CreateService();
        var conv = NewConversation(Guid.NewGuid(), DateTime.UtcNow);
        db.Set<Conversation>().Add(conv);
        db.Set<Message>().Add(NewMessage(conv.Id, MessageRole.User, "bad", DateTime.UtcNow, SafetyFlag.Flagged));
        await db.SaveChangesAsync();

        var result = await service.GetConversationByIdAsync(conv.Id);

        Assert.NotNull(result);
        Assert.True(result!.HasFlaggedContent);
        Assert.Equal(SafetyFlag.Flagged, result.Messages[0].SafetyFlag);
    }

    [Fact]
    public async Task GetById_ReturnsAllMessages()
    {
        var (service, db) = CreateService();
        var t0 = new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc);
        var conv = NewConversation(Guid.NewGuid(), t0);
        db.Set<Conversation>().Add(conv);
        db.Set<Message>().AddRange(
            NewMessage(conv.Id, MessageRole.User, "first", t0.AddSeconds(1)),
            NewMessage(conv.Id, MessageRole.Assistant, "second", t0.AddSeconds(2)),
            NewMessage(conv.Id, MessageRole.User, "third", t0.AddSeconds(3)));
        await db.SaveChangesAsync();

        var result = await service.GetConversationByIdAsync(conv.Id);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Messages.Count);
        Assert.Contains(result.Messages, m => m.Content == "first");
        Assert.Contains(result.Messages, m => m.Content == "second");
        Assert.Contains(result.Messages, m => m.Content == "third");
    }

    [Fact]
    public async Task GetById_ReturnsDeviceId_ForOwnershipVerification()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var conv = NewConversation(deviceId, DateTime.UtcNow);
        db.Set<Conversation>().Add(conv);
        await db.SaveChangesAsync();

        var result = await service.GetConversationByIdAsync(conv.Id);

        Assert.NotNull(result);
        Assert.Equal(deviceId, result!.DeviceId);
    }
}

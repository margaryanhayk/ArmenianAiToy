using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for ConversationService write/read operations:
/// GetOrCreateActiveConversationAsync, AddMessageAsync, GetRecentMessagesAsync.
/// Uses EF Core InMemory.
/// </summary>
public class ConversationServiceCrudTests
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

    // --- GetOrCreateActiveConversationAsync ---

    [Fact]
    public async Task GetOrCreateActive_NoExisting_CreatesNewConversation()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();

        var conv = await service.GetOrCreateActiveConversationAsync(deviceId, null);

        Assert.NotEqual(Guid.Empty, conv.Id);
        Assert.Equal(deviceId, conv.DeviceId);
        Assert.Null(conv.EndedAt);
        Assert.Equal(1, await db.Set<Conversation>().CountAsync());
    }

    [Fact]
    public async Task GetOrCreateActive_ActiveExists_ReusesExisting()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var existing = NewConversation(deviceId, DateTime.UtcNow.AddMinutes(-5));
        db.Set<Conversation>().Add(existing);
        await db.SaveChangesAsync();

        var conv = await service.GetOrCreateActiveConversationAsync(deviceId, null);

        Assert.Equal(existing.Id, conv.Id);
        Assert.Equal(1, await db.Set<Conversation>().CountAsync());
    }

    [Fact]
    public async Task GetOrCreateActive_ExpiredConversation_CreatesNew()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var expired = NewConversation(deviceId, DateTime.UtcNow.AddMinutes(-35));
        db.Set<Conversation>().Add(expired);
        await db.SaveChangesAsync();

        var conv = await service.GetOrCreateActiveConversationAsync(deviceId, null);

        Assert.NotEqual(expired.Id, conv.Id);
        Assert.Equal(2, await db.Set<Conversation>().CountAsync());
    }

    [Fact]
    public async Task GetOrCreateActive_EndedConversation_CreatesNew()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var ended = NewConversation(deviceId, DateTime.UtcNow.AddMinutes(-5), endedAt: DateTime.UtcNow.AddMinutes(-1));
        db.Set<Conversation>().Add(ended);
        await db.SaveChangesAsync();

        var conv = await service.GetOrCreateActiveConversationAsync(deviceId, null);

        Assert.NotEqual(ended.Id, conv.Id);
        Assert.Equal(2, await db.Set<Conversation>().CountAsync());
    }

    [Fact]
    public async Task GetOrCreateActive_WithChildId_SetsChildId()
    {
        var (service, _) = CreateService();
        var deviceId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var conv = await service.GetOrCreateActiveConversationAsync(deviceId, childId);

        Assert.Equal(childId, conv.ChildId);
    }

    [Fact]
    public async Task GetOrCreateActive_MultipleActive_ReturnsMostRecent()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var older = NewConversation(deviceId, DateTime.UtcNow.AddMinutes(-10));
        var newer = NewConversation(deviceId, DateTime.UtcNow.AddMinutes(-2));
        db.Set<Conversation>().AddRange(older, newer);
        await db.SaveChangesAsync();

        var conv = await service.GetOrCreateActiveConversationAsync(deviceId, null);

        Assert.Equal(newer.Id, conv.Id);
    }

    [Fact]
    public async Task GetOrCreateActive_DifferentDevice_DoesNotReuse()
    {
        var (service, db) = CreateService();
        var deviceA = Guid.NewGuid();
        var deviceB = Guid.NewGuid();
        var convA = NewConversation(deviceA, DateTime.UtcNow.AddMinutes(-5));
        db.Set<Conversation>().Add(convA);
        await db.SaveChangesAsync();

        var conv = await service.GetOrCreateActiveConversationAsync(deviceB, null);

        Assert.NotEqual(convA.Id, conv.Id);
        Assert.Equal(deviceB, conv.DeviceId);
    }

    // --- AddMessageAsync ---

    [Fact]
    public async Task AddMessageAsync_PersistsMessageWithCorrectFields()
    {
        var (service, db) = CreateService();
        var conv = NewConversation(Guid.NewGuid(), DateTime.UtcNow);
        db.Set<Conversation>().Add(conv);
        await db.SaveChangesAsync();

        var msg = await service.AddMessageAsync(conv.Id, MessageRole.User, "Hello world");

        Assert.NotEqual(Guid.Empty, msg.Id);
        Assert.Equal(conv.Id, msg.ConversationId);
        Assert.Equal(MessageRole.User, msg.Role);
        Assert.Equal("Hello world", msg.Content);
        Assert.Equal(SafetyFlag.Clean, msg.SafetyFlag);

        var persisted = await db.Set<Message>().FindAsync(msg.Id);
        Assert.NotNull(persisted);
    }

    [Fact]
    public async Task AddMessageAsync_WithSafetyFlag_Persisted()
    {
        var (service, db) = CreateService();
        var conv = NewConversation(Guid.NewGuid(), DateTime.UtcNow);
        db.Set<Conversation>().Add(conv);
        await db.SaveChangesAsync();

        var msg = await service.AddMessageAsync(conv.Id, MessageRole.User, "bad content", SafetyFlag.Blocked);

        Assert.Equal(SafetyFlag.Blocked, msg.SafetyFlag);
        var persisted = await db.Set<Message>().FindAsync(msg.Id);
        Assert.Equal(SafetyFlag.Blocked, persisted!.SafetyFlag);
    }

    [Fact]
    public async Task AddMessageAsync_AssistantRole_Stored()
    {
        var (service, db) = CreateService();
        var conv = NewConversation(Guid.NewGuid(), DateTime.UtcNow);
        db.Set<Conversation>().Add(conv);
        await db.SaveChangesAsync();

        var msg = await service.AddMessageAsync(conv.Id, MessageRole.Assistant, "AI response");

        Assert.Equal(MessageRole.Assistant, msg.Role);
    }

    // --- GetRecentMessagesAsync ---

    [Fact]
    public async Task GetRecentMessages_ReturnsInChronologicalOrder()
    {
        var (service, db) = CreateService();
        var t0 = new DateTime(2026, 4, 8, 12, 0, 0, DateTimeKind.Utc);
        var conv = NewConversation(Guid.NewGuid(), t0);
        db.Set<Conversation>().Add(conv);
        db.Set<Message>().AddRange(
            NewMessage(conv.Id, MessageRole.User, "first", t0.AddSeconds(1)),
            NewMessage(conv.Id, MessageRole.Assistant, "second", t0.AddSeconds(2)),
            NewMessage(conv.Id, MessageRole.User, "third", t0.AddSeconds(3)));
        await db.SaveChangesAsync();

        var result = await service.GetRecentMessagesAsync(conv.Id);

        Assert.Equal(3, result.Count);
        Assert.Equal("first", result[0].Content);
        Assert.Equal("second", result[1].Content);
        Assert.Equal("third", result[2].Content);
    }

    [Fact]
    public async Task GetRecentMessages_RespectsCountLimit()
    {
        var (service, db) = CreateService();
        var t0 = new DateTime(2026, 4, 8, 12, 0, 0, DateTimeKind.Utc);
        var conv = NewConversation(Guid.NewGuid(), t0);
        db.Set<Conversation>().Add(conv);
        for (int i = 0; i < 10; i++)
            db.Set<Message>().Add(NewMessage(conv.Id, MessageRole.User, $"msg{i}", t0.AddSeconds(i)));
        await db.SaveChangesAsync();

        var result = await service.GetRecentMessagesAsync(conv.Id, count: 3);

        Assert.Equal(3, result.Count);
        Assert.Equal("msg7", result[0].Content);
        Assert.Equal("msg8", result[1].Content);
        Assert.Equal("msg9", result[2].Content);
    }

    [Fact]
    public async Task GetRecentMessages_RoleConvertedToLowercase()
    {
        var (service, db) = CreateService();
        var t0 = DateTime.UtcNow;
        var conv = NewConversation(Guid.NewGuid(), t0);
        db.Set<Conversation>().Add(conv);
        db.Set<Message>().AddRange(
            NewMessage(conv.Id, MessageRole.User, "hi", t0.AddSeconds(1)),
            NewMessage(conv.Id, MessageRole.Assistant, "hello", t0.AddSeconds(2)));
        await db.SaveChangesAsync();

        var result = await service.GetRecentMessagesAsync(conv.Id);

        Assert.Equal("user", result[0].Role);
        Assert.Equal("assistant", result[1].Role);
    }

    [Fact]
    public async Task GetRecentMessages_NoMessages_ReturnsEmptyList()
    {
        var (service, db) = CreateService();
        var conv = NewConversation(Guid.NewGuid(), DateTime.UtcNow);
        db.Set<Conversation>().Add(conv);
        await db.SaveChangesAsync();

        var result = await service.GetRecentMessagesAsync(conv.Id);

        Assert.NotNull(result);
        Assert.Empty(result);
    }
}

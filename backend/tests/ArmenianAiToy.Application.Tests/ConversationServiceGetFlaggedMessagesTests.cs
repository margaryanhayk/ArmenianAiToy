using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for ConversationService.GetFlaggedMessagesAsync — the read path
/// behind GET /api/conversations/flagged. Mirrors the InMemory pattern used by
/// ConversationServiceGetSummariesTests / ConversationServiceGetByIdTests.
/// </summary>
public class ConversationServiceGetFlaggedMessagesTests
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

    private static Message NewMessage(Guid conversationId, MessageRole role, string content, DateTime ts, SafetyFlag flag)
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
    public async Task GetFlaggedMessagesAsync_ReturnsOnlyNonCleanMessages()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var conv = NewConversation(deviceId, new DateTime(2026, 4, 8, 12, 0, 0, DateTimeKind.Utc));
        db.Set<Conversation>().Add(conv);

        var clean = NewMessage(conv.Id, MessageRole.User, "fine", conv.StartedAt.AddSeconds(1), SafetyFlag.Clean);
        var flagged = NewMessage(conv.Id, MessageRole.User, "borderline", conv.StartedAt.AddSeconds(2), SafetyFlag.Flagged);
        var blocked = NewMessage(conv.Id, MessageRole.User, "bad", conv.StartedAt.AddSeconds(3), SafetyFlag.Blocked);
        db.Set<Message>().AddRange(clean, flagged, blocked);
        await db.SaveChangesAsync();

        var result = await service.GetFlaggedMessagesAsync(deviceId);

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, r => r.MessageId == clean.Id);
        Assert.Contains(result, r => r.MessageId == flagged.Id && r.SafetyFlag == SafetyFlag.Flagged);
        Assert.Contains(result, r => r.MessageId == blocked.Id && r.SafetyFlag == SafetyFlag.Blocked);
    }

    [Fact]
    public async Task GetFlaggedMessagesAsync_ProjectsAllExpectedFields()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var conv = NewConversation(deviceId, new DateTime(2026, 4, 8, 12, 0, 0, DateTimeKind.Utc));
        db.Set<Conversation>().Add(conv);
        var msg = NewMessage(conv.Id, MessageRole.Assistant, "review me", conv.StartedAt.AddSeconds(5), SafetyFlag.Blocked);
        db.Set<Message>().Add(msg);
        await db.SaveChangesAsync();

        var dto = (await service.GetFlaggedMessagesAsync(deviceId)).Single();

        Assert.Equal(msg.Id, dto.MessageId);
        Assert.Equal(conv.Id, dto.ConversationId);
        Assert.Equal(conv.StartedAt, dto.ConversationStartedAt);
        Assert.Equal("assistant", dto.Role);
        Assert.Equal("review me", dto.Content);
        Assert.Equal(msg.Timestamp, dto.Timestamp);
        Assert.Equal(SafetyFlag.Blocked, dto.SafetyFlag);
    }

    [Fact]
    public async Task GetFlaggedMessagesAsync_OrdersNewestFirst()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var conv = NewConversation(deviceId, new DateTime(2026, 4, 8, 12, 0, 0, DateTimeKind.Utc));
        db.Set<Conversation>().Add(conv);

        var m1 = NewMessage(conv.Id, MessageRole.User, "one", conv.StartedAt.AddSeconds(1), SafetyFlag.Flagged);
        var m2 = NewMessage(conv.Id, MessageRole.User, "two", conv.StartedAt.AddSeconds(2), SafetyFlag.Flagged);
        var m3 = NewMessage(conv.Id, MessageRole.User, "three", conv.StartedAt.AddSeconds(3), SafetyFlag.Blocked);
        db.Set<Message>().AddRange(m1, m2, m3);
        await db.SaveChangesAsync();

        var result = await service.GetFlaggedMessagesAsync(deviceId);

        Assert.Equal(3, result.Count);
        Assert.Equal(m3.Id, result[0].MessageId);
        Assert.Equal(m2.Id, result[1].MessageId);
        Assert.Equal(m1.Id, result[2].MessageId);
    }

    [Fact]
    public async Task GetFlaggedMessagesAsync_SpansMultipleConversationsOnSameDevice()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var t0 = new DateTime(2026, 4, 8, 12, 0, 0, DateTimeKind.Utc);

        var convA = NewConversation(deviceId, t0);
        var convB = NewConversation(deviceId, t0.AddMinutes(10));
        db.Set<Conversation>().AddRange(convA, convB);

        var mA = NewMessage(convA.Id, MessageRole.User, "from A", t0.AddSeconds(5), SafetyFlag.Flagged);
        var mB = NewMessage(convB.Id, MessageRole.User, "from B", t0.AddMinutes(10).AddSeconds(5), SafetyFlag.Blocked);
        db.Set<Message>().AddRange(mA, mB);
        await db.SaveChangesAsync();

        var result = await service.GetFlaggedMessagesAsync(deviceId);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.MessageId == mA.Id && r.ConversationId == convA.Id);
        Assert.Contains(result, r => r.MessageId == mB.Id && r.ConversationId == convB.Id);
    }

    [Fact]
    public async Task GetFlaggedMessagesAsync_FiltersByDeviceId()
    {
        var (service, db) = CreateService();
        var deviceA = Guid.NewGuid();
        var deviceB = Guid.NewGuid();
        var t0 = new DateTime(2026, 4, 8, 12, 0, 0, DateTimeKind.Utc);

        var convA = NewConversation(deviceA, t0);
        var convB = NewConversation(deviceB, t0);
        db.Set<Conversation>().AddRange(convA, convB);

        var mA = NewMessage(convA.Id, MessageRole.User, "A bad", t0.AddSeconds(1), SafetyFlag.Blocked);
        var mB = NewMessage(convB.Id, MessageRole.User, "B bad", t0.AddSeconds(2), SafetyFlag.Blocked);
        db.Set<Message>().AddRange(mA, mB);
        await db.SaveChangesAsync();

        var resultA = await service.GetFlaggedMessagesAsync(deviceA);
        var resultB = await service.GetFlaggedMessagesAsync(deviceB);

        var rowA = Assert.Single(resultA);
        Assert.Equal(mA.Id, rowA.MessageId);
        var rowB = Assert.Single(resultB);
        Assert.Equal(mB.Id, rowB.MessageId);
    }

    [Fact]
    public async Task GetFlaggedMessagesAsync_RespectsLimitAndOffset()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var t0 = new DateTime(2026, 4, 8, 12, 0, 0, DateTimeKind.Utc);
        var conv = NewConversation(deviceId, t0);
        db.Set<Conversation>().Add(conv);

        var msgs = new List<Message>();
        for (int i = 0; i < 5; i++)
            msgs.Add(NewMessage(conv.Id, MessageRole.User, "m" + i, t0.AddSeconds(i + 1), SafetyFlag.Flagged));
        db.Set<Message>().AddRange(msgs);
        await db.SaveChangesAsync();

        var page = await service.GetFlaggedMessagesAsync(deviceId, limit: 2, offset: 2);

        // Newest-first: msgs[4], msgs[3], msgs[2], msgs[1], msgs[0]
        // Skip 2, take 2 → msgs[2], msgs[1]
        Assert.Equal(2, page.Count);
        Assert.Equal(msgs[2].Id, page[0].MessageId);
        Assert.Equal(msgs[1].Id, page[1].MessageId);
    }

    [Fact]
    public async Task GetFlaggedMessagesAsync_OnlyCleanMessages_ReturnsEmptyList()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var conv = NewConversation(deviceId, DateTime.UtcNow);
        db.Set<Conversation>().Add(conv);
        db.Set<Message>().Add(NewMessage(conv.Id, MessageRole.User, "ok", conv.StartedAt.AddSeconds(1), SafetyFlag.Clean));
        await db.SaveChangesAsync();

        var result = await service.GetFlaggedMessagesAsync(deviceId);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetFlaggedMessagesAsync_UnknownDevice_ReturnsEmptyList()
    {
        var (service, _) = CreateService();
        var result = await service.GetFlaggedMessagesAsync(Guid.NewGuid());
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}

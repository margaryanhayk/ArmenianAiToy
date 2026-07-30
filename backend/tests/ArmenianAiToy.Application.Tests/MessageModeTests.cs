using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// E1.3 mode persistence — the Message.Mode column is stamped at write
/// time (never re-derived from history) and surfaces additively on the
/// parent-facing read shapes: per-message on the detail DTO, distinct
/// per-conversation on the summary DTO. Mirrors the InMemory pattern of
/// ConversationServiceGetSummariesTests.
/// </summary>
public class MessageModeTests
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

    private static Message NewMessage(
        Guid conversationId, MessageRole role, string content, DateTime ts, string? mode = null)
        => new()
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = role,
            Content = content,
            Timestamp = ts,
            SafetyFlag = SafetyFlag.Clean,
            Mode = mode
        };

    [Fact]
    public async Task StampMessageModeAsync_StoresMode_AndLeavesOthersNull()
    {
        var (service, db) = CreateService();
        var conv = NewConversation(Guid.NewGuid(), DateTime.UtcNow);
        db.Set<Conversation>().Add(conv);
        await db.SaveChangesAsync();

        var stamped = await service.AddMessageAsync(
            conv.Id, MessageRole.Assistant, "story reply");
        var unstamped = await service.AddMessageAsync(
            conv.Id, MessageRole.User, "child said something");
        await service.StampMessageModeAsync(stamped.Id, "story");

        var storedStamped = await db.Set<Message>().SingleAsync(m => m.Id == stamped.Id);
        var storedUnstamped = await db.Set<Message>().SingleAsync(m => m.Id == unstamped.Id);
        Assert.Equal("story", storedStamped.Mode);
        Assert.Null(storedUnstamped.Mode);
    }

    [Fact]
    public async Task StampMessageModeAsync_UnknownId_IsANoOp()
    {
        var (service, _) = CreateService();
        await service.StampMessageModeAsync(Guid.NewGuid(), "story");
    }

    [Fact]
    public async Task GetConversationSummariesAsync_ExposesDistinctModes()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var t0 = new DateTime(2026, 7, 28, 12, 0, 0, DateTimeKind.Utc);
        var conv = NewConversation(deviceId, t0);
        db.Set<Conversation>().Add(conv);
        db.Set<Message>().AddRange(
            NewMessage(conv.Id, MessageRole.User, "u1", t0.AddSeconds(1)),
            NewMessage(conv.Id, MessageRole.Assistant, "a1", t0.AddSeconds(2), mode: "story"),
            NewMessage(conv.Id, MessageRole.User, "u2", t0.AddSeconds(3)),
            NewMessage(conv.Id, MessageRole.Assistant, "a2", t0.AddSeconds(4), mode: "story"),
            NewMessage(conv.Id, MessageRole.Assistant, "a3", t0.AddSeconds(5), mode: "curiosity")
        );
        await db.SaveChangesAsync();

        var rows = await service.GetConversationSummariesAsync(deviceId);

        var row = Assert.Single(rows);
        Assert.NotNull(row.Modes);
        Assert.Equal(2, row.Modes!.Count);
        Assert.Contains("story", row.Modes);
        Assert.Contains("curiosity", row.Modes);
    }

    [Fact]
    public async Task GetConversationSummariesAsync_PreModeRows_YieldEmptyModes()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var t0 = new DateTime(2026, 7, 28, 12, 0, 0, DateTimeKind.Utc);
        var conv = NewConversation(deviceId, t0);
        db.Set<Conversation>().Add(conv);
        db.Set<Message>().AddRange(
            NewMessage(conv.Id, MessageRole.User, "u1", t0.AddSeconds(1)),
            NewMessage(conv.Id, MessageRole.Assistant, "a1", t0.AddSeconds(2))
        );
        await db.SaveChangesAsync();

        var rows = await service.GetConversationSummariesAsync(deviceId);

        var row = Assert.Single(rows);
        Assert.NotNull(row.Modes);
        Assert.Empty(row.Modes!);
    }

    [Fact]
    public async Task GetConversationByIdAsync_ExposesPerMessageMode()
    {
        var (service, db) = CreateService();
        var t0 = new DateTime(2026, 7, 28, 12, 0, 0, DateTimeKind.Utc);
        var conv = NewConversation(Guid.NewGuid(), t0);
        db.Set<Conversation>().Add(conv);
        db.Set<Message>().AddRange(
            NewMessage(conv.Id, MessageRole.User, "u1", t0.AddSeconds(1)),
            NewMessage(conv.Id, MessageRole.Assistant, "a1", t0.AddSeconds(2), mode: "riddle")
        );
        await db.SaveChangesAsync();

        var dto = await service.GetConversationByIdAsync(conv.Id);

        Assert.NotNull(dto);
        var assistant = dto!.Messages.Single(m => m.Role == "assistant");
        var user = dto.Messages.Single(m => m.Role == "user");
        Assert.Equal("riddle", assistant.Mode);
        Assert.Null(user.Mode);
    }
}

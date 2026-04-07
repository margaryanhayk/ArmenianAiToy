using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for ConversationService.GetConversationByIdAsync — the read path
/// behind the new GET /api/conversations/{id} parent-history endpoint.
/// Uses EF Core InMemory with the base DbContext type the service depends on.
/// </summary>
public class ConversationServiceGetByIdTests
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

    [Fact]
    public async Task GetConversationByIdAsync_ReturnsConversationWithMessagesInOrder()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var t0 = new DateTime(2026, 4, 8, 12, 0, 0, DateTimeKind.Utc);

        db.Set<Conversation>().Add(new Conversation
        {
            Id = conversationId,
            DeviceId = deviceId,
            StartedAt = t0
        });
        db.Set<Message>().AddRange(
            new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = conversationId,
                Role = MessageRole.User,
                Content = "Արի մի հեքիաթ պատմիր",
                Timestamp = t0.AddSeconds(1),
                SafetyFlag = SafetyFlag.Clean
            },
            new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = conversationId,
                Role = MessageRole.Assistant,
                Content = "Մի փոքրիկ նապաստակ ապրում էր անտառում։",
                Timestamp = t0.AddSeconds(2),
                SafetyFlag = SafetyFlag.Clean
            });
        await db.SaveChangesAsync();

        var dto = await service.GetConversationByIdAsync(conversationId);

        Assert.NotNull(dto);
        Assert.Equal(conversationId, dto!.Id);
        Assert.Equal(deviceId, dto.DeviceId);
        Assert.Equal(2, dto.MessageCount);
        Assert.False(dto.HasFlaggedContent);
        Assert.Equal("user", dto.Messages[0].Role);
        Assert.Equal("assistant", dto.Messages[1].Role);
        Assert.Equal("Արի մի հեքիաթ պատմիր", dto.Messages[0].Content);
        Assert.Equal("Մի փոքրիկ նապաստակ ապրում էր անտառում։", dto.Messages[1].Content);
    }

    [Fact]
    public async Task GetConversationByIdAsync_UnknownId_ReturnsNull()
    {
        var (service, _) = CreateService();
        var dto = await service.GetConversationByIdAsync(Guid.NewGuid());
        Assert.Null(dto);
    }

    [Fact]
    public async Task GetConversationByIdAsync_FlaggedMessage_SetsHasFlaggedContent()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();

        db.Set<Conversation>().Add(new Conversation
        {
            Id = conversationId,
            DeviceId = deviceId,
            StartedAt = DateTime.UtcNow
        });
        db.Set<Message>().Add(new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = MessageRole.User,
            Content = "blocked thing",
            Timestamp = DateTime.UtcNow,
            SafetyFlag = SafetyFlag.Blocked
        });
        await db.SaveChangesAsync();

        var dto = await service.GetConversationByIdAsync(conversationId);

        Assert.NotNull(dto);
        Assert.True(dto!.HasFlaggedContent);
    }
}

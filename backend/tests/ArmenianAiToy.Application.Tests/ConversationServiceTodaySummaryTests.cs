using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for ConversationService.GetTodaySummaryAsync — the read path
/// behind GET /api/conversations/today-summary (E1.2). Mirrors the
/// InMemory pattern used by ConversationServiceGetSummariesTests.
///
/// Pinned invariants:
///  - Per-message daily counts are EXACT (vs E1.1's whole-conversation
///    over-counting), boundary-correct at midnight UTC.
///  - AssistantMessagesWithAudio uses the same role gate as MessageDto's
///    AudioAvailable contract — child WAV uploads with AudioBlobPath set
///    are NEVER counted.
///  - Newest is ordered by Conversation.StartedAt desc, capped at 3.
///  - Flagged is ordered by latest flagged-today timestamp desc, capped
///    at 3, and excludes today-clean conversations.
///  - Snippets reuse the existing /summary trim convention.
///  - Cross-device messages are not mixed in.
/// </summary>
public class ConversationServiceTodaySummaryTests
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
        Guid conversationId, MessageRole role, string content, DateTime ts,
        SafetyFlag flag = SafetyFlag.Clean, string? audioBlobPath = null)
        => new()
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = role,
            Content = content,
            Timestamp = ts,
            SafetyFlag = flag,
            AudioBlobPath = audioBlobPath,
        };

    // Reference instant: 2026-04-30 12:00 UTC. dayStart = 2026-04-30 00:00 UTC.
    private static readonly DateTime AsOf =
        new(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DayStart =
        new(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Yesterday =
        new(2026, 4, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetTodaySummary_EmptyDb_ReturnsZeroCounts()
    {
        var (service, _) = CreateService();
        var deviceId = Guid.NewGuid();

        var result = await service.GetTodaySummaryAsync(deviceId, AsOf);

        Assert.Equal(deviceId, result.DeviceId);
        Assert.Equal(AsOf, result.AsOfUtc);
        Assert.Equal(DayStart, result.DayStartUtc);
        Assert.Equal(0, result.ConversationsCount);
        Assert.Equal(0, result.MessagesCount);
        Assert.Equal(0, result.FlaggedMessagesCount);
        Assert.Equal(0, result.AssistantMessagesWithAudio);
        Assert.Empty(result.Newest);
        Assert.Empty(result.Flagged);
    }

    [Fact]
    public async Task GetTodaySummary_OnlyYesterdayActivity_ReturnsZeroCounts()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var conv = NewConversation(deviceId, Yesterday);
        db.Set<Conversation>().Add(conv);
        db.Set<Message>().AddRange(
            NewMessage(conv.Id, MessageRole.User, "u", Yesterday),
            NewMessage(conv.Id, MessageRole.Assistant, "a", Yesterday.AddSeconds(1)));
        await db.SaveChangesAsync();

        var result = await service.GetTodaySummaryAsync(deviceId, AsOf);

        Assert.Equal(0, result.ConversationsCount);
        Assert.Equal(0, result.MessagesCount);
        Assert.Empty(result.Newest);
    }

    [Fact]
    public async Task GetTodaySummary_MessagesToday_CountedExactly()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var c1 = NewConversation(deviceId, DayStart.AddHours(1));
        var c2 = NewConversation(deviceId, DayStart.AddHours(2));
        var cYesterday = NewConversation(deviceId, Yesterday);
        db.Set<Conversation>().AddRange(c1, c2, cYesterday);
        db.Set<Message>().AddRange(
            // Today: 5 messages across c1 and c2.
            NewMessage(c1.Id, MessageRole.User, "u1", DayStart.AddHours(1)),
            NewMessage(c1.Id, MessageRole.Assistant, "a1", DayStart.AddHours(1).AddSeconds(1)),
            NewMessage(c1.Id, MessageRole.User, "u2", DayStart.AddHours(1).AddSeconds(2)),
            NewMessage(c2.Id, MessageRole.User, "u3", DayStart.AddHours(2)),
            NewMessage(c2.Id, MessageRole.Assistant, "a2", DayStart.AddHours(2).AddSeconds(1)),
            // Yesterday: 7 messages on cYesterday — must NOT be counted.
            NewMessage(cYesterday.Id, MessageRole.User, "y1", Yesterday),
            NewMessage(cYesterday.Id, MessageRole.User, "y2", Yesterday.AddSeconds(1)),
            NewMessage(cYesterday.Id, MessageRole.User, "y3", Yesterday.AddSeconds(2)),
            NewMessage(cYesterday.Id, MessageRole.User, "y4", Yesterday.AddSeconds(3)),
            NewMessage(cYesterday.Id, MessageRole.User, "y5", Yesterday.AddSeconds(4)),
            NewMessage(cYesterday.Id, MessageRole.User, "y6", Yesterday.AddSeconds(5)),
            NewMessage(cYesterday.Id, MessageRole.User, "y7", Yesterday.AddSeconds(6)));
        await db.SaveChangesAsync();

        var result = await service.GetTodaySummaryAsync(deviceId, AsOf);

        Assert.Equal(2, result.ConversationsCount);
        Assert.Equal(5, result.MessagesCount);
    }

    [Fact]
    public async Task GetTodaySummary_MessageAtDayStart_IsCounted()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var c = NewConversation(deviceId, DayStart);
        db.Set<Conversation>().Add(c);
        // Timestamp == DayStart should be counted (>= boundary).
        db.Set<Message>().Add(NewMessage(c.Id, MessageRole.User, "u", DayStart));
        await db.SaveChangesAsync();

        var result = await service.GetTodaySummaryAsync(deviceId, AsOf);

        Assert.Equal(1, result.MessagesCount);
        Assert.Equal(1, result.ConversationsCount);
    }

    [Fact]
    public async Task GetTodaySummary_MessageOneSecondBeforeDayStart_IsExcluded()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var c = NewConversation(deviceId, Yesterday);
        db.Set<Conversation>().Add(c);
        db.Set<Message>().Add(NewMessage(
            c.Id, MessageRole.User, "u", DayStart.AddSeconds(-1)));
        await db.SaveChangesAsync();

        var result = await service.GetTodaySummaryAsync(deviceId, AsOf);

        Assert.Equal(0, result.MessagesCount);
        Assert.Equal(0, result.ConversationsCount);
    }

    [Fact]
    public async Task GetTodaySummary_ConversationStartedYesterdayMessagedToday_IsCounted()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var c = NewConversation(deviceId, Yesterday);
        db.Set<Conversation>().Add(c);
        db.Set<Message>().AddRange(
            NewMessage(c.Id, MessageRole.User, "yesterday-user", Yesterday),
            NewMessage(c.Id, MessageRole.User, "today-user",
                DayStart.AddHours(1)));
        await db.SaveChangesAsync();

        var result = await service.GetTodaySummaryAsync(deviceId, AsOf);

        Assert.Equal(1, result.ConversationsCount);
        // Only today's 1 message counts toward MessagesCount.
        Assert.Equal(1, result.MessagesCount);
        // The conversation appears in Newest with StartedAt == Yesterday.
        Assert.Single(result.Newest);
        Assert.Equal(Yesterday, result.Newest[0].StartedAt);
        Assert.Equal(1, result.Newest[0].MessageCountToday);
    }

    [Fact]
    public async Task GetTodaySummary_FlaggedMessagesCountedExactly()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var c = NewConversation(deviceId, DayStart);
        db.Set<Conversation>().Add(c);
        db.Set<Message>().AddRange(
            NewMessage(c.Id, MessageRole.User, "clean", DayStart),
            NewMessage(c.Id, MessageRole.User, "flag1", DayStart.AddSeconds(1),
                flag: SafetyFlag.Flagged),
            NewMessage(c.Id, MessageRole.User, "block1", DayStart.AddSeconds(2),
                flag: SafetyFlag.Blocked),
            NewMessage(c.Id, MessageRole.User, "block2", DayStart.AddSeconds(3),
                flag: SafetyFlag.Blocked));
        await db.SaveChangesAsync();

        var result = await service.GetTodaySummaryAsync(deviceId, AsOf);

        Assert.Equal(3, result.FlaggedMessagesCount);
        Assert.Equal(4, result.MessagesCount);
    }

    [Fact]
    public async Task GetTodaySummary_AssistantAudio_OnlyAssistantWithAudioBlobPath()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var c = NewConversation(deviceId, DayStart);
        db.Set<Conversation>().Add(c);
        db.Set<Message>().AddRange(
            // Assistant w/ audio — counted (3 of these).
            NewMessage(c.Id, MessageRole.Assistant, "a1", DayStart,
                audioBlobPath: "abc/1.mp3"),
            NewMessage(c.Id, MessageRole.Assistant, "a2", DayStart.AddSeconds(1),
                audioBlobPath: "abc/2.mp3"),
            NewMessage(c.Id, MessageRole.Assistant, "a3", DayStart.AddSeconds(2),
                audioBlobPath: "abc/3.mp3"),
            // Assistant without audio — NOT counted.
            NewMessage(c.Id, MessageRole.Assistant, "a4", DayStart.AddSeconds(3)),
            // User with AudioBlobPath set (should not happen in production
            // but the role gate must structurally exclude it).
            NewMessage(c.Id, MessageRole.User, "u1", DayStart.AddSeconds(4),
                audioBlobPath: "abc/u1.wav"),
            NewMessage(c.Id, MessageRole.User, "u2", DayStart.AddSeconds(5),
                audioBlobPath: "abc/u2.wav"));
        await db.SaveChangesAsync();

        var result = await service.GetTodaySummaryAsync(deviceId, AsOf);

        Assert.Equal(3, result.AssistantMessagesWithAudio);
    }

    [Fact]
    public async Task GetTodaySummary_AssistantAudio_NoneWhenAudioBlobPathNull()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var c = NewConversation(deviceId, DayStart);
        db.Set<Conversation>().Add(c);
        db.Set<Message>().AddRange(
            NewMessage(c.Id, MessageRole.Assistant, "a", DayStart),
            NewMessage(c.Id, MessageRole.Assistant, "a", DayStart.AddSeconds(1)));
        await db.SaveChangesAsync();

        var result = await service.GetTodaySummaryAsync(deviceId, AsOf);

        Assert.Equal(0, result.AssistantMessagesWithAudio);
    }

    [Fact]
    public async Task GetTodaySummary_NewestList_LimitedToThreeOrderedByStartedAtDesc()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var c1 = NewConversation(deviceId, DayStart.AddHours(1));
        var c2 = NewConversation(deviceId, DayStart.AddHours(2));
        var c3 = NewConversation(deviceId, DayStart.AddHours(3));
        var c4 = NewConversation(deviceId, DayStart.AddHours(4));
        var c5 = NewConversation(deviceId, DayStart.AddHours(5));
        var c6 = NewConversation(deviceId, DayStart.AddHours(6));
        db.Set<Conversation>().AddRange(c1, c2, c3, c4, c5, c6);
        foreach (var c in new[] { c1, c2, c3, c4, c5, c6 })
        {
            db.Set<Message>().Add(
                NewMessage(c.Id, MessageRole.User, "msg", c.StartedAt));
        }
        await db.SaveChangesAsync();

        var result = await service.GetTodaySummaryAsync(deviceId, AsOf);

        Assert.Equal(6, result.ConversationsCount);
        Assert.Equal(3, result.Newest.Count);
        Assert.Equal(c6.Id, result.Newest[0].Id);
        Assert.Equal(c5.Id, result.Newest[1].Id);
        Assert.Equal(c4.Id, result.Newest[2].Id);
    }

    [Fact]
    public async Task GetTodaySummary_FlaggedList_LimitedAndOrderedByLatestFlaggedTimestamp()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var c1 = NewConversation(deviceId, DayStart.AddHours(1));
        var c2 = NewConversation(deviceId, DayStart.AddHours(2));
        var c3 = NewConversation(deviceId, DayStart.AddHours(3));
        var c4 = NewConversation(deviceId, DayStart.AddHours(4));
        db.Set<Conversation>().AddRange(c1, c2, c3, c4);

        // c1 has its flagged message LATEST (by Timestamp), at 11:00.
        // c2 flagged at 09:00, c3 flagged at 10:00, c4 flagged at 08:00.
        // Even though c4 was created LATEST, its latest-flagged-today
        // timestamp is OLDEST → it should not be in the top 3 by
        // latest-flagged ordering.
        db.Set<Message>().AddRange(
            NewMessage(c1.Id, MessageRole.User, "f1", DayStart.AddHours(11),
                flag: SafetyFlag.Flagged),
            NewMessage(c2.Id, MessageRole.User, "f2", DayStart.AddHours(9),
                flag: SafetyFlag.Flagged),
            NewMessage(c3.Id, MessageRole.User, "f3", DayStart.AddHours(10),
                flag: SafetyFlag.Flagged),
            NewMessage(c4.Id, MessageRole.User, "f4", DayStart.AddHours(8),
                flag: SafetyFlag.Flagged));
        await db.SaveChangesAsync();

        var result = await service.GetTodaySummaryAsync(deviceId, AsOf);

        Assert.Equal(3, result.Flagged.Count);
        Assert.Equal(c1.Id, result.Flagged[0].Id);   // latest flag at 11:00
        Assert.Equal(c3.Id, result.Flagged[1].Id);   // 10:00
        Assert.Equal(c2.Id, result.Flagged[2].Id);   // 09:00
        Assert.DoesNotContain(result.Flagged, r => r.Id == c4.Id);
    }

    [Fact]
    public async Task GetTodaySummary_FlaggedList_ExcludesCleanOnlyConversations()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var clean = NewConversation(deviceId, DayStart);
        var dirty = NewConversation(deviceId, DayStart.AddHours(1));
        db.Set<Conversation>().AddRange(clean, dirty);
        db.Set<Message>().AddRange(
            NewMessage(clean.Id, MessageRole.User, "ok", DayStart),
            NewMessage(dirty.Id, MessageRole.User, "bad", DayStart.AddHours(1),
                flag: SafetyFlag.Blocked));
        await db.SaveChangesAsync();

        var result = await service.GetTodaySummaryAsync(deviceId, AsOf);

        Assert.Single(result.Flagged);
        Assert.Equal(dirty.Id, result.Flagged[0].Id);
    }

    [Fact]
    public async Task GetTodaySummary_Snippets_TrimmedAtSnippetMaxLength()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var c = NewConversation(deviceId, DayStart);
        db.Set<Conversation>().Add(c);
        // 200-char user message — should be truncated to 120 + "…".
        var longContent = new string('A', 200);
        db.Set<Message>().Add(NewMessage(
            c.Id, MessageRole.User, longContent, DayStart));
        await db.SaveChangesAsync();

        var result = await service.GetTodaySummaryAsync(deviceId, AsOf);

        Assert.Single(result.Newest);
        Assert.NotNull(result.Newest[0].FirstUserSnippet);
        Assert.EndsWith("…", result.Newest[0].FirstUserSnippet!);
        // 120 base chars + 1 ellipsis char.
        Assert.Equal(121, result.Newest[0].FirstUserSnippet!.Length);
    }

    [Fact]
    public async Task GetTodaySummary_DifferentDevice_NotMixedIn()
    {
        var (service, db) = CreateService();
        var ourDevice = Guid.NewGuid();
        var otherDevice = Guid.NewGuid();
        var ours = NewConversation(ourDevice, DayStart);
        var theirs = NewConversation(otherDevice, DayStart);
        db.Set<Conversation>().AddRange(ours, theirs);
        db.Set<Message>().AddRange(
            NewMessage(ours.Id, MessageRole.User, "ours", DayStart),
            NewMessage(theirs.Id, MessageRole.User, "theirs",
                DayStart.AddSeconds(1)),
            NewMessage(theirs.Id, MessageRole.Assistant, "theirs-a",
                DayStart.AddSeconds(2), audioBlobPath: "x/y.mp3"));
        await db.SaveChangesAsync();

        var result = await service.GetTodaySummaryAsync(ourDevice, AsOf);

        Assert.Equal(1, result.ConversationsCount);
        Assert.Equal(1, result.MessagesCount);
        Assert.Equal(0, result.AssistantMessagesWithAudio);
        Assert.Single(result.Newest);
        Assert.Equal(ours.Id, result.Newest[0].Id);
    }
}

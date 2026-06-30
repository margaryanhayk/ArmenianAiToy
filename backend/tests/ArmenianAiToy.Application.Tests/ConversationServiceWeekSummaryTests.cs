using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for ConversationService.GetWeekSummaryAsync — the read path
/// behind GET /api/conversations/week-summary. Mirrors the InMemory
/// pattern + invariants of ConversationServiceTodaySummaryTests.
///
/// Pinned invariants:
///  - 7-day window INCLUDING today; Days always has exactly 7 entries
///    oldest-first, including zero-activity days.
///  - Per-message counts are EXACT and boundary-correct.
///  - Activity older than the window is excluded.
///  - AssistantMessagesWithAudio uses the role gate (Role == Assistant
///    AND AudioBlobPath != null) — child WAV uploads are NEVER counted.
///  - Cross-device messages are not mixed in.
///  - Unresolvable time zone fails soft to UTC (TimeZoneResolved=false,
///    TimeZoneId echoes the attempted id).
/// </summary>
public class ConversationServiceWeekSummaryTests
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

            modelBuilder.Entity<Device>().HasKey(d => d.Id);
            modelBuilder.Entity<Device>().Ignore(d => d.Conversations);
            modelBuilder.Entity<Device>().Ignore(d => d.ParentDevices);
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

    // Reference instant: 2026-04-30 12:00 UTC. With tz=UTC the window is
    // [2026-04-24 00:00, 2026-05-01 00:00); Days are 04-24..04-30.
    private const string Utc = "UTC";
    private static readonly DateTime AsOf = new(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WindowStart = new(2026, 4, 24, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Today = new(2026, 4, 30, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TwoDaysAgo = new(2026, 4, 28, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EightDaysAgo = new(2026, 4, 22, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetWeekSummary_EmptyDb_ReturnsSevenZeroDays()
    {
        var (service, _) = CreateService();
        var deviceId = Guid.NewGuid();

        var result = await service.GetWeekSummaryAsync(deviceId, AsOf, Utc);

        Assert.Equal(deviceId, result.DeviceId);
        Assert.Equal(AsOf, result.AsOfUtc);
        Assert.Equal(WindowStart, result.WindowStartUtc);
        Assert.Equal(0, result.ConversationsCount);
        Assert.Equal(0, result.MessagesCount);
        Assert.Equal(0, result.FlaggedMessagesCount);
        Assert.Equal(0, result.AssistantMessagesWithAudio);
        Assert.Equal(0, result.ActiveDays);
        Assert.Equal(7, result.Days.Count);
        Assert.All(result.Days, d => Assert.Equal(0, d.MessagesCount));
    }

    [Fact]
    public async Task GetWeekSummary_DaysAreOldestFirstAndSpanSeven()
    {
        var (service, _) = CreateService();

        var result = await service.GetWeekSummaryAsync(Guid.NewGuid(), AsOf, Utc);

        Assert.Equal(7, result.Days.Count);
        Assert.Equal(new DateTime(2026, 4, 24), result.Days[0].DateLocal);
        Assert.Equal(new DateTime(2026, 4, 30), result.Days[6].DateLocal);
        // strictly increasing by one day
        for (var i = 1; i < result.Days.Count; i++)
            Assert.Equal(result.Days[i - 1].DateLocal.AddDays(1), result.Days[i].DateLocal);
    }

    [Fact]
    public async Task GetWeekSummary_CountsAndBucketsAreExact()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();

        var convToday = NewConversation(deviceId, Today);
        var convTwoAgo = NewConversation(deviceId, TwoDaysAgo);
        db.Set<Conversation>().AddRange(convToday, convTwoAgo);
        db.Set<Message>().AddRange(
            NewMessage(convToday.Id, MessageRole.User, "u", Today),
            NewMessage(convToday.Id, MessageRole.Assistant, "a", Today.AddSeconds(1),
                audioBlobPath: "conv/today.mp3"),
            NewMessage(convTwoAgo.Id, MessageRole.User, "u2", TwoDaysAgo, SafetyFlag.Blocked),
            NewMessage(convTwoAgo.Id, MessageRole.Assistant, "a2", TwoDaysAgo.AddSeconds(1)));
        await db.SaveChangesAsync();

        var result = await service.GetWeekSummaryAsync(deviceId, AsOf, Utc);

        Assert.Equal(2, result.ConversationsCount);
        Assert.Equal(4, result.MessagesCount);
        Assert.Equal(1, result.FlaggedMessagesCount);
        Assert.Equal(1, result.AssistantMessagesWithAudio);
        Assert.Equal(2, result.ActiveDays);
        // 04-28 is index 4, 04-30 is index 6.
        Assert.Equal(2, result.Days[6].MessagesCount);
        Assert.Equal(0, result.Days[6].FlaggedMessagesCount);
        Assert.Equal(2, result.Days[4].MessagesCount);
        Assert.Equal(1, result.Days[4].FlaggedMessagesCount);
        Assert.Equal(0, result.Days[0].MessagesCount);
    }

    [Fact]
    public async Task GetWeekSummary_ActivityOlderThanWindow_IsExcluded()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var conv = NewConversation(deviceId, EightDaysAgo);
        db.Set<Conversation>().Add(conv);
        db.Set<Message>().Add(NewMessage(conv.Id, MessageRole.User, "old", EightDaysAgo));
        await db.SaveChangesAsync();

        var result = await service.GetWeekSummaryAsync(deviceId, AsOf, Utc);

        Assert.Equal(0, result.MessagesCount);
        Assert.Equal(0, result.ConversationsCount);
        Assert.Equal(0, result.ActiveDays);
    }

    [Fact]
    public async Task GetWeekSummary_ChildWavAudio_IsNotCountedAsAssistantAudio()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var conv = NewConversation(deviceId, Today);
        db.Set<Conversation>().Add(conv);
        db.Set<Message>().AddRange(
            // child upload WITH an audio path — must NOT count
            NewMessage(conv.Id, MessageRole.User, "u", Today, audioBlobPath: "conv/child.wav"),
            // assistant WITH an audio path — counts
            NewMessage(conv.Id, MessageRole.Assistant, "a", Today.AddSeconds(1),
                audioBlobPath: "conv/areg.mp3"));
        await db.SaveChangesAsync();

        var result = await service.GetWeekSummaryAsync(deviceId, AsOf, Utc);

        Assert.Equal(1, result.AssistantMessagesWithAudio);
    }

    [Fact]
    public async Task GetWeekSummary_OtherDeviceMessages_AreNotMixedIn()
    {
        var (service, db) = CreateService();
        var deviceId = Guid.NewGuid();
        var otherDeviceId = Guid.NewGuid();
        var mine = NewConversation(deviceId, Today);
        var theirs = NewConversation(otherDeviceId, Today);
        db.Set<Conversation>().AddRange(mine, theirs);
        db.Set<Message>().AddRange(
            NewMessage(mine.Id, MessageRole.User, "mine", Today),
            NewMessage(theirs.Id, MessageRole.User, "theirs", Today),
            NewMessage(theirs.Id, MessageRole.Assistant, "theirs2", Today.AddSeconds(1)));
        await db.SaveChangesAsync();

        var result = await service.GetWeekSummaryAsync(deviceId, AsOf, Utc);

        Assert.Equal(1, result.MessagesCount);
        Assert.Equal(1, result.ConversationsCount);
    }

    [Fact]
    public async Task GetWeekSummary_UnresolvableTimeZone_FailsSoftToUtc()
    {
        var (service, _) = CreateService();

        var result = await service.GetWeekSummaryAsync(Guid.NewGuid(), AsOf, "Mars/Phobos");

        Assert.False(result.TimeZoneResolved);
        Assert.Equal("Mars/Phobos", result.TimeZoneId);
        // Fell back to UTC, so the window anchors on the UTC calendar day.
        Assert.Equal(WindowStart, result.WindowStartUtc);
    }
}

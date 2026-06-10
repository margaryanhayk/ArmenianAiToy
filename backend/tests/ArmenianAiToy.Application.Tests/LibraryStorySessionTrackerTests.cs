using ArmenianAiToy.Application.Stories;
using Microsoft.Extensions.Time.Testing;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Pins the curated-library session tracker contract: sessions start
/// at segment 0, advance sequentially with a sliding 30-minute
/// inactivity expiry (same convention as ChatService.ChoiceExpiry —
/// exactly 30 minutes counts as expired), and Clear removes the
/// session. All time behavior is driven through FakeTimeProvider; no
/// sleeps. The tracker is not wired into any live flow in this slice.
/// </summary>
public class LibraryStorySessionTrackerTests
{
    private static readonly Guid ConversationId = Guid.NewGuid();
    private const string StoryId = InMemoryCuratedStoryLibrary.LittleCloudId;

    private readonly FakeTimeProvider _clock = new(
        new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero));
    private readonly LibraryStorySessionTracker _tracker;

    public LibraryStorySessionTrackerTests()
    {
        _tracker = new LibraryStorySessionTracker(_clock);
    }

    [Fact]
    public void Start_BeginsAtSegmentZero_WithStoryIdAndTimestamp()
    {
        var session = _tracker.Start(ConversationId, StoryId);

        Assert.Equal(StoryId, session.StoryId);
        Assert.Equal(0, session.SegmentIndex);
        Assert.Equal(_clock.GetUtcNow(), session.ActivatedAt);
    }

    [Fact]
    public void GetCurrent_AfterStart_ReturnsTheSession()
    {
        _tracker.Start(ConversationId, StoryId);

        var current = _tracker.GetCurrent(ConversationId);

        Assert.NotNull(current);
        Assert.Equal(StoryId, current!.StoryId);
        Assert.Equal(0, current.SegmentIndex);
    }

    [Fact]
    public void GetCurrent_UnknownConversation_ReturnsNull()
    {
        Assert.Null(_tracker.GetCurrent(Guid.NewGuid()));
    }

    [Fact]
    public void Advance_IncrementsSegmentIndexSequentially()
    {
        _tracker.Start(ConversationId, StoryId);

        var first = _tracker.Advance(ConversationId);
        var second = _tracker.Advance(ConversationId);

        Assert.Equal(1, first!.SegmentIndex);
        Assert.Equal(2, second!.SegmentIndex);
        Assert.Equal(2, _tracker.GetCurrent(ConversationId)!.SegmentIndex);
    }

    [Fact]
    public void Advance_WithoutSession_ReturnsNull()
    {
        Assert.Null(_tracker.Advance(ConversationId));
    }

    [Fact]
    public void GetCurrent_JustUnderThirtyMinutes_StillAlive()
    {
        _tracker.Start(ConversationId, StoryId);
        _clock.Advance(TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(1));

        Assert.NotNull(_tracker.GetCurrent(ConversationId));
    }

    [Fact]
    public void GetCurrent_AtExactlyThirtyMinutes_IsExpired()
    {
        _tracker.Start(ConversationId, StoryId);
        _clock.Advance(TimeSpan.FromMinutes(30));

        Assert.Null(_tracker.GetCurrent(ConversationId));
    }

    [Fact]
    public void Advance_OnExpiredSession_ReturnsNull()
    {
        _tracker.Start(ConversationId, StoryId);
        _clock.Advance(TimeSpan.FromMinutes(31));

        Assert.Null(_tracker.Advance(ConversationId));
    }

    [Fact]
    public void Advance_RefreshesExpiry_SlidingWindow()
    {
        _tracker.Start(ConversationId, StoryId);

        _clock.Advance(TimeSpan.FromMinutes(29));
        Assert.NotNull(_tracker.Advance(ConversationId));

        // 58 minutes after Start, but only 29 after the Advance —
        // the sliding window keeps the session alive.
        _clock.Advance(TimeSpan.FromMinutes(29));
        var current = _tracker.GetCurrent(ConversationId);

        Assert.NotNull(current);
        Assert.Equal(1, current!.SegmentIndex);
    }

    [Fact]
    public void Clear_RemovesSession()
    {
        _tracker.Start(ConversationId, StoryId);

        Assert.True(_tracker.Clear(ConversationId));
        Assert.Null(_tracker.GetCurrent(ConversationId));
    }

    [Fact]
    public void Clear_UnknownConversation_ReturnsFalse()
    {
        Assert.False(_tracker.Clear(Guid.NewGuid()));
    }

    [Fact]
    public void Start_ExistingSession_RestartsAtSegmentZero()
    {
        _tracker.Start(ConversationId, StoryId);
        _tracker.Advance(ConversationId);

        var restarted = _tracker.Start(ConversationId, StoryId);

        Assert.Equal(0, restarted.SegmentIndex);
        Assert.Equal(0, _tracker.GetCurrent(ConversationId)!.SegmentIndex);
    }

    [Fact]
    public void Sessions_AreIsolatedPerConversation()
    {
        var other = Guid.NewGuid();
        _tracker.Start(ConversationId, StoryId);
        _tracker.Start(other, StoryId);

        _tracker.Advance(ConversationId);

        Assert.Equal(1, _tracker.GetCurrent(ConversationId)!.SegmentIndex);
        Assert.Equal(0, _tracker.GetCurrent(other)!.SegmentIndex);
    }

    [Fact]
    public void Start_EmptyStoryId_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => _tracker.Start(ConversationId, " "));
    }
}

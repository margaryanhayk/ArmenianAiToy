using System.Collections.Concurrent;

namespace ArmenianAiToy.Application.Stories;

/// <summary>
/// A conversation's position inside a curated library story.
/// </summary>
/// <param name="StoryId">Library id of the active story.</param>
/// <param name="SegmentIndex">Zero-based index of the segment the
/// conversation is currently on.</param>
/// <param name="ActivatedAt">Last-activity timestamp (UTC). Start and
/// Advance both stamp it, so expiry is 30 minutes of inactivity —
/// matching the conversation-expiry convention.</param>
public sealed record LibraryStorySession(
    string StoryId,
    int SegmentIndex,
    DateTimeOffset ActivatedAt);

/// <summary>
/// In-memory per-conversation tracker for active curated-library
/// stories, mirroring the shape of ChatService's PendingChoices /
/// StoryMemories dictionaries (keyed by conversation id, 30-minute
/// expiry, staleness detected on access, no background cleanup).
/// <para>
/// Deliberately an instance class (not a static dictionary) so a later
/// wiring slice can register it as a singleton and tests stay
/// isolated. The tracker knows nothing about story length — callers
/// compare <see cref="LibraryStorySession.SegmentIndex"/> against
/// <see cref="CuratedStory.Segments"/> to detect the end. Not wired
/// into any live flow in this slice.
/// </para>
/// </summary>
public sealed class LibraryStorySessionTracker
{
    /// <summary>Inactivity window after which a session is treated as
    /// gone. Same 30-minute convention as ChatService.ChoiceExpiry and
    /// conversation auto-expiry.</summary>
    public static readonly TimeSpan SessionExpiry = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<Guid, LibraryStorySession> _sessions = new();
    private readonly TimeProvider _clock;

    public LibraryStorySessionTracker() : this(TimeProvider.System) { }

    /// <summary>Test seam — same pattern as ExportCooldown.</summary>
    public LibraryStorySessionTracker(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <summary>
    /// Starts (or restarts) a story for the conversation at segment 0.
    /// An existing session for the same conversation is replaced.
    /// </summary>
    public LibraryStorySession Start(Guid conversationId, string storyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storyId);
        var session = new LibraryStorySession(storyId, 0, _clock.GetUtcNow());
        _sessions[conversationId] = session;
        return session;
    }

    /// <summary>
    /// Returns the conversation's active session, or null when none
    /// exists or the session has sat inactive for
    /// <see cref="SessionExpiry"/> or longer (the stale entry is
    /// removed on detection).
    /// </summary>
    public LibraryStorySession? GetCurrent(Guid conversationId)
    {
        if (!_sessions.TryGetValue(conversationId, out var session))
        {
            return null;
        }

        if (IsExpired(session))
        {
            _sessions.TryRemove(conversationId, out _);
            return null;
        }

        return session;
    }

    /// <summary>
    /// Advances the conversation's session to the next segment and
    /// refreshes its activity timestamp (sliding expiry). Returns the
    /// updated session, or null when there is no live session to
    /// advance.
    /// </summary>
    public LibraryStorySession? Advance(Guid conversationId)
    {
        var current = GetCurrent(conversationId);
        if (current is null)
        {
            return null;
        }

        var advanced = current with
        {
            SegmentIndex = current.SegmentIndex + 1,
            ActivatedAt = _clock.GetUtcNow(),
        };
        _sessions[conversationId] = advanced;
        return advanced;
    }

    /// <summary>
    /// Removes the conversation's session. Returns true when a session
    /// (live or stale) was present.
    /// </summary>
    public bool Clear(Guid conversationId) =>
        _sessions.TryRemove(conversationId, out _);

    /// <summary>Valid iff elapsed &lt; SessionExpiry — same boundary
    /// convention as ChatService's ChoiceExpiry check (exactly 30
    /// minutes counts as expired).</summary>
    private bool IsExpired(LibraryStorySession session) =>
        _clock.GetUtcNow() - session.ActivatedAt >= SessionExpiry;
}

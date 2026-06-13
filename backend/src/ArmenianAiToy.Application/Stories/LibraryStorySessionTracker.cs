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
/// Marker left behind when a library story finishes (W4): which story
/// just ended for this conversation, and when. Lets the post-end
/// repeat cues («էլի», «նորից», …) deterministically distinguish
/// "story just finished" from "no story context at all" after the
/// session itself has been cleared. Same 30-minute expiry convention.
/// </summary>
/// <param name="StoryId">Library id of the story that ended.</param>
/// <param name="EndedAt">UTC timestamp of the ending turn.</param>
public sealed record LibraryStoryEnded(
    string StoryId,
    DateTimeOffset EndedAt);

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
    private readonly ConcurrentDictionary<Guid, LibraryStoryEnded> _ended = new();
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
        // A fresh session supersedes any "just ended" state — repeat
        // cues now mean "continue" again, not "play it again".
        _ended.TryRemove(conversationId, out _);
        return session;
    }

    /// <summary>Records that a story just finished for this
    /// conversation (called by the playback service on the ending
    /// turn, after the session is cleared).</summary>
    public void MarkEnded(Guid conversationId, string storyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storyId);
        _ended[conversationId] = new LibraryStoryEnded(storyId, _clock.GetUtcNow());
    }

    /// <summary>Returns the conversation's recently-ended marker, or
    /// null when none exists or it has sat for
    /// <see cref="SessionExpiry"/> or longer (stale entries are
    /// removed on detection — same convention as
    /// <see cref="GetCurrent"/>).</summary>
    public LibraryStoryEnded? GetRecentlyEnded(Guid conversationId)
    {
        if (!_ended.TryGetValue(conversationId, out var ended))
        {
            return null;
        }

        if (_clock.GetUtcNow() - ended.EndedAt >= SessionExpiry)
        {
            _ended.TryRemove(conversationId, out _);
            return null;
        }

        return ended;
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

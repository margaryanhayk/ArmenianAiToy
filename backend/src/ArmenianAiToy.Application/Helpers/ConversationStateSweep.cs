namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Pure staleness decision shared by the periodic sweep of ChatService's
/// process-wide, per-conversation dictionaries (PendingChoices,
/// StoryMemories, RiddleSessions, GameSessions, ActiveModes). Mirrors the
/// boundary every read-path check in ChatService already uses: an entry
/// is still live while <c>now - timestamp &lt; expiry</c>, so it becomes
/// stale at exactly <c>now - timestamp == expiry</c> — this returns
/// <c>true</c> at that exact boundary, the same convention
/// <c>LibraryStorySessionTracker</c> already uses for its own read-time
/// expiry check.
/// </summary>
public static class ConversationStateSweep
{
    /// <summary>
    /// True when an entry stamped at <paramref name="timestamp"/> is
    /// stale as of <paramref name="now"/>, i.e. has sat for
    /// <paramref name="expiry"/> or longer.
    /// </summary>
    public static bool IsStale(DateTime timestamp, DateTime now, TimeSpan expiry)
        => now - timestamp >= expiry;
}

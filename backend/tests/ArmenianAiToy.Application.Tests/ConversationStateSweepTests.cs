using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Enums;
using Xunit;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for the memory-sweep guard (2026-09-15) added to close the
/// unbounded process-lifetime growth of ChatService's five process-wide,
/// per-conversation dictionaries. Two layers:
/// <list type="bullet">
///   <item><description><see cref="ConversationStateSweep.IsStale"/> —
///   the pure boundary decision, tested directly including the exact
///   expiry-age boundary.</description></item>
///   <item><description><see cref="ChatService.SweepExpiredConversationState"/>
///   — one stale + one fresh entry per dictionary, proving the sweep
///   removes exactly the stale one and never the fresh one (the
///   guarantee that protects a child mid-story/mid-riddle/mid-game).</description></item>
/// </list>
/// Each test uses a fresh <see cref="Guid"/> per dictionary key, the same
/// idiom <c>ChoiceHandoffTests</c> already relies on, so tests never
/// collide on the shared static dictionaries.
/// </summary>
public class ConversationStateSweepTests
{
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(30);

    // ---- Pure boundary decision -------------------------------------

    [Fact]
    public void IsStale_EntryYoungerThanExpiry_ReturnsFalse()
    {
        var now = DateTime.UtcNow;
        var timestamp = now - (Expiry - TimeSpan.FromSeconds(1));

        Assert.False(ConversationStateSweep.IsStale(timestamp, now, Expiry));
    }

    [Fact]
    public void IsStale_EntryAtExactlyExpiryAge_ReturnsTrue()
    {
        var now = DateTime.UtcNow;
        var timestamp = now - Expiry;

        Assert.True(ConversationStateSweep.IsStale(timestamp, now, Expiry));
    }

    [Fact]
    public void IsStale_EntryOlderThanExpiry_ReturnsTrue()
    {
        var now = DateTime.UtcNow;
        var timestamp = now - (Expiry + TimeSpan.FromSeconds(1));

        Assert.True(ConversationStateSweep.IsStale(timestamp, now, Expiry));
    }

    // ---- Empty sweep --------------------------------------------------

    [Fact]
    public void Sweep_AgainstEmptyDictionaries_IsNoOpAndDoesNotThrow()
    {
        // Guids not present in any dictionary — the sweep still walks
        // whatever unrelated entries other tests may have left behind
        // (the dictionaries are process-wide statics), so this only
        // asserts the call itself is safe; it does not assert Total==0.
        var exception = Record.Exception(
            () => ChatService.SweepExpiredConversationState(DateTime.UtcNow));

        Assert.Null(exception);
    }

    // ---- PendingChoices -------------------------------------------------

    [Fact]
    public void Sweep_StalePendingChoice_IsRemoved()
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        ChatService.PendingChoices[id] = new ChatService.PendingChoice(
            "A", "B", now - Expiry - TimeSpan.FromMinutes(1));

        ChatService.SweepExpiredConversationState(now);

        Assert.False(ChatService.PendingChoices.ContainsKey(id));
    }

    [Fact]
    public void Sweep_FreshPendingChoice_IsNotRemoved()
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        ChatService.PendingChoices[id] = new ChatService.PendingChoice("A", "B", now);

        ChatService.SweepExpiredConversationState(now);

        Assert.True(ChatService.PendingChoices.ContainsKey(id));
    }

    // ---- StoryMemories --------------------------------------------------

    [Fact]
    public void Sweep_StaleStoryMemory_IsRemoved()
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        ChatService.StoryMemories[id] = new StoryMemory(
            "Character", null, null, null, null, now - Expiry - TimeSpan.FromMinutes(1));

        ChatService.SweepExpiredConversationState(now);

        Assert.False(ChatService.StoryMemories.ContainsKey(id));
    }

    [Fact]
    public void Sweep_FreshStoryMemory_IsNotRemoved()
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        ChatService.StoryMemories[id] = new StoryMemory(
            "Character", null, null, null, null, now);

        ChatService.SweepExpiredConversationState(now);

        Assert.True(ChatService.StoryMemories.ContainsKey(id));
    }

    // ---- RiddleSessions -------------------------------------------------

    [Fact]
    public void Sweep_StaleRiddleSession_IsRemoved()
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        ChatService.RiddleSessions[id] = new RiddleSessionState(
            null, 1, new List<string>(), now - Expiry - TimeSpan.FromMinutes(1));

        ChatService.SweepExpiredConversationState(now);

        Assert.False(ChatService.RiddleSessions.ContainsKey(id));
    }

    [Fact]
    public void Sweep_FreshRiddleSession_IsNotRemoved()
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        ChatService.RiddleSessions[id] = new RiddleSessionState(
            null, 1, new List<string>(), now);

        ChatService.SweepExpiredConversationState(now);

        Assert.True(ChatService.RiddleSessions.ContainsKey(id));
    }

    // ---- GameSessions ---------------------------------------------------

    [Fact]
    public void Sweep_StaleGameSession_IsRemoved()
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        ChatService.GameSessions[id] = new GameSessionState(
            null, new List<string>(), now - Expiry - TimeSpan.FromMinutes(1));

        ChatService.SweepExpiredConversationState(now);

        Assert.False(ChatService.GameSessions.ContainsKey(id));
    }

    [Fact]
    public void Sweep_FreshGameSession_IsNotRemoved()
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        ChatService.GameSessions[id] = new GameSessionState(
            null, new List<string>(), now);

        ChatService.SweepExpiredConversationState(now);

        Assert.True(ChatService.GameSessions.ContainsKey(id));
    }

    // ---- ActiveModes ------------------------------------------------------

    [Fact]
    public void Sweep_StaleActiveMode_IsRemoved()
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        ChatService.ActiveModes[id] = new ChatService.ActiveModeEntry(
            DetectedMode.Story, now - Expiry - TimeSpan.FromMinutes(1));

        ChatService.SweepExpiredConversationState(now);

        Assert.False(ChatService.ActiveModes.ContainsKey(id));
    }

    [Fact]
    public void Sweep_FreshActiveMode_IsNotRemoved()
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        ChatService.ActiveModes[id] = new ChatService.ActiveModeEntry(DetectedMode.Story, now);

        ChatService.SweepExpiredConversationState(now);

        Assert.True(ChatService.ActiveModes.ContainsKey(id));
    }

    // ---- Aggregate result shape ------------------------------------------

    [Fact]
    public void Sweep_MultipleStaleEntriesAcrossDictionaries_CountsEachDictionarySeparately()
    {
        var now = DateTime.UtcNow;
        var staleTimestamp = now - Expiry - TimeSpan.FromMinutes(1);

        var pendingId = Guid.NewGuid();
        var storyId = Guid.NewGuid();
        var riddleId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var modeId = Guid.NewGuid();

        ChatService.PendingChoices[pendingId] = new ChatService.PendingChoice("A", "B", staleTimestamp);
        ChatService.StoryMemories[storyId] = new StoryMemory(null, null, null, null, null, staleTimestamp);
        ChatService.RiddleSessions[riddleId] = new RiddleSessionState(null, 1, new List<string>(), staleTimestamp);
        ChatService.GameSessions[gameId] = new GameSessionState(null, new List<string>(), staleTimestamp);
        ChatService.ActiveModes[modeId] = new ChatService.ActiveModeEntry(DetectedMode.Riddle, staleTimestamp);

        var result = ChatService.SweepExpiredConversationState(now);

        Assert.True(result.PendingChoicesRemoved >= 1);
        Assert.True(result.StoryMemoriesRemoved >= 1);
        Assert.True(result.RiddleSessionsRemoved >= 1);
        Assert.True(result.GameSessionsRemoved >= 1);
        Assert.True(result.ActiveModesRemoved >= 1);
        Assert.Equal(
            result.PendingChoicesRemoved + result.StoryMemoriesRemoved + result.RiddleSessionsRemoved
                + result.GameSessionsRemoved + result.ActiveModesRemoved,
            result.Total);

        Assert.False(ChatService.PendingChoices.ContainsKey(pendingId));
        Assert.False(ChatService.StoryMemories.ContainsKey(storyId));
        Assert.False(ChatService.RiddleSessions.ContainsKey(riddleId));
        Assert.False(ChatService.GameSessions.ContainsKey(gameId));
        Assert.False(ChatService.ActiveModes.ContainsKey(modeId));
    }
}

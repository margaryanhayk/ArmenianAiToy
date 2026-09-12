using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// N11 — the ordering contract for <see cref="ChatService"/>'s
/// <c>CancellationToken</c> (a toy that drops Wi-Fi mid-turn):
/// <list type="bullet">
///   <item>never cancelled → byte-identical to before (every other
///     ChatService test passes the default token and is unchanged);</item>
///   <item>cancelled before the model call → <see cref="OperationCanceledException"/>,
///     the model is never invoked, nothing is stored;</item>
///   <item>cancelled while a safety check is in flight → nothing stored
///     (no assistant row), no reply;</item>
///   <item>cancelled after output moderation approved the reply → the
///     turn is still stored and returned (storage never observes the
///     token).</item>
/// </list>
/// The moderation substitute plays both adapter shapes: "completed, then
/// the caller was found gone" (returns a result after cancelling) and
/// "aborted mid-flight by the caller's token" (throws OCE after
/// cancelling), which is what <c>OpenAIModerationAdapter</c> does.
/// </summary>
public class ChatServiceCancellationTests
{
    private const string Reply = "Բարև՛, փոքրիկ։ Ինչպե՞ս ես։";

    private readonly IAiChatClient _aiClient = Substitute.For<IAiChatClient>();
    private readonly IModerationService _moderation = Substitute.For<IModerationService>();
    private readonly IConversationService _conversations = Substitute.For<IConversationService>();
    private readonly List<(MessageRole Role, string Content)> _stored = new();
    private readonly Guid _conversationId = Guid.NewGuid();

    public ChatServiceCancellationTests()
    {
        _moderation.CheckContentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult(true, new List<string>()));
        _aiClient.GetCompletionAsync(
                Arg.Any<string>(), Arg.Any<List<(string Role, string Content)>>(),
                Arg.Any<CancellationToken>())
            .Returns(Reply);

        _conversations.GetOrCreateActiveConversationAsync(Arg.Any<Guid>(), Arg.Any<Guid?>())
            .Returns(new Conversation
            {
                Id = _conversationId,
                DeviceId = Guid.NewGuid(),
                StartedAt = DateTime.UtcNow
            });
        _conversations.GetRecentMessagesAsync(Arg.Any<Guid>(), Arg.Any<int>())
            .Returns(new List<(string Role, string Content)>());
        _conversations.AddMessageAsync(
                Arg.Any<Guid>(), Arg.Any<MessageRole>(), Arg.Any<string>(), Arg.Any<SafetyFlag>())
            .Returns(callInfo =>
            {
                _stored.Add((callInfo.ArgAt<MessageRole>(1), callInfo.ArgAt<string>(2)));
                return new Message
                {
                    Id = Guid.NewGuid(),
                    ConversationId = callInfo.ArgAt<Guid>(0),
                    Role = callInfo.ArgAt<MessageRole>(1),
                    Content = callInfo.ArgAt<string>(2),
                    Timestamp = DateTime.UtcNow,
                    SafetyFlag = callInfo.ArgAt<SafetyFlag>(3)
                };
            });
    }

    private IChatService MakeService(
        LibraryStoryPlaybackService? playback = null, ICuratedStoryLibrary? library = null)
    {
        var childService = Substitute.For<IChildService>();
        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>()).Returns((Child?)null);
        var config = Substitute.For<IConfiguration>();
        config["SystemPrompt"].Returns("You are a test assistant.");
        return new ChatService(
            _aiClient, _moderation, _conversations, childService, config,
            Substitute.For<ILogger<ChatService>>(), new StoryChoiceCoherenceGate(),
            libraryPlayback: playback, storyLibrary: library,
            libraryQuestions: playback is null ? null : new LibraryStoryQuestionService(_aiClient));
    }

    private int ModelCalls() => _aiClient.ReceivedCalls().Count();

    /// <summary>Scripts the moderation substitute for the given input to
    /// cancel <paramref name="cts"/> while "in flight" and then either
    /// complete (<paramref name="adapterThrows"/> = false) or abort the way
    /// the real adapter does on a caller-cancelled token.</summary>
    private void ModerationCancelsCaller(string content, CancellationTokenSource cts, bool adapterThrows)
    {
        _moderation.CheckContentAsync(content, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                if (adapterThrows)
                    throw new OperationCanceledException(cts.Token);
                return new ModerationResult(true, new List<string>());
            });
    }

    // ── never cancelled ────────────────────────────────────────────

    [Fact]
    public async Task LiveToken_NeverCancelled_TurnCompletesAndStores()
    {
        using var cts = new CancellationTokenSource();

        var result = await MakeService().GetResponseAsync(
            Guid.NewGuid(), "Բարև Արեգ։", cancellationToken: cts.Token);

        Assert.Equal(Reply, result.Response);
        Assert.Equal(1, ModelCalls());
        Assert.Contains(_stored, m => m.Role == MessageRole.User);
        Assert.Contains(_stored, m => m.Role == MessageRole.Assistant && m.Content == Reply);
        // The token the caller handed in is the one the model saw — no
        // wrapping, no substitution.
        await _aiClient.Received(1).GetCompletionAsync(
            Arg.Any<string>(), Arg.Any<List<(string Role, string Content)>>(), cts.Token);
    }

    // ── cancelled before the model call ────────────────────────────

    [Fact]
    public async Task CancelledBeforeTheTurnStarts_Throws_ModelNeverInvoked_NothingStored()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MakeService().GetResponseAsync(Guid.NewGuid(), "Բարև Արեգ։", cancellationToken: cts.Token));

        Assert.Equal(0, ModelCalls());
        Assert.Empty(_stored);
    }

    [Theory]
    [InlineData(false)] // check completed, caller found gone right after
    [InlineData(true)]  // check aborted mid-flight (real adapter shape)
    public async Task CancelledDuringInputModeration_Throws_ModelNeverInvoked_NothingStored(bool adapterThrows)
    {
        using var cts = new CancellationTokenSource();
        const string input = "Բարև Արեգ։";
        ModerationCancelsCaller(input, cts, adapterThrows);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MakeService().GetResponseAsync(Guid.NewGuid(), input, cancellationToken: cts.Token));

        Assert.Equal(0, ModelCalls());
        Assert.Empty(_stored); // not even the child's own row
    }

    // ── cancelled during the output safety check ───────────────────

    [Fact]
    public async Task CancelledDuringOutputModeration_Throws_NoAssistantRowStored()
    {
        using var cts = new CancellationTokenSource();
        ModerationCancelsCaller(Reply, cts, adapterThrows: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MakeService().GetResponseAsync(Guid.NewGuid(), "Բարև Արեգ։", cancellationToken: cts.Token));

        // The model was paid for (unavoidable — the reply already came
        // back) but nothing it said reaches the child or the parent
        // dashboard. The child's own row (Step 3, stored before the model
        // call by long-standing design) is the same state as a Path-5
        // model failure today.
        Assert.Equal(1, ModelCalls());
        Assert.DoesNotContain(_stored, m => m.Role == MessageRole.Assistant);
    }

    // ── cancelled after approval ───────────────────────────────────

    [Fact]
    public async Task CancelledAfterOutputModerationApproved_TurnStillStoredAndReturned()
    {
        using var cts = new CancellationTokenSource();
        // Output check completes with "safe"; the caller is found gone
        // only afterwards. The reply is approved — storage must not care.
        ModerationCancelsCaller(Reply, cts, adapterThrows: false);

        var result = await MakeService().GetResponseAsync(
            Guid.NewGuid(), "Բարև Արեգ։", cancellationToken: cts.Token);

        Assert.True(cts.IsCancellationRequested);
        Assert.Equal(Reply, result.Response);
        Assert.Equal(SafetyFlag.Clean, result.SafetyFlag);
        Assert.Contains(_stored, m => m.Role == MessageRole.Assistant && m.Content == Reply);
    }

    // ── library autoplay ───────────────────────────────────────────

    [Fact]
    public async Task ContinueLibraryStory_CancelledBeforeTheTurnStarts_Throws_NothingStored()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MakeService().ContinueLibraryStoryAsync(Guid.NewGuid(), cancellationToken: cts.Token));

        Assert.Empty(_stored);
    }

    [Fact]
    public async Task ContinueLibraryStory_CancelledDuringOutputModeration_Throws_NothingStored()
    {
        var library = new InMemoryCuratedStoryLibrary();
        var tracker = new LibraryStorySessionTracker();
        var playback = new LibraryStoryPlaybackService(
            library, tracker, new StoryEngineOptions { Engine = "library" });
        var service = MakeService(playback, library);
        var deviceId = Guid.NewGuid();

        // Start a library story normally (default token), then the
        // autoplay step's output check is aborted by the caller.
        await service.GetResponseAsync(deviceId, "tell me a story");
        Assert.NotNull(tracker.GetCurrent(_conversationId));
        _stored.Clear();
        using var cts = new CancellationTokenSource();
        _moderation.CheckContentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<ModerationResult>(_ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ContinueLibraryStoryAsync(deviceId, cancellationToken: cts.Token));

        Assert.Empty(_stored);
        Assert.Equal(0, ModelCalls()); // library path never touches the model
    }
}

using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// End-to-end coverage for the Game Mode v2 multi-turn loop:
///   start game → child responds → continue (next round) → switch → stop.
/// Mirrors RiddleLoopIntegrationTests.
/// </summary>
public class GameLoopIntegrationTests
{
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly Guid _conversationId = Guid.NewGuid();
    private readonly IChatService _chatService;
    private readonly IAiChatClient _aiClient;

    public GameLoopIntegrationTests()
    {
        // Per-conversation cleanup only — mass Clear() would race with parallel tests.
        ChatService.GameSessions.TryRemove(_conversationId, out _);
        ChatService.RiddleSessions.TryRemove(_conversationId, out _);
        ChatService.PendingChoices.TryRemove(_conversationId, out _);
        ChatService.ActiveModes.TryRemove(_conversationId, out _);
        ChatService.StoryMemories.TryRemove(_conversationId, out _);

        _aiClient = Substitute.For<IAiChatClient>();
        var moderation = Substitute.For<IModerationService>();
        var conversations = Substitute.For<IConversationService>();
        var childService = Substitute.For<IChildService>();
        var logger = Substitute.For<ILogger<ChatService>>();

        var config = Substitute.For<IConfiguration>();
        config["SystemPrompt"].Returns("You are a test assistant.");

        moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(true, new List<string>()));

        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>())
            .Returns((Child?)null);

        var conversation = new Conversation
        {
            Id = _conversationId,
            DeviceId = _deviceId,
            StartedAt = DateTime.UtcNow
        };
        conversations.GetOrCreateActiveConversationAsync(Arg.Any<Guid>(), Arg.Any<Guid?>())
            .Returns(conversation);

        conversations.GetRecentMessagesAsync(Arg.Any<Guid>(), Arg.Any<int>())
            .Returns(new List<(string Role, string Content)>());

        conversations.AddMessageAsync(
            Arg.Any<Guid>(), Arg.Any<MessageRole>(), Arg.Any<string>(), Arg.Any<SafetyFlag>())
            .Returns(callInfo => new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = callInfo.ArgAt<Guid>(0),
                Role = callInfo.ArgAt<MessageRole>(1),
                Content = callInfo.ArgAt<string>(2),
                Timestamp = DateTime.UtcNow,
                SafetyFlag = callInfo.ArgAt<SafetyFlag>(3)
            });

        _chatService = new ChatService(
            _aiClient, moderation, conversations, childService, config, logger);
    }

    private const string GameWithBlock =
        "\u053e\u0561\u0583 \u057f\u0561\u0576\u0584 \u0574\u056b\u0561\u057d\u056b\u0576\u0589 \u0544\u0565\u056f, \u0565\u0580\u056f\u0578\u0582, \u0565\u0580\u0565\u0584\u0589\n---\nGAME_TYPE:clap_along\nGAME_DIFFICULTY:1";

    private const string ColorGameWithBlock =
        "\u0533\u057f\u056b\u0580 \u0574\u056b \u056f\u0561\u0580\u0574\u056b\u0580 \u0562\u0561\u0576\u0589\n---\nGAME_TYPE:color_find\nGAME_DIFFICULTY:1";

    [Fact]
    public async Task NewGame_StoresSessionAndStripsBlock()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);

        var result = await _chatService.GetResponseAsync(_deviceId, "let's play");

        Assert.Equal("game", result.Mode);
        Assert.DoesNotContain("GAME_TYPE", result.Response);
        Assert.DoesNotContain("---", result.Response);
        Assert.True(ChatService.GameSessions.TryGetValue(_conversationId, out var state));
        Assert.NotNull(state!.CurrentRound);
        Assert.Equal("clap_along", state.CurrentRound!.GameType);
        Assert.Equal(1, state.CurrentRound.Difficulty);
        Assert.Equal(0, state.CurrentRound.TurnsCompleted);
    }

    [Fact]
    public async Task NewGame_PromptIncludesNewGameDirective()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);

        await _chatService.GetResponseAsync(_deviceId, "let's play");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("GAME_TURN_KIND: new_game")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task ChildResponse_TriggersContinueAndBumpsTurns()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "let's play");

        _aiClient.ClearReceivedCalls();
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0531\u057a\u0580\u0565\u055b\u057d\u0589 \u0540\u056b\u0574\u0561\u055d \u0561\u057e\u0565\u056c\u056b \u0561\u0580\u0561\u0563\u0589");

        await _chatService.GetResponseAsync(_deviceId, "ok");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("GAME_TURN_KIND: continue")
                && s.Contains("clap_along")),
            Arg.Any<List<(string, string)>>());

        Assert.True(ChatService.GameSessions.TryGetValue(_conversationId, out var state));
        Assert.Equal(1, state!.CurrentRound!.TurnsCompleted);
        Assert.Equal(1, state.CurrentRound.Difficulty);
    }

    [Fact]
    public async Task DifficultyBumps_AfterTwoContinueTurns()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "let's play");

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0531\u057a\u0580\u0565\u055b\u057d\u0589 \u0540\u056b\u0574\u0561\u0589");

        await _chatService.GetResponseAsync(_deviceId, "ok"); // turns=1, diff=1
        await _chatService.GetResponseAsync(_deviceId, "ok"); // turns=2, diff bumps to 2

        Assert.True(ChatService.GameSessions.TryGetValue(_conversationId, out var state));
        Assert.Equal(2, state!.CurrentRound!.TurnsCompleted);
        Assert.Equal(2, state.CurrentRound.Difficulty);
    }

    [Fact]
    public async Task SwitchGame_ClearsRoundAndKeepsAvoidList()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "let's play");

        _aiClient.ClearReceivedCalls();
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(ColorGameWithBlock);

        await _chatService.GetResponseAsync(_deviceId, "another game");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("GAME_TURN_KIND: switch_game")
                && s.Contains("AVOID")
                && s.Contains("clap_along")),
            Arg.Any<List<(string, string)>>());

        Assert.True(ChatService.GameSessions.TryGetValue(_conversationId, out var state));
        Assert.NotNull(state!.CurrentRound);
        Assert.Equal("color_find", state.CurrentRound!.GameType);
        // Both types are tracked in RecentGameTypes (clap_along, color_find).
        Assert.Contains("clap_along", state.RecentGameTypes);
        Assert.Contains("color_find", state.RecentGameTypes);
    }

    [Fact]
    public async Task StopGame_TriggersStopDirectiveAndClearsRound()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "let's play");

        _aiClient.ClearReceivedCalls();
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u055b\u057e, \u056c\u0561\u057e \u056d\u0561\u0572 \u0567\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "\u0562\u0561\u057e \u0567"); // բավ է

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("GAME_TURN_KIND: stop_game")),
            Arg.Any<List<(string, string)>>());

        Assert.True(ChatService.GameSessions.TryGetValue(_conversationId, out var state));
        Assert.Null(state!.CurrentRound);
    }

    [Fact]
    public async Task ContinueDirective_IncludesRoundProgressionHint()
    {
        // v3: round-counter drives a per-turn playful hint. Round 3 should
        // tell the model to switch the SUBTYPE inside the same game type.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "let's play");

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0531\u057a\u0580\u0565\u055b\u057d\u0589 \u0540\u056b\u0574\u0561\u0589");

        await _chatService.GetResponseAsync(_deviceId, "ok");  // turns=1
        await _chatService.GetResponseAsync(_deviceId, "ok");  // turns=2

        _aiClient.ClearReceivedCalls();
        await _chatService.GetResponseAsync(_deviceId, "ok");  // turns=3 → switch SUBTYPE hint

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("GAME_TURN_KIND: continue")
                && s.Contains("switch the SUBTYPE")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task ContinueDirective_Round2_SwitchesSubtypeFromRound1()
    {
        // Regression for F-Game-1: the Round 2 runtime hint previously read
        // "Same subtype is OK here, but vary the specific item", which
        // directly contradicted the VARIETY POLICY and the BAD/GOOD pair
        // in GameModeInstruction (both require a different subtype on
        // every CONTINUE turn, including the first one). The Round 2
        // directive must now instruct the model to switch the subtype.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "let's play");

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0531\u057a\u0580\u0565\u055b\u057d\u0589 \u0540\u056b\u0574\u0561\u0589");

        await _chatService.GetResponseAsync(_deviceId, "ok");  // turns=1

        _aiClient.ClearReceivedCalls();
        await _chatService.GetResponseAsync(_deviceId, "ok");  // turns=2 → Round 2 hint

        // Assert the Round-2-specific wording that distinguishes this arm
        // from Round 1 ("set a friendly fun pace…") and Round 3+
        // ("switch the SUBTYPE inside this game type"): the "Do not repeat
        // the subtype you used in Round 1" phrasing is unique to Round 2.
        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("GAME_TURN_KIND: continue")
                && s.Contains("Do not repeat the subtype you used in Round 1")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task ContinueDirective_RotateCelebration()
    {
        // v3: directive asks the model to rotate the celebration phrase.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "let's play");

        _aiClient.ClearReceivedCalls();
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0531\u057a\u0580\u0565\u055b\u057d\u0589");

        await _chatService.GetResponseAsync(_deviceId, "ok");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("Rotate the celebration phrase")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task GameResponse_NeverCarriesStoryChoices()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);

        var result = await _chatService.GetResponseAsync(_deviceId, "let's play");

        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
        Assert.Null(result.StorySessionId);
    }
}

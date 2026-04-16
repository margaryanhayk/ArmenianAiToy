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
/// End-to-end coverage for the Riddle Mode v2 multi-turn loop:
///   start riddle → wrong guess → hint → wrong guess → reveal,
///   start riddle → correct guess → celebrate,
///   "another one" → fresh riddle.
/// </summary>
public class RiddleLoopIntegrationTests
{
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly Guid _conversationId = Guid.NewGuid();
    private readonly IChatService _chatService;
    private readonly IAiChatClient _aiClient;

    public RiddleLoopIntegrationTests()
    {
        // Each instance gets its own conversation id, so we only need to scrub
        // entries keyed by that id. Mass Clear() would race with other test
        // classes that depend on these statics.
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

    private const string RiddleWithBlock =
        "\u053f\u0561\u0580\u0574\u056b\u0580 \u0567, \u056f\u056c\u0578\u0580 \u0567, \u056e\u0561\u057c\u056b \u057e\u0580\u0561 \u0567 \u0561\u0573\u0578\u0582\u0574\u0589 \u053b\u055e\u0576\u0579 \u0567\u0589\n---\nRIDDLE_ANSWER:\u056d\u0576\u0571\u0578\u0580\nRIDDLE_CATEGORY:fruit\nRIDDLE_DIFFICULTY:1";

    [Fact]
    public async Task NewRiddle_StoresSessionAndStripsBlock()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(RiddleWithBlock);

        var result = await _chatService.GetResponseAsync(_deviceId, "give me a riddle");

        Assert.Equal("riddle", result.Mode);
        Assert.DoesNotContain("RIDDLE_ANSWER", result.Response);
        Assert.DoesNotContain("---", result.Response);
        Assert.True(ChatService.RiddleSessions.TryGetValue(_conversationId, out var state));
        Assert.NotNull(state!.CurrentRound);
        Assert.Equal("\u056d\u0576\u0571\u0578\u0580", state.CurrentRound!.AnswerDisplay);
        Assert.Equal("fruit", state.CurrentRound.Category);
    }

    [Fact]
    public async Task NewRiddle_PromptIncludesNewRiddleDirective()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(RiddleWithBlock);

        await _chatService.GetResponseAsync(_deviceId, "give me a riddle");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("RIDDLE_TURN_KIND: new_riddle")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task WrongGuess_TriggersHintDirectiveAndBumpsHintCount()
    {
        // Turn 1: pose riddle.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(RiddleWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "give me a riddle");

        // Turn 2: wrong guess «կատու» when answer is «խնձոր».
        _aiClient.ClearReceivedCalls();
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0548\u0579, \u0578\u0582\u0580\u056b\u0577 \u0562\u0561\u0576 \u0567\u0589 \u0553\u0578\u0580\u0571\u056b\u0580 \u0567\u056c\u056b\u0589");

        var result = await _chatService.GetResponseAsync(_deviceId, "\u056f\u0561\u057f\u0578\u0582");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("RIDDLE_TURN_KIND: hint")
                && s.Contains("\u056d\u0576\u0571\u0578\u0580")),
            Arg.Any<List<(string, string)>>());

        Assert.Equal("riddle", result.Mode);
        Assert.True(ChatService.RiddleSessions.TryGetValue(_conversationId, out var state));
        Assert.Equal(1, state!.CurrentRound!.HintsUsed);
    }

    [Fact]
    public async Task TwoWrongGuesses_ThirdWrongTriggersReveal()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(RiddleWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "give me a riddle");

        // Two wrong guesses.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0540\u056b\u0576\u057f\u0589");
        await _chatService.GetResponseAsync(_deviceId, "\u056f\u0561\u057f\u0578\u0582");
        await _chatService.GetResponseAsync(_deviceId, "\u0571\u0578\u0582\u056f");

        // Third wrong guess → reveal directive.
        _aiClient.ClearReceivedCalls();
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u054a\u0561\u057f\u0561\u057d\u056d\u0561\u0576\u0576 \u0567\u0580 \u056d\u0576\u0571\u0578\u0580\u0589");
        await _chatService.GetResponseAsync(_deviceId, "\u0584\u0561\u0580");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("RIDDLE_TURN_KIND: reveal")
                && s.Contains("\u056d\u0576\u0571\u0578\u0580")),
            Arg.Any<List<(string, string)>>());

        Assert.True(ChatService.RiddleSessions.TryGetValue(_conversationId, out var state));
        Assert.Null(state!.CurrentRound);
    }

    [Fact]
    public async Task CorrectGuess_TriggersCelebrateAndClearsRound()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(RiddleWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "give me a riddle");

        _aiClient.ClearReceivedCalls();
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0531\u057a\u0580\u0565\u055b\u057d, \u0573\u056b\u0577\u057f \u0567\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "\u056d\u0576\u0571\u0578\u0580");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("RIDDLE_TURN_KIND: celebrate")
                && s.Contains("\u056d\u0576\u0571\u0578\u0580")),
            Arg.Any<List<(string, string)>>());

        Assert.True(ChatService.RiddleSessions.TryGetValue(_conversationId, out var state));
        Assert.Null(state!.CurrentRound);
        Assert.Equal(2, state.LastDifficulty);
    }

    [Fact]
    public async Task GiveUp_TriggersRevealDirective()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(RiddleWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "give me a riddle");

        _aiClient.ClearReceivedCalls();
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u054a\u0561\u057f\u0561\u057d\u056d\u0561\u0576\u0576 \u0567\u0580 \u056d\u0576\u0571\u0578\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "\u0579\u0563\u056b\u057f\u0565\u0574"); // չգիտեմ

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("RIDDLE_TURN_KIND: reveal")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task AnotherOne_TriggersFreshNewRiddleWithAvoidedCategory()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(RiddleWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "give me a riddle");

        _aiClient.ClearReceivedCalls();
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(RiddleWithBlock);

        await _chatService.GetResponseAsync(_deviceId, "another one");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("RIDDLE_TURN_KIND: new_riddle")
                && s.Contains("AVOID")
                && s.Contains("fruit")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task RiddleResponse_NeverCarriesStoryChoices()
    {
        // A riddle reply must never produce ChoiceA/ChoiceB or a StorySessionId.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(RiddleWithBlock);

        var result = await _chatService.GetResponseAsync(_deviceId, "give me a riddle");

        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
        Assert.Null(result.StorySessionId);
    }
}

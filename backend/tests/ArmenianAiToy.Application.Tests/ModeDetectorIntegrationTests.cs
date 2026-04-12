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
/// Integration tests verifying that ModeDetector is wired into ChatService
/// and that non-Story modes correctly skip story-specific treatment.
/// </summary>
public class ModeDetectorIntegrationTests
{
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly Guid _conversationId = Guid.NewGuid();
    private readonly IChatService _chatService;
    private readonly IAiChatClient _aiClient;
    private readonly ILogger<ChatService> _logger;

    public ModeDetectorIntegrationTests()
    {
        _aiClient = Substitute.For<IAiChatClient>();
        var moderation = Substitute.For<IModerationService>();
        var conversations = Substitute.For<IConversationService>();
        var childService = Substitute.For<IChildService>();
        _logger = Substitute.For<ILogger<ChatService>>();

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
            _aiClient, moderation, conversations, childService, config, _logger);
    }

    [Fact]
    public async Task StoryTrigger_StillActivatesStoryMode()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("A fox walked.\n---\nCHOICE_A:Help fox\nCHOICE_B:Run away");

        var result = await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        Assert.NotNull(result.ChoiceA);
        Assert.NotNull(result.ChoiceB);
        Assert.NotNull(result.StorySessionId);
    }

    [Fact]
    public async Task CalmTriggerMidStory_SkipsStoryTreatment()
    {
        // Turn 1: Start a story — establishes pending choices.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("A fox walked.\n---\nCHOICE_A:Help fox\nCHOICE_B:Run away");
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        // Turn 2: Child says "I'm tired" — should exit story mode.
        // AI returns plain text (no choice block). If story mode were active,
        // fallback choice generation would fire and add choices. With calm
        // detection, story mode is false and the response stays choice-free.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580, \u0570\u0561\u0576\u0563\u057d\u057f\u0561\u0581\u056b\u0580\u0589"); // Լdelays delays delays

        var result = await _chatService.GetResponseAsync(_deviceId, "i'm tired");

        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
        Assert.Null(result.StorySessionId);
    }

    [Fact]
    public async Task CalmTrigger_DoesNotInjectStoryPrompt()
    {
        // Turn 1: Start a story.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("A fox.\n---\nCHOICE_A:Go left\nCHOICE_B:Go right");
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        // Turn 2: "good night" — calm mode.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "good night");

        // Verify the system prompt sent to AI does NOT contain the story instruction.
        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => !s.Contains("MANDATORY OUTPUT FORMAT")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task ExplicitChoiceSelection_AlwaysStoryMode()
    {
        // Turn 1: Start a story.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("A fox.\n---\nCHOICE_A:Go left\nCHOICE_B:Go right");
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        // Turn 2: Explicit selectedChoice=A bypasses ModeDetector entirely.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Fox went left.\n---\nCHOICE_A:Open door\nCHOICE_B:Climb tree");

        var result = await _chatService.GetResponseAsync(
            _deviceId, "A", selectedChoice: "A");

        Assert.NotNull(result.ChoiceA);
        Assert.NotNull(result.ChoiceB);
        Assert.NotNull(result.StorySessionId);
    }

    [Fact]
    public async Task StoryAboutSleeping_StaysInStoryMode()
    {
        // "tell me a story about sleeping" has both story and calm cues,
        // but story-cue gating in ModeDetector ensures Story wins.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("A bear slept.\n---\nCHOICE_A:Wake bear\nCHOICE_B:Let bear sleep");

        var result = await _chatService.GetResponseAsync(
            _deviceId, "tell me a story about sleeping");

        Assert.NotNull(result.ChoiceA);
        Assert.NotNull(result.ChoiceB);
    }

    [Fact]
    public async Task CuriosityMidStory_SkipsStoryTreatment()
    {
        // Turn 1: Start a story.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("A fox.\n---\nCHOICE_A:Go left\nCHOICE_B:Go right");
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        // Turn 2: Off-topic question — curiosity window.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("The sky is blue because of how light scatters.");

        var result = await _chatService.GetResponseAsync(
            _deviceId, "why is the sky blue");

        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
        Assert.Null(result.StorySessionId);
    }

    [Fact]
    public async Task NeutralMessageMidStory_ContinuesStory()
    {
        // Turn 1: Start a story.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("A fox.\n---\nCHOICE_A:Go left\nCHOICE_B:Go right");
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        // Turn 2: Neutral "ok" with active session — story continues.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Fox ran.\n---\nCHOICE_A:Chase\nCHOICE_B:Hide");

        var result = await _chatService.GetResponseAsync(_deviceId, "ok");

        Assert.NotNull(result.ChoiceA);
        Assert.NotNull(result.ChoiceB);
    }
}

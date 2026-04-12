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
    public async Task CalmTrigger_InjectsCalmPrompt()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0531\u0574\u0565\u0576 \u056b\u0576\u0579 \u056c\u0561\u057e \u0567\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "i'm sleepy");

        // Verify the system prompt contains the calm instruction marker.
        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("MODE: CALM / BEDTIME")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task CalmTrigger_PromptForbidsChoiceBlock()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "good night");

        // Verify the calm prompt explicitly bans CHOICE_A/CHOICE_B.
        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("Do NOT include a CHOICE_A / CHOICE_B block")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task CalmTrigger_PromptForbidsQuestions()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "bedtime");

        // Verify the calm prompt explicitly bans questions.
        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("Do NOT ask any questions")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task CalmArmenianTrigger_InjectsCalmPrompt()
    {
        // Armenian "գիշեր բարի" (good night).
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "\u0563\u056b\u0577\u0565\u0580 \u0562\u0561\u0580\u056b");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("MODE: CALM / BEDTIME")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task CalmMode_NoFormatReminderInjected()
    {
        // Calm mode must NOT inject the story format reminder into history.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "sleep now");

        // Verify the history passed to AI does NOT contain the format reminder.
        await _aiClient.Received().GetCompletionAsync(
            Arg.Any<string>(),
            Arg.Is<List<(string, string)>>(h =>
                !h.Any(m => m.Item2.Contains("FORMAT REMINDER"))));
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

    // ─────────────────────────────────────────────────────────────────────
    // Calm quality gate retry path (end-to-end)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CalmRetry_QuestionInFirstResponse_ReturnsCleanSecondResponse()
    {
        // First AI call returns a response with a question mark (violates calm rules).
        // Quality gate fires calm_question → retry. Second AI call returns clean text.
        var badResponse = "\u0531\u0579\u0584\u0565\u0580\u0564 \u0583\u0561\u056f\u056b\u0580, \u056b\u0576\u0579 \u0565\u057d \u0578\u0582\u0566\u0578\u0582\u0574?";
        var cleanResponse = "\u0531\u0579\u0584\u0565\u0580\u0564 \u0583\u0561\u056f\u056b\u0580, \u0561\u0574\u0565\u0576 \u056b\u0576\u0579 \u056c\u0561\u057e \u0567\u0580\u0589";

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(badResponse, cleanResponse);

        var result = await _chatService.GetResponseAsync(_deviceId, "good night");

        Assert.Equal(cleanResponse, result.Response);
        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
    }

    [Fact]
    public async Task CalmRetry_ExclamationInFirstResponse_ReturnsCleanSecondResponse()
    {
        var badResponse = "\u0548\u0582\u0580\u0561\u056d \u0565\u0576\u0584 \u057e\u0561\u0572\u0568!";
        var cleanResponse = "\u0531\u0574\u0565\u0576 \u056b\u0576\u0579 \u056c\u0561\u057e \u0567\u0580\u0589";

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(badResponse, cleanResponse);

        var result = await _chatService.GetResponseAsync(_deviceId, "i'm tired");

        Assert.Equal(cleanResponse, result.Response);
    }

    [Fact]
    public async Task CalmRetry_LogsRetryReason()
    {
        var badResponse = "\u053c\u0561\u057e \u0567\u0580?";
        var cleanResponse = "\u053c\u0561\u057e \u0567\u0580\u0589";

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(badResponse, cleanResponse);

        await _chatService.GetResponseAsync(_deviceId, "kpnem");

        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Quality gate retry triggered")
                && o.ToString()!.Contains("calm_question")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task CalmRetry_AiCalledTwice()
    {
        var badResponse = "\u053c\u0561\u057e \u0567\u0580!";
        var cleanResponse = "\u053c\u0561\u057e \u0567\u0580\u0589";

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(badResponse, cleanResponse);

        await _chatService.GetResponseAsync(_deviceId, "bedtime");

        // AI should be called exactly twice: initial + retry.
        await _aiClient.Received(2).GetCompletionAsync(
            Arg.Any<string>(), Arg.Any<List<(string, string)>>());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Curiosity Window
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CuriosityTrigger_InjectsCuriosityPrompt()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567\u0589");

        await _chatService.GetResponseAsync(_deviceId, "why is the sky blue");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("MODE: CURIOSITY WINDOW")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task CuriosityArmenianTrigger_InjectsCuriosityPrompt()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0541\u0575\u0578\u0582\u0576\u0568 \u057d\u057a\u056b\u057f\u0561\u056f \u0567\u0589");

        // Armenian "ինչու" (why)
        await _chatService.GetResponseAsync(_deviceId,
            "\u056b\u0576\u0579\u0578\u0582 \u0567 \u0571\u0575\u0578\u0582\u0576\u0568 \u057d\u057a\u056b\u057f\u0561\u056f");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("MODE: CURIOSITY WINDOW")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task CuriosityMidStory_PreservesPendingChoices()
    {
        // Turn 1: Start a story → establishes pending choices.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("A fox walked.\n---\nCHOICE_A:Help fox\nCHOICE_B:Run away");
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        // Turn 2: Curiosity detour — choices should be preserved.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567\u0589");
        await _chatService.GetResponseAsync(_deviceId, "why is the sky blue");

        // Verify pending choices still exist for this conversation.
        Assert.True(ChatService.PendingChoices.ContainsKey(_conversationId));
    }

    [Fact]
    public async Task CuriosityMidStory_StoryResumesOnNextTurn()
    {
        // Turn 1: Start a story.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("A fox walked.\n---\nCHOICE_A:Help fox\nCHOICE_B:Run away");
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        // Turn 2: Curiosity detour.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567\u0589");
        await _chatService.GetResponseAsync(_deviceId, "why is the sky blue");

        // Turn 3: Neutral message — story should resume because preserved choices
        // make hasActiveStorySession true, so ModeDetector returns Story.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Fox ran fast.\n---\nCHOICE_A:Chase\nCHOICE_B:Hide");

        var result = await _chatService.GetResponseAsync(_deviceId, "ok");

        Assert.NotNull(result.ChoiceA);
        Assert.NotNull(result.ChoiceB);
        Assert.NotNull(result.StorySessionId);
    }

    [Fact]
    public async Task CuriosityNoActiveStory_DoesNotStoreChoices()
    {
        // No prior story — just a curiosity question from scratch.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567\u0589");

        await _chatService.GetResponseAsync(_deviceId, "why is the sky blue");

        Assert.False(ChatService.PendingChoices.ContainsKey(_conversationId));
    }

    [Fact]
    public async Task CuriosityMode_NoFormatReminderInjected()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567\u0589");

        await _chatService.GetResponseAsync(_deviceId, "what is a rainbow");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Any<string>(),
            Arg.Is<List<(string, string)>>(h =>
                !h.Any(m => m.Item2.Contains("FORMAT REMINDER"))));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Game mode
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GameTrigger_InjectsGamePrompt()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053e\u0561\u0583\u056b\u056f \u057f\u0578\u0582\u0580 \u0565\u0580\u056f\u0578\u0582 \u0561\u0576\u0563\u0561\u0574\u0589");

        await _chatService.GetResponseAsync(_deviceId, "let's play a game");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("MODE: GAME")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task GameArmenianTrigger_InjectsGamePrompt()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053e\u0561\u0583\u056b\u056f \u057f\u0578\u0582\u0580\u0589");

        // Armenian "խdelays delays delaysdelays delays delays" (let's play)
        await _chatService.GetResponseAsync(_deviceId, "\u056d\u0561\u0572\u0561\u0576\u0584");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("MODE: GAME")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task GameMode_NoChoiceBlock()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053e\u0561\u0583\u056b\u056f \u057f\u0578\u0582\u0580\u0589");

        var result = await _chatService.GetResponseAsync(_deviceId, "play with me");

        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
        Assert.Null(result.StorySessionId);
    }

    [Fact]
    public async Task GameMode_NoFormatReminder()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053e\u0561\u0583\u056b\u056f \u057f\u0578\u0582\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "let's play");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Any<string>(),
            Arg.Is<List<(string, string)>>(h =>
                !h.Any(m => m.Item2.Contains("FORMAT REMINDER"))));
    }

    [Fact]
    public async Task GameMode_PromptForbidsStoryContent()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053e\u0561\u0583\u056b\u056f \u057f\u0578\u0582\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "let's play a game");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("Do NOT tell a story")),
            Arg.Any<List<(string, string)>>());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Riddle mode
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RiddleTrigger_InjectsRiddlePrompt()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053b\u0576\u0579 \u0567 \u0564\u0561, \u056b\u0576\u0579 \u0567 \u0564\u0561\u0589");

        await _chatService.GetResponseAsync(_deviceId, "give me a riddle");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("MODE: RIDDLE")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task RiddleArmenianTrigger_InjectsRiddlePrompt()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053b\u0576\u0579 \u0567 \u0564\u0561\u0589");

        // Armenian "delaysdelays delaysdelays delaysdelays delaysdelays delays" (riddle)
        await _chatService.GetResponseAsync(_deviceId, "\u0570\u0561\u0576\u0565\u056c\u0578\u0582\u056f");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("MODE: RIDDLE")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task RiddleMode_NoChoiceBlock()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053b\u0576\u0579 \u0567 \u0564\u0561\u0589");

        var result = await _chatService.GetResponseAsync(_deviceId, "riddle me this");

        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
        Assert.Null(result.StorySessionId);
    }

    [Fact]
    public async Task RiddleMode_NoFormatReminder()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053b\u0576\u0579 \u0567 \u0564\u0561\u0589");

        await _chatService.GetResponseAsync(_deviceId, "ask me a riddle");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Any<string>(),
            Arg.Is<List<(string, string)>>(h =>
                !h.Any(m => m.Item2.Contains("FORMAT REMINDER"))));
    }

    [Fact]
    public async Task RiddleMode_PromptForbidsTrickRiddles()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053b\u0576\u0579 \u0567 \u0564\u0561\u0589");

        await _chatService.GetResponseAsync(_deviceId, "give me a riddle");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("FORBIDDEN RIDDLE TYPES")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task RiddleMode_PromptContainsConcreteExamples()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053b\u0576\u0579 \u0567 \u0564\u0561\u0589");

        await _chatService.GetResponseAsync(_deviceId, "riddle me");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("GOOD RIDDLE EXAMPLES") && s.Contains("Sphinx")),
            Arg.Any<List<(string, string)>>());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Mode field in ChatResponse
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ModeField_Story_ReturnsStory()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("A fox.\n---\nCHOICE_A:Go\nCHOICE_B:Stay");
        var result = await _chatService.GetResponseAsync(_deviceId, "tell me a story");
        Assert.Equal("story", result.Mode);
    }

    [Fact]
    public async Task ModeField_Calm_ReturnsCalm()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580\u0589");
        var result = await _chatService.GetResponseAsync(_deviceId, "good night");
        Assert.Equal("calm", result.Mode);
    }

    [Fact]
    public async Task ModeField_Game_ReturnsGame()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053e\u0561\u0583\u056b\u056f \u057f\u0578\u0582\u0580\u0589");
        var result = await _chatService.GetResponseAsync(_deviceId, "let's play");
        Assert.Equal("game", result.Mode);
    }

    [Fact]
    public async Task ModeField_Riddle_ReturnsRiddle()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053b\u0576\u0579 \u0567 \u0564\u0561\u0589");
        var result = await _chatService.GetResponseAsync(_deviceId, "give me a riddle");
        Assert.Equal("riddle", result.Mode);
    }

    [Fact]
    public async Task ModeField_Curiosity_ReturnsCuriosity()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567\u0589");
        var result = await _chatService.GetResponseAsync(_deviceId, "why is the sky blue");
        Assert.Equal("curiosity", result.Mode);
    }

    [Fact]
    public async Task ModeField_NoMode_ReturnsNull()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0532\u0561\u0580\u0587 \u0571\u0565\u0566\u0589");
        var result = await _chatService.GetResponseAsync(_deviceId, "hello");
        Assert.Null(result.Mode);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Non-story mode choice-block isolation
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GameMode_LeakedChoiceBlock_DoesNotCreatePendingChoices()
    {
        // AI accidentally produces a choice block in Game mode.
        // The block should be stripped but NOT stored as pending choices.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Play!\n---\nCHOICE_A:Clap\nCHOICE_B:Jump");

        var result = await _chatService.GetResponseAsync(_deviceId, "let's play");

        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
        Assert.Null(result.StorySessionId);
        Assert.False(ChatService.PendingChoices.ContainsKey(_conversationId));
    }

    [Fact]
    public async Task CalmMode_LeakedChoiceBlock_DoesNotCreatePendingChoices()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Sleep.\n---\nCHOICE_A:Dream\nCHOICE_B:Rest");

        var result = await _chatService.GetResponseAsync(_deviceId, "good night");

        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
        Assert.False(ChatService.PendingChoices.ContainsKey(_conversationId));
    }

    [Fact]
    public async Task GameMode_LeakedChoiceBlock_StillStrippedFromResponse()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Play!\n---\nCHOICE_A:Clap\nCHOICE_B:Jump");

        var result = await _chatService.GetResponseAsync(_deviceId, "let's play a game");

        Assert.DoesNotContain("CHOICE_A", result.Response);
        Assert.DoesNotContain("CHOICE_B", result.Response);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Game/Riddle session persistence
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GameSession_ShortFollowUp_StaysInGame()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053e\u0561\u0583\u056b\u056f \u057f\u0578\u0582\u0580\u0589");

        // Turn 1: start game
        var r1 = await _chatService.GetResponseAsync(_deviceId, "lets play a game");
        Assert.Equal("game", r1.Mode);

        // Turn 2: short follow-up — should persist in game mode
        var r2 = await _chatService.GetResponseAsync(_deviceId, "ok I did it");
        Assert.Equal("game", r2.Mode);

        // Turn 3: another short follow-up
        var r3 = await _chatService.GetResponseAsync(_deviceId, "done");
        Assert.Equal("game", r3.Mode);
    }

    [Fact]
    public async Task RiddleSession_GuessFollowUp_StaysInRiddle()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053b\u0576\u0579 \u0567 \u0564\u0561\u0589");

        var r1 = await _chatService.GetResponseAsync(_deviceId, "give me a riddle");
        Assert.Equal("riddle", r1.Mode);

        var r2 = await _chatService.GetResponseAsync(_deviceId, "a cat");
        Assert.Equal("riddle", r2.Mode);

        var r3 = await _chatService.GetResponseAsync(_deviceId, "the sun");
        Assert.Equal("riddle", r3.Mode);
    }

    [Fact]
    public async Task GameSession_ExplicitStoryTrigger_OverridesGame()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053e\u0561\u0583\u056b\u056f\u0589",
                     "Fox.\n---\nCHOICE_A:Go\nCHOICE_B:Stay");

        await _chatService.GetResponseAsync(_deviceId, "lets play");
        var r2 = await _chatService.GetResponseAsync(_deviceId, "tell me a story");
        Assert.Equal("story", r2.Mode);
    }

    [Fact]
    public async Task GameSession_CalmTrigger_OverridesGame()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053e\u0561\u0583\u056b\u056f\u0589",
                     "\u053c\u0561\u057e \u0567\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "lets play");
        var r2 = await _chatService.GetResponseAsync(_deviceId, "good night");
        Assert.Equal("calm", r2.Mode);
    }

    [Fact]
    public async Task RiddleSession_CuriosityTrigger_OverridesRiddle()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053b\u0576\u0579 \u0567 \u0564\u0561\u0589",
                     "\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567\u0589");

        await _chatService.GetResponseAsync(_deviceId, "riddle me");
        var r2 = await _chatService.GetResponseAsync(_deviceId, "why is the sky blue");
        Assert.Equal("curiosity", r2.Mode);
    }

    [Fact]
    public async Task GameSession_ClearedAfterStory()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053e\u0561\u0583\u056b\u056f\u0589",
                     "Fox.\n---\nCHOICE_A:Go\nCHOICE_B:Stay",
                     "\u053e\u0561\u0583\u056b\u056f\u0589");

        await _chatService.GetResponseAsync(_deviceId, "lets play");

        // Switch to story — clears game session
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        // Short follow-up after story should NOT fall back to game
        var r3 = await _chatService.GetResponseAsync(_deviceId, "ok");
        // Story has pending choices → stays in story, not game
        Assert.Equal("story", r3.Mode);
    }

    [Fact]
    public async Task NoActiveSession_ShortMessage_ReturnsNone()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0532\u0561\u0580\u0587\u0589");

        // No prior game/riddle — short message stays as None
        var result = await _chatService.GetResponseAsync(_deviceId, "yes");
        Assert.Null(result.Mode);
    }
}

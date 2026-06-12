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
/// Integration tests for the <see cref="ArmenianVoiceReplyGuard"/> wiring in
/// <see cref="ChatService"/>. Verifies the three call-site contracts:
///   (a) garbled child input short-circuits BEFORE the LLM and returns the
///       canonical clarification line;
///   (b) non-story replies are capped at 3 sentences after generation;
///   (c) Story mode bypasses the sentence cap (its own length contract).
/// </summary>
public class ChatServiceVoiceGuardTests
{
    private readonly IChatService _chatService;
    private readonly IAiChatClient _aiClient;
    private readonly IConversationService _conversations;
    private readonly IModerationService _moderation;

    private string? _storedAssistantContent;

    public ChatServiceVoiceGuardTests()
    {
        _aiClient = Substitute.For<IAiChatClient>();
        _moderation = Substitute.For<IModerationService>();
        _conversations = Substitute.For<IConversationService>();
        var childService = Substitute.For<IChildService>();
        var logger = Substitute.For<ILogger<ChatService>>();

        var config = Substitute.For<IConfiguration>();
        config["SystemPrompt"].Returns("You are a test assistant.");

        _moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(true, new List<string>()));

        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>())
            .Returns((Child?)null);

        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            StartedAt = DateTime.UtcNow
        };
        _conversations.GetOrCreateActiveConversationAsync(Arg.Any<Guid>(), Arg.Any<Guid?>())
            .Returns(conversation);

        _conversations.GetRecentMessagesAsync(Arg.Any<Guid>(), Arg.Any<int>())
            .Returns(new List<(string Role, string Content)>());

        _conversations.AddMessageAsync(
            Arg.Any<Guid>(),
            MessageRole.Assistant,
            Arg.Do<string>(content => _storedAssistantContent = content),
            Arg.Any<SafetyFlag>())
            .Returns(callInfo => new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = callInfo.ArgAt<Guid>(0),
                Role = MessageRole.Assistant,
                Content = callInfo.ArgAt<string>(2),
                Timestamp = DateTime.UtcNow,
                SafetyFlag = callInfo.ArgAt<SafetyFlag>(3)
            });

        _conversations.AddMessageAsync(
            Arg.Any<Guid>(),
            MessageRole.User,
            Arg.Any<string>(),
            Arg.Any<SafetyFlag>())
            .Returns(callInfo => new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = callInfo.ArgAt<Guid>(0),
                Role = MessageRole.User,
                Content = callInfo.ArgAt<string>(2),
                Timestamp = DateTime.UtcNow,
                SafetyFlag = callInfo.ArgAt<SafetyFlag>(3)
            });

        _chatService = new ChatService(
            _aiClient, _moderation, _conversations, childService, config, logger,
            new StoryChoiceCoherenceGate());
    }

    [Fact]
    public async Task GarbledInput_ShortCircuits_ReturnsExactClarification_AndSkipsLlm()
    {
        // Sanity check the exact text contract — if this drifts the firmware
        // will be playing back unexpected audio.
        Assert.Equal("Կներե՛ս, լավ չլսեցի։ Կրկնի՞ր, խնդրում եմ։",
                     ArmenianVoiceReplyGuard.ClarificationResponse);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "քրռռռռ բխխխ");

        Assert.Equal(ArmenianVoiceReplyGuard.ClarificationResponse, result.Response);
        Assert.Equal(ArmenianVoiceReplyGuard.ClarificationResponse, _storedAssistantContent);
        Assert.Equal(SafetyFlag.Clean, result.SafetyFlag);
        await _aiClient.DidNotReceive().GetCompletionAsync(
            Arg.Any<string>(), Arg.Any<List<(string Role, string Content)>>());
    }

    [Fact]
    public async Task NonStoryReply_FiveSentences_TrimmedToThree()
    {
        // No story trigger in the user message → detectedMode = None → guard runs.
        var fiveSentenceReply = "Մեկ։ Երկու։ Երեք։ Չորս։ Հինգ։";
        _aiClient.GetCompletionAsync(
                Arg.Any<string>(), Arg.Any<List<(string Role, string Content)>>())
            .Returns(fiveSentenceReply);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "Բարև Արեգ։");

        Assert.Equal("Մեկ։ Երկու։ Երեք։", result.Response);
        Assert.Equal("Մեկ։ Երկու։ Երեք։", _storedAssistantContent);
    }

    [Fact]
    public async Task CalmReply_FourSentences_PreservedPerModesPacing()
    {
        // MODES.md Calm/Bedtime pacing allows 2-4 sentences — the voice cap
        // is mode-aware and must NOT trim the fourth wind-down sentence.
        // "good night" is a canonical Calm trigger.
        var fourSentenceReply = "Մեկ։ Երկու։ Երեք։ Չորս։";
        _aiClient.GetCompletionAsync(
                Arg.Any<string>(), Arg.Any<List<(string Role, string Content)>>())
            .Returns(fourSentenceReply);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "good night");

        Assert.Equal(fourSentenceReply, result.Response);
        Assert.Equal(fourSentenceReply, _storedAssistantContent);
    }

    [Fact]
    public async Task CalmReply_FiveSentences_TrimmedToFour()
    {
        // The Calm cap is 4, not uncapped: a runaway fifth sentence still
        // gets trimmed at the sentence boundary.
        var fiveSentenceReply = "Մեկ։ Երկու։ Երեք։ Չորս։ Հինգ։";
        _aiClient.GetCompletionAsync(
                Arg.Any<string>(), Arg.Any<List<(string Role, string Content)>>())
            .Returns(fiveSentenceReply);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "good night");

        Assert.Equal("Մեկ։ Երկու։ Երեք։ Չորս։", result.Response);
        Assert.Equal("Մեկ։ Երկու։ Երեք։ Չորս։", _storedAssistantContent);
    }

    [Fact]
    public async Task StoryReply_FiveSentencesPlusChoiceBlock_PreservesAllFiveSentences()
    {
        // "tell me a story" is a canonical Story trigger → isStoryMode=true →
        // guard MUST skip both the typo pass and the sentence cap. The
        // CHOICE_A/B tail block is stripped upstream by TailBlockParser, so by
        // the time we inspect the stored content only the prose remains.
        var storyReply =
            "Մեկ։ Երկու։ Երեք։ Չորս։ Հինգ։"
            + "\n---\nCHOICE_A:Օգնել աղվեսին\nCHOICE_B:Մենակ անցնել";
        _aiClient.GetCompletionAsync(
                Arg.Any<string>(), Arg.Any<List<(string Role, string Content)>>())
            .Returns(storyReply);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "tell me a story");

        // Only the prose contract matters here — five sentences must survive
        // the post-AI pipeline unmodified. The CHOICE_A/B labels in the mock
        // tail block don't anchor to the placeholder prose, so the Story
        // coherence gate's deterministic-repair step (Step 10c-final-coh)
        // legitimately rewrites them. That belongs to StoryChoiceCoherenceGate
        // tests, not this one — we only assert that both labels are still
        // non-null (i.e. Story mode preserved its CHOICE_A/B contract).
        Assert.Equal("Մեկ։ Երկու։ Երեք։ Չորս։ Հինգ։", result.Response);
        Assert.Equal("Մեկ։ Երկու։ Երեք։ Չորս։ Հինգ։", _storedAssistantContent);
        Assert.NotNull(result.ChoiceA);
        Assert.NotNull(result.ChoiceB);
    }
}

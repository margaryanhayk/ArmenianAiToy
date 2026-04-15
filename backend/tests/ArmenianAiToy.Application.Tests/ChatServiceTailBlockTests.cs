using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests that ChatService strips the tail block from assistant responses
/// before storing and returning to the client.
/// </summary>
public class ChatServiceTailBlockTests
{
    private readonly IChatService _chatService;
    private readonly IAiChatClient _aiClient;
    private readonly IConversationService _conversations;
    private readonly IModerationService _moderation;

    // Captures the content string passed to AddMessageAsync for the assistant message.
    private string? _storedAssistantContent;

    public ChatServiceTailBlockTests()
    {
        _aiClient = Substitute.For<IAiChatClient>();
        _moderation = Substitute.For<IModerationService>();
        _conversations = Substitute.For<IConversationService>();
        var childService = Substitute.For<IChildService>();
        var logger = Substitute.For<ILogger<ChatService>>();

        var config = Substitute.For<IConfiguration>();
        config["SystemPrompt"].Returns("You are a test assistant.");

        // Default: moderation always passes
        _moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(true, new List<string>()));

        // Default: no child
        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>())
            .Returns((Child?)null);

        // Default: return a conversation
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

        // Capture the stored assistant message content
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

        // Also handle user message storage (don't capture)
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
            _aiClient, _moderation, _conversations, childService, config, logger);
    }

    [Fact]
    public async Task ResponseWithTailBlock_StoresCleanedText()
    {
        var rawAiResponse = "Աղվեսը վազեց դեպի գետ։\n---\nCHOICE_A:Օգնել աղվեսին\nCHOICE_B:Մենակ անցնել";
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(rawAiResponse);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "hello");

        Assert.Equal("Աղվեսը վազեց դեպի գետ։", result.Response);
        Assert.Equal("Աղվեսը վազեց դեպի գետ։", _storedAssistantContent);
    }

    [Fact]
    public async Task ResponseWithoutTailBlock_PassesThroughUnchanged()
    {
        var rawAiResponse = "Մի սովորական պատասխան։";
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(rawAiResponse);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "hello");

        Assert.Equal("Մի սովորական պատասխան։", result.Response);
        Assert.Equal("Մի սովորական պատասխան։", _storedAssistantContent);
    }

    [Fact]
    public async Task QualityGate_LatinRun_TriggersOneRetry()
    {
        // First call: contains 4+ Latin letters in a row → must retry.
        // Second call: clean Armenian → must be the stored response.
        var bad = "Once կար մեկ նապաստակ։\n---\nCHOICE_A:Բացենք տուփը\nCHOICE_B:Փակենք տուփը";
        var good = "Կար մի նապաստակ։\n---\nCHOICE_A:Բացենք տուփը\nCHOICE_B:Փակենք տուփը";
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(bad, good);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "tell me a story");

        await _aiClient.Received(2).GetCompletionAsync(
            Arg.Any<string>(), Arg.Any<List<(string, string)>>());
        Assert.Equal("Կար մի նապաստակ։", result.Response);
        Assert.Equal("Կար մի նապաստակ։", _storedAssistantContent);
    }

    [Fact]
    public async Task QualityGate_CleanResponse_DoesNotRetry()
    {
        var good = "Կար մի նապաստակ։\n---\nCHOICE_A:Բացենք տուփը\nCHOICE_B:Փակենք տուփը";
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(good);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "tell me a story about a bunny");

        await _aiClient.Received(1).GetCompletionAsync(
            Arg.Any<string>(), Arg.Any<List<(string, string)>>());
        Assert.Equal("Կար մի նապաստակ։", result.Response);
    }

    [Fact]
    public async Task MalformedTailBlock_CleanedByResponseCleaner()
    {
        // TailBlockParser can't parse this (missing CHOICE_B), so the raw text
        // passes through. ResponseCleaner then strips the leaked artifacts.
        var rawAiResponse = "Հեքիաթ։\n---\nCHOICE_A:Only one option";
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(rawAiResponse);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "hello");

        Assert.Equal("Հեքիաթ։", result.Response);
        Assert.Equal("Հեքիաթ։", _storedAssistantContent);
    }

    [Fact]
    public async Task ChoiceLabel_LatinRun_DropsBothChoices_PreservesProse()
    {
        // Mock AI returns clean Armenian prose but English choice labels.
        // Prose passes the post-retry recheck; choices trip the new
        // choice-label latin_run check. Expectation: prose is preserved,
        // both choices are cleared (we can't show one without the other).
        var raw = "Աղվեսը վազեց անտառով։\n---\nCHOICE_A:Find the fox\nCHOICE_B:Open the box";
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(raw);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "tell me a story");

        Assert.Equal("Աղվեսը վազեց անտառով։", result.Response);
        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
        Assert.Null(result.StorySessionId);
    }

    [Fact]
    public async Task ChoiceLabel_OnlyOneHasLatin_StillDropsBoth()
    {
        // If only one choice has Latin, we still drop both — can't show one
        // without a counterpart.
        var raw = "Աղվեսը վազեց։\n---\nCHOICE_A:Բացենք\nCHOICE_B:Find the box";
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(raw);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "tell me a story");

        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
    }

    [Fact]
    public async Task QualityGate_LatinRun_RetryAlsoLatinPassesModeration_FallsBackToSafety()
    {
        // Failure mode B: first response has Latin → triggers retry.
        // Retry response ALSO has Latin (model stuck on a foreign word) but
        // passes moderation. Without the post-retry recheck, the still-bad
        // text would replace aiResponse and reach the child unchanged.
        // Expectation: post-retry latin_run hard check falls back to safety.
        var firstBad = "Once կար մի աղվես։\n---\nCHOICE_A:Բացենք\nCHOICE_B:Փակենք";
        var retryAlsoBad = "Forever կար մի աղվես։\n---\nCHOICE_A:Բացենք\nCHOICE_B:Փակենք";
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(firstBad, retryAlsoBad);

        // Default moderation mock returns IsSafe=true for every call.
        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "tell me a story");

        // Both AI calls should have happened (original + 1 retry).
        await _aiClient.Received(2).GetCompletionAsync(
            Arg.Any<string>(), Arg.Any<List<(string, string)>>());
        // Final response must NOT contain a 4+ Latin run.
        Assert.DoesNotMatch("[A-Za-z]{4,}", result.Response);
        // Choices must be cleared since we fell back to safety.
        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
    }

    [Fact]
    public async Task QualityGate_LatinRun_RetryRejectedByModeration_FallsBackToSafety()
    {
        // First call: contains Latin → triggers latin_run retry.
        // Second call (retry): also has Latin (same English).
        // Moderation: first call passes (default), retry call REJECTS.
        // Expectation: do NOT silently keep the original Latin response;
        // instead use the safety fallback so no English text reaches the child.
        var bad = "Once upon a time there was a fox.\n---\nCHOICE_A:Բացենք\nCHOICE_B:Փակենք";
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(bad);

        // First moderation call (input + output of first AI call): safe.
        // Retry moderation: unsafe.
        _moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(
                new ModerationResult(true, new List<string>()),  // input mod
                new ModerationResult(true, new List<string>()),  // output mod
                new ModerationResult(false, new List<string> { "moderation_unavailable" })); // retry mod

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "tell me a story");

        // Should NOT contain the Latin text. Should be the safety fallback.
        Assert.DoesNotMatch("[A-Za-z]{4,}", result.Response);
    }

    [Fact]
    public async Task ChoiceDiversityGuard_SameFirstVerb_TriggersRegenerationPath()
    {
        // Phase E E2: first AI response has structurally weak choices
        // (same first verb, swapped noun). The diversity guard must reject
        // them and fall through to the Step 10c regeneration path, which
        // calls the AI again with ChoiceGenerationPrompt. The second call
        // returns genuinely different verbs — those must be what reaches
        // the child.
        var weakPair = "Աղվեսը վազեց անտառով։\n---\nCHOICE_A:Բացենք փոքրիկ տուփը\nCHOICE_B:Բացենք մեծ դուռը";
        // Regeneration prompt expects raw CHOICE_A/CHOICE_B lines; the
        // Step 10c code prepends "\n---\n" before parsing.
        var regeneratedChoices = "CHOICE_A:Կանչենք թռչունիկին\nCHOICE_B:Լսենք զանգակի ձայնը";

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(weakPair, regeneratedChoices);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "tell me a story");

        // Two AI calls: the original story call + the regeneration call.
        await _aiClient.Received(2).GetCompletionAsync(
            Arg.Any<string>(), Arg.Any<List<(string, string)>>());

        // The story prose is preserved from the first call.
        Assert.Equal("Աղվեսը վազեց անտառով։", result.Response);

        // The weak pair must NOT reach the child; the regenerated pair does.
        Assert.Equal("Կանչենք թռչունիկին", result.ChoiceA);
        Assert.Equal("Լսենք զանգակի ձայնը", result.ChoiceB);
    }

    [Fact]
    public async Task ChoiceDiversityGuard_DifferentVerbs_DoesNotTriggerRegeneration()
    {
        // Control: genuinely different verbs must pass the guard and the
        // single AI call must be the only call made.
        var goodPair = "Աղվեսը վազեց։\n---\nCHOICE_A:Կանչենք թռչունիկին\nCHOICE_B:Լսենք զանգակի ձայնը";
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(goodPair);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "tell me a story");

        await _aiClient.Received(1).GetCompletionAsync(
            Arg.Any<string>(), Arg.Any<List<(string, string)>>());
        Assert.Equal("Կանչենք թռչունիկին", result.ChoiceA);
        Assert.Equal("Լսենք զանգակի ձայնը", result.ChoiceB);
    }
}

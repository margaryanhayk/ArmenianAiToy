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
/// Phase Cat-B: integration coverage for the runtime story-choice
/// coherence gate inside ChatService. Mirrors the existing
/// ChatServiceTailBlockTests setup so the boundary behavior is the only
/// new assertion. We pin two paths here:
///   1. Initial response has ungrounded choices → ChatService retries
///      (one extra LLM call, retry returns a coherent pair).
///   2. Initial AND retry responses both ungrounded → ChatService keeps
///      the prose and substitutes deterministic body-anchored choices.
/// </summary>
public class ChatServiceCoherenceTests
{
    private readonly IChatService _chatService;
    private readonly IAiChatClient _aiClient;
    private readonly IConversationService _conversations;
    private readonly IModerationService _moderation;

    private string? _storedAssistantContent;

    public ChatServiceCoherenceTests()
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
    public async Task UngroundedChoices_TriggerOneFullRetry_AndKeepRetryPair()
    {
        // First LLM response: body about a garden + bell, but choices
        // wander into a cave + dragon (Cat-B failure mode).
        var ungrounded =
            "Փոքրիկ սկյուռիկը կանգնեց պարտեզում։ "
          + "Զանգակը զանգում էր կամաց։\n"
          + "---\n"
          + "CHOICE_A:Մտնենք քարանձավը\n"
          + "CHOICE_B:Կանչենք վիշապին";

        // Retry response: same body, but choices now grounded in body
        // (sciuriid + bell). Gate should accept this pair.
        var groundedRetry =
            "Փոքրիկ սկյուռիկը կանգնեց պարտեզում։ "
          + "Զանգակը զանգում էր կամաց։\n"
          + "---\n"
          + "CHOICE_A:Մոտենանք սկյուռիկին\n"
          + "CHOICE_B:Լսենք զանգակը";

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(ungrounded, groundedRetry);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "tell me a story");

        // Two AI calls: original + the coherence-driven retry.
        await _aiClient.Received(2).GetCompletionAsync(
            Arg.Any<string>(), Arg.Any<List<(string, string)>>());

        // Retry's pair must reach the child.
        Assert.Equal("Մոտենանք սկյուռիկին", result.ChoiceA);
        Assert.Equal("Լսենք զանգակը", result.ChoiceB);

        // The ungrounded pair must NOT leak through.
        Assert.NotEqual("Մտնենք քարանձավը", result.ChoiceA);
        Assert.NotEqual("Կանչենք վիշապին", result.ChoiceB);
    }

    [Fact]
    public async Task UngroundedRetry_FallsBackToDeterministicAnchorRepair()
    {
        // Both initial and retry responses come back with ungrounded
        // choices. ChatService must keep the (last) body and replace
        // the labels with deterministic body-anchored repair.
        var bodyOne =
            "Փոքրիկ սկյուռիկը կանգնեց պարտեզում։ Զանգակը զանգում էր կամաց։";
        var firstUngrounded = bodyOne
          + "\n---\nCHOICE_A:Մտնենք քարանձավը\nCHOICE_B:Կանչենք վիշապին";

        var bodyTwo =
            "Թռչունիկը նստեց ճյուղին։ Քամին քաշեց տերևները։";
        var retryAlsoUngrounded = bodyTwo
          + "\n---\nCHOICE_A:Բացենք բանալին\nCHOICE_B:Մտնենք քարանձավը";

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(firstUngrounded, retryAlsoUngrounded);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "tell me a story");

        // Two AI calls — original + one retry. Pipeline does NOT fan
        // out into a third LLM call for repair.
        await _aiClient.Received(2).GetCompletionAsync(
            Arg.Any<string>(), Arg.Any<List<(string, string)>>());

        // The retry's body wins (most recent prose). Labels replaced by
        // deterministic repair built from THAT body.
        Assert.Equal(bodyTwo, result.Response);
        Assert.NotNull(result.ChoiceA);
        Assert.NotNull(result.ChoiceB);

        // Repair must NOT carry the ungrounded BAD-example nouns from
        // either rejected pair.
        Assert.DoesNotContain("քարանձավ", result.ChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("քարանձավ", result.ChoiceB!, StringComparison.Ordinal);
        Assert.DoesNotContain("վիշապ", result.ChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("վիշապ", result.ChoiceB!, StringComparison.Ordinal);
        Assert.DoesNotContain("բանալի", result.ChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("բանալի", result.ChoiceB!, StringComparison.Ordinal);

        // At least one repaired label must reference a noun from the
        // retry body so the child sees their world reflected.
        var refsBody =
               result.ChoiceA!.Contains("թռչունիկ", StringComparison.Ordinal)
            || result.ChoiceA!.Contains("ճյուղ", StringComparison.Ordinal)
            || result.ChoiceA!.Contains("քամի", StringComparison.Ordinal)
            || result.ChoiceA!.Contains("տերև", StringComparison.Ordinal)
            || result.ChoiceB!.Contains("թռչունիկ", StringComparison.Ordinal)
            || result.ChoiceB!.Contains("ճյուղ", StringComparison.Ordinal)
            || result.ChoiceB!.Contains("քամի", StringComparison.Ordinal)
            || result.ChoiceB!.Contains("տերև", StringComparison.Ordinal);
        Assert.True(refsBody,
            $"Repair must reference a body anchor. Got A=\"{result.ChoiceA}\" B=\"{result.ChoiceB}\"");
    }

    [Fact]
    public async Task LiveQA_BadButterflyPair_DoesNotReachUser_EvenWhenRetryRepeats()
    {
        // 2026-04-27 voice-MVP regression. The LLM produced a Story body
        // that's fine on its own but a choice pair that combines a
        // verb-derived pseudo-noun («հպենք») with a fabricated compound
        // («շատրվանաքար»). Even when the retry returns the same coined
        // pair, the deterministic final-coh repair must replace the
        // labels with body-anchored alternatives before the response
        // reaches the child.
        var body =
              "Հին ժամանակներում, մի փոքրիկ թիթեռ ապրում էր ծաղկավոր պարտեզում։ "
            + "Թիթեռը շատ էր սիրում պարել ծաղիկների տերևների վրա։ "
            + "Մի անգամ, երբ նա պտտվում էր, տեսավ մի առեղծվածային լուսավոր քար, "
            + "որը փայլում էր մեղմ լույսով և ծաղիկներին հատուկ հոտով։ "
            + "Թիթեռը մոտեցավ քարին և զարմացավ նրա գեղեցկությամբ։";
        var bad = body
            + "\n---\nCHOICE_A:Մոտենանք հպենքին\nCHOICE_B:Նայենք շատրվանաքարին";

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(bad, bad);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "tell me a story");

        // The exact bad strings must NOT reach the user.
        Assert.NotEqual("Մոտենանք հպենքին", result.ChoiceA);
        Assert.NotEqual("Նայենք շատրվանաքարին", result.ChoiceB);
        Assert.NotNull(result.ChoiceA);
        Assert.NotNull(result.ChoiceB);

        // Coined / fabricated tokens must not survive in either label.
        Assert.DoesNotContain("հպենք", result.ChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("հպենք", result.ChoiceB!, StringComparison.Ordinal);
        Assert.DoesNotContain("շատրվան", result.ChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("շատրվան", result.ChoiceB!, StringComparison.Ordinal);

        // Body verbs must not be selected as repair anchors.
        Assert.DoesNotContain("մոտեցավ", result.ChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("մոտեցավ", result.ChoiceB!, StringComparison.Ordinal);
        Assert.DoesNotContain("զարմացավ", result.ChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("զարմացավ", result.ChoiceB!, StringComparison.Ordinal);

        // The two delivered labels must differ.
        Assert.NotEqual(result.ChoiceA, result.ChoiceB);

        // At least one delivered label must reference a real body noun.
        var refsBody =
               result.ChoiceA!.Contains("թիթեռ", StringComparison.Ordinal)
            || result.ChoiceA!.Contains("քար", StringComparison.Ordinal)
            || result.ChoiceA!.Contains("ծաղիկ", StringComparison.Ordinal)
            || result.ChoiceA!.Contains("պարտեզ", StringComparison.Ordinal)
            || result.ChoiceB!.Contains("թիթեռ", StringComparison.Ordinal)
            || result.ChoiceB!.Contains("քար", StringComparison.Ordinal)
            || result.ChoiceB!.Contains("ծաղիկ", StringComparison.Ordinal)
            || result.ChoiceB!.Contains("պարտեզ", StringComparison.Ordinal);
        Assert.True(refsBody,
            $"Delivered labels must reference a body noun. Got A=\"{result.ChoiceA}\" B=\"{result.ChoiceB}\"");
    }

    [Fact]
    public async Task GroundedInitialPair_DoesNotTriggerExtraRetry()
    {
        // Sanity / no-regression: a grounded pair on the first call must
        // not provoke any extra LLM round-trip from the new gate.
        var ok =
            "Փոքրիկ նապաստակը նայեց տուփին։ Տուփի վրա փայլում էր մի փոքրիկ զանգակ։\n"
          + "---\n"
          + "CHOICE_A:Մոտենանք նապաստակին\n"
          + "CHOICE_B:Լսենք զանգակը";

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(ok);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "tell me a story");

        await _aiClient.Received(1).GetCompletionAsync(
            Arg.Any<string>(), Arg.Any<List<(string, string)>>());
        Assert.Equal("Մոտենանք նապաստակին", result.ChoiceA);
        Assert.Equal("Լսենք զանգակը", result.ChoiceB);
    }
}

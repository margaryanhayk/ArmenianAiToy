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
    public async Task LiveQA_AuxiliaryStemBadPair_DoesNotReachUser_EvenWhenRetryRepeats()
    {
        // 2026-04-28 voice-MVP follow-up regression. The LLM picked the
        // body's auxiliary «կարող» and verb-«to be» «լին» stems and
        // inflected them as Dative pseudo-nouns (Մոտենանք կարողին /
        // Նայենք լինին). Both initial and retry returned the same bad
        // pair. The pipeline must NOT deliver them to the child —
        // deterministic final-coh repair has to replace the labels with
        // body-anchored alternatives.
        var body =
              "Փոքրիկ սկյուռիկը վազեց ծառերի միջով։ "
            + "Ամեն կողմից լսվում էր քամու մեղմ շնչառությունը։ "
            + "Հանկարծ նա տեսավ մի փայլուն իր ճյուղերի մեջ։ "
            + "Սրտում ուրախություն զգալով, նա մոտեցավ։ "
            + "Ի՞նչ կարող է լինել այդտեղ։";
        var bad = body
            + "\n---\nCHOICE_A:Մոտենանք կարողին\nCHOICE_B:Նայենք լինին";

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(bad, bad);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "tell me a story");

        // The exact bad strings must NOT reach the user.
        Assert.NotEqual("Մոտենանք կարողին", result.ChoiceA);
        Assert.NotEqual("Նայենք լինին", result.ChoiceB);
        Assert.NotNull(result.ChoiceA);
        Assert.NotNull(result.ChoiceB);

        // Auxiliary / verb stems must not survive in either label.
        Assert.DoesNotContain("կարող", result.ChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("կարող", result.ChoiceB!, StringComparison.Ordinal);
        Assert.DoesNotContain("լին", result.ChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("լին", result.ChoiceB!, StringComparison.Ordinal);

        // Past-tense verb forms must also not appear (defense in depth
        // for the prior 6dae9a0 invariant).
        Assert.DoesNotContain("մոտեցավ", result.ChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("մոտեցավ", result.ChoiceB!, StringComparison.Ordinal);
        Assert.DoesNotContain("տեսավ", result.ChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("տեսավ", result.ChoiceB!, StringComparison.Ordinal);

        // The two delivered labels must differ.
        Assert.NotEqual(result.ChoiceA, result.ChoiceB);
    }

    [Fact]
    public async Task LiveQA_HetwBadPair_DoesNotReachUser_EvenWhenRetryRepeats()
    {
        // 2026-04-28 voice-MVP follow-up regression. Body contains the
        // postposition «հետ» ("with") and the LLM produced choiceA
        // «Մոտենանք հետևին» — coincidentally prefix-matching «հետ» but
        // semantically unrelated. Both initial and retry returned the
        // same coined pair. The pipeline must NOT deliver
        // «Մոտենանք հետևին» to the child — final-coh repair has to
        // replace it with body-anchored deterministic labels.
        var body =
              "Փոքրիկ նապաստակը ցատկեց քարի վրայից։ "
            + "Փափուկ խոտի վրա քամիի մեղմ ծեսը լսվում էր։ "
            + "Նապաստակը իր բույսերի ընկերների հետ խաղում էր, երբ տեսավ, "
            + "որ հողի վրա փայլում է մի փոքրիկ թանկարժեք քար։ "
            + "Նա զարմացած մոտեցավ, որպեսզի ավելի լավ տեսնի։ "
            + "Բայց այդ ժամանակ քարից դուրս թռավ մի արտասովոր փոքրիկ թիթեռ, "
            + "որ փայլում էր գունագեղ լույսերով։";
        var bad = body
            + "\n---\nCHOICE_A:Մոտենանք հետևին\nCHOICE_B:Նայենք թիթեռին";

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(bad, bad);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "tell me a story");

        // The exact bad string must NOT reach the user.
        Assert.NotEqual("Մոտենանք հետևին", result.ChoiceA);
        Assert.NotNull(result.ChoiceA);
        Assert.NotNull(result.ChoiceB);

        // «հետև» / «հետևին» must not survive in either label.
        Assert.DoesNotContain("հետև", result.ChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("հետև", result.ChoiceB!, StringComparison.Ordinal);

        // Defense in depth — none of the prior bad-anchor classes either.
        foreach (var bad2 in new[]
        {
            "հպենք", "շատրվանաքար", "մոտեցավ", "տեսավ", "զարմացավ",
            "կարող", "լին",
        })
        {
            Assert.DoesNotContain(bad2, result.ChoiceA!, StringComparison.Ordinal);
            Assert.DoesNotContain(bad2, result.ChoiceB!, StringComparison.Ordinal);
        }

        // The two delivered labels must differ.
        Assert.NotEqual(result.ChoiceA, result.ChoiceB);
    }

    [Fact]
    public async Task LiveQA_VerbAdjectiveStemBadPair_DoesNotReachUser_EvenWhenRetryRepeats()
    {
        // 2026-04-28 fourth follow-up regression. The LLM produced
        // «Մոտենանք վերցնին» (verb stem of «վերցնել», to take) and
        // «Նայենք փայլուին» (adjective stem of «փայլուն», shining)
        // — both Dative pseudo-nouns coerced from non-noun parts of
        // speech. Both initial and retry returned the same coined
        // pair. The pipeline must replace them with body-anchored
        // concrete-noun labels before the response reaches the child.
        var body =
              "Փոքրիկ սկյուռիկը վազում էր ճյուղի վրայով։ "
            + "Վար քամու մեղմ հովը փչում էր նրա քիչ մազերը։ "
            + "Նա ուրախ էր ու չէր շտապում ոչ մի տեղ։ "
            + "Փոթորկոտ խոտերի մեջ նա գտավ մի փայլուն փոքրիկ քար։ "
            + "Որոշեց իմանալ, թե ինչքան զարմանալի գաղտնիք ունի այդ քարը։";
        var bad = body
            + "\n---\nCHOICE_A:Մոտենանք վերցնին\nCHOICE_B:Նայենք փայլուին";

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(bad, bad);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "tell me a story");

        // The exact bad strings must NOT reach the user.
        Assert.NotEqual("Մոտենանք վերցնին", result.ChoiceA);
        Assert.NotEqual("Նայենք փայլուին", result.ChoiceB);
        Assert.NotNull(result.ChoiceA);
        Assert.NotNull(result.ChoiceB);

        // None of the verb / adjective / auxiliary stems may survive in
        // either delivered label.
        foreach (var bad2 in new[]
        {
            "վերցն", "որոշ", "իման", "շտապ", "փայլու",
            "հպենք", "շատրվանաքար", "մոտեցավ", "տեսավ", "զարմացավ",
            "կարող", "լին", "հետև",
        })
        {
            Assert.DoesNotContain(bad2, result.ChoiceA!, StringComparison.Ordinal);
            Assert.DoesNotContain(bad2, result.ChoiceB!, StringComparison.Ordinal);
        }

        // The two delivered labels must differ.
        Assert.NotEqual(result.ChoiceA, result.ChoiceB);

        // At least one delivered label must reference a concrete body
        // noun («սկյուռ», «ճյուղ», «քամ», «խոտ», «քար»).
        var refsBody =
               result.ChoiceA!.Contains("սկյուռ", StringComparison.Ordinal)
            || result.ChoiceA!.Contains("ճյուղ", StringComparison.Ordinal)
            || result.ChoiceA!.Contains("քամ", StringComparison.Ordinal)
            || result.ChoiceA!.Contains("խոտ", StringComparison.Ordinal)
            || result.ChoiceA!.Contains("քար", StringComparison.Ordinal)
            || result.ChoiceB!.Contains("սկյուռ", StringComparison.Ordinal)
            || result.ChoiceB!.Contains("ճյուղ", StringComparison.Ordinal)
            || result.ChoiceB!.Contains("քամ", StringComparison.Ordinal)
            || result.ChoiceB!.Contains("խոտ", StringComparison.Ordinal)
            || result.ChoiceB!.Contains("քար", StringComparison.Ordinal);
        Assert.True(refsBody,
            $"Delivered labels must reference a concrete body noun. Got A=\"{result.ChoiceA}\" B=\"{result.ChoiceB}\"");
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

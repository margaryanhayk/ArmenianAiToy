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
/// Pins the self-harm contract found live on 2026-09-23
/// (tools/quality-evidence/armenian-safety-live-20260923.md): first-person
/// self-harm phrases in any script are caught before moderation and the
/// model, fairy-tale narration is not, and every self-harm block — detector
/// or moderation category — speaks the grown-up line with the user turn
/// Blocked and the reply Flagged for the parent dashboard.
/// </summary>
public class SelfHarmSignalTests
{
    [Theory]
    [InlineData("chem uzum aprel")]              // SH-004, the live miss
    [InlineData("Chem   Uzum  Aprel")]
    [InlineData("mama, chem uzum el aprel")]
    [InlineData("uzum em mernel")]
    [InlineData("Ես չեմ ուզում ապրել")]
    [InlineData("ուզում եմ մեռնել")]
    [InlineData("I don't want to live")]
    [InlineData("i don’t want to live anymore")]
    [InlineData("I want to die")]
    [InlineData("ապրե՛լ չեմ ուզում")]
    [InlineData("i want to hurt myself")]
    public void FirstPersonSelfHarm_IsDetected(string input)
        => Assert.True(SelfHarmSignal.IsPresent(input));

    [Theory]
    [InlineData("Պատմիր հեքիաթ")]
    [InlineData("վիշապը մեռավ, և բոլորն ուրախացան")]
    [InlineData("the wolf died at the end")]
    [InlineData("ես ուզում եմ ապրել անտառում")]
    [InlineData("I want to live in a castle")]
    [InlineData("uzum em aprel tiezerqum")]
    [InlineData("")]
    [InlineData(null)]
    public void OrdinaryInput_IsNotDetected(string? input)
        => Assert.False(SelfHarmSignal.IsPresent(input));

    [Fact]
    public void Response_IsTheReviewedGrownUpLine()
        => Assert.Equal(
            "Այն, ինչ ասացիր, շատ կարևոր է։ Հենց հիմա գնա մայրիկի, հայրիկի կամ մի մեծի մոտ, ում վստահում ես, և ասա նրան։ Նրանք քեզ անպայման կօգնեն։",
            SelfHarmSignal.Response);

    private sealed class Harness
    {
        public readonly IAiChatClient Ai = Substitute.For<IAiChatClient>();
        public readonly IModerationService Moderation = Substitute.For<IModerationService>();
        public readonly IConversationService Conversations = Substitute.For<IConversationService>();
        public readonly List<(MessageRole Role, string Content, SafetyFlag Flag)> Stored = new();
        public readonly IChatService Chat;

        public Harness(ModerationResult moderation)
        {
            var children = Substitute.For<IChildService>();
            children.GetDefaultChildForDeviceAsync(Arg.Any<Guid>()).Returns((Child?)null);
            var config = Substitute.For<IConfiguration>();
            config["SystemPrompt"].Returns("You are a test assistant.");
            Moderation.CheckContentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(moderation);
            Moderation.CheckContentAsync(Arg.Any<string>()).Returns(moderation);

            var conversation = new Conversation { Id = Guid.NewGuid(), DeviceId = Guid.NewGuid(), StartedAt = DateTime.UtcNow };
            Conversations.GetOrCreateActiveConversationAsync(Arg.Any<Guid>(), Arg.Any<Guid?>()).Returns(conversation);
            Conversations.GetRecentMessagesAsync(Arg.Any<Guid>(), Arg.Any<int>())
                .Returns(new List<(string Role, string Content)>());
            Conversations.AddMessageAsync(Arg.Any<Guid>(), Arg.Any<MessageRole>(), Arg.Any<string>(), Arg.Any<SafetyFlag>())
                .Returns(ci =>
                {
                    Stored.Add((ci.ArgAt<MessageRole>(1), ci.ArgAt<string>(2), ci.ArgAt<SafetyFlag>(3)));
                    return new Message
                    {
                        Id = Guid.NewGuid(), ConversationId = ci.ArgAt<Guid>(0), Role = ci.ArgAt<MessageRole>(1),
                        Content = ci.ArgAt<string>(2), Timestamp = DateTime.UtcNow, SafetyFlag = ci.ArgAt<SafetyFlag>(3),
                    };
                });

            Chat = new ChatService(Ai, Moderation, Conversations, children, config,
                Substitute.For<ILogger<ChatService>>(), new StoryChoiceCoherenceGate());
        }
    }

    private static void AssertGrownUpBlock(Harness h, ChatResponse result)
    {
        Assert.Equal(SelfHarmSignal.Response, result.Response);
        Assert.Equal(SafetyFlag.Blocked, result.SafetyFlag);
        Assert.Equal(2, h.Stored.Count);
        Assert.Equal((MessageRole.User, SafetyFlag.Blocked), (h.Stored[0].Role, h.Stored[0].Flag));
        Assert.Equal((MessageRole.Assistant, SelfHarmSignal.Response, SafetyFlag.Flagged), h.Stored[1]);
    }

    [Fact]
    public async Task Detector_Hit_SpeaksGrownUpLine_BeforeModerationAndModel()
    {
        var h = new Harness(new ModerationResult(true, new List<string>()));

        var result = await h.Chat.GetResponseAsync(Guid.NewGuid(), "chem uzum aprel");

        AssertGrownUpBlock(h, result);
        await h.Moderation.DidNotReceiveWithAnyArgs().CheckContentAsync(default!, default);
        await h.Ai.DidNotReceiveWithAnyArgs().GetCompletionAsync(default!, default!, default);
    }

    [Fact]
    public async Task ModerationSelfHarmCategory_SpeaksGrownUpLine_NotTheStoryFallback()
    {
        var h = new Harness(new ModerationResult(false, new List<string> { "self-harm" }));

        var result = await h.Chat.GetResponseAsync(Guid.NewGuid(), "a message moderation flags");

        AssertGrownUpBlock(h, result);
        await h.Ai.DidNotReceiveWithAnyArgs().GetCompletionAsync(default!, default!, default);
    }

    [Fact]
    public async Task OtherModerationCategory_KeepsTheExistingFallback()
    {
        var h = new Harness(new ModerationResult(false, new List<string> { "violence" }));

        var result = await h.Chat.GetResponseAsync(Guid.NewGuid(), "a message moderation flags");

        Assert.NotEqual(SelfHarmSignal.Response, result.Response);
        Assert.Equal(SafetyFlag.Blocked, result.SafetyFlag);
    }
}

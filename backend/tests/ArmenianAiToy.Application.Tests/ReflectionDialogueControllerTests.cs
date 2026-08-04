using System.Text;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Reflection-DIALOGUE contract on the reflection-answer endpoint: when the
/// dialogue service is wired and the config gate is on, a valid + safe model
/// reaction replaces the deterministic acknowledgement; every doubt path
/// (unsafe input, unsafe output, gate off) falls back and the child never
/// hears unvalidated model text. Complements StoryQaControllerReflectionTests,
/// which pins the no-service (legacy deterministic) behavior.
/// </summary>
public class ReflectionDialogueControllerTests
{
    private const string StoryId = InMemoryCuratedStoryLibrary.LittleCloudId;
    private const string CloseLine = "Հիմա հանգստացի՛ր, փոքրիկ ընկեր։";
    private const string ValidReaction = "Ի՜նչ ջերմ պատասխան էր։ Օգնելը իսկապես ուրախացնում է։";

    private static readonly byte[] InboundWav = Encoding.UTF8.GetBytes("RIFF child answer marker");
    private static readonly byte[] TtsMp3 = Encoding.UTF8.GetBytes("ID3 synthesized marker");

    private sealed record Harness(
        StoryQaController Controller,
        IAudioTranscriptionService Transcription,
        IAudioSynthesisService Synthesis,
        IModerationService Moderation,
        IAiChatClient DialogueAi,
        IConversationService Conversations);

    private static Harness Create(bool aiRepliesEnabled = true)
    {
        var transcription = Substitute.For<IAudioTranscriptionService>();
        var synthesis = Substitute.For<IAudioSynthesisService>();
        var moderation = Substitute.For<IModerationService>();
        var qaAi = Substitute.For<IAiChatClient>();
        var dialogueAi = Substitute.For<IAiChatClient>();
        var conversations = Substitute.For<IConversationService>();
        var childService = Substitute.For<IChildService>();
        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>()).Returns((Child?)null);
        var deviceService = Substitute.For<IDeviceService>();
        deviceService.HasLinkedParentAsync(Arg.Any<Guid>()).Returns(true);
        var library = new InMemoryCuratedStoryLibrary();
        var env = Substitute.For<IWebHostEnvironment>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["StoryQa:ReflectionAiReplies"] = aiRepliesEnabled ? "true" : "false",
            }).Build();

        synthesis.SynthesizeArmenianAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AudioSynthesisResult(TtsMp3, "audio/mpeg"));
        deviceService.IsDevicePausedAsync(Arg.Any<Guid>()).Returns(false);
        deviceService.IsDeviceInBedtimeWindowAsync(Arg.Any<Guid>(), Arg.Any<DateTime>()).Returns(false);
        deviceService.IsModeEnabledForRequestAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), DetectedMode.Story)
            .Returns(true);

        var deviceId = Guid.NewGuid();
        conversations.GetOrCreateActiveConversationAsync(Arg.Any<Guid>(), Arg.Any<Guid?>())
            .Returns(new Conversation { Id = Guid.NewGuid(), DeviceId = deviceId, StartedAt = DateTime.UtcNow });
        conversations.AddMessageAsync(
                Arg.Any<Guid>(), Arg.Any<MessageRole>(), Arg.Any<string>(), Arg.Any<SafetyFlag>())
            .Returns(ci => new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = ci.ArgAt<Guid>(0),
                Role = ci.ArgAt<MessageRole>(1),
                Content = ci.ArgAt<string>(2),
                SafetyFlag = ci.ArgAt<SafetyFlag>(3),
                Timestamp = DateTime.UtcNow
            });

        var controller = new StoryQaController(
            transcription, synthesis, library, new LibraryStoryQuestionService(qaAi),
            moderation, conversations, childService, deviceService,
            new CannedVoiceClips(synthesis), new OpenAICostMeter(),
            Options.Create(new OpenAIDailyCostCapOptions { Enabled = false }),
            env, config, Substitute.For<ILogger<StoryQaController>>(),
            new ReflectionDialogueService(dialogueAi));

        var httpContext = new DefaultHttpContext();
        httpContext.Items["DeviceId"] = deviceId;
        httpContext.Request.Body = new MemoryStream(InboundWav);
        httpContext.Request.ContentType = "audio/wav";
        httpContext.Request.ContentLength = InboundWav.Length;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return new Harness(controller, transcription, synthesis, moderation, dialogueAi, conversations);
    }

    private static void WireTranscript(Harness h, string transcript) =>
        h.Transcription.TranscribeArmenianAsync(
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(transcript);

    private static void WireModeration(Harness h, bool inputSafe, bool reactionSafe)
    {
        h.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(ci =>
            {
                var text = ci.ArgAt<string>(0);
                var safe = text == ValidReaction ? reactionSafe : inputSafe;
                return Task.FromResult(new ModerationResult(safe, new List<string>()));
            });
    }

    // KEYSTONE: valid + output-safe model reaction is what the child hears
    // (its TTS is requested), and the persisted assistant text carries the
    // reaction + the authored conclusion + the close.
    [Fact]
    public async Task ValidSafeReaction_IsSpokenAndPersisted()
    {
        var h = Create();
        WireTranscript(h, "օգնում եմ մայրիկիս");
        WireModeration(h, inputSafe: true, reactionSafe: true);
        h.DialogueAi.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(ValidReaction);

        var result = await h.Controller.AnswerReflection(StoryId, questionIndex: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        await h.Synthesis.Received().SynthesizeArmenianAsync(ValidReaction, Arg.Any<CancellationToken>());
        await h.Conversations.Received(1).AddMessageAsync(
            Arg.Any<Guid>(), MessageRole.Assistant,
            Arg.Is<string>(t => t.StartsWith(ValidReaction) && t.EndsWith(CloseLine)),
            SafetyFlag.Clean);
    }

    // KEYSTONE: an output-moderation-unsafe reaction NEVER reaches TTS —
    // the deterministic acknowledgement is spoken instead.
    [Fact]
    public async Task UnsafeReaction_FallsBackToDeterministicAck()
    {
        var h = Create();
        WireTranscript(h, "պատասխան");
        WireModeration(h, inputSafe: true, reactionSafe: false);
        h.DialogueAi.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(ValidReaction);

        var result = await h.Controller.AnswerReflection(StoryId, questionIndex: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        await h.Synthesis.DidNotReceive().SynthesizeArmenianAsync(ValidReaction, Arg.Any<CancellationToken>());
        await h.Conversations.Received(1).AddMessageAsync(
            Arg.Any<Guid>(), MessageRole.Assistant,
            Arg.Is<string>(t => !t.Contains(ValidReaction)),
            SafetyFlag.Clean);
    }

    // KEYSTONE: a moderation-blocked child answer never reaches the model.
    [Fact]
    public async Task BlockedChildAnswer_NeverCallsModel()
    {
        var h = Create();
        WireTranscript(h, "վատ բովանդակություն");
        WireModeration(h, inputSafe: false, reactionSafe: true);

        var result = await h.Controller.AnswerReflection(StoryId, questionIndex: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        await h.DialogueAi.DidNotReceiveWithAnyArgs()
            .GetCompletionAsync(default!, default!);
    }

    // Config kill-switch restores the pre-dialogue deterministic behavior.
    [Fact]
    public async Task GateOff_NeverCallsModel()
    {
        var h = Create(aiRepliesEnabled: false);
        WireTranscript(h, "պատասխան");
        WireModeration(h, inputSafe: true, reactionSafe: true);

        var result = await h.Controller.AnswerReflection(StoryId, questionIndex: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        await h.DialogueAi.DidNotReceiveWithAnyArgs()
            .GetCompletionAsync(default!, default!);
    }

    // Story-QA/reflection turns must carry the "story" mode stamp, or the
    // dashboard's Stories tab shows nothing for a child who only used the
    // story flow (owner-reported bug 2026-08-03).
    [Fact]
    public async Task PersistedTurn_IsStampedWithStoryMode()
    {
        var h = Create();
        WireTranscript(h, "պատասխան");
        WireModeration(h, inputSafe: true, reactionSafe: true);
        h.DialogueAi.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(ValidReaction);

        await h.Controller.AnswerReflection(StoryId, questionIndex: 0, CancellationToken.None);

        await h.Conversations.Received(1).StampMessageModeAsync(Arg.Any<Guid>(), "story");
    }

    // `last=false` (multi-question loop, non-final question): the fixed
    // goodbye is NOT spoken/persisted — it belongs to the final question.
    [Fact]
    public async Task NonFinalQuestion_OmitsClose()
    {
        var h = Create();
        WireTranscript(h, "պատասխան");
        WireModeration(h, inputSafe: true, reactionSafe: true);
        h.DialogueAi.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(ValidReaction);
        h.Controller.ControllerContext.HttpContext.Request.QueryString = new QueryString("?last=false");

        var result = await h.Controller.AnswerReflection(StoryId, questionIndex: 1, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        await h.Conversations.Received(1).AddMessageAsync(
            Arg.Any<Guid>(), MessageRole.Assistant,
            Arg.Is<string>(t => !t.Contains(CloseLine)),
            SafetyFlag.Clean);
    }
}

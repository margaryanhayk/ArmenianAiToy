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
/// Contract for the post-story reflection answer endpoint
/// (<c>POST /api/chat/story-qa/reflection-answer</c>). After the story +
/// conclusion + reflection question play, the child speaks an answer; Areg
/// replies with a warm, rotated, DETERMINISTIC acknowledgement + a fixed close
/// — affirming engagement, never grading. No GPT answer model is consulted on
/// this path. The transcript is moderated + persisted for the safety record,
/// but the spoken reply does not depend on its content.
/// <para>
/// No real network: transcription / synthesis / moderation are substituted.
/// The acknowledgement + close TTS clips are cached in static fields and the
/// rotation counter is static, so these tests assert behavioral outcomes
/// (200 audio, composition shape, persistence, gate short-circuits) rather
/// than a specific rotation index or a cache-warmth-dependent synthesis call.
/// </para>
/// </summary>
public class StoryQaControllerReflectionTests
{
    private const string StoryId = InMemoryCuratedStoryLibrary.LittleCloudId;

    // The fixed gentle close (must match StoryQaController.ReflectionClose,
    // which is private — pinning the literal here also guards against silent
    // edits to child-facing text).
    private const string CloseLine = "Հիմա հանգստացի՛ր, փոքրիկ ընկեր։";

    private static readonly byte[] InboundWav = Encoding.UTF8.GetBytes("RIFF child answer marker");
    private static readonly byte[] TtsMp3 = Encoding.UTF8.GetBytes("ID3 synthesized marker");

    private sealed record Harness(
        StoryQaController Controller,
        IAudioTranscriptionService Transcription,
        IAudioSynthesisService Synthesis,
        IModerationService Moderation,
        IAiChatClient AiChatClient,
        IConversationService Conversations,
        IChildService ChildService,
        IDeviceService DeviceService,
        OpenAICostMeter CostMeter,
        Guid DeviceId,
        Guid ConversationId);

    private static Harness Create(OpenAIDailyCostCapOptions? costCap = null)
    {
        var transcription = Substitute.For<IAudioTranscriptionService>();
        var synthesis = Substitute.For<IAudioSynthesisService>();
        var moderation = Substitute.For<IModerationService>();
        var aiChatClient = Substitute.For<IAiChatClient>();
        var conversations = Substitute.For<IConversationService>();
        var childService = Substitute.For<IChildService>();
        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>()).Returns((Child?)null);
        var deviceService = Substitute.For<IDeviceService>();
        var library = new InMemoryCuratedStoryLibrary();
        var questions = new LibraryStoryQuestionService(aiChatClient);
        var env = Substitute.For<IWebHostEnvironment>();
        var config = Substitute.For<IConfiguration>();
        var logger = Substitute.For<ILogger<StoryQaController>>();

        synthesis.SynthesizeArmenianAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AudioSynthesisResult(TtsMp3, "audio/mpeg"));

        deviceService.IsDevicePausedAsync(Arg.Any<Guid>()).Returns(false);
        deviceService.IsDeviceInBedtimeWindowAsync(Arg.Any<Guid>(), Arg.Any<DateTime>()).Returns(false);
        deviceService.IsModeEnabledForRequestAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), DetectedMode.Story)
            .Returns(true);

        var canned = new CannedVoiceClips(synthesis);
        var costMeter = new OpenAICostMeter();
        var costCapOptions = Options.Create(
            costCap ?? new OpenAIDailyCostCapOptions { Enabled = false });

        var deviceId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        conversations.GetOrCreateActiveConversationAsync(Arg.Any<Guid>(), Arg.Any<Guid?>())
            .Returns(new Conversation
            {
                Id = conversationId,
                DeviceId = deviceId,
                StartedAt = DateTime.UtcNow
            });
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
            transcription, synthesis, library, questions, moderation,
            conversations, childService, deviceService, canned, costMeter, costCapOptions,
            env, config, logger);

        var httpContext = new DefaultHttpContext();
        httpContext.Items["DeviceId"] = deviceId;
        httpContext.Request.Body = new MemoryStream(InboundWav);
        httpContext.Request.ContentType = "audio/wav";
        httpContext.Request.ContentLength = InboundWav.Length;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return new Harness(
            controller, transcription, synthesis, moderation, aiChatClient,
            conversations, childService, deviceService, costMeter, deviceId, conversationId);
    }

    private static void WireTranscript(Harness h, string transcript) =>
        h.Transcription.TranscribeArmenianAsync(
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(transcript);

    private static void WireSafe(Harness h) =>
        h.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(IsSafe: true, FlaggedCategories: new List<string>()));

    // --- Happy path: warm ack + close, no GPT, turn persisted ----------

    [Fact]
    public async Task SafeAnswer_Returns200_AckThenPauseThenClose_NoGpt()
    {
        var h = Create();
        WireTranscript(h, "ոսկի");
        WireSafe(h);

        var result = await h.Controller.AnswerReflection(StoryId, questionIndex: 0, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("audio/mpeg", file.ContentType);

        // The affirming path is ack + pause + close: the body starts with the
        // (cached) ack clip and is strictly longer than a single clip — proving
        // the close (and a pause) follow the acknowledgement.
        Assert.True(file.FileContents.AsSpan().StartsWith(TtsMp3), "ack audio must be first");
        Assert.True(file.FileContents.Length > TtsMp3.Length, "pause + close must follow the ack");

        // No GPT answer model is ever consulted on this path.
        await h.AiChatClient.DidNotReceiveWithAnyArgs().GetCompletionAsync(default!, default!);
    }

    [Fact]
    public async Task SafeAnswer_PersistsChildAnswer_AndSpokenReply_Clean()
    {
        var h = Create();
        const string Answer = "ոսկի";
        WireTranscript(h, Answer);
        WireSafe(h);

        await h.Controller.AnswerReflection(StoryId, questionIndex: 0, CancellationToken.None);

        await h.Conversations.Received(1).GetOrCreateActiveConversationAsync(h.DeviceId, null);
        // Child answer stored verbatim as a Clean User row.
        await h.Conversations.Received(1).AddMessageAsync(
            h.ConversationId, MessageRole.User, Answer, SafetyFlag.Clean);
        // Assistant row is the spoken reply: <rotated ack> + " " + close.
        // Rotation is static/non-deterministic, so assert it ends with the
        // fixed close and is longer than the close alone (an ack was prepended).
        await h.Conversations.Received(1).AddMessageAsync(
            h.ConversationId, MessageRole.Assistant,
            Arg.Is<string>(s => s.EndsWith(CloseLine) && s.Length > CloseLine.Length),
            SafetyFlag.Clean);
    }

    [Fact]
    public async Task Transcription_IsBiasedWithTheReflectionQuestion()
    {
        var h = Create();
        WireTranscript(h, "ոսկի");
        WireSafe(h);
        var question = new InMemoryCuratedStoryLibrary().GetById(StoryId)!.ReflectionQuestions[0];

        await h.Controller.AnswerReflection(StoryId, questionIndex: 0, CancellationToken.None);

        await h.Transcription.Received(1).TranscribeArmenianAsync(
            Arg.Any<Stream>(), Arg.Any<string>(),
            question, Arg.Any<CancellationToken>(), null);
    }

    // --- Blocked answer: close only, never an affirmation --------------

    [Fact]
    public async Task BlockedAnswer_SpeaksCloseOnly_PersistsBlockedAndFlagged()
    {
        var h = Create();
        const string Answer = "ինչ-որ վտանգավոր բան";
        WireTranscript(h, Answer);
        h.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(IsSafe: false, FlaggedCategories: new List<string> { "violence" }));

        var result = await h.Controller.AnswerReflection(StoryId, questionIndex: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        // The child row is Blocked; the assistant reply is the close ALONE
        // (no affirming line over flagged content), stored Flagged.
        await h.Conversations.Received(1).AddMessageAsync(
            h.ConversationId, MessageRole.User, Answer, SafetyFlag.Blocked);
        await h.Conversations.Received(1).AddMessageAsync(
            h.ConversationId, MessageRole.Assistant, CloseLine, SafetyFlag.Flagged);
    }

    [Fact]
    public async Task EmptyTranscript_PersistsNothing_SkipsModeration_StillReturnsAudio()
    {
        var h = Create();
        WireTranscript(h, "   ");

        var result = await h.Controller.AnswerReflection(StoryId, questionIndex: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        await h.Moderation.DidNotReceiveWithAnyArgs().CheckContentAsync(default!);
        await h.Conversations.DidNotReceiveWithAnyArgs()
            .AddMessageAsync(default, default, default!, default);
    }

    // --- 404s: unknown story / bad question index (no existence leak) --

    [Fact]
    public async Task UnknownStory_Returns404_NoStt()
    {
        var h = Create();
        WireTranscript(h, "ոսկի");

        var result = await h.Controller.AnswerReflection("no-such-story", questionIndex: 0, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        await h.Transcription.DidNotReceiveWithAnyArgs()
            .TranscribeArmenianAsync(default!, default!, default, default, default);
    }

    [Fact]
    public async Task BadQuestionIndex_Returns404_NoStt()
    {
        var h = Create();
        WireTranscript(h, "ոսկի");

        var result = await h.Controller.AnswerReflection(StoryId, questionIndex: 999, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        await h.Transcription.DidNotReceiveWithAnyArgs()
            .TranscribeArmenianAsync(default!, default!, default, default, default);
    }

    // --- Gates + cost cap: short-circuit before any STT ----------------

    [Fact]
    public async Task PausedDevice_ReturnsCannedClip_NoStt()
    {
        var h = Create();
        h.DeviceService.IsDevicePausedAsync(Arg.Any<Guid>()).Returns(true);

        var result = await h.Controller.AnswerReflection(StoryId, questionIndex: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        await h.Transcription.DidNotReceiveWithAnyArgs()
            .TranscribeArmenianAsync(default!, default!, default, default, default);
    }

    [Fact]
    public async Task BedtimeWindow_ReturnsCannedClip_NoStt()
    {
        var h = Create();
        h.DeviceService.IsDeviceInBedtimeWindowAsync(Arg.Any<Guid>(), Arg.Any<DateTime>()).Returns(true);

        var result = await h.Controller.AnswerReflection(StoryId, questionIndex: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        await h.Transcription.DidNotReceiveWithAnyArgs()
            .TranscribeArmenianAsync(default!, default!, default, default, default);
    }

    [Fact]
    public async Task StoryModeDisabled_ReturnsCannedClip_NoStt()
    {
        var h = Create();
        h.DeviceService.IsModeEnabledForRequestAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), DetectedMode.Story)
            .Returns(false);

        var result = await h.Controller.AnswerReflection(StoryId, questionIndex: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        await h.Transcription.DidNotReceiveWithAnyArgs()
            .TranscribeArmenianAsync(default!, default!, default, default, default);
    }

    [Fact]
    public async Task OverDailyCostCap_ReturnsCannedClip_NoStt()
    {
        var h = Create(new OpenAIDailyCostCapOptions { Enabled = true });
        h.CostMeter.Record(h.DeviceId, 999m, DateTime.UtcNow);

        var result = await h.Controller.AnswerReflection(StoryId, questionIndex: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        await h.Transcription.DidNotReceiveWithAnyArgs()
            .TranscribeArmenianAsync(default!, default!, default, default, default);
    }

    // --- Transient STT failure -> spoken fallback, never a silent 502 --

    [Fact]
    public async Task SttFailure_ReturnsSpokenFallbackAudio_Not502()
    {
        var h = Create();
        h.Transcription.TranscribeArmenianAsync(
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns<string>(_ => throw new InvalidOperationException("whisper down"));

        var result = await h.Controller.AnswerReflection(StoryId, questionIndex: 0, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("audio/mpeg", file.ContentType);
        await h.Conversations.DidNotReceiveWithAnyArgs()
            .AddMessageAsync(default, default, default!, default);
    }
}

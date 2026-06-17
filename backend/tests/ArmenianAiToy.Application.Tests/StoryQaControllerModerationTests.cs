using System.Text;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Input-moderation contract for the voice in-story Q&amp;A endpoint
/// (<c>POST /api/chat/story-qa</c>). The transcribed child question is
/// moderated BEFORE any GPT answer call — mirroring the text path
/// (<c>ChatService</c> step 2). An unsafe transcript is never sent to
/// the answer model; the child still hears the warm in-story fallback
/// (and the story resumes), never a 502 / silence.
/// <para>
/// No real network: transcription + synthesis + moderation are
/// substituted, and the answer model (<see cref="IAiChatClient"/>) is a
/// substitute we assert was — or was not — called. The Q&amp;A path does
/// not touch the database, so no SQLite harness is needed.
/// </para>
/// </summary>
public class StoryQaControllerModerationTests
{
    private const string StoryId = InMemoryCuratedStoryLibrary.LittleCloudId;
    private static readonly byte[] InboundWav = Encoding.UTF8.GetBytes("RIFF child question marker");
    private static readonly byte[] TtsMp3 = Encoding.UTF8.GetBytes("ID3 synthesized marker");

    private sealed record Harness(
        StoryQaController Controller,
        IAudioTranscriptionService Transcription,
        IAudioSynthesisService Synthesis,
        IModerationService Moderation,
        IAiChatClient AiChatClient,
        IConversationService Conversations,
        Guid DeviceId,
        Guid ConversationId);

    private static Harness Create()
    {
        var transcription = Substitute.For<IAudioTranscriptionService>();
        var synthesis = Substitute.For<IAudioSynthesisService>();
        var moderation = Substitute.For<IModerationService>();
        var aiChatClient = Substitute.For<IAiChatClient>();
        var conversations = Substitute.For<IConversationService>();
        var library = new InMemoryCuratedStoryLibrary();
        // Real question service over the substituted answer model, so we
        // can assert whether the GPT call happened from the controller's
        // moderation decision (not a mock of the service itself).
        var questions = new LibraryStoryQuestionService(aiChatClient);
        var env = Substitute.For<IWebHostEnvironment>();
        var config = Substitute.For<IConfiguration>();
        var logger = Substitute.For<ILogger<StoryQaController>>();

        // Synthesis returns canned MP3 bytes for ANY text (answer, bridge,
        // recap) so composition succeeds without inspecting content.
        synthesis.SynthesizeArmenianAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AudioSynthesisResult(TtsMp3, "audio/mpeg"));

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
            conversations, env, config, logger);

        var httpContext = new DefaultHttpContext();
        httpContext.Items["DeviceId"] = deviceId;
        httpContext.Request.Body = new MemoryStream(InboundWav);
        httpContext.Request.ContentType = "audio/wav";
        httpContext.Request.ContentLength = InboundWav.Length;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return new Harness(
            controller, transcription, synthesis, moderation, aiChatClient,
            conversations, deviceId, conversationId);
    }

    private static void WireTranscript(Harness h, string transcript) =>
        h.Transcription.TranscribeArmenianAsync(
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(transcript);

    // --- Unsafe transcript: gated before GPT, child hears fallback ----

    [Fact]
    public async Task UnsafeTranscript_NeverCallsGpt_SpeaksInStoryFallback_Returns200()
    {
        var h = Create();
        WireTranscript(h, "ինչ-որ վտանգավոր բան");
        h.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(IsSafe: false, FlaggedCategories: new List<string> { "violence" }));

        var result = await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        // 200 audio (no 502, no silence).
        Assert.IsType<FileContentResult>(result);

        // Moderation ran; GPT answer model NEVER called.
        await h.Moderation.Received(1).CheckContentAsync(Arg.Any<string>());
        await h.AiChatClient.DidNotReceiveWithAnyArgs()
            .GetCompletionAsync(default!, default!);

        // The spoken answer portion is the in-story safe fallback.
        await h.Synthesis.Received().SynthesizeArmenianAsync(
            StoryAnswerFilter.SafeFallback, Arg.Any<CancellationToken>());
    }

    // --- Fail-closed: moderation_unavailable is treated as unsafe -----

    [Fact]
    public async Task ModerationUnavailable_TreatedAsUnsafe_NeverCallsGpt()
    {
        var h = Create();
        WireTranscript(h, "բոլորովին անվնաս հարց");
        h.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(
                IsSafe: false, FlaggedCategories: new List<string> { "moderation_unavailable" }));

        var result = await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        await h.AiChatClient.DidNotReceiveWithAnyArgs()
            .GetCompletionAsync(default!, default!);
        await h.Synthesis.Received().SynthesizeArmenianAsync(
            StoryAnswerFilter.SafeFallback, Arg.Any<CancellationToken>());
    }

    // --- Safe transcript: GPT answer path proceeds as before ----------

    [Fact]
    public async Task SafeTranscript_ProceedsToGpt()
    {
        var h = Create();
        WireTranscript(h, "Ո՞վ է փոքրիկ ամպիկը");
        h.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(IsSafe: true, FlaggedCategories: new List<string>()));
        // The answer model returns something; whether it passes the answer
        // filter is irrelevant here — we only assert the GPT path was entered.
        h.AiChatClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Փոքրիկ ամպիկը երկնքի ընկերն է։");

        var result = await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        await h.Moderation.Received(1).CheckContentAsync(Arg.Any<string>());
        await h.AiChatClient.ReceivedWithAnyArgs()
            .GetCompletionAsync(default!, default!);
    }

    // --- Empty transcript: unchanged; moderation not consulted --------

    [Fact]
    public async Task EmptyTranscript_SkipsModerationAndGpt_SpeaksFallback()
    {
        var h = Create();
        WireTranscript(h, "   ");
        h.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(IsSafe: true, FlaggedCategories: new List<string>()));

        var result = await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        // No text to check → moderation not consulted, GPT not called.
        await h.Moderation.DidNotReceiveWithAnyArgs().CheckContentAsync(default!);
        await h.AiChatClient.DidNotReceiveWithAnyArgs()
            .GetCompletionAsync(default!, default!);
    }

    // --- Gap 3: turn persistence (dashboard + retention) --------------

    [Fact]
    public async Task SafeTranscript_PersistsUserAndAssistantMessages_Clean()
    {
        var h = Create();
        const string Question = "Ո՞վ է փոքրիկ ամպիկը";
        WireTranscript(h, Question);
        h.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(IsSafe: true, FlaggedCategories: new List<string>()));
        h.AiChatClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Փոքրիկ ամպիկը երկնքի ընկերն է։");

        await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        await h.Conversations.Received(1)
            .GetOrCreateActiveConversationAsync(h.DeviceId, null);
        // Child question stored verbatim as a Clean User row.
        await h.Conversations.Received(1).AddMessageAsync(
            h.ConversationId, MessageRole.User, Question, SafetyFlag.Clean);
        // Assistant answer stored as a Clean Assistant row.
        await h.Conversations.Received(1).AddMessageAsync(
            h.ConversationId, MessageRole.Assistant, Arg.Any<string>(), SafetyFlag.Clean);
    }

    [Fact]
    public async Task UnsafeTranscript_PersistsBlockedUser_AndFlaggedFallbackAssistant()
    {
        var h = Create();
        const string Question = "ինչ-որ վտանգավոր բան";
        WireTranscript(h, Question);
        h.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(IsSafe: false, FlaggedCategories: new List<string> { "violence" }));

        await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        // Blocked child row + Flagged assistant fallback row — mirrors the
        // text path so the parent dashboard can distinguish this turn.
        await h.Conversations.Received(1).AddMessageAsync(
            h.ConversationId, MessageRole.User, Question, SafetyFlag.Blocked);
        await h.Conversations.Received(1).AddMessageAsync(
            h.ConversationId, MessageRole.Assistant,
            StoryAnswerFilter.SafeFallback, SafetyFlag.Flagged);
    }

    [Fact]
    public async Task EmptyTranscript_PersistsNothing()
    {
        var h = Create();
        WireTranscript(h, "   ");

        await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        await h.Conversations.DidNotReceiveWithAnyArgs()
            .GetOrCreateActiveConversationAsync(default, default);
        await h.Conversations.DidNotReceiveWithAnyArgs()
            .AddMessageAsync(default, default, default!, default);
    }

    [Fact]
    public async Task PersistenceFailure_IsNonFatal_StillReturnsAudio()
    {
        var h = Create();
        WireTranscript(h, "Ո՞վ է փոքրիկ ամպիկը");
        h.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(IsSafe: true, FlaggedCategories: new List<string>()));
        h.AiChatClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Փոքրիկ ամպիկը երկնքի ընկերն է։");
        h.Conversations.GetOrCreateActiveConversationAsync(Arg.Any<Guid>(), Arg.Any<Guid?>())
            .Throws(new InvalidOperationException("db down"));

        var result = await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        // The child still hears the answer despite the persistence failure.
        Assert.IsType<FileContentResult>(result);
    }
}

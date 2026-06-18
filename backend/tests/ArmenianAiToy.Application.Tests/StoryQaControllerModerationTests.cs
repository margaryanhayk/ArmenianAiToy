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
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(transcript);

    /// <summary>Materializes the response body to bytes whether the controller
    /// returned the buffered failure-clip result (<see cref="FileContentResult"/>)
    /// or the happy-path streaming result (<see cref="StreamingAudioResult"/>).
    /// Confirms the response is 200 audio/mpeg either way.</summary>
    private static async Task<byte[]> ReadBodyAsync(IActionResult result)
    {
        switch (result)
        {
            case FileContentResult file:
                Assert.Equal("audio/mpeg", file.ContentType);
                return file.FileContents;
            case StreamingAudioResult streaming:
            {
                var http = new DefaultHttpContext();
                using var body = new MemoryStream();
                http.Response.Body = body;
                await streaming.ExecuteResultAsync(
                    new ActionContext { HttpContext = http });
                Assert.Equal(200, http.Response.StatusCode);
                Assert.Equal("audio/mpeg", http.Response.ContentType);
                return body.ToArray();
            }
            default:
                Assert.Fail($"Unexpected result type {result.GetType().Name}");
                return [];
        }
    }

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
        await ReadBodyAsync(result);

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

        await ReadBodyAsync(result);
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

        await ReadBodyAsync(result);
        // Moderation runs on the question (input); the answer is also output-
        // moderated now, so this is "at least once" — the exact dual-call
        // contract is pinned by UnsafeAnswer_IsOutputModerated_*.
        await h.Moderation.Received().CheckContentAsync("Ո՞վ է փոքրիկ ամպիկը");
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

        await ReadBodyAsync(result);
        // No text to check → moderation not consulted, GPT not called.
        await h.Moderation.DidNotReceiveWithAnyArgs().CheckContentAsync(default!);
        await h.AiChatClient.DidNotReceiveWithAnyArgs()
            .GetCompletionAsync(default!, default!);
    }

    // --- OUTPUT moderation on the spoken answer (P0 fix) -------------

    [Fact]
    public async Task UnsafeAnswer_IsOutputModerated_SpeaksFallback_NotTheAnswer()
    {
        var h = Create();
        const string Question = "Ինչու էր ամպիկը տխուր";
        // A model answer that PASSES StoryAnswerFilter (short Armenian, no
        // markers/digits/unknown proper nouns) so it reaches output moderation.
        const string ModelAnswer = "Նա շատ ուրախ էր այդ օրը։";
        WireTranscript(h, Question);
        // Input (question) safe; OUTPUT (answer) flagged by the classifier.
        h.Moderation.CheckContentAsync(Question)
            .Returns(new ModerationResult(IsSafe: true, FlaggedCategories: new List<string>()));
        h.Moderation.CheckContentAsync(ModelAnswer)
            .Returns(new ModerationResult(IsSafe: false, FlaggedCategories: new List<string> { "violence" }));
        h.AiChatClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(ModelAnswer);

        var result = await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        // Still a normal (streamed) audio turn — never a 502/silence.
        Assert.IsType<StreamingAudioResult>(result);
        // The answer WAS run through output moderation...
        await h.Moderation.Received(1).CheckContentAsync(ModelAnswer);
        // ...and the child hears the safe fallback, NOT the flagged answer.
        await h.Synthesis.Received().SynthesizeArmenianAsync(
            StoryAnswerFilter.SafeFallback, Arg.Any<CancellationToken>());
        await h.Synthesis.DidNotReceive().SynthesizeArmenianAsync(
            ModelAnswer, Arg.Any<CancellationToken>());
    }

    // --- Gap 6: Whisper biasing with the current scene ---------------

    [Fact]
    public async Task Transcription_IsBiasedWithCurrentSceneText()
    {
        var h = Create();
        WireTranscript(h, "Ո՞վ է փոքրիկ ամպիկը");
        h.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(IsSafe: true, FlaggedCategories: new List<string>()));
        h.AiChatClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Փոքրիկ ամպիկը երկնքի ընկերն է։");
        // offset 0 → segment 0; the bias is (the tail of) that segment's text.
        var seg0 = new InMemoryCuratedStoryLibrary().GetById(StoryId)!.Segments[0].Text;

        await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        await h.Transcription.Received(1).TranscribeArmenianAsync(
            Arg.Any<Stream>(), Arg.Any<string>(),
            Arg.Is<string?>(p => !string.IsNullOrEmpty(p) && seg0.EndsWith(p)),
            Arg.Any<CancellationToken>(), Arg.Any<string?>());
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
        await ReadBodyAsync(result);
    }

    // --- Gap 4: transient failure -> spoken fallback, never silence ---

    [Fact]
    public async Task SttFailure_ReturnsSpokenFallbackAudio_Not502()
    {
        var h = Create();
        h.Transcription.TranscribeArmenianAsync(
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Throws(new InvalidOperationException("whisper down"));

        var result = await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        // 200 audio (the cached safe-fallback clip), not a silent 502.
        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("audio/mpeg", file.ContentType);
        // No GPT, no persistence on the STT-failure path.
        await h.AiChatClient.DidNotReceiveWithAnyArgs().GetCompletionAsync(default!, default!);
        await h.Conversations.DidNotReceiveWithAnyArgs()
            .GetOrCreateActiveConversationAsync(default, default);
    }

    [Fact]
    public async Task AnswerTtsFailure_ReturnsSpokenFallbackAudio_Not502()
    {
        var h = Create();
        WireTranscript(h, "Ո՞վ է փոքրիկ ամպիկը");
        h.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(IsSafe: true, FlaggedCategories: new List<string>()));
        h.AiChatClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Փոքրիկ ամպիկը երկնքի ընկերն է։");

        // The FIRST synthesis call (the answer) fails; the fallback-clip
        // render (or its cache) still produces audio. Counting calls keeps
        // this deterministic regardless of the answer-filter outcome.
        var calls = 0;
        h.Synthesis.SynthesizeArmenianAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                if (calls == 1) throw new InvalidOperationException("tts down for the answer");
                return new AudioSynthesisResult(TtsMp3, "audio/mpeg");
            });

        var result = await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("audio/mpeg", file.ContentType);
    }

    // --- PART B: streamed answer audio (backward-compatible) ----------

    /// <summary>The happy path now returns a chunked
    /// <see cref="StreamingAudioResult"/> (audio/mpeg, status 200) rather
    /// than a buffered <see cref="FileContentResult"/>. The core PART B
    /// guarantee: the FIRST bytes on the wire are the already-VALIDATED
    /// answer's audio (never raw GPT tokens), and the tail clips (pause +
    /// bridge) follow — so the streamed body is strictly longer than the
    /// answer alone. A client that buffers the whole body still gets the
    /// full clip (backward-compatible).</summary>
    [Fact]
    public async Task SafeTranscript_StreamsValidatedAnswerFirst_ThenTail()
    {
        var h = Create();
        WireTranscript(h, "Ո՞վ է փոքրիկ ամպիկը");
        h.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(IsSafe: true, FlaggedCategories: new List<string>()));
        h.AiChatClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Փոքրիկ ամպիկը երկնքի ընկերն է։");

        // Distinct bytes per synthesized text so we can locate the answer
        // portion in the streamed body. The answer text is whatever passed
        // the filter (GPT answer or the safe fallback) — both are VALIDATED
        // before any audio byte is produced.
        h.Synthesis.SynthesizeArmenianAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new AudioSynthesisResult(
                Encoding.UTF8.GetBytes("TTS::" + ci.ArgAt<string>(0)), "audio/mpeg"));

        var result = await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        // Backward-compatible: streamed, 200, audio/mpeg, full body buffers fine.
        var streaming = Assert.IsType<StreamingAudioResult>(result);
        var body = await ReadBodyAsync(streaming);
        Assert.NotEmpty(body);

        // The validated answer (the FIRST synthesis call) is the START of the
        // body, and the body is strictly longer (the pause + bridge tail
        // follows it), confirming answer-first streaming with the tail behind.
        var answerText = (string)h.Synthesis.ReceivedCalls()
            .First(c => c.GetMethodInfo().Name == nameof(IAudioSynthesisService.SynthesizeArmenianAsync))
            .GetArguments()[0]!;
        var answerBytes = Encoding.UTF8.GetBytes("TTS::" + answerText);
        Assert.True(body.AsSpan().StartsWith(answerBytes),
            "the validated answer audio must be the first bytes on the wire");
        Assert.True(body.Length > answerBytes.Length,
            "the pause + bridge tail must follow the answer in the body");
    }

    /// <summary>The streamed body is byte-identical (bytes + order) to the
    /// canonical buffered composition
    /// <c>ComposeAnswerWithPause(answer, bridge, recap)</c> built from the
    /// SAME answer + bridge clips — i.e. WHAT the child hears is unchanged;
    /// only WHEN the bytes start flowing differs. Resilient to the process-
    /// static bridge cache: we read back the exact answer and bridge bytes
    /// the controller actually streamed rather than re-synthesizing.</summary>
    [Fact]
    public async Task StreamedBody_IsByteIdentical_ToBufferedComposition()
    {
        var h = Create();
        const string AnswerInput = "Փոքրիկ ամպիկը երկնքի ընկերն է։";
        WireTranscript(h, "Ո՞վ է փոքրիկ ամպիկը");
        h.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(IsSafe: true, FlaggedCategories: new List<string>()));
        h.AiChatClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(AnswerInput);
        h.Synthesis.SynthesizeArmenianAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new AudioSynthesisResult(
                Encoding.UTF8.GetBytes("TTS::" + ci.ArgAt<string>(0)), "audio/mpeg"));

        var result = await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);
        var body = await ReadBodyAsync(Assert.IsType<StreamingAudioResult>(result));

        // The answer that was spoken (first synthesis call) — used to derive
        // the answer clip. The bridge clip is recovered from the body itself
        // (everything after answer + pause), making the reference robust to
        // whatever the cross-test bridge cache contains.
        var spokenAnswer = (string)h.Synthesis.ReceivedCalls()
            .First(c => c.GetMethodInfo().Name == nameof(IAudioSynthesisService.SynthesizeArmenianAsync))
            .GetArguments()[0]!;
        var answer = Encoding.UTF8.GetBytes("TTS::" + spokenAnswer);

        // Reconstruct pause length the same way the controller does, then the
        // bridge is the remainder of the body. LittleCloud has no recap, so
        // the recap slot is null in both the body and the reference.
        var pauseBytes = await PauseByteCountAsync(h);
        var bridge = body[(answer.Length + pauseBytes)..];

        var expected = h.Controller.ComposeAnswerWithPause(answer, bridge, recap: null);
        Assert.Equal(expected, body);
    }

    /// <summary>Reconstructs the controller's silence-pause byte count via a
    /// throwaway composition: ComposeAnswerWithPause(empty, empty, null)
    /// yields exactly the pause bytes.</summary>
    private static Task<int> PauseByteCountAsync(Harness h) =>
        Task.FromResult(h.Controller.ComposeAnswerWithPause([], [], recap: null).Length);

    // --- PART A: STT model seam (default unchanged) -------------------

    /// <summary>Builds a controller with the supplied real configuration so a
    /// test can set (or omit) <c>StoryQa:TranscriptionModel</c>. Mirrors the
    /// main harness but takes config explicitly.</summary>
    private static Harness CreateWithConfig(IConfiguration config)
    {
        var transcription = Substitute.For<IAudioTranscriptionService>();
        var synthesis = Substitute.For<IAudioSynthesisService>();
        synthesis.SynthesizeArmenianAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AudioSynthesisResult(TtsMp3, "audio/mpeg"));
        var moderation = Substitute.For<IModerationService>();
        var aiChatClient = Substitute.For<IAiChatClient>();
        var conversations = Substitute.For<IConversationService>();
        conversations.GetOrCreateActiveConversationAsync(Arg.Any<Guid>(), Arg.Any<Guid?>())
            .Returns(new Conversation { Id = Guid.NewGuid(), DeviceId = Guid.NewGuid(), StartedAt = DateTime.UtcNow });
        conversations.AddMessageAsync(
                Arg.Any<Guid>(), Arg.Any<MessageRole>(), Arg.Any<string>(), Arg.Any<SafetyFlag>())
            .Returns(new Message { Id = Guid.NewGuid() });

        var controller = new StoryQaController(
            transcription, synthesis, new InMemoryCuratedStoryLibrary(),
            new LibraryStoryQuestionService(aiChatClient), moderation, conversations,
            Substitute.For<IWebHostEnvironment>(), config,
            Substitute.For<ILogger<StoryQaController>>());

        var deviceId = Guid.NewGuid();
        var http = new DefaultHttpContext();
        http.Items["DeviceId"] = deviceId;
        http.Request.Body = new MemoryStream(InboundWav);
        http.Request.ContentType = "audio/wav";
        http.Request.ContentLength = InboundWav.Length;
        controller.ControllerContext = new ControllerContext { HttpContext = http };

        return new Harness(controller, transcription, synthesis, moderation,
            aiChatClient, conversations, deviceId, Guid.NewGuid());
    }

    /// <summary>DEFAULT behavior unchanged: with no
    /// <c>StoryQa:TranscriptionModel</c> configured, the controller passes
    /// <c>model: null</c> to the transcription call, so the STT service keeps
    /// its configured default model (whisper-1).</summary>
    [Fact]
    public async Task NoTranscriptionModelConfigured_PassesNullModel_DefaultUnchanged()
    {
        var config = new ConfigurationBuilder().Build(); // no StoryQa:* keys
        var h = CreateWithConfig(config);
        WireTranscript(h, "Ո՞վ է փոքրիկ ամպիկը");
        h.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(true, new List<string>()));
        h.AiChatClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Փոքրիկ ամպիկը երկնքի ընկերն է։");

        await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        await h.Transcription.Received(1).TranscribeArmenianAsync(
            Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>(), null);
    }

    /// <summary>The seam works: when an operator sets
    /// <c>StoryQa:TranscriptionModel</c>, that model name is threaded to the
    /// per-call STT override. (Defaults stay whisper-1; this only flips when
    /// configured, post-benchmark.)</summary>
    [Fact]
    public async Task ConfiguredTranscriptionModel_IsThreadedToSttCall()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("StoryQa:TranscriptionModel", "gpt-4o-mini-transcribe"),
        }).Build();
        var h = CreateWithConfig(config);
        WireTranscript(h, "Ո՞վ է փոքրիկ ամպիկը");
        h.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(true, new List<string>()));
        h.AiChatClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Փոքրիկ ամպիկը երկնքի ընկերն է։");

        await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        await h.Transcription.Received(1).TranscribeArmenianAsync(
            Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>(), "gpt-4o-mini-transcribe");
    }
}

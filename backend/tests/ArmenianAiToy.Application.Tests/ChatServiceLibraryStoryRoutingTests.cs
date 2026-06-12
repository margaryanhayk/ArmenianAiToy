using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// W2 — ChatService Step 3.7 library-story interrupt routing. Pins the
/// routing matrix (engine flag × session presence), the deterministic
/// continue-cue resume, the bounded Q&amp;A call, the exact garbled
/// guard precedence, output moderation on the branch, and the
/// position-read-only contract. Two separate fake AI clients make the
/// paths observable: <c>_legacyAi</c> backs the legacy pipeline,
/// <c>_qaAi</c> backs LibraryStoryQuestionService — so "which engine
/// answered" is a hard assertion, not an inference. No paid calls.
/// </summary>
public class ChatServiceLibraryStoryRoutingTests
{
    private const string LegacyReply = "Բարև՛ ջան։";
    private const string QaReply = "Ամպիկը մի փոքր տխրեց։ Արի լսենք, թե ինչ կլինի հետո։";
    private const string BadQaReply = "As an AI language model, I cannot answer that.";

    private readonly IAiChatClient _legacyAi = Substitute.For<IAiChatClient>();
    private readonly IAiChatClient _qaAi = Substitute.For<IAiChatClient>();
    private readonly IModerationService _moderation = Substitute.For<IModerationService>();
    private readonly IConversationService _conversations = Substitute.For<IConversationService>();
    private readonly InMemoryCuratedStoryLibrary _library = new();
    private readonly LibraryStorySessionTracker _tracker = new();
    private readonly Guid _conversationId;

    private string? _storedAssistantContent;
    private SafetyFlag? _storedAssistantFlag;

    public ChatServiceLibraryStoryRoutingTests()
    {
        _moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(true, new List<string>()));

        _legacyAi.GetCompletionAsync(
                Arg.Any<string>(), Arg.Any<List<(string Role, string Content)>>())
            .Returns(LegacyReply);
        _qaAi.GetCompletionAsync(
                Arg.Any<string>(), Arg.Any<List<(string Role, string Content)>>())
            .Returns(QaReply);

        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            StartedAt = DateTime.UtcNow
        };
        _conversationId = conversation.Id;
        _conversations.GetOrCreateActiveConversationAsync(Arg.Any<Guid>(), Arg.Any<Guid?>())
            .Returns(conversation);
        _conversations.GetRecentMessagesAsync(Arg.Any<Guid>(), Arg.Any<int>())
            .Returns(new List<(string Role, string Content)>());
        _conversations.AddMessageAsync(
                Arg.Any<Guid>(), Arg.Any<MessageRole>(), Arg.Any<string>(), Arg.Any<SafetyFlag>())
            .Returns(callInfo =>
            {
                if (callInfo.ArgAt<MessageRole>(1) == MessageRole.Assistant)
                {
                    _storedAssistantContent = callInfo.ArgAt<string>(2);
                    _storedAssistantFlag = callInfo.ArgAt<SafetyFlag>(3);
                }
                return new Message
                {
                    Id = Guid.NewGuid(),
                    ConversationId = callInfo.ArgAt<Guid>(0),
                    Role = callInfo.ArgAt<MessageRole>(1),
                    Content = callInfo.ArgAt<string>(2),
                    Timestamp = DateTime.UtcNow,
                    SafetyFlag = callInfo.ArgAt<SafetyFlag>(3)
                };
            });
    }

    private IChatService MakeService(string engine)
    {
        var childService = Substitute.For<IChildService>();
        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>()).Returns((Child?)null);
        var config = Substitute.For<IConfiguration>();
        config["SystemPrompt"].Returns("You are a test assistant.");
        // NSubstitute auto-returns "" (not null) for unstubbed string
        // members, which would defeat the ?? fallback — stub the key the
        // way every real deployment ships it.
        config["SafetyFallbackResponse"].Returns("Արի, մի հեքիաթ սկսենք։");

        var playback = new LibraryStoryPlaybackService(
            _library, _tracker, new StoryEngineOptions { Engine = engine });

        return new ChatService(
            _legacyAi, _moderation, _conversations, childService, config,
            Substitute.For<ILogger<ChatService>>(), new StoryChoiceCoherenceGate(),
            libraryPlayback: playback,
            storyLibrary: _library,
            libraryQuestions: new LibraryStoryQuestionService(_qaAi));
    }

    private async Task<int> LegacyAiCalls() =>
        (await Task.FromResult(_legacyAi.ReceivedCalls().Count()));

    private int QaAiCalls() => _qaAi.ReceivedCalls().Count();

    // ── Routing matrix: flag × session ──────────────────────────────

    [Fact]
    public async Task LegacyEngine_EvenWithTrackedSession_UsesLegacyPipelineUnchanged()
    {
        // A session row exists in the tracker, but the engine flag is
        // legacy — Step 3.7 must be structurally inert.
        _tracker.Start(_conversationId, InMemoryCuratedStoryLibrary.LittleCloudId);
        var service = MakeService("legacy");

        var result = await service.GetResponseAsync(Guid.NewGuid(), "Բարև Արեգ։");

        Assert.Equal(LegacyReply, result.Response);
        Assert.Equal(0, QaAiCalls());
        Assert.True(await LegacyAiCalls() > 0);
        // Session untouched by the legacy turn:
        Assert.Equal(0, _tracker.GetCurrent(_conversationId)!.SegmentIndex);
    }

    [Fact]
    public async Task LibraryEngine_NoActiveSession_UsesLegacyPipelineUnchanged()
    {
        var service = MakeService("library");

        var result = await service.GetResponseAsync(Guid.NewGuid(), "Բարև Արեգ։");

        Assert.Equal(LegacyReply, result.Response);
        Assert.Equal(0, QaAiCalls());
        Assert.True(await LegacyAiCalls() > 0);
    }

    // ── Q&A branch ──────────────────────────────────────────────────

    [Fact]
    public async Task LibraryEngine_ActiveSession_Question_RoutesToQaOnce_NeverLegacy()
    {
        _tracker.Start(_conversationId, InMemoryCuratedStoryLibrary.LittleCloudId);
        var service = MakeService("library");

        var result = await service.GetResponseAsync(
            Guid.NewGuid(), "Ինչո՞ւ էր ամպիկը տխուր");

        Assert.Equal(QaReply, result.Response);
        Assert.Equal(QaReply, _storedAssistantContent);
        Assert.Equal(SafetyFlag.Clean, _storedAssistantFlag);
        Assert.Equal(1, QaAiCalls());
        Assert.Equal(0, await LegacyAiCalls()); // legacy GPT/ModeDetector path never entered
    }

    [Fact]
    public async Task QaBranch_NeverAdvancesOrClearsSession()
    {
        _tracker.Start(_conversationId, InMemoryCuratedStoryLibrary.LittleCloudId);
        var service = MakeService("library");
        var before = _tracker.GetCurrent(_conversationId)!;

        await service.GetResponseAsync(Guid.NewGuid(), "Ո՞վ է ամպիկը");

        var after = _tracker.GetCurrent(_conversationId);
        Assert.NotNull(after);
        Assert.Equal(before.StoryId, after!.StoryId);
        Assert.Equal(before.SegmentIndex, after.SegmentIndex);
    }

    [Fact]
    public async Task QaBranch_FilterRejectsBothAttempts_ReturnsAndPersistsExactFallback()
    {
        _qaAi.GetCompletionAsync(
                Arg.Any<string>(), Arg.Any<List<(string Role, string Content)>>())
            .Returns(BadQaReply, BadQaReply);
        _tracker.Start(_conversationId, InMemoryCuratedStoryLibrary.LittleCloudId);
        var service = MakeService("library");

        var result = await service.GetResponseAsync(Guid.NewGuid(), "Ինչո՞ւ");

        Assert.Equal(StoryAnswerFilter.SafeFallback, result.Response);
        Assert.Equal(StoryAnswerFilter.SafeFallback, _storedAssistantContent);
        Assert.Equal(2, QaAiCalls()); // first attempt + one strict repair
        Assert.Equal(0, await LegacyAiCalls());
    }

    [Fact]
    public async Task QaBranch_OutputModerationFlag_SpeaksSafetyFallbackBlocked()
    {
        // Input moderation (on the child question) stays safe via the
        // ctor stub; the output-moderation call is keyed on the exact
        // Q&A answer text and flagged — the branch must fail safe.
        _moderation.CheckContentAsync(QaReply).Returns(
            new ModerationResult(false, new List<string> { "test_flag" }));
        _tracker.Start(_conversationId, InMemoryCuratedStoryLibrary.LittleCloudId);
        var service = MakeService("library");

        var result = await service.GetResponseAsync(Guid.NewGuid(), "Ինչո՞ւ էր ամպիկը տխուր");

        Assert.Equal("Արի, մի հեքիաթ սկսենք։", result.Response);
        Assert.Equal(SafetyFlag.Blocked, result.SafetyFlag);
        Assert.Equal(SafetyFlag.Blocked, _storedAssistantFlag);
    }

    // ── Garbled guard precedence ────────────────────────────────────

    [Fact]
    public async Task GarbledInput_WithActiveSession_ExactClarification_ZeroQaCalls_SessionUnchanged()
    {
        _tracker.Start(_conversationId, InMemoryCuratedStoryLibrary.LittleCloudId);
        var service = MakeService("library");

        var result = await service.GetResponseAsync(Guid.NewGuid(), "քրռռռռ բխխխ");

        Assert.Equal("Կներե՛ս, լավ չլսեցի։ Կրկնի՞ր, խնդրում եմ։", result.Response);
        Assert.Equal(ArmenianVoiceReplyGuard.ClarificationResponse, result.Response);
        Assert.Equal(0, QaAiCalls());
        Assert.Equal(0, await LegacyAiCalls());
        var session = _tracker.GetCurrent(_conversationId);
        Assert.NotNull(session);
        Assert.Equal(0, session!.SegmentIndex);
    }

    // ── Continue cues ───────────────────────────────────────────────

    [Theory]
    [InlineData("հետո՞")]
    [InlineData("շարունակիր")]
    [InlineData("Շարունակի՛ր")]
    [InlineData("հա")]
    [InlineData("էլի")]
    public async Task ContinueCue_AdvancesAndServesNextVerbatimSegment_NoGpt(string cue)
    {
        // W3 contract: continue cues ADVANCE the story by one segment
        // and serve the canned lead-in + the NEXT verbatim segment.
        _tracker.Start(_conversationId, InMemoryCuratedStoryLibrary.LittleCloudId);
        var story = _library.GetById(InMemoryCuratedStoryLibrary.LittleCloudId)!;
        var service = MakeService("library");

        var result = await service.GetResponseAsync(Guid.NewGuid(), cue);

        Assert.Equal(
            LibraryStoryCannedLines.ResumeLeadIn + "\n" + story.Segments[1].Text,
            result.Response);
        Assert.Equal(0, QaAiCalls());      // «հետո՞» must NEVER reach Q&A
        Assert.Equal(0, await LegacyAiCalls());
        Assert.Equal(1, _tracker.GetCurrent(_conversationId)!.SegmentIndex);
    }

    [Theory]
    [InlineData("հետո ինչ եղավ ամպիկի հետ")] // contains a cue word but is a question
    [InlineData("շարունակիր պատմել ինձ ամեն ինչ")]
    public async Task CueWordInsideLongerSentence_IsAQuestion_NotACue(string input)
    {
        _tracker.Start(_conversationId, InMemoryCuratedStoryLibrary.LittleCloudId);
        var service = MakeService("library");

        var result = await service.GetResponseAsync(Guid.NewGuid(), input);

        Assert.Equal(QaReply, result.Response);
        Assert.Equal(1, QaAiCalls());
    }

    // ── Defensive paths ─────────────────────────────────────────────

    [Fact]
    public async Task UnresolvableStoryId_ClearsSession_FallsThroughToLegacy()
    {
        _tracker.Start(_conversationId, "no-such-story");
        var service = MakeService("library");

        var result = await service.GetResponseAsync(Guid.NewGuid(), "Բարև Արեգ։");

        Assert.Equal(LegacyReply, result.Response);
        Assert.Equal(0, QaAiCalls());
        Assert.True(await LegacyAiCalls() > 0);
        Assert.Null(_tracker.GetCurrent(_conversationId)); // defensively cleared
    }

    [Fact]
    public async Task AnbanHuriDraft_SessionPointingAtIt_IsUnreachable_FallsThroughToLegacy()
    {
        // Even a session row naming the draft cannot make it serve: the
        // approved-only library does not resolve "anban-huri", so the
        // session is cleared and the turn runs legacy. The draft stays
        // draft — runtime Q&A never touches it.
        _tracker.Start(_conversationId, "anban-huri");
        var service = MakeService("library");

        var result = await service.GetResponseAsync(Guid.NewGuid(), "Ո՞վ է Հուռին");

        Assert.Equal(LegacyReply, result.Response);
        Assert.Equal(0, QaAiCalls());
        Assert.Null(_tracker.GetCurrent(_conversationId));
    }

    // ── TTS seam ────────────────────────────────────────────────────

    [Fact]
    public void Composer_PassesQaAnswerThroughUnchanged_NoChoicesNoMode()
    {
        // The Q&A/resume branch returns ChatResponse with null choices
        // and null mode — AudioChatController's composer must hand the
        // text to TTS byte-identical.
        Assert.Equal(QaReply,
            AudioStoryResponseComposer.ComposeTtsText(QaReply, null, null, null));
        Assert.Equal(StoryAnswerFilter.SafeFallback,
            AudioStoryResponseComposer.ComposeTtsText(
                StoryAnswerFilter.SafeFallback, null, null, null));
    }

    // ── ContinueCueDetector unit pins ───────────────────────────────

    [Theory]
    [InlineData("հետո՞", true)]
    [InlineData("հետո", true)]
    [InlineData("Հա՛", true)]
    [InlineData("էլի՜", true)]
    [InlineData("«շարունակիր»", true)]
    [InlineData("շարունակի", true)]
    [InlineData("հետո ինչ եղավ", false)]
    [InlineData("Բարև Արեգ", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void ContinueCueDetector_MatchesWholeUtteranceOnly(string? input, bool expected)
    {
        Assert.Equal(expected, ContinueCueDetector.IsContinueCue(input));
    }
}

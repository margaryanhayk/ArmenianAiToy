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
/// W3 — library-story start + autoplay-by-cue + ending/reflection
/// lifecycle through ChatService. Pins: deterministic story-start cues
/// open the default APPROVED story (zero GPT), continue cues advance
/// segment-by-segment with byte-verbatim text, the authored
/// reflection turn is delivered in the same turn that detects the end
/// (the session is already cleared), Q&amp;A still never moves the
/// position, garbled input still wins, the draft Anban Huri cannot be
/// started, and legacy behavior is untouched when the flag is off.
/// Same two-fake-AI-clients harness as the W2 routing tests.
/// </summary>
public class ChatServiceLibraryStoryLifecycleTests
{
    private const string LegacyStoryReply =
        "Մեկ։ Երկու։\n---\nCHOICE_A:Գնալ առաջ\nCHOICE_B:Մնալ տանը";
    private const string QaReply = "Ամպիկը մի փոքր տխրեց։ Արի լսենք, թե ինչ կլինի հետո։";

    private readonly IAiChatClient _legacyAi = Substitute.For<IAiChatClient>();
    private readonly IAiChatClient _qaAi = Substitute.For<IAiChatClient>();
    private readonly IModerationService _moderation = Substitute.For<IModerationService>();
    private readonly IConversationService _conversations = Substitute.For<IConversationService>();
    private readonly InMemoryCuratedStoryLibrary _library = new();
    private readonly LibraryStorySessionTracker _tracker = new();
    private readonly Guid _conversationId;

    private string? _storedAssistantContent;

    public ChatServiceLibraryStoryLifecycleTests()
    {
        _moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(true, new List<string>()));
        _legacyAi.GetCompletionAsync(
                Arg.Any<string>(), Arg.Any<List<(string Role, string Content)>>())
            .Returns(LegacyStoryReply);
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

    private IChatService MakeService(string engine, bool benchAutoStory = false)
    {
        var childService = Substitute.For<IChildService>();
        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>()).Returns((Child?)null);
        var config = Substitute.For<IConfiguration>();
        config["SystemPrompt"].Returns("You are a test assistant.");
        config["SafetyFallbackResponse"].Returns("Արի, մի հեքիաթ սկսենք։");
        config["Story:BenchAutoStory"].Returns(benchAutoStory ? "true" : null);

        var playback = new LibraryStoryPlaybackService(
            _library, _tracker, new StoryEngineOptions { Engine = engine });

        return new ChatService(
            _legacyAi, _moderation, _conversations, childService, config,
            Substitute.For<ILogger<ChatService>>(), new StoryChoiceCoherenceGate(),
            libraryPlayback: playback,
            storyLibrary: _library,
            libraryQuestions: new LibraryStoryQuestionService(_qaAi));
    }

    private int LegacyAiCalls() => _legacyAi.ReceivedCalls().Count();
    private int QaAiCalls() => _qaAi.ReceivedCalls().Count();

    private CuratedStory DefaultStory() => _library.SelectDefault();

    // ── Story start ─────────────────────────────────────────────────

    [Fact]
    public async Task LegacyEngine_StoryRequest_StaysOnLegacyPipeline_NoLibrarySession()
    {
        var service = MakeService("legacy");

        var result = await service.GetResponseAsync(Guid.NewGuid(), "tell me a story");

        Assert.True(LegacyAiCalls() > 0);          // legacy GPT story flow ran
        Assert.Equal(0, QaAiCalls());
        Assert.Null(_tracker.GetCurrent(_conversationId)); // no library session
        Assert.NotNull(result.Response);
        Assert.NotEqual(string.Empty, result.Response);
    }

    [Theory]
    [InlineData("հեքիաթ պատմիր")]
    [InlineData("Պատմիր հեքիաթ։")]
    [InlineData("tell me a story")]
    public async Task LibraryEngine_StoryStartCue_StartsDefaultApprovedStory_ServesFirstSegmentVerbatim(
        string request)
    {
        var service = MakeService("library");
        var story = DefaultStory();

        var result = await service.GetResponseAsync(Guid.NewGuid(), request);

        // Canned reviewed lead-in + byte-verbatim first segment:
        Assert.Equal(
            LibraryStoryCannedLines.StartLeadIn + "\n" + story.Segments[0].Text,
            result.Response);
        Assert.Equal(result.Response, _storedAssistantContent);
        var session = _tracker.GetCurrent(_conversationId);
        Assert.NotNull(session);
        Assert.Equal(story.Id, session!.StoryId);
        Assert.Equal(0, session.SegmentIndex);
        // No GPT anywhere in start/serve:
        Assert.Equal(0, LegacyAiCalls());
        Assert.Equal(0, QaAiCalls());
    }

    [Fact]
    public async Task LibraryEngine_NonStoryRequest_NoSession_StaysOnLegacyPipeline()
    {
        var service = MakeService("library");

        await service.GetResponseAsync(Guid.NewGuid(), "Բարև Արեգ։");

        Assert.True(LegacyAiCalls() > 0);
        Assert.Equal(0, QaAiCalls());
        Assert.Null(_tracker.GetCurrent(_conversationId));
    }

    // ── Continue-cue advancement ────────────────────────────────────

    [Theory]
    [InlineData("հետո՞")]
    [InlineData("շարունակիր")]
    public async Task ActiveSession_ContinueCue_AdvancesAndServesNextSegmentVerbatim(string cue)
    {
        var service = MakeService("library");
        var story = DefaultStory();
        await service.GetResponseAsync(Guid.NewGuid(), "հեքիաթ պատմիր"); // segment 0

        var result = await service.GetResponseAsync(Guid.NewGuid(), cue);

        Assert.Equal(
            LibraryStoryCannedLines.ResumeLeadIn + "\n" + story.Segments[1].Text,
            result.Response);
        Assert.Equal(1, _tracker.GetCurrent(_conversationId)!.SegmentIndex);
        Assert.Equal(0, LegacyAiCalls());
        Assert.Equal(0, QaAiCalls());   // continue cue never reaches Q&A or GPT
    }

    // ── Ending / reflection ─────────────────────────────────────────

    [Fact]
    public async Task ContinuePastFinalSegment_DeliversAuthoredReflectionTurn_AndClearsSession()
    {
        var service = MakeService("library");
        var story = DefaultStory();
        var deviceId = Guid.NewGuid();

        await service.GetResponseAsync(deviceId, "հեքիաթ պատմիր"); // segment 0
        // Walk to the final segment with continue cues:
        for (var i = 1; i < story.Segments.Count; i++)
        {
            var step = await service.GetResponseAsync(deviceId, "հետո՞");
            Assert.EndsWith(story.Segments[i].Text, step.Response);
        }
        Assert.Equal(story.Segments.Count - 1,
            _tracker.GetCurrent(_conversationId)!.SegmentIndex);

        // One more cue past the final segment → the authored ending:
        var ending = await service.GetResponseAsync(deviceId, "հետո՞");

        Assert.Equal(
            story.ReflectionText + "\n" + story.ReflectionQuestions[0],
            ending.Response);
        Assert.Equal(ending.Response, _storedAssistantContent);
        Assert.Null(_tracker.GetCurrent(_conversationId)); // session cleared at end
        Assert.Equal(0, LegacyAiCalls());
        Assert.Equal(0, QaAiCalls());
    }

    // ── Q&A unchanged (position read-only) ──────────────────────────

    [Fact]
    public async Task ActiveSession_Question_AnswersViaQa_DoesNotAdvanceSegment()
    {
        var service = MakeService("library");
        var deviceId = Guid.NewGuid();
        await service.GetResponseAsync(deviceId, "հեքիաթ պատմիր");      // segment 0
        await service.GetResponseAsync(deviceId, "հետո՞");               // segment 1

        var result = await service.GetResponseAsync(deviceId, "Ինչո՞ւ էր ամպիկը տխուր");

        Assert.Equal(QaReply, result.Response);
        Assert.Equal(1, QaAiCalls());
        Assert.Equal(0, LegacyAiCalls());
        // Position untouched by the question:
        Assert.Equal(1, _tracker.GetCurrent(_conversationId)!.SegmentIndex);

        // And the story continues from exactly where it paused:
        var story = DefaultStory();
        var next = await service.GetResponseAsync(deviceId, "հետո՞");
        if (story.Segments.Count > 2)
        {
            Assert.EndsWith(story.Segments[2].Text, next.Response);
        }
        else
        {
            // Two-segment story: continuing past the final segment is
            // the authored ending turn.
            Assert.StartsWith(story.ReflectionText, next.Response);
        }
    }

    // ── Garbled guard still first ───────────────────────────────────

    [Fact]
    public async Task GarbledInput_WithActiveSession_ExactClarification_NoAdvance()
    {
        var service = MakeService("library");
        var deviceId = Guid.NewGuid();
        await service.GetResponseAsync(deviceId, "հեքիաթ պատմիր");

        var result = await service.GetResponseAsync(deviceId, "քրռռռռ բխխխ");

        Assert.Equal("Կներե՛ս, լավ չլսեցի։ Կրկնի՞ր, խնդրում եմ։", result.Response);
        Assert.Equal(ArmenianVoiceReplyGuard.ClarificationResponse, result.Response);
        Assert.Equal(0, _tracker.GetCurrent(_conversationId)!.SegmentIndex);
        Assert.Equal(0, QaAiCalls());
        Assert.Equal(0, LegacyAiCalls());
    }

    // ── Draft posture ───────────────────────────────────────────────

    [Fact]
    public async Task StoryStart_NeverSelectsAnbanHuriDraft()
    {
        var service = MakeService("library");

        await service.GetResponseAsync(Guid.NewGuid(), "հեքիաթ պատմիր");

        var session = _tracker.GetCurrent(_conversationId);
        Assert.NotNull(session);
        Assert.NotEqual("anban-huri", session!.StoryId);
        // The draft is structurally absent from the approved library:
        Assert.Null(_library.GetById("anban-huri"));
        Assert.DoesNotContain(_library.ListAvailable(), s => s.Id == "anban-huri");
    }

    // ── Verbatim contract / TTS seam ────────────────────────────────

    [Fact]
    public async Task ServedSegments_BypassVoiceCapAndCleaning_EvenWhenLongerThanThreeSentences()
    {
        // The non-story voice cap trims to 3 sentences; library text must
        // bypass it entirely. Serve every segment and assert each arrives
        // byte-verbatim regardless of its sentence count.
        var service = MakeService("library");
        var story = DefaultStory();
        var deviceId = Guid.NewGuid();

        var first = await service.GetResponseAsync(deviceId, "հեքիաթ պատմիր");
        Assert.Equal(
            LibraryStoryCannedLines.StartLeadIn + "\n" + story.Segments[0].Text,
            first.Response);

        for (var i = 1; i < story.Segments.Count; i++)
        {
            var step = await service.GetResponseAsync(deviceId, "շարունակիր");
            Assert.Equal(
                LibraryStoryCannedLines.ResumeLeadIn + "\n" + story.Segments[i].Text,
                step.Response);
        }
    }

    [Fact]
    public void Composer_PassesLibraryTurnsThroughUnchanged()
    {
        var story = DefaultStory();
        var startTurn = LibraryStoryCannedLines.StartLeadIn + "\n" + story.Segments[0].Text;
        var endingTurn = story.ReflectionText + "\n" + story.ReflectionQuestions[0];

        // Library turns carry null choices / null mode — TTS receives
        // ChatResponse.Response byte-identical.
        Assert.Equal(startTurn,
            AudioStoryResponseComposer.ComposeTtsText(startTurn, null, null, null));
        Assert.Equal(endingTurn,
            AudioStoryResponseComposer.ComposeTtsText(endingTurn, null, null, null));
    }

    // ── Bench auto-story (Whisper-proof: any input starts/advances) ─

    [Fact]
    public async Task BenchAutoStory_AnyInput_StartsThenAdvancesVerbatim_NeverQa()
    {
        var service = MakeService("library", benchAutoStory: true);
        var story = DefaultStory();
        var deviceId = Guid.NewGuid();

        // Input that is NOT a start cue still starts the default story.
        var start = await service.GetResponseAsync(deviceId, "ինչ որ բան");
        Assert.Equal(
            LibraryStoryCannedLines.StartLeadIn + "\n" + story.Segments[0].Text,
            start.Response);
        Assert.Equal(0, _tracker.GetCurrent(_conversationId)!.SegmentIndex);

        // A misheard re-press (not a continue cue) ADVANCES verbatim —
        // it must NOT become a GPT Q&A summary.
        var next = await service.GetResponseAsync(deviceId, "Բատմիր հեկիաթ");
        Assert.Equal(
            LibraryStoryCannedLines.ResumeLeadIn + "\n" + story.Segments[1].Text,
            next.Response);
        Assert.Equal(1, _tracker.GetCurrent(_conversationId)!.SegmentIndex);
        Assert.Equal(0, QaAiCalls());      // GPT Q&A never invoked
        Assert.Equal(0, LegacyAiCalls());  // legacy never invoked
    }

    [Fact]
    public async Task BenchAutoStory_Off_PreservesQaForNonCueInput()
    {
        // With the flag off, a non-cue question during an active session
        // still routes to Q&A (unchanged production behavior).
        var service = MakeService("library", benchAutoStory: false);
        var deviceId = Guid.NewGuid();
        await service.GetResponseAsync(deviceId, "հեքիաթ պատմիր"); // start

        var q = await service.GetResponseAsync(deviceId, "Ինչո՞ւ էր ամպիկը տխուր");

        Assert.Equal(QaReply, q.Response);
        Assert.Equal(1, QaAiCalls());
        Assert.Equal(0, _tracker.GetCurrent(_conversationId)!.SegmentIndex); // not advanced
    }

    // ── #073: Calm/bedtime veto on library start ────────────────────

    [Theory]
    [InlineData("i'm sleepy")]
    [InlineData("good night")]
    [InlineData("քնել")]   // քնել (sleep)
    [InlineData("հոգնած")] // հոգնած (tired)
    public async Task CalmCuedInput_DoesNotStartLibraryStory_FallsThroughToLegacy(string calmInput)
    {
        // Bench auto-start makes ANY input a start trigger, so this exercises
        // the #073 calm veto on the start gate directly (it applies to EVERY
        // start path, including bench). A bedtime/Calm-cued message must NOT
        // open the interactive (reflection-question-ending) library engine; it
        // falls through to the existing Calm/legacy handling.
        var service = MakeService("library", benchAutoStory: true);

        var result = await service.GetResponseAsync(Guid.NewGuid(), calmInput);

        Assert.Null(_tracker.GetCurrent(_conversationId)); // no library session opened
        Assert.True(LegacyAiCalls() > 0);                  // fell through to legacy pipeline
        Assert.Equal(0, QaAiCalls());
        // Crucially, the response is NOT a library story lead-in.
        Assert.DoesNotContain(LibraryStoryCannedLines.StartLeadIn, result.Response);
    }

    [Fact]
    public async Task CalmCuedInput_Off_PlainStartCue_StillStartsLibraryStory()
    {
        // Regression guard: the veto must not block a normal (non-calm)
        // story-start cue — the library engine still opens as before.
        var service = MakeService("library");

        var result = await service.GetResponseAsync(Guid.NewGuid(), "հեքիաթ պատմիր");

        Assert.NotNull(_tracker.GetCurrent(_conversationId));
        Assert.StartsWith(LibraryStoryCannedLines.StartLeadIn, result.Response);
    }

    // ── Hands-free autoplay (ContinueLibraryStoryAsync) ─────────────

    [Fact]
    public async Task Autoplay_StartSignalsContinue_ThenWalksToEndingAndStops()
    {
        var service = MakeService("library");
        var story = DefaultStory();
        var deviceId = Guid.NewGuid();

        // Start turn (real audio in production) signals "more to play".
        var start = await service.GetResponseAsync(deviceId, "հեքիաթ պատմիր");
        Assert.True(start.LibraryAutoContinue);
        Assert.Equal(0, _tracker.GetCurrent(_conversationId)!.SegmentIndex);

        // Each continue (no audio, no button, no GPT) advances one segment.
        for (var i = 1; i < story.Segments.Count; i++)
        {
            var step = await service.ContinueLibraryStoryAsync(deviceId);
            Assert.Equal(
                LibraryStoryCannedLines.ResumeLeadIn + "\n" + story.Segments[i].Text,
                step.Response);
            Assert.True(step.LibraryAutoContinue);
            Assert.Equal(i, _tracker.GetCurrent(_conversationId)!.SegmentIndex);
            Assert.Equal(0, QaAiCalls());
            Assert.Equal(0, LegacyAiCalls());
        }

        // One more continue → authored ending; session cleared; STOP signal.
        var ending = await service.ContinueLibraryStoryAsync(deviceId);
        Assert.Equal(
            story.ReflectionText + "\n" + story.ReflectionQuestions[0],
            ending.Response);
        Assert.False(ending.LibraryAutoContinue);
        Assert.Null(_tracker.GetCurrent(_conversationId));

        // A continue with no live session → empty stop response.
        var after = await service.ContinueLibraryStoryAsync(deviceId);
        Assert.Equal(string.Empty, after.Response);
        Assert.False(after.LibraryAutoContinue);
    }

    [Fact]
    public async Task ContinueLibraryStory_NoActiveSession_ReturnsEmptyStop_NoGpt()
    {
        var service = MakeService("library");

        var r = await service.ContinueLibraryStoryAsync(Guid.NewGuid());

        Assert.Equal(string.Empty, r.Response);
        Assert.False(r.LibraryAutoContinue);
        Assert.Equal(0, QaAiCalls());
        Assert.Equal(0, LegacyAiCalls());
    }

    [Fact]
    public async Task LegacyEngine_ContinueLibraryStory_IsInert()
    {
        var service = MakeService("legacy");

        var r = await service.ContinueLibraryStoryAsync(Guid.NewGuid());

        Assert.Equal(string.Empty, r.Response);
        Assert.False(r.LibraryAutoContinue);
    }

    [Fact]
    public async Task NonLibraryTurn_DoesNotSignalContinue()
    {
        // A legacy reply must never carry the autoplay-continue signal.
        var service = MakeService("library");
        var r = await service.GetResponseAsync(Guid.NewGuid(), "Բարև Արեգ։");
        Assert.False(r.LibraryAutoContinue);
    }

    // ── W4: post-end repeat cues ────────────────────────────────────

    /// <summary>Starts the default story and walks it to the ending
    /// turn (reflection delivered, session cleared, ended marker set).</summary>
    private async Task FinishDefaultStoryAsync(IChatService service, Guid deviceId)
    {
        var story = DefaultStory();
        await service.GetResponseAsync(deviceId, "հեքիաթ պատմիր");
        for (var i = 1; i < story.Segments.Count; i++)
        {
            await service.GetResponseAsync(deviceId, "հետո՞");
        }
        var ending = await service.GetResponseAsync(deviceId, "հետո՞");
        Assert.StartsWith(story.ReflectionText, ending.Response);
        Assert.Null(_tracker.GetCurrent(_conversationId));
    }

    [Theory]
    [InlineData("էլի")]
    [InlineData("նորից")]
    [InlineData("Էլի՛ պատմիր")]
    public async Task PostEnd_RepeatCue_StartsFreshApprovedStory_NoGpt(string repeatCue)
    {
        var service = MakeService("library");
        var deviceId = Guid.NewGuid();
        var story = DefaultStory();
        await FinishDefaultStoryAsync(service, deviceId);

        var result = await service.GetResponseAsync(deviceId, repeatCue);

        Assert.Equal(
            LibraryStoryCannedLines.StartLeadIn + "\n" + story.Segments[0].Text,
            result.Response);
        var session = _tracker.GetCurrent(_conversationId);
        Assert.NotNull(session);
        Assert.Equal(story.Id, session!.StoryId);          // default approved story
        Assert.NotEqual("anban-huri", session.StoryId);    // never the draft
        Assert.Equal(0, session.SegmentIndex);
        Assert.Equal(0, LegacyAiCalls());
        Assert.Equal(0, QaAiCalls());
    }

    [Theory]
    [InlineData("նորից")]
    [InlineData("էլի")]
    public async Task RepeatCue_WithoutRecentEnding_StaysLegacy_NoSession(string repeatCue)
    {
        // No story ever played in this conversation — a bare repeat
        // word has no story context and must not start anything.
        var service = MakeService("library");

        await service.GetResponseAsync(Guid.NewGuid(), repeatCue);

        Assert.True(LegacyAiCalls() > 0);
        Assert.Equal(0, QaAiCalls());
        Assert.Null(_tracker.GetCurrent(_conversationId));
    }

    // ── W4: broader start cues ──────────────────────────────────────

    [Theory]
    [InlineData("հեքիաթ կպատմե՞ս")]
    [InlineData("ուզում եմ հեքիաթ լսել")]
    [InlineData("արի մի հեքիաթ լսենք")]
    public async Task NewStartCues_StartDefaultApprovedStory(string request)
    {
        var service = MakeService("library");
        var story = DefaultStory();

        var result = await service.GetResponseAsync(Guid.NewGuid(), request);

        Assert.Equal(
            LibraryStoryCannedLines.StartLeadIn + "\n" + story.Segments[0].Text,
            result.Response);
        Assert.Equal(story.Id, _tracker.GetCurrent(_conversationId)!.StoryId);
        Assert.Equal(0, LegacyAiCalls());
        Assert.Equal(0, QaAiCalls());
    }

    [Fact]
    public async Task SentenceContainingHekiat_NotInFixedList_StaysLegacy()
    {
        // No broad substring matching: mentioning «հեքիաթ» is not a
        // request to start one.
        var service = MakeService("library");

        await service.GetResponseAsync(Guid.NewGuid(), "երեկ մի հեքիաթ կարդացի մայրիկիս հետ");

        Assert.True(LegacyAiCalls() > 0);
        Assert.Null(_tracker.GetCurrent(_conversationId));
    }

    [Fact]
    public async Task LegacyEngine_NewStartCue_StaysLegacy_NoSession()
    {
        var service = MakeService("legacy");

        await service.GetResponseAsync(Guid.NewGuid(), "հեքիաթ կպատմես");

        Assert.True(LegacyAiCalls() > 0);
        Assert.Equal(0, QaAiCalls());
        Assert.Null(_tracker.GetCurrent(_conversationId));
    }

    // ── RepeatCueDetector unit pins ─────────────────────────────────

    [Theory]
    [InlineData("էլի", true)]
    [InlineData("Նորի՛ց", true)]
    [InlineData("մեկ էլ", true)]
    [InlineData("նորից պատմիր", true)]
    [InlineData("ուրիշ հեքիաթ", true)]
    [InlineData("նորից եմ ուզում լսել այդ հեքիաթը", false)] // sentence, not a cue
    [InlineData("Բարև Արեգ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void RepeatCueDetector_MatchesWholeUtteranceOnly(string? input, bool expected)
    {
        Assert.Equal(expected, RepeatCueDetector.IsRepeatCue(input));
    }

    // ── StoryStartCueDetector unit pins ─────────────────────────────

    [Theory]
    [InlineData("հեքիաթ պատմիր", true)]
    [InlineData("Պատմիր հեքիաթ։", true)]
    [InlineData("մի հեքիաթ պատմիր", true)]
    [InlineData("tell me a story", true)]
    [InlineData("Tell me a story!", true)]
    [InlineData("հեքիաթ", false)]              // bare noun is not a request
    [InlineData("պատմիր ինձ արջուկի մասին հեքիաթ", false)] // rich request → legacy
    [InlineData("Բարև Արեգ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void StoryStartCueDetector_MatchesFixedRequestsOnly(string? input, bool expected)
    {
        Assert.Equal(expected, StoryStartCueDetector.IsStoryStartCue(input));
    }
}

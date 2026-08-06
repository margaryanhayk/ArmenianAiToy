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
/// End-to-end coverage for the Game Mode v2 multi-turn loop:
///   start game → child responds → continue (next round) → switch → stop.
/// Mirrors RiddleLoopIntegrationTests.
/// </summary>
public class GameLoopIntegrationTests
{
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly Guid _conversationId = Guid.NewGuid();
    private readonly IChatService _chatService;
    private readonly IAiChatClient _aiClient;

    public GameLoopIntegrationTests()
    {
        // Per-conversation cleanup only — mass Clear() would race with parallel tests.
        ChatService.GameSessions.TryRemove(_conversationId, out _);
        ChatService.RiddleSessions.TryRemove(_conversationId, out _);
        ChatService.PendingChoices.TryRemove(_conversationId, out _);
        ChatService.ActiveModes.TryRemove(_conversationId, out _);
        ChatService.StoryMemories.TryRemove(_conversationId, out _);

        _aiClient = Substitute.For<IAiChatClient>();
        var moderation = Substitute.For<IModerationService>();
        var conversations = Substitute.For<IConversationService>();
        var childService = Substitute.For<IChildService>();
        var logger = Substitute.For<ILogger<ChatService>>();

        var config = Substitute.For<IConfiguration>();
        config["SystemPrompt"].Returns("You are a test assistant.");

        moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(true, new List<string>()));

        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>())
            .Returns((Child?)null);

        var conversation = new Conversation
        {
            Id = _conversationId,
            DeviceId = _deviceId,
            StartedAt = DateTime.UtcNow
        };
        conversations.GetOrCreateActiveConversationAsync(Arg.Any<Guid>(), Arg.Any<Guid?>())
            .Returns(conversation);

        conversations.GetRecentMessagesAsync(Arg.Any<Guid>(), Arg.Any<int>())
            .Returns(new List<(string Role, string Content)>());

        conversations.AddMessageAsync(
            Arg.Any<Guid>(), Arg.Any<MessageRole>(), Arg.Any<string>(), Arg.Any<SafetyFlag>())
            .Returns(callInfo => new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = callInfo.ArgAt<Guid>(0),
                Role = callInfo.ArgAt<MessageRole>(1),
                Content = callInfo.ArgAt<string>(2),
                Timestamp = DateTime.UtcNow,
                SafetyFlag = callInfo.ArgAt<SafetyFlag>(3)
            });

        _chatService = new ChatService(
            _aiClient, moderation, conversations, childService, config, logger,
            new StoryChoiceCoherenceGate());
    }

    private const string GameWithBlock =
        "\u053e\u0561\u0583 \u057f\u0561\u0576\u0584 \u0574\u056b\u0561\u057d\u056b\u0576\u0589 \u0544\u0565\u056f, \u0565\u0580\u056f\u0578\u0582, \u0565\u0580\u0565\u0584\u0589\n---\nGAME_TYPE:animal_sound\nGAME_DIFFICULTY:1";

    private const string ColorGameWithBlock =
        "\u0533\u057f\u056b\u0580 \u0574\u056b \u056f\u0561\u0580\u0574\u056b\u0580 \u0562\u0561\u0576\u0589\n---\nGAME_TYPE:count_to\nGAME_DIFFICULTY:1";

    [Fact]
    public async Task NewGame_StoresSessionAndStripsBlock()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);

        var result = await _chatService.GetResponseAsync(_deviceId, "let's play");

        Assert.Equal("game", result.Mode);
        Assert.DoesNotContain("GAME_TYPE", result.Response);
        Assert.DoesNotContain("---", result.Response);
        Assert.True(ChatService.GameSessions.TryGetValue(_conversationId, out var state));
        Assert.NotNull(state!.CurrentRound);
        Assert.Equal("animal_sound", state.CurrentRound!.GameType);
        Assert.Equal(1, state.CurrentRound.Difficulty);
        Assert.Equal(0, state.CurrentRound.TurnsCompleted);
    }

    [Fact]
    public async Task NewGame_PromptIncludesNewGameDirective()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);

        await _chatService.GetResponseAsync(_deviceId, "let's play");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("GAME_TURN_KIND: new_game")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task ChildResponse_TriggersContinueAndBumpsTurns()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "let's play");

        _aiClient.ClearReceivedCalls();
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0531\u057a\u0580\u0565\u055b\u057d\u0589 \u0540\u056b\u0574\u0561\u055d \u0561\u057e\u0565\u056c\u056b \u0561\u0580\u0561\u0563\u0589");

        await _chatService.GetResponseAsync(_deviceId, "ok");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("GAME_TURN_KIND: continue")
                && s.Contains("animal_sound")),
            Arg.Any<List<(string, string)>>());

        Assert.True(ChatService.GameSessions.TryGetValue(_conversationId, out var state));
        Assert.Equal(1, state!.CurrentRound!.TurnsCompleted);
        Assert.Equal(1, state.CurrentRound.Difficulty);
    }

    [Fact]
    public async Task DifficultyBumps_AfterTwoContinueTurns()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "let's play");

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0531\u057a\u0580\u0565\u055b\u057d\u0589 \u0540\u056b\u0574\u0561\u0589");

        await _chatService.GetResponseAsync(_deviceId, "ok"); // turns=1, diff=1
        await _chatService.GetResponseAsync(_deviceId, "ok"); // turns=2, diff bumps to 2

        Assert.True(ChatService.GameSessions.TryGetValue(_conversationId, out var state));
        Assert.Equal(2, state!.CurrentRound!.TurnsCompleted);
        Assert.Equal(2, state.CurrentRound.Difficulty);
    }

    [Fact]
    public async Task SwitchGame_ClearsRoundAndKeepsAvoidList()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "let's play");

        _aiClient.ClearReceivedCalls();
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(ColorGameWithBlock);

        await _chatService.GetResponseAsync(_deviceId, "another game");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("GAME_TURN_KIND: switch_game")
                && s.Contains("AVOID")
                && s.Contains("animal_sound")),
            Arg.Any<List<(string, string)>>());

        Assert.True(ChatService.GameSessions.TryGetValue(_conversationId, out var state));
        Assert.NotNull(state!.CurrentRound);
        Assert.Equal("count_to", state.CurrentRound!.GameType);
        // Both types are tracked in RecentGameTypes (animal_sound, count_to).
        Assert.Contains("animal_sound", state.RecentGameTypes);
        Assert.Contains("count_to", state.RecentGameTypes);
    }

    [Fact]
    public async Task StopGame_TriggersStopDirectiveAndClearsRound()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "let's play");

        _aiClient.ClearReceivedCalls();
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u055b\u057e, \u056c\u0561\u057e \u056d\u0561\u0572 \u0567\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "\u0562\u0561\u057e \u0567"); // բավ է

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("GAME_TURN_KIND: stop_game")),
            Arg.Any<List<(string, string)>>());

        Assert.True(ChatService.GameSessions.TryGetValue(_conversationId, out var state));
        Assert.Null(state!.CurrentRound);
    }

    [Fact]
    public async Task ContinueDirective_IncludesRoundProgressionHint()
    {
        // v3: round-counter drives a per-turn playful hint. Round 3 should
        // tell the model to switch the SUBTYPE inside the same game type.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "let's play");

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0531\u057a\u0580\u0565\u055b\u057d\u0589 \u0540\u056b\u0574\u0561\u0589");

        await _chatService.GetResponseAsync(_deviceId, "ok");  // turns=1
        await _chatService.GetResponseAsync(_deviceId, "ok");  // turns=2

        _aiClient.ClearReceivedCalls();
        await _chatService.GetResponseAsync(_deviceId, "ok");  // turns=3 → switch SUBTYPE hint

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("GAME_TURN_KIND: continue")
                && s.Contains("switch the SUBTYPE")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task ContinueDirective_Round2_SwitchesSubtypeFromRound1()
    {
        // Regression for F-Game-1: the Round 2 runtime hint previously read
        // "Same subtype is OK here, but vary the specific item", which
        // directly contradicted the VARIETY POLICY and the BAD/GOOD pair
        // in GameModeInstruction (both require a different subtype on
        // every CONTINUE turn, including the first one). The Round 2
        // directive must now instruct the model to switch the subtype.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "let's play");

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0531\u057a\u0580\u0565\u055b\u057d\u0589 \u0540\u056b\u0574\u0561\u0589");

        await _chatService.GetResponseAsync(_deviceId, "ok");  // turns=1

        _aiClient.ClearReceivedCalls();
        await _chatService.GetResponseAsync(_deviceId, "ok");  // turns=2 → Round 2 hint

        // Assert the Round-2-specific wording that distinguishes this arm
        // from Round 1 ("set a friendly fun pace…") and Round 3+
        // ("switch the SUBTYPE inside this game type"): the "Do not repeat
        // the subtype you used in Round 1" phrasing is unique to Round 2.
        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("GAME_TURN_KIND: continue")
                && s.Contains("Do not repeat the subtype you used in Round 1")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task ContinueDirective_RotateCelebration()
    {
        // v6: the continue directive requires an HONEST reaction; when it
        // does celebrate, the phrase must rotate (never the one used last
        // turn).
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "let's play");

        _aiClient.ClearReceivedCalls();
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0531\u057a\u0580\u0565\u055b\u057d\u0589");

        await _chatService.GetResponseAsync(_deviceId, "ok");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("REACT HONESTLY")
                && s.Contains("rotate the phrase")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task RetryPath_PreservesGameTailBlock_AndStoresRound()
    {
        // Regression for the Game mirror of F-Rid-1: the quality-gate
        // retry path must re-run GameTailBlockParser and update
        // GameSessions when the retry carries the GAME_TYPE block.
        // Before the fix, a game_too_long/latin_run retry orphaned the
        // block — no round was stored, every later turn re-routed to
        // new_game, and Stop was unreachable.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(
                // Initial response — trips latin_run (4+ Latin letters),
                // no usable tail block.
                "Armenian text here mixed with english prose sentence.",
                // Retry response — clean opener + proper tail block.
                GameWithBlock);

        var r = await _chatService.GetResponseAsync(_deviceId, "let's play");

        Assert.Equal("game", r.Mode);
        Assert.True(ChatService.GameSessions.TryGetValue(_conversationId, out var state));
        Assert.NotNull(state!.CurrentRound);
        Assert.Equal("animal_sound", state.CurrentRound!.GameType);
        // The visible reply must not leak the block.
        Assert.DoesNotContain("GAME_TYPE", r.Response);
    }

    [Fact]
    public async Task OffTaxonomyGameType_IsStrippedButStoresNoRound()
    {
        // The whitelist is enforced at the storage site: a model-invented
        // type is stripped from the visible reply but never becomes a
        // round (pre-fix it was stored, echoed into later directives, and
        // written into the AVOID list).
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Խաղանք։\n---\nGAME_TYPE:memory_game\nGAME_DIFFICULTY:2");

        var r = await _chatService.GetResponseAsync(_deviceId, "let's play");

        Assert.DoesNotContain("memory_game", r.Response);
        var has = ChatService.GameSessions.TryGetValue(_conversationId, out var state);
        Assert.True(!has || state!.CurrentRound is null);
    }

    [Fact]
    public async Task ModelChosenDifficulty_IsForcedTo1_OnFreshRound()
    {
        // The directive asks for "target: 1"; a model answering
        // GAME_DIFFICULTY:3 must not pin the child at maximum — the
        // ladder is ours to grow.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Հնչեցրու կատվի ձայնը։\n---\nGAME_TYPE:animal_sound\nGAME_DIFFICULTY:3");

        await _chatService.GetResponseAsync(_deviceId, "let's play");

        Assert.True(ChatService.GameSessions.TryGetValue(_conversationId, out var state));
        Assert.Equal(1, state!.CurrentRound!.Difficulty);
    }

    [Fact]
    public async Task LeavingGameForStory_ClearsRound_KeepsVarietyMemory()
    {
        // H4 regression: a story detour must abandon the in-flight round.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "let's play");
        Assert.True(ChatService.GameSessions.TryGetValue(_conversationId, out var mid));
        Assert.NotNull(mid!.CurrentRound);

        // Story turn — leaves Game.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Մի անգամ մի նապաստակ կար։");
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        Assert.True(ChatService.GameSessions.TryGetValue(_conversationId, out var after));
        Assert.Null(after!.CurrentRound);
        // Variety memory survives the detour.
        Assert.Contains("animal_sound", after.RecentGameTypes);
    }

    [Fact]
    public async Task LeavingGameForRiddle_ClearsRound_SoNextPlayStartsFresh()
    {
        // H4 regression, riddle path: pre-fix the clearing only ran on the
        // Story/None branch — a riddle detour left the stale round alive,
        // and a later «խաղանք» was read as CONTINUE of it instead of
        // starting a new game.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "let's play");

        // Riddle turn — leaves Game.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Կռահի՞ր, թե ինչ եմ մտածել։");
        await _chatService.GetResponseAsync(_deviceId, "give me a riddle");

        Assert.True(ChatService.GameSessions.TryGetValue(_conversationId, out var after));
        Assert.Null(after!.CurrentRound);

        // An explicit «խաղանք» now opens a NEW game, not round 2 of the
        // abandoned one.
        _aiClient.ClearReceivedCalls();
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "խաղանք");
        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("GAME_TURN_KIND: new_game")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task StopWord_WithNoActiveRound_ProducesStopTurn_NotNewGame()
    {
        // H1 regression: pre-fix, a stop word with no round fell through
        // to StartNew — the child said «բավ է» and got a NEW game.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Լա՛վ, լավ խաղ էր։");

        // No round exists; route into Game via an explicit game word plus
        // the stop phrase (mirror of the real transcript «չեմ ուզում խաղալ»).
        await _chatService.GetResponseAsync(_deviceId, "չեմ ուզում խաղալ");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("GAME_TURN_KIND: stop_game")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task ContinueDirective_YesNoRound_CarriesAnswerClassification()
    {
        // H2: on a yes_no_silly round the directive must carry the
        // deterministic yes/no classification of the child's answer so
        // the model can react honestly instead of celebrating blindly.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Ասա՛՝ ձուկը թռչու՞մ է։\n---\nGAME_TYPE:yes_no_silly\nGAME_DIFFICULTY:1");
        await _chatService.GetResponseAsync(_deviceId, "let's play");

        _aiClient.ClearReceivedCalls();
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Հա՛, ճիշտ է՝ չի թռչում։");
        await _chatService.GetResponseAsync(_deviceId, "ոչ");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("classified as NO")
                && s.Contains("REACT HONESTLY")),
            Arg.Any<List<(string, string)>>());
    }

    // ─────────────────────────────────────────────────────────────────────
    // make_it_small — the fourth taxonomy member (2026-08-06). The v6 loop
    // machinery is type-agnostic; these pin that it actually covers the new
    // type rather than assuming it does.
    // ─────────────────────────────────────────────────────────────────────

    private const string MakeItSmallWithBlock =
        "Փոքրացրո՛ւ՝ կատու։\n---\nGAME_TYPE:make_it_small\nGAME_DIFFICULTY:1";

    [Fact]
    public async Task MakeItSmall_IsAcceptedByTheWhitelist_AndStoresARound()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(MakeItSmallWithBlock);

        var result = await _chatService.GetResponseAsync(_deviceId, "let's play");

        Assert.Equal("game", result.Mode);
        Assert.DoesNotContain("GAME_TYPE", result.Response);
        Assert.DoesNotContain("make_it_small", result.Response);
        Assert.True(ChatService.GameSessions.TryGetValue(_conversationId, out var state));
        Assert.Equal("make_it_small", state!.CurrentRound!.GameType);
        Assert.Equal(0, state.CurrentRound.TurnsCompleted);
    }

    [Fact]
    public async Task MakeItSmall_ModelChosenDifficulty_IsForcedTo1_OnFreshRound()
    {
        // The v6 difficulty override is type-agnostic — verify, don't assume.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Փոքրացրո՛ւ՝ կատու։\n---\nGAME_TYPE:make_it_small\nGAME_DIFFICULTY:3");

        await _chatService.GetResponseAsync(_deviceId, "let's play");

        Assert.True(ChatService.GameSessions.TryGetValue(_conversationId, out var state));
        Assert.Equal(1, state!.CurrentRound!.Difficulty);
    }

    [Fact]
    public async Task MakeItSmall_ChildAnswer_ContinuesInsideTheSameType()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(MakeItSmallWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "let's play");

        _aiClient.ClearReceivedCalls();
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Ապրե՛ս՝ կատվիկ։ Հիմա՝ արջ։");
        await _chatService.GetResponseAsync(_deviceId, "կատվիկ");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("GAME_TURN_KIND: continue")
                && s.Contains("make_it_small")
                && s.Contains("REACT HONESTLY")),
            Arg.Any<List<(string, string)>>());

        Assert.True(ChatService.GameSessions.TryGetValue(_conversationId, out var state));
        Assert.Equal(1, state!.CurrentRound!.TurnsCompleted);
    }

    [Fact]
    public async Task MakeItSmall_StopWord_EndsTheGame_AndClearsTheRound()
    {
        // Stop-anytime must cover the new type — the child gets a goodbye,
        // not another base word.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(MakeItSmallWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "let's play");

        _aiClient.ClearReceivedCalls();
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Լա՛վ, լավ խաղ էր։");
        await _chatService.GetResponseAsync(_deviceId, "բավ է");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("GAME_TURN_KIND: stop_game")),
            Arg.Any<List<(string, string)>>());

        Assert.True(ChatService.GameSessions.TryGetValue(_conversationId, out var state));
        Assert.Null(state!.CurrentRound);
    }

    [Fact]
    public async Task MakeItSmall_LeavingGameForStory_ClearsRound_KeepsVarietyMemory()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(MakeItSmallWithBlock);
        await _chatService.GetResponseAsync(_deviceId, "let's play");

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Մի անգամ մի նապաստակ կար։");
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        Assert.True(ChatService.GameSessions.TryGetValue(_conversationId, out var after));
        Assert.Null(after!.CurrentRound);
        Assert.Contains("make_it_small", after.RecentGameTypes);
    }

    [Fact]
    public async Task MakeItSmall_RetryPath_PreservesTheGameTailBlock()
    {
        // Game mirror of F-Rid-1, on the new type: a retried opener must
        // still land its round instead of losing the metadata block.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(
                "Armenian text here mixed with english prose sentence.",
                MakeItSmallWithBlock);

        var r = await _chatService.GetResponseAsync(_deviceId, "let's play");

        Assert.Equal("game", r.Mode);
        Assert.True(ChatService.GameSessions.TryGetValue(_conversationId, out var state));
        Assert.Equal("make_it_small", state!.CurrentRound!.GameType);
        Assert.DoesNotContain("GAME_TYPE", r.Response);
    }

    [Fact]
    public async Task NearMissGameType_IsStillRejected_ByTheClosedWhitelist()
    {
        // The whitelist is exact-match: a plausible-looking neighbour of a
        // real type is not a real type.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Խաղանք։\n---\nGAME_TYPE:make_small\nGAME_DIFFICULTY:1");

        var r = await _chatService.GetResponseAsync(_deviceId, "let's play");

        Assert.DoesNotContain("make_small", r.Response);
        var has = ChatService.GameSessions.TryGetValue(_conversationId, out var state);
        Assert.True(!has || state!.CurrentRound is null);
    }

    [Fact]
    public async Task GameResponse_NeverCarriesStoryChoices()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(GameWithBlock);

        var result = await _chatService.GetResponseAsync(_deviceId, "let's play");

        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
        Assert.Null(result.StorySessionId);
    }
}

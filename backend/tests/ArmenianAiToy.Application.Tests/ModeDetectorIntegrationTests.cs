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
/// Integration tests verifying that ModeDetector is wired into ChatService
/// and that non-Story modes correctly skip story-specific treatment.
/// </summary>
public class ModeDetectorIntegrationTests
{
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly Guid _conversationId = Guid.NewGuid();
    private readonly IChatService _chatService;
    private readonly IAiChatClient _aiClient;
    private readonly ILogger<ChatService> _logger;

    public ModeDetectorIntegrationTests()
    {
        _aiClient = Substitute.For<IAiChatClient>();
        var moderation = Substitute.For<IModerationService>();
        var conversations = Substitute.For<IConversationService>();
        var childService = Substitute.For<IChildService>();
        _logger = Substitute.For<ILogger<ChatService>>();

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
            _aiClient, moderation, conversations, childService, config, _logger);
    }

    [Fact]
    public async Task StoryTrigger_StillActivatesStoryMode()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Աղվեսը քայլեց։\n---\nCHOICE_A:Օգնել աղվեսին\nCHOICE_B:Փախչել");

        var result = await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        Assert.NotNull(result.ChoiceA);
        Assert.NotNull(result.ChoiceB);
        Assert.NotNull(result.StorySessionId);
    }

    [Fact]
    public async Task CalmTriggerMidStory_SkipsStoryTreatment()
    {
        // Turn 1: Start a story — establishes pending choices.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Աղվեսը քայլեց։\n---\nCHOICE_A:Օգնել աղվեսին\nCHOICE_B:Փախչել");
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        // Turn 2: Child says "I'm tired" — should exit story mode.
        // AI returns plain text (no choice block). If story mode were active,
        // fallback choice generation would fire and add choices. With calm
        // detection, story mode is false and the response stays choice-free.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580, \u0570\u0561\u0576\u0563\u057d\u057f\u0561\u0581\u056b\u0580\u0589"); // Լdelays delays delays

        var result = await _chatService.GetResponseAsync(_deviceId, "i'm tired");

        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
        Assert.Null(result.StorySessionId);
    }

    [Fact]
    public async Task CalmTrigger_DoesNotInjectStoryPrompt()
    {
        // Turn 1: Start a story.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("A fox.\n---\nCHOICE_A:Գնալ ձախ\nCHOICE_B:Գնալ աջ");
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        // Turn 2: "good night" — calm mode.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "good night");

        // Verify the system prompt sent to AI does NOT contain the story instruction.
        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => !s.Contains("MANDATORY OUTPUT FORMAT")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task CalmTrigger_InjectsCalmPrompt()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0531\u0574\u0565\u0576 \u056b\u0576\u0579 \u056c\u0561\u057e \u0567\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "i'm sleepy");

        // Verify the system prompt contains the calm instruction marker.
        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("MODE: CALM / BEDTIME")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task CalmTrigger_PromptForbidsChoiceBlock()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "good night");

        // Verify the calm prompt explicitly bans CHOICE_A/CHOICE_B.
        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("Do NOT include a CHOICE_A / CHOICE_B block")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task CalmTrigger_PromptForbidsQuestions()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "bedtime");

        // Verify the calm prompt explicitly bans questions.
        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("Do NOT ask any questions")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task CalmArmenianTrigger_InjectsCalmPrompt()
    {
        // Armenian "գիշեր բարի" (good night).
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "\u0563\u056b\u0577\u0565\u0580 \u0562\u0561\u0580\u056b");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("MODE: CALM / BEDTIME")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task CalmMode_NoFormatReminderInjected()
    {
        // Calm mode must NOT inject the story format reminder into history.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "sleep now");

        // Verify the history passed to AI does NOT contain the format reminder.
        await _aiClient.Received().GetCompletionAsync(
            Arg.Any<string>(),
            Arg.Is<List<(string, string)>>(h =>
                !h.Any(m => m.Item2.Contains("FORMAT REMINDER"))));
    }

    [Fact]
    public async Task StoryMode_FormatReminder_UsesRealNewlinesNotLiteralEscapes()
    {
        // Regression for F-PG-2: the Story FORMAT REMINDER used to ship the
        // two-character sequence backslash-n (C# source "\\n") to the model,
        // meaning GPT-4o saw "---\nCHOICE_A:" as prose describing a separator
        // rather than a shaped example with real line breaks. Fix replaces
        // the escape with a real newline. Pin both sides so a future typo
        // that reintroduces "\\n" fails this test distinctly.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Story.\n---\nCHOICE_A:Go left\nCHOICE_B:Go right");

        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Any<string>(),
            Arg.Is<List<(string, string)>>(h =>
                h.Any(m =>
                    m.Item2.Contains("FORMAT REMINDER")
                    && m.Item2.Contains("---\nCHOICE_A")        // real newline
                    && m.Item2.Contains("CHOICE_A:<action>\nCHOICE_B:<action>")
                    && !m.Item2.Contains(@"---\nCHOICE_A"))));  // no literal backslash-n
    }

    [Fact]
    public async Task StoryMode_SystemPrompt_DocumentsStoryMemorySchema()
    {
        // Regression for F-PG-1: the Story system prompt used to require
        // the model to emit a STORY_MEMORY block (via the FORMAT REMINDER
        // and the runtime memory re-injection at ChatService.cs:1189-1202)
        // without ever documenting the schema in StoryChoiceInstruction.
        // StoryMemoryParser.cs:63-70 silently drops any key that is not
        // one of {character, place, object, situation, mood} (lowercase),
        // so an undocumented schema causes invented keys to vanish and
        // cross-turn continuity to weaken. Fix: append an explicit
        // "STORY_MEMORY BLOCK — STRICT SCHEMA" section to the Story
        // prompt enumerating the five allowed lowercase keys.
        // Pin the presence of the schema section and each key so a
        // future edit that renames the parser's bucket or drops the
        // prompt section fails this test distinctly.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Աղվեսը քայլեց։\n---\nCHOICE_A:Գնալ ձախ\nCHOICE_B:Գնալ աջ");

        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s =>
                s.Contains("STORY_MEMORY BLOCK — STRICT SCHEMA")
                && s.Contains("STORY_MEMORY:")
                && s.Contains("character:<short Armenian phrase>")
                && s.Contains("place:<short Armenian phrase>")
                && s.Contains("object:<short Armenian phrase>")
                && s.Contains("situation:<short Armenian phrase>")
                && s.Contains("mood:<short Armenian phrase>")),
            Arg.Any<List<(string, string)>>());

        // Anti-tautology: the Story branch must have fired on this turn.
        // "MANDATORY OUTPUT FORMAT" is the opening line of
        // StoryChoiceInstruction and is appended to the system prompt
        // only in the Story branch. Without this, a routing regression
        // that sent the turn to any other mode, or a future prompt
        // reshape that dropped the Story branch's opener, would silently
        // pass the positive assertions above.
        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("MANDATORY OUTPUT FORMAT")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task StoryMode_SystemPrompt_BansTimeFrameOpeners()
    {
        // Regression for F-PS-1: the Story system prompt carries two
        // load-bearing banned-opener invariants —
        // (1) OPENING VARIETY — STRICT RULE at StoryChoiceInstruction
        //     (ChatService.cs:~173), which flags time/weather-frame
        //     openers as OVERUSED and enumerates the allowed-only-when-
        //     called-for openers "Մի անգամ..." / "Մի գեղեցիկ [X]
        //     օր/առավոտ/երեկո...".
        // (2) FINAL STORY CHECK first bullet at ChatService.cs:~336:
        //     'Opening is NOT "Մի անգամ…" or "Մի գեղեցիկ X
        //     օր/առավոտ/երեկո…" unless the previous turn called for
        //     one.'
        // Both surfaces are currently unpinned. A future prompt
        // refactor that weakened the STRICT RULE framing of OPENING
        // VARIETY, dropped the FINAL STORY CHECK bullet, or removed
        // either banned-opener exemplar would leave the test suite
        // green while silently eroding opener diversity. This test
        // pins the presence of both invariants as substrings of the
        // Story-branch system prompt so that drift fails distinctly.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Աղվեսը քայլեց։\n---\nCHOICE_A:Գնալ ձախ\nCHOICE_B:Գնալ աջ");

        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s =>
                s.Contains("OPENING VARIETY — STRICT RULE")
                && s.Contains("Time/weather-frame openers are")
                && s.Contains("Opening is NOT")
                && s.Contains("Մի անգամ")
                && s.Contains("Մի գեղեցիկ")),
            Arg.Any<List<(string, string)>>());

        // Anti-tautology: the Story branch must have fired on this turn.
        // "MANDATORY OUTPUT FORMAT" is the opening line of
        // StoryChoiceInstruction and is appended to the system prompt
        // only in the Story branch. Without this, a routing regression
        // that sent the turn to any other mode, or a future prompt
        // reshape that dropped the Story branch's opener, would silently
        // pass the positive assertions above.
        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("MANDATORY OUTPUT FORMAT")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task ExplicitChoiceSelection_AlwaysStoryMode()
    {
        // Turn 1: Start a story.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("A fox.\n---\nCHOICE_A:Գնալ ձախ\nCHOICE_B:Գնալ աջ");
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        // Turn 2: Explicit selectedChoice=A bypasses ModeDetector entirely.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Աղվեսը գնաց ձախ։\n---\nCHOICE_A:Բացել դուռ\nCHOICE_B:Մագլցել ծառ");

        var result = await _chatService.GetResponseAsync(
            _deviceId, "A", selectedChoice: "A");

        Assert.NotNull(result.ChoiceA);
        Assert.NotNull(result.ChoiceB);
        Assert.NotNull(result.StorySessionId);
    }

    [Fact]
    public async Task StoryAboutSleeping_StaysInStoryMode()
    {
        // "tell me a story about sleeping" has both story and calm cues,
        // but story-cue gating in ModeDetector ensures Story wins.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Արջը քնեց։\n---\nCHOICE_A:Արթնացնել արջ\nCHOICE_B:Թողնել քնի");

        var result = await _chatService.GetResponseAsync(
            _deviceId, "tell me a story about sleeping");

        Assert.NotNull(result.ChoiceA);
        Assert.NotNull(result.ChoiceB);
    }

    [Fact]
    public async Task CuriosityMidStory_SkipsStoryTreatment()
    {
        // Turn 1: Start a story.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("A fox.\n---\nCHOICE_A:Գնալ ձախ\nCHOICE_B:Գնալ աջ");
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        // Turn 2: Off-topic question — curiosity window.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("The sky is blue because of how light scatters.");

        var result = await _chatService.GetResponseAsync(
            _deviceId, "why is the sky blue");

        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
        Assert.Null(result.StorySessionId);
    }

    [Fact]
    public async Task NeutralMessageMidStory_ContinuesStory()
    {
        // Turn 1: Start a story.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("A fox.\n---\nCHOICE_A:Գնալ ձախ\nCHOICE_B:Գնալ աջ");
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        // Turn 2: Neutral "ok" with active session — story continues.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Fox ran.\n---\nCHOICE_A:Հետապնդել\nCHOICE_B:Թաքնվել");

        var result = await _chatService.GetResponseAsync(_deviceId, "ok");

        Assert.NotNull(result.ChoiceA);
        Assert.NotNull(result.ChoiceB);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Calm quality gate retry path (end-to-end)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CalmRetry_QuestionInFirstResponse_ReturnsCleanSecondResponse()
    {
        // First AI call returns a response with a question mark (violates calm rules).
        // Quality gate fires calm_question → retry. Second AI call returns clean text.
        var badResponse = "\u0531\u0579\u0584\u0565\u0580\u0564 \u0583\u0561\u056f\u056b\u0580, \u056b\u0576\u0579 \u0565\u057d \u0578\u0582\u0566\u0578\u0582\u0574?";
        var cleanResponse = "\u0531\u0579\u0584\u0565\u0580\u0564 \u0583\u0561\u056f\u056b\u0580, \u0561\u0574\u0565\u0576 \u056b\u0576\u0579 \u056c\u0561\u057e \u0567\u0580\u0589";

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(badResponse, cleanResponse);

        var result = await _chatService.GetResponseAsync(_deviceId, "good night");

        Assert.Equal(cleanResponse, result.Response);
        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
    }

    [Fact]
    public async Task CalmRetry_ExclamationInFirstResponse_ReturnsCleanSecondResponse()
    {
        var badResponse = "\u0548\u0582\u0580\u0561\u056d \u0565\u0576\u0584 \u057e\u0561\u0572\u0568!";
        var cleanResponse = "\u0531\u0574\u0565\u0576 \u056b\u0576\u0579 \u056c\u0561\u057e \u0567\u0580\u0589";

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(badResponse, cleanResponse);

        var result = await _chatService.GetResponseAsync(_deviceId, "i'm tired");

        Assert.Equal(cleanResponse, result.Response);
    }

    [Fact]
    public async Task CalmRetry_LogsRetryReason()
    {
        var badResponse = "\u053c\u0561\u057e \u0567\u0580?";
        var cleanResponse = "\u053c\u0561\u057e \u0567\u0580\u0589";

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(badResponse, cleanResponse);

        await _chatService.GetResponseAsync(_deviceId, "kpnem");

        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Quality gate retry triggered")
                && o.ToString()!.Contains("calm_question")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task CalmRetry_AiCalledTwice()
    {
        var badResponse = "\u053c\u0561\u057e \u0567\u0580!";
        var cleanResponse = "\u053c\u0561\u057e \u0567\u0580\u0589";

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(badResponse, cleanResponse);

        await _chatService.GetResponseAsync(_deviceId, "bedtime");

        // AI should be called exactly twice: initial + retry.
        await _aiClient.Received(2).GetCompletionAsync(
            Arg.Any<string>(), Arg.Any<List<(string, string)>>());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Curiosity Window
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CuriosityTrigger_InjectsCuriosityPrompt()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567\u0589");

        await _chatService.GetResponseAsync(_deviceId, "why is the sky blue");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("MODE: CURIOSITY WINDOW")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task CuriosityArmenianTrigger_InjectsCuriosityPrompt()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0541\u0575\u0578\u0582\u0576\u0568 \u057d\u057a\u056b\u057f\u0561\u056f \u0567\u0589");

        // Armenian "ինչու" (why)
        await _chatService.GetResponseAsync(_deviceId,
            "\u056b\u0576\u0579\u0578\u0582 \u0567 \u0571\u0575\u0578\u0582\u0576\u0568 \u057d\u057a\u056b\u057f\u0561\u056f");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("MODE: CURIOSITY WINDOW")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task CuriosityMidStory_PreservesPendingChoices()
    {
        // Turn 1: Start a story → establishes pending choices.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Աղվեսը քայլեց։\n---\nCHOICE_A:Օգնել աղվեսին\nCHOICE_B:Փախչել");
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        // Turn 2: Curiosity detour — choices should be preserved.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567\u0589");
        await _chatService.GetResponseAsync(_deviceId, "why is the sky blue");

        // Verify pending choices still exist for this conversation.
        Assert.True(ChatService.PendingChoices.ContainsKey(_conversationId));
    }

    [Fact]
    public async Task CuriosityMidStory_StoryResumesOnNextTurn()
    {
        // Turn 1: Start a story.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Աղվեսը քայլեց։\n---\nCHOICE_A:Օգնել աղվեսին\nCHOICE_B:Փախչել");
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        // Turn 2: Curiosity detour.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567\u0589");
        await _chatService.GetResponseAsync(_deviceId, "why is the sky blue");

        // Turn 3: Neutral message — story should resume because preserved choices
        // make hasActiveStorySession true, so ModeDetector returns Story.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Աղվեսը արագ վազեց։\n---\nCHOICE_A:Հետապնդել\nCHOICE_B:Թաքնվել");

        var result = await _chatService.GetResponseAsync(_deviceId, "ok");

        Assert.NotNull(result.ChoiceA);
        Assert.NotNull(result.ChoiceB);
        Assert.NotNull(result.StorySessionId);
    }

    [Fact]
    public async Task StoryPath_UnknownNormalizedGuess_InjectsPreviousChoiceUnclear()
    {
        // Regression for F-PG-3: the Story pipeline carries a deliberate
        // semantic rename from normalizer vocabulary to prompt vocabulary —
        // ChoiceNormalizer.Normalize returns one of {"option_a", "option_b",
        // "unknown"}; the Story branch at ChatService.cs:1196-1210 maps
        // the "unknown" case (normalizedChoice is null, pending unexpired,
        // not preserved-across-Curiosity) to an injected
        // `previous_story_choice: unclear` hint. That translation is
        // intentional but currently unpinned — a future edit that swapped
        // "unclear" for "unknown" in the injection, or that renamed the
        // normalizer's "unknown" bucket to something else and broke the
        // bridging is-null check, would leave the test suite green.
        //
        // Turn 1: seed a Story with tail block so PendingChoices populates
        //         with options "Օգնել աղվեսին" / "Փախչել".
        // Turn 2: send a neutral non-matching input ("maybe"). It is NOT
        //         a Curiosity/Calm/Game/Riddle trigger, NOT a positional
        //         keyword, NOT either option's label. ChoiceNormalizer
        //         returns "unknown"; normalizedChoice stays null; pending
        //         is unexpired and not detour-preserved; the else-if at
        //         ChatService.cs:1197-1202 fires.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Աղվեսը քայլեց։\n---\nCHOICE_A:Օգնել աղվեսին\nCHOICE_B:Փախչել");
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Աղվեսը արագ վազեց։\n---\nCHOICE_A:Հետապնդել\nCHOICE_B:Թաքնվել");
        _aiClient.ClearReceivedCalls();

        await _chatService.GetResponseAsync(_deviceId, "maybe");

        // Positive: the translated hint is present on the wire with the
        // exact injection format used at ChatService.cs:1209. The format
        // "previous_story_choice: unclear" (colon + space + word) appears
        // ONLY at the injection site — the prompt body uses a different
        // descriptive shape ('previous_story_choice is "unclear"') — so
        // this substring unambiguously identifies the runtime translation.
        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("previous_story_choice: unclear")),
            Arg.Any<List<(string, string)>>());

        // Anti-tautology: the Story branch must have fired on this turn.
        // "MANDATORY OUTPUT FORMAT" is the opening line of
        // StoryChoiceInstruction and is appended to the system prompt
        // only in the Story branch. Without this, a routing regression
        // that sent the turn to any other mode, or a future prompt
        // reshape that dropped the Story branch's opener, would silently
        // pass the positive assertion above.
        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("MANDATORY OUTPUT FORMAT")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task ResumeAfterCuriosityDetour_DoesNotInjectUnclearHint()
    {
        // Regression for F-CB-1: after a Story → Curiosity detour, a neutral
        // resume turn ("ok") used to inject `previous_story_choice: unclear`
        // into the Story system prompt, which in turn told the model NOT to
        // emit CHOICE_A/CHOICE_B and framed the child as having failed to
        // answer. The Curiosity-preservation site now marks the PendingChoice
        // with PreservedAcrossCuriosityDetour=true so the Story branch skips
        // the unclear-injection guard on the resume turn.
        //
        // Turn 1: seed a story so PendingChoices is populated.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Աղվեսը քայլեց։\n---\nCHOICE_A:Օգնել աղվեսին\nCHOICE_B:Փախչել");
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        // Turn 2: Curiosity detour preserves the PendingChoice with the flag set.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567\u0589");
        await _chatService.GetResponseAsync(_deviceId, "why is the sky blue");

        // Turn 3: neutral resume. ModeDetector returns Story (active session).
        // The Story system prompt for this turn must NOT carry the unclear hint.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Աղվեսը արագ վազեց։\n---\nCHOICE_A:Հետապնդել\nCHOICE_B:Թաքնվել");
        _aiClient.ClearReceivedCalls();

        await _chatService.GetResponseAsync(_deviceId, "ok");

        // Negative assertion: the unclear hint must be absent on the resume turn.
        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => !s.Contains("previous_story_choice: unclear")),
            Arg.Any<List<(string, string)>>());

        // Anti-tautology guard: the Story branch must actually have fired
        // on this turn. "MANDATORY OUTPUT FORMAT — READ THIS FIRST" is the
        // opening line of StoryChoiceInstruction and is appended to the
        // system prompt only in the Story branch. Without this the negative
        // assertion above would silently pass if Turn 3 were routed to any
        // non-Story branch, or if the Story branch disappeared entirely.
        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("MANDATORY OUTPUT FORMAT")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task CuriosityNoActiveStory_DoesNotStoreChoices()
    {
        // No prior story — just a curiosity question from scratch.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567\u0589");

        await _chatService.GetResponseAsync(_deviceId, "why is the sky blue");

        Assert.False(ChatService.PendingChoices.ContainsKey(_conversationId));
    }

    [Fact]
    public async Task CuriosityMidStory_InjectsPreviousModeStoryDirective()
    {
        // Turn 1: start a story — establishes pending choices.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0531\u0572\u057e\u0565\u057d\u0568 \u0584\u0561\u0575\u056c\u0565\u0581\u0589\n---\nCHOICE_A:\u0555\u0563\u0576\u0565\u056c \u0561\u0572\u057e\u0565\u057d\u056b\u0576\nCHOICE_B:\u0553\u0561\u056d\u0579\u0565\u056c");
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        // Turn 2: Curiosity detour — directive must be present in system prompt.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567\u0589");
        await _chatService.GetResponseAsync(_deviceId, "why is the sky blue");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("PREVIOUS_MODE: Story")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task CuriosityNoActiveStory_DoesNotInjectPreviousModeStoryDirective()
    {
        // No prior story — pure Curiosity turn. Directive must NOT appear.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567\u0589");

        await _chatService.GetResponseAsync(_deviceId, "why is the sky blue");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => !s.Contains("PREVIOUS_MODE: Story")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task CuriosityMode_NoFormatReminderInjected()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567\u0589");

        await _chatService.GetResponseAsync(_deviceId, "what is a rainbow");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Any<string>(),
            Arg.Is<List<(string, string)>>(h =>
                !h.Any(m => m.Item2.Contains("FORMAT REMINDER"))));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Game mode
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GameTrigger_InjectsGamePrompt()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053e\u0561\u0583\u056b\u056f \u057f\u0578\u0582\u0580 \u0565\u0580\u056f\u0578\u0582 \u0561\u0576\u0563\u0561\u0574\u0589");

        await _chatService.GetResponseAsync(_deviceId, "let's play a game");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("MODE: GAME")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task GameArmenianTrigger_InjectsGamePrompt()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053e\u0561\u0583\u056b\u056f \u057f\u0578\u0582\u0580\u0589");

        // Armenian "խdelays delays delaysdelays delays delays" (let's play)
        await _chatService.GetResponseAsync(_deviceId, "\u056d\u0561\u0572\u0561\u0576\u0584");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("MODE: GAME")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task GameMode_NoChoiceBlock()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053e\u0561\u0583\u056b\u056f \u057f\u0578\u0582\u0580\u0589");

        var result = await _chatService.GetResponseAsync(_deviceId, "play with me");

        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
        Assert.Null(result.StorySessionId);
    }

    [Fact]
    public async Task GameMode_NoFormatReminder()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053e\u0561\u0583\u056b\u056f \u057f\u0578\u0582\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "let's play");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Any<string>(),
            Arg.Is<List<(string, string)>>(h =>
                !h.Any(m => m.Item2.Contains("FORMAT REMINDER"))));
    }

    [Fact]
    public async Task GameMode_PromptForbidsStoryContent()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053e\u0561\u0583\u056b\u056f \u057f\u0578\u0582\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "let's play a game");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("Do NOT tell a story")),
            Arg.Any<List<(string, string)>>());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Riddle mode
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RiddleTrigger_InjectsRiddlePrompt()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053b\u0576\u0579 \u0567 \u0564\u0561, \u056b\u0576\u0579 \u0567 \u0564\u0561\u0589");

        await _chatService.GetResponseAsync(_deviceId, "give me a riddle");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("MODE: RIDDLE")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task RiddleArmenianTrigger_InjectsRiddlePrompt()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053b\u0576\u0579 \u0567 \u0564\u0561\u0589");

        // Armenian "delaysdelays delaysdelays delaysdelays delaysdelays delays" (riddle)
        await _chatService.GetResponseAsync(_deviceId, "\u0570\u0561\u0576\u0565\u056c\u0578\u0582\u056f");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("MODE: RIDDLE")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task RiddleMode_NoChoiceBlock()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053b\u0576\u0579 \u0567 \u0564\u0561\u0589");

        var result = await _chatService.GetResponseAsync(_deviceId, "riddle me this");

        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
        Assert.Null(result.StorySessionId);
    }

    [Fact]
    public async Task RiddleMode_NoFormatReminder()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053b\u0576\u0579 \u0567 \u0564\u0561\u0589");

        await _chatService.GetResponseAsync(_deviceId, "ask me a riddle");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Any<string>(),
            Arg.Is<List<(string, string)>>(h =>
                !h.Any(m => m.Item2.Contains("FORMAT REMINDER"))));
    }

    [Fact]
    public async Task RiddleMode_PromptForbidsTrickRiddles()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053b\u0576\u0579 \u0567 \u0564\u0561\u0589");

        await _chatService.GetResponseAsync(_deviceId, "give me a riddle");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("FORBIDDEN RIDDLE TYPES")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task RiddleMode_PromptContainsConcreteExamples()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053b\u0576\u0579 \u0567 \u0564\u0561\u0589");

        await _chatService.GetResponseAsync(_deviceId, "riddle me");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("GOOD RIDDLE EXAMPLES") && s.Contains("Sphinx")),
            Arg.Any<List<(string, string)>>());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Mode field in ChatResponse
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ModeField_Story_ReturnsStory()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("A fox.\n---\nCHOICE_A:Go\nCHOICE_B:Մնալ");
        var result = await _chatService.GetResponseAsync(_deviceId, "tell me a story");
        Assert.Equal("story", result.Mode);
    }

    [Fact]
    public async Task ModeField_Calm_ReturnsCalm()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580\u0589");
        var result = await _chatService.GetResponseAsync(_deviceId, "good night");
        Assert.Equal("calm", result.Mode);
    }

    [Fact]
    public async Task ModeField_Game_ReturnsGame()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053e\u0561\u0583\u056b\u056f \u057f\u0578\u0582\u0580\u0589");
        var result = await _chatService.GetResponseAsync(_deviceId, "let's play");
        Assert.Equal("game", result.Mode);
    }

    [Fact]
    public async Task ModeField_Riddle_ReturnsRiddle()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053b\u0576\u0579 \u0567 \u0564\u0561\u0589");
        var result = await _chatService.GetResponseAsync(_deviceId, "give me a riddle");
        Assert.Equal("riddle", result.Mode);
    }

    [Fact]
    public async Task ModeField_Curiosity_ReturnsCuriosity()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567\u0589");
        var result = await _chatService.GetResponseAsync(_deviceId, "why is the sky blue");
        Assert.Equal("curiosity", result.Mode);
    }

    [Fact]
    public async Task ModeField_NoMode_ReturnsNull()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0532\u0561\u0580\u0587 \u0571\u0565\u0566\u0589");
        var result = await _chatService.GetResponseAsync(_deviceId, "hello");
        Assert.Null(result.Mode);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Non-story mode choice-block isolation
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GameMode_LeakedChoiceBlock_DoesNotCreatePendingChoices()
    {
        // AI accidentally produces a choice block in Game mode.
        // The block should be stripped but NOT stored as pending choices.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Play!\n---\nCHOICE_A:Ծափ\nCHOICE_B:Ցատկ");

        var result = await _chatService.GetResponseAsync(_deviceId, "let's play");

        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
        Assert.Null(result.StorySessionId);
        Assert.False(ChatService.PendingChoices.ContainsKey(_conversationId));
    }

    [Fact]
    public async Task CalmMode_LeakedChoiceBlock_DoesNotCreatePendingChoices()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Sleep.\n---\nCHOICE_A:Երազել\nCHOICE_B:Հանգիստ");

        var result = await _chatService.GetResponseAsync(_deviceId, "good night");

        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
        Assert.False(ChatService.PendingChoices.ContainsKey(_conversationId));
    }

    [Fact]
    public async Task GameMode_LeakedChoiceBlock_StillStrippedFromResponse()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Play!\n---\nCHOICE_A:Ծափ\nCHOICE_B:Ցատկ");

        var result = await _chatService.GetResponseAsync(_deviceId, "let's play a game");

        Assert.DoesNotContain("CHOICE_A", result.Response);
        Assert.DoesNotContain("CHOICE_B", result.Response);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Game/Riddle session persistence
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GameSession_ShortFollowUp_StaysInGame()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053e\u0561\u0583\u056b\u056f \u057f\u0578\u0582\u0580\u0589");

        // Turn 1: start game
        var r1 = await _chatService.GetResponseAsync(_deviceId, "lets play a game");
        Assert.Equal("game", r1.Mode);

        // Turn 2: short follow-up — should persist in game mode
        var r2 = await _chatService.GetResponseAsync(_deviceId, "ok I did it");
        Assert.Equal("game", r2.Mode);

        // Turn 3: another short follow-up
        var r3 = await _chatService.GetResponseAsync(_deviceId, "done");
        Assert.Equal("game", r3.Mode);
    }

    [Fact]
    public async Task RiddleSession_GuessFollowUp_StaysInRiddle()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053b\u0576\u0579 \u0567 \u0564\u0561\u0589");

        var r1 = await _chatService.GetResponseAsync(_deviceId, "give me a riddle");
        Assert.Equal("riddle", r1.Mode);

        var r2 = await _chatService.GetResponseAsync(_deviceId, "a cat");
        Assert.Equal("riddle", r2.Mode);

        var r3 = await _chatService.GetResponseAsync(_deviceId, "the sun");
        Assert.Equal("riddle", r3.Mode);
    }

    [Fact]
    public async Task GameSession_ExplicitStoryTrigger_OverridesGame()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053e\u0561\u0583\u056b\u056f\u0589",
                     "Fox.\n---\nCHOICE_A:Go\nCHOICE_B:Մնալ");

        await _chatService.GetResponseAsync(_deviceId, "lets play");
        var r2 = await _chatService.GetResponseAsync(_deviceId, "tell me a story");
        Assert.Equal("story", r2.Mode);
    }

    [Fact]
    public async Task GameSession_CalmTrigger_OverridesGame()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053e\u0561\u0583\u056b\u056f\u0589",
                     "\u053c\u0561\u057e \u0567\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "lets play");
        var r2 = await _chatService.GetResponseAsync(_deviceId, "good night");
        Assert.Equal("calm", r2.Mode);
    }

    [Fact]
    public async Task RiddleSession_CuriosityTrigger_OverridesRiddle()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053b\u0576\u0579 \u0567 \u0564\u0561\u0589",
                     "\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567\u0589");

        await _chatService.GetResponseAsync(_deviceId, "riddle me");
        var r2 = await _chatService.GetResponseAsync(_deviceId, "why is the sky blue");
        Assert.Equal("curiosity", r2.Mode);
    }

    [Fact]
    public async Task GameSession_CuriosityDetour_ResumesOnNextNeutralTurn()
    {
        // Regression for F-Game-2 / F-Cur-2: Game → Curiosity → neutral "ok"
        // must re-enter Game mode on the resume turn. Before the fix at
        // ChatService.cs:1166-1170, the else-clear wiped the ActiveModes
        // Game pointer on the Curiosity turn, and the neutral resume turn
        // dropped to None — the child's session felt abandoned.
        //
        // Turn 1: seed a real Game round with tail block so GameSessions
        //         has CurrentRound populated.
        // Turn 2: Curiosity detour — the preserved predicate now skips
        //         ActiveModes.TryRemove for Curiosity turns.
        // Turn 3: neutral "ok" — resume path at ~L1144 promotes detectedMode
        //         from None to Game via the preserved ActiveModes entry.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(
                "\u053e\u0561\u0583 \u057f\u0561\u0576\u0584 \u0574\u056b\u0561\u057d\u056b\u0576\u0589\n---\nGAME_TYPE:clap_along\nGAME_DIFFICULTY:1",
                "\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567\u0589",
                "\u0531\u057a\u0580\u0565\u055b\u057d\u0589 \u0540\u056b\u0574\u0561\u0589");

        await _chatService.GetResponseAsync(_deviceId, "lets play");
        await _chatService.GetResponseAsync(_deviceId, "why is the sky blue");

        _aiClient.ClearReceivedCalls();
        var r3 = await _chatService.GetResponseAsync(_deviceId, "ok");

        // Top-level mode resolution matches Game.
        Assert.Equal("game", r3.Mode);

        // Resume turn's system prompt carries the Game-continue directive
        // AND does NOT carry the CuriosityWindowInstruction header — so a
        // future regression that clears ActiveModes on Curiosity (restoring
        // the pre-fix bug) or routes the resume turn back to Curiosity
        // fails distinctly.
        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("GAME_TURN_KIND: continue")
                && !s.Contains("MODE: CURIOSITY WINDOW")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task RiddleSession_CuriosityDetour_ResumesOnNextNeutralTurn()
    {
        // Regression for F-Rid-4 / F-Cur-2: Riddle → Curiosity → a guess
        // must re-enter Riddle mode on the resume turn. Before the fix,
        // the preserved RIDDLE_ANSWER was orphaned — the child's next guess
        // was reclassified StartNew because the ActiveModes pointer was
        // wiped on the Curiosity turn.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(
                "\u053f\u0561\u0580\u0574\u056b\u0580 \u0567, \u056f\u056c\u0578\u0580 \u0567, \u056e\u0561\u057c\u056b \u057e\u0580\u0561 \u0567 \u0561\u0573\u0578\u0582\u0574\u0589 \u053b\u055e\u0576\u0579 \u0567\u0589\n---\nRIDDLE_ANSWER:\u056d\u0576\u0571\u0578\u0580\nRIDDLE_CATEGORY:fruit\nRIDDLE_DIFFICULTY:1",
                "\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567\u0589",
                "\u0544\u0578\u057f \u0565\u057d\u0589");

        await _chatService.GetResponseAsync(_deviceId, "give me a riddle");
        await _chatService.GetResponseAsync(_deviceId, "why is the sky blue");

        _aiClient.ClearReceivedCalls();
        var r3 = await _chatService.GetResponseAsync(_deviceId, "a cat");

        // Top-level mode resolution matches Riddle.
        Assert.Equal("riddle", r3.Mode);

        // Resume turn's system prompt carries a RIDDLE_TURN_KIND directive
        // (any kind — the guess will be classified by the runtime) AND does
        // NOT carry the CuriosityWindowInstruction header.
        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("RIDDLE_TURN_KIND:")
                && !s.Contains("MODE: CURIOSITY WINDOW")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task GameSession_ClearedAfterStory()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053e\u0561\u0583\u056b\u056f\u0589",
                     "Fox.\n---\nCHOICE_A:Go\nCHOICE_B:Մնալ",
                     "\u053e\u0561\u0583\u056b\u056f\u0589");

        await _chatService.GetResponseAsync(_deviceId, "lets play");

        // Switch to story — clears game session
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        // Short follow-up after story should NOT fall back to game
        var r3 = await _chatService.GetResponseAsync(_deviceId, "ok");
        // Story has pending choices → stays in story, not game
        Assert.Equal("story", r3.Mode);
    }

    [Fact]
    public async Task NoActiveSession_ShortMessage_ReturnsNone()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0532\u0561\u0580\u0587\u0589");

        // No prior game/riddle — short message stays as None
        var result = await _chatService.GetResponseAsync(_deviceId, "yes");
        Assert.Null(result.Mode);
    }

    // ─────────────────────────────────────────────────────────────────────
    // C1: Calm session persistence
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CalmSession_ShortFollowUp_StaysInCalm()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580\u0589");

        // Turn 1: enter Calm.
        var r1 = await _chatService.GetResponseAsync(_deviceId, "\u0584\u0576\u0565\u056c \u0565\u0574 \u0578\u0582\u0566\u0578\u0582\u0574"); // քնել եմ ուզում
        Assert.Equal("calm", r1.Mode);

        // Turn 2: neutral follow-up — Calm persists.
        var r2 = await _chatService.GetResponseAsync(_deviceId, "\u056c\u0561\u057e"); // լավ
        Assert.Equal("calm", r2.Mode);

        // And the Calm prompt is still injected on that follow-up turn.
        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("MODE: CALM / BEDTIME")),
            Arg.Any<List<(string, string)>>());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Calm Mode v2 — wind-down arc directive
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CalmDirective_FirstTurn_SaysTurn1()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "good night");

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("CALM_TURN_INDEX: 1")
                && s.Contains("Turn 1")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task CalmDirective_SecondTurn_SaysExactly2()
    {
        // Regression for F3: commit b865163 tightened the Turn-2 arc step
        // to the exact cardinality "Exactly 2 short sentences". The runtime
        // directive in BuildCalmTurnDirective must carry that wording on
        // the second consecutive Calm turn.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "i'm tired");

        _aiClient.ClearReceivedCalls();
        await _chatService.GetResponseAsync(_deviceId, "\u056c\u0561\u057e"); // լավ — 2nd Calm turn

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("CALM_TURN_INDEX: 2")
                && s.Contains("Exactly 2 short sentences")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task CalmDirective_ThirdConsecutiveTurn_SaysTurn3Plus()
    {
        // Three consecutive Calm turns — directive should escalate to "Turn 3+".
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "i'm tired");
        await _chatService.GetResponseAsync(_deviceId, "\u056c\u0561\u057e"); // լավ

        _aiClient.ClearReceivedCalls();
        await _chatService.GetResponseAsync(_deviceId, "\u056c\u0561\u057e"); // լավ — 3rd Calm turn

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("CALM_TURN_INDEX: 3")
                && s.Contains("Turn 3+")
                && s.Contains("never longer")
                // Regression for F3: cardinality tightened in b865163.
                && s.Contains("Exactly 1 short sentence")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task CalmDirective_ResetsAfterModeSwitch()
    {
        // Calm turn → switch to story → switch back to calm should reset
        // the turn index to 1.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580\u0589",
                     "Fox.\n---\nCHOICE_A:Go\nCHOICE_B:\u0544\u0576\u0561\u056c",
                     "\u053c\u0561\u057e \u0567\u0580\u0589");

        await _chatService.GetResponseAsync(_deviceId, "good night");          // calm turn 1
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");      // story
        _aiClient.ClearReceivedCalls();
        await _chatService.GetResponseAsync(_deviceId, "i'm tired");           // calm turn 1 again

        await _aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("CALM_TURN_INDEX: 1")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task CalmSession_ExplicitStoryTrigger_OverridesCalm()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580\u0589",
                     "Fox.\n---\nCHOICE_A:Go\nCHOICE_B:Մնալ");

        await _chatService.GetResponseAsync(_deviceId, "good night");
        var r2 = await _chatService.GetResponseAsync(_deviceId, "tell me a story");
        Assert.Equal("story", r2.Mode);
    }

    [Fact]
    public async Task CalmSession_CuriosityTrigger_OverridesCalm()
    {
        // A real off-topic question after a Calm turn must route to Curiosity,
        // not get swallowed by Calm persistence.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580\u0589",
                     "\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567\u0589");

        await _chatService.GetResponseAsync(_deviceId, "good night");
        var r2 = await _chatService.GetResponseAsync(_deviceId, "why is the sky blue");
        Assert.Equal("curiosity", r2.Mode);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Post-processing punctuation cleanup
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CalmMode_QuestionSurvivesRetry_StrippedByPostProcessing()
    {
        // Both first and retry responses contain a question mark.
        // Post-processing should replace it with Armenian period.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053c\u0561\u057e \u0567\u0580?", "\u053c\u0561\u057e \u0567\u0580?");

        var result = await _chatService.GetResponseAsync(_deviceId, "good night");

        Assert.DoesNotContain("?", result.Response);
        Assert.Contains("\u0589", result.Response);
    }

    [Fact]
    public async Task CuriosityMode_QuestionSurvivesRetry_StrippedByPostProcessing()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u0531\u0575\u0578?", "\u0531\u0575\u0578?");

        var result = await _chatService.GetResponseAsync(_deviceId, "why is the sky blue");

        Assert.DoesNotContain("?", result.Response);
    }

    [Fact]
    public async Task StoryMode_QuestionNotStripped()
    {
        // Story mode should NOT strip questions — they're legitimate in stories.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("\u053b\u0576\u0579 \u056f\u056c\u056b\u0576\u056b?\n---\nCHOICE_A:Go\nCHOICE_B:Մնալ");

        var result = await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        Assert.Contains("?", result.Response);
    }
}

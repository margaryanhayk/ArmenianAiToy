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
/// Provider-agnostic guarantee (live OK-001 / PR-003, 2026-09-23): an
/// empty or whitespace-only model reply is never stored or returned — the
/// child hears the calm fallback line instead of silence.
/// </summary>
public class ChatServiceEmptyReplyTests
{
    private readonly IChatService _chatService;
    private readonly IAiChatClient _aiClient;
    private readonly IConversationService _conversations;
    private readonly IConfiguration _config;
    private readonly IModerationService _moderation;
    private readonly IChildService _childService;
    private readonly Guid _conversationId;

    private string? _storedAssistantContent;
    private SafetyFlag? _storedAssistantFlag;

    public ChatServiceEmptyReplyTests()
    {
        _aiClient = Substitute.For<IAiChatClient>();
        var moderation = _moderation = Substitute.For<IModerationService>();
        _conversations = Substitute.For<IConversationService>();
        var childService = _childService = Substitute.For<IChildService>();

        _config = Substitute.For<IConfiguration>();
        _config["SystemPrompt"].Returns("You are a test assistant.");

        moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(true, new List<string>()));
        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>())
            .Returns((Child?)null);

        var conversation = new Conversation
        {
            Id = Guid.NewGuid(), DeviceId = Guid.NewGuid(), StartedAt = DateTime.UtcNow
        };
        _conversationId = conversation.Id;
        _conversations.GetOrCreateActiveConversationAsync(Arg.Any<Guid>(), Arg.Any<Guid?>())
            .Returns(conversation);
        _conversations.GetRecentMessagesAsync(Arg.Any<Guid>(), Arg.Any<int>())
            .Returns(new List<(string Role, string Content)>());

        _conversations.AddMessageAsync(
                Arg.Any<Guid>(), MessageRole.Assistant,
                Arg.Do<string>(c => _storedAssistantContent = c),
                Arg.Do<SafetyFlag>(f => _storedAssistantFlag = f))
            .Returns(ci => new Message
            {
                Id = Guid.NewGuid(), ConversationId = ci.ArgAt<Guid>(0),
                Role = MessageRole.Assistant, Content = ci.ArgAt<string>(2),
                Timestamp = DateTime.UtcNow, SafetyFlag = ci.ArgAt<SafetyFlag>(3)
            });
        _conversations.AddMessageAsync(
                Arg.Any<Guid>(), MessageRole.User, Arg.Any<string>(), Arg.Any<SafetyFlag>())
            .Returns(ci => new Message
            {
                Id = Guid.NewGuid(), ConversationId = ci.ArgAt<Guid>(0),
                Role = MessageRole.User, Content = ci.ArgAt<string>(2),
                Timestamp = DateTime.UtcNow, SafetyFlag = ci.ArgAt<SafetyFlag>(3)
            });

        _chatService = CreateWith(_aiClient);
    }

    private ChatService CreateWith(IAiChatClient client) => new(
        client, _moderation, _conversations, _childService, _config,
        Substitute.For<ILogger<ChatService>>(), new StoryChoiceCoherenceGate());

    private void ModelReturns(string reply) =>
        _aiClient.GetCompletionAsync(
                Arg.Any<string>(), Arg.Any<List<(string Role, string Content)>>())
            .Returns(reply);

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("   \n\t ")]
    [InlineData("։")]
    public async Task NonStory_EmptyReply_FallbackStoredAndReturned(string reply)
    {
        ModelReturns(reply);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "Բարև Արեգ։");

        Assert.Equal(ChatService.DefaultFallbackResponse, result.Response);
        Assert.Equal(ChatService.DefaultFallbackResponse, _storedAssistantContent);
        Assert.Equal(SafetyFlag.Flagged, result.SafetyFlag);
        Assert.Equal(SafetyFlag.Flagged, _storedAssistantFlag);
    }

    [Fact]
    public async Task NonStory_EmptyReply_UsesConfiguredFallback()
    {
        _config["SafetyFallbackResponse"].Returns("Խաղա՞նք միասին։");
        ModelReturns("\n");

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "Բարև Արեգ։");

        Assert.Equal("Խաղա՞նք միասին։", result.Response);
    }

    [Fact]
    public async Task Calm_EmptyReply_UsesCalmFallback()
    {
        ModelReturns("\n");

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "good night");

        Assert.Equal(ChatService.CalmFallbackResponse, result.Response);
        Assert.Equal(ChatService.CalmFallbackResponse, _storedAssistantContent);
    }

    [Theory]
    [InlineData("\n")]                                                // OK-001
    [InlineData("\n---\nCHOICE_A:Օգնել աղվեսին\nCHOICE_B:Մենակ անցնել")] // tail block only
    public async Task Story_EmptyProse_FallbackReturned_NoChoiceGenerationOnEmpty(string reply)
    {
        ModelReturns(reply);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "tell me a story");

        Assert.Equal(ChatService.DefaultFallbackResponse, result.Response);
        Assert.Equal(ChatService.DefaultFallbackResponse, _storedAssistantContent);
        Assert.Equal(SafetyFlag.Flagged, result.SafetyFlag);
        // Existing fallback contract: choices cleared on a safe-fallback.
        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
        // Exactly one model call: the choice generator was never fed empty prose.
        await _aiClient.Received(1).GetCompletionAsync(
            Arg.Any<string>(), Arg.Any<List<(string Role, string Content)>>());
    }

    [Fact]
    public async Task NormalReply_Unchanged_StaysClean()
    {
        ModelReturns("Բարև։ Ինչպե՞ս ես։");

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "Բարև Արեգ։");

        Assert.Equal("Բարև։ Ինչպե՞ս ես։", result.Response);
        Assert.Equal(SafetyFlag.Clean, result.SafetyFlag);
    }

    // ---- Real Gemini adapter → ChatService (review 2026-09-24): the
    // adapter must hand an empty candidate through as "" so the mode-aware,
    // Flagged, choice-clearing guard here runs on the production provider.

    private sealed class GeminiStubHandler : HttpMessageHandler
    {
        public int Calls;
        public string Json =
            "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"\\n\"}]},\"finishReason\":\"STOP\"}]}";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(Json, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private ChatService CreateWithRealGemini(GeminiStubHandler handler) =>
        CreateWith(new ArmenianAiToy.Infrastructure.Ai.GeminiChatClientAdapter(
            new HttpClient(handler), "test-key", "gemini-test",
            Substitute.For<ILogger<ArmenianAiToy.Infrastructure.Ai.GeminiChatClientAdapter>>()));

    [Fact]
    public async Task RealGemini_WhitespaceCandidate_Story_FlaggedFallback_NoChoices_OneCall()
    {
        var handler = new GeminiStubHandler();
        var svc = CreateWithRealGemini(handler);

        var result = await svc.GetResponseAsync(Guid.NewGuid(), "tell me a story");

        Assert.Equal(ChatService.DefaultFallbackResponse, result.Response);
        Assert.Equal(SafetyFlag.Flagged, result.SafetyFlag);
        Assert.Equal(SafetyFlag.Flagged, _storedAssistantFlag);
        Assert.Null(result.ChoiceA);
        Assert.Null(result.ChoiceB);
        Assert.Equal(1, handler.Calls); // no choice-generation call on the fallback
    }

    [Fact]
    public async Task RealGemini_NoPartsCandidate_Calm_UsesCalmFallback_Flagged()
    {
        var handler = new GeminiStubHandler
        {
            Json = "{\"candidates\":[{\"finishReason\":\"OTHER\"}]}",
        };
        var svc = CreateWithRealGemini(handler);

        var result = await svc.GetResponseAsync(Guid.NewGuid(), "good night");

        Assert.Equal(ChatService.CalmFallbackResponse, result.Response);
        Assert.Equal(ChatService.CalmFallbackResponse, _storedAssistantContent);
        Assert.Equal(SafetyFlag.Flagged, result.SafetyFlag);
        Assert.Equal(1, handler.Calls);
    }

    // ---- Game honesty: a round from a tail-block-only reply was never heard.

    [Fact]
    public async Task Riddle_TailBlockOnlyReply_NoRoundStored()
    {
        ChatService.RiddleSessions.TryRemove(_conversationId, out _);
        ModelReturns("\n---\nRIDDLE_ANSWER:\u056d\u0576\u0571\u0578\u0580\nRIDDLE_CATEGORY:fruit\nRIDDLE_DIFFICULTY:1");

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "give me a riddle");

        Assert.Equal(SafetyFlag.Flagged, result.SafetyFlag);
        Assert.False(ChatService.RiddleSessions.TryGetValue(_conversationId, out var state)
            && state.CurrentRound is not null);
    }

    [Fact]
    public async Task Game_TailBlockOnlyReply_NoRoundStored_TurnEnds()
    {
        ChatService.GameSessions.TryRemove(_conversationId, out _);
        ModelReturns("\n---\nGAME_TYPE:animal_sound\nGAME_DIFFICULTY:1");

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "let's play");

        Assert.Equal(SafetyFlag.Flagged, result.SafetyFlag);
        Assert.False(ChatService.GameSessions.TryGetValue(_conversationId, out var state)
            && state.CurrentRound is not null);
    }

    // ---- An empty quality retry is never adopted over an acceptable original.

    [Fact]
    public async Task QualityRetry_EmptyRetry_KeepsOriginalReply()
    {
        const string original = "\u0554\u0576\u056b\u0580 \u0570\u0561\u0576\u0563\u056b\u057d\u057f!"; // calm_exclamation trigger
        _aiClient.GetCompletionAsync(
                Arg.Any<string>(), Arg.Any<List<(string Role, string Content)>>())
            .Returns(original, "\n");

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "good night");

        await _aiClient.Received(2).GetCompletionAsync(
            Arg.Any<string>(), Arg.Any<List<(string Role, string Content)>>());
        Assert.NotEqual(ChatService.CalmFallbackResponse, result.Response);
        Assert.Contains("\u0554\u0576\u056b\u0580", result.Response);
        Assert.Equal(SafetyFlag.Clean, result.SafetyFlag);
    }
}

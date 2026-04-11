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
/// Tests that StoryMemory extracted from AI responses is injected
/// into the system prompt on subsequent story-mode requests.
/// </summary>
public class StoryMemoryInjectionTests
{
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly Guid _conversationId = Guid.NewGuid();
    private readonly IChatService _chatService;
    private readonly IAiChatClient _aiClient;
    private string? _capturedSystemPrompt;

    public StoryMemoryInjectionTests()
    {
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

        // Capture the system prompt passed to the AI client
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(callInfo =>
            {
                _capturedSystemPrompt = callInfo.ArgAt<string>(0);
                return "A simple response.";
            });

        _chatService = new ChatService(
            _aiClient, moderation, conversations, childService, config, logger);
    }

    [Fact]
    public async Task StoryMemory_InjectedIntoPromptOnSubsequentRequest()
    {
        // Seed story memory for the conversation
        ChatService.StoryMemories[_conversationId] = new StoryMemory(
            "Fox", "Forest", "Golden key", "Lost in woods", "Curious", DateTime.UtcNow);

        // Make a story request — the memory should be injected into the system prompt
        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        Assert.NotNull(_capturedSystemPrompt);
        Assert.Contains("CURRENT STORY STATE", _capturedSystemPrompt);
        Assert.Contains("Character: Fox", _capturedSystemPrompt);
        Assert.Contains("Place: Forest", _capturedSystemPrompt);
        Assert.Contains("Key object: Golden key", _capturedSystemPrompt);
        Assert.Contains("Situation: Lost in woods", _capturedSystemPrompt);
        Assert.Contains("Mood: Curious", _capturedSystemPrompt);
    }

    [Fact]
    public async Task StoryMemory_PartialFields_OnlyPresentFieldsInjected()
    {
        ChatService.StoryMemories[_conversationId] = new StoryMemory(
            "Bear", null, null, null, "Happy", DateTime.UtcNow);

        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        Assert.NotNull(_capturedSystemPrompt);
        Assert.Contains("Character: Bear", _capturedSystemPrompt);
        Assert.Contains("Mood: Happy", _capturedSystemPrompt);
        Assert.DoesNotContain("Place:", _capturedSystemPrompt);
        Assert.DoesNotContain("Key object:", _capturedSystemPrompt);
        Assert.DoesNotContain("Situation:", _capturedSystemPrompt);
    }

    [Fact]
    public async Task StoryMemory_NoMemory_NoInjection()
    {
        // Ensure no memory exists for this conversation
        ChatService.StoryMemories.TryRemove(_conversationId, out _);

        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        Assert.NotNull(_capturedSystemPrompt);
        Assert.DoesNotContain("CURRENT STORY STATE", _capturedSystemPrompt);
    }

    [Fact]
    public async Task StoryMemory_NotInjectedOutsideStoryMode()
    {
        ChatService.StoryMemories[_conversationId] = new StoryMemory(
            "Fox", "Forest", null, null, null, DateTime.UtcNow);

        // Non-story message — memory should NOT be injected
        await _chatService.GetResponseAsync(_deviceId, "hello how are you");

        Assert.NotNull(_capturedSystemPrompt);
        Assert.DoesNotContain("CURRENT STORY STATE", _capturedSystemPrompt);
    }
}

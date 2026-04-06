using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests that ChatService strips the tail block from assistant responses
/// before storing and returning to the client.
/// </summary>
public class ChatServiceTailBlockTests
{
    private readonly IChatService _chatService;
    private readonly IAiChatClient _aiClient;
    private readonly IConversationService _conversations;
    private readonly IModerationService _moderation;

    // Captures the content string passed to AddMessageAsync for the assistant message.
    private string? _storedAssistantContent;

    public ChatServiceTailBlockTests()
    {
        _aiClient = Substitute.For<IAiChatClient>();
        _moderation = Substitute.For<IModerationService>();
        _conversations = Substitute.For<IConversationService>();
        var childService = Substitute.For<IChildService>();
        var logger = Substitute.For<ILogger<ChatService>>();

        var config = Substitute.For<IConfiguration>();
        config["SystemPrompt"].Returns("You are a test assistant.");

        // Default: moderation always passes
        _moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(true, new List<string>()));

        // Default: no child
        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>())
            .Returns((Child?)null);

        // Default: return a conversation
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            StartedAt = DateTime.UtcNow
        };
        _conversations.GetOrCreateActiveConversationAsync(Arg.Any<Guid>(), Arg.Any<Guid?>())
            .Returns(conversation);

        _conversations.GetRecentMessagesAsync(Arg.Any<Guid>(), Arg.Any<int>())
            .Returns(new List<(string Role, string Content)>());

        // Capture the stored assistant message content
        _conversations.AddMessageAsync(
            Arg.Any<Guid>(),
            MessageRole.Assistant,
            Arg.Do<string>(content => _storedAssistantContent = content),
            Arg.Any<SafetyFlag>())
            .Returns(callInfo => new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = callInfo.ArgAt<Guid>(0),
                Role = MessageRole.Assistant,
                Content = callInfo.ArgAt<string>(2),
                Timestamp = DateTime.UtcNow,
                SafetyFlag = callInfo.ArgAt<SafetyFlag>(3)
            });

        // Also handle user message storage (don't capture)
        _conversations.AddMessageAsync(
            Arg.Any<Guid>(),
            MessageRole.User,
            Arg.Any<string>(),
            Arg.Any<SafetyFlag>())
            .Returns(callInfo => new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = callInfo.ArgAt<Guid>(0),
                Role = MessageRole.User,
                Content = callInfo.ArgAt<string>(2),
                Timestamp = DateTime.UtcNow,
                SafetyFlag = callInfo.ArgAt<SafetyFlag>(3)
            });

        _chatService = new ChatService(
            _aiClient, _moderation, _conversations, childService, config, logger);
    }

    [Fact]
    public async Task ResponseWithTailBlock_StoresCleanedText()
    {
        var rawAiResponse = "The fox ran to the river.\n---\nCHOICE_A:Help the fox\nCHOICE_B:Cross alone";
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(rawAiResponse);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "hello");

        Assert.Equal("The fox ran to the river.", result.Response);
        Assert.Equal("The fox ran to the river.", _storedAssistantContent);
    }

    [Fact]
    public async Task ResponseWithoutTailBlock_PassesThroughUnchanged()
    {
        var rawAiResponse = "Just a normal reply.";
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(rawAiResponse);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "hello");

        Assert.Equal("Just a normal reply.", result.Response);
        Assert.Equal("Just a normal reply.", _storedAssistantContent);
    }

    [Fact]
    public async Task QualityGate_LatinRun_TriggersOneRetry()
    {
        // First call: contains 4+ Latin letters in a row → must retry.
        // Second call: clean Armenian → must be the stored response.
        var bad = "Once կար մեկ նապաստակ։\n---\nCHOICE_A:Բացենք տուփը\nCHOICE_B:Փակենք տուփը";
        var good = "Կար մի նապաստակ։\n---\nCHOICE_A:Բացենք տուփը\nCHOICE_B:Փակենք տուփը";
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(bad, good);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "tell me a story");

        await _aiClient.Received(2).GetCompletionAsync(
            Arg.Any<string>(), Arg.Any<List<(string, string)>>());
        Assert.Equal("Կար մի նապաստակ։", result.Response);
        Assert.Equal("Կար մի նապաստակ։", _storedAssistantContent);
    }

    [Fact]
    public async Task QualityGate_CleanResponse_DoesNotRetry()
    {
        var good = "Կար մի նապաստակ։\n---\nCHOICE_A:Բացենք տուփը\nCHOICE_B:Փակենք տուփը";
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(good);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "tell me a story about a bunny");

        await _aiClient.Received(1).GetCompletionAsync(
            Arg.Any<string>(), Arg.Any<List<(string, string)>>());
        Assert.Equal("Կար մի նապաստակ։", result.Response);
    }

    [Fact]
    public async Task MalformedTailBlock_CleanedByResponseCleaner()
    {
        // TailBlockParser can't parse this (missing CHOICE_B), so the raw text
        // passes through. ResponseCleaner then strips the leaked artifacts.
        var rawAiResponse = "Story.\n---\nCHOICE_A:Only one option";
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(rawAiResponse);

        var result = await _chatService.GetResponseAsync(Guid.NewGuid(), "hello");

        Assert.Equal("Story.", result.Response);
        Assert.Equal("Story.", _storedAssistantContent);
    }
}

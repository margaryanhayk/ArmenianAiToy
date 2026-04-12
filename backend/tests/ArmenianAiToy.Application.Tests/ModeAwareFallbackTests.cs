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
/// Tests that the safety fallback response is mode-aware: calm mode
/// gets a soft bedtime fallback instead of "let's start a fairy tale."
/// </summary>
public class ModeAwareFallbackTests
{
    [Fact]
    public async Task CalmMode_OutputFlagged_ReturnsCalmFallback()
    {
        var deviceId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var aiClient = Substitute.For<IAiChatClient>();
        var moderation = Substitute.For<IModerationService>();
        var conversations = Substitute.For<IConversationService>();
        var childService = Substitute.For<IChildService>();
        var logger = Substitute.For<ILogger<ChatService>>();
        var config = Substitute.For<IConfiguration>();
        config["SystemPrompt"].Returns("Test assistant.");

        // Input moderation passes, output moderation fails.
        moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(
                new ModerationResult(true, new List<string>()),
                new ModerationResult(false, new List<string> { "test_flag" }));

        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>())
            .Returns((Child?)null);

        var conversation = new Conversation
        {
            Id = conversationId, DeviceId = deviceId, StartedAt = DateTime.UtcNow
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

        aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Some flagged AI response");

        var chatService = new ChatService(
            aiClient, moderation, conversations, childService, config, logger);

        var result = await chatService.GetResponseAsync(deviceId, "good night");

        Assert.Equal(ChatService.CalmFallbackResponse, result.Response);
        Assert.Equal("calm", result.Mode);
    }

    [Fact]
    public async Task StoryMode_OutputFlagged_ReturnsDefaultFallback()
    {
        var deviceId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var aiClient = Substitute.For<IAiChatClient>();
        var moderation = Substitute.For<IModerationService>();
        var conversations = Substitute.For<IConversationService>();
        var childService = Substitute.For<IChildService>();
        var logger = Substitute.For<ILogger<ChatService>>();
        var config = Substitute.For<IConfiguration>();
        config["SystemPrompt"].Returns("Test assistant.");

        moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(
                new ModerationResult(true, new List<string>()),
                new ModerationResult(false, new List<string> { "test_flag" }));

        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>())
            .Returns((Child?)null);

        var conversation = new Conversation
        {
            Id = conversationId, DeviceId = deviceId, StartedAt = DateTime.UtcNow
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

        aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Some flagged story response");

        var chatService = new ChatService(
            aiClient, moderation, conversations, childService, config, logger);

        var result = await chatService.GetResponseAsync(deviceId, "tell me a story");

        Assert.Equal(ChatService.DefaultFallbackResponse, result.Response);
    }
}

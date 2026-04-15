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

    // ─────────────────────────────────────────────────────────────────────
    // D3-lite: unavailable vs flagged separation
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InputModeration_Unavailable_ReturnsUnavailableFallback()
    {
        var (chatService, deviceId) = BuildChatService(
            firstModeration: new ModerationResult(false, new List<string> { "moderation_unavailable" }),
            secondModeration: null,
            aiResponse: "ignored");

        var result = await chatService.GetResponseAsync(deviceId, "tell me a story");

        Assert.Equal(ChatService.ModerationUnavailableFallbackResponse, result.Response);
        Assert.Equal(SafetyFlag.Blocked, result.SafetyFlag);
    }

    [Fact]
    public async Task InputModeration_GenuineFlag_ReturnsSafetyFallback()
    {
        var (chatService, deviceId) = BuildChatService(
            firstModeration: new ModerationResult(false, new List<string> { "violence" }),
            secondModeration: null,
            aiResponse: "ignored");

        var result = await chatService.GetResponseAsync(deviceId, "unsafe input");

        // Genuine flag uses the existing config-or-default safety fallback,
        // NOT the new unavailable constant.
        Assert.NotEqual(ChatService.ModerationUnavailableFallbackResponse, result.Response);
        Assert.Equal(SafetyFlag.Blocked, result.SafetyFlag);
    }

    [Theory]
    [InlineData("tell me a story")]
    [InlineData("good night")]
    public async Task OutputModeration_Unavailable_ReturnsUnavailableFallback(string userMessage)
    {
        // Input moderation passes, output moderation returns unavailable.
        var (chatService, deviceId) = BuildChatService(
            firstModeration: new ModerationResult(true, new List<string>()),
            secondModeration: new ModerationResult(false, new List<string> { "moderation_unavailable" }),
            aiResponse: "Some generated response");

        var result = await chatService.GetResponseAsync(deviceId, userMessage);

        // Calm mode must NOT use CalmFallbackResponse when moderation is
        // unavailable — the unavailable fallback beats mode-specific
        // fallbacks so the child gets honest "try again" signaling.
        Assert.Equal(ChatService.ModerationUnavailableFallbackResponse, result.Response);
    }

    // Shared builder — mirrors the plumbing in the two pre-existing tests
    // above. Returns a chat service wired to the scripted moderation and AI
    // responses, and the deviceId to pass to GetResponseAsync.
    private static (ChatService, Guid) BuildChatService(
        ModerationResult firstModeration,
        ModerationResult? secondModeration,
        string aiResponse)
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

        if (secondModeration is not null)
            moderation.CheckContentAsync(Arg.Any<string>())
                .Returns(firstModeration, secondModeration);
        else
            moderation.CheckContentAsync(Arg.Any<string>()).Returns(firstModeration);

        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>()).Returns((Child?)null);

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
                SafetyFlag = callInfo.ArgAt<SafetyFlag>(3),
            });

        aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(aiResponse);

        var chatService = new ChatService(
            aiClient, moderation, conversations, childService, config, logger);

        return (chatService, deviceId);
    }
}

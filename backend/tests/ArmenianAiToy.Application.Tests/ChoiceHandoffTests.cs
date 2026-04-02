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
/// Tests that option labels extracted from a tail block on one request
/// are available to ChoiceNormalizer on the next request for the same conversation.
/// Verifies consume-and-remove semantics, expiry, and safety ordering.
/// </summary>
public class ChoiceHandoffTests
{
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly Guid _conversationId = Guid.NewGuid();
    private readonly IChatService _chatService;
    private readonly IAiChatClient _aiClient;
    private readonly ILogger<ChatService> _logger;

    public ChoiceHandoffTests()
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
    public async Task ExtractedLabels_UsedByNormalizerOnNextRequest()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Pick one!\n---\nCHOICE_A:Help the fox\nCHOICE_B:Cross alone");

        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("The fox was happy!");

        await _chatService.GetResponseAsync(_deviceId, "left");

        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Choice normalized") && o.ToString()!.Contains("option_a")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Labels_ConsumedAfterFirstUse()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Pick!\n---\nCHOICE_A:Option A\nCHOICE_B:Option B");

        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Continuing...");

        await _chatService.GetResponseAsync(_deviceId, "left");

        _logger.ClearReceivedCalls();

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("More story.");

        await _chatService.GetResponseAsync(_deviceId, "right");

        _logger.DidNotReceive().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Choice normalized")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task NoPendingLabels_NormalizerDoesNotFire()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Just chatting.");

        await _chatService.GetResponseAsync(_deviceId, "hello");

        _logger.DidNotReceive().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Choice normalized")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task BlockedInput_DoesNotTriggerNormalizer()
    {
        // Separate setup: moderation blocks the second request
        var aiClient = Substitute.For<IAiChatClient>();
        var moderation = Substitute.For<IModerationService>();
        var conversations = Substitute.For<IConversationService>();
        var childService = Substitute.For<IChildService>();
        var logger = Substitute.For<ILogger<ChatService>>();
        var config = Substitute.For<IConfiguration>();
        config["SystemPrompt"].Returns("Test.");

        var convId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        var conv = new Conversation { Id = convId, DeviceId = deviceId, StartedAt = DateTime.UtcNow };
        conversations.GetOrCreateActiveConversationAsync(Arg.Any<Guid>(), Arg.Any<Guid?>()).Returns(conv);
        conversations.GetRecentMessagesAsync(Arg.Any<Guid>(), Arg.Any<int>())
            .Returns(new List<(string Role, string Content)>());
        conversations.AddMessageAsync(Arg.Any<Guid>(), Arg.Any<MessageRole>(), Arg.Any<string>(), Arg.Any<SafetyFlag>())
            .Returns(callInfo => new Message
            {
                Id = Guid.NewGuid(), ConversationId = convId,
                Role = callInfo.ArgAt<MessageRole>(1), Content = callInfo.ArgAt<string>(2),
                Timestamp = DateTime.UtcNow, SafetyFlag = callInfo.ArgAt<SafetyFlag>(3)
            });
        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>()).Returns((Child?)null);

        // Request 1: moderation passes, AI returns tail block
        moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(true, new List<string>()));
        aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Story.\n---\nCHOICE_A:Left\nCHOICE_B:Right");

        var svc = new ChatService(aiClient, moderation, conversations, childService, config, logger);
        await svc.GetResponseAsync(deviceId, "tell me a story");

        // Request 2: moderation BLOCKS the child input
        moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(false, new List<string> { "violence" }));

        await svc.GetResponseAsync(deviceId, "bad input");

        // Normalizer should NOT have fired
        logger.DidNotReceive().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Choice normalized")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task LogDoesNotContainRawInput()
    {
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Pick!\n---\nCHOICE_A:Left\nCHOICE_B:Right");

        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("OK!");

        await _chatService.GetResponseAsync(_deviceId, "left");

        // The log message should contain "Choice normalized" but NOT "Raw:"
        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Choice normalized") && !o.ToString()!.Contains("Raw:")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task NormalizedChoice_InjectedIntoSystemPrompt()
    {
        // Set up a separate instance with controllable history
        var aiClient = Substitute.For<IAiChatClient>();
        var moderation = Substitute.For<IModerationService>();
        var conversations = Substitute.For<IConversationService>();
        var childService = Substitute.For<IChildService>();
        var logger = Substitute.For<ILogger<ChatService>>();
        var config = Substitute.For<IConfiguration>();
        config["SystemPrompt"].Returns("You are a test assistant.");

        var convId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var conv = new Conversation { Id = convId, DeviceId = deviceId, StartedAt = DateTime.UtcNow };

        conversations.GetOrCreateActiveConversationAsync(Arg.Any<Guid>(), Arg.Any<Guid?>()).Returns(conv);
        moderation.CheckContentAsync(Arg.Any<string>()).Returns(new ModerationResult(true, new List<string>()));
        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>()).Returns((Child?)null);
        conversations.AddMessageAsync(Arg.Any<Guid>(), Arg.Any<MessageRole>(), Arg.Any<string>(), Arg.Any<SafetyFlag>())
            .Returns(callInfo => new Message
            {
                Id = Guid.NewGuid(), ConversationId = convId,
                Role = callInfo.ArgAt<MessageRole>(1), Content = callInfo.ArgAt<string>(2),
                Timestamp = DateTime.UtcNow, SafetyFlag = callInfo.ArgAt<SafetyFlag>(3)
            });

        // Request 1: empty history, AI returns story with tail block
        conversations.GetRecentMessagesAsync(Arg.Any<Guid>(), Arg.Any<int>())
            .Returns(new List<(string Role, string Content)>());
        aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Pick one!\n---\nCHOICE_A:Help the fox\nCHOICE_B:Cross alone");

        var svc = new ChatService(aiClient, moderation, conversations, childService, config, logger);
        await svc.GetResponseAsync(deviceId, "tell me a story");

        // Request 2: history now contains "tell me a story" (story intent active)
        conversations.GetRecentMessagesAsync(Arg.Any<Guid>(), Arg.Any<int>())
            .Returns(new List<(string Role, string Content)>
            {
                ("User", "tell me a story"),
                ("Assistant", "Pick one!"),
            });
        aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("The fox was happy!");

        await svc.GetResponseAsync(deviceId, "left");

        // The system prompt in the second call should contain the choice hint
        await aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("previous_story_choice: option_a")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task UnknownChoice_NotInjectedIntoSystemPrompt()
    {
        // Set up a separate instance with controllable history
        var aiClient = Substitute.For<IAiChatClient>();
        var moderation = Substitute.For<IModerationService>();
        var conversations = Substitute.For<IConversationService>();
        var childService = Substitute.For<IChildService>();
        var logger = Substitute.For<ILogger<ChatService>>();
        var config = Substitute.For<IConfiguration>();
        config["SystemPrompt"].Returns("You are a test assistant.");

        var convId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var conv = new Conversation { Id = convId, DeviceId = deviceId, StartedAt = DateTime.UtcNow };

        conversations.GetOrCreateActiveConversationAsync(Arg.Any<Guid>(), Arg.Any<Guid?>()).Returns(conv);
        moderation.CheckContentAsync(Arg.Any<string>()).Returns(new ModerationResult(true, new List<string>()));
        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>()).Returns((Child?)null);
        conversations.AddMessageAsync(Arg.Any<Guid>(), Arg.Any<MessageRole>(), Arg.Any<string>(), Arg.Any<SafetyFlag>())
            .Returns(callInfo => new Message
            {
                Id = Guid.NewGuid(), ConversationId = convId,
                Role = callInfo.ArgAt<MessageRole>(1), Content = callInfo.ArgAt<string>(2),
                Timestamp = DateTime.UtcNow, SafetyFlag = callInfo.ArgAt<SafetyFlag>(3)
            });

        // Request 1: AI returns story with tail block
        conversations.GetRecentMessagesAsync(Arg.Any<Guid>(), Arg.Any<int>())
            .Returns(new List<(string Role, string Content)>());
        aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Pick one!\n---\nCHOICE_A:Help the fox\nCHOICE_B:Cross alone");

        var svc = new ChatService(aiClient, moderation, conversations, childService, config, logger);
        await svc.GetResponseAsync(deviceId, "tell me a story");

        // Request 2: history has story trigger, but child says gibberish → unknown
        conversations.GetRecentMessagesAsync(Arg.Any<Guid>(), Arg.Any<int>())
            .Returns(new List<(string Role, string Content)>
            {
                ("User", "tell me a story"),
                ("Assistant", "Pick one!"),
            });
        aiClient.ClearReceivedCalls();
        aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("What do you mean?");

        await svc.GetResponseAsync(deviceId, "asdfgh");

        // The system prompt should NOT contain the choice hint
        await aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => !s.Contains("previous_story_choice:")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task ExpiredPendingLabels_DoNotTriggerNormalizer()
    {
        // Directly seed a stale pending choice (31 minutes old)
        ChatService.PendingChoices[_conversationId] = new ChatService.PendingChoice(
            "Left", "Right", DateTime.UtcNow.AddMinutes(-31));

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Continuing...");

        await _chatService.GetResponseAsync(_deviceId, "left");

        // Normalizer should NOT have fired because labels are expired
        _logger.DidNotReceive().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Choice normalized")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());

        // Entry should have been consumed (removed) even though expired
        Assert.False(ChatService.PendingChoices.ContainsKey(_conversationId));
    }
}

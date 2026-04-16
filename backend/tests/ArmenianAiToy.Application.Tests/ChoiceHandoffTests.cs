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
            .Returns("Ընտրիր մեկը։\n---\nCHOICE_A:Օգնել աղվեսին\nCHOICE_B:Մենակ անցնել");

        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Աղվեսն ուրախացավ։");

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
            .Returns("Ընտրիր։\n---\nCHOICE_A:Տարբերակ Ա\nCHOICE_B:Տարբերակ Բ");

        await _chatService.GetResponseAsync(_deviceId, "tell me a story");

        // Second call: return response WITH choices so the fallback doesn't fire
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Continuing...\n---\nCHOICE_A:New A\nCHOICE_B:New B");

        await _chatService.GetResponseAsync(_deviceId, "left");

        _logger.ClearReceivedCalls();

        // Third call: the ORIGINAL labels (Option A / Option B) should be gone.
        // New labels (New A / New B) may be present from the second call.
        _aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("More story.");

        await _chatService.GetResponseAsync(_deviceId, "right");

        // Verify the original "Տարբերակ Ա"/"Տարբերակ Բ" labels are NOT in any log —
        // they were consumed on the second call.
        _logger.DidNotReceive().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Տարբերակ Ա") || o.ToString()!.Contains("Տարբերակ Բ")),
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
            .Returns("Հեքիաթ։\n---\nCHOICE_A:Ձախ\nCHOICE_B:Աջ");

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
            .Returns("Ընտրիր։\n---\nCHOICE_A:Ձախ\nCHOICE_B:Աջ");

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
            .Returns("Ընտրիր մեկը։\n---\nCHOICE_A:Օգնել աղվեսին\nCHOICE_B:Մենակ անցնել");

        var svc = new ChatService(aiClient, moderation, conversations, childService, config, logger);
        await svc.GetResponseAsync(deviceId, "tell me a story");

        // Request 2: history now contains "tell me a story" (story intent active)
        conversations.GetRecentMessagesAsync(Arg.Any<Guid>(), Arg.Any<int>())
            .Returns(new List<(string Role, string Content)>
            {
                ("User", "tell me a story"),
                ("Assistant", "Ընտրիր մեկը։"),
            });
        aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Աղվեսն ուրախացավ։");

        await svc.GetResponseAsync(deviceId, "left");

        // The system prompt in the second call should contain the choice hint
        await aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("previous_story_choice: option_a")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task UnknownChoice_InjectsUnclearHint()
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
            .Returns("Ընտրիր մեկը։\n---\nCHOICE_A:Օգնել աղվեսին\nCHOICE_B:Մենակ անցնել");

        var svc = new ChatService(aiClient, moderation, conversations, childService, config, logger);
        await svc.GetResponseAsync(deviceId, "tell me a story");

        // Request 2: history has story trigger, but child says gibberish → unknown
        conversations.GetRecentMessagesAsync(Arg.Any<Guid>(), Arg.Any<int>())
            .Returns(new List<(string Role, string Content)>
            {
                ("User", "tell me a story"),
                ("Assistant", "Ընտրիր մեկը։"),
            });
        aiClient.ClearReceivedCalls();
        aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Ի՞նչ ի նկատի ունես։");

        await svc.GetResponseAsync(deviceId, "asdfgh");

        // The system prompt should contain the "unclear" hint
        await aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => s.Contains("previous_story_choice: unclear")),
            Arg.Any<List<(string, string)>>());
    }

    [Fact]
    public async Task ExpiredPendingLabels_DoNotTriggerNormalizer()
    {
        // Directly seed a stale pending choice (31 minutes old)
        ChatService.PendingChoices[_conversationId] = new ChatService.PendingChoice(
            "Ձախ", "Աջ", DateTime.UtcNow.AddMinutes(-31));

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

    [Fact]
    public async Task ExplicitChoice_InjectsStrengthenedDirectiveIntoHistory()
    {
        // Set up a separate instance with controllable history so we can
        // observe the history content handed to the AI on the second call.
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

        // Request 1: seed pending choices via tail block
        conversations.GetRecentMessagesAsync(Arg.Any<Guid>(), Arg.Any<int>())
            .Returns(new List<(string Role, string Content)>());
        aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Ընտրիր մեկը։\n---\nCHOICE_A:Օգնել աղվեսին\nCHOICE_B:Մենակ անցնել");

        var svc = new ChatService(aiClient, moderation, conversations, childService, config, logger);
        await svc.GetResponseAsync(deviceId, "tell me a story");

        // Request 2: explicit selectedChoice=A. History should have the raw
        // "A" message replaced with the strengthened directive.
        conversations.GetRecentMessagesAsync(Arg.Any<Guid>(), Arg.Any<int>())
            .Returns(new List<(string Role, string Content)>
            {
                ("user", "tell me a story"),
                ("assistant", "Ընտրիր մեկը։"),
                ("user", "A"),
            });
        aiClient.ClearReceivedCalls();
        aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Աղվեսը ուրախացավ։\n---\nCHOICE_A:Նոր Ա\nCHOICE_B:Նոր Բ");

        await svc.GetResponseAsync(deviceId, "A", selectedChoice: "A");

        // The last user turn in history should contain the strengthened
        // directive — chosen label quoted, act-on-choice instruction, and
        // anti-recap instruction all present.
        await aiClient.Received().GetCompletionAsync(
            Arg.Any<string>(),
            Arg.Is<List<(string, string)>>(h =>
                h.Any(m => m.Item1 == "user"
                    && m.Item2.Contains("\"Օգնել աղվեսին\"")
                    && m.Item2.Contains("MUST contain at least one key noun or verb from this label verbatim")
                    && m.Item2.Contains("Do not restate the previous scene"))));
    }

    [Fact]
    public async Task ExpiredPendingLabels_DoNotInjectUnclearHint()
    {
        // Set up with controllable history so story intent is active
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

        // History has story trigger so HasStoryIntent returns true
        conversations.GetRecentMessagesAsync(Arg.Any<Guid>(), Arg.Any<int>())
            .Returns(new List<(string Role, string Content)>
            {
                ("User", "tell me a story"),
                ("Assistant", "Once upon a time..."),
            });

        // Seed an expired pending choice (31 minutes old)
        ChatService.PendingChoices[convId] = new ChatService.PendingChoice(
            "Ձախ", "Աջ", DateTime.UtcNow.AddMinutes(-31));

        aiClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Continuing...");

        var svc = new ChatService(aiClient, moderation, conversations, childService, config, logger);
        await svc.GetResponseAsync(deviceId, "asdfgh");

        // Expired labels should NOT inject any previous_story_choice hint
        await aiClient.Received().GetCompletionAsync(
            Arg.Is<string>(s => !s.Contains("previous_story_choice:")),
            Arg.Any<List<(string, string)>>());
    }
}

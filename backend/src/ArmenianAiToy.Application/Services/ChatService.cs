using System.Collections.Concurrent;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ArmenianAiToy.Application.Services;

public class ChatService : IChatService
{
    private const string StoryChoiceInstruction = """

        STORY CHOICES: When you offer the child exactly two choices in a story,
        end your message with a separator and two labeled lines, like this:
        ---
        CHOICE_A:first option here
        CHOICE_B:second option here
        Do not include this block when you are not offering a choice.
        Each choice should be short, action-based, clearly different from the
        other, and simple enough for a child aged 4–7 to say back easily.

        If a previous_story_choice is provided, continue the story following
        that choice. Do not ask the child to choose again, do not ignore the
        choice, and do not re-offer the same options.

        If previous_story_choice is "unclear", the child tried to answer but
        their reply was not understood. First respond naturally to what the
        child said, then gently move the story forward — a new event, a soft
        resolution, or a small scene change. Do not stay frozen in the same
        moment. Do not say "since you didn't choose" or blame the child.
        Do not include a CHOICE_A/CHOICE_B block in this response.
        """;

    // One-shot in-memory store for option labels extracted from the previous
    // assistant response. Keyed by conversation ID. Consumed and removed on
    // the next child message. Entries older than 30 minutes are discarded.
    internal static readonly ConcurrentDictionary<Guid, PendingChoice> PendingChoices = new();
    private static readonly TimeSpan ChoiceExpiry = TimeSpan.FromMinutes(30);

    internal record PendingChoice(string OptionA, string OptionB, DateTime ExtractedAt);

    private static readonly string[] StoryTriggerPhrases =
    [
        "tell me a story",
        "tell a story",
        "what happens next",
        "\u057a\u0561\u057f\u0574\u056b\u0580",                             // patmir
        "\u057a\u0561\u057f\u0574\u0578\u0582\u0569\u0575\u0578\u0582\u0576", // patmutyun
        "\u0570\u0565\u0584\u056b\u0561\u0569",                             // heqiat
        "\u056b\u0576\u0579 \u056f\u056c\u056b\u0576\u056b",                 // inch klini
    ];

    private readonly IAiChatClient _aiClient;
    private readonly IModerationService _moderation;
    private readonly IConversationService _conversations;
    private readonly IChildService _childService;
    private readonly IConfiguration _config;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        IAiChatClient aiClient,
        IModerationService moderation,
        IConversationService conversations,
        IChildService childService,
        IConfiguration config,
        ILogger<ChatService> logger)
    {
        _aiClient = aiClient;
        _moderation = moderation;
        _conversations = conversations;
        _childService = childService;
        _config = config;
        _logger = logger;
    }

    public async Task<ChatResponse> GetResponseAsync(Guid deviceId, string userMessage, Guid? childId = null)
    {
        // Load child profile (use provided childId or default child for device)
        var child = childId.HasValue
            ? await _childService.GetChildAsync(childId.Value)
            : await _childService.GetDefaultChildForDeviceAsync(deviceId);

        var conversation = await _conversations.GetOrCreateActiveConversationAsync(deviceId, child?.Id);

        // Step 1: Consume pending choice labels (always remove to prevent stale entries)
        PendingChoices.TryRemove(conversation.Id, out var pending);

        // Step 2: Pre-moderate user input
        var inputModeration = await _moderation.CheckContentAsync(userMessage);
        if (!inputModeration.IsSafe)
        {
            _logger.LogWarning("User input blocked. Device: {DeviceId}, Categories: {Categories}",
                deviceId, string.Join(", ", inputModeration.FlaggedCategories));

            await _conversations.AddMessageAsync(
                conversation.Id, MessageRole.User, userMessage, SafetyFlag.Blocked);

            var fallback = _config["SafetyFallbackResponse"]
                ?? "Let's talk about something more fun!";
            var fallbackMsg = await _conversations.AddMessageAsync(
                conversation.Id, MessageRole.Assistant, fallback, SafetyFlag.Clean);

            return new ChatResponse(fallback, conversation.Id, fallbackMsg.Id, SafetyFlag.Blocked);
        }

        // Step 3: Store user message
        await _conversations.AddMessageAsync(conversation.Id, MessageRole.User, userMessage);

        // Step 4: Normalize child input against pending choice labels (best-effort)
        string? normalizedChoice = null;
        if (pending is not null && DateTime.UtcNow - pending.ExtractedAt < ChoiceExpiry)
        {
            var choiceResult = ChoiceNormalizer.Normalize(userMessage, pending.OptionA, pending.OptionB);
            _logger.LogInformation(
                "Choice normalized. ConversationId: {ConversationId}, Normalized: {Normalized}, Confidence: {Confidence}, Method: {Method}",
                conversation.Id, choiceResult.Normalized, choiceResult.Confidence, choiceResult.Method);

            if (choiceResult.Normalized is "option_a" or "option_b")
                normalizedChoice = choiceResult.Normalized;
        }

        // Step 5: Build system prompt with child context
        var systemPrompt = _config["SystemPrompt"] ?? "You are a friendly assistant for Armenian children. Reply in Armenian.";

        if (child != null)
        {
            systemPrompt += _childService.BuildChildContext(child);
        }

        // Step 6: Build conversation history
        var history = await _conversations.GetRecentMessagesAsync(conversation.Id);

        // Step 7: Append story-choice instruction if conversation has story intent
        if (HasStoryIntent(userMessage, history))
        {
            systemPrompt += StoryChoiceInstruction;

            // Step 7b: If the child made a recognized choice, hint the model.
            // If there was a pending choice but the reply was unclear, hint that too.
            if (normalizedChoice is not null)
            {
                systemPrompt += $"\n\nprevious_story_choice: {normalizedChoice}";
            }
            else if (pending is not null && DateTime.UtcNow - pending.ExtractedAt < ChoiceExpiry)
            {
                systemPrompt += "\n\nprevious_story_choice: unclear";
            }
        }

        // Step 8: Call AI
        string aiResponse;
        try
        {
            aiResponse = await _aiClient.GetCompletionAsync(systemPrompt, history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI service error for device {DeviceId}", deviceId);
            throw;
        }

        // Step 9: Post-moderate AI response
        var outputModeration = await _moderation.CheckContentAsync(aiResponse);
        var safetyFlag = SafetyFlag.Clean;

        if (!outputModeration.IsSafe)
        {
            _logger.LogWarning("AI response flagged. Device: {DeviceId}", deviceId);
            safetyFlag = SafetyFlag.Flagged;
            aiResponse = _config["SafetyFallbackResponse"]
                ?? "Let's talk about something more fun!";
        }

        // Step 10: Strip tail block and store labels for next request
        if (TailBlockParser.TryExtract(aiResponse, out var cleanedResponse, out var optionA, out var optionB))
        {
            PendingChoices[conversation.Id] = new PendingChoice(optionA!, optionB!, DateTime.UtcNow);
            _logger.LogInformation(
                "Story choice extracted. ConversationId: {ConversationId}, OptionA: {OptionA}, OptionB: {OptionB}",
                conversation.Id, optionA, optionB);
            aiResponse = cleanedResponse;
        }

        // Step 11: Store AI response
        var responseMsg = await _conversations.AddMessageAsync(
            conversation.Id, MessageRole.Assistant, aiResponse, safetyFlag);

        return new ChatResponse(aiResponse, conversation.Id, responseMsg.Id, safetyFlag);
    }

    internal static bool HasStoryIntent(
        string userMessage, List<(string Role, string Content)> history)
    {
        if (MatchesAnyTrigger(userMessage)) return true;

        int checkedCount = 0;
        for (int i = history.Count - 1; i >= 0 && checkedCount < 2; i--)
        {
            if (history[i].Role == "User")
            {
                if (MatchesAnyTrigger(history[i].Content)) return true;
                checkedCount++;
            }
        }

        return false;
    }

    private static bool MatchesAnyTrigger(string text)
    {
        var lower = text.ToLowerInvariant();
        return StoryTriggerPhrases.Any(p => lower.Contains(p));
    }
}

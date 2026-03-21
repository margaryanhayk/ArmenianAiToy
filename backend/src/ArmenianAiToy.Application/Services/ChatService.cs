using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ArmenianAiToy.Application.Services;

public class ChatService : IChatService
{
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

        // Step 1: Pre-moderate user input
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

        // Step 2: Store user message
        await _conversations.AddMessageAsync(conversation.Id, MessageRole.User, userMessage);

        // Step 3: Build system prompt with child context
        var systemPrompt = _config["SystemPrompt"] ?? "You are a friendly assistant for Armenian children. Reply in Armenian.";

        if (child != null)
        {
            systemPrompt += _childService.BuildChildContext(child);
        }

        // Step 4: Build conversation history
        var history = await _conversations.GetRecentMessagesAsync(conversation.Id);

        // Step 5: Call AI
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

        // Step 6: Post-moderate AI response
        var outputModeration = await _moderation.CheckContentAsync(aiResponse);
        var safetyFlag = SafetyFlag.Clean;

        if (!outputModeration.IsSafe)
        {
            _logger.LogWarning("AI response flagged. Device: {DeviceId}", deviceId);
            safetyFlag = SafetyFlag.Flagged;
            aiResponse = _config["SafetyFallbackResponse"]
                ?? "Let's talk about something more fun!";
        }

        // Step 7: Store AI response
        var responseMsg = await _conversations.AddMessageAsync(
            conversation.Id, MessageRole.Assistant, aiResponse, safetyFlag);

        return new ChatResponse(aiResponse, conversation.Id, responseMsg.Id, safetyFlag);
    }
}

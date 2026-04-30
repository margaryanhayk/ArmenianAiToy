using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;

namespace ArmenianAiToy.Application.Interfaces;

public interface IConversationService
{
    Task<Conversation> GetOrCreateActiveConversationAsync(Guid deviceId, Guid? childId);
    Task<Message> AddMessageAsync(Guid conversationId, MessageRole role, string content, SafetyFlag flag = SafetyFlag.Clean);
    Task<List<(string Role, string Content)>> GetRecentMessagesAsync(Guid conversationId, int count = 20);
    Task<List<ConversationDto>> GetConversationHistoryAsync(Guid deviceId, int limit = 10, int offset = 0);
    Task<ConversationDto?> GetConversationByIdAsync(Guid conversationId);
    Task<List<ConversationSummaryDto>> GetConversationSummariesAsync(Guid deviceId, int limit = 20, int offset = 0);
    Task<List<FlaggedMessageDto>> GetFlaggedMessagesAsync(Guid deviceId, int limit = 20, int offset = 0);

    /// <summary>
    /// E1.2 server-side aggregation for the parent dashboard's Today panel.
    /// Returns counts and per-conversation links scoped to today (UTC) on
    /// the given device. Caller is expected to enforce ownership BEFORE
    /// calling this method (the controller does so via the existing
    /// GetLinkedDeviceIdsAsync gate).
    /// </summary>
    Task<TodaySummaryDto> GetTodaySummaryAsync(Guid deviceId, DateTime asOfUtc);
}

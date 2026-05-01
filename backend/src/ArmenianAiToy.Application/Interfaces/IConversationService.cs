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
    /// Returns counts and per-conversation links scoped to today on the
    /// given device. Caller is expected to enforce ownership BEFORE
    /// calling this method (the controller does so via the existing
    /// GetLinkedDeviceIdsAsync gate).
    /// <para>
    /// E2.1 — <paramref name="tz"/> is an optional IANA time-zone id that
    /// overrides the device's stored <c>TimeZone</c>. Resolution
    /// precedence: explicit <paramref name="tz"/> &gt; <c>Device.TimeZone</c>
    /// &gt; <c>"UTC"</c>. Unresolvable ids fail soft to UTC; the DTO
    /// returns <c>TimeZoneResolved=false</c> and echoes the attempted id.
    /// </para>
    /// </summary>
    Task<TodaySummaryDto> GetTodaySummaryAsync(Guid deviceId, DateTime asOfUtc, string? tz = null);
}

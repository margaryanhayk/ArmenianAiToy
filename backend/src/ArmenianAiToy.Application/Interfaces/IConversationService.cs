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
}

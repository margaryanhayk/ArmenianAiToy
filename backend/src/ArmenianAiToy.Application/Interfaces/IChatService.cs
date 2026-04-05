using ArmenianAiToy.Application.DTOs;

namespace ArmenianAiToy.Application.Interfaces;

public interface IChatService
{
    Task<ChatResponse> GetResponseAsync(Guid deviceId, string userMessage, Guid? childId = null,
        Guid? storySessionId = null, string? selectedChoice = null);
}

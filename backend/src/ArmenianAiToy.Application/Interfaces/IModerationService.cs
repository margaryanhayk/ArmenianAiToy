using ArmenianAiToy.Application.DTOs;

namespace ArmenianAiToy.Application.Interfaces;

public interface IModerationService
{
    Task<ModerationResult> CheckContentAsync(string content);
}

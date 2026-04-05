using ArmenianAiToy.Domain.Enums;

namespace ArmenianAiToy.Application.DTOs;

public record ChatResponse(
    string Response,
    Guid ConversationId,
    Guid MessageId,
    SafetyFlag SafetyFlag,
    string? ChoiceA = null,
    string? ChoiceB = null,
    Guid? StorySessionId = null);

using ArmenianAiToy.Domain.Enums;

namespace ArmenianAiToy.Application.DTOs;

public record ConversationDto(
    Guid Id,
    Guid DeviceId,
    DateTime StartedAt,
    DateTime? EndedAt,
    int MessageCount,
    bool HasFlaggedContent,
    List<MessageDto> Messages);

public record MessageDto(
    Guid Id,
    string Role,
    string Content,
    DateTime Timestamp,
    SafetyFlag SafetyFlag);

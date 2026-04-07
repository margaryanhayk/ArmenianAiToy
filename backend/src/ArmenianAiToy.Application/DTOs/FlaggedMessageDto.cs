using ArmenianAiToy.Domain.Enums;

namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Flat row for the parent flagged-messages quick-review endpoint.
/// Carries enough context for parent review and a jump-to-conversation link,
/// without hydrating full conversation message lists.
/// </summary>
public record FlaggedMessageDto(
    Guid MessageId,
    Guid ConversationId,
    DateTime ConversationStartedAt,
    string Role,
    string Content,
    DateTime Timestamp,
    SafetyFlag SafetyFlag);

using ArmenianAiToy.Domain.Enums;

namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Lightweight per-conversation row for the parent summary list endpoint.
/// Excludes full message lists; carries only counts, flags, and short snippets.
/// </summary>
public record ConversationSummaryDto(
    Guid Id,
    DateTime StartedAt,
    DateTime? EndedAt,
    int MessageCount,
    bool HasFlaggedContent,
    string? FirstUserSnippet,
    string? LastAssistantSnippet,
    SafetyFlag? LastAssistantSafetyFlag = null,
    int FlaggedMessageCount = 0,
    // E1.3: distinct runtime-stamped modes present in this conversation
    // ("story", "game", …), in no guaranteed order. Empty for
    // conversations recorded before mode stamping existed. Additive.
    List<string>? Modes = null);

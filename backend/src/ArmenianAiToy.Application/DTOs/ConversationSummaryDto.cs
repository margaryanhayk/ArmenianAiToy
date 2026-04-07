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
    string? LastAssistantSnippet);

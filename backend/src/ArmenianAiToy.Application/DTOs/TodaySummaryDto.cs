namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// E1.2 — server-aggregated daily snapshot for the parent dashboard's
/// Today panel. Returned by GET /api/conversations/today-summary.
/// All timestamps are UTC; the day boundary is computed in UTC.
///
/// Counts are EXACT per-message (not whole-conversation) and live
/// alongside the existing /summary, /flagged, and /{id} endpoints
/// under ConversationController. Modes-used-today is deliberately NOT
/// included — DetectedMode is not persisted today (lives only in the
/// in-memory ChatService.ActiveModes dictionary), so any aggregate
/// here would diverge from runtime resolution. Deferred to E1.3.
///
/// The response intentionally does NOT expose ChildId or AudioBlobPath:
///  - per-child filtering is a separate concern (would need ChildId in
///    the response and an explicit per-child authorization step);
///  - audio paths are server-internal and never leave ConversationService.
/// AssistantMessagesWithAudio uses the same role gate as MessageDto's
/// AudioAvailable contract (Role == Assistant AND AudioBlobPath != null);
/// child WAV uploads cannot contribute to the count.
/// </summary>
public record TodaySummaryDto(
    Guid DeviceId,
    DateTime AsOfUtc,
    DateTime DayStartUtc,
    int ConversationsCount,
    int MessagesCount,
    int FlaggedMessagesCount,
    int AssistantMessagesWithAudio,
    List<TodaySummaryConversationLink> Newest,
    List<TodaySummaryConversationLink> Flagged);

public record TodaySummaryConversationLink(
    Guid Id,
    DateTime StartedAt,
    string? FirstUserSnippet,
    int MessageCountToday,
    int FlaggedMessageCountToday);

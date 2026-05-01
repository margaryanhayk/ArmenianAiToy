namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// E1.2 — server-aggregated daily snapshot for the parent dashboard's
/// Today panel. Returned by GET /api/conversations/today-summary.
///
/// E2.1 — the day boundary is computed in the device's local time zone
/// by default (per <see cref="ArmenianAiToy.Domain.Entities.Device.TimeZone"/>),
/// with an optional <c>?tz=&lt;IANA&gt;</c> override on the controller.
/// Unresolvable time-zone ids fail soft to UTC: <see cref="TimeZoneResolved"/>
/// flips to <c>false</c> and <see cref="TimeZoneId"/> echoes the attempted
/// id (or <c>"UTC"</c> when no id was attempted). The fail-soft contract
/// matches <see cref="ArmenianAiToy.Application.Helpers.BedtimeWindowEvaluator"/>.
///
/// Counts are EXACT per-message (not whole-conversation). All
/// <c>Timestamp &gt;= DayStartUtc</c> filters use the converted UTC
/// instant of midnight in the resolved time zone, so messages that
/// crossed midnight in the wrong direction are not double-counted.
///
/// Modes-used-today is deliberately NOT included — DetectedMode is not
/// persisted today (lives only in the in-memory ChatService.ActiveModes
/// dictionary), so any aggregate here would diverge from runtime
/// resolution. Deferred to E1.3.
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
    DateTime DayStartLocal,
    string TimeZoneId,
    bool TimeZoneResolved,
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

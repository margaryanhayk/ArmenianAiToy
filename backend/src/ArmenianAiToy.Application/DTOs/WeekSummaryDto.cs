namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Parent dashboard "This week" overview — a 7-day, server-aggregated
/// activity snapshot for an owned device. Returned by
/// GET /api/conversations/week-summary. Complements the E1.2
/// <see cref="TodaySummaryDto"/> (single day) with a rolling week and a
/// per-day breakdown so a parent sees the week at a glance.
///
/// The window is the last 7 calendar days INCLUDING today, computed in
/// the device's local time zone (same resolution chain + fail-soft-to-UTC
/// contract as <see cref="TodaySummaryDto"/>): explicit <c>?tz=</c> &gt;
/// <c>Device.TimeZone</c> &gt; <c>"UTC"</c>. Each day's local boundary is
/// converted back to a UTC instant so DST transitions inside the window
/// are handled correctly.
///
/// Counts are EXACT per-message. <see cref="Days"/> always carries 7
/// entries (oldest first), including zero-activity days, so the dashboard
/// can render a stable 7-bar strip.
///
/// Privacy mirrors <see cref="TodaySummaryDto"/>: the response exposes no
/// <c>ChildId</c>, no <c>AudioBlobPath</c>, and no message content — only
/// counts. <see cref="AssistantMessagesWithAudio"/> uses the same role
/// gate as MessageDto's AudioAvailable contract (Role == Assistant AND
/// AudioBlobPath != null), so child WAV uploads cannot contribute.
/// </summary>
public record WeekSummaryDto(
    Guid DeviceId,
    DateTime AsOfUtc,
    DateTime WindowStartUtc,
    DateTime WindowStartLocal,
    string TimeZoneId,
    bool TimeZoneResolved,
    int ConversationsCount,
    int MessagesCount,
    int FlaggedMessagesCount,
    int AssistantMessagesWithAudio,
    int ActiveDays,
    List<WeekDaySummary> Days);

/// <summary>One local calendar day inside the week window. Counts are
/// per-message and scoped to this day in the resolved time zone.</summary>
public record WeekDaySummary(
    DateTime DateLocal,
    int MessagesCount,
    int FlaggedMessagesCount);

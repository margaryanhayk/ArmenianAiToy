namespace ArmenianAiToy.Application.Notifications;

/// <summary>
/// Counts-only payload for a parent's weekly activity digest email.
/// Deliberately carries NO PII and NO content — no child names, no
/// message text, no device names, no conversation ids — only aggregate
/// counts over the 7-day window, mirroring the privacy posture of the
/// parent dashboard's week-summary. The digest is a nudge back to the
/// dashboard, not a second copy of the child's data.
/// </summary>
public sealed record WeeklyDigestSummary(
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    int DeviceCount,
    int ConversationCount,
    int MessageCount,
    int ActiveDays);

using Microsoft.Extensions.Configuration;

namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// #067 — single source of truth for reading the message-retention policy in
/// a form both the parent-facing export (disclosure) and startup (operator
/// alert) can consume. The authoritative purge logic lives in
/// <c>RetentionPurgeService</c> (Infrastructure); this is a read-only
/// projection of the SAME config key with the SAME semantics so the two never
/// disagree about whether deletion is on.
///
/// Semantics (mirror <c>RetentionPurgeService</c>): missing / unparseable
/// resolves to the shipped default (<see cref="DefaultMessagesMaxAgeDays"/>,
/// never 0); a non-positive explicit override DISABLES automatic deletion.
/// </summary>
public static class RetentionPolicy
{
    // Keep in sync with RetentionPurgeService.DefaultMaxAgeDays (Infrastructure).
    public const int DefaultMessagesMaxAgeDays = 90;

    /// <summary>Effective message retention: whether automatic deletion is on,
    /// and the age threshold in days (0 when disabled).</summary>
    public static (bool Enabled, int Days) ResolveMessages(IConfiguration config)
    {
        var raw = config["Retention:Messages:MaxAgeDays"];
        var days = int.TryParse(raw, out var v) ? v : DefaultMessagesMaxAgeDays;
        return days > 0 ? (true, days) : (false, 0);
    }

    /// <summary>A neutral, parent-readable description of the retention policy.</summary>
    public static string DescribeForParent(IConfiguration config)
    {
        var (enabled, days) = ResolveMessages(config);
        return enabled
            ? $"Conversations and their messages are automatically and permanently " +
              $"deleted {days} days after the conversation's last activity."
            : "Automatic deletion of conversations and messages is currently turned " +
              "off on this deployment; data is retained until it is removed manually " +
              "or you delete it from the dashboard.";
    }
}

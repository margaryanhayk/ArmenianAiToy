using ArmenianAiToy.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace ArmenianAiToy.Infrastructure.Notifications;

/// <summary>
/// Default <see cref="INotifier"/> implementation. Writes exactly one
/// structured <c>ILogger.LogInformation</c> line per send attempt and
/// does not actually deliver anything. This is the log-only transport
/// the forgot-password slice ships with — a future deploy slice can
/// register a second implementation without changing any caller.
///
/// <para>
/// <b>No-raw-token invariant.</b> The structured payload carries only
/// the notification type, the target email, and a constant
/// <c>delivered=false</c> marker. The raw <c>resetToken</c> parameter
/// is NEVER placed into the log (not even a prefix — prefix leakage is
/// still leakage). This is pinned by
/// <c>LoggingNotifierTests.SendPasswordResetAsync_DoesNotLogRawToken</c>.
/// </para>
/// </summary>
public sealed class LoggingNotifier : INotifier
{
    private readonly ILogger<LoggingNotifier> _logger;

    public LoggingNotifier(ILogger<LoggingNotifier> logger)
    {
        _logger = logger;
    }

    public Task SendPasswordResetAsync(
        string email, string resetToken, CancellationToken cancellationToken = default)
    {
        // resetToken is deliberately NOT in the template holes. Pinned
        // by a dedicated test — the token must never leak into stdout.
        _logger.LogInformation(
            "Notification send-attempt: type={NotificationType}, email={Email}, delivered={Delivered}",
            "password_reset",
            email,
            false);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Log-only implementation of the dormancy warning — always
    /// "delivered" to the log, never throws, so the worker will stamp
    /// and audit on every call. This is why the DI-time precondition
    /// rejects <c>WarnAfterDays &gt; 0</c> paired with log transport:
    /// LoggingNotifier returning <c>true</c> with no actual email
    /// delivery would let the worker mark parents as warned without
    /// reaching them.
    /// </summary>
    public Task<bool> SendDormancyWarningAsync(
        string email, DateTime? deleteAtUtc, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Notification send-attempt: type={NotificationType}, email={Email}, delivered={Delivered}",
            "dormancy_warning",
            email,
            false);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Log-only implementation of the email-verification send.
    /// Never logs the raw token (pinned by test). Does not actually
    /// deliver anything — a parent receiving no verification link on
    /// a log-transport deployment still has access to every other
    /// flow (login, forgot-password, etc.); verification is tracking-
    /// only in T1 and nothing breaks when the email doesn't land.
    /// </summary>
    public Task SendEmailVerificationAsync(
        string email, string verificationToken, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Notification send-attempt: type={NotificationType}, email={Email}, delivered={Delivered}",
            "email_verification",
            email,
            false);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Log-only implementation of the dormant-device warning. Always
    /// returns true so any worker consumer with a log-only transport
    /// would naively mark every device as warned. The
    /// DormancyTransportPrecondition's third guard (added with this
    /// slice) rejects WarnAfterDays > 0 paired with log transport at
    /// DI-time, which is the actual safeguard. This impl exists so
    /// log-transport environments don't crash if the worker pass
    /// somehow fires (e.g. in test harnesses).
    /// </summary>
    public Task<bool> SendDormantDeviceWarningAsync(
        string parentEmail, string deviceName, DateTime lastSeenAtUtc,
        DateTime? deleteAtUtc, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Notification send-attempt: type={NotificationType}, email={Email}, device_name={DeviceName}, last_seen_at_utc={LastSeenAtUtc:O}, delivered={Delivered}",
            "dormant_device_warning",
            parentEmail,
            deviceName,
            lastSeenAtUtc,
            false);
        return Task.FromResult(true);
    }
}

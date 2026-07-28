using System.Net;
using System.Net.Mail;
using System.Text;
using ArmenianAiToy.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ArmenianAiToy.Infrastructure.Notifications;

/// <summary>
/// Minimal BCL SMTP <see cref="INotifier"/> implementation. Uses
/// <see cref="SmtpClient"/> from <c>System.Net.Mail</c> — no new NuGet,
/// works against any relay that speaks SMTP (including the SMTP
/// endpoints offered by SendGrid / SES / Mailgun / Postmark). Armenian-
/// first body copy, plain-text only.
///
/// <para>
/// <b>Failure containment (hard invariant).</b> The forgot-password
/// request endpoint returns <c>202 Accepted</c> with an
/// enumeration-resistant body; a broken SMTP relay must not convert
/// that contract into a 500. Any non-cancellation exception from the
/// send path is caught here, converted to a structured warning log,
/// and swallowed. <see cref="System.OperationCanceledException"/> is
/// deliberately NOT caught — if the HTTP request itself is cancelled
/// mid-flight, the cancellation propagates normally.
/// </para>
///
/// <para>
/// <b>No-raw-token invariant.</b> The raw reset token is placed into
/// the outgoing message body (it has to be; that's the whole point of
/// sending the email). It is NEVER logged — not even a prefix. Pinned
/// by <c>SmtpNotifierTests.SendPasswordResetAsync_DoesNotLogRawToken</c>.
/// </para>
///
/// <para>
/// <b>Internal send seam.</b> The actual wire call is behind a
/// <see cref="SendMailDelegate"/> seam so unit tests can substitute a
/// throwing / capturing fake without standing up a real SMTP server.
/// This mirrors the <c>_hashPassword</c> spy seam <c>ParentService</c>
/// already uses for BCrypt.
/// </para>
///
/// <para>
/// <b>SmtpClient deprecation note.</b> Microsoft documents
/// <see cref="SmtpClient"/> as "not recommended for new development"
/// in favour of MailKit. For this repo's stage (single low-volume
/// transactional-email path, forgot-password only, no bounce
/// handling) the BCL implementation is intentional — it keeps the
/// repo's no-new-NuGet discipline. If operational experience later
/// forces a change, swapping internals for MailKit is a one-file
/// drop-in against the unchanged <see cref="INotifier"/> seam.
/// </para>
/// </summary>
public sealed class SmtpNotifier : INotifier
{
    /// <summary>
    /// Test-facing seam for the actual SMTP wire call. Default
    /// implementation uses <see cref="SmtpClient.SendMailAsync(MailMessage, System.Threading.CancellationToken)"/>
    /// with credentials resolved from <c>Notifications:Smtp:*</c>.
    /// </summary>
    internal delegate Task SendMailDelegate(
        MailMessage message, CancellationToken cancellationToken);

    private readonly ILogger<SmtpNotifier> _logger;
    private readonly IConfiguration _config;
    private readonly SendMailDelegate _sendMail;

    // Standard DI constructor.
    public SmtpNotifier(ILogger<SmtpNotifier> logger, IConfiguration config)
        : this(logger, config, sendMail: null)
    {
    }

    // Test-only constructor. `internal` so it participates in the
    // InternalsVisibleTo test assembly if needed, and optional so the
    // DI path cannot accidentally pick the wrong overload.
    internal SmtpNotifier(
        ILogger<SmtpNotifier> logger,
        IConfiguration config,
        SendMailDelegate? sendMail)
    {
        _logger = logger;
        _config = config;
        _sendMail = sendMail ?? DefaultSendAsync;
    }

    public async Task SendPasswordResetAsync(
        string email, string resetToken, CancellationToken cancellationToken = default)
    {
        // Compose the message inside the notifier — no external
        // templating, no extra seam. MailMessage is disposable; a
        // single `using` keeps cleanup obvious. Subjects / bodies /
        // links come from NotificationEmailContent, shared with the
        // Resend transport so the two can never drift.
        var link = NotificationEmailContent.BuildResetLink(
            _config["Notifications:PasswordResetLinkBase"] ?? "", resetToken);
        var fromAddress = _config["Notifications:Smtp:FromAddress"] ?? "";

        using var message = new MailMessage
        {
            From = new MailAddress(fromAddress),
            Subject = NotificationEmailContent.PasswordResetSubject,
            Body = NotificationEmailContent.BuildPasswordResetBody(link),
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };
        message.To.Add(email);

        try
        {
            await _sendMail(message, cancellationToken);
            // Token deliberately NOT in the template holes — see class
            // xmldoc. `email` is fine (this is the sole place that pairs
            // "we attempted delivery" with "to whom"; there is no audit
            // row for per-send outcomes by design).
            _logger.LogInformation(
                "Notification send-attempt: type={NotificationType}, email={Email}, transport={Transport}, delivered={Delivered}",
                "password_reset", email, "smtp", true);
        }
        catch (OperationCanceledException)
        {
            // Caller cancellation propagates — this is not an SMTP
            // failure, it is the HTTP request going away.
            throw;
        }
        catch (Exception ex)
        {
            // Swallow-and-log. Preserves the 202 Accepted contract on
            // POST /api/parents/password/reset-request. Token never
            // appears in the log template holes. Exception type name
            // (e.g. SmtpException, SmtpFailedRecipientException,
            // AuthenticationException) is the only "what failed" signal
            // emitted here — full stack trace is carried as the first
            // argument to LogWarning so the structured log pipeline
            // can expose it without leaking the token.
            _logger.LogWarning(ex,
                "Notification send-attempt: type={NotificationType}, email={Email}, transport={Transport}, delivered={Delivered}, error_category={ErrorCategory}",
                "password_reset", email, "smtp", false, ex.GetType().Name);
        }
    }

    /// <summary>
    /// Dormant-account warning email to a parent whose
    /// <c>LastLoginAt</c> is past the configured threshold. Returns
    /// <c>true</c> on successful delivery, <c>false</c> on a
    /// swallowed send failure — the calling worker uses this bool to
    /// decide whether to stamp <c>DormancyWarnedAt</c> and write an
    /// audit row. Deliberately different from
    /// <see cref="SendPasswordResetAsync"/>'s fire-and-log-only
    /// behavior because the dormancy caller owns retry semantics and
    /// needs the outcome.
    /// <para>
    /// <paramref name="deleteAtUtc"/> is unused by this slice (warn-
    /// only); the outgoing copy never references an imminent delete
    /// date. The parameter is carried on the interface so the later
    /// delete-action slice can populate it without a signature change.
    /// </para>
    /// <para>
    /// Same Armenian-first plain-text shape as the reset email and
    /// the same no-secret-logging discipline — only the recipient
    /// address, the notification type, the transport, the
    /// delivered-bool, and (on failure) the exception type name land
    /// in the log.
    /// </para>
    /// </summary>
    public async Task<bool> SendDormancyWarningAsync(
        string email, DateTime? deleteAtUtc, CancellationToken cancellationToken = default)
    {
        // deleteAtUtc carries the scheduled destructive-action date
        // when the anonymize pass is enabled. Null when destructive is
        // disabled (warn-only mode). The body helper renders an extra
        // sentence naming the date only when non-null, so parents who
        // are merely being nudged do not see scary "will be removed"
        // copy.
        var fromAddress = _config["Notifications:Smtp:FromAddress"] ?? "";

        using var message = new MailMessage
        {
            From = new MailAddress(fromAddress),
            Subject = NotificationEmailContent.DormancyWarningSubject,
            Body = NotificationEmailContent.BuildDormancyWarningBody(deleteAtUtc),
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };
        message.To.Add(email);

        try
        {
            await _sendMail(message, cancellationToken);
            _logger.LogInformation(
                "Notification send-attempt: type={NotificationType}, email={Email}, transport={Transport}, delivered={Delivered}",
                "dormancy_warning", email, "smtp", true);
            return true;
        }
        catch (OperationCanceledException)
        {
            // Worker cancellation propagates — lets the hosted service
            // shut down cleanly rather than continuing a zombie send.
            throw;
        }
        catch (Exception ex)
        {
            // Swallow the exception, log a structured warning, and
            // signal "not delivered" back to the worker. The worker
            // will skip the DormancyWarnedAt stamp and the audit row,
            // so the next tick retries this parent. Exception type is
            // the only "what broke" signal in the log template; full
            // stack trace travels as the LogWarning first argument.
            _logger.LogWarning(ex,
                "Notification send-attempt: type={NotificationType}, email={Email}, transport={Transport}, delivered={Delivered}, error_category={ErrorCategory}",
                "dormancy_warning", email, "smtp", false, ex.GetType().Name);
            return false;
        }
    }

    /// <summary>
    /// Email-verification link send. Reuses
    /// <c>Notifications:PasswordResetLinkBase</c> for the URL prefix
    /// — the dashboard page is the same; only the query-param key
    /// (<c>verifyToken</c>) disambiguates from the forgot-password
    /// <c>token</c> parameter.
    /// <para>
    /// Same failure-containment as <see cref="SendPasswordResetAsync"/>:
    /// non-cancellation exceptions are swallowed into a structured
    /// warning log and do not propagate. The HTTP-synchronous
    /// caller's anti-enum response shape is preserved regardless of
    /// delivery outcome.
    /// </para>
    /// <para>
    /// <b>No-raw-token invariant.</b> The raw verification token
    /// appears only in the outgoing message body. It is NEVER placed
    /// into a log line — same pinned contract as
    /// <see cref="SendPasswordResetAsync"/>.
    /// </para>
    /// </summary>
    public async Task SendEmailVerificationAsync(
        string email, string verificationToken, CancellationToken cancellationToken = default)
    {
        var link = NotificationEmailContent.BuildVerificationLink(
            _config["Notifications:PasswordResetLinkBase"] ?? "", verificationToken);
        var fromAddress = _config["Notifications:Smtp:FromAddress"] ?? "";

        using var message = new MailMessage
        {
            From = new MailAddress(fromAddress),
            Subject = NotificationEmailContent.EmailVerificationSubject,
            Body = NotificationEmailContent.BuildEmailVerificationBody(link),
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };
        message.To.Add(email);

        try
        {
            await _sendMail(message, cancellationToken);
            _logger.LogInformation(
                "Notification send-attempt: type={NotificationType}, email={Email}, transport={Transport}, delivered={Delivered}",
                "email_verification", email, "smtp", true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Notification send-attempt: type={NotificationType}, email={Email}, transport={Transport}, delivered={Delivered}, error_category={ErrorCategory}",
                "email_verification", email, "smtp", false, ex.GetType().Name);
        }
    }

    /// <summary>
    /// Dormant-device warning email to one verified linked parent
    /// of a device that has not checked in for the configured
    /// threshold. Returns <c>true</c> on successful delivery,
    /// <c>false</c> on a swallowed send failure. The calling worker
    /// aggregates per-recipient bools across the device's verified
    /// linked parents — see CLAUDE.md § Retention for the fan-out
    /// + partial-failure rules.
    /// <para>
    /// <paramref name="deleteAtUtc"/> is reserved-but-null in the
    /// warn-only first slice; the body MUST NOT include any
    /// destructive-date copy when this parameter is null. The
    /// parameter exists today so the future destructive-device
    /// slice is a body-only change.
    /// </para>
    /// <para>
    /// Same swallow-and-log failure posture as the parent dormancy
    /// warning — non-cancellation exceptions become a structured
    /// warning log + return false; OperationCanceledException
    /// propagates so the worker can shut down cleanly.
    /// </para>
    /// </summary>
    public async Task<bool> SendDormantDeviceWarningAsync(
        string parentEmail,
        string deviceName,
        DateTime lastSeenAtUtc,
        DateTime? deleteAtUtc,
        CancellationToken cancellationToken = default)
    {
        var fromAddress = _config["Notifications:Smtp:FromAddress"] ?? "";

        using var message = new MailMessage
        {
            From = new MailAddress(fromAddress),
            Subject = NotificationEmailContent.DormantDeviceWarningSubject,
            Body = NotificationEmailContent.BuildDormantDeviceWarningBody(
                deviceName, lastSeenAtUtc, deleteAtUtc),
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };
        message.To.Add(parentEmail);

        try
        {
            await _sendMail(message, cancellationToken);
            _logger.LogInformation(
                "Notification send-attempt: type={NotificationType}, email={Email}, device_name={DeviceName}, transport={Transport}, delivered={Delivered}",
                "dormant_device_warning", parentEmail, deviceName, "smtp", true);
            return true;
        }
        catch (OperationCanceledException)
        {
            // Worker cancellation propagates — host shutdown.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Notification send-attempt: type={NotificationType}, email={Email}, device_name={DeviceName}, transport={Transport}, delivered={Delivered}, error_category={ErrorCategory}",
                "dormant_device_warning", parentEmail, deviceName, "smtp", false, ex.GetType().Name);
            return false;
        }
    }

    // Default wire call. Pulls credentials / host / port off
    // IConfiguration on every send so a config reload (via a restart)
    // is picked up without recycling the singleton. Scoped registration
    // in DI also means a new instance per request scope anyway.
    private async Task DefaultSendAsync(
        MailMessage message, CancellationToken cancellationToken)
    {
        var host = _config["Notifications:Smtp:Host"] ?? "";
        var port = int.TryParse(_config["Notifications:Smtp:Port"], out var p) ? p : 25;
        var useSsl = bool.TryParse(_config["Notifications:Smtp:UseSsl"], out var s) && s;
        var username = _config["Notifications:Smtp:Username"] ?? "";
        var password = _config["Notifications:Smtp:Password"] ?? "";

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = useSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        // Anonymous SMTP (local MTA, no-auth internal relay) is a
        // legitimate dev/staging shape — only attach credentials when
        // both fields are actually populated.
        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            client.Credentials = new NetworkCredential(username, password);

        await client.SendMailAsync(message, cancellationToken);
    }
}

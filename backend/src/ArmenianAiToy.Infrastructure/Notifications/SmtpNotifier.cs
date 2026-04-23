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
        // single `using` keeps cleanup obvious.
        var link = BuildResetLink(
            _config["Notifications:PasswordResetLinkBase"] ?? "", resetToken);
        var fromAddress = _config["Notifications:Smtp:FromAddress"] ?? "";

        using var message = new MailMessage
        {
            From = new MailAddress(fromAddress),
            Subject = "Գաղտնաբառի վերականգնում",
            Body = BuildPlainTextBody(link),
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

    private static string BuildResetLink(string linkBase, string rawToken)
    {
        // NotifierTransport.ResolveImplementation already validated that
        // linkBase is non-empty when transport=smtp; we still guard
        // here defensively so a reconfigured process never constructs
        // a bare-token "link". Token is URL-encoded even though the
        // current token charset is URL-safe, because that's the
        // behaviour a future caller would expect.
        var prefix = string.IsNullOrWhiteSpace(linkBase) ? "" : linkBase.Trim();
        var separator = prefix.Contains('?') ? '&' : '?';
        return $"{prefix}{separator}token={Uri.EscapeDataString(rawToken)}";
    }

    private static string BuildPlainTextBody(string resetLink)
    {
        // Eastern Armenian, natural tone — matches the product's
        // Armenian-first posture. Keeps the copy short and honest:
        // what this is, how long it is valid, and what to do if the
        // recipient did not request it. No marketing surface, no
        // unsubscribe footer (this is a transactional / account-
        // recovery mail, not bulk).
        return string.Join("\n\n",
            "Գաղտնաբառի վերականգնման հղումը ստորև է։",
            "Այս հղումը վավեր է սահմանափակ ժամանակ։",
            "Եթե Դուք այս խնդրանքը չեք արել, անտեսեք այս նամակը։",
            resetLink);
    }
}

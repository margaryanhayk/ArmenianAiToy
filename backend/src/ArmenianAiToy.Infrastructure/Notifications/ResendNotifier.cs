using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ArmenianAiToy.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ArmenianAiToy.Infrastructure.Notifications;

/// <summary>
/// Resend (resend.com) HTTP-API <see cref="INotifier"/> implementation.
/// One HTTPS POST per email to <c>https://api.resend.com/emails</c> with a
/// bearer API key — plain BCL <see cref="HttpClient"/>, no new NuGet
/// (same no-new-NuGet discipline <see cref="SmtpNotifier"/> follows with
/// <c>System.Net.Mail</c>). Armenian-first plain-text copy, shared with
/// the SMTP transport via <see cref="NotificationEmailContent"/>.
///
/// <para>
/// <b>Failure containment (hard invariant).</b> Identical posture to
/// <see cref="SmtpNotifier"/>: the forgot-password request endpoint
/// returns <c>202 Accepted</c> with an enumeration-resistant body; a
/// broken Resend API must not convert that contract into a 500. Any
/// non-cancellation exception from the send path is caught, converted to
/// a structured warning log, and swallowed (or surfaced as
/// <c>false</c> to the worker-facing <c>Task&lt;bool&gt;</c> methods).
/// <see cref="OperationCanceledException"/> is deliberately NOT caught.
/// A non-2xx API response is a delivery failure, not an exception —
/// logged with the status code only, never the response body (it can
/// echo the recipient address).
/// </para>
///
/// <para>
/// <b>No-raw-token invariant.</b> The raw reset / verification token is
/// placed into the outgoing email body (that's the point of the email).
/// It is NEVER logged — not even a prefix. Pinned by
/// <c>ResendNotifierTests</c>, mirroring the SMTP and logging pins.
/// </para>
///
/// <para>
/// <b>Internal send seam.</b> The actual wire call is behind a
/// <see cref="SendHttpDelegate"/> seam so unit tests can substitute a
/// capturing / throwing fake without real network access — same pattern
/// as <see cref="SmtpNotifier"/>'s <c>SendMailDelegate</c>.
/// </para>
/// </summary>
public sealed class ResendNotifier : INotifier
{
    /// <summary>
    /// Test-facing seam for the actual HTTP wire call. Default
    /// implementation posts via a process-shared <see cref="HttpClient"/>.
    /// </summary>
    internal delegate Task<HttpResponseMessage> SendHttpDelegate(
        HttpRequestMessage request, CancellationToken cancellationToken);

    private const string ApiEndpoint = "https://api.resend.com/emails";

    // One shared client for the process — sockets are pooled, DNS
    // refresh is a non-issue for a single fixed API host, and the
    // notifier itself stays scoped-registered without churning
    // connections per request scope. 10s ceiling: an email API that
    // hasn't answered in 10 seconds is down, and the HTTP-synchronous
    // callers (forgot-password / verify-request) shouldn't hold their
    // 202 hostage longer than that.
    private static readonly HttpClient SharedClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly ILogger<ResendNotifier> _logger;
    private readonly IConfiguration _config;
    private readonly SendHttpDelegate _sendHttp;

    // Standard DI constructor.
    public ResendNotifier(ILogger<ResendNotifier> logger, IConfiguration config)
        : this(logger, config, sendHttp: null)
    {
    }

    // Test-only constructor — internal + optional so the DI path cannot
    // accidentally pick the wrong overload (same shape as SmtpNotifier).
    internal ResendNotifier(
        ILogger<ResendNotifier> logger,
        IConfiguration config,
        SendHttpDelegate? sendHttp)
    {
        _logger = logger;
        _config = config;
        _sendHttp = sendHttp ?? DefaultSendAsync;
    }

    public async Task SendPasswordResetAsync(
        string email, string resetToken, CancellationToken cancellationToken = default)
    {
        var link = NotificationEmailContent.BuildResetLink(
            _config["Notifications:PasswordResetLinkBase"] ?? "", resetToken);

        // Fire-and-log: the HTTP-synchronous caller's anti-enum 202
        // contract must hold regardless of delivery outcome, so the
        // delivered-bool is observed only by the log line.
        await SendCoreAsync(
            "password_reset", email, deviceName: null,
            NotificationEmailContent.PasswordResetSubject,
            NotificationEmailContent.BuildPasswordResetBody(link),
            cancellationToken);
    }

    public async Task<bool> SendDormancyWarningAsync(
        string email, DateTime? deleteAtUtc, CancellationToken cancellationToken = default)
    {
        // Worker consumer — the bool is load-bearing: false means "do
        // NOT stamp DormancyWarnedAt, retry next tick." See the
        // SmtpNotifier xmldoc for the full contract; semantics here are
        // identical, only the wire transport differs.
        return await SendCoreAsync(
            "dormancy_warning", email, deviceName: null,
            NotificationEmailContent.DormancyWarningSubject,
            NotificationEmailContent.BuildDormancyWarningBody(deleteAtUtc),
            cancellationToken);
    }

    public async Task SendEmailVerificationAsync(
        string email, string verificationToken, CancellationToken cancellationToken = default)
    {
        var link = NotificationEmailContent.BuildVerificationLink(
            _config["Notifications:PasswordResetLinkBase"] ?? "", verificationToken);

        await SendCoreAsync(
            "email_verification", email, deviceName: null,
            NotificationEmailContent.EmailVerificationSubject,
            NotificationEmailContent.BuildEmailVerificationBody(link),
            cancellationToken);
    }

    public async Task<bool> SendDormantDeviceWarningAsync(
        string parentEmail,
        string deviceName,
        DateTime lastSeenAtUtc,
        DateTime? deleteAtUtc,
        CancellationToken cancellationToken = default)
    {
        return await SendCoreAsync(
            "dormant_device_warning", parentEmail, deviceName,
            NotificationEmailContent.DormantDeviceWarningSubject,
            NotificationEmailContent.BuildDormantDeviceWarningBody(
                deviceName, lastSeenAtUtc, deleteAtUtc),
            cancellationToken);
    }

    /// <summary>
    /// Shared send path for all four notification types. Returns
    /// <c>true</c> on a 2xx API response, <c>false</c> on a non-2xx
    /// response or a swallowed exception. Cancellation propagates.
    /// The log line shape matches <see cref="SmtpNotifier"/> exactly
    /// (with <c>transport=resend</c>) so downstream log queries keyed
    /// on <c>delivered</c> work across both transports; the
    /// <c>device_name</c> hole appears only for the dormant-device
    /// type, mirroring the SMTP lines.
    /// </summary>
    private async Task<bool> SendCoreAsync(
        string notificationType,
        string email,
        string? deviceName,
        string subject,
        string textBody,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = BuildRequest(email, subject, textBody);
            using var response = await _sendHttp(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                LogOutcome(notificationType, email, deviceName, delivered: true,
                    exception: null, errorCategory: null);
                return true;
            }

            // Non-2xx is a delivery failure, not an exception. Only the
            // status code lands in the log — the response body can echo
            // the recipient address and stays off the log stream.
            LogOutcome(notificationType, email, deviceName, delivered: false,
                exception: null, errorCategory: $"http_{(int)response.StatusCode}");
            return false;
        }
        catch (OperationCanceledException)
        {
            // Caller cancellation propagates — HTTP request going away
            // or worker shutting down; not a Resend failure.
            throw;
        }
        catch (Exception ex)
        {
            // Swallow-and-log. Exception type name is the only "what
            // failed" signal in the template; the full stack trace
            // travels as the LogWarning exception argument. Token never
            // appears in any hole.
            LogOutcome(notificationType, email, deviceName, delivered: false,
                exception: ex, errorCategory: ex.GetType().Name);
            return false;
        }
    }

    private HttpRequestMessage BuildRequest(string toEmail, string subject, string textBody)
    {
        // Resend's create-email shape: https://resend.com/docs/api-reference/emails/send-email
        // Plain-text only (`text`), matching the SMTP transport's
        // IsBodyHtml=false posture.
        var payload = JsonSerializer.Serialize(new
        {
            from = _config["Notifications:Resend:FromAddress"] ?? "",
            to = new[] { toEmail },
            subject,
            text = textBody
        });

        var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", _config["Notifications:Resend:ApiKey"] ?? "");
        return request;
    }

    private void LogOutcome(
        string notificationType,
        string email,
        string? deviceName,
        bool delivered,
        Exception? exception,
        string? errorCategory)
    {
        // Same template families as SmtpNotifier — keep hole names and
        // ordering identical so structured-log queries span transports.
        if (delivered)
        {
            if (deviceName is null)
            {
                _logger.LogInformation(
                    "Notification send-attempt: type={NotificationType}, email={Email}, transport={Transport}, delivered={Delivered}",
                    notificationType, email, "resend", true);
            }
            else
            {
                _logger.LogInformation(
                    "Notification send-attempt: type={NotificationType}, email={Email}, device_name={DeviceName}, transport={Transport}, delivered={Delivered}",
                    notificationType, email, deviceName, "resend", true);
            }
            return;
        }

        if (deviceName is null)
        {
            _logger.LogWarning(exception,
                "Notification send-attempt: type={NotificationType}, email={Email}, transport={Transport}, delivered={Delivered}, error_category={ErrorCategory}",
                notificationType, email, "resend", false, errorCategory);
        }
        else
        {
            _logger.LogWarning(exception,
                "Notification send-attempt: type={NotificationType}, email={Email}, device_name={DeviceName}, transport={Transport}, delivered={Delivered}, error_category={ErrorCategory}",
                notificationType, email, deviceName, "resend", false, errorCategory);
        }
    }

    // Default wire call — process-shared client, per-request message.
    private static Task<HttpResponseMessage> DefaultSendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => SharedClient.SendAsync(request, cancellationToken);
}

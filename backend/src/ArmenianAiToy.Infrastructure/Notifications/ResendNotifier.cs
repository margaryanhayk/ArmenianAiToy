using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ArmenianAiToy.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ArmenianAiToy.Infrastructure.Notifications;

/// <summary>
/// <see cref="INotifier"/> that sends over the Resend HTTP API
/// (https://api.resend.com/emails) instead of SMTP.
///
/// <para><b>Why:</b> most PaaS hosts (Railway included) block outbound SMTP
/// ports (25/465/587) to fight spam, so the SMTP notifier's send just times
/// out. Resend sends over HTTPS (443), which is not blocked, and gives better
/// deliverability than a personal Gmail relay. Same anti-enumeration posture
/// as the SMTP path: a send failure is swallowed-and-logged (never thrown to
/// the HTTP handler) so the reset-request stays a uniform 202; a bounded
/// HTTP timeout means a slow/blocked network never hangs the request.</para>
///
/// <para>Config: <c>Resend:ApiKey</c>, <c>Resend:FromAddress</c> (e.g.
/// "Areg &lt;noreply@yourdomain&gt;"), and <c>Notifications:PasswordResetLinkBase</c>
/// for the dashboard URL the token links back to. Selected via
/// <c>Notifications:Transport=resend</c>.</para>
/// </summary>
public sealed class ResendNotifier : INotifier
{
    private const string Endpoint = "https://api.resend.com/emails";
    // One shared client; Resend volume for account mail is tiny.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly IConfiguration _config;
    private readonly ILogger<ResendNotifier> _logger;

    public ResendNotifier(IConfiguration config, ILogger<ResendNotifier> logger)
    {
        _config = config;
        _logger = logger;
    }

    public Task SendPasswordResetAsync(string email, string resetToken, CancellationToken cancellationToken = default)
    {
        var link = BuildLink("token", resetToken);
        var body = string.Join("\n\n",
            "Գաղտնաբառի վերականգնման հղումը ստորև է։",
            "Այս հղումը վավեր է սահմանափակ ժամանակ։",
            "Եթե Դուք այս խնդրանքը չեք արել, անտեսեք այս նամակը։",
            link);
        return SendFireAndForgetAsync(email, "Գաղտնաբառի վերականգնում", body, "password_reset", cancellationToken);
    }

    public Task SendEmailVerificationAsync(string email, string verificationToken, CancellationToken cancellationToken = default)
    {
        var link = BuildLink("verifyToken", verificationToken);
        var body = string.Join("\n\n",
            "Ձեր էլ. փոստը հաստատելու հղումը ստորև է։",
            "Այս հղումը վավեր է սահմանափակ ժամանակ։",
            "Եթե Դուք հաշիվ չեք ստեղծել, անտեսեք այս նամակը։",
            link);
        return SendFireAndForgetAsync(email, "Էլ. փոստի հաստատում", body, "email_verification", cancellationToken);
    }

    public async Task<bool> SendDormancyWarningAsync(string email, DateTime? deleteAtUtc, CancellationToken cancellationToken = default)
    {
        var body = deleteAtUtc is null
            ? "Բարև։ Ձեր Areg հաշիվը վերջերս ակտիվ չի եղել։ Մուտք գործեք՝ այն ակտիվ պահելու համար։"
            : $"Բարև։ Ձեր Areg հաշիվը վերջերս ակտիվ չի եղել և կարող է ջնջվել {deleteAtUtc:yyyy-MM-dd}-ից հետո։ Մուտք գործեք՝ այն պահպանելու համար։";
        return await TrySendAsync(email, "Ձեր Areg հաշիվը", body, "dormancy_warning", cancellationToken);
    }

    public async Task<bool> SendDormantDeviceWarningAsync(
        string parentEmail, string deviceName, DateTime lastSeenAtUtc, DateTime? deleteAtUtc,
        CancellationToken cancellationToken = default)
    {
        var lastSeen = lastSeenAtUtc.ToString("yyyy-MM-dd");
        var body = deleteAtUtc is null
            ? $"Բարև։ Ձեր Areg հաշվին կապված սարքը — {deviceName} — վերջերս չի օգտագործվել (վերջին ակտիվությունը {lastSeen})։"
            : $"Բարև։ Ձեր Areg հաշվին կապված սարքը — {deviceName} — վերջերս չի օգտագործվել (վերջին ակտիվությունը {lastSeen}), և կարող է ջնջվել {deleteAtUtc:yyyy-MM-dd}-ից հետո։";
        return await TrySendAsync(parentEmail, "Ձեր Areg սարքը", body, "dormant_device_warning", cancellationToken);
    }

    // Password-reset / verification are called from HTTP handlers that MUST
    // return a uniform 202 regardless of delivery — swallow and log, and
    // never rethrow (except cooperative cancellation, which is the request
    // going away, not a delivery failure).
    private async Task SendFireAndForgetAsync(
        string email, string subject, string body, string type, CancellationToken ct)
    {
        try { await TrySendAsync(email, subject, body, type, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Notification send-attempt failed: type={NotificationType}, email={Email}, transport=resend, error={Error}",
                type, email, ex.GetType().Name);
        }
    }

    private async Task<bool> TrySendAsync(
        string email, string subject, string body, string type, CancellationToken ct)
    {
        var apiKey = _config["Resend:ApiKey"] ?? "";
        var from = _config["Resend:FromAddress"] ?? "";
        var payload = new
        {
            from,
            to = new[] { email },
            subject,
            text = body
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage res;
        try
        {
            res = await Http.SendAsync(req, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Notification send-attempt failed: type={NotificationType}, email={Email}, transport=resend, error={Error}",
                type, email, ex.GetType().Name);
            return false;
        }

        var ok = res.IsSuccessStatusCode;
        _logger.LogInformation(
            "Notification send-attempt: type={NotificationType}, email={Email}, transport=resend, delivered={Delivered}, status={Status}",
            type, email, ok, (int)res.StatusCode);
        res.Dispose();
        return ok;
    }

    private string BuildLink(string param, string rawToken)
    {
        var prefix = (_config["Notifications:PasswordResetLinkBase"] ?? "").Trim();
        var sep = prefix.Contains('?') ? '&' : '?';
        return $"{prefix}{sep}{param}={Uri.EscapeDataString(rawToken)}";
    }
}

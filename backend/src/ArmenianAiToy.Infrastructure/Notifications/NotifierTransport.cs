using Microsoft.Extensions.Configuration;

namespace ArmenianAiToy.Infrastructure.Notifications;

/// <summary>
/// Resolves which <see cref="ArmenianAiToy.Application.Notifications.INotifier"/>
/// implementation to register, based on the <c>Notifications:Transport</c>
/// config key. The selector is bounded to exactly three values today —
/// <see cref="Log"/>, <see cref="Smtp"/>, and <see cref="Resend"/> — so
/// any unknown value fails fast at startup rather than silently falling
/// back to the log-only transport.
///
/// <para>
/// <b>Log is the shipped default.</b> Missing or empty
/// <c>Notifications:Transport</c> resolves to <see cref="Log"/>, which
/// preserves every environment that has not explicitly opted in to
/// real email delivery.
/// </para>
///
/// <para>
/// <b>Transport config is validated here, not inside the notifiers.</b>
/// Missing host / from / api-key / reset-link-base keys are a
/// configuration error the operator should see at process start, not a
/// first-email-send-time surprise. The shipped <c>appsettings.json</c>
/// carries the Notifications block with empty placeholder values —
/// selecting <c>smtp</c> or <c>resend</c> without filling them in
/// throws a clear <see cref="System.InvalidOperationException"/>
/// before the service provider can hand out a broken notifier instance.
/// </para>
/// </summary>
public static class NotifierTransport
{
    public const string Log = "log";
    public const string Smtp = "smtp";
    public const string Resend = "resend";

    /// <summary>
    /// Returns the concrete <see cref="ArmenianAiToy.Application.Notifications.INotifier"/>
    /// implementation type to register for the given config. Throws
    /// <see cref="System.InvalidOperationException"/> on an unknown
    /// transport value or on missing required SMTP config when
    /// transport is <see cref="Smtp"/>.
    /// </summary>
    public static System.Type ResolveImplementation(IConfiguration config)
    {
        var raw = config["Notifications:Transport"];
        var transport = string.IsNullOrWhiteSpace(raw) ? Log : raw.Trim().ToLowerInvariant();

        if (transport == Log)
            return typeof(LoggingNotifier);

        if (transport == Smtp)
        {
            ValidateSmtpConfig(config);
            return typeof(SmtpNotifier);
        }

        if (transport == Resend)
        {
            ValidateResendConfig(config);
            return typeof(ResendNotifier);
        }

        throw new System.InvalidOperationException(
            $"Notifications:Transport value '{raw}' is not recognised. " +
            $"Expected '{Log}', '{Smtp}' or '{Resend}'.");
    }

    private static void ValidateSmtpConfig(IConfiguration config)
    {
        // Only the keys that would break a real send are required.
        // Username/Password/UseSsl/Port are optional — a local dev MTA
        // (Mailhog / Papercut / etc.) typically wants port 1025,
        // no auth, no TLS.
        string[] requiredKeys =
        {
            "Notifications:Smtp:Host",
            "Notifications:Smtp:FromAddress",
            "Notifications:PasswordResetLinkBase"
        };
        var missing = new System.Collections.Generic.List<string>();
        foreach (var key in requiredKeys)
        {
            if (string.IsNullOrWhiteSpace(config[key]))
                missing.Add(key);
        }
        if (missing.Count > 0)
        {
            throw new System.InvalidOperationException(
                "Notifications:Transport=smtp requires non-empty values for: " +
                string.Join(", ", missing) + ".");
        }
    }

    private static void ValidateResendConfig(IConfiguration config)
    {
        // DELIBERATELY NARROWER than the SMTP validator: only the API key
        // is genuinely required, because without it no send can ever
        // succeed. FromAddress falls back to Resend's shared test sender
        // and the link base to the public dashboard URL (see
        // ResendNotifier's Resolve* helpers), so a missing optional key
        // degrades to a working default instead of refusing to boot.
        // Learned on the first real deploy: throwing here takes the WHOLE
        // site down over a non-critical email setting, which is the worse
        // outcome. The key lookup goes through ResendNotifier.ResolveApiKey
        // so the provider-style RESEND_API_KEY name an operator copies out
        // of the Resend dashboard satisfies the check too.
        var apiKey = ResendNotifier.ResolveApiKey(config);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new System.InvalidOperationException(
                "Notifications:Transport=resend requires an API key. Set " +
                "Notifications:Resend:ApiKey (env Notifications__Resend__ApiKey) " +
                "or RESEND_API_KEY to your re_... key.");
        }
    }
}

namespace ArmenianAiToy.Api.Security;

/// <summary>
/// #007/#008 — resolves the app-side HTTPS hardening posture (HTTP→HTTPS
/// redirect + HSTS). Deliberately <b>opt-in and OFF by default</b>, in every
/// environment, because whether the app itself should redirect/emit HSTS
/// depends on the deployment topology the operator has not yet chosen:
///
/// <list type="bullet">
/// <item>TLS terminated at <b>Kestrel</b> → enable here; the app redirects and
/// sends HSTS directly.</item>
/// <item>TLS terminated at a <b>reverse proxy</b> → usually let the proxy own
/// redirect/HSTS; if enabling here, the proxy must forward
/// <c>X-Forwarded-Proto</c> and <c>ForwardedHeaders</c> (#039) must be on, or
/// the redirect will loop.</item>
/// </list>
///
/// So this stays a one-flag deploy switch (<c>Security:RequireHttps</c>) rather
/// than guessing from the environment — turning it on blindly could black-hole
/// every request behind a proxy. Pure function (no config/host access) so it is
/// unit-testable in isolation; <c>Program.cs</c> calls it, registers HSTS
/// options with the resolved max-age, and conditionally adds the middleware.
/// </summary>
public static class HttpsHardeningConfig
{
    /// <summary>Default HSTS max-age when enabled and unset: 365 days.</summary>
    public const int DefaultHstsMaxAgeDays = 365;

    public sealed record Result(bool Enabled, int HstsMaxAgeDays);

    /// <param name="requireHttps">The <c>Security:RequireHttps</c> flag.</param>
    /// <param name="hstsMaxAgeDays">Optional <c>Security:HstsMaxAgeDays</c>
    /// override. Null/unset → <see cref="DefaultHstsMaxAgeDays"/>; clamped to a
    /// floor of 1 so an enabled HSTS is never a no-op zero.</param>
    public static Result Resolve(bool requireHttps, int? hstsMaxAgeDays)
    {
        if (!requireHttps)
            return new Result(false, DefaultHstsMaxAgeDays);

        var days = hstsMaxAgeDays ?? DefaultHstsMaxAgeDays;
        if (days < 1)
            days = 1;

        return new Result(true, days);
    }
}

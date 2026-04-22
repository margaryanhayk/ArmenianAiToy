using System.Security.Cryptography;
using System.Text;

namespace ArmenianAiToy.Api.Observability;

/// <summary>
/// Narrow access guard for the OpenTelemetry Prometheus scrape endpoint
/// (<c>/metrics</c>). The OTel Prometheus exporter middleware does not
/// plug into MVC's <c>[Authorize]</c> pipeline cleanly, so we guard the
/// path with a small purpose-built bearer-token check placed immediately
/// before <c>UseOpenTelemetryPrometheusScrapingEndpoint</c> in
/// <c>Program.cs</c>. Intended strictly to protect the scrape surface
/// from public scraping once deployed — NOT a general auth scheme for
/// other endpoints.
///
/// <para>
/// <b>Shipped defaults are fail-closed.</b> With both
/// <c>Metrics:ScrapeToken</c> empty and
/// <c>Metrics:AllowUnauthenticatedScrape</c> false (the <c>appsettings.json</c>
/// defaults), every request to <c>/metrics</c> gets a 404. Operators
/// opt in to scraping by either (a) setting a token and configuring
/// Prometheus to send <c>Authorization: Bearer &lt;token&gt;</c>, or
/// (b) flipping <c>AllowUnauthenticatedScrape</c> to true in a local
/// dev overlay. The bypass is deliberately tied to an explicit flag,
/// not to the Development environment — a forgotten Development-only
/// shortcut would silently expose metrics in prod.
/// </para>
///
/// <para>
/// <b>Failure status is 404, not 401.</b> Concealment: a public scanner
/// learns nothing from a 404. 401 would force a <c>WWW-Authenticate</c>
/// header pretending to be a standard auth scheme we are not actually
/// running. Prometheus scrapers configured with a valid token see 200
/// either way. Consistent with the repo's existing silent-404 pattern
/// for owned-resource protection elsewhere.
/// </para>
///
/// <para>
/// <b>Constant-time token compare.</b> Uses
/// <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>
/// so the compare itself cannot leak the token via timing — important
/// because the token is secret-material.
/// </para>
///
/// <para>
/// <b>Proxy headers.</b> This guard reads only the <c>Authorization</c>
/// header. It does not consult <c>X-Forwarded-*</c>; the repo does not
/// wire <c>ForwardedHeaders</c> middleware, so those are
/// attacker-controlled. Same invariant the auth rate-limiter documents.
/// </para>
/// </summary>
public static class MetricsScrapeAuth
{
    /// <summary>
    /// The OTel Prometheus exporter's default path; matches exactly,
    /// case-insensitive. Kept as a constant so the guard and the test
    /// suite share one source of truth.
    /// </summary>
    public const string MetricsPath = "/metrics";

    public const string AuthorizationHeader = "Authorization";
    public const string BearerPrefix = "Bearer ";

    public enum Decision { Allow, Deny }

    /// <summary>
    /// Pure decision function, no side effects. Given the request context
    /// and current config values, returns whether the scrape should be
    /// allowed through to the OTel middleware or short-circuited with
    /// 404.
    /// </summary>
    public static Decision Evaluate(
        HttpContext ctx,
        string? configuredToken,
        bool allowUnauthenticated)
    {
        // Explicit bypass wins outright — this is the dev/local switch.
        // The invariant is "protection by default"; the bypass is the
        // opt-out operators must knowingly set.
        if (allowUnauthenticated) return Decision.Allow;

        // Protection enabled (implicit because bypass is off). If no
        // token is configured, we cannot authenticate anyone — so we
        // fail closed. This is the shipped default state and is what
        // makes a fresh deployment protected without extra config.
        if (string.IsNullOrEmpty(configuredToken)) return Decision.Deny;

        var header = ctx.Request.Headers[AuthorizationHeader].ToString();
        if (string.IsNullOrEmpty(header)
            || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return Decision.Deny;

        // Scheme match is case-insensitive (RFC 7235 §2.1 — schemes are
        // case-insensitive). The token itself is still compared
        // constant-time-equal below, so "Bearer", "bearer", and
        // "BEARER" all route to the same compare, but the token bytes
        // after the prefix must still match byte-for-byte.
        var presented = header[BearerPrefix.Length..];
        return ConstantTimeEquals(presented, configuredToken)
            ? Decision.Allow
            : Decision.Deny;
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        // Constant-time-compare the UTF-8 bytes. FixedTimeEquals short-
        // circuits on length mismatch, which is a known and acceptable
        // side-channel — it only leaks token length, not the token.
        var ab = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        if (ab.Length != bb.Length) return false;
        return CryptographicOperations.FixedTimeEquals(ab, bb);
    }
}

using System.Security.Cryptography;
using System.Text;

namespace ArmenianAiToy.Api.Observability;

/// <summary>
/// Access guard for the superuser internal console API
/// (<c>/api/internal/*</c>). Deliberately a clone of the
/// <see cref="MetricsScrapeAuth"/> pattern — a small, purpose-built
/// bearer-token check wired as inline middleware in <c>Program.cs</c>,
/// NOT a general auth scheme and NOT tied to the parent JWT pipeline.
/// The console is an operator surface that reads across ALL parents and
/// devices, so it must be locked down independently of the per-parent
/// JWT auth.
///
/// <para>
/// <b>Shipped defaults are fail-closed.</b> With both
/// <c>Internal:AdminToken</c> empty and
/// <c>Internal:AllowUnauthenticated</c> false (the
/// <c>appsettings.json</c> defaults), every request to
/// <c>/api/internal/*</c> gets a 404. The operator opts in by setting a
/// strong token (and sending <c>Authorization: Bearer &lt;token&gt;</c>
/// from the console), or by flipping the explicit dev bypass. The
/// bypass is tied to its own flag, not to the Development environment,
/// so a forgotten dev shortcut cannot silently expose the whole-system
/// view in prod.
/// </para>
///
/// <para>
/// <b>Failure status is 404, not 401.</b> Concealment: a scanner learns
/// nothing about the console's existence, and we avoid mimicking a
/// standard auth-challenge scheme the app is not running. Consistent
/// with the repo's silent-404 pattern (owned-resource protection,
/// <c>/metrics</c>).
/// </para>
///
/// <para>
/// <b>Constant-time token compare</b> via
/// <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>
/// so the compare cannot leak the token via timing. Reads only the
/// <c>Authorization</c> header — never <c>X-Forwarded-*</c> (the repo
/// does not wire <c>ForwardedHeaders</c>; same invariant as the auth
/// rate-limiter and <see cref="MetricsScrapeAuth"/>).
/// </para>
/// </summary>
public static class InternalAdminAuth
{
    /// <summary>Path prefix this guard covers. Matched case-insensitively
    /// as a prefix so every <c>/api/internal/...</c> route is gated.</summary>
    public const string PathPrefix = "/api/internal";

    public const string AuthorizationHeader = "Authorization";
    public const string BearerPrefix = "Bearer ";

    public enum Decision { Allow, Deny }

    /// <summary>Whether <paramref name="path"/> falls under the guarded
    /// console API prefix.</summary>
    public static bool IsInternalPath(PathString path) =>
        path.StartsWithSegments(PathPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Pure decision function, no side effects. Returns whether the
    /// request may reach the console controller or must be short-circuited
    /// with a 404.
    /// </summary>
    public static Decision Evaluate(
        HttpContext ctx,
        string? configuredToken,
        bool allowUnauthenticated)
    {
        // Explicit bypass wins outright — the dev/local switch. The
        // invariant is "protected by default"; the bypass is the opt-out
        // an operator must knowingly set.
        if (allowUnauthenticated) return Decision.Allow;

        // No token configured → cannot authenticate anyone → fail closed.
        // This is the shipped default and is what makes a fresh deploy
        // protected without extra config.
        if (string.IsNullOrEmpty(configuredToken)) return Decision.Deny;

        var header = ctx.Request.Headers[AuthorizationHeader].ToString();
        if (string.IsNullOrEmpty(header)
            || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return Decision.Deny;

        var presented = header[BearerPrefix.Length..];
        return ConstantTimeEquals(presented, configuredToken)
            ? Decision.Allow
            : Decision.Deny;
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        var ab = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        if (ab.Length != bb.Length) return false;
        return CryptographicOperations.FixedTimeEquals(ab, bb);
    }
}

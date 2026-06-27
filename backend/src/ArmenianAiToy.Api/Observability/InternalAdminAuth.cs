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
    /// <summary>A named operator credential (from <c>Internal:Operators</c>).
    /// <paramref name="TotpSecret"/> (base32) is optional — when present, the
    /// JIT session exchange (<c>POST /api/internal/session</c>) requires a valid
    /// TOTP code for this operator (MFA). Absent ⇒ no second factor.</summary>
    public sealed record OperatorCredential(string Name, string Token, string? TotpSecret = null);

    /// <summary>Extracts the raw bearer token from the Authorization header,
    /// or null when absent / not a Bearer scheme. Used by the session-aware
    /// gate to look a presented token up in the session store.</summary>
    public static string? ExtractBearer(HttpContext ctx)
    {
        var header = ctx.Request.Headers[AuthorizationHeader].ToString();
        if (string.IsNullOrEmpty(header)
            || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return null;
        return header[BearerPrefix.Length..];
    }

    /// <summary>Back-compat single-token decision. Delegates to
    /// <see cref="ResolveOperatorName"/> with no named operators — Allow iff the
    /// presented bearer matches the single token (or the bypass is on).</summary>
    public static Decision Evaluate(
        HttpContext ctx,
        string? configuredToken,
        bool allowUnauthenticated)
        => ResolveOperatorName(ctx, Array.Empty<OperatorCredential>(), configuredToken, allowUnauthenticated)
               is not null
            ? Decision.Allow
            : Decision.Deny;

    /// <summary>
    /// #012 — resolves the calling operator's IDENTITY, or null = deny. The
    /// presented bearer is matched (constant-time) against the named operators
    /// first, then the legacy single token; the resolved NAME is what the access
    /// audit (#013) records, so a leaked token is traceable to one operator and
    /// can be revoked without rotating everyone. The explicit dev/bench bypass
    /// returns the sentinel "dev-bypass". null => 404 (fail-closed default).
    /// </summary>
    public static string? ResolveOperatorName(
        HttpContext ctx,
        IReadOnlyList<OperatorCredential> operators,
        string? legacyToken,
        bool allowUnauthenticated)
    {
        // Explicit bypass wins outright — the dev/local switch.
        if (allowUnauthenticated) return "dev-bypass";

        var header = ctx.Request.Headers[AuthorizationHeader].ToString();
        if (string.IsNullOrEmpty(header)
            || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var presented = header[BearerPrefix.Length..];

        // Named per-operator tokens first — the resolved name is what the access
        // audit records, so an individual operator can be identified and revoked.
        foreach (var op in operators)
        {
            if (!string.IsNullOrEmpty(op.Token) && ConstantTimeEquals(presented, op.Token))
                return op.Name;
        }
        // Legacy single shared token (back-compat / bench). Anonymous-ish: there
        // is no per-operator identity behind it, so the audit records it as such.
        if (!string.IsNullOrEmpty(legacyToken) && ConstantTimeEquals(presented, legacyToken))
            return "admin (shared token)";

        return null;
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        var ab = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        if (ab.Length != bb.Length) return false;
        return CryptographicOperations.FixedTimeEquals(ab, bb);
    }
}

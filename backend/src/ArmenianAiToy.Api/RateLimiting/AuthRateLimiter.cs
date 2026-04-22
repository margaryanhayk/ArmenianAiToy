using System.Threading.RateLimiting;

namespace ArmenianAiToy.Api.RateLimiting;

/// <summary>
/// Per-caller-IP rate-limit policy for the parent auth / account-sensitive
/// endpoints:
/// <list type="bullet">
///   <item><description><c>POST /api/parents/register</c></description></item>
///   <item><description><c>POST /api/parents/login</c></description></item>
///   <item><description><c>POST /api/parents/password</c></description></item>
///   <item><description><c>DELETE /api/parents/account</c></description></item>
/// </list>
/// This exists to close the single biggest abuse surface in the shipped
/// auth path — unlimited login attempts and unlimited account creation
/// from one caller. It is structurally parallel to
/// <see cref="ChatRateLimiter"/>: fixed-window, shared
/// <c>AppMeter.RateLimitRejected</c> counter via the common
/// <c>OnRejected</c> handler in <c>Program.cs</c>, same 429 response
/// shape. Separate policy so the auth bucket and the chat bucket do
/// not share quota.
///
/// <para>
/// <b>Keying.</b> Partition key is <c>Connection.RemoteIpAddress</c>,
/// serialized as a string, falling back to
/// <see cref="AnonymousKey"/> when unavailable. The repo does NOT
/// currently configure <c>ForwardedHeaders</c> middleware, so
/// <c>X-Forwarded-For</c> values are attacker-controlled and MUST NOT
/// be trusted for keying in this slice. Proxy-aware keying is a
/// deploy-slice concern — enabling it requires the operator to wire
/// <c>ForwardedHeaders</c> with an explicit trusted-proxy list before
/// this limiter's key should be changed. Same no-high-cardinality
/// invariant the <c>AppMeter</c> contract documents applies: do not
/// add per-email or per-username partitioning here.
/// </para>
///
/// <para>
/// Defaults are conservative (10 permits / 60 s) — tighter than the
/// chat limiter because the protected endpoints are authentication
/// actions, not a per-utterance pipeline. Overridable via
/// <c>RateLimiting:Auth:PermitLimit</c> and
/// <c>RateLimiting:Auth:WindowSeconds</c>.
/// </para>
/// </summary>
public static class AuthRateLimiter
{
    public const string PolicyName = "auth";
    public const string AnonymousKey = "unknown-ip";

    public const int DefaultPermitLimit = 10;
    public const int DefaultWindowSeconds = 60;

    public static RateLimitPartition<string> PolicyFactory(
        HttpContext context, int permitLimit, int windowSeconds)
    {
        var remote = context.Connection.RemoteIpAddress;
        var key = remote?.ToString();
        if (string.IsNullOrWhiteSpace(key)) key = AnonymousKey;
        return BuildPartition(key, permitLimit, windowSeconds);
    }

    public static RateLimitPartition<string> BuildPartition(
        string key, int permitLimit, int windowSeconds)
    {
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }
}

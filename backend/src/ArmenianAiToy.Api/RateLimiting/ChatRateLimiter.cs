using System.Threading.RateLimiting;

namespace ArmenianAiToy.Api.RateLimiting;

/// <summary>
/// Per-device rate-limit policy for <c>POST /api/chat</c>. Applied ahead of
/// <c>DeviceAuthMiddleware</c> so rejected requests never hit the device
/// validation DB lookup or the OpenAI pipeline — the point of this layer is
/// cost containment, not authentication.
///
/// Partition key is the raw <c>X-Device-Id</c> header. We deliberately do not
/// pre-validate the header against the DB here; DeviceAuthMiddleware handles
/// that and will 401 any spoofed id that slips past the limiter. A client
/// that rotates random device ids to dodge buckets gains nothing — they still
/// fail auth and never cost OpenAI quota.
///
/// Policy is fixed-window, not token-bucket: simpler reasoning, deterministic
/// window boundaries, predictable Retry-After behavior. Defaults to 30
/// permits per 60 s, overridable via <c>RateLimiting:Chat:PermitLimit</c> and
/// <c>RateLimiting:Chat:WindowSeconds</c>.
/// </summary>
public static class ChatRateLimiter
{
    public const string PolicyName = "chat";
    public const string AnonymousKey = "anonymous";

    public const int DefaultPermitLimit = 30;
    public const int DefaultWindowSeconds = 60;

    public static RateLimitPartition<string> PolicyFactory(
        HttpContext context, int permitLimit, int windowSeconds)
    {
        var key = context.Request.Headers["X-Device-Id"].FirstOrDefault();
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

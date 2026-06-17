using System.Threading.RateLimiting;

namespace ArmenianAiToy.Api.RateLimiting;

/// <summary>
/// Per-caller-IP rate-limit policy for the header-less
/// <c>GET /api/story-audio</c> stream. The stream is reachable without
/// device-auth headers (the firmware's audio HTTP client cannot set
/// them), so before this slice it was open and hammerable. This fixed-
/// window limiter caps request volume per source IP.
///
/// <para>
/// Structurally parallel to <see cref="ChatRateLimiter"/> /
/// <see cref="AuthRateLimiter"/>: fixed-window, shares the common
/// <c>OnRejected</c> handler and the <c>aat_rate_limit_rejected_total</c>
/// counter (tagged <c>story_audio</c> via
/// <see cref="RateLimitRejectionPolicy"/>). Separate bucket so story
/// streaming and the chat / auth surfaces never share quota.
/// </para>
///
/// <para>
/// <b>Keying.</b> Partition key is <c>Connection.RemoteIpAddress</c>,
/// same as <see cref="AuthRateLimiter"/> — and the same
/// <c>X-Forwarded-For</c>-is-untrusted caveat applies until a deploy
/// slice wires <c>ForwardedHeaders</c> with a trusted-proxy list.
/// </para>
///
/// <para>
/// Defaults are looser than auth (60 / 60 s) because one storytime
/// session legitimately makes the initial GET plus a resume GET on every
/// barge-in. Overridable via <c>RateLimiting:StoryAudio:PermitLimit</c>
/// and <c>RateLimiting:StoryAudio:WindowSeconds</c>.
/// </para>
/// </summary>
public static class StoryAudioRateLimiter
{
    public const string PolicyName = "story-audio";
    public const string AnonymousKey = "unknown-ip";

    public const int DefaultPermitLimit = 60;
    public const int DefaultWindowSeconds = 60;

    public static RateLimitPartition<string> PolicyFactory(
        HttpContext context, int permitLimit, int windowSeconds)
    {
        var remote = context.Connection.RemoteIpAddress;
        var key = remote?.ToString();
        if (string.IsNullOrWhiteSpace(key)) key = AnonymousKey;
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }
}

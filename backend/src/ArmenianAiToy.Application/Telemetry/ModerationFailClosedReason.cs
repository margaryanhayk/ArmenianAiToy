namespace ArmenianAiToy.Application.Telemetry;

/// <summary>
/// Bounded, closed set of <c>reason</c> tag values for the
/// <see cref="AppMeter.ModerationFailClosed"/> counter. Each constant
/// names one failure mode the moderation adapter's <c>FailClosed</c>
/// path can land in. Any string outside this set is mapped to
/// <see cref="Unknown"/> by <see cref="Normalize"/> — the counter's
/// tag cardinality is therefore strictly bounded at seven, regardless
/// of what the adapter passes in.
///
/// <para>The seven values mirror the catch sites in
/// <c>OpenAIModerationAdapter.CheckContentAsync</c>:</para>
/// <list type="bullet">
///   <item><see cref="RateLimitedRetryFailed"/> — 429 on initial call AND on the single D1 retry.</item>
///   <item><see cref="AuthError"/> — <c>ClientResultException</c> with HTTP 401 or 403.</item>
///   <item><see cref="ServerError"/> — any other <c>ClientResultException</c>.</item>
///   <item><see cref="Timeout"/> — <c>OperationCanceledException</c> (10 s adapter ceiling).</item>
///   <item><see cref="NetworkError"/> — any other <see cref="System.Exception"/> reaching the outer catch.</item>
///   <item><see cref="ParseError"/> — reserved for a future explicit catch on malformed responses.</item>
///   <item><see cref="Unknown"/> — defensive fallback if any caller passes an unrecognized string.</item>
/// </list>
///
/// <para>This counter is for moderation-unavailable / fail-closed
/// causes ONLY. Genuine content flags (Sexual / Violence / SelfHarm /
/// Hate / Harassment) are NOT fail-closed events — they exit
/// <c>CheckContentAsync</c> through the happy path with
/// <c>IsSafe=false</c> but WITHOUT going through <c>FailClosed</c>,
/// so they do not increment this counter.</para>
/// </summary>
public static class ModerationFailClosedReason
{
    public const string RateLimitedRetryFailed = "rate_limited_retry_failed";
    public const string AuthError = "auth_error";
    public const string ServerError = "server_error";
    public const string Timeout = "timeout";
    public const string NetworkError = "network_error";
    public const string ParseError = "parse_error";
    public const string Unknown = "unknown";

    private static readonly HashSet<string> KnownValues = new(StringComparer.Ordinal)
    {
        RateLimitedRetryFailed,
        AuthError,
        ServerError,
        Timeout,
        NetworkError,
        ParseError,
    };

    /// <summary>
    /// Map an arbitrary string to a bounded tag value. Returns the
    /// input unchanged when it is one of the recognized constants;
    /// returns <see cref="Unknown"/> otherwise (including for null /
    /// empty / whitespace inputs).
    /// </summary>
    public static string Normalize(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return Unknown;
        return KnownValues.Contains(reason) ? reason : Unknown;
    }
}

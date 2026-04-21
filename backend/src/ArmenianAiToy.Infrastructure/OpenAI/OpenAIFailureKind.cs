namespace ArmenianAiToy.Infrastructure.OpenAI;

/// <summary>
/// Classified kinds of OpenAI upstream failure as seen by the
/// <see cref="OpenAIReliabilityGate"/>. Bounded enum — the set is stable
/// and small so it can safely be used as a metric tag under the
/// <c>AppMeter</c> no-high-cardinality invariant.
/// <para>
/// Retryable (see <see cref="OpenAIReliabilityGate"/>): <c>RateLimited</c>,
/// <c>Timeout</c>, <c>UpstreamServerError</c>.
/// Non-retryable: <c>AuthFailure</c>, <c>Other</c>.
/// </para>
/// </summary>
public enum OpenAIFailureKind
{
    /// <summary>HTTP 429. Retryable. May honor Retry-After header.</summary>
    RateLimited,

    /// <summary>
    /// Client-side or transport-level timeout, including
    /// <see cref="System.OperationCanceledException"/> /
    /// <see cref="System.TimeoutException"/>. Retryable once.
    /// </summary>
    Timeout,

    /// <summary>HTTP 5xx from OpenAI. Retryable once.</summary>
    UpstreamServerError,

    /// <summary>HTTP 401 / 403. Never retried — retry wastes quota and
    /// masks the misconfiguration.</summary>
    AuthFailure,

    /// <summary>Unclassified exception (bad request, JSON parse error,
    /// network-level I/O error, etc.). Never retried — too ambiguous to
    /// retry safely.</summary>
    Other
}

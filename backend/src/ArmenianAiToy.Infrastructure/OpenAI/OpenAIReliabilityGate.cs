using System.ClientModel;
using System.Runtime.ExceptionServices;
using ArmenianAiToy.Application.Telemetry;
using Microsoft.Extensions.Logging;

namespace ArmenianAiToy.Infrastructure.OpenAI;

/// <summary>
/// Small hand-rolled reliability gate in front of the OpenAI chat path.
/// Performs classification-aware retry (at most one) and holds minimal
/// circuit-breaker state so a sustained upstream outage fails fast
/// rather than burning token / request quota against a guaranteed
/// failure.
/// <para>
/// Scope in this slice: <see cref="OpenAIChatClientAdapter"/> only. The
/// moderation adapter has its own purpose-specific D1 retry policy and
/// fail-closed-to-sentinel contract that's deliberately different
/// (child-safety-critical: never retry 5xx / timeout on moderation). See
/// CLAUDE.md § OpenAI reliability for the full rationale.
/// </para>
/// <para>
/// User-facing response shape is unchanged — on final failure the gate
/// rethrows the classified exception, which <see cref="ChatController"/>
/// catches in its existing Path-5 branch and returns as the same
/// sanitized 502. No new wire contract.
/// </para>
/// </summary>
public sealed class OpenAIReliabilityGate
{
    // ----- Retry policy -----
    private const int MaxAttempts = 2; // 1 initial + 1 retry
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromMilliseconds(500);
    private const double BackoffJitterFraction = 0.25; // ±25%
    private static readonly TimeSpan RateLimitCeiling = TimeSpan.FromSeconds(5);

    // ----- Circuit-breaker defaults -----
    // 5 failures within a 30-second rolling window trip the breaker;
    // while open, calls fail fast for 60 seconds. At end-of-open one
    // half-open probe is allowed — success closes, failure reopens.
    private const int TripThreshold = 5;
    private static readonly TimeSpan TripWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OpenDuration = TimeSpan.FromSeconds(60);

    private readonly TimeProvider _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ILogger<OpenAIReliabilityGate> _logger;

    private readonly object _stateLock = new();
    private readonly LinkedList<DateTimeOffset> _recentFailures = new();
    private DateTimeOffset? _openUntil;
    private bool _probeInFlight;

    /// <summary>
    /// DI-friendly constructor. Uses <see cref="TimeProvider.System"/> and
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/> for the real
    /// clock and real delay. Tests should use the overload that takes
    /// injectable clock + delay.
    /// </summary>
    public OpenAIReliabilityGate(ILogger<OpenAIReliabilityGate> logger)
        : this(TimeProvider.System, (ts, ct) => Task.Delay(ts, ct), logger)
    { }

    /// <summary>
    /// Test-friendly constructor. Injects the clock and the delay
    /// function so the breaker window / open duration / backoff can all
    /// be driven without real sleeps.
    /// </summary>
    public OpenAIReliabilityGate(
        TimeProvider clock,
        Func<TimeSpan, CancellationToken, Task> delay,
        ILogger<OpenAIReliabilityGate> logger)
    {
        _clock = clock;
        _delay = delay;
        _logger = logger;
    }

    /// <summary>
    /// Runs <paramref name="operation"/> under the reliability policy.
    /// On success returns the operation's result. On final failure
    /// (non-retryable kind, retry exhausted, or short-circuit because
    /// the breaker is open) rethrows the classified exception — callers
    /// may catch and handle as they already do.
    /// </summary>
    public async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        // ----- Circuit state check -----
        bool shortCircuit = false;
        bool isProbe = false;
        lock (_stateLock)
        {
            if (_openUntil is not null)
            {
                var now = _clock.GetUtcNow();
                if (now < _openUntil.Value)
                {
                    shortCircuit = true;
                }
                else if (!_probeInFlight)
                {
                    isProbe = true;
                    _probeInFlight = true;
                }
                else
                {
                    shortCircuit = true;
                }
            }
        }

        if (shortCircuit)
        {
            AppMeter.ChatOpenAICircuitShortCircuit.Add(1);
            throw new OpenAIReliabilityCircuitOpenException(
                "OpenAI reliability gate is open; call short-circuited.");
        }

        // ----- Retry loop -----
        int attempt = 0;
        Exception? lastException = null;
        OpenAIFailureKind lastKind = OpenAIFailureKind.Other;

        while (attempt < MaxAttempts)
        {
            attempt++;
            try
            {
                var result = await operation(ct).ConfigureAwait(false);
                // Success — if we were the half-open probe, close the breaker.
                if (isProbe)
                {
                    lock (_stateLock)
                    {
                        _openUntil = null;
                        _probeInFlight = false;
                        _recentFailures.Clear();
                    }
                }
                return result;
            }
            catch (Exception ex)
            {
                var kind = OpenAIFailureClassifier.Classify(ex);
                AppMeter.ChatOpenAIFailure.Add(1,
                    new KeyValuePair<string, object?>("kind", KindTag(kind)));
                lastException = ex;
                lastKind = kind;

                bool canRetry = IsRetryable(kind)
                    && attempt < MaxAttempts
                    && !ct.IsCancellationRequested;
                if (!canRetry)
                    break;

                var backoff = ComputeBackoff(kind, ex);
                AppMeter.ChatOpenAIRetry.Add(1);
                try
                {
                    await _delay(backoff, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Caller's cancellation budget ran out during backoff —
                    // skip the retry, let the original classified failure
                    // bubble.
                    break;
                }
            }
        }

        // ----- Breaker-state update on final failure -----
        lock (_stateLock)
        {
            var now = _clock.GetUtcNow();
            if (isProbe)
            {
                // Probe failed → reopen the breaker for another OpenDuration.
                _openUntil = now.Add(OpenDuration);
                _probeInFlight = false;
                _logger.LogWarning(
                    "OpenAI reliability probe failed; reopening breaker for {OpenSeconds}s. last_kind={LastKind}",
                    (int)OpenDuration.TotalSeconds, lastKind);
            }
            else if (_openUntil is null)
            {
                // Prune failures outside the rolling window, then record.
                while (_recentFailures.First is not null
                       && now - _recentFailures.First.Value > TripWindow)
                {
                    _recentFailures.RemoveFirst();
                }
                _recentFailures.AddLast(now);

                if (_recentFailures.Count >= TripThreshold)
                {
                    _openUntil = now.Add(OpenDuration);
                    AppMeter.ChatOpenAICircuitTrip.Add(1);
                    _logger.LogWarning(
                        "OpenAI reliability gate tripped: {Failures} failures within {WindowSeconds}s; " +
                        "opening for {OpenSeconds}s. last_kind={LastKind}",
                        _recentFailures.Count,
                        (int)TripWindow.TotalSeconds,
                        (int)OpenDuration.TotalSeconds,
                        lastKind);
                }
            }
            // else: breaker already open; another concurrent call tripped it.
            // This call's failure does not re-trip (avoids double-counting).
        }

        ExceptionDispatchInfo.Capture(lastException!).Throw();
        throw lastException!; // unreachable — ExceptionDispatchInfo.Throw() does not return
    }

    // ----- Helpers -----

    private static bool IsRetryable(OpenAIFailureKind kind) =>
        kind is OpenAIFailureKind.RateLimited
            or OpenAIFailureKind.Timeout
            or OpenAIFailureKind.UpstreamServerError;

    private static TimeSpan ComputeBackoff(OpenAIFailureKind kind, Exception ex)
    {
        // For 429, prefer the upstream's Retry-After when reasonable.
        if (kind == OpenAIFailureKind.RateLimited
            && ex is ClientResultException cre
            && TryGetRetryAfter(cre, out var retryAfter))
        {
            return retryAfter > RateLimitCeiling ? RateLimitCeiling : retryAfter;
        }

        // Otherwise: BaseBackoff ± BackoffJitterFraction.
        var jitter = (Random.Shared.NextDouble() * 2.0 - 1.0) * BackoffJitterFraction;
        var ms = BaseBackoff.TotalMilliseconds * (1.0 + jitter);
        return TimeSpan.FromMilliseconds(ms);
    }

    private static bool TryGetRetryAfter(ClientResultException cre, out TimeSpan retryAfter)
    {
        retryAfter = default;
        try
        {
            var response = cre.GetRawResponse();
            if (response is null) return false;
            if (response.Headers.TryGetValue("Retry-After", out var value)
                && !string.IsNullOrWhiteSpace(value)
                && int.TryParse(value, out var seconds)
                && seconds > 0)
            {
                retryAfter = TimeSpan.FromSeconds(seconds);
                return true;
            }
        }
        catch
        {
            // Best-effort parse — any failure falls back to default backoff.
        }
        return false;
    }

    private static string KindTag(OpenAIFailureKind kind) => kind switch
    {
        OpenAIFailureKind.RateLimited => "rate_limited",
        OpenAIFailureKind.Timeout => "timeout",
        OpenAIFailureKind.UpstreamServerError => "upstream_5xx",
        OpenAIFailureKind.AuthFailure => "auth_failure",
        _ => "other"
    };
}

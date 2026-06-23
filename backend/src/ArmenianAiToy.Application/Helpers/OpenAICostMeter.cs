namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// In-process per-device per-UTC-day OpenAI cost meter. Singleton.
/// Backed by a single lock + plain <see cref="Dictionary{Guid, DailyCostBucket}"/>
/// — the locking cost is negligible compared to the request path and
/// the simplicity is worth more than the marginal concurrency the
/// lock-free <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/>
/// would buy.
/// <para>
/// v1 contract:
/// <list type="bullet">
///   <item><description><see cref="IsOverCap"/> answers "is this device
///   already at or above its cap for today?" using the UTC date in
///   <paramref name="utcNow"/>.</description></item>
///   <item><description><see cref="Record"/> adds an estimated cost
///   sample. Day-boundary rolls reset the bucket. Negative costs are
///   clamped to zero (defensive, never negative).</description></item>
///   <item><description><see cref="ShouldLogCapTrip"/> returns
///   <c>true</c> at most once per device per UTC day. The caller uses
///   it for flood-controlled structured warnings.</description></item>
///   <item><description><see cref="GetCurrentTotal"/> is for telemetry
///   / inspection.</description></item>
/// </list>
/// </para>
/// <para>
/// Process restart resets all counters. Documented in
/// <c>docs/openai-daily-cost-cap.md</c>; acceptable for v1 because the
/// worst case is one extra cap-worth of spend per restart, not
/// unbounded spend.
/// </para>
/// </summary>
public sealed class OpenAICostMeter
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, DailyCostBucket> _buckets = new();

    // #022 — fleet-wide daily accumulator (sum of EVERY device's recorded
    // cost on the current UTC day). Backs the optional global spend ceiling,
    // a kill-switch for runaway total spend. Same in-process / per-instance /
    // resets-on-restart caveat as the per-device buckets (the real fix is the
    // shared meter in #020/#021); one instance's runaway loop still self-limits.
    private DailyCostBucket _global = new(default, 0m, false);

    /// <summary>
    /// True iff the accumulated cost for <paramref name="deviceId"/>
    /// on the UTC date of <paramref name="utcNow"/> is at or above
    /// <paramref name="capUsd"/>.
    /// </summary>
    public bool IsOverCap(Guid deviceId, decimal capUsd, DateTime utcNow)
    {
        lock (_lock)
        {
            return GetOrRefreshBucketLocked(deviceId, utcNow).AccumulatedUsd >= capUsd;
        }
    }

    /// <summary>
    /// The accumulated cost for <paramref name="deviceId"/> on the UTC
    /// date of <paramref name="utcNow"/>. Used by metric/log emission.
    /// </summary>
    public decimal GetCurrentTotal(Guid deviceId, DateTime utcNow)
    {
        lock (_lock)
        {
            return GetOrRefreshBucketLocked(deviceId, utcNow).AccumulatedUsd;
        }
    }

    /// <summary>
    /// Add an estimated cost sample for <paramref name="deviceId"/>.
    /// Negative values are clamped to zero. A new UTC date resets the
    /// bucket before adding.
    /// </summary>
    public void Record(Guid deviceId, decimal costUsd, DateTime utcNow)
    {
        if (costUsd <= 0m) return;
        lock (_lock)
        {
            var bucket = GetOrRefreshBucketLocked(deviceId, utcNow);
            _buckets[deviceId] = bucket with
            {
                AccumulatedUsd = bucket.AccumulatedUsd + costUsd
            };
            // #022 — also accrue to the fleet-wide daily total.
            var g = GetOrRefreshGlobalLocked(utcNow);
            _global = g with { AccumulatedUsd = g.AccumulatedUsd + costUsd };
        }
    }

    // ----- #022 global daily ceiling -----

    /// <summary>True iff the FLEET-wide accumulated cost on the UTC date of
    /// <paramref name="utcNow"/> is at or above <paramref name="globalCapUsd"/>.
    /// The caller only invokes this when a positive global cap is configured.</summary>
    public bool IsGlobalOverCap(decimal globalCapUsd, DateTime utcNow)
    {
        lock (_lock)
        {
            return GetOrRefreshGlobalLocked(utcNow).AccumulatedUsd >= globalCapUsd;
        }
    }

    /// <summary>The fleet-wide accumulated cost on the UTC date of
    /// <paramref name="utcNow"/>. For log / metric emission.</summary>
    public decimal GetGlobalTotal(DateTime utcNow)
    {
        lock (_lock)
        {
            return GetOrRefreshGlobalLocked(utcNow).AccumulatedUsd;
        }
    }

    /// <summary>Flood-controlled global log gate — true at most once per UTC
    /// day across the whole process, so a sustained over-ceiling state logs
    /// exactly one warning per day rather than per request.</summary>
    public bool ShouldLogGlobalCapTrip(DateTime utcNow)
    {
        lock (_lock)
        {
            var g = GetOrRefreshGlobalLocked(utcNow);
            if (g.LoggedToday) return false;
            _global = g with { LoggedToday = true };
            return true;
        }
    }

    /// <summary>
    /// Flood-controlled log gate. Returns <c>true</c> at most once per
    /// device per UTC day. The caller uses it to decide whether to
    /// emit a structured warning when the cap trips, so a runaway
    /// client that hits the cap a thousand times in one day produces
    /// exactly one log line, not a thousand.
    /// </summary>
    public bool ShouldLogCapTrip(Guid deviceId, DateTime utcNow)
    {
        lock (_lock)
        {
            var bucket = GetOrRefreshBucketLocked(deviceId, utcNow);
            if (bucket.LoggedToday) return false;
            _buckets[deviceId] = bucket with { LoggedToday = true };
            return true;
        }
    }

    /// <summary>
    /// Resets all in-memory state. Provided for tests; not on a
    /// production hot path. Acquires the same lock as other methods.
    /// </summary>
    internal void ResetAll()
    {
        lock (_lock)
        {
            _buckets.Clear();
            _global = new DailyCostBucket(default, 0m, false);
        }
    }

    private DailyCostBucket GetOrRefreshGlobalLocked(DateTime utcNow)
    {
        // Caller holds _lock. Roll the fleet bucket on a UTC day boundary.
        var todayUtc = utcNow.Date;
        if (_global.DayUtc == todayUtc) return _global;
        _global = new DailyCostBucket(todayUtc, 0m, false);
        return _global;
    }

    private DailyCostBucket GetOrRefreshBucketLocked(Guid deviceId, DateTime utcNow)
    {
        // Caller holds _lock.
        var todayUtc = utcNow.Date;
        if (_buckets.TryGetValue(deviceId, out var existing) && existing.DayUtc == todayUtc)
            return existing;
        var fresh = new DailyCostBucket(todayUtc, 0m, false);
        _buckets[deviceId] = fresh;
        return fresh;
    }

    /// <summary>
    /// Per-device per-day bucket. Internal — exposed only for tests.
    /// </summary>
    internal sealed record DailyCostBucket(
        DateTime DayUtc,
        decimal AccumulatedUsd,
        bool LoggedToday);
}

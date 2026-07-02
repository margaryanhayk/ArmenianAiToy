namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// In-process per-key per-UTC-day counter for the daily AI-question
/// quota (the Curiosity-Window "questions per day" limit). Singleton,
/// mirroring <see cref="OpenAICostMeter"/>'s single-lock + Dictionary
/// design — the lock cost is negligible next to the request path.
///
/// <para>The "key" is the child id when the request carries one, else the
/// device id (resolved by the caller). So siblings sharing one device
/// without per-child identification share one quota; an identified child
/// gets their own.</para>
///
/// <para><b>Counts QUESTIONS, not cost.</b> The caller records exactly one
/// unit per completed Curiosity-Window turn. Stories / games / riddles /
/// calm are never recorded, so story playback stays unlimited — the quota
/// only bounds the live AI-answer surface.</para>
///
/// <para><b>Process restart resets all counters</b> — same accepted v1
/// caveat as <see cref="OpenAICostMeter"/>: the worst case is one extra
/// quota-worth of questions per restart, not unbounded usage.</para>
/// </summary>
public sealed class AiQuotaMeter
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, DailyCountBucket> _buckets = new();

    /// <summary>
    /// True iff <paramref name="key"/> has already asked at least
    /// <paramref name="limit"/> questions on the UTC date of
    /// <paramref name="utcNow"/>. A non-positive limit is treated as
    /// "no quota" (never over).
    /// </summary>
    public bool IsOverQuota(Guid key, int limit, DateTime utcNow)
    {
        if (limit <= 0)
        {
            return false;
        }
        lock (_lock)
        {
            return GetOrRefreshLocked(key, utcNow).Count >= limit;
        }
    }

    /// <summary>The question count for <paramref name="key"/> on the UTC
    /// date of <paramref name="utcNow"/>. For telemetry / a future
    /// parent-facing "questions today" surface.</summary>
    public int GetCount(Guid key, DateTime utcNow)
    {
        lock (_lock)
        {
            return GetOrRefreshLocked(key, utcNow).Count;
        }
    }

    /// <summary>Records one question for <paramref name="key"/>. A new UTC
    /// date resets the bucket before incrementing.</summary>
    public void Record(Guid key, DateTime utcNow)
    {
        lock (_lock)
        {
            var bucket = GetOrRefreshLocked(key, utcNow);
            _buckets[key] = bucket with { Count = bucket.Count + 1 };
        }
    }

    /// <summary>Flood-controlled log gate — true at most once per key per
    /// UTC day, so a child who keeps hitting the quota produces one log
    /// line, not one per blocked request.</summary>
    public bool ShouldLogQuotaTrip(Guid key, DateTime utcNow)
    {
        lock (_lock)
        {
            var bucket = GetOrRefreshLocked(key, utcNow);
            if (bucket.LoggedToday)
            {
                return false;
            }
            _buckets[key] = bucket with { LoggedToday = true };
            return true;
        }
    }

    /// <summary>Resets all in-memory state. For tests; not a hot path.</summary>
    internal void ResetAll()
    {
        lock (_lock)
        {
            _buckets.Clear();
        }
    }

    private DailyCountBucket GetOrRefreshLocked(Guid key, DateTime utcNow)
    {
        // Caller holds _lock. Roll the bucket on a UTC day boundary.
        var todayUtc = utcNow.Date;
        if (_buckets.TryGetValue(key, out var existing) && existing.DayUtc == todayUtc)
        {
            return existing;
        }
        var fresh = new DailyCountBucket(todayUtc, 0, false);
        _buckets[key] = fresh;
        return fresh;
    }

    /// <summary>Per-key per-day bucket. Internal — exposed only for tests.</summary>
    internal sealed record DailyCountBucket(DateTime DayUtc, int Count, bool LoggedToday);
}

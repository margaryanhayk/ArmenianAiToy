namespace ArmenianAiToy.Application.Stories;

/// <summary>Coarse time-of-day bucket used to shape the daily story pick.
/// Evening deliberately draws from the calm, bedtime-safe pool.</summary>
public enum PartOfDay
{
    Morning,
    Daytime,
    Evening,
}

/// <summary>
/// Deterministic "story of the day" picker. Given the child's LOCAL date
/// and the time-of-day bucket, it returns exactly one library story —
/// stable within a given day+bucket (so re-asking returns the same
/// story) and rotating across days.
///
/// The morning/daytime/evening distinction reuses the existing
/// <see cref="CuratedStory.BedtimeSafe"/> flag rather than any new
/// per-story time tagging:
///  - Morning / Daytime → the full library (lively stories are fine).
///  - Evening → only bedtime-safe (calm) stories; falls back to the full
///    library when the library has none flagged.
///
/// Pure and side-effect-free — no GPT, no persistence, no clock read.
/// The caller resolves the local date/time (and the time zone).
/// </summary>
public static class StoryOfTheDaySelector
{
    /// <summary>Maps a local wall-clock time to its part-of-day bucket.
    /// Morning 05:00–10:59, Daytime 11:00–17:59, Evening 18:00–04:59
    /// (evening wraps past midnight so late-night cues stay calm).</summary>
    public static PartOfDay ResolvePartOfDay(TimeOnly localTime)
    {
        var hour = localTime.Hour;
        if (hour >= 18 || hour < 5)
        {
            return PartOfDay.Evening;
        }
        return hour < 11 ? PartOfDay.Morning : PartOfDay.Daytime;
    }

    /// <summary>Picks the story of the day for the given local date and
    /// bucket. Returns null only when <paramref name="stories"/> is empty.
    /// Deterministic: the same (stories, date, part) always yields the
    /// same story.</summary>
    public static CuratedStory? Select(
        IReadOnlyList<CuratedStory> stories, DateOnly localDate, PartOfDay part)
    {
        if (stories is null || stories.Count == 0)
        {
            return null;
        }

        // Evening prefers the calm pool; fall back to the whole library
        // when nothing is flagged bedtime-safe (so evening never 404s).
        IReadOnlyList<CuratedStory> pool = stories;
        if (part == PartOfDay.Evening)
        {
            var calm = stories.Where(s => s.BedtimeSafe).ToList();
            if (calm.Count > 0)
            {
                pool = calm;
            }
        }

        // Stable ordering (by id) so the rotation is independent of the
        // library's enumeration order.
        var ordered = pool.OrderBy(s => s.Id, StringComparer.Ordinal).ToList();

        // Daily rotation seed + a per-bucket offset so morning / daytime /
        // evening don't all land on the same story on a given date.
        var offset = part switch
        {
            PartOfDay.Morning => 0,
            PartOfDay.Daytime => 1,
            PartOfDay.Evening => 2,
            _ => 0,
        };
        var index = (localDate.DayNumber + offset) % ordered.Count;
        return ordered[index];
    }
}

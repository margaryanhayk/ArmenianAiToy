using ArmenianAiToy.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Pure evaluator for B4 bedtime window. No DB access, no time source —
/// caller passes <c>nowUtc</c>. Keeps the chat hot-path deterministic and
/// the helper trivially unit-testable with fixed timestamps.
///
/// Contract:
///   - Either start or end null ⇒ window is disabled ⇒ returns false.
///   - start == end ⇒ zero-length window ⇒ returns false.
///   - Same-day window (start &lt; end): in-window when
///     <c>localTime &gt;= start &amp;&amp; localTime &lt; end</c>.
///   - Midnight-crossing (start &gt; end): in-window when
///     <c>localTime &gt;= start || localTime &lt; end</c>.
///   - Boundary: start is inclusive, end is exclusive. A request arriving
///     exactly at end is out-of-window.
///   - Timezone resolves the device's <see cref="Device.TimeZone"/> via
///     <see cref="TimeZoneInfo.FindSystemTimeZoneById"/>. On failure we
///     log a warning and fall back to UTC — the window is still evaluated,
///     just in UTC, never silently disabled.
/// </summary>
public static class BedtimeWindowEvaluator
{
    public static bool IsDeviceInWindow(Device device, DateTime nowUtc, ILogger? logger = null)
        => IsInWindow(device.BedtimeStart, device.BedtimeEnd, device.TimeZone, nowUtc, logger);

    public static bool IsInWindow(
        TimeOnly? start,
        TimeOnly? end,
        string? timeZoneId,
        DateTime nowUtc,
        ILogger? logger = null)
    {
        if (start is null || end is null)
            return false;

        var s = start.Value;
        var e = end.Value;

        if (s == e)
            return false;

        var tz = ResolveTimeZone(timeZoneId, logger);
        var local = TimeZoneInfo.ConvertTimeFromUtc(
            nowUtc.Kind == DateTimeKind.Utc ? nowUtc : DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
            tz);
        var t = TimeOnly.FromDateTime(local);

        // Midnight-crossing window (e.g. 22:00–07:00): active if now is
        // at-or-after start (evening side) OR before end (morning side).
        if (s > e)
            return t >= s || t < e;

        // Same-day window.
        return t >= s && t < e;
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId, ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return TimeZoneInfo.Utc;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            logger?.LogWarning(
                "Bedtime window: time zone id '{TimeZoneId}' not found on this host; evaluating in UTC.",
                timeZoneId);
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            logger?.LogWarning(
                "Bedtime window: time zone id '{TimeZoneId}' is invalid; evaluating in UTC.",
                timeZoneId);
            return TimeZoneInfo.Utc;
        }
    }
}

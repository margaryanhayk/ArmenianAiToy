namespace ArmenianAiToy.Application.Helpers;

using System;

/// <summary>
/// Canonicalizes a parent-supplied bedtime window to what is actually
/// enforced, so the write path, the persisted row, the API echo, and the
/// dashboard badge all agree.
///
/// <para>Disabled (both null) when: either endpoint is null (half-null →
/// "clear the window"), OR start == end. A zero-length window
/// (<c>start == end</c>) is <see cref="BedtimeWindowEvaluator"/>-inert
/// (it documents <c>start == end ⇒ false</c>), but previously it was
/// persisted verbatim and the dashboard rendered a "Bedtime 12:00–12:00"
/// badge that did nothing — a parent believing quiet hours were on when
/// they were off. Normalizing to disabled at the write boundary removes
/// that mismatch.</para>
/// </summary>
public static class BedtimeWindowNormalizer
{
    public static (TimeOnly? Start, TimeOnly? End) Normalize(TimeOnly? start, TimeOnly? end)
    {
        if (start is null || end is null) return (null, null);
        if (start.Value == end.Value) return (null, null);
        return (start, end);
    }
}

namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Tolerant dotted-numeric firmware version comparison ("v1.2.3" /
/// "1.2.3"). Missing or non-numeric input parses as 0.0.0, so a device that
/// has never reported a version is treated as the oldest possible (and is
/// therefore offered an update). Deliberately simple — no pre-release /
/// build-metadata semantics; firmware versions here are a plain MAJOR.MINOR.PATCH.
/// </summary>
public static class FirmwareVersionComparer
{
    /// <summary>Returns &lt;0 if a&lt;b, 0 if equal, &gt;0 if a&gt;b.</summary>
    public static int Compare(string? a, string? b)
    {
        var pa = Parse(a);
        var pb = Parse(b);
        for (var i = 0; i < 3; i++)
        {
            var c = pa[i].CompareTo(pb[i]);
            if (c != 0)
            {
                return c;
            }
        }
        return 0;
    }

    private static int[] Parse(string? v)
    {
        var result = new[] { 0, 0, 0 };
        if (string.IsNullOrWhiteSpace(v))
        {
            return result;
        }
        var s = v.Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V'))
        {
            s = s[1..];
        }
        var parts = s.Split('.');
        for (var i = 0; i < 3 && i < parts.Length; i++)
        {
            int.TryParse(parts[i], out result[i]);
        }
        return result;
    }
}

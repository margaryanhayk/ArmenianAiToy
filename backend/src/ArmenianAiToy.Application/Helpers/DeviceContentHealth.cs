namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Derives whether a toy has the CURRENT story library on its card, by
/// comparing what the toy reported against what the content manifest
/// currently advertises.
///
/// <para>
/// <b>The gap this closes.</b> The backend knew what it advertised and
/// nothing about what any toy had downloaded, so "is my toy up to date?"
/// had no truthful answer — only listening to a story told you. The toy
/// now reports its verified index on the heartbeat and this turns the two
/// halves into one verdict.
/// </para>
///
/// <para>
/// <b>Derived at READ time, never stored.</b> Same rule as
/// <see cref="DeviceOtaHealth"/> and <see cref="DeviceStoryHealth"/>: the
/// answer depends on config that can change without the device saying
/// anything (ship a new story version and every toy is instantly stale),
/// so a persisted verdict would be wrong the moment the manifest moves.
/// </para>
///
/// <para>
/// <b>Absence of a report is never a fault.</b> A toy on firmware older
/// than the content-report slice sends nothing, and <see cref="Unknown"/>
/// says exactly that. Reporting it as stale would accuse a healthy toy.
/// </para>
///
/// Pure and clock-injected; no DB, no IO, no config reads.
/// </summary>
public static class DeviceContentHealth
{
    /// <summary>Every advertised story is on the card, at the advertised
    /// version.</summary>
    public const string UpToDate = "up_to_date";

    /// <summary>Some advertised stories are present, some are not. The
    /// expected state during a sync — 33 MB over the toy's Wi-Fi is
    /// minutes, not seconds — and also the state of a toy that gave up
    /// part-way. The two are indistinguishable from here, which is why the
    /// word is neutral.</summary>
    public const string Syncing = "syncing";

    /// <summary>The toy reported, and has none of what is advertised. Either
    /// it has never synced or its card was replaced.</summary>
    public const string Stale = "stale";

    /// <summary>Not checking in. Its content is unknowable, and telling a
    /// parent their toy is out of date when the real problem is that it is
    /// switched off would send them chasing the wrong thing.</summary>
    public const string Offline = "offline";

    /// <summary>Checking in, but has never sent a content report — firmware
    /// predating this slice. Absence of evidence, not a fault.</summary>
    public const string Unknown = "unknown";

    /// <summary>Mirrors the presence window used for the online dot.</summary>
    public const int DefaultOnlineThresholdSeconds =
        DeviceOtaHealth.DefaultOnlineThresholdSeconds;

    /// <summary>
    /// How many of the advertised stories the toy holds at the advertised
    /// version, and how many are advertised in total. Both zero when the toy
    /// has never reported — callers must check the verdict before showing
    /// "0 of N", which would read as a fault.
    /// </summary>
    public readonly record struct Counts(int Present, int Advertised);

    /// <summary>
    /// Resolves the verdict. Offline wins over a stale report, for the same
    /// reason it does in <see cref="DeviceStoryHealth"/>: a toy unplugged
    /// three days ago is not an out-of-date toy, it is an off toy.
    /// </summary>
    /// <param name="reportedStories">The device-reported
    /// <c>"id:version,id:version"</c> list, or null when it has never
    /// reported.</param>
    /// <param name="advertised">What the content manifest currently offers,
    /// as (storyId, version) pairs.</param>
    public static string Resolve(
        string? reportedStories,
        IReadOnlyCollection<(string StoryId, int Version)> advertised,
        DateTime lastSeenAtUtc,
        DateTime nowUtc,
        int onlineThresholdSeconds = DefaultOnlineThresholdSeconds)
    {
        var threshold = onlineThresholdSeconds > 0
            ? onlineThresholdSeconds
            : DefaultOnlineThresholdSeconds;
        if ((nowUtc - lastSeenAtUtc).TotalSeconds >= threshold)
        {
            return Offline;
        }
        if (reportedStories is null)
        {
            return Unknown;
        }
        // Nothing advertised (content sync disabled, or an empty manifest):
        // the toy cannot be behind something that was never offered.
        if (advertised is null || advertised.Count == 0)
        {
            return UpToDate;
        }

        var counts = Count(reportedStories, advertised);
        if (counts.Present >= counts.Advertised) return UpToDate;
        if (counts.Present == 0) return Stale;
        return Syncing;
    }

    /// <summary>
    /// Counts how many advertised stories the toy holds AT THE ADVERTISED
    /// VERSION. A story present at an older version counts as absent — that
    /// is the whole point of bumping a version, and counting it as present
    /// would report a toy playing last week's narration as up to date.
    /// </summary>
    public static Counts Count(
        string? reportedStories,
        IReadOnlyCollection<(string StoryId, int Version)> advertised)
    {
        var total = advertised?.Count ?? 0;
        if (string.IsNullOrWhiteSpace(reportedStories) || total == 0)
        {
            return new Counts(0, total);
        }

        var have = Parse(reportedStories);
        var present = 0;
        foreach (var (storyId, version) in advertised!)
        {
            if (have.TryGetValue(storyId, out var reportedVersion)
                && reportedVersion >= version)
            {
                present++;
            }
        }
        return new Counts(present, total);
    }

    /// <summary>
    /// The advertised stories the toy does NOT hold at the advertised
    /// version, in advertised order. For the operator console — knowing
    /// WHICH story is missing is the difference between a diagnosis and a
    /// shrug. Empty when the toy has never reported: naming every story as
    /// missing would be a fabricated fault.
    /// </summary>
    public static IReadOnlyList<string> MissingStoryIds(
        string? reportedStories,
        IReadOnlyCollection<(string StoryId, int Version)> advertised)
    {
        if (reportedStories is null || advertised is null || advertised.Count == 0)
        {
            return Array.Empty<string>();
        }
        var have = Parse(reportedStories);
        var missing = new List<string>();
        foreach (var (storyId, version) in advertised)
        {
            if (!have.TryGetValue(storyId, out var reportedVersion)
                || reportedVersion < version)
            {
                missing.Add(storyId);
            }
        }
        return missing;
    }

    /// <summary>
    /// Parses <c>"ulik:12,anban-huri:9"</c>. Deliberately forgiving: this
    /// string crosses a wire from a device we do not control, and a
    /// malformed entry must cost that one entry, never the whole report.
    /// Ids are compared case-insensitively, matching the content-sync
    /// contract where one backend story can never become two files.
    /// </summary>
    private static Dictionary<string, int> Parse(string reportedStories)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in reportedStories.Split(
                     ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = entry.LastIndexOf(':');
            if (colon <= 0 || colon == entry.Length - 1) continue;
            var id = entry[..colon].Trim();
            if (id.Length == 0) continue;
            if (!int.TryParse(entry[(colon + 1)..].Trim(), out var version)) continue;
            // Duplicate ids keep the HIGHEST version — the same
            // keep-what-works posture the firmware's own dedupe takes.
            if (map.TryGetValue(id, out var existing) && existing >= version) continue;
            map[id] = version;
        }
        return map;
    }
}

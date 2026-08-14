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

    /// <summary>Checking in, but has never sent a content report NOR a sync
    /// diagnostic — firmware predating both slices. Absence of evidence, not
    /// a fault.
    ///
    /// <para>
    /// <b>The conflation this verdict used to carry is now resolved.</b> The
    /// firmware sends no content block when its SD card is missing or dead
    /// (<c>content_report.cpp</c> returns early on <c>!audio_sd_available()</c>),
    /// so a toy on CURRENT firmware with a broken card used to land here
    /// alongside a healthy toy on old firmware — and that toy will never tell
    /// a story. The sync-diagnostics slice closes it: a toy that can report
    /// nothing about its card can still report <see cref="SyncStatusFailed"/>
    /// with a reason, and that now resolves to <see cref="SyncFailed"/>
    /// rather than silence. A toy reaching <c>Unknown</c> today is one that
    /// sent neither, which really is firmware age.
    /// </para>
    /// </summary>
    public const string Unknown = "unknown";

    /// <summary>The toy's last sync attempt did not complete. Ranks above
    /// the count-derived verdicts on purpose: the card looking complete is
    /// precisely the lie this verdict exists to break. A failed sync leaves
    /// the card untouched and the firmware's carry-forward re-advertises the
    /// OLD entry as verified, so counting alone can report a healthy library
    /// on a toy that will never receive another story.</summary>
    public const string SyncFailed = "sync_failed";

    /// <summary>The toy is rebooting on a fault rather than running. Ranks
    /// above <see cref="SyncFailed"/> because it is the deeper fault — a toy
    /// that panics before finishing a sync reports the failure as a symptom,
    /// and naming the crash is what sends an operator to the right
    /// place.</summary>
    public const string CrashLooping = "crash_looping";

    // --- Bounded sync-status vocabulary ----------------------------------
    // The wire value space, closed. The DERIVED verdict reacts only to these
    // four; anything else the toy sends is stored verbatim for an operator
    // to read but never moves the verdict, so a firmware typo degrades to
    // "no opinion" instead of inventing a fault on a healthy toy.

    /// <summary>Last attempt completed; everything it tried, it got.</summary>
    public const string SyncStatusOk = "ok";

    /// <summary>Last attempt completed, but some items failed.</summary>
    public const string SyncStatusPartial = "partial";

    /// <summary>Last attempt did not complete at all.</summary>
    public const string SyncStatusFailed = "failed";

    /// <summary>The toy has never attempted a sync. Not a fault — a toy that
    /// has only just been switched on is in exactly this state.</summary>
    public const string SyncStatusNever = "never";

    /// <summary>
    /// How many boots since the last clean sync before the toy is judged to
    /// be looping rather than merely restarting.
    ///
    /// <para>
    /// <b>Three, and it is a conjunction, not a count on its own.</b> A boot
    /// count alone is not a loop signal: a family that switches the toy off
    /// every night runs up boots without anything being wrong, and a toy that
    /// simply has no Wi-Fi accumulates boots with no clean sync for a
    /// perfectly ordinary reason. A fault reset alone is not one either — one
    /// panic is an incident. Both together are: the 2026-08-14 failure
    /// rebooted every ~184 s, so a real loop crosses this threshold inside
    /// ten minutes, while a toy power-cycled by a child never reports a fault
    /// reason at all.
    /// </para>
    /// </summary>
    public const int CrashLoopBootThreshold = 3;

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
    /// <param name="contentSyncStatus">The toy's own verdict on its last sync
    /// attempt. Only the bounded vocabulary moves the result; null (never
    /// reported) and any unrecognised value are treated as no opinion.</param>
    /// <param name="resetReason">How the toy's last boot ended, e.g.
    /// <c>PANIC</c> or <c>SW</c>.</param>
    /// <param name="bootCount">Boots since the toy's last clean sync.</param>
    public static string Resolve(
        string? reportedStories,
        IReadOnlyCollection<(string StoryId, int Version)> advertised,
        DateTime lastSeenAtUtc,
        DateTime nowUtc,
        int onlineThresholdSeconds = DefaultOnlineThresholdSeconds,
        string? contentSyncStatus = null,
        string? resetReason = null,
        int? bootCount = null)
    {
        var threshold = onlineThresholdSeconds > 0
            ? onlineThresholdSeconds
            : DefaultOnlineThresholdSeconds;
        if ((nowUtc - lastSeenAtUtc).TotalSeconds >= threshold)
        {
            return Offline;
        }
        // Both fault verdicts sit ABOVE the "never reported" branch on
        // purpose. A toy that dies mid-sync, or whose card cannot be read,
        // sends no story list at all — reading that silence as Unknown is
        // exactly the blindness that let a crash loop run all night looking
        // merely online.
        if (IsCrashLooping(resetReason, bootCount))
        {
            return CrashLooping;
        }
        if (IsFailedSyncStatus(contentSyncStatus))
        {
            return SyncFailed;
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
    /// Whether a device-reported status is one this backend understands.
    /// The vocabulary is closed so an unrecognised value — a firmware typo, a
    /// future status this build predates — degrades to "no opinion" rather
    /// than being rendered to a parent as a fault on a healthy toy.
    /// </summary>
    public static bool IsKnownSyncStatus(string? status)
        => status is not null
           && (Matches(status, SyncStatusOk)
               || Matches(status, SyncStatusPartial)
               || Matches(status, SyncStatusFailed)
               || Matches(status, SyncStatusNever));

    /// <summary>
    /// Whether the toy's last sync attempt is one to surface as a fault.
    /// <c>partial</c> counts: it means the toy tried and did not get
    /// everything, which is the state the carry-forward would otherwise
    /// disguise as a complete library.
    /// </summary>
    private static bool IsFailedSyncStatus(string? status)
        => Matches(status, SyncStatusFailed) || Matches(status, SyncStatusPartial);

    /// <summary>
    /// Whether the toy is rebooting on a fault rather than running. See
    /// <see cref="CrashLoopBootThreshold"/> for why this is a conjunction —
    /// neither half alone distinguishes a loop from ordinary use.
    /// </summary>
    public static bool IsCrashLooping(string? resetReason, int? bootCount)
        => bootCount is int boots
           && boots >= CrashLoopBootThreshold
           && IsFaultResetReason(resetReason);

    /// <summary>
    /// A reset the toy did not choose. <c>PANIC</c> is the crash; anything
    /// naming a watchdog (<c>WDT</c>, <c>TASK_WDT</c>, <c>INT_WDT</c>,
    /// <c>RTC_WDT</c>) is a hang the hardware broke out of. Deliberately
    /// excludes <c>SW</c>, <c>POWERON</c> and <c>DEEPSLEEP</c>: an OTA
    /// reboots by software and a child switches the toy off at the wall, and
    /// neither is a fault.
    /// </summary>
    private static bool IsFaultResetReason(string? resetReason)
    {
        if (string.IsNullOrWhiteSpace(resetReason)) return false;
        var value = resetReason.Trim();
        return value.Contains("PANIC", StringComparison.OrdinalIgnoreCase)
               || value.Contains("WDT", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Matches(string? value, string expected)
        => value is not null
           && string.Equals(value.Trim(), expected, StringComparison.OrdinalIgnoreCase);

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

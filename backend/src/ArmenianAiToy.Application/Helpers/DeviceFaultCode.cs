namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Bounded catalogue of toy fault codes shown to a parent.
///
/// <para>
/// <b>Why a code and not an explanation (owner decision 2026-08-03).</b> The
/// parent surface deliberately does NOT describe what is wrong or how to fix
/// it. A parent gets a short code, reports it to support, and support (the
/// owner) decides what it means and what to do. Rationale: a technical
/// explanation invites a parent to open the toy, it promises a self-service
/// fix we cannot guarantee, and it exposes internal hardware detail that will
/// change between hardware revisions. A code is stable, unambiguous over the
/// phone, and keeps support in the loop.
/// </para>
///
/// <para>
/// <b>Codes are permanent.</b> Once a code has been shown to a parent it is
/// in the wild — it may be read back to support months later. Never renumber
/// or repurpose one; retire it and allocate a new number instead. The number
/// space is grouped so support can triage from the first digit:
/// 1xx = storage, 2xx = audio in/out, 3xx = connectivity, 4xx = content/
/// library sync, 5xx = firmware/reboot health.
/// </para>
/// </summary>
public static class DeviceFaultCode
{
    /// <summary>No fault to report.</summary>
    public const string None = "";

    /// <summary>Storage: the toy's memory card is not readable, so no stories
    /// can play. (Bench cause seen 2026-08-03: the card reader's 5 V supply
    /// came loose.)</summary>
    public const string StorageUnavailable = "E-101";

    /// <summary>Content/library sync: the toy's last content-sync attempt did
    /// not complete (<see cref="DeviceContentHealth.SyncFailed"/>) — the
    /// library is stale even though counting the card's contents alone would
    /// look complete. Allocated 2026-09-11.</summary>
    public const string LibrarySyncFailed = "E-401";

    /// <summary>Firmware/reboot health: the toy is rebooting on a fault
    /// rather than running (<see cref="DeviceContentHealth.CrashLooping"/>).
    /// Allocated 2026-09-11.</summary>
    public const string CrashLooping = "E-501";

    /// <summary>
    /// Maps a <see cref="DeviceStoryHealth"/> verdict to the code a parent
    /// sees. Only genuine, actionable faults get a code: "offline" is already
    /// conveyed by the presence indicator, and "unknown" (older firmware that
    /// does not self-report) is the absence of information, not a fault —
    /// giving either one a code would train parents to ignore codes.
    /// </summary>
    public static string FromStoryHealth(string storyHealth) => storyHealth switch
    {
        DeviceStoryHealth.NoStorage => StorageUnavailable,
        _ => None,
    };

    /// <summary>
    /// Maps a <see cref="DeviceContentHealth"/> verdict to the code a parent
    /// sees. Only the two genuine, actionable content-sync faults get a
    /// code — "syncing"/"stale"/"offline"/"unknown" are, respectively, an
    /// expected in-progress state, count-derived (not diagnostic), presence
    /// (not library) information, and absence of a report, none of which are
    /// a fault to report a code for.
    /// </summary>
    public static string FromContentHealth(string contentHealth) => contentHealth switch
    {
        DeviceContentHealth.CrashLooping => CrashLooping,
        DeviceContentHealth.SyncFailed => LibrarySyncFailed,
        _ => None,
    };

    /// <summary>
    /// Combines a device's story-health and content-health verdicts into
    /// the single code shown to a parent. A toy can have a storage fault
    /// AND a stalled sync at once; only one code is ever shown, ranked by
    /// how deep the underlying fault is: crash-looping (the toy cannot even
    /// run cleanly) outranks storage unavailable (a fixed, physical fault),
    /// which outranks a failed sync (often transient — a Wi-Fi hiccup,
    /// retried on the next heartbeat) — the same ordering
    /// <see cref="DeviceContentHealth.Resolve"/> already uses internally
    /// for crash-looping over sync-failed.
    /// </summary>
    public static string FromHealth(string storyHealth, string contentHealth)
    {
        if (contentHealth == DeviceContentHealth.CrashLooping)
            return CrashLooping;

        var storyCode = FromStoryHealth(storyHealth);
        if (storyCode != None)
            return storyCode;

        return FromContentHealth(contentHealth);
    }
}

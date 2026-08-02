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
/// 1xx = storage, 2xx = audio in/out, 3xx = connectivity.
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
}

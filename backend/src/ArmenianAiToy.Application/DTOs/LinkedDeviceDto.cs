using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Domain.Enums;

namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Enriched device row for the parent linked-devices detail endpoint.
/// <para>
/// <c>IsDormant</c> is a derived, reporting-only observation: true when
/// <c>UtcNow - LastSeenAt &gt;= Dormancy:Devices:NotSeenDays</c> (default
/// 180, clamp floor 1). No behavior change is tied to this flag — chat
/// gates, retention, auth, and moderation are untouched. See
/// <see cref="ArmenianAiToy.Application.Services.ParentService.GetLinkedDeviceDetailsAsync"/>
/// for the single computation site.
/// </para>
/// <para>
/// <c>IsOnline</c> (platform/presence) is derived the same way: true when
/// <c>UtcNow - LastSeenAt &lt; Presence:OnlineThresholdSeconds</c> (default
/// 180s, clamp floor 30s). The toy refreshes <c>LastSeenAt</c> via the
/// throttled heartbeat (<c>POST /api/devices/heartbeat</c>) and normal chat
/// traffic; the app shows a live online/offline dot from this flag. Also
/// reporting-only — no behavior is gated on it. Same single computation site.
/// </para>
/// </summary>
public record LinkedDeviceDto(
    Guid DeviceId,
    string DeviceName,
    DateTime LastSeenAt,
    DateTime LinkedAt,
    DateTime? LastConversationAt,
    List<LinkedDeviceChildDto> Children,
    bool IsPaused,
    bool IsRevoked,
    TimeOnly? BedtimeStart,
    TimeOnly? BedtimeEnd,
    bool StoryEnabled,
    bool GameEnabled,
    bool RiddleEnabled,
    bool CuriosityEnabled,
    bool IsDormant,
    bool IsOnline,

    /// <summary>
    /// Whether the toy can actually play stories, derived at read time by
    /// <c>DeviceStoryHealth.Resolve</c>: "ok" / "no_storage" / "offline" /
    /// "unknown". Diagnostic detail — for the operator console and support,
    /// NOT shown to the parent.
    /// </summary>
    string StoryHealth,

    /// <summary>
    /// The short fault code the PARENT sees (e.g. "E-101"), empty when there
    /// is nothing to report. Owner decision 2026-08-03: parents get a code to
    /// quote to support, never a technical explanation or a self-service fix.
    /// See <c>DeviceFaultCode</c>.
    /// </summary>
    string FaultCode)
{
    /// <summary>B3 — spoken story intro toggle (default ON). Init-prop so
    /// pre-existing constructor call sites/tests compile unchanged.</summary>
    public bool StoryIntroEnabled { get; init; } = true;

    /// <summary>In-story pauses toggle (default ON). Same init-prop
    /// discipline as <see cref="StoryIntroEnabled"/>.</summary>
    public bool StoryPausesEnabled { get; init; } = true;

    /// <summary>Variant-endings toggle (default ON).</summary>
    public bool VariantEndingsEnabled { get; init; } = true;

    /// <summary>Slice E — bedtime-music opt-in (default OFF).</summary>
    public bool BedtimeMusicEnabled { get; init; }

    /// <summary>
    /// Whether the toy has the current story library on its card, derived at
    /// read time by <c>DeviceContentHealth.Resolve</c>: "up_to_date" /
    /// "syncing" / "stale" / "offline" / "unknown".
    /// <para>
    /// "unknown" means the toy is running firmware older than the
    /// content-report slice and has never told us — NOT that anything is
    /// wrong. The dashboard must say so in those words; "0 stories" would
    /// accuse a healthy toy.
    /// </para>
    /// </summary>
    public string ContentHealth { get; init; } = DeviceContentHealth.Unknown;

    /// <summary>How many of the advertised stories the toy holds at the
    /// advertised version. Meaningless unless <see cref="ContentHealth"/> is
    /// one of up_to_date / syncing / stale — zero also means "never
    /// reported".</summary>
    public int StoriesOnToy { get; init; }

    /// <summary>How many stories the library currently offers. Zero when
    /// content sync is disabled.</summary>
    public int StoriesAvailable { get; init; }
}

public record LinkedDeviceChildDto(
    Guid ChildId,
    string Name,
    int? Age,
    Gender Gender,
    bool? StoryEnabled,
    bool? GameEnabled,
    bool? RiddleEnabled,
    bool? CuriosityEnabled);

/// <summary>
/// Small self-scoped dormancy-reporting summary exposed alongside the
/// linked-device list on <c>GET /api/parents/devices/details</c>.
/// Observational only — NOT an action signal, NOT a policy threshold.
/// <para>
/// <c>LastLoginAt</c> is the raw <c>Parent.LastLoginAt</c> value
/// (nullable). No "parent is dormant" boolean is derived — there is no
/// authoritative threshold for parent-level dormancy in this repo yet,
/// and surfacing a misleading boolean would create a signal the
/// dashboard can't honestly interpret.
/// </para>
/// <para>
/// <c>TotalDevices</c> / <c>DormantDevices</c> are counted directly from
/// the already-derived <see cref="LinkedDeviceDto.IsDormant"/> values on
/// the same response — there is exactly one dormancy-derivation site
/// (<see cref="ArmenianAiToy.Application.Services.ParentService.GetLinkedDeviceDetailsAsync"/>).
/// </para>
/// </summary>
public record DormancySummaryDto(
    int TotalDevices,
    int DormantDevices,
    DateTime? LastLoginAt,
    DateTime? EmailVerifiedAt);

/// <summary>
/// Response envelope for <c>GET /api/parents/devices/details</c>.
/// Additive extension: pre-slice clients reading only <c>Devices</c>
/// continue to work unchanged; the dashboard reads both.
/// </summary>
public record LinkedDevicesResponse(
    List<LinkedDeviceDto> Devices,
    DormancySummaryDto Summary);

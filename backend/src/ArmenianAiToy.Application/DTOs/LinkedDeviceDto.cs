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
/// </summary>
public record LinkedDeviceDto(
    Guid DeviceId,
    string DeviceName,
    DateTime LastSeenAt,
    DateTime LinkedAt,
    DateTime? LastConversationAt,
    List<LinkedDeviceChildDto> Children,
    bool IsPaused,
    TimeOnly? BedtimeStart,
    TimeOnly? BedtimeEnd,
    bool StoryEnabled,
    bool GameEnabled,
    bool RiddleEnabled,
    bool CuriosityEnabled,
    bool IsDormant);

public record LinkedDeviceChildDto(
    Guid ChildId,
    string Name,
    int? Age,
    Gender Gender,
    bool? StoryEnabled,
    bool? GameEnabled,
    bool? RiddleEnabled,
    bool? CuriosityEnabled);

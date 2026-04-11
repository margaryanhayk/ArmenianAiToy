using ArmenianAiToy.Domain.Enums;

namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Enriched device row for the parent linked-devices detail endpoint.
/// </summary>
public record LinkedDeviceDto(
    Guid DeviceId,
    string DeviceName,
    DateTime LastSeenAt,
    DateTime LinkedAt,
    DateTime? LastConversationAt,
    List<LinkedDeviceChildDto> Children);

public record LinkedDeviceChildDto(
    Guid ChildId,
    string Name,
    int? Age,
    Gender Gender);

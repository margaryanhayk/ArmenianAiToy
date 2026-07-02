using System.Text.Json;

namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// One command as delivered to a device on <c>GET /api/devices/commands</c>.
/// <see cref="Payload"/> is the parsed JSON object (or null) so the firmware
/// reads a real object, not an escaped string. Only fields the device needs
/// to act + ack are exposed (no internal status/timestamps).
/// </summary>
public sealed record DeviceCommandDto(
    Guid Id,
    string Type,
    JsonElement? Payload,
    DateTime CreatedAt,
    DateTime? ExpiresAt);

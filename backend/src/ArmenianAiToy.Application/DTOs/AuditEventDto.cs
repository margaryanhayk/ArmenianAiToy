using System.Text.Json.Nodes;

namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Row shape for the parent-facing audit read endpoint
/// (<c>GET /api/parents/audit</c>). The authenticated parent is always
/// the actor, so <c>ActorParentId</c> is deliberately not surfaced —
/// every row a parent reads belongs to them by the query filter.
/// <para>
/// <c>EventType</c> is the enum name as a string (e.g.
/// <c>"ParentDeviceUnlinked"</c>) — no localization layer in slice 1.
/// <c>Metadata</c> is the audit row's JSON blob parsed into a
/// <see cref="JsonNode"/> so it serializes on the wire as an actual
/// JSON object (or <c>null</c>), not as an escaped JSON string.
/// </para>
/// </summary>
public record AuditEventDto(
    Guid Id,
    DateTime Timestamp,
    string EventType,
    Guid? TargetDeviceId,
    Guid? TargetChildId,
    JsonNode? Metadata);

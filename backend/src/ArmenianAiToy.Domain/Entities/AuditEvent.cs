using System.Text.Json;
using ArmenianAiToy.Domain.Enums;

namespace ArmenianAiToy.Domain.Entities;

/// <summary>
/// Append-only audit record for sensitive parent actions. See CLAUDE.md
/// § Audit events for the full contract. Two invariants worth calling out:
///
///  1. No foreign keys to Parent / Device / Child. Audit rows must outlive
///     the entities they describe — a Parent deletion cascade that also
///     removed the parent's audit trail would destroy the record of the
///     deletion at the same moment it is meant to document.
///
///  2. <see cref="Metadata"/> must contain no PII — no names, no emails,
///     no message content. Only counts, booleans, and identifiers already
///     carried in the dedicated <c>*Id</c> columns. Keeps audit durable
///     without becoming a second copy of the data the parent just asked
///     to have erased.
/// </summary>
public class AuditEvent
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    public AuditEventType EventType { get; set; }

    /// <summary>
    /// Actor (parent) identifier as it stood at the time of the event.
    /// Deliberately not an FK — the Parent row may be gone afterwards.
    /// </summary>
    public Guid? ActorParentId { get; set; }

    /// <summary>Target device identifier, populated for unlink events.</summary>
    public Guid? TargetDeviceId { get; set; }

    /// <summary>Target child identifier, populated for child-delete events.</summary>
    public Guid? TargetChildId { get; set; }

    /// <summary>
    /// Optional small JSON text blob of event-specific counts/booleans.
    /// Never contains PII.
    /// </summary>
    public string? Metadata { get; set; }

    public static AuditEvent ParentAccountDeleted(
        Guid parentId, int linkedDevices, int orphanedDevicesDeleted) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentAccountDeleted,
        ActorParentId = parentId,
        Metadata = JsonSerializer.Serialize(new
        {
            linked_devices = linkedDevices,
            orphaned_devices_deleted = orphanedDevicesDeleted
        })
    };

    public static AuditEvent ParentChildDeleted(
        Guid parentId, Guid childId, int conversationsRemoved) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentChildDeleted,
        ActorParentId = parentId,
        TargetChildId = childId,
        Metadata = JsonSerializer.Serialize(new
        {
            conversations_removed = conversationsRemoved
        })
    };

    public static AuditEvent ParentDeviceUnlinked(
        Guid parentId, Guid deviceId, bool orphanCascaded) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentDeviceUnlinked,
        ActorParentId = parentId,
        TargetDeviceId = deviceId,
        Metadata = JsonSerializer.Serialize(new
        {
            orphan_cascaded = orphanCascaded
        })
    };

    public static AuditEvent ParentPasswordChanged(Guid parentId) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentPasswordChanged,
        ActorParentId = parentId
    };

    public static AuditEvent ParentDevicePauseStateChanged(
        Guid parentId, Guid deviceId, bool isPaused) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentDevicePauseStateChanged,
        ActorParentId = parentId,
        TargetDeviceId = deviceId,
        Metadata = JsonSerializer.Serialize(new
        {
            is_paused = isPaused
        })
    };

    public static AuditEvent ParentBedtimeWindowSet(
        Guid parentId, Guid deviceId, TimeOnly? start, TimeOnly? end) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentBedtimeWindowSet,
        ActorParentId = parentId,
        TargetDeviceId = deviceId,
        Metadata = JsonSerializer.Serialize(new
        {
            start = start,
            end = end
        })
    };

    public static AuditEvent ParentDeviceModeFlagsSet(
        Guid parentId, Guid deviceId,
        bool story, bool game, bool riddle, bool curiosity) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentDeviceModeFlagsSet,
        ActorParentId = parentId,
        TargetDeviceId = deviceId,
        Metadata = JsonSerializer.Serialize(new
        {
            story = story,
            game = game,
            riddle = riddle,
            curiosity = curiosity
        })
    };

    public static AuditEvent ChildModeOverridesSet(
        Guid parentId, Guid childId,
        bool? story, bool? game, bool? riddle, bool? curiosity) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ChildModeOverridesSet,
        ActorParentId = parentId,
        TargetChildId = childId,
        Metadata = JsonSerializer.Serialize(new
        {
            story = story,
            game = game,
            riddle = riddle,
            curiosity = curiosity
        })
    };

    /// <summary>
    /// Emitted on every successful <c>GET /api/parents/export</c>. Metadata
    /// carries only counts — no PII, no content, no identifiers beyond
    /// <see cref="ActorParentId"/>. Target*Id are null because the event
    /// describes the parent's whole-account export, not a single target.
    /// </summary>
    public static AuditEvent ParentDataExported(
        Guid parentId,
        int devices,
        int children,
        int conversations,
        int messages,
        int auditEvents) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentDataExported,
        ActorParentId = parentId,
        Metadata = JsonSerializer.Serialize(new
        {
            devices = devices,
            children = children,
            conversations = conversations,
            messages = messages,
            audit_events = auditEvents
        })
    };

    /// <summary>
    /// Emitted by the retention purge <c>BackgroundService</c> on every
    /// tick that actually deleted at least one conversation. Noop ticks
    /// write no row — the durable "purge happened and here is the scope"
    /// signal is what this event exists for, not a heartbeat.
    /// <para>
    /// <b>First system-actor audit event.</b>
    /// <see cref="ActorParentId"/> is deliberately <c>null</c> — no human
    /// parent initiated this action. That null is also what keeps this
    /// event out of every parent-facing read surface:
    /// <c>GET /api/parents/audit</c> and the parent-scoped audit slice of
    /// <c>GET /api/parents/export</c> both filter
    /// <c>ActorParentId == parentId</c>, so a null-actor row is invisible
    /// to every parent by construction. Do not change the factory to
    /// populate <see cref="ActorParentId"/> — the invisibility of this
    /// event to parents is a contract, not an accident.
    /// </para>
    /// <para>
    /// <see cref="TargetDeviceId"/> and <see cref="TargetChildId"/> are
    /// null because a purge tick spans the whole DB, not a single
    /// target. Metadata is counts-only plus the effective cutoff — no
    /// PII, consistent with the rest of the audit contract.
    /// </para>
    /// </summary>
    public static AuditEvent ConversationsPurgedByRetention(
        int conversationsDeleted,
        int messagesDeleted,
        DateTime cutoffUtc,
        int batchSizeLimit) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ConversationsPurgedByRetention,
        ActorParentId = null,
        TargetDeviceId = null,
        TargetChildId = null,
        Metadata = JsonSerializer.Serialize(new
        {
            conversations_deleted = conversationsDeleted,
            messages_deleted = messagesDeleted,
            cutoff_utc = cutoffUtc,
            batch_size_limit = batchSizeLimit
        })
    };
}

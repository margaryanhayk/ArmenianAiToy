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
        Guid parentId,
        int linkedDevices,
        int orphanedDevicesDeleted,
        // C2.2a — audio cleanup roll-up across the orphaned-device
        // cascade. Defaults are 0 so call sites that predate the
        // cascade hook (none today, but keeps the signature
        // additive-only) compile unchanged.
        int audioConversationsAttempted = 0,
        int audioFilesDeleted = 0,
        int audioDeleteFailures = 0) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentAccountDeleted,
        ActorParentId = parentId,
        Metadata = JsonSerializer.Serialize(new
        {
            linked_devices = linkedDevices,
            orphaned_devices_deleted = orphanedDevicesDeleted,
            audio_conversations_attempted = audioConversationsAttempted,
            audio_files_deleted = audioFilesDeleted,
            audio_delete_failures = audioDeleteFailures
        })
    };

    public static AuditEvent ParentChildDeleted(
        Guid parentId,
        Guid childId,
        int conversationsRemoved,
        // C2.2a — audio cleanup roll-up across every conversation the
        // child owned. Defaults preserve compatibility with any
        // future call site that wants to skip the cleanup signal.
        int audioConversationsAttempted = 0,
        int audioFilesDeleted = 0,
        int audioDeleteFailures = 0) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentChildDeleted,
        ActorParentId = parentId,
        TargetChildId = childId,
        Metadata = JsonSerializer.Serialize(new
        {
            conversations_removed = conversationsRemoved,
            audio_conversations_attempted = audioConversationsAttempted,
            audio_files_deleted = audioFilesDeleted,
            audio_delete_failures = audioDeleteFailures
        })
    };

    public static AuditEvent ParentDeviceUnlinked(
        Guid parentId,
        Guid deviceId,
        bool orphanCascaded,
        // C2.2a — audio cleanup roll-up across the orphan-cascaded
        // device's conversations. Always 0 on the still-linked
        // branch (no cascade fired). Defaults keep the signature
        // additive-only.
        int audioConversationsAttempted = 0,
        int audioFilesDeleted = 0,
        int audioDeleteFailures = 0) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentDeviceUnlinked,
        ActorParentId = parentId,
        TargetDeviceId = deviceId,
        Metadata = JsonSerializer.Serialize(new
        {
            orphan_cascaded = orphanCascaded,
            audio_conversations_attempted = audioConversationsAttempted,
            audio_files_deleted = audioFilesDeleted,
            audio_delete_failures = audioDeleteFailures
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

    /// <summary>#074 — parent revoked/restored a device's server-side
    /// credential kill-switch. Counts-only metadata (the post-change flag).</summary>
    public static AuditEvent ParentDeviceRevocationChanged(
        Guid parentId, Guid deviceId, bool isRevoked) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentDeviceRevocationChanged,
        ActorParentId = parentId,
        TargetDeviceId = deviceId,
        Metadata = JsonSerializer.Serialize(new
        {
            is_revoked = isRevoked
        })
    };

    /// <summary>Phase A.2 — parent claimed a device to their account via its
    /// single-use claim code. No metadata (the claim code must never be
    /// recorded); the parent + device live in the dedicated columns.</summary>
    public static AuditEvent ParentDeviceClaimed(Guid parentId, Guid deviceId) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentDeviceClaimed,
        ActorParentId = parentId,
        TargetDeviceId = deviceId
    };

    /// <summary>A parent renamed a linked device. No metadata — the chosen name
    /// is parent free-text that could carry a child's name (PII); we record
    /// only that a rename occurred. Current name lives on the Device row.</summary>
    public static AuditEvent ParentDeviceRenamed(Guid parentId, Guid deviceId) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentDeviceRenamed,
        ActorParentId = parentId,
        TargetDeviceId = deviceId
    };

    public static AuditEvent ParentDeviceLinked(Guid parentId, Guid deviceId) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentDeviceLinked,
        ActorParentId = parentId,
        TargetDeviceId = deviceId
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

    /// <summary>Slice E — a parent toggled bedtime music on a linked
    /// device. Post-change flag only; written only on real flips.</summary>
    public static AuditEvent ParentBedtimeMusicSet(
        Guid parentId, Guid deviceId, bool enabled) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentBedtimeMusicSet,
        ActorParentId = parentId,
        TargetDeviceId = deviceId,
        Metadata = JsonSerializer.Serialize(new
        {
            enabled = enabled
        })
    };

    /// <summary>Slice F — a parent submitted a custom-story request.
    /// Metadata: bounded type + has-photo only; the free text stays on the
    /// request row (audit never carries parent-authored content).</summary>
    public static AuditEvent ParentStoryRequestSubmitted(
        Guid parentId, Guid requestId, string type, bool hasPhoto) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentStoryRequestSubmitted,
        ActorParentId = parentId,
        Metadata = JsonSerializer.Serialize(new
        {
            request_id = requestId,
            type = type,
            has_photo = hasPhoto
        })
    };

    /// <summary>Slice F — a console operator moved a story request through
    /// its lifecycle. Same InternalConsoleAction envelope as the other
    /// operator mutations (ActorParentId null keeps it console-only).</summary>
    public static AuditEvent InternalConsoleStoryRequestStatus(
        string operatorName, Guid requestId, string status, string reason) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.InternalConsoleAction,
        ActorParentId = null,
        Metadata = JsonSerializer.Serialize(new
        {
            @operator = operatorName,
            action = "story_request_status",
            request_id = requestId,
            status = status,
            reason = reason
        })
    };

    /// <summary>A parent toggled the spoken story intro on a linked device.
    /// Metadata is the post-change flag only. Written only on a real flip
    /// (idempotent no-ops write nothing — same contract as pause/revoke).</summary>
    public static AuditEvent ParentDeviceStoryIntroSet(
        Guid parentId, Guid deviceId, bool enabled) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentDeviceStoryIntroSet,
        ActorParentId = parentId,
        TargetDeviceId = deviceId,
        Metadata = JsonSerializer.Serialize(new
        {
            enabled = enabled
        })
    };

    /// <summary>A parent toggled the in-story pauses on a linked device.
    /// Same envelope as <see cref="ParentDeviceStoryIntroSet"/>: post-change
    /// flag only, written only on a real flip.</summary>
    public static AuditEvent ParentDeviceStoryPausesSet(
        Guid parentId, Guid deviceId, bool enabled) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentDeviceStoryPausesSet,
        ActorParentId = parentId,
        TargetDeviceId = deviceId,
        Metadata = JsonSerializer.Serialize(new
        {
            enabled = enabled
        })
    };

    /// <summary>A parent toggled variant endings on a linked device. Same
    /// envelope as <see cref="ParentDeviceStoryIntroSet"/>: post-change flag
    /// only, written only on a real flip.</summary>
    public static AuditEvent ParentDeviceVariantEndingsSet(
        Guid parentId, Guid deviceId, bool enabled) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentDeviceVariantEndingsSet,
        ActorParentId = parentId,
        TargetDeviceId = deviceId,
        Metadata = JsonSerializer.Serialize(new
        {
            enabled = enabled
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
        int batchSizeLimit,
        // C2.2b — audio cleanup roll-up across the conversations
        // this tick deleted. Defaults preserve compatibility with
        // any future caller that omits the cleanup signal; the
        // retention worker always populates them with real counts.
        int audioConversationsAttempted = 0,
        int audioFilesDeleted = 0,
        int audioDeleteFailures = 0) => new()
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
            batch_size_limit = batchSizeLimit,
            audio_conversations_attempted = audioConversationsAttempted,
            audio_files_deleted = audioFilesDeleted,
            audio_delete_failures = audioDeleteFailures
        })
    };

    /// <summary>
    /// Manual parent-driven conversation deletion via
    /// <c>DELETE /api/conversations/{id}</c>. Complements the scheduled
    /// retention sweep: this one has a real human actor (the
    /// authenticated parent), so it lands in the parent-scoped audit
    /// feed and the parent-scoped slice of the export — unlike
    /// <see cref="ConversationsPurgedByRetention"/>, whose
    /// <see cref="ActorParentId"/> is null by design.
    /// <para>
    /// <see cref="TargetDeviceId"/> is populated so the parent
    /// dashboard's existing device-name resolution works; the
    /// conversation id lives in metadata because there is no
    /// dedicated <c>TargetConversationId</c> column (and one is not
    /// justified by a single event type). Metadata is counts/ids
    /// only — no PII, no message content.
    /// </para>
    /// </summary>
    public static AuditEvent ParentConversationDeleted(
        Guid parentId,
        Guid deviceId,
        Guid conversationId,
        int messageCountDeleted,
        // C2.2a — audio cleanup signal for this single-conversation
        // delete. Single-conversation scope means audio_conversations_
        // attempted is always 1 here in practice; kept consistent with
        // the multi-conversation factories so downstream readers can
        // sum across audit events without special-casing event types.
        int audioConversationsAttempted = 0,
        int audioFilesDeleted = 0,
        int audioDeleteFailures = 0) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentConversationDeleted,
        ActorParentId = parentId,
        TargetDeviceId = deviceId,
        TargetChildId = null,
        Metadata = JsonSerializer.Serialize(new
        {
            conversation_id = conversationId,
            message_count_deleted = messageCountDeleted,
            deleted_at_utc = DateTime.UtcNow,
            audio_conversations_attempted = audioConversationsAttempted,
            audio_files_deleted = audioFilesDeleted,
            audio_delete_failures = audioDeleteFailures
        })
    };

    /// <summary>
    /// Emitted on successful <c>POST /api/parents/password/reset-request</c>
    /// for a known email. The unknown-email path writes no audit row
    /// (part of the enumeration-resistance contract — an audit side
    /// effect would re-introduce a signal an attacker with DB read
    /// could observe). Metadata is deliberately empty: the raw token
    /// MUST NOT appear, the token hash MUST NOT appear, and there is
    /// no counts-shaped fact worth recording beyond "the event
    /// happened." Mirrors <see cref="ParentPasswordChanged"/>.
    /// </summary>
    public static AuditEvent ParentPasswordResetRequested(Guid parentId) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentPasswordResetRequested,
        ActorParentId = parentId
    };

    /// <summary>
    /// Emitted on successful <c>POST /api/parents/password/reset</c>.
    /// Written in the same <c>SaveChangesAsync</c> as the password
    /// update and the token's <c>ConsumedAt</c> stamp. Failure paths
    /// (unknown / expired / already-consumed token) write NO row —
    /// the 400 response is uniform and the audit trail MUST NOT
    /// differentiate those paths either. Zero metadata for the same
    /// reason as the request event.
    /// </summary>
    public static AuditEvent ParentPasswordResetCompleted(Guid parentId) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentPasswordResetCompleted,
        ActorParentId = parentId
    };

    /// <summary>
    /// Emitted by the scheduled dormant-parent warn pass on every
    /// successful notifier delivery (one row per warned parent). A
    /// notifier-side swallowed failure writes NO row — the next tick
    /// retries.
    /// <para>
    /// <b>System-actor event.</b> <see cref="ActorParentId"/> is
    /// deliberately <c>null</c> — the worker, not the parent, is the
    /// actor. That null is also what keeps this event out of every
    /// parent-facing read surface (<c>GET /api/parents/audit</c> and
    /// the parent-scoped audit slice of <c>GET /api/parents/export</c>
    /// both filter <c>ActorParentId == parentId</c>). Same
    /// invisibility contract as <see cref="ConversationsPurgedByRetention"/>.
    /// </para>
    /// <para>
    /// Metadata is counts-only / operational — last-login timestamp,
    /// the effective warn threshold, and the refire interval in use
    /// at send time. <b>No email, no parent identifier, no PII.</b>
    /// The audit row is a "something happened" signal with operational
    /// context; per-parent reconstruction (if ever needed) is done by
    /// querying <c>Parents</c> filtered by <c>DormancyWarnedAt</c>.
    /// </para>
    /// </summary>
    public static AuditEvent ParentDormancyWarned(
        DateTime lastLoginAtUtc,
        int warnThresholdDays,
        int refireIntervalDays) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentDormancyWarned,
        ActorParentId = null,
        TargetDeviceId = null,
        TargetChildId = null,
        Metadata = JsonSerializer.Serialize(new
        {
            last_login_at_utc = lastLoginAtUtc,
            warn_threshold_days = warnThresholdDays,
            refire_interval_days = refireIntervalDays
        })
    };

    /// <summary>
    /// Emitted by the scheduled destructive dormant-parent pass on
    /// every successful anonymize (one row per anonymized parent).
    /// <para>
    /// <b>System-actor event.</b> <see cref="ActorParentId"/> is
    /// deliberately <c>null</c> — the worker, not the parent, is the
    /// actor. That null is what keeps this event out of every
    /// parent-facing read surface (<c>GET /api/parents/audit</c> and
    /// the parent-scoped audit slice of <c>GET /api/parents/export</c>
    /// both filter <c>ActorParentId == parentId</c>). Same
    /// invisibility contract as <see cref="ConversationsPurgedByRetention"/>
    /// and <see cref="ParentDormancyWarned"/>.
    /// </para>
    /// <para>
    /// Metadata is counts-only / operational — the effective warn /
    /// anonymize thresholds and refire interval at action time, plus
    /// device-cascade counts. <b>No email, no parent identifier, no
    /// PII.</b> The audit row signals "a destructive action fired
    /// with these parameters" — per-parent identity can be
    /// reconstructed by querying the <c>Parents</c> table for
    /// <c>AnonymizedAt</c> in a matching timestamp window if
    /// post-facto analysis is ever needed.
    /// </para>
    /// </summary>
    public static AuditEvent ParentDormancyAnonymized(
        int warnAfterDays,
        int anonymizeAfterDays,
        int warnRefireIntervalDays,
        int linkedDevicesUnlinked,
        int orphanDevicesCascaded,
        // C2.2b — audio cleanup roll-up across the orphan-cascaded
        // devices. Always 0 when the parent only had shared devices
        // (no orphan cascade fired), even if a notional cleanup was
        // attempted with an empty id list. Defaults preserve
        // compatibility with any future caller that omits the
        // cleanup signal.
        int audioConversationsAttempted = 0,
        int audioFilesDeleted = 0,
        int audioDeleteFailures = 0) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentDormancyAnonymized,
        ActorParentId = null,
        TargetDeviceId = null,
        TargetChildId = null,
        Metadata = JsonSerializer.Serialize(new
        {
            warn_after_days = warnAfterDays,
            anonymize_after_days = anonymizeAfterDays,
            warn_refire_interval_days = warnRefireIntervalDays,
            linked_devices_unlinked = linkedDevicesUnlinked,
            orphan_devices_cascaded = orphanDevicesCascaded,
            audio_conversations_attempted = audioConversationsAttempted,
            audio_files_deleted = audioFilesDeleted,
            audio_delete_failures = audioDeleteFailures
        })
    };

    /// <summary>
    /// Emitted on successful <c>POST /api/parents/verify</c>. Written
    /// in the same <c>SaveChangesAsync</c> as the
    /// <see cref="Parent.EmailVerifiedAt"/> stamp and the token's
    /// <c>ConsumedAt</c>. Failure paths (unknown / expired /
    /// already-consumed token) write NO row — the uniform 400
    /// response on failure is mirrored in the audit trail by
    /// absence. Zero metadata for the same reason as
    /// <see cref="ParentPasswordResetCompleted"/>: the event
    /// happening is the fact; there is no counts-shaped additional
    /// context worth recording.
    /// </summary>
    public static AuditEvent ParentEmailVerified(Guid parentId) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentEmailVerified,
        ActorParentId = parentId
    };

    /// <summary>
    /// Emitted by the scheduled dormant-device warn pass on every
    /// tick where a device was successfully warned (at least one of
    /// its verified linked parents received the email). One row per
    /// warned device, not per recipient.
    /// <para>
    /// <b>System-actor event.</b> <see cref="ActorParentId"/> is
    /// deliberately <c>null</c> — the worker, not a parent, is the
    /// actor. That null is what keeps this event out of every
    /// parent-facing read surface (<c>GET /api/parents/audit</c> and
    /// the parent-scoped audit slice of <c>GET /api/parents/export</c>
    /// both filter <c>ActorParentId == parentId</c>). Same
    /// invisibility contract as <see cref="ConversationsPurgedByRetention"/>,
    /// <see cref="ParentDormancyWarned"/>, and
    /// <see cref="ParentDormancyAnonymized"/>.
    /// </para>
    /// <para>
    /// <b>Distinct from the parent dormancy events</b> in that
    /// <see cref="TargetDeviceId"/> IS populated here — the device
    /// id is the natural target of a device-level audit row, and
    /// the column already exists from the manual-unlink audit
    /// shape. <see cref="TargetChildId"/> remains null.
    /// </para>
    /// <para>
    /// Metadata is counts-only / operational — the effective
    /// thresholds, the device's last-seen-at timestamp, and the
    /// fan-out tally (linked-parents-total, of which verified, of
    /// which actually delivered to). <b>No emails, no parent ids,
    /// no device name.</b>
    /// </para>
    /// </summary>
    /// <summary>
    /// Emitted on every successful
    /// <c>POST /api/parents/google-login</c>. Actor is the parent who
    /// just authenticated. Metadata carries the minimum needed to
    /// distinguish returning sign-in from first-time link / first-time
    /// signup without leaking any PII — no email, no Google subject,
    /// no token, no timestamp beyond the row's own <see cref="Timestamp"/>.
    /// </summary>
    public static AuditEvent ParentGoogleSignIn(
        Guid parentId, bool firstTime, bool linkedToPasswordAccount) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ParentGoogleSignIn,
        ActorParentId = parentId,
        Metadata = JsonSerializer.Serialize(new
        {
            first_time = firstTime,
            linked_to_password_account = linkedToPasswordAccount
        })
    };

    /// <summary>
    /// Emitted by the scheduled dormant-device destructive pass on
    /// every successful whole-device delete (one row per deleted
    /// device, not per linked parent).
    /// <para>
    /// <b>System-actor event.</b> <see cref="ActorParentId"/> is
    /// <c>null</c> — the worker, not a parent, is the actor. That
    /// null keeps this row out of every parent-facing read surface
    /// (<c>GET /api/parents/audit</c> and the audit slice of
    /// <c>GET /api/parents/export</c> both filter
    /// <c>ActorParentId == parentId</c>). Same invisibility
    /// contract as <see cref="DeviceDormancyWarned"/> and every
    /// other system-actor event.
    /// </para>
    /// <para>
    /// <see cref="TargetDeviceId"/> is populated (the deleted
    /// device's id at the time of delete); <see cref="TargetChildId"/>
    /// remains null. Metadata is counts-only / operational — no
    /// device name, no parent emails, no parent ids.
    /// </para>
    /// </summary>
    public static AuditEvent DeviceDormancyDeleted(
        Guid deviceId,
        int warnAfterDays,
        int deleteAfterDays,
        DateTime lastSeenAtUtc,
        int linkedParentsAtDelete,
        int childrenDeleted,
        int conversationsDeleted,
        int messagesDeleted,
        // C2.2b — audio cleanup roll-up for the device's
        // conversations. Defaults preserve compatibility with any
        // future caller that omits the cleanup signal; the
        // retention worker always populates them with real counts.
        int audioConversationsAttempted = 0,
        int audioFilesDeleted = 0,
        int audioDeleteFailures = 0) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.DeviceDormancyDeleted,
        ActorParentId = null,
        TargetDeviceId = deviceId,
        TargetChildId = null,
        Metadata = JsonSerializer.Serialize(new
        {
            warn_after_days = warnAfterDays,
            delete_after_days = deleteAfterDays,
            last_seen_at_utc = lastSeenAtUtc,
            linked_parents_at_delete = linkedParentsAtDelete,
            children_deleted = childrenDeleted,
            conversations_deleted = conversationsDeleted,
            messages_deleted = messagesDeleted,
            audio_conversations_attempted = audioConversationsAttempted,
            audio_files_deleted = audioFilesDeleted,
            audio_delete_failures = audioDeleteFailures
        })
    };

    public static AuditEvent DeviceDormancyWarned(
        Guid deviceId,
        int warnAfterDays,
        int warnRefireIntervalDays,
        DateTime lastSeenAtUtc,
        int linkedParentsTotal,
        int linkedParentsVerified,
        int verifiedRecipientsNotified) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.DeviceDormancyWarned,
        ActorParentId = null,
        TargetDeviceId = deviceId,
        TargetChildId = null,
        Metadata = JsonSerializer.Serialize(new
        {
            warn_after_days = warnAfterDays,
            warn_refire_interval_days = warnRefireIntervalDays,
            last_seen_at_utc = lastSeenAtUtc,
            linked_parents_total = linkedParentsTotal,
            linked_parents_verified = linkedParentsVerified,
            verified_recipients_notified = verifiedRecipientsNotified
        })
    };

    /// <summary>
    /// #013 — a superuser-console operator read child-bearing data (a
    /// conversation transcript, the flagged feed, or a device's conversations).
    /// <see cref="ActorParentId"/> is null (the actor is an operator, not a
    /// parent) so this stays out of parent-facing feeds, exactly like the other
    /// system-actor events. The operator IDENTITY lives in metadata along with
    /// the endpoint, target id, and row count — operational data, NOT child PII.
    /// </summary>
    public static AuditEvent InternalConsoleAccess(
        string operatorName, string endpoint, Guid? targetId, int count) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.InternalConsoleAccess,
        ActorParentId = null,
        TargetDeviceId = null,
        TargetChildId = null,
        Metadata = JsonSerializer.Serialize(new
        {
            @operator = operatorName,
            endpoint = endpoint,
            target = targetId,
            count = count
        })
    };

    /// <summary>
    /// Phase 3 — a superuser-console operator performed a reversible device
    /// action (revoke/restore, pause/resume). <see cref="ActorParentId"/> is
    /// null (operator, not a parent) so it stays out of parent-facing feeds,
    /// but <see cref="TargetDeviceId"/> IS set so the affected device is
    /// queryable. Metadata carries the operator identity, the action, the new
    /// value, and the required operator reason — operational data, no child PII.
    /// </summary>
    public static AuditEvent InternalConsoleAction(
        string operatorName, string action, Guid targetDeviceId, bool value, string reason) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.InternalConsoleAction,
        ActorParentId = null,
        TargetDeviceId = targetDeviceId,
        TargetChildId = null,
        Metadata = JsonSerializer.Serialize(new
        {
            @operator = operatorName,
            action = action,
            value = value,
            reason = reason
        })
    };

    /// <summary>
    /// A superuser-console operator reset a parent's password (owner-recovery
    /// path). This is the console's highest-blast-radius mutation — full
    /// account takeover of any parent — so it gets the same DURABLE audit row
    /// the reversible device actions get, not just a stdout log line: stdout
    /// has no retention owner in this repo (see § Retention), so a log-only
    /// record of an account takeover can silently age out.
    /// <see cref="ActorParentId"/> is null (operator, not a parent) so the row
    /// stays out of parent-facing feeds; the affected parent is recorded in
    /// metadata rather than as an actor. NO password material, ever.
    /// </summary>
    public static AuditEvent InternalConsolePasswordReset(
        string operatorName, Guid targetParentId, string reason) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.InternalConsoleAction,
        ActorParentId = null,
        TargetDeviceId = null,
        TargetChildId = null,
        Metadata = JsonSerializer.Serialize(new
        {
            @operator = operatorName,
            action = "parent_password_reset",
            target_parent_id = targetParentId,
            reason = reason
        })
    };
}

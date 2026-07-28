namespace ArmenianAiToy.Domain.Enums;

public enum AuditEventType
{
    ParentAccountDeleted,
    ParentChildDeleted,
    ParentDeviceUnlinked,
    ParentPasswordChanged,
    ParentDevicePauseStateChanged,
    ParentBedtimeWindowSet,
    ParentDeviceModeFlagsSet,
    ChildModeOverridesSet,
    ParentDataExported,
    ConversationsPurgedByRetention,
    ParentConversationDeleted,
    ParentPasswordResetRequested,
    ParentPasswordResetCompleted,
    ParentDormancyWarned,
    ParentDormancyAnonymized,
    ParentEmailVerified,
    DeviceDormancyWarned,
    ParentGoogleSignIn,
    DeviceDormancyDeleted,

    /// <summary>#013 — a superuser console operator read child-bearing data
    /// (a conversation transcript, the flagged feed, or a device's
    /// conversations). System-actor-style row (ActorParentId null, so it stays
    /// out of parent-facing feeds); metadata carries the operator name + the
    /// endpoint + target id + count. No child PII.</summary>
    InternalConsoleAccess,

    /// <summary>Phase 3 — a superuser console operator performed a reversible
    /// device ACTION (revoke/restore or pause/resume). System-actor-style row
    /// (ActorParentId null, console-only) but TargetDeviceId IS set; metadata
    /// carries the operator name, the action, the new value, and the required
    /// reason. Written only when the flag actually changed.</summary>
    InternalConsoleAction,

    /// <summary>#074 — a parent revoked or restored a linked device's
    /// server-side credential kill-switch. Metadata carries the post-change
    /// state (is_revoked: bool). Written only when the flag actually flips.</summary>
    ParentDeviceRevocationChanged,

    /// <summary>Platform pairing (Phase A.2) — a parent successfully CLAIMED a
    /// device to their account via its single-use claim code. ActorParentId =
    /// the claimer; TargetDeviceId = the toy. No claim code in metadata.</summary>
    ParentDeviceClaimed,

    /// <summary>A parent renamed one of their linked devices (multi-toy
    /// labeling). ActorParentId = the parent; TargetDeviceId = the toy. The new
    /// name is NOT recorded (parent-chosen free text may contain a child's
    /// name → potential PII); only that a rename happened.</summary>
    ParentDeviceRenamed,

    /// <summary>A parent attached to a device via the legacy API-key link path
    /// (POST /api/parents/devices/link) — distinct from the single-use claim-code
    /// path (ParentDeviceClaimed). ActorParentId = the linker; TargetDeviceId =
    /// the toy. Gives the durable record the link path previously lacked so a
    /// second-parent attach on an already-claimed toy is not invisible.</summary>
    ParentDeviceLinked
}

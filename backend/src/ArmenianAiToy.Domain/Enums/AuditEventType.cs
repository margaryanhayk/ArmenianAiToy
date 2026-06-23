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

    /// <summary>#074 — a parent revoked or restored a linked device's
    /// server-side credential kill-switch. Metadata carries the post-change
    /// state (is_revoked: bool). Written only when the flag actually flips.</summary>
    ParentDeviceRevocationChanged
}

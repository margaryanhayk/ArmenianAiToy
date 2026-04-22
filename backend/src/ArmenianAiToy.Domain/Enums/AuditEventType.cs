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
    ParentPasswordResetCompleted
}

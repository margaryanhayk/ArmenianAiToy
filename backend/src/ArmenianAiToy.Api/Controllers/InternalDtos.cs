using System.Text.Json;

namespace ArmenianAiToy.Api.Controllers;

// Response shapes for the superuser internal console (/api/internal/*).
// READ-ONLY god-view. Two hard invariants, enforced by construction
// (these DTOs simply do not carry the forbidden fields):
//   • NEVER expose Device.ApiKey / Device.ApiKeyHash.
//   • NEVER expose Parent.PasswordHash.
// Google linkage is surfaced as a bool only, never the raw subject.

public sealed record AdminOverviewDto(
    int Devices,
    int Parents,
    int Children,
    int Conversations,
    int Messages,
    int FlaggedMessages,
    int MessagesToday,
    int FlaggedToday,
    int PausedDevices,
    decimal CostTodayUsd,
    bool DatabaseReachable,
    DateTime GeneratedAtUtc);

public sealed record AdminChildDto(
    Guid Id,
    string Name,
    string Gender,
    int? Age,
    bool? StoryEnabled,
    bool? GameEnabled,
    bool? RiddleEnabled,
    bool? CuriosityEnabled);

public sealed record AdminDeviceDto(
    Guid Id,
    string Name,
    string MacAddress,
    string? FirmwareVersion,
    // OTA status split (bench caveat fix): LastOtaStatus is the device-
    // reported LAST-ATTEMPT outcome (sticky diagnostic, e.g.
    // "failed:sha256_mismatch"); OtaHealth is the DERIVED current health
    // ("ok"/"updating"/"offline" via DeviceOtaHealth) so a healthy,
    // checking-in device is never painted broken by an old failed attempt.
    string? LastOtaStatus,
    string OtaHealth,
    DateTime RegisteredAt,
    DateTime LastSeenAt,
    bool IsPaused,
    bool IsRevoked,
    TimeOnly? BedtimeStart,
    TimeOnly? BedtimeEnd,
    string TimeZone,
    bool StoryEnabled,
    bool GameEnabled,
    bool RiddleEnabled,
    bool CuriosityEnabled,
    DateTime? DormancyWarnedAt,
    int LinkedParents,
    decimal CostTodayUsd,
    IReadOnlyList<AdminChildDto> ChildrenList);

public sealed record AdminParentDto(
    Guid Id,
    string Email,
    DateTime RegisteredAt,
    DateTime? EmailVerifiedAt,
    DateTime? LastLoginAt,
    DateTime? AnonymizedAt,
    string? TermsVersion,
    bool GoogleLinked,
    int LinkedDevices,
    int AuditEvents);

public sealed record AdminStoryDto(
    string Id,
    string Title,
    int MinAge,
    int MaxAge,
    string Tone,
    int Segments,
    bool BedtimeSafe,
    bool HasReflectionText,
    int ReflectionQuestions,
    bool IsDefault);

public sealed record AdminFlaggedMessageDto(
    Guid Id,
    Guid ConversationId,
    Guid DeviceId,
    string Role,
    string Snippet,
    string SafetyFlag,
    DateTime Timestamp);

public sealed record AdminConversationSummaryDto(
    Guid Id,
    Guid DeviceId,
    Guid? ChildId,
    DateTime StartedAt,
    DateTime? EndedAt,
    int MessageCount,
    int FlaggedCount,
    string Snippet);

public sealed record AdminMessageDto(
    Guid Id,
    string Role,
    string Content,
    string SafetyFlag,
    DateTime Timestamp,
    bool AudioAvailable);

public sealed record AdminConversationDetailDto(
    Guid Id,
    Guid DeviceId,
    Guid? ChildId,
    DateTime StartedAt,
    DateTime? EndedAt,
    IReadOnlyList<AdminMessageDto> Messages);

public sealed record AdminAuditDto(
    Guid Id,
    DateTime Timestamp,
    string EventType,
    Guid? ActorParentId,
    Guid? TargetDeviceId,
    Guid? TargetChildId,
    JsonElement? Metadata);

// JIT session exchange: the operator presents their static token (first factor,
// in the Authorization header) and — when MFA is configured for them — a TOTP
// code (second factor) to mint a short-lived session token.
public sealed record InternalSessionRequest(string? Totp);

// Phase 3 — operator device action (reversible). Value = the new flag state
// (revoked/paused); Reason is required and recorded in the audit row.
public sealed record InternalDeviceActionRequest(bool Value, string Reason);

/// <summary>Slice F — operator moves a story request through its
/// lifecycle. Bounded status vocabulary validated at the action.</summary>
public sealed record InternalStoryRequestStatusRequest(string? Status, string? Reason);

// OTA foundation — bench/test enqueue of a device command. Type must be a
// known DeviceCommandTypes value; Payload is an optional JSON object stored
// verbatim; TtlSeconds bounds how long the command stays deliverable
// (default 3600, clamped 60..86400).
public sealed record InternalEnqueueCommandRequest(
    string? Type,
    System.Text.Json.JsonElement? Payload = null,
    int? TtlSeconds = null);

// Owner recovery — set a locked-out parent's password. Console-gated
// (fail-closed 404 unless the admin token is configured). Reason required;
// the new password is never logged or echoed.
public sealed record InternalParentPasswordResetRequest(string Email, string NewPassword, string Reason);

// Story-QA tuning playground (Phase 2). Operator-only; text-only; calls
// OpenAI (cost) but mutates nothing and persists nothing.
public sealed record AdminStoryQaTestRequest(
    string StoryId,
    int SegmentIndex,
    string Question);

public sealed record AdminStoryQaTestResult(
    string StoryId,
    int SegmentIndex,
    string SegmentText,
    string Question,
    string Answer,
    bool UsedFallback,
    bool InputSafe,
    bool OutputSafe,
    string FirstRejection,
    string? RetryRejection,
    string Outcome);

using ArmenianAiToy.Application.Helpers;
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
    IReadOnlyList<AdminChildDto> ChildrenList)
{
    /// <summary>
    /// What the toy reports it actually has on its card, and the derived
    /// verdict against what the manifest advertises. The operator half of
    /// the content report: a parent is told ready / not ready, but
    /// diagnosing a stuck sync needs the NAMES — knowing that
    /// «Խոսող ձուկը» specifically is missing is the difference between a
    /// diagnosis and a shrug.
    /// <para>
    /// Init-props so every existing construction site and test double
    /// compiles unchanged.
    /// </para>
    /// </summary>
    public string ContentHealth { get; init; } = DeviceContentHealth.Unknown;

    /// <summary>Advertised stories the toy does NOT hold at the advertised
    /// version. Empty when it has never reported — naming every story as
    /// missing would be a fabricated fault.</summary>
    public IReadOnlyList<string> MissingStoryIds { get; init; } = Array.Empty<string>();

    /// <summary>The raw device-reported <c>id:version</c> list, verbatim.
    /// Verbatim on purpose: when the derived verdict looks wrong, the
    /// operator needs to see what the toy actually said.</summary>
    public string? ContentStories { get; init; }

    public int? ContentIndexSchema { get; init; }
    public int? ContentGameClips { get; init; }
    public int? ContentVoiceClips { get; init; }
    public int? ContentMusicTracks { get; init; }
    public DateTime? ContentReportedAt { get; init; }

    /// <summary>
    /// What the toy's last sync ATTEMPT did, beside the report of what it
    /// HAS. The content report answers "which stories are on the card"; it
    /// cannot answer "why did nothing new arrive", because a failed sync
    /// leaves the card as it was and the firmware's carry-forward then
    /// re-advertises the old entry as verified.
    /// <para>
    /// The raw strings are surfaced verbatim, like <c>ContentStories</c>
    /// above and for the same reason: when the derived verdict looks wrong,
    /// the operator needs to see what the toy actually said.
    /// <c>ResetReason</c> and <c>BootCount</c> are what make a crash loop
    /// visible at all — a toy that panics and reboots every ~184 s heartbeats
    /// normally in between, so <c>LastSeenAt</c> stays fresh and it reads as
    /// online right up until someone attaches a serial cable.
    /// </para>
    /// </summary>
    public string? ContentSyncStatus { get; init; }
    public string? ContentSyncError { get; init; }
    public DateTime? ContentSyncReportedAt { get; init; }
    public DateTime? ContentSyncedAt { get; init; }
    public string? ResetReason { get; init; }
    public int? BootCount { get; init; }

    /// <summary>
    /// The rest of the device-reported identity. The toy has sent all three
    /// on every heartbeat since the OTA foundation and the backend has
    /// stored them since then, but nothing ever displayed them — so the
    /// answer to "what board is this toy?" was a serial cable.
    /// <para>
    /// <c>BoardModel</c> is the one that bites: the firmware offer gate
    /// skips a device whose board differs from the configured release, so a
    /// mismatch here produces <c>updateAvailable:false</c> with no error and
    /// no log — a silently blocked rollout. Being able to read it is how
    /// that gets diagnosed. Null means firmware old enough not to report.
    /// </para>
    /// </summary>
    public string? BoardModel { get; init; }
    public string? FirmwareBuild { get; init; }
    public string? PartitionName { get; init; }
    public DateTime? FirmwareReportedAt { get; init; }
}

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

/// <summary>An operator action that carries no value of its own — only the
/// mandatory reason that every console action must record.</summary>
public sealed record InternalReasonRequest(string Reason);

/// <summary>
/// Per-toy content entitlement: give this toy this item
/// (<c>Value = true</c> → allow) or take it away (<c>Value = false</c> →
/// deny). <c>Value</c> rather than a mode string so this reads exactly like
/// every other console device action and reuses the same reason-prompt UI.
/// <para>
/// <c>ItemKey</c> is the item's address within its namespace — a story id, a
/// track id, a voice clip id, or for a game the <c>gameKey/clipId</c> PAIR.
/// It is a lookup key, never a path.
/// </para>
/// </summary>
public sealed record InternalDeviceContentRequest(
    string? ItemKind, string? ItemKey, bool Value, string? Reason);

/// <summary>One row of the console's per-device content list: a catalogue
/// item and whether THIS toy currently gets it.</summary>
public sealed record AdminDeviceContentItemDto(
    string ItemKind,
    string ItemKey,
    string? Title,
    int? Version,
    bool Allowed,
    string? OverrideMode,
    string? OverrideReason,
    DateTime? OverrideAt,
    // Source: `catalogue` (shipped in the image) or `uploaded` (added from this
    // console). Trailing and defaulted so the Stage-3 rows are unaffected.
    string Source = "catalogue",
    // FleetDefault: whether the FLEET default sends this item. False marks a
    // fleet-dark upload — one that reaches this toy only because an operator
    // granted it. Without it the console cannot tell "everyone gets this and
    // nobody touched it" from "only this toy has it", and those are the two
    // halves of a staged rollout.
    bool FleetDefault = true,
    // Retired: the item is in no manifest at all, whatever any override says.
    // It is listed rather than hidden because an operator's own grant must
    // never become invisible — but `Allowed` is false for it, because retiring
    // an item is documented as removing it from every manifest and a console
    // that then reported "sent: yes" would be contradicting the product.
    bool Retired = false);

/// <summary>
/// One row of the console's CATALOGUE list — an owner-uploaded item, fleet-wide
/// rather than per-toy.
/// <para>
/// Carries no <c>RelativePath</c> and no absolute path: a server filesystem
/// path is internal, the same rule the parent export and every device DTO
/// follow. <see cref="FileOnDisk"/> is the honest substitute — it answers "the
/// row says this exists; does it?", which is the question an operator has after
/// a volume is remounted or a backup is restored.
/// </para>
/// </summary>
public sealed record AdminContentItemDto(
    Guid Id,
    string Kind,
    string ItemKey,
    string Title,
    int Version,
    string Sha256,
    long SizeBytes,
    bool DefaultEnabled,
    DateTime? RetiredAt,
    string CreatedBy,
    DateTime CreatedAt,
    bool FileOnDisk,
    // How many toys were explicitly sent this item. The middle step of the
    // whole workflow — "send it to ONE toy, listen, then release" — had no
    // readout at all without it: granting a story changed nothing visible, so
    // "uploaded and on nobody's toy" and "uploaded and on the bench toy" were
    // the same screen.
    int GrantedDeviceCount = 0);

/// <summary>
/// Release an uploaded item to the fleet (<c>Value = true</c>) or pull it back
/// to fleet-dark (<c>Value = false</c>); or retire it (<c>Value = true</c>) and
/// un-retire it (<c>Value = false</c>). Same <c>{ value, reason }</c> shape as
/// every other console action so the console reuses one reason-prompt.
/// </summary>
public sealed record InternalContentItemActionRequest(bool Value, string? Reason);

/// <summary>Slice F — operator moves a story request through its
/// lifecycle. Bounded status vocabulary validated at the action.</summary>
public sealed record InternalStoryRequestStatusRequest(string? Status, string? Reason);

// Operator enqueue of a device command. Type must be a known
// DeviceCommandTypes value; Payload is an optional JSON object stored
// verbatim; TtlSeconds bounds how long the command stays deliverable
// (default 3600, clamped 60..86400). Reason is REQUIRED — this is a real
// operator surface (the console's "Sync this toy now"), so it carries the
// same accountability as revoke and pause.
public sealed record InternalEnqueueCommandRequest(
    string? Type,
    System.Text.Json.JsonElement? Payload = null,
    int? TtlSeconds = null,
    string? Reason = null);

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

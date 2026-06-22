using System.Text.Json.Nodes;
using ArmenianAiToy.Domain.Enums;

namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Top-level envelope for <c>GET /api/parents/export</c> — the single
/// JSON document returned for a parent's full data export.
///
/// <para>
/// Invariants (see CLAUDE.md § Data export):
/// </para>
/// <list type="bullet">
/// <item><description>No credential material ever appears in the body —
/// <c>Parent.PasswordHash</c> and <c>Device.ApiKey</c> are deliberately
/// omitted. This is enforced by the projection shapes below: neither
/// field has a counterpart property.</description></item>
/// <item><description>No system telemetry, no prompt / model internals
/// beyond what is already surfaced through the parent-facing read
/// endpoints.</description></item>
/// <item><description>Scope is "this parent's own data" — every nested
/// collection is filtered by the authenticated parent's id at query
/// time. No cross-parent bleed, same invariant the audit read endpoint
/// enforces.</description></item>
/// </list>
/// </summary>
public record ParentExport(
    string SchemaVersion,
    DateTime GeneratedAt,
    ParentExportProfile Parent,
    List<ParentExportDevice> Devices,
    List<AuditEventDto> AuditEvents,
    string[] ExcludedFields);

/// <summary>
/// Safe parent fields only — no password hash. <c>GoogleSubject</c>
/// is included because it is the Google account identifier already
/// visible to the parent in their Google account settings; it is
/// user-owned data, not credential material, so an export that
/// omitted it would misrepresent the shape of a Google-linked
/// account. Null for password-only parents.
/// </summary>
public record ParentExportProfile(
    Guid Id,
    string Email,
    DateTime RegisteredAt,
    DateTime? TermsAcceptedAt,
    string? TermsVersion,
    DateTime? LastLoginAt,
    DateTime? EmailVerifiedAt,
    string? GoogleSubject);

/// <summary>
/// Safe device fields only — no <c>ApiKey</c>. All settings here are
/// already individually writable via existing parent endpoints
/// (pause/resume, bedtime window, device mode flags) so there is no
/// new disclosure surface.
/// </summary>
public record ParentExportDevice(
    Guid Id,
    string MacAddress,
    string Name,
    DateTime RegisteredAt,
    DateTime LastSeenAt,
    bool IsPaused,
    TimeOnly? BedtimeStart,
    TimeOnly? BedtimeEnd,
    string TimeZone,
    bool StoryEnabled,
    bool GameEnabled,
    bool RiddleEnabled,
    bool CuriosityEnabled,
    List<ParentExportChild> Children,
    List<ConversationDto> Conversations);

/// <summary>
/// Export-only projection of <see cref="ArmenianAiToy.Domain.Entities.Child"/>.
/// No public child-read endpoint exists today; this slice synthesises
/// the minimum needed shape rather than introducing one. If a general
/// child-read surface is added later, this can be consolidated.
/// <para>
/// <see cref="ModeOverrides"/> is the three-valued per-mode override
/// block already carried on <c>Child</c>: null = inherit device,
/// true = force on, false = force off.
/// </para>
/// </summary>
public record ParentExportChild(
    Guid Id,
    Guid DeviceId,
    string Name,
    Gender Gender,
    int? BirthYear,
    int? Age,
    ParentExportChildModeOverrides ModeOverrides);

public record ParentExportChildModeOverrides(
    bool? Story,
    bool? Game,
    bool? Riddle,
    bool? Curiosity);

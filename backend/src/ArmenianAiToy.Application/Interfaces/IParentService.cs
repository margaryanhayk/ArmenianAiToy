using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Notifications;

namespace ArmenianAiToy.Application.Interfaces;

public interface IParentService
{
    /// <summary>
    /// Register a new parent account. Anti-enumeration contract: this
    /// method returns without a differentiating signal whether the email
    /// was new or already registered — the only outward difference
    /// between the two paths is internal (DB state). Callers MUST NOT
    /// try to reconstruct the distinction. Throws only on request-shape
    /// violations (e.g. consent not accepted) as a defense-in-depth
    /// check, never on email collision.
    /// </summary>
    Task RegisterAsync(string email, string password, bool acceptedTerms);
    Task<ParentLoginResponse?> LoginAsync(string email, string password);
    Task<bool> LinkDeviceAsync(Guid parentId, Guid deviceId, string apiKey);
    Task<bool> UnlinkDeviceAsync(Guid parentId, Guid deviceId);
    Task<List<Guid>> GetLinkedDeviceIdsAsync(Guid parentId);
    Task<List<LinkedDeviceDto>> GetLinkedDeviceDetailsAsync(Guid parentId);

    /// <summary>
    /// Parent linked-device list PLUS a small self-scoped dormancy
    /// summary (device counts + raw <c>LastLoginAt</c>). Wraps
    /// <see cref="GetLinkedDeviceDetailsAsync"/> — single dormancy-
    /// derivation site, counts are aggregated from the already-derived
    /// <see cref="LinkedDeviceDto.IsDormant"/> booleans.
    /// <para>
    /// Reporting-only. No deletion / unlink / warning / notifier
    /// behavior is tied to this response.
    /// </para>
    /// </summary>
    Task<LinkedDevicesResponse> GetLinkedDeviceDetailsWithSummaryAsync(Guid parentId);
    Task<bool> ChangePasswordAsync(Guid parentId, string currentPassword, string newPassword);
    Task<bool> SetDevicePauseStateAsync(Guid parentId, Guid deviceId, bool paused);
    Task<bool> SetBedtimeWindowAsync(Guid parentId, Guid deviceId, TimeOnly? start, TimeOnly? end);
    Task<bool> SetDeviceModeFlagsAsync(
        Guid parentId, Guid deviceId,
        bool story, bool game, bool riddle, bool curiosity);
    Task<bool> DeleteChildAsync(Guid parentId, Guid childId);
    Task<bool> DeleteConversationAsync(Guid parentId, Guid conversationId);
    Task<bool> DeleteAccountAsync(Guid parentId, string currentPassword);

    /// <summary>
    /// Begin a password-reset flow for the given email. Anti-enumeration
    /// contract: this method returns without a differentiating signal
    /// whether the email was known or unknown. Callers MUST NOT try to
    /// reconstruct the distinction. For a known email, a single-use
    /// reset token is generated, its hash is persisted, and the
    /// registered <see cref="INotifier"/> is invoked with the raw
    /// token. For an unknown email, the method fakes equivalent
    /// timing (via the same BCrypt seam the register anti-enumeration
    /// slice uses) and then returns silently — no token row, no
    /// notifier call, no audit row.
    /// </summary>
    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Complete a password reset with a previously-issued token.
    /// Returns <c>true</c> on success, <c>false</c> on any failure
    /// (token unknown / expired / already consumed). Failure reasons
    /// are deliberately not distinguished on the wire — the controller
    /// maps <c>false</c> to a uniform 400 response.
    /// </summary>
    Task<bool> CompletePasswordResetAsync(string token, string newPassword);
    Task<bool> SetChildModeOverridesAsync(
        Guid parentId, Guid childId,
        bool? story, bool? game, bool? riddle, bool? curiosity);
    Task<List<AuditEventDto>> GetAuditEventsForParentAsync(Guid parentId, int limit, int offset);
    Task<ParentExport?> BuildExportAsync(Guid parentId);
}

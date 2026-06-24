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
    Task<bool> ClaimDeviceAsync(Guid parentId, Guid deviceId, string claimCode);
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
    Task<bool> SetDeviceRevocationAsync(Guid parentId, Guid deviceId, bool revoked);
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

    /// <summary>
    /// Begin the email-verification flow for the given email.
    /// Anti-enumeration contract: returns without a differentiating
    /// signal whether the email was unknown, known-verified, or
    /// known-unverified. The known-unverified path issues a token
    /// and calls <c>INotifier.SendEmailVerificationAsync</c>; the
    /// other two paths return silently. BCrypt-on-every-path timing
    /// normalization applies. Callers MUST NOT try to distinguish
    /// the three branches.
    /// </summary>
    Task RequestEmailVerificationAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Complete email verification with a previously-issued token.
    /// Returns <c>true</c> on success, <c>false</c> on any failure
    /// (unknown / expired / already-consumed / empty). Failure
    /// reasons are deliberately not distinguished — the controller
    /// maps <c>false</c> to a uniform 400 response.
    /// </summary>
    Task<bool> CompleteEmailVerificationAsync(string token);

    /// <summary>
    /// Minimal authenticated-parent profile lookup. Returns
    /// <see cref="ParentMeResponse"/> with the parent's email and
    /// verification timestamp. Returns <c>null</c> when the parent
    /// row no longer exists (JWT was valid but the account was
    /// deleted / anonymized between token issue and this call) — the
    /// controller maps null to a 404 in the same shape used by
    /// other parent-owned read endpoints.
    /// </summary>
    Task<ParentMeResponse?> GetMeAsync(Guid parentId);

    /// <summary>
    /// Exchange a Google ID token for a parent JWT. First-class
    /// additive auth method: returns the same JWT shape as password
    /// login on success. Internally validates the token via
    /// <c>IGoogleIdTokenValidator</c>, rejects unverified emails and
    /// audience mismatches, and applies the linking rules documented
    /// in CLAUDE.md § Google sign-in:
    /// <list type="number">
    /// <item><description>existing row with matching <c>GoogleSubject</c>
    /// → sign in (returning user);</description></item>
    /// <item><description>else existing row with matching <c>Email</c>
    /// (and <c>AnonymizedAt == null</c>) and <c>GoogleSubject == null</c>
    /// → link;</description></item>
    /// <item><description>else existing row with matching <c>Email</c>
    /// and non-null different <c>GoogleSubject</c> →
    /// <see cref="GoogleSignInStatus.InvalidToken"/>;</description></item>
    /// <item><description>else create a new parent.</description></item>
    /// </list>
    /// Anonymized rows are never matched/reused. Existing
    /// <c>EmailVerifiedAt</c> is never overwritten. Audits exactly
    /// one <c>ParentGoogleSignIn</c> row on success.
    /// </summary>
    Task<GoogleSignInResult> GoogleSignInAsync(
        string idToken, bool acceptedTerms, CancellationToken cancellationToken = default);
    Task<bool> SetChildModeOverridesAsync(
        Guid parentId, Guid childId,
        bool? story, bool? game, bool? riddle, bool? curiosity);
    Task<List<AuditEventDto>> GetAuditEventsForParentAsync(Guid parentId, int limit, int offset);
    Task<ParentExport?> BuildExportAsync(Guid parentId);

    /// <summary>
    /// C2.1 — resolve a message id for parent-dashboard audio replay.
    /// Returns the (conversationId, messageId) pair iff <b>all</b> of:
    /// <list type="bullet">
    ///   <item>the message exists,</item>
    ///   <item>the authenticated parent owns the device that the
    ///   message's conversation belongs to (Message → Conversation →
    ///   Device → ParentDevice),</item>
    ///   <item>the message role is <see cref="Domain.Enums.MessageRole.Assistant"/>
    ///   (child WAV uploads are never replayable in C2.1 even if their
    ///   AudioBlobPath is populated),</item>
    ///   <item><see cref="Domain.Entities.Message.AudioBlobPath"/> is non-null.</item>
    /// </list>
    /// Returns <c>null</c> on every other case. Failure reasons are
    /// deliberately not distinguished — the controller maps null to a
    /// uniform 404 so a parent cannot probe message existence,
    /// ownership across families, or whether a given message has an
    /// audio attachment.
    /// </summary>
    Task<(Guid ConversationId, Guid MessageId)?> GetAssistantAudioMessageAsync(
        Guid parentId, Guid messageId);
}

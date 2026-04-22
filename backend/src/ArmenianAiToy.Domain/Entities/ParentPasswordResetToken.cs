namespace ArmenianAiToy.Domain.Entities;

/// <summary>
/// Server-side state for a single-use parent password-reset token.
///
/// <para>
/// The raw token is never stored — only <see cref="TokenHash"/>, which
/// is a hash of the 32-byte random value we emitted to the parent via
/// the notifier. A DB exfil therefore does not yield usable tokens.
/// The raw token travels exactly once: from the reset-request endpoint
/// through <see cref="Application.Notifications.INotifier"/> to the
/// parent's email address (today, via <c>LoggingNotifier</c>). The
/// completion endpoint re-hashes the submitted token and looks it up
/// here.
/// </para>
///
/// <para>
/// Single-use is enforced by <see cref="ConsumedAt"/> — set to the
/// completion timestamp once the token has been redeemed. A token
/// whose <c>ConsumedAt</c> is non-null is dead, even if still within
/// <see cref="ExpiresAt"/>. Expiry is authoritative and is checked on
/// every completion attempt.
/// </para>
///
/// <para>
/// <see cref="ParentId"/> is a real FK with cascade-on-delete. A
/// deleted parent takes their pending tokens with them. (Contrast
/// <see cref="AuditEvent"/>, which is deliberately FK-less because
/// audit rows must outlive their subjects.)
/// </para>
/// </summary>
public class ParentPasswordResetToken
{
    public Guid Id { get; set; }
    public Guid ParentId { get; set; }
    public Parent Parent { get; set; } = null!;

    /// <summary>
    /// Hash of the emitted token (not the token itself). Unique so
    /// the completion endpoint can look up by hash without needing
    /// <see cref="ParentId"/> up front.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Set to <c>DateTime.UtcNow</c> at successful completion.
    /// Non-null => token is dead; used by the completion lookup to
    /// enforce single-use independently of expiry.
    /// </summary>
    public DateTime? ConsumedAt { get; set; }
}

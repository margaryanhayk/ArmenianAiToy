namespace ArmenianAiToy.Domain.Entities;

/// <summary>
/// Server-side state for a single-use parent email-verification token.
///
/// <para>
/// Modeled exactly on <see cref="ParentPasswordResetToken"/>: the raw
/// token is never stored, only <see cref="TokenHash"/>. The raw token
/// travels exactly once — from the register / verify-request endpoint
/// through <see cref="Application.Notifications.INotifier"/> to the
/// parent's email. The completion endpoint re-hashes the submitted
/// token and looks it up here.
/// </para>
///
/// <para>
/// Single-use is enforced by <see cref="ConsumedAt"/>. A token with
/// non-null <c>ConsumedAt</c> is dead even if still within
/// <see cref="ExpiresAt"/>. Expiry is authoritative and is checked on
/// every completion attempt. TTL is configurable via
/// <c>Auth:EmailVerificationTokenTtlHours</c> (default 168 = 7 days)
/// — longer than the password-reset window because verification is
/// less time-sensitive.
/// </para>
///
/// <para>
/// <see cref="ParentId"/> is a real FK with cascade-on-delete. A
/// deleted parent takes their pending tokens with them, same contract
/// as <see cref="ParentPasswordResetToken"/>. Distinct from
/// <see cref="AuditEvent"/>, which is FK-less because audit rows
/// must outlive their subjects.
/// </para>
/// </summary>
public class ParentEmailVerificationToken
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
    /// Non-null => token is dead.
    /// </summary>
    public DateTime? ConsumedAt { get; set; }
}

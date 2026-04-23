namespace ArmenianAiToy.Application.Notifications;

/// <summary>
/// Minimal outbound-notification seam. Introduced in the forgot-password
/// slice as the FIRST consumer of this abstraction — matches the repo's
/// established "abstraction ships with first consumer" pattern
/// (<c>RateLimiting</c> came with chat, <c>AuditEvent</c> came with the
/// first sensitive parent action, <c>JwtKeys</c> came with rotation
/// becoming useful).
///
/// <para>
/// <b>Typed methods, not a generic envelope.</b> Each notification kind
/// gets its own method so call sites are explicit about what they are
/// sending and unit tests can spy on a single call signature without
/// reflecting over a payload union. Future consumers (dormant-purge
/// warnings, register-collision notifications, etc.) add their own
/// typed methods when they land — this interface grows by one method
/// per new consumer, not by one generic-envelope case statement.
/// </para>
///
/// <para>
/// <b>Delivery contract is intentionally weak.</b> The default
/// <c>LoggingNotifier</c> implementation just writes a structured log
/// line and returns — it does not actually send anything. A future
/// deploy slice can swap the implementation to an email / webhook /
/// provider SDK without changing any caller. No retry, no delivery
/// tracking, no bounce handling in this slice.
/// </para>
///
/// <para>
/// <b>No-raw-token invariant.</b> Implementations MUST NOT log, store,
/// or otherwise retain the raw secret material passed in (the
/// <paramref name="resetToken"/> in this method). The repo is a
/// structured-JSON-stdout environment; a carelessly-logged token
/// would linger in logs far longer than any single request.
/// </para>
/// </summary>
public interface INotifier
{
    /// <summary>
    /// Deliver a password-reset link containing the supplied raw
    /// token to the supplied email. Called only on the known-email
    /// path of the reset-request endpoint; the unknown-email path
    /// never invokes this method (part of the enumeration-resistance
    /// contract).
    /// </summary>
    Task SendPasswordResetAsync(
        string email, string resetToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deliver a dormant-account warning email to the supplied parent.
    /// Called only by the scheduled warn pass in
    /// <c>RetentionPurgeService</c> on parents whose <c>LastLoginAt</c>
    /// is past the configured threshold.
    /// <para>
    /// <b>Returns</b> <c>true</c> on successful delivery,
    /// <c>false</c> on a swallowed send failure. The worker uses this
    /// bool to decide whether to stamp <c>DormancyWarnedAt</c> and
    /// write the audit row — a <c>false</c> return leaves both
    /// untouched so the next tick retries. This is a deliberate
    /// departure from <see cref="SendPasswordResetAsync"/>, which
    /// returns <c>Task</c> because its HTTP-handler caller must not
    /// break on an SMTP failure (anti-enumeration 202 contract). The
    /// worker-consumer here owns retry semantics, so the delivery
    /// outcome must be observable.
    /// </para>
    /// <para>
    /// <paramref name="deleteAtUtc"/> is nullable because the current
    /// slice ships warn-only — the delete action is a separate
    /// future slice. The parameter exists today so the method's
    /// shape is stable for that slice; implementations may omit or
    /// parameterise the delete-date reference in the outgoing copy
    /// when it is null.
    /// </para>
    /// </summary>
    Task<bool> SendDormancyWarningAsync(
        string email, DateTime? deleteAtUtc, CancellationToken cancellationToken = default);
}

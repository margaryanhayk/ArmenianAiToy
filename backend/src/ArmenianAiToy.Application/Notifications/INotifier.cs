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

    /// <summary>
    /// Deliver a single-use email-verification link to the supplied
    /// address. Called by <c>ParentService.RegisterAsync</c> on the
    /// new-email path (not the collision path) and by
    /// <c>RequestEmailVerificationAsync</c> on the known-unverified
    /// path (not the known-verified or unknown paths).
    /// <para>
    /// Returns <c>Task</c> (not <c>Task&lt;bool&gt;</c>) because the
    /// consumer is an HTTP-synchronous handler, not a worker — the
    /// response shape must stay anti-enum-compliant regardless of
    /// delivery outcome. Failed sends must be swallowed into a
    /// structured log line by the implementation, not propagated.
    /// Same shape as <see cref="SendPasswordResetAsync"/>.
    /// </para>
    /// <para>
    /// <b>No-raw-token invariant.</b> Implementations MUST NOT log,
    /// store, or otherwise retain the raw <paramref name="verificationToken"/>.
    /// The token travels exactly once from this method to the parent's
    /// inbox.
    /// </para>
    /// </summary>
    Task SendEmailVerificationAsync(
        string email, string verificationToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deliver a dormant-device warning email to one of the device's
    /// verified linked parents. Called by the scheduled
    /// <c>WarnDormantDevicesAsync</c> pass once per verified linked
    /// parent of a dormant device (multi-parent fan-out).
    /// <para>
    /// <b>Returns</b> <c>true</c> on successful delivery,
    /// <c>false</c> on a swallowed send failure. The worker
    /// aggregates per-recipient results: a device is stamped as
    /// warned (and audited) iff at least one of the per-recipient
    /// calls returned <c>true</c>; if every call returned
    /// <c>false</c>, the device is left unstamped so the next tick
    /// retries. This mirrors <see cref="SendDormancyWarningAsync"/>'s
    /// worker-consumer contract.
    /// </para>
    /// <para>
    /// <paramref name="deleteAtUtc"/> is reserved-but-null in the
    /// warn-only first slice. Passed through to keep the method
    /// signature stable for any future destructive device-action
    /// slice — exactly the same posture
    /// <see cref="SendDormancyWarningAsync"/> took with its own
    /// <c>deleteAtUtc</c> parameter before the parent-anonymize
    /// slice activated it. Implementations MUST NOT include
    /// destructive-date copy in the body when this parameter is
    /// null.
    /// </para>
    /// <para>
    /// <b>No-PII-in-logs invariant.</b> Implementations MUST NOT
    /// retain the recipient email beyond the structured log line
    /// the existing notifier impls already use, and MUST NOT log
    /// the device name in a way that leaks beyond ops visibility.
    /// </para>
    /// </summary>
    Task<bool> SendDormantDeviceWarningAsync(
        string parentEmail,
        string deviceName,
        DateTime lastSeenAtUtc,
        DateTime? deleteAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tell a parent that somebody else has just paired a toy they already
    /// hold. Called by <c>ParentService.ClaimDeviceAsync</c>, once per
    /// already-linked parent, when a NEW link is added (never on a re-claim
    /// of a toy the same parent already holds).
    /// <para>
    /// This exists because the pairing code lives on the toy for its whole
    /// life: anyone who can hold the toy can join it. That is the intended
    /// behaviour for the second parent in a household, but it means a parent
    /// must be able to find out it happened. Being told is what makes the
    /// seat limit something they can act on.
    /// </para>
    /// <para>
    /// Returns <c>Task</c>, not <c>Task&lt;bool&gt;</c>: the caller is an
    /// HTTP handler completing a successful pairing, and a mail failure must
    /// never turn that into an error for the parent who just scanned. Failed
    /// sends are swallowed into a structured log by the implementation.
    /// </para>
    /// <para>
    /// <b>No cross-parent PII.</b> The joining parent's email address is NOT
    /// a parameter and must never appear in the message — the recipient is
    /// told that a second parent joined and which toy, nothing about who.
    /// </para>
    /// </summary>
    Task SendToyJoinedByAnotherParentAsync(
        string parentEmail,
        string deviceName,
        CancellationToken cancellationToken = default);
}

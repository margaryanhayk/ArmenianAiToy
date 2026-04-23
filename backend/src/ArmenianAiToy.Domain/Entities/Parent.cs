namespace ArmenianAiToy.Domain.Entities;

public class Parent
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }

    /// <summary>
    /// Timestamp at which the parent explicitly accepted the terms of
    /// service during registration. Null means consent was never captured —
    /// which should not occur for new rows (enforced by the registration
    /// flow) but remains nullable to accommodate any row created before C1
    /// landed. Paired with <see cref="TermsVersion"/> so we know WHICH
    /// version of the terms the acceptance applies to.
    /// </summary>
    public DateTime? TermsAcceptedAt { get; set; }

    /// <summary>
    /// Version of the terms the parent accepted at registration time
    /// (e.g. "1.0"). Immutable after write — a future bump in the terms
    /// version would require a separate re-acknowledgement flow.
    /// </summary>
    public string? TermsVersion { get; set; }

    /// <summary>
    /// Timestamp of the parent's most recent successful login. Null for
    /// parents who have never logged in (including every row migrated in
    /// before this column existed). Canonical parent-activity signal for
    /// a future dormancy policy — see CLAUDE.md § Parent activity signal.
    /// <para>
    /// Stamped in <c>ParentService.LoginAsync</c> on the successful-auth
    /// branch only. Failed logins (wrong password, unknown email) and
    /// every non-login endpoint leave this field untouched — the signal
    /// is "last successful credentials-plus-JWT exchange," not "last
    /// authenticated request." Register, password change, and reset
    /// completion deliberately do NOT stamp this column.
    /// </para>
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Timestamp of the most recent dormant-account warning email
    /// dispatched to this parent by the scheduled warn pass. Null for
    /// parents who have never been warned (the shipped default for
    /// every row). Stamped only on successful notifier delivery —
    /// a failed or swallowed send leaves this field untouched so the
    /// next worker tick retries. See CLAUDE.md § Retention for the
    /// warn-only eligibility rules and refire interval semantics.
    /// <para>
    /// Paired with <see cref="LastLoginAt"/> on the eligibility query:
    /// a successful login refreshes <c>LastLoginAt</c> and naturally
    /// removes the parent from the dormant set on the next tick, so
    /// <c>DormancyWarnedAt</c> becoming stale is harmless — no
    /// explicit "clear warning" side effect is needed.
    /// </para>
    /// </summary>
    public DateTime? DormancyWarnedAt { get; set; }

    /// <summary>
    /// Timestamp at which this parent was anonymized by the scheduled
    /// destructive dormant-parent pass. Null for active accounts (the
    /// shipped default). When non-null, the <c>Email</c> and
    /// <c>PasswordHash</c> columns are scrubbed to empty strings by
    /// construction, and <c>LastLoginAt</c> + <c>DormancyWarnedAt</c>
    /// are null. Anonymize is irreversible at the application level:
    /// the row is kept so audit-event FKs and structural references
    /// remain intact, but no credential material survives. See
    /// CLAUDE.md § Retention and § Parent anonymize for the policy
    /// contract. Login naturally fails for anonymized parents
    /// because BCrypt verification against an empty-string hash
    /// cannot succeed — no separate "disabled" flag is required.
    /// </summary>
    public DateTime? AnonymizedAt { get; set; }

    /// <summary>
    /// Timestamp at which the parent completed the email verification
    /// flow via <c>POST /api/parents/verify</c>. Null means not yet
    /// verified — includes every row migrated in before this column
    /// existed (R-null retroactive handling). See CLAUDE.md §
    /// Email verification for the tracking-only tier contract.
    /// <para>
    /// In the shipped T1 tier this field gates exactly one thing: the
    /// dormant-parent warn pass skips parents with
    /// <c>EmailVerifiedAt == null</c>. Login, forgot-password,
    /// password change, account delete, export, and every other
    /// parent endpoint are unaffected.
    /// </para>
    /// </summary>
    public DateTime? EmailVerifiedAt { get; set; }

    /// <summary>
    /// Google account identifier (<c>sub</c> claim from a validated
    /// Google ID token). Null means this parent has never signed in
    /// with Google — every row created before the Google sign-in slice
    /// carries null. A filtered unique index enforces one Parent row
    /// per Google <c>sub</c>; null entries are excluded from the
    /// uniqueness constraint so password-only parents coexist freely.
    /// <para>
    /// Once stamped, <see cref="Email"/> remains the Parent's primary
    /// identifier for UI / password flows — Google sign-in is an
    /// additive authentication method, not a replacement.
    /// </para>
    /// </summary>
    public string? GoogleSubject { get; set; }

    public ICollection<ParentDevice> ParentDevices { get; set; } = new List<ParentDevice>();
}

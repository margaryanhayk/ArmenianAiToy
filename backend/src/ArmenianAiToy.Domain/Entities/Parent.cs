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

    public ICollection<ParentDevice> ParentDevices { get; set; } = new List<ParentDevice>();
}

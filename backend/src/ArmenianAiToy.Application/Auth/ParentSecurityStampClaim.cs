namespace ArmenianAiToy.Application.Auth;

/// <summary>
/// N10 — the per-parent security-stamp claim and the pure decision the
/// JWT validator makes with it. Lives in Application (beside
/// <see cref="JwtKeys"/>) so the issue side (<c>ParentService.GenerateJwt</c>)
/// and the validation side (<c>Api/Security/ParentTokenValidation</c>)
/// share one spelling of the claim name and one rule.
///
/// <para>
/// Contract: a parent JWT carries the parent's <c>Parent.SecurityStamp</c>
/// at issue time as the <c>sst</c> claim. On every authenticated request
/// the validator loads the row's CURRENT stamp and rejects the token when
/// the two differ — which is exactly what a password change, a
/// password-reset completion or a dormancy anonymization makes happen by
/// rotating the row's stamp. An ordinary login does not rotate it, so
/// logging in on a second device does not sign the first one out.
/// </para>
/// </summary>
public static class ParentSecurityStampClaim
{
    /// <summary>
    /// JWT claim name. Short on purpose — the token rides in every
    /// request's Authorization header. Not one of the registered JWT
    /// claim names and not remapped by <c>JwtSecurityTokenHandler</c>'s
    /// inbound claim-type map, so it reads back verbatim.
    /// </summary>
    public const string Name = "sst";

    /// <summary>Config key of the rollback switch (default true).</summary>
    public const string RequireConfigKey = "Jwt:RequireSecurityStamp";

    public enum Decision
    {
        /// <summary>Token may proceed.</summary>
        Accept,
        /// <summary>No live Parent row (hard-deleted or anonymized).</summary>
        AccountGone,
        /// <summary>Token carries no stamp claim and the switch requires one.</summary>
        StampMissing,
        /// <summary>Token's stamp differs from the row's current stamp.</summary>
        StampMismatch
    }

    /// <summary>
    /// Pure decision. <paramref name="currentStamp"/> is the row's stamp
    /// as loaded by the validator, or <c>null</c> when no live row exists.
    /// <paramref name="claimStamp"/> is the token's claim, or <c>null</c>
    /// when absent; an empty claim counts as absent.
    /// </summary>
    /// <remarks>
    /// Order matters: a gone account is rejected before the stamp is even
    /// looked at (same answer with the switch on or off). With
    /// <paramref name="requireStamp"/> false — the rollback switch — a
    /// token WITHOUT the claim passes, but a token WITH a wrong claim is
    /// still rejected: the switch exists to tolerate pre-N10 tokens, not
    /// to disable rotation for tokens that already carry a stamp.
    /// </remarks>
    public static Decision Decide(string? claimStamp, string? currentStamp, bool requireStamp)
    {
        if (currentStamp is null)
            return Decision.AccountGone;

        if (string.IsNullOrEmpty(claimStamp))
            return requireStamp ? Decision.StampMissing : Decision.Accept;

        // Ordinal — stamps are opaque, no culture/case folding. An empty
        // row stamp (should not exist after the migration's backfill)
        // can never match a non-empty claim, so it falls through to
        // mismatch rather than accepting a token by accident.
        return string.Equals(claimStamp, currentStamp, StringComparison.Ordinal)
            ? Decision.Accept
            : Decision.StampMismatch;
    }

    /// <summary>
    /// Parses the rollback switch. Only the literal (case-insensitive)
    /// <c>false</c> turns it off; missing, empty or anything else keeps
    /// the shipped default of <c>true</c>, so a typo cannot silently
    /// disable the check.
    /// </summary>
    public static bool ParseRequire(string? raw)
        => !string.Equals(raw?.Trim(), "false", StringComparison.OrdinalIgnoreCase);
}

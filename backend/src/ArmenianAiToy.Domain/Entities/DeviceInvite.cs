namespace ArmenianAiToy.Domain.Entities;

/// <summary>
/// A short-lived, single-use invitation letting a SECOND parent link an
/// existing toy by typing a short code, instead of needing the toy's 36-character
/// id plus the claim code printed on its box.
///
/// <para>
/// <b>This is not the claim code and must never replace it.</b>
/// <c>Device.ClaimCodeHash</c> backs the QR printed on the toy, which is
/// deliberately never consumed so it keeps working for the toy's whole life
/// (see <c>ParentService.ClaimDeviceAsync</c>). Re-minting it to produce
/// something shareable would kill that printed code — the same trap that once
/// made unlinking a toy a one-way door. An invite is a separate, expiring
/// credential that lives alongside it and leaves the toy untouched.
/// </para>
///
/// <para>
/// <b>Selector + secret, not a plain hash.</b> The code is split: the first
/// characters are the <see cref="Selector"/>, stored in the clear and indexed,
/// and the rest is a secret stored only as <see cref="SecretHash"/> (PBKDF2 via
/// <c>DeviceApiKeyHasher</c>, the same hasher used for device keys and claim
/// codes). The selector is what makes redemption an O(1) lookup with no device
/// id — the point of the feature is that the other parent needs ONLY the code.
/// </para>
///
/// <para>
/// The unsalted SHA-256 used for password-reset tokens is deliberately NOT used
/// here. That is safe there precisely because the token is a 32-byte CSPRNG
/// value; a code short enough for a parent to type is a few dozen bits, and an
/// unsalted hash of it is brute-forcible by anyone holding a database dump.
/// </para>
///
/// <para>
/// Single-use is enforced by <see cref="ConsumedAt"/>. Expiry is authoritative
/// and re-checked on every redemption. Neither of them is what enforces the
/// two-parent limit: that is re-counted inside the redemption transaction,
/// because an invite issued while a seat was free can be redeemed after that
/// seat has been taken by someone else.
/// </para>
///
/// <para>
/// <see cref="DeviceId"/> is a real FK with cascade-on-delete — an invite must
/// not outlive the toy it lets someone into. (Contrast <c>AuditEvent</c>, which
/// is FK-less because audit rows must outlive their subjects.)
/// </para>
/// </summary>
public class DeviceInvite
{
    public Guid Id { get; set; }

    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;

    /// <summary>
    /// The parent who issued it. Deliberately NOT a foreign key: an invite is
    /// evidence of an action and should not vanish, nor block a parent's own
    /// account deletion, because of who created it.
    /// </summary>
    public Guid CreatedByParentId { get; set; }

    /// <summary>
    /// Public half of the code, stored in the clear and unique so redemption
    /// can find the row without knowing the device.
    /// </summary>
    public string Selector { get; set; } = string.Empty;

    /// <summary>PBKDF2 hash of the secret half. Never the secret itself.</summary>
    public string SecretHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>Set at successful redemption. Non-null =&gt; dead, whatever the expiry says.</summary>
    public DateTime? ConsumedAt { get; set; }

    /// <summary>Who redeemed it. Null until then.</summary>
    public Guid? ConsumedByParentId { get; set; }
}

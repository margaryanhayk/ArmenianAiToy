namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Config-bound description of the CURRENT firmware release the backend will
/// offer to devices, from section <c>FirmwareUpdate</c>. This slice models
/// "the latest release" as static config (one release at a time); a future
/// slice can replace it with a per-cohort release table without changing the
/// firmware-facing contract.
/// <para>
/// Ships <see cref="Enabled"/> = <c>false</c> so the manifest endpoint returns
/// "no update available" until an operator configures a real release — the
/// device-facing contract exists and is testable, but nothing is offered by
/// default.
/// </para>
/// </summary>
public sealed class FirmwareUpdateOptions
{
    /// <summary>Master switch. When false the manifest always says no-update.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>The latest firmware version (MAJOR.MINOR.PATCH). A device
    /// strictly older than this is offered the update.</summary>
    public string LatestVersion { get; set; } = string.Empty;

    /// <summary>Informational floor: the minimum version that may jump directly
    /// to <see cref="LatestVersion"/>. Surfaced to the device; not a server-side
    /// offer gate in this slice.</summary>
    public string MinVersion { get; set; } = string.Empty;

    /// <summary>When set, the release is only offered to devices reporting this
    /// exact board model — a hardware-specific image never lands on the wrong board.</summary>
    public string BoardModel { get; set; } = string.Empty;

    /// <summary>HTTPS URL of the firmware .bin.</summary>
    public string Url { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>Lowercase hex SHA-256 of the .bin; the device verifies this
    /// before applying.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>HMAC key used to sign the manifest. Empty → the signature field
    /// is returned as an empty placeholder (contract present, signing not yet
    /// configured). This signs the MANIFEST, not the image; image signing
    /// (Secure Boot) is a separate, later, deliberate step.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Manifest freshness window in seconds (the device rejects a stale
    /// manifest). Floor-clamped to 1 at build time.</summary>
    public int TtlSeconds { get; set; } = 300;
}

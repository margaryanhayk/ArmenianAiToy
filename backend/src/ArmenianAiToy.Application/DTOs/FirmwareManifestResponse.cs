namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Response for <c>GET /api/devices/firmware-manifest</c>. When
/// <see cref="UpdateAvailable"/> is false all other fields are null. When true,
/// the device downloads <see cref="Url"/> over HTTPS, verifies
/// <see cref="Sha256"/> (and later a real image signature), checks
/// <see cref="Signature"/> over the manifest, and refuses the manifest after
/// <see cref="ExpiresAt"/>.
/// </summary>
public sealed record FirmwareManifestResponse(
    bool UpdateAvailable,
    string? Version = null,
    string? BoardModel = null,
    string? MinVersion = null,
    string? Url = null,
    long? SizeBytes = null,
    string? Sha256 = null,
    string? Signature = null,
    DateTime? ExpiresAt = null)
{
    /// <summary>The "nothing to do" response — a lone
    /// <c>{ "updateAvailable": false }</c>.</summary>
    public static FirmwareManifestResponse NoUpdate() => new(UpdateAvailable: false);
}

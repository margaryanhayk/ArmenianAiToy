using System.Security.Cryptography;
using System.Text;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;

namespace ArmenianAiToy.Application.Services;

/// <inheritdoc />
public sealed class FirmwareManifestService : IFirmwareManifestService
{
    private readonly FirmwareUpdateOptions _options;

    public FirmwareManifestService(FirmwareUpdateOptions options) => _options = options;

    public FirmwareManifestResponse Build(
        string? deviceFirmwareVersion, string? deviceBoardModel, DateTime nowUtc)
    {
        // Disabled or not configured → nothing offered.
        if (!_options.Enabled
            || string.IsNullOrWhiteSpace(_options.LatestVersion)
            || string.IsNullOrWhiteSpace(_options.Url))
        {
            return FirmwareManifestResponse.NoUpdate();
        }

        // Board gate: a board-specific release is only offered to that board.
        if (!string.IsNullOrWhiteSpace(_options.BoardModel)
            && !string.Equals(_options.BoardModel, deviceBoardModel, StringComparison.Ordinal))
        {
            return FirmwareManifestResponse.NoUpdate();
        }

        // Offer only when the device is strictly OLDER than the latest release.
        if (FirmwareVersionComparer.Compare(deviceFirmwareVersion, _options.LatestVersion) >= 0)
        {
            return FirmwareManifestResponse.NoUpdate();
        }

        var expiresAt = nowUtc.AddSeconds(Math.Max(1, _options.TtlSeconds));
        return new FirmwareManifestResponse(
            UpdateAvailable: true,
            Version: _options.LatestVersion,
            BoardModel: string.IsNullOrWhiteSpace(_options.BoardModel) ? null : _options.BoardModel,
            MinVersion: string.IsNullOrWhiteSpace(_options.MinVersion) ? null : _options.MinVersion,
            Url: _options.Url,
            SizeBytes: _options.SizeBytes,
            Sha256: _options.Sha256,
            Signature: Sign(_options.LatestVersion, _options.Url, _options.Sha256, _options.SizeBytes, expiresAt),
            ExpiresAt: expiresAt);
    }

    // HMAC-SHA256 over the manifest's load-bearing fields, so the device can
    // reject a tampered manifest. Empty key → an empty placeholder (contract
    // present, signing key not yet provisioned). NOTE: this signs the MANIFEST,
    // not the firmware image — image signing (Secure Boot) is a separate step.
    //
    // CANONICAL-STRING CONTRACT (device must match byte-for-byte):
    //   version \n url \n sha256 \n sizeBytes \n expiresAtWireString
    // expiresAt is signed in its JSON WIRE FORM (System.Text.Json rendering,
    // e.g. "2026-07-03T12:00:00.123Z" — fractional-second digits trimmed),
    // NOT the "O" round-trip format, because the device can only rebuild the
    // canonical string from the raw JSON field text it received. Pinned by
    // FirmwareManifestServiceTests.Signature_VerifiesAgainstJsonWireForm.
    private string Sign(string version, string url, string sha256, long size, DateTime expiresAt)
    {
        if (string.IsNullOrEmpty(_options.SigningKey))
        {
            return string.Empty;
        }
        // MUST match UtcDateTimeConverter byte-for-byte — see JsonWireFormats.
        // JsonSerializer.Serialize was used here and TRIMS trailing fractional
        // zeros, which the converter does not, so ~1 manifest in 10 was signed
        // over text the device never received and every toy refused it.
        var utc = expiresAt.Kind switch
        {
            DateTimeKind.Utc => expiresAt,
            DateTimeKind.Local => expiresAt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc),
        };
        var expiresAtWire = utc.ToString(Helpers.JsonWireFormats.UtcDateTime,
            System.Globalization.CultureInfo.InvariantCulture);
        var canonical = $"{version}\n{url}\n{sha256}\n{size}\n{expiresAtWire}";
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(_options.SigningKey));
        var mac = h.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }
}

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
    private string Sign(string version, string url, string sha256, long size, DateTime expiresAt)
    {
        if (string.IsNullOrEmpty(_options.SigningKey))
        {
            return string.Empty;
        }
        var canonical = $"{version}\n{url}\n{sha256}\n{size}\n{expiresAt:O}";
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(_options.SigningKey));
        var mac = h.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }
}

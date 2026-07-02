using ArmenianAiToy.Application.DTOs;

namespace ArmenianAiToy.Application.Interfaces;

/// <summary>
/// Builds the firmware-update manifest for a device from the configured
/// current release. Pure decision + signing — no DB, no IO.
/// </summary>
public interface IFirmwareManifestService
{
    /// <summary>Decide whether the device (reporting
    /// <paramref name="deviceFirmwareVersion"/> / <paramref name="deviceBoardModel"/>)
    /// should be offered an update, and return the signed manifest or a
    /// no-update response.</summary>
    FirmwareManifestResponse Build(
        string? deviceFirmwareVersion, string? deviceBoardModel, DateTime nowUtc);
}

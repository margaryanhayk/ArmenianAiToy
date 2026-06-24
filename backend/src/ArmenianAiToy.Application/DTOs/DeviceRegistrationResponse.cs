namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Response from POST /api/devices/register (the provisioning / factory-mint
/// endpoint, gated by #009). <see cref="ApiKey"/> is the device's backend
/// credential — returned ONCE, burned into the toy's NVS, never shown again.
///
/// <para>Phase A.3 (consumer pairing): a NEW registration also mints a
/// single-use <see cref="ClaimCode"/> (returned once; only its hash is stored)
/// and a ready-to-render <see cref="QrPayload"/> — the exact JSON the toy's QR
/// should encode (<c>{ deviceId, claim }</c>), which the app's claim scanner
/// parses. The factory burns {deviceId, apiKey} to NVS and prints
/// {deviceId, claim} as the QR. Both are <c>null</c> on a re-registration
/// (key rotation, #011), which does NOT re-mint a claim code.</para>
/// </summary>
public record DeviceRegistrationResponse(
    Guid DeviceId,
    string ApiKey,
    string? ClaimCode = null,
    string? QrPayload = null);

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
///
/// <para>Factory pairing (2026-09-11): a NEW registration also mints
/// <see cref="Pop"/> — the toy's per-device BLE provisioning
/// proof-of-possession, replacing the one pairing code every toy used to
/// share. Returned ONCE, additive inside <see cref="QrPayload"/>
/// (<c>{ deviceId, claim, pop }</c>), and burned to the toy's NVS beside the
/// device id and key. <b>Never persisted anywhere, not even hashed</b> — see
/// <c>DeviceService.GeneratePop</c> for why. <c>null</c> on a re-registration,
/// same as <see cref="ClaimCode"/>: a BLE pairing code is not something a
/// re-provision should silently change under an owner who already printed
/// the old one on the box.</para>
/// </summary>
public record DeviceRegistrationResponse(
    Guid DeviceId,
    string ApiKey,
    string? ClaimCode = null,
    string? QrPayload = null,
    string? Pop = null);

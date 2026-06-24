namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Phase A.2 request body for POST /api/parents/devices/claim. The parent's app
/// scans the toy's QR (which carries deviceId + the single-use claim code) and
/// posts both. The claim code is NOT the device's backend key — it only proves
/// physical possession and binds the toy to this parent's account.
/// </summary>
public record DeviceClaimRequest(Guid DeviceId, string ClaimCode);

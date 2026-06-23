namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// #074 request body for PUT /api/parents/devices/{deviceId}/revoke.
/// <c>Revoked=true</c> disables the device's server-side credential (every
/// device-auth path then returns 401 until it re-provisions a fresh key);
/// <c>Revoked=false</c> restores it. Reversible.
/// </summary>
public record DeviceRevocationRequest(bool Revoked);

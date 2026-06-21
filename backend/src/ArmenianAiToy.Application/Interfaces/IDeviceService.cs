using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Domain.Entities;

namespace ArmenianAiToy.Application.Interfaces;

public interface IDeviceService
{
    /// <summary>
    /// Registers a device. A NEW MAC creates the device and returns its id + key.
    /// An EXISTING MAC is refused (returns null) UNLESS
    /// <paramref name="allowReRegister"/> is true — #011: a plain re-registration
    /// must NOT silently rotate an in-field device's credential (that is both a
    /// DoS and a hijack vector). Rotation is a deliberate, explicitly-requested
    /// re-provision only.
    /// </summary>
    Task<DeviceRegistrationResponse?> RegisterDeviceAsync(
        DeviceRegistrationRequest request, bool allowReRegister = false);
    Task<Device?> ValidateDeviceAsync(Guid deviceId, string apiKey);
    Task UpdateLastSeenAsync(Guid deviceId);
    Task<bool> IsDevicePausedAsync(Guid deviceId);
    Task<bool> IsDeviceInBedtimeWindowAsync(Guid deviceId, DateTime nowUtc);
    Task<bool> IsDeviceModeEnabledAsync(Guid deviceId, DetectedMode mode);
    Task<bool> IsModeEnabledForRequestAsync(Guid deviceId, Guid? childId, DetectedMode mode);
}

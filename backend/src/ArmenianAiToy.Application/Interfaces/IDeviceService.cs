using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Domain.Entities;

namespace ArmenianAiToy.Application.Interfaces;

public interface IDeviceService
{
    Task<DeviceRegistrationResponse> RegisterDeviceAsync(DeviceRegistrationRequest request);
    Task<Device?> ValidateDeviceAsync(Guid deviceId, string apiKey);
    Task UpdateLastSeenAsync(Guid deviceId);
    Task<bool> IsDevicePausedAsync(Guid deviceId);
    Task<bool> IsDeviceInBedtimeWindowAsync(Guid deviceId, DateTime nowUtc);
    Task<bool> IsDeviceModeEnabledAsync(Guid deviceId, DetectedMode mode);
}

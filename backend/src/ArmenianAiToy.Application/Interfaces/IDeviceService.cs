using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Domain.Entities;

namespace ArmenianAiToy.Application.Interfaces;

public interface IDeviceService
{
    Task<DeviceRegistrationResponse> RegisterDeviceAsync(DeviceRegistrationRequest request);
    Task<Device?> ValidateDeviceAsync(Guid deviceId, string apiKey);
    Task UpdateLastSeenAsync(Guid deviceId);
}

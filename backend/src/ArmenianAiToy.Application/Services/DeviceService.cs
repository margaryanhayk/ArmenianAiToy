using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ArmenianAiToy.Application.Services;

public class DeviceService : IDeviceService
{
    private readonly DbContext _db;
    private readonly ILogger<DeviceService> _logger;

    public DeviceService(DbContext db, ILogger<DeviceService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<DeviceRegistrationResponse> RegisterDeviceAsync(DeviceRegistrationRequest request)
    {
        // Check if device already registered
        var existing = await _db.Set<Device>()
            .FirstOrDefaultAsync(d => d.MacAddress == request.MacAddress);

        if (existing != null)
        {
            _logger.LogInformation("Device {MacAddress} re-registered", request.MacAddress);
            existing.LastSeenAt = DateTime.UtcNow;
            existing.FirmwareVersion = request.FirmwareVersion;
            await _db.SaveChangesAsync();
            return new DeviceRegistrationResponse(existing.Id, existing.ApiKey);
        }

        var device = new Device
        {
            Id = Guid.NewGuid(),
            MacAddress = request.MacAddress,
            Name = $"Toy-{request.MacAddress[^4..]}",
            ApiKey = $"dtk_{Guid.NewGuid():N}",
            FirmwareVersion = request.FirmwareVersion,
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };

        _db.Set<Device>().Add(device);
        await _db.SaveChangesAsync();

        _logger.LogInformation("New device registered: {DeviceId} ({MacAddress})", device.Id, device.MacAddress);
        return new DeviceRegistrationResponse(device.Id, device.ApiKey);
    }

    public async Task<Device?> ValidateDeviceAsync(Guid deviceId, string apiKey)
    {
        return await _db.Set<Device>()
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.ApiKey == apiKey);
    }

    public async Task UpdateLastSeenAsync(Guid deviceId)
    {
        var device = await _db.Set<Device>().FindAsync(deviceId);
        if (device != null)
        {
            device.LastSeenAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }
}

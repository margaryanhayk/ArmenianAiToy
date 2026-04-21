using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
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

    public async Task<bool> IsDevicePausedAsync(Guid deviceId)
    {
        // Single-field read — avoids materializing the full Device row
        // on the hot chat path. Returns false for an unknown device id so
        // the chat gate's "paused short-circuit" is never triggered by a
        // deleted/renamed device; the downstream DeviceAuthMiddleware
        // already 401s invalid credentials earlier in the pipeline.
        return await _db.Set<Device>()
            .Where(d => d.Id == deviceId)
            .Select(d => d.IsPaused)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> IsDeviceInBedtimeWindowAsync(Guid deviceId, DateTime nowUtc)
    {
        // Projection: only the three bedtime fields, same hot-path
        // discipline as IsDevicePausedAsync. Unknown device id → false
        // (same contract as the pause check).
        var gate = await _db.Set<Device>()
            .Where(d => d.Id == deviceId)
            .Select(d => new { d.BedtimeStart, d.BedtimeEnd, d.TimeZone })
            .FirstOrDefaultAsync();
        if (gate is null)
            return false;
        return BedtimeWindowEvaluator.IsInWindow(
            gate.BedtimeStart, gate.BedtimeEnd, gate.TimeZone, nowUtc, _logger);
    }

    public async Task<bool> IsDeviceModeEnabledAsync(Guid deviceId, DetectedMode mode)
    {
        // Calm always enabled (safety invariant from MODES.md). Any mode
        // outside the four configurable ones (None) is also treated as
        // enabled — the chat gate only calls this with a definitive
        // Story/Game/Riddle/Curiosity, but being permissive on the
        // non-gated branches avoids surprising callers.
        if (mode is not DetectedMode.Story
            and not DetectedMode.Game
            and not DetectedMode.Riddle
            and not DetectedMode.Curiosity)
            return true;

        var flags = await _db.Set<Device>()
            .Where(d => d.Id == deviceId)
            .Select(d => new
            {
                d.StoryEnabled,
                d.GameEnabled,
                d.RiddleEnabled,
                d.CuriosityEnabled
            })
            .FirstOrDefaultAsync();
        // Unknown device → don't block. DeviceAuthMiddleware already 401s
        // invalid credentials upstream; this gate must never be the one to
        // invent a rejection for a missing device.
        if (flags is null)
            return true;
        return mode switch
        {
            DetectedMode.Story => flags.StoryEnabled,
            DetectedMode.Game => flags.GameEnabled,
            DetectedMode.Riddle => flags.RiddleEnabled,
            DetectedMode.Curiosity => flags.CuriosityEnabled,
            _ => true
        };
    }
}

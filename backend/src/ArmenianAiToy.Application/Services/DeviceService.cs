using System.Security.Cryptography;
using System.Text.Json;
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

    public async Task<DeviceRegistrationResponse?> RegisterDeviceAsync(
        DeviceRegistrationRequest request, bool allowReRegister = false)
    {
        // Check if device already registered
        var existing = await _db.Set<Device>()
            .FirstOrDefaultAsync(d => d.MacAddress == request.MacAddress);

        if (existing != null)
        {
            // #011: never SILENTLY rotate an in-field device's credential. A
            // plain re-registration is refused (null -> the controller returns
            // 409) — the device already holds its key, and LastSeen is kept
            // current by DeviceAuthMiddleware on authenticated requests. Rotating
            // (or returning the legacy plaintext) is a deliberate re-provision,
            // allowed only when the caller explicitly forces it.
            if (!allowReRegister)
            {
                _logger.LogInformation(
                    "Device {MacAddress} re-registration refused (no force; credential unchanged)",
                    request.MacAddress);
                return null;
            }

            existing.LastSeenAt = DateTime.UtcNow;
            existing.FirmwareVersion = request.FirmwareVersion;

            // Three re-registration arms, in priority order:
            //   1. Legacy row (ApiKey != null AND ApiKeyHash == null) —
            //      pre-hash-at-rest slice. Return the existing plaintext
            //      verbatim AND lazy-upgrade the row to hash so the next
            //      validation does not need the plaintext column. Keeps
            //      the old idempotency contract for any device that
            //      registered before this slice.
            //   2. Hashed row (ApiKeyHash != null) — cannot recover the
            //      original plaintext from the hash, so rotate to a fresh
            //      key, persist a new hash, and return the new key. This
            //      is the documented behavior change for the hash-at-rest
            //      slice.
            //   3. Corrupt row (both null) — should not happen in practice;
            //      treat as a rotate.
            string returnedKey;
            if (existing.ApiKey is { Length: > 0 } legacyPlaintext
                && string.IsNullOrEmpty(existing.ApiKeyHash))
            {
                returnedKey = legacyPlaintext;
                existing.ApiKeyHash = DeviceApiKeyHasher.Hash(legacyPlaintext);
                existing.ApiKey = null;
                await _db.SaveChangesAsync();
                _logger.LogInformation(
                    "Device {MacAddress} re-registered (legacy plaintext upgraded to hash)",
                    request.MacAddress);
            }
            else
            {
                returnedKey = $"dtk_{Guid.NewGuid():N}";
                existing.ApiKeyHash = DeviceApiKeyHasher.Hash(returnedKey);
                existing.ApiKey = null;
                await _db.SaveChangesAsync();
                _logger.LogInformation(
                    "Device {MacAddress} re-registered (API key rotated)",
                    request.MacAddress);
            }
            return new DeviceRegistrationResponse(existing.Id, returnedKey);
        }

        var plaintext = $"dtk_{Guid.NewGuid():N}";
        // Phase A.3 — mint a single-use CLAIM CODE alongside the device key.
        // Crypto-strong (128-bit), returned ONCE; only its hash is stored. The
        // factory prints it (in the QR) so a parent can claim the toy; the
        // device key (above) is never printed. The two secrets are independent.
        var claimCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var device = new Device
        {
            Id = Guid.NewGuid(),
            MacAddress = request.MacAddress,
            Name = $"Toy-{request.MacAddress[^4..]}",
            ApiKey = null,
            ApiKeyHash = DeviceApiKeyHasher.Hash(plaintext),
            ClaimCodeHash = DeviceApiKeyHasher.Hash(claimCode),
            FirmwareVersion = request.FirmwareVersion,
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };

        _db.Set<Device>().Add(device);
        await _db.SaveChangesAsync();

        _logger.LogInformation("New device registered: {DeviceId} ({MacAddress})", device.Id, device.MacAddress);
        // QR payload = the exact JSON the toy's QR should encode; the app's
        // claim scanner parses { deviceId, claim }. Device key is NOT included.
        var qrPayload = JsonSerializer.Serialize(new { deviceId = device.Id, claim = claimCode });
        return new DeviceRegistrationResponse(device.Id, plaintext, claimCode, qrPayload);
    }

    public async Task<Device?> ValidateDeviceAsync(Guid deviceId, string apiKey)
    {
        // SQL filter by Id only — the hash compare cannot run in SQL because
        // PBKDF2 + per-row salt require server-side computation. The legacy
        // plaintext-equality branch is handled in-process too, so an attacker
        // probing with the wrong key cannot see a timing difference between
        // "device id exists" and "device id missing" beyond the FindAsync
        // cost itself (which is the same for either branch).
        var device = await _db.Set<Device>()
            .FirstOrDefaultAsync(d => d.Id == deviceId);
        if (device is null) return null;

        // #074 — server-side revocation kill-switch. A revoked device fails
        // auth BEFORE the key compare (no hash work) and returns the SAME
        // uniform null (-> 401 "Invalid device credentials") as a wrong key,
        // so revocation is not an enumeration oracle. This single chokepoint
        // covers every device-auth path; the device is dead until it
        // re-provisions a fresh key (registration) or a parent restores it.
        if (device.IsRevoked) return null;

        // Preferred path: hashed credential.
        if (DeviceApiKeyHasher.IsHash(device.ApiKeyHash))
        {
            return DeviceApiKeyHasher.Verify(apiKey, device.ApiKeyHash)
                ? device
                : null;
        }

        // Legacy plaintext fallback. Constant-time compare so a wrong key
        // does not leak a prefix-match timing signal. On success, lazy-
        // upgrade: persist the hash and clear the plaintext column so the
        // next request goes through the preferred path.
        if (!string.IsNullOrEmpty(device.ApiKey)
            && DeviceApiKeyHasher.ConstantTimeEquals(apiKey, device.ApiKey))
        {
            device.ApiKeyHash = DeviceApiKeyHasher.Hash(apiKey);
            device.ApiKey = null;
            try
            {
                await _db.SaveChangesAsync();
                _logger.LogInformation(
                    "Device {DeviceId} legacy plaintext key upgraded to hash on successful auth",
                    device.Id);
            }
            catch (Exception ex)
            {
                // Persisting the upgrade is best-effort; the auth itself
                // already succeeded, so we never fail the request on a
                // save error. The next successful auth will retry the
                // upgrade.
                _logger.LogWarning(ex,
                    "Device {DeviceId} legacy-to-hash upgrade save failed (will retry)",
                    device.Id);
            }
            return device;
        }

        return null;
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

    public async Task<bool> IsModeEnabledForRequestAsync(
        Guid deviceId, Guid? childId, DetectedMode mode)
    {
        // Calm / None / ambiguous are never gated — same safety invariant
        // as the device-level resolver. Mirrors IsDeviceModeEnabledAsync's
        // first branch.
        if (mode is not DetectedMode.Story
            and not DetectedMode.Game
            and not DetectedMode.Riddle
            and not DetectedMode.Curiosity)
            return true;

        // No ChildId on the request → device-level path, unchanged.
        if (childId is null)
            return await IsDeviceModeEnabledAsync(deviceId, mode);

        // Look up the child's override, filtered by BOTH childId AND deviceId
        // so a request carrying a ChildId that belongs to another device
        // cannot influence this device's gate. Projection pulls only the
        // four nullable override columns — same hot-path discipline as
        // IsDeviceModeEnabledAsync.
        var overrides = await _db.Set<Child>()
            .Where(c => c.Id == childId.Value && c.DeviceId == deviceId)
            .Select(c => new
            {
                c.StoryEnabled,
                c.GameEnabled,
                c.RiddleEnabled,
                c.CuriosityEnabled
            })
            .FirstOrDefaultAsync();

        // Child not found on this device — cross-device probe or unknown
        // id; fall back to device-level flag. Never apply an override
        // belonging to another device.
        if (overrides is null)
            return await IsDeviceModeEnabledAsync(deviceId, mode);

        bool? childOverride = mode switch
        {
            DetectedMode.Story => overrides.StoryEnabled,
            DetectedMode.Game => overrides.GameEnabled,
            DetectedMode.Riddle => overrides.RiddleEnabled,
            DetectedMode.Curiosity => overrides.CuriosityEnabled,
            _ => null
        };

        // Non-null child override wins over device flag in both directions.
        // Null override means inherit — fall through to device flag.
        return childOverride
            ?? await IsDeviceModeEnabledAsync(deviceId, mode);
    }
}

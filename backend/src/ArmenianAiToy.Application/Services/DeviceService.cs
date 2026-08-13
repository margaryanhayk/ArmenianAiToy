using System.Security.Cryptography;
using System.Text.Json;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
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
            // Defense-in-depth: the controller enforces length >= 4, but
            // never index past the start here either (a short MAC would
            // throw ArgumentOutOfRangeException -> 500).
            Name = $"Toy-{(request.MacAddress.Length >= 4 ? request.MacAddress[^4..] : request.MacAddress)}",
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

    public async Task<Device?> GetDeviceAsync(Guid deviceId)
        => await _db.Set<Device>().FirstOrDefaultAsync(d => d.Id == deviceId);

    public async Task UpdateFirmwareReportAsync(
        Guid deviceId, DeviceHeartbeatRequest report, DateTime nowUtc)
    {
        var device = await _db.Set<Device>().FindAsync(deviceId);
        if (device is null)
        {
            return;
        }
        // Only overwrite fields the device actually sent; a partial report
        // never blanks a previously-reported value.
        if (report.FirmwareVersion is not null) device.FirmwareVersion = report.FirmwareVersion;
        if (report.FirmwareBuild is not null) device.FirmwareBuild = report.FirmwareBuild;
        if (report.BoardModel is not null) device.BoardModel = report.BoardModel;
        if (report.PartitionName is not null) device.PartitionName = report.PartitionName;
        if (report.LastOtaStatus is not null) device.LastOtaStatus = report.LastOtaStatus;
        if (report.SdCardOk is not null) device.SdCardOk = report.SdCardOk;
        // Content report — same partial-report rule. The toy sends this block
        // only when its card changed, so most heartbeats leave it all null and
        // the previously-reported snapshot must survive untouched.
        if (report.ContentIndexSchema is not null) device.ContentIndexSchema = report.ContentIndexSchema;
        if (report.ContentStories is not null) device.ContentStories = report.ContentStories;
        if (report.ContentGameClips is not null) device.ContentGameClips = report.ContentGameClips;
        if (report.ContentVoiceClips is not null) device.ContentVoiceClips = report.ContentVoiceClips;
        if (report.ContentMusicTracks is not null) device.ContentMusicTracks = report.ContentMusicTracks;
        if (report.HasAnyContentField) device.ContentReportedAt = nowUtc;
        device.FirmwareReportedAt = nowUtc;
        await _db.SaveChangesAsync();
    }

    public async Task<int> ReportStoryPlaysAsync(
        Guid deviceId, StoryPlayReportRequest request, DateTime nowUtc)
    {
        var events = request?.Events;
        if (events is null || events.Count == 0)
        {
            return 0;
        }

        // Bounded relative age: the toy has no wall clock and reports a
        // best-effort "seconds ago" from its boot timer. Anything past this
        // window is treated as unknown (approximate upload-time stamp) rather
        // than producing a nonsense historical timestamp.
        var maxSecondsAgo = (long)TimeSpan.FromDays(90).TotalSeconds;

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var accepted = 0;
        foreach (var ev in events.Take(StoryPlayReportRequest.MaxEvents))
        {
            var key = ev?.Key?.Trim();
            var storyId = ev?.StoryId?.Trim();
            // Malformed events are skipped, not rejected wholesale — one bad
            // entry must not make the device re-upload its good siblings
            // forever. Length caps bound storage on a device-writable field.
            if (ev is null
                || string.IsNullOrEmpty(key) || key.Length > 64
                || string.IsNullOrEmpty(storyId) || storyId.Length > 64)
            {
                continue;
            }
            if (!seenKeys.Add(key))
            {
                continue; // in-batch duplicate — keep the first
            }

            // Idempotency: the upload is at-least-once, so a key we already
            // hold is a silent no-op (the unique DB index is the backstop for
            // the concurrent-upload race below).
            var exists = await _db.Set<StoryPlay>()
                .AnyAsync(p => p.DeviceId == deviceId && p.ClientEventKey == key);
            if (exists)
            {
                continue;
            }

            // Bounded source vocabulary — mirrors the metrics no-free-form
            // discipline. Unknown values collapse to "other" rather than
            // storing arbitrary device-supplied strings.
            var source = (ev.Source ?? string.Empty).Trim().ToLowerInvariant();
            if (source is not ("sd" or "pack" or "stream"))
            {
                source = "other";
            }

            var playedAt = nowUtc;
            var approximate = true;
            if (ev.SecondsAgo is { } ago && ago >= 0 && ago <= maxSecondsAgo)
            {
                playedAt = nowUtc.AddSeconds(-ago);
                approximate = false;
            }

            var row = new StoryPlay
            {
                Id = Guid.NewGuid(),
                DeviceId = deviceId,
                StoryId = storyId,
                Source = source,
                Finished = ev.Finished,
                ClientEventKey = key,
                PlayedAtUtc = playedAt,
                TimeIsApproximate = approximate,
            };
            _db.Set<StoryPlay>().Add(row);
            try
            {
                // Per-event save: a unique-index race (a concurrently retried
                // upload) fails only ITS row, and the 2xx the device gets
                // still covers every event actually persisted. Batches are
                // tiny (firmware queue ~16), so per-row saves are cheap.
                await _db.SaveChangesAsync();
                accepted++;
            }
            catch (DbUpdateException ex)
            {
                _db.Entry(row).State = EntityState.Detached;
                _logger.LogWarning(ex,
                    "Story-play insert skipped for device {DeviceId} key {EventKey} (likely duplicate)",
                    deviceId, key);
            }
        }

        if (accepted > 0)
        {
            _logger.LogInformation(
                "Device {DeviceId} reported {Count} new story play(s)", deviceId, accepted);
        }
        return accepted;
    }

    public async Task AddStoryReflectionAnswerAsync(
        Guid deviceId, string storyId, int questionIndex,
        string answerText, SafetyFlag safetyFlag, DateTime nowUtc)
    {
        // Append-only by contract — one row per listen. Bounded fields: the
        // storyId comes from the validated library lookup upstream, the
        // answer is the STT transcript (same persistence discipline as the
        // conversation record).
        _db.Set<StoryReflectionAnswer>().Add(new StoryReflectionAnswer
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            StoryId = storyId,
            QuestionIndex = questionIndex,
            AnswerText = answerText,
            SafetyFlag = safetyFlag,
            CreatedAtUtc = nowUtc,
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Whether any parent account currently holds this toy.
    /// <para>
    /// A toy with zero linked parents is nobody's: it has been unlinked and
    /// is waiting to be paired again from its QR. It must not keep talking
    /// to a child in that gap — there is no parent who could see, pause or
    /// stop it. Claiming re-links it and it wakes up on the next request,
    /// so this needs no stored flag and nothing for a parent to switch back
    /// on.
    /// </para>
    /// </summary>
    public async Task<bool> HasLinkedParentAsync(Guid deviceId)
    {
        return await _db.Set<ParentDevice>().AnyAsync(pd => pd.DeviceId == deviceId);
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

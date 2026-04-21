using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Telemetry;
using ArmenianAiToy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.Tokens;

namespace ArmenianAiToy.Application.Services;

public class ParentService : IParentService
{
    /// <summary>
    /// Current terms-of-service version recorded on newly registered
    /// Parent rows. Bump this when the parent-facing terms text changes.
    /// A bump should be accompanied by a separate re-acknowledgement flow
    /// for existing parents (not in scope for C1).
    /// </summary>
    public const string CurrentTermsVersion = "1.0";

    private readonly DbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<ParentService> _logger;

    public ParentService(DbContext db, IConfiguration config, ILogger<ParentService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task<Guid> RegisterAsync(string email, string password, bool acceptedTerms)
    {
        if (!acceptedTerms)
            throw new InvalidOperationException("Terms must be accepted to register.");

        var existing = await _db.Set<Parent>().AnyAsync(p => p.Email == email);
        if (existing)
            throw new InvalidOperationException("Email already registered");

        var now = DateTime.UtcNow;
        var parent = new Parent
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            RegisteredAt = now,
            TermsAcceptedAt = now,
            TermsVersion = CurrentTermsVersion
        };

        _db.Set<Parent>().Add(parent);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Parent registered: {Email}, terms version {TermsVersion}",
            email, CurrentTermsVersion);
        return parent.Id;
    }

    public async Task<ParentLoginResponse?> LoginAsync(string email, string password)
    {
        var parent = await _db.Set<Parent>().FirstOrDefaultAsync(p => p.Email == email);
        if (parent == null || !BCrypt.Net.BCrypt.Verify(password, parent.PasswordHash))
            return null;

        var token = GenerateJwt(parent);
        return new ParentLoginResponse(token);
    }

    public async Task<bool> LinkDeviceAsync(Guid parentId, Guid deviceId, string apiKey)
    {
        var device = await _db.Set<Device>()
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.ApiKey == apiKey);

        if (device == null)
            return false;

        var alreadyLinked = await _db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == parentId && pd.DeviceId == deviceId);

        if (alreadyLinked)
            return true;

        _db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId,
            DeviceId = deviceId,
            LinkedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        _logger.LogInformation("Parent {ParentId} linked device {DeviceId}", parentId, deviceId);
        return true;
    }

    public async Task<bool> UnlinkDeviceAsync(Guid parentId, Guid deviceId)
    {
        var link = await _db.Set<ParentDevice>()
            .FirstOrDefaultAsync(pd => pd.ParentId == parentId && pd.DeviceId == deviceId);

        if (link == null)
            return false;

        _db.Set<ParentDevice>().Remove(link);
        await _db.SaveChangesAsync();

        // Orphan-aware cleanup: if this unlink removed the last parent link
        // to the device, the device and its subtree (children, conversations,
        // messages) become unreachable by any parent API. Delete the Device
        // and rely on the existing Cascade FKs to take the subtree with it.
        // Same shape as DeleteAccountAsync's orphan loop, narrowed to a
        // single device.
        var stillLinked = await _db.Set<ParentDevice>()
            .AnyAsync(pd => pd.DeviceId == deviceId);
        if (stillLinked)
        {
            TrackAndAddAudit(AuditEvent.ParentDeviceUnlinked(
                parentId, deviceId, orphanCascaded: false));
            await _db.SaveChangesAsync();
            _logger.LogInformation(
                "Parent {ParentId} unlinked device {DeviceId} (still linked to other parents)",
                parentId, deviceId);
            return true;
        }

        var device = await _db.Set<Device>().FindAsync(deviceId);
        if (device != null)
        {
            _db.Set<Device>().Remove(device);
        }
        // orphanCascaded captures whether the device row was actually removed.
        // In the rare "device already gone" race it stays false even though
        // this was the last ParentDevice link.
        TrackAndAddAudit(AuditEvent.ParentDeviceUnlinked(
            parentId, deviceId, orphanCascaded: device != null));
        await _db.SaveChangesAsync();
        _logger.LogInformation(
            "Parent {ParentId} unlinked last link to device {DeviceId}; device and subtree deleted",
            parentId, deviceId);
        return true;
    }

    public async Task<List<Guid>> GetLinkedDeviceIdsAsync(Guid parentId)
    {
        return await _db.Set<ParentDevice>()
            .Where(pd => pd.ParentId == parentId)
            .Select(pd => pd.DeviceId)
            .ToListAsync();
    }

    public async Task<bool> ChangePasswordAsync(Guid parentId, string currentPassword, string newPassword)
    {
        var parent = await _db.Set<Parent>().FirstOrDefaultAsync(p => p.Id == parentId);
        if (parent == null)
            return false;

        if (!BCrypt.Net.BCrypt.Verify(currentPassword, parent.PasswordHash))
            return false;

        parent.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        TrackAndAddAudit(AuditEvent.ParentPasswordChanged(parentId));
        await _db.SaveChangesAsync();

        _logger.LogInformation("Parent {ParentId} changed password", parentId);
        return true;
    }

    public async Task<bool> DeleteAccountAsync(Guid parentId, string currentPassword)
    {
        // Re-authenticate: the JWT proves the caller is logged in as this
        // parent, but account deletion is destructive enough to warrant a
        // second factor. Same BCrypt.Verify pattern ChangePasswordAsync
        // uses; returns silent false on wrong password or unknown parent
        // so the caller cannot probe.
        var parent = await _db.Set<Parent>().FirstOrDefaultAsync(p => p.Id == parentId);
        if (parent == null)
            return false;

        if (!BCrypt.Net.BCrypt.Verify(currentPassword, parent.PasswordHash))
            return false;

        // Capture the device ids BEFORE the cascade removes the
        // ParentDevice rows — otherwise we lose the information needed to
        // identify orphaned devices below.
        var linkedDeviceIds = await _db.Set<ParentDevice>()
            .Where(pd => pd.ParentId == parentId)
            .Select(pd => pd.DeviceId)
            .ToListAsync();

        // Delete the Parent. FK ParentDevices → Parents is Cascade, so
        // every ParentDevice row for this parent is removed in the same
        // transaction as the Parent row.
        _db.Set<Parent>().Remove(parent);
        await _db.SaveChangesAsync();

        // Orphan-aware device cleanup: for each device this parent had
        // linked, if no ParentDevice rows remain (i.e. this parent was
        // the last owner), the device and its data (children,
        // conversations, messages) are unreachable forever. Delete the
        // device; existing Cascade FKs handle the subtree.
        //
        // Devices still linked to another parent are preserved — the
        // multi-parent-device semantic of the ParentDevice composite key
        // is respected. UnlinkDeviceAsync applies the same orphan-cleanup
        // rule per device; this loop is the bulk equivalent when the
        // whole account goes away at once.
        int orphanedDevicesDeleted = 0;
        foreach (var deviceId in linkedDeviceIds)
        {
            var stillLinked = await _db.Set<ParentDevice>()
                .AnyAsync(pd => pd.DeviceId == deviceId);
            if (stillLinked)
                continue;

            var device = await _db.Set<Device>().FindAsync(deviceId);
            if (device != null)
            {
                _db.Set<Device>().Remove(device);
                orphanedDevicesDeleted++;
            }
        }
        // Audit must be written even when no orphan cleanup was needed, so
        // this SaveChangesAsync runs unconditionally now.
        TrackAndAddAudit(AuditEvent.ParentAccountDeleted(
            parentId, linkedDeviceIds.Count, orphanedDevicesDeleted));
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Parent {ParentId} deleted account ({LinkedDevices} linked devices, {OrphanedDevices} orphaned devices cascaded)",
            parentId, linkedDeviceIds.Count, orphanedDevicesDeleted);
        return true;
    }

    public async Task<bool> DeleteChildAsync(Guid parentId, Guid childId)
    {
        // Ownership: the parent must own the device the child belongs to.
        // Same shape as SetDevicePauseStateAsync — silent false on a miss,
        // no existence leak for children owned by other parents.
        var child = await _db.Set<Child>().FirstOrDefaultAsync(c => c.Id == childId);
        if (child == null)
            return false;

        var ownsDevice = await _db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == parentId && pd.DeviceId == child.DeviceId);
        if (!ownsDevice)
            return false;

        // Service-level cascade for the one non-cascading FK:
        // Conversation.ChildId → Children is NoAction (initial migration), so
        // we must delete the child's conversations before removing the Child
        // row. Messages → Conversations IS Cascade, so Messages go with
        // their Conversations at the DB level in the same SaveChanges.
        //
        // Kept service-level (rather than a schema FK change to Cascade) so
        // the cascade is auditable in the log line below and easy to adjust
        // if product ever prefers detach-over-delete semantics.
        var conversations = await _db.Set<Conversation>()
            .Where(c => c.ChildId == childId)
            .ToListAsync();
        if (conversations.Count > 0)
            _db.Set<Conversation>().RemoveRange(conversations);

        _db.Set<Child>().Remove(child);
        TrackAndAddAudit(AuditEvent.ParentChildDeleted(parentId, childId, conversations.Count));
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Parent {ParentId} deleted child {ChildId} on device {DeviceId} ({ConversationCount} conversations cascaded)",
            parentId, childId, child.DeviceId, conversations.Count);
        return true;
    }

    public async Task<bool> SetDevicePauseStateAsync(Guid parentId, Guid deviceId, bool paused)
    {
        // Ownership check: the parent must have a ParentDevice link to this
        // device id. Silent false on missing link — matches UnlinkDeviceAsync's
        // no-existence-leak shape. Same pattern used by the
        // ConversationController read endpoints.
        var linked = await _db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == parentId && pd.DeviceId == deviceId);
        if (!linked)
            return false;

        var device = await _db.Set<Device>().FirstOrDefaultAsync(d => d.Id == deviceId);
        if (device == null)
            return false;

        if (device.IsPaused == paused)
        {
            // Idempotent: already in the requested state. No mutation, no
            // log, no audit row — the system state didn't change, so there
            // is nothing to audit. Parent still sees success.
            return true;
        }

        device.IsPaused = paused;
        TrackAndAddAudit(
            AuditEvent.ParentDevicePauseStateChanged(parentId, deviceId, paused));
        await _db.SaveChangesAsync();
        _logger.LogInformation(
            "Parent {ParentId} set device {DeviceId} paused={Paused}",
            parentId, deviceId, paused);
        return true;
    }

    public async Task<bool> SetBedtimeWindowAsync(Guid parentId, Guid deviceId, TimeOnly? start, TimeOnly? end)
    {
        // Ownership check — same silent-false shape as SetDevicePauseStateAsync.
        var linked = await _db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == parentId && pd.DeviceId == deviceId);
        if (!linked)
            return false;

        var device = await _db.Set<Device>().FirstOrDefaultAsync(d => d.Id == deviceId);
        if (device == null)
            return false;

        // Half-null is normalized to disabled; we do not reject with 400.
        // Keeps the endpoint idempotent for "clear the window".
        if (start is null || end is null)
        {
            device.BedtimeStart = null;
            device.BedtimeEnd = null;
        }
        else
        {
            device.BedtimeStart = start;
            device.BedtimeEnd = end;
        }

        // Audit the post-normalization state so the record matches what is
        // actually persisted on the Device row — half-null inputs become
        // {start:null,end:null} here, not {start:"22:00:00",end:null}.
        TrackAndAddAudit(AuditEvent.ParentBedtimeWindowSet(
            parentId, deviceId, device.BedtimeStart, device.BedtimeEnd));
        await _db.SaveChangesAsync();
        _logger.LogInformation(
            "Parent {ParentId} set bedtime window on device {DeviceId} to {Start}-{End}",
            parentId, deviceId,
            device.BedtimeStart?.ToString() ?? "disabled",
            device.BedtimeEnd?.ToString() ?? "disabled");
        return true;
    }

    public async Task<bool> SetDeviceModeFlagsAsync(
        Guid parentId, Guid deviceId,
        bool story, bool game, bool riddle, bool curiosity)
    {
        // Ownership check — same silent-false shape as SetDevicePauseStateAsync
        // and SetBedtimeWindowAsync.
        var linked = await _db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == parentId && pd.DeviceId == deviceId);
        if (!linked)
            return false;

        var device = await _db.Set<Device>().FirstOrDefaultAsync(d => d.Id == deviceId);
        if (device == null)
            return false;

        device.StoryEnabled = story;
        device.GameEnabled = game;
        device.RiddleEnabled = riddle;
        device.CuriosityEnabled = curiosity;

        TrackAndAddAudit(AuditEvent.ParentDeviceModeFlagsSet(
            parentId, deviceId, story, game, riddle, curiosity));
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Parent {ParentId} set mode flags on device {DeviceId}: story={Story} game={Game} riddle={Riddle} curiosity={Curiosity}",
            parentId, deviceId, story, game, riddle, curiosity);
        return true;
    }

    public async Task<bool> SetChildModeOverridesAsync(
        Guid parentId, Guid childId,
        bool? story, bool? game, bool? riddle, bool? curiosity)
    {
        // Ownership shape mirrors DeleteChildAsync: parent must own the
        // device the child belongs to. Silent false on a miss — no
        // existence leak for children owned by other parents.
        var child = await _db.Set<Child>().FirstOrDefaultAsync(c => c.Id == childId);
        if (child == null)
            return false;

        var ownsDevice = await _db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == parentId && pd.DeviceId == child.DeviceId);
        if (!ownsDevice)
            return false;

        child.StoryEnabled = story;
        child.GameEnabled = game;
        child.RiddleEnabled = riddle;
        child.CuriosityEnabled = curiosity;

        TrackAndAddAudit(AuditEvent.ChildModeOverridesSet(
            parentId, childId, story, game, riddle, curiosity));
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Parent {ParentId} set child {ChildId} mode overrides: story={Story} game={Game} riddle={Riddle} curiosity={Curiosity}",
            parentId, childId, story, game, riddle, curiosity);
        return true;
    }

    public async Task<List<LinkedDeviceDto>> GetLinkedDeviceDetailsAsync(Guid parentId)
    {
        var links = await _db.Set<ParentDevice>()
            .Where(pd => pd.ParentId == parentId)
            .Join(_db.Set<Device>(), pd => pd.DeviceId, d => d.Id, (pd, d) => new { pd.LinkedAt, Device = d })
            .ToListAsync();

        if (links.Count == 0)
            return new List<LinkedDeviceDto>();

        var deviceIds = links.Select(l => l.Device.Id).ToList();

        var childrenByDevice = (await _db.Set<Child>()
            .Where(c => deviceIds.Contains(c.DeviceId))
            .ToListAsync())
            .GroupBy(c => c.DeviceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var lastConversationByDevice = await _db.Set<Conversation>()
            .Where(c => deviceIds.Contains(c.DeviceId))
            .GroupBy(c => c.DeviceId)
            .Select(g => new { DeviceId = g.Key, LastStartedAt = g.Max(c => c.StartedAt) })
            .ToDictionaryAsync(x => x.DeviceId, x => x.LastStartedAt);

        return links.Select(l => new LinkedDeviceDto(
            l.Device.Id,
            l.Device.Name,
            l.Device.LastSeenAt,
            l.LinkedAt,
            lastConversationByDevice.TryGetValue(l.Device.Id, out var lastConv) ? lastConv : null,
            childrenByDevice.TryGetValue(l.Device.Id, out var children)
                ? children.Select(c => new LinkedDeviceChildDto(
                    c.Id, c.Name, c.GetAge(), c.Gender,
                    c.StoryEnabled, c.GameEnabled, c.RiddleEnabled, c.CuriosityEnabled)).ToList()
                : new List<LinkedDeviceChildDto>(),
            l.Device.IsPaused,
            l.Device.BedtimeStart,
            l.Device.BedtimeEnd,
            l.Device.StoryEnabled,
            l.Device.GameEnabled,
            l.Device.RiddleEnabled,
            l.Device.CuriosityEnabled
        )).ToList();
    }

    public async Task<List<AuditEventDto>> GetAuditEventsForParentAsync(
        Guid parentId, int limit, int offset)
    {
        // Ownership is enforced at the query level — there is no code path
        // below that could return another parent's row. Caller is already
        // [Authorize]'d in the controller; the parentId comes from their
        // JWT claim. Caller is also responsible for clamping limit/offset.
        var rows = await _db.Set<AuditEvent>()
            .Where(a => a.ActorParentId == parentId)
            .OrderByDescending(a => a.Timestamp)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return rows.Select(a => new AuditEventDto(
            a.Id,
            a.Timestamp,
            a.EventType.ToString(),
            a.TargetDeviceId,
            a.TargetChildId,
            // Parsed JsonNode so the wire payload is an actual JSON object,
            // not an escaped string. Factory code is the only writer so the
            // blob is always valid JSON (or null).
            a.Metadata is null ? null : JsonNode.Parse(a.Metadata)
        )).ToList();
    }

    // Central write path for audit rows: Adds the entity to the DbSet
    // AND increments AppMeter.AuditEventsWritten with the event_type tag.
    // Must be called exactly once per audit entity before the
    // SaveChangesAsync that persists it — same-transaction discipline
    // is preserved because the DbSet.Add happens here, not later.
    // The counter is volatile (process-local, scraped via /metrics) and
    // complements — does not replace — the durable AuditEvents row.
    private void TrackAndAddAudit(AuditEvent ev)
    {
        _db.Set<AuditEvent>().Add(ev);
        AppMeter.AuditEventsWritten.Add(1,
            new KeyValuePair<string, object?>("event_type", ev.EventType.ToString()));
    }

    private string GenerateJwt(Parent parent)
    {
        var key = _config["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key) || key == "ArmenianAiToyDefaultSecretKeyThatShouldBeChanged123!")
            throw new InvalidOperationException(
                "Jwt:Key must be configured and must not be the legacy default. " +
                "Set it via user-secrets (dotnet user-secrets set \"Jwt:Key\" ...) " +
                "or the JWT__KEY environment variable.");
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, parent.Id.ToString()),
            new Claim(ClaimTypes.Email, parent.Email)
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "ArmenianAiToy",
            audience: _config["Jwt:Audience"] ?? "ArmenianAiToy",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

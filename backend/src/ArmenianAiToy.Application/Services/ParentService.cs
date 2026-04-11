using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ArmenianAiToy.Application.Services;

public class ParentService : IParentService
{
    private readonly DbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<ParentService> _logger;

    public ParentService(DbContext db, IConfiguration config, ILogger<ParentService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task<Guid> RegisterAsync(string email, string password)
    {
        var existing = await _db.Set<Parent>().AnyAsync(p => p.Email == email);
        if (existing)
            throw new InvalidOperationException("Email already registered");

        var parent = new Parent
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            RegisteredAt = DateTime.UtcNow
        };

        _db.Set<Parent>().Add(parent);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Parent registered: {Email}", email);
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

    public async Task<List<Guid>> GetLinkedDeviceIdsAsync(Guid parentId)
    {
        return await _db.Set<ParentDevice>()
            .Where(pd => pd.ParentId == parentId)
            .Select(pd => pd.DeviceId)
            .ToListAsync();
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
                ? children.Select(c => new LinkedDeviceChildDto(c.Id, c.Name, c.GetAge(), c.Gender)).ToList()
                : new List<LinkedDeviceChildDto>()
        )).ToList();
    }

    private string GenerateJwt(Parent parent)
    {
        var key = _config["Jwt:Key"] ?? "ArmenianAiToyDefaultSecretKeyThatShouldBeChanged123!";
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

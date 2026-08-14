using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ArmenianAiToy.Application.Services;

/// <inheritdoc />
public sealed class ContentCatalogService : IContentCatalogService
{
    private readonly DbContext _db;
    private readonly ContentSyncOptions _baseline;

    /// <summary>Takes the base <see cref="DbContext"/> rather than
    /// <c>AppDbContext</c>, the same seam <c>ParentService</c> uses, so this
    /// stays in Application beside the helper it calls.</summary>
    public ContentCatalogService(DbContext db, ContentSyncOptions baseline)
    {
        _db = db;
        _baseline = baseline;
    }

    public async Task<ContentSyncOptions> ResolveForDeviceAsync(
        Guid deviceId, CancellationToken ct = default)
    {
        // Projected, not materialised as entities: this runs on the device
        // content-manifest path, which every toy hits on every sync attempt.
        var rows = await _db.Set<DeviceContentOverride>()
            .AsNoTracking()
            .Where(o => o.DeviceId == deviceId)
            .Select(o => new { o.ItemKind, o.ItemKey, o.Mode })
            .ToListAsync(ct);

        return DeviceContentEntitlement.Apply(
            _baseline, rows.Select(r => (r.ItemKind, r.ItemKey, r.Mode)));
    }
}

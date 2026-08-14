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

        // Which fleet-dark items THIS toy has been granted. Building the set
        // from the same rows the deny filter reads is what keeps "one row per
        // (device, item)" sufficient: an item cannot be granted and withheld at
        // once, so the two layers can never contradict each other.
        var allowed = rows
            .Where(r => string.Equals(
                DeviceContentEntitlement.NormalizeMode(r.Mode),
                DeviceContentEntitlement.ModeAllow, StringComparison.Ordinal))
            .Select(r => OverrideKey(r.ItemKind, r.ItemKey))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var items = await LoadItemsAsync(
            row => row.DefaultEnabled || allowed.Contains(OverrideKey(row.Kind, row.ItemKey)),
            ct);

        // Overlay first (what the catalogue HAS for this toy), then the deny
        // filter (what this toy is refused). A denied item that is also
        // fleet-dark and allowed cannot exist — see above — so the order is a
        // readability choice, not a precedence rule.
        return DeviceContentEntitlement.Apply(
            ContentItemOverlay.Apply(_baseline, items),
            rows.Select(r => (r.ItemKind, r.ItemKey, r.Mode)));
    }

    public async Task<ContentSyncOptions> ResolveFleetAsync(CancellationToken ct = default)
        => ContentItemOverlay.Apply(_baseline, await LoadItemsAsync(row => row.DefaultEnabled, ct));

    /// <summary>The catalogue fields this service needs. <c>DefaultEnabled</c>
    /// lives here rather than on <see cref="ContentItemOverlay.Item"/> because
    /// deciding WHICH items belong in a catalogue is this service's job — the
    /// overlay is handed a list that has already been decided.</summary>
    private sealed record Row(
        string Kind, string ItemKey, string Title, int Version,
        string RelativePath, string Sha256, long SizeBytes, bool DefaultEnabled);

    /// <summary>
    /// Every non-retired catalogue row, narrowed by <paramref name="keep"/>.
    /// <para>
    /// <c>RetiredAt</c> is filtered in the QUERY, in this one place: a retired
    /// item must leave every manifest by construction rather than by each
    /// caller remembering, because forgetting it would keep serving a story the
    /// owner believed he had removed.
    /// </para>
    /// </summary>
    private async Task<List<ContentItemOverlay.Item>> LoadItemsAsync(
        Func<Row, bool> keep, CancellationToken ct)
    {
        var rows = await _db.Set<ContentItem>()
            .AsNoTracking()
            .Where(i => i.RetiredAt == null)
            .Select(i => new Row(
                i.Kind, i.ItemKey, i.Title, i.Version,
                i.RelativePath, i.Sha256, i.SizeBytes, i.DefaultEnabled))
            .ToListAsync(ct);

        return rows
            .Where(keep)
            .Select(r => new ContentItemOverlay.Item(
                r.Kind, r.ItemKey, r.Title, r.Version,
                r.RelativePath, r.Sha256, r.SizeBytes))
            .ToList();
    }

    private static string OverrideKey(string? kind, string? key)
        => DeviceContentEntitlement.NormalizeKind(kind)
           + "|"
           + DeviceContentEntitlement.NormalizeKey(key);
}

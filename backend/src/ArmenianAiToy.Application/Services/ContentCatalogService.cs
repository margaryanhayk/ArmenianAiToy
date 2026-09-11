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
        string RelativePath, string Sha256, long SizeBytes, bool DefaultEnabled,
        bool Retired);

    /// <summary>
    /// Every catalogue row this device/fleet may see, narrowed by
    /// <paramref name="keep"/> — EXCEPT a retired one, which is always kept
    /// regardless of <paramref name="keep"/> or <c>DefaultEnabled</c>.
    /// <para>
    /// Before 2026-09-11, <c>RetiredAt</c> was filtered in the QUERY, so a
    /// retired item simply vanished from every manifest — indistinguishable
    /// from "never entitled" on the wire, and a device that had already
    /// cached it kept the file forever (CLAUDE.md: absence is not a
    /// retirement instruction). It is no longer filtered here: a retired row
    /// still reaches <see cref="ContentItemOverlay.Apply"/>, tagged
    /// <see cref="ContentItemOverlay.Item.Retired"/>, so
    /// <c>ContentManifestService</c> can emit it with <c>retired:true</c> —
    /// the one signal that tells a device to actually delete its copy.
    /// </para>
    /// </summary>
    private async Task<List<ContentItemOverlay.Item>> LoadItemsAsync(
        Func<Row, bool> keep, CancellationToken ct)
    {
        var rows = await _db.Set<ContentItem>()
            .AsNoTracking()
            .Select(i => new Row(
                i.Kind, i.ItemKey, i.Title, i.Version,
                i.RelativePath, i.Sha256, i.SizeBytes, i.DefaultEnabled,
                i.RetiredAt != null))
            .ToListAsync(ct);

        return rows
            .Where(r => r.Retired || keep(r))
            .Select(r => new ContentItemOverlay.Item(
                r.Kind, r.ItemKey, r.Title, r.Version,
                r.RelativePath, r.Sha256, r.SizeBytes, r.Retired))
            .ToList();
    }

    private static string OverrideKey(string? kind, string? key)
        => DeviceContentEntitlement.NormalizeKind(kind)
           + "|"
           + DeviceContentEntitlement.NormalizeKey(key);
}

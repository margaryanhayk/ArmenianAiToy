using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Interfaces;

/// <summary>
/// One toy's content catalogue: the fleet <see cref="ContentSyncOptions"/>
/// with that device's entitlement overrides applied.
///
/// <para>
/// Returns a plain <see cref="ContentSyncOptions"/> — deliberately NOT a new
/// type. Everything downstream (the manifest service, the content-file
/// endpoint, <c>AdvertisedStoryVersions</c>) already speaks that shape, so
/// per-device content is a different INSTANCE of the existing contract
/// rather than a second one to keep in step.
/// </para>
///
/// <para>
/// Scoped, because it reads the database. The fleet baseline stays the
/// singleton bound at startup; this only ever narrows it.
/// </para>
/// </summary>
public interface IContentCatalogService
{
    Task<ContentSyncOptions> ResolveForDeviceAsync(
        Guid deviceId, CancellationToken ct = default);
}

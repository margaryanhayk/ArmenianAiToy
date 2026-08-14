using ArmenianAiToy.Application.DTOs;

namespace ArmenianAiToy.Application.Interfaces;

/// <summary>
/// Builds the device content-manifest from a configured item set. Pure
/// decision — no DB, no IO.
/// <para>
/// Per-device entitlement lives OUTSIDE this service, exactly as its
/// original note promised: the caller resolves the device's own
/// <c>ContentSyncOptions</c> (see <see cref="IContentCatalogService"/>) and
/// hands it to <see cref="Build(Helpers.ContentSyncOptions)"/>. The
/// device-facing contract is unchanged, and this service stays a pure
/// function of the item set it is given.
/// </para>
/// </summary>
public interface IContentManifestService
{
    /// <summary>The FLEET manifest, from the options bound at startup. Still
    /// the right call for fleet-level questions ("is any music configured at
    /// all?"); anything a specific toy will act on wants the overload.</summary>
    ContentManifestResponse Build();

    /// <summary>The manifest for a specific item set — in practice one
    /// device's catalogue.</summary>
    ContentManifestResponse Build(Helpers.ContentSyncOptions options);
}

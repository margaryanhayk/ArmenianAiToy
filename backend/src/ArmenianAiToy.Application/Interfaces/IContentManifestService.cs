using ArmenianAiToy.Application.DTOs;

namespace ArmenianAiToy.Application.Interfaces;

/// <summary>
/// Builds the device content-manifest from the configured item. Pure
/// decision — no DB, no IO. (A later slice makes this per-device /
/// per-tier; the device-facing contract stays the same.)
/// </summary>
public interface IContentManifestService
{
    ContentManifestResponse Build();
}

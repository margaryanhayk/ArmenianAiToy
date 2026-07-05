using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;

namespace ArmenianAiToy.Application.Services;

/// <inheritdoc />
public sealed class ContentManifestService : IContentManifestService
{
    private readonly ContentSyncOptions _options;

    public ContentManifestService(ContentSyncOptions options) => _options = options;

    public ContentManifestResponse Build()
    {
        // Fail-closed: disabled OR missing any load-bearing field → empty
        // manifest (the device simply has nothing to sync). sha256 must be
        // 64 hex chars — a truncated hash would make every download "fail".
        if (!_options.Enabled
            || string.IsNullOrWhiteSpace(_options.StoryId)
            || string.IsNullOrWhiteSpace(_options.AudioUrl)
            || _options.SizeBytes <= 0
            || _options.Sha256 is not { Length: 64 })
        {
            return ContentManifestResponse.Empty();
        }

        return new ContentManifestResponse(new[]
        {
            new ContentStoryItem(
                StoryId: _options.StoryId,
                Version: _options.Version < 1 ? 1 : _options.Version,
                Title: _options.Title,
                AudioUrl: _options.AudioUrl,
                Sha256: _options.Sha256.ToLowerInvariant(),
                SizeBytes: _options.SizeBytes,
                Enabled: true),
        });
    }
}

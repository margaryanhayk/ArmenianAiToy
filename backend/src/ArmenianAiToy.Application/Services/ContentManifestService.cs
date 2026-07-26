using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;

namespace ArmenianAiToy.Application.Services;

/// <inheritdoc />
public sealed class ContentManifestService : IContentManifestService
{
    /// <summary>This backend's own device-authed streamer. Used to fill in
    /// an item that configured no explicit <c>AudioUrl</c>.</summary>
    public const string DefaultContentFileRoute = "/api/devices/content-file";

    private readonly ContentSyncOptions _options;

    public ContentManifestService(ContentSyncOptions options) => _options = options;

    public ContentManifestResponse Build()
    {
        // Master switch short-circuits before anything else: a disabled
        // deployment offers nothing regardless of what is configured.
        if (!_options.Enabled)
        {
            return ContentManifestResponse.Empty();
        }

        var items = new List<ContentStoryItem>();
        var seenStoryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var story in _options.ResolveStories())
        {
            // Fail-closed PER ITEM: one misconfigured story must not deny
            // the device the stories that ARE valid. sha256 must be 64 hex
            // chars — a truncated hash would make every download "fail" on
            // the device with no way to tell config rot from a bad transfer.
            if (string.IsNullOrWhiteSpace(story.StoryId)
                || story.SizeBytes <= 0
                || !IsSha256Hex(story.Sha256))
            {
                continue;
            }

            // storyId is the content-file lookup key, so a duplicate would
            // make that endpoint ambiguous. Keep the first and drop the rest
            // rather than serving an arbitrary one.
            if (!seenStoryIds.Add(story.StoryId))
            {
                continue;
            }

            items.Add(new ContentStoryItem(
                StoryId: story.StoryId,
                Version: story.Version < 1 ? 1 : story.Version,
                Title: story.Title,
                AudioUrl: ResolveAudioUrl(story),
                Sha256: story.Sha256.ToLowerInvariant(),
                SizeBytes: story.SizeBytes,
                Enabled: true));
        }

        return items.Count == 0
            ? ContentManifestResponse.Empty()
            : new ContentManifestResponse(items);
    }

    /// <summary>A configured URL wins verbatim — that is what keeps the
    /// legacy single-item config emitting the bare
    /// <c>/api/devices/content-file</c> that pre-multi-story firmware
    /// fetches. Only an unset URL gets the story-scoped default.</summary>
    private static string ResolveAudioUrl(ContentSyncStoryOptions story) =>
        string.IsNullOrWhiteSpace(story.AudioUrl)
            ? $"{DefaultContentFileRoute}?storyId={Uri.EscapeDataString(story.StoryId)}"
            : story.AudioUrl;

    private static bool IsSha256Hex(string? value)
    {
        if (value is not { Length: 64 })
        {
            return false;
        }
        foreach (var c in value)
        {
            var isHex = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!isHex)
            {
                return false;
            }
        }
        return true;
    }
}

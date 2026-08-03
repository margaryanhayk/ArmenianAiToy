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
                Enabled: true)
            {
                Clips = BuildClips(story),
            });
        }

        var music = BuildMusic();
        if (items.Count == 0 && music is null)
        {
            return ContentManifestResponse.Empty();
        }
        return new ContentManifestResponse(items) { Music = music };
    }

    /// <summary>Slice E — per-track validation, mirroring the story loop
    /// (drop only the offending track; dedupe keeps the first; default URL
    /// fill scoped by trackId). Null when nothing valid is configured, so
    /// the wire stays byte-identical for music-less deployments.</summary>
    private IReadOnlyList<ContentMusicItem>? BuildMusic()
    {
        var items = new List<ContentMusicItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var track in _options.ResolveMusic())
        {
            if (string.IsNullOrWhiteSpace(track.TrackId)
                || track.SizeBytes <= 0
                || !IsSha256Hex(track.Sha256)
                || !seen.Add(track.TrackId))
            {
                continue;
            }
            items.Add(new ContentMusicItem(
                TrackId: track.TrackId,
                Version: track.Version < 1 ? 1 : track.Version,
                Title: track.Title,
                AudioUrl: string.IsNullOrWhiteSpace(track.AudioUrl)
                    ? $"{DefaultContentFileRoute}?trackId={Uri.EscapeDataString(track.TrackId)}"
                    : track.AudioUrl,
                Sha256: track.Sha256.ToLowerInvariant(),
                SizeBytes: track.SizeBytes,
                Enabled: true));
        }
        return items.Count == 0 ? null : items;
    }

    /// <summary>
    /// B2 — per-clip validation, same fail-closed-per-item discipline as the
    /// story loop: an invalid clip (unknown kind, bad sha, non-positive
    /// size) is dropped and NEVER takes the story or its valid sibling clips
    /// with it. Duplicate kinds keep the first (the kind is the lookup key
    /// for <c>content-file?clip=</c>). Returns null when nothing survives so
    /// the wire stays byte-identical for clip-less stories.
    /// </summary>
    private static IReadOnlyList<ContentClipItem>? BuildClips(ContentSyncStoryOptions story)
    {
        if (story.Clips.Count == 0)
        {
            return null;
        }
        var clips = new List<ContentClipItem>();
        var seenKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var clip in story.Clips)
        {
            var kind = (clip.Kind ?? string.Empty).Trim().ToLowerInvariant();
            if (!ContentSyncClipOptions.AllowedKinds.Contains(kind, StringComparer.Ordinal)
                || clip.SizeBytes <= 0
                || !IsSha256Hex(clip.Sha256)
                || !seenKinds.Add(kind))
            {
                continue;
            }
            clips.Add(new ContentClipItem(
                Kind: kind,
                AudioUrl: string.IsNullOrWhiteSpace(clip.AudioUrl)
                    ? $"{DefaultContentFileRoute}?storyId={Uri.EscapeDataString(story.StoryId)}&clip={kind}"
                    : clip.AudioUrl,
                Sha256: clip.Sha256.ToLowerInvariant(),
                SizeBytes: clip.SizeBytes));
        }
        return clips.Count == 0 ? null : clips;
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

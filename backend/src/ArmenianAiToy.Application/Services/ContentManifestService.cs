using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ArmenianAiToy.Application.Services;

/// <inheritdoc />
public sealed class ContentManifestService : IContentManifestService
{
    /// <summary>This backend's own device-authed streamer. Used to fill in
    /// an item that configured no explicit <c>AudioUrl</c>.</summary>
    public const string DefaultContentFileRoute = "/api/devices/content-file";

    private readonly ContentSyncOptions _options;
    private readonly ILogger<ContentManifestService>? _logger;

    /// <summary>The logger is OPTIONAL (and the only logging in this class)
    /// so the many <c>new ContentManifestService(options)</c> call sites in
    /// tests keep compiling. It exists for exactly one message: a
    /// half-configured series pair, which is otherwise invisible — the toy
    /// would simply play the episodes as unordered standalones and nothing
    /// would look broken.</summary>
    public ContentManifestService(
        ContentSyncOptions options,
        ILogger<ContentManifestService>? logger = null)
    {
        _options = options;
        _logger = logger;
    }

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

            // Variant endings — checked BEFORE the dedupe, because an invalid
            // altOf drops the ITEM rather than the field, and a dropped item
            // must not consume its id and take a valid namesake down with it.
            // See TryResolveAltOf for why this failure mode is inverted
            // relative to the series pairing's.
            if (!TryResolveAltOf(story, out var altOf))
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

            // Computed ONCE per story: HasValidSeries logs on rejection, and
            // asking it twice (once per field) would double every warning.
            var hasSeries = HasValidSeries(story);

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
                SeriesId = hasSeries ? story.SeriesId : null,
                SeriesIndex = hasSeries ? story.SeriesIndex : null,
                // Only on a valid pairing, and only when actually authored —
                // an empty string on the wire would make the dashboard render
                // a blank headline instead of falling back to its descriptor.
                SeriesTitle = hasSeries && !string.IsNullOrWhiteSpace(story.SeriesTitle)
                    ? story.SeriesTitle.Trim()
                    : null,
                AltOf = altOf,
            });
        }

        var music = BuildMusic();
        var voice = BuildVoice();
        var games = BuildGames();
        if (items.Count == 0 && music is null && voice is null && games is null)
        {
            return ContentManifestResponse.Empty();
        }
        return new ContentManifestResponse(items)
        {
            Music = music,
            Voice = voice,
            Games = games,
        };
    }

    /// <summary>Offline games — per-clip validation, mirroring the voice loop
    /// (drop only the offending clip; dedupe keeps the first; default URL
    /// fill scoped by the pair). Null when nothing valid is configured, so
    /// the wire stays byte-identical for deployments with no game clips.
    /// <para>
    /// BOTH halves of the pair are held to the id allowlist here, unlike the
    /// story loop which only allowlists <c>seriesId</c>/<c>altOf</c>. The
    /// reason is what they become on the card: the game key is a DIRECTORY
    /// name and the clip id a filename stem, so a stray <c>/</c> or <c>..</c>
    /// would be a path, not an id. The firmware re-applies the identical rule
    /// on parse — a card can be hand-edited — but nothing invalid should ever
    /// reach the wire in the first place.
    /// </para></summary>
    private IReadOnlyList<ContentGameItem>? BuildGames()
    {
        var items = new List<ContentGameItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var clip in _options.ResolveGames())
        {
            if (!IsValidId(clip.GameKey)
                || !IsValidId(clip.ClipId)
                || clip.SizeBytes <= 0
                || !IsSha256Hex(clip.Sha256)
                // The PAIR is the lookup key — one game's "intro" and
                // another's are different clips, and must not collide.
                || !seen.Add($"{clip.GameKey}/{clip.ClipId}"))
            {
                continue;
            }
            items.Add(new ContentGameItem(
                GameKey: clip.GameKey,
                ClipId: clip.ClipId,
                Version: clip.Version < 1 ? 1 : clip.Version,
                AudioUrl: string.IsNullOrWhiteSpace(clip.AudioUrl)
                    ? $"{DefaultContentFileRoute}?gameKey={Uri.EscapeDataString(clip.GameKey)}"
                      + $"&clipId={Uri.EscapeDataString(clip.ClipId)}"
                    : clip.AudioUrl,
                Sha256: clip.Sha256.ToLowerInvariant(),
                SizeBytes: clip.SizeBytes,
                Enabled: true));
        }
        return items.Count == 0 ? null : items;
    }

    /// <summary>Welcome-flow — per-clip validation, mirroring the music loop
    /// exactly (drop only the offending clip; dedupe keeps the first; default
    /// URL fill scoped by voiceId). Null when nothing valid is configured, so
    /// the wire stays byte-identical for deployments with no voice clips.</summary>
    private IReadOnlyList<ContentVoiceItem>? BuildVoice()
    {
        var items = new List<ContentVoiceItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var clip in _options.ResolveVoice())
        {
            if (string.IsNullOrWhiteSpace(clip.VoiceId)
                || clip.SizeBytes <= 0
                || !IsSha256Hex(clip.Sha256)
                || !seen.Add(clip.VoiceId))
            {
                continue;
            }
            items.Add(new ContentVoiceItem(
                VoiceId: clip.VoiceId,
                Version: clip.Version < 1 ? 1 : clip.Version,
                AudioUrl: string.IsNullOrWhiteSpace(clip.AudioUrl)
                    ? $"{DefaultContentFileRoute}?voiceId={Uri.EscapeDataString(clip.VoiceId)}"
                    : clip.AudioUrl,
                Sha256: clip.Sha256.ToLowerInvariant(),
                SizeBytes: clip.SizeBytes,
                Enabled: true));
        }
        return items.Count == 0 ? null : items;
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

    /// <summary>
    /// Serial support — <c>seriesId</c> and <c>seriesIndex</c> are validated
    /// as ONE unit and travel together or not at all.
    /// <para>
    /// Valid means: a series id passing the same allowlist a story id must
    /// pass (lowercase ASCII, digits, <c>-</c>, <c>_</c>, bounded at 48),
    /// AND a positive index. Anything else — a missing half, an uppercase
    /// id, a zero/negative index — drops BOTH fields.
    /// </para>
    /// <para>
    /// Note what is NOT dropped: the story. A metadata typo must not take a
    /// working narration off the toy; the episode simply reverts to being an
    /// ordinary standalone story in the rotation. Uniqueness of
    /// <c>(seriesId, seriesIndex)</c> is deliberately not enforced here —
    /// the device orders by index and keeps the first of a tie, exactly the
    /// rule it already applies to duplicate story ids.
    /// </para>
    /// </summary>
    private bool HasValidSeries(ContentSyncStoryOptions story)
    {
        var hasId = !string.IsNullOrWhiteSpace(story.SeriesId);
        var hasIndex = story.SeriesIndex is > 0;
        if (hasId && hasIndex && IsValidId(story.SeriesId))
        {
            return true;
        }
        if (hasId || story.SeriesIndex is not null)
        {
            _logger?.LogWarning(
                "ContentSync story {StoryId}: series pairing dropped "
                + "(seriesId='{SeriesId}', seriesIndex={SeriesIndex}); "
                + "it will play as a standalone story.",
                story.StoryId, story.SeriesId, story.SeriesIndex);
        }
        return false;
    }

    /// <summary>
    /// Variant endings — resolves an item's <c>altOf</c>, or refuses the item.
    /// <para>
    /// Returns true with <paramref name="altOf"/> null for an ordinary story
    /// (nothing configured), and true with the trimmed id for a well-formed
    /// variant. Returns FALSE — meaning "drop this whole item" — when a value
    /// was configured but fails the story-id allowlist or names the item
    /// itself.
    /// </para>
    /// <para>
    /// The drop-the-item posture is deliberate, and the opposite of
    /// <see cref="HasValidSeries"/>'s drop-the-field. It turns on what the
    /// item IS. A serial episode is a whole story; stripping its position
    /// leaves a story the child can still enjoy out of order, so keeping it is
    /// the kinder failure. An alternate ending is NOT a whole story — it is
    /// the tail of one, assembled to start mid-way. Stripping its
    /// <c>altOf</c> would make the device treat it as an ordinary rotation
    /// member and one day narrate half a story to a child as though it were
    /// the whole thing. Refusing it costs nothing a child would otherwise
    /// hear: the base story is untouched and keeps its only ending.
    /// </para>
    /// <para>
    /// Existence of the referenced base story is NOT checked. The device
    /// fails closed — it only ever looks up a variant BY the base story it is
    /// already about to play — so a dangling reference is inert, and
    /// enforcing it here would make config edits order-dependent for no
    /// safety gain.
    /// </para>
    /// </summary>
    private bool TryResolveAltOf(ContentSyncStoryOptions story, out string? altOf)
    {
        altOf = null;
        var configured = story.AltOf?.Trim();
        if (string.IsNullOrEmpty(configured))
        {
            return true;   // an ordinary story
        }
        if (!IsValidId(configured))
        {
            _logger?.LogWarning(
                "ContentSync story {StoryId}: dropped — altOf='{AltOf}' is not a "
                + "valid story id, and an alternate ending must never enter the "
                + "rotation as a story of its own.",
                story.StoryId, configured);
            return false;
        }
        if (string.Equals(configured, story.StoryId?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogWarning(
                "ContentSync story {StoryId}: dropped — altOf names the story "
                + "itself, which has no meaning as a variant.",
                story.StoryId);
            return false;
        }
        altOf = configured;
        return true;
    }

    /// <summary>The firmware's <c>cs_is_valid_story_id</c> allowlist, kept
    /// in step here: lowercase ASCII letters, digits, <c>-</c> and <c>_</c>,
    /// 1..48 characters. Uppercase is rejected on both sides so one series
    /// can never become two on a case-preserving SD card.</summary>
    private static bool IsValidId(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 48)
        {
            return false;
        }
        foreach (var c in value)
        {
            var ok = c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_';
            if (!ok)
            {
                return false;
            }
        }
        return true;
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

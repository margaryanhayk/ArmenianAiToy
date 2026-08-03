namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Response for <c>GET /api/devices/content-manifest</c> — the story-audio
/// set this device should hold on its SD card. Minimal first slice: zero
/// or one item, from static config. <c>Stories</c> is always present
/// (empty when sync is disabled/unconfigured) so the firmware parses one
/// stable shape.
/// </summary>
public sealed record ContentManifestResponse(
    IReadOnlyList<ContentStoryItem> Stories)
{
    /// <summary>B3 — the device's parent-set spoken-story-intro flag,
    /// stamped by the controller (the manifest service is static config and
    /// knows nothing about devices). Nullable so service-built instances and
    /// pre-B3 consumers are unaffected; the firmware caches the last-known
    /// value so the toggle applies offline.</summary>
    public bool? StoryIntroEnabled { get; init; }

    /// <summary>Slice E — bedtime music tracks (separate namespace from
    /// stories; firmware syncs them to /music). Null/empty until the owner
    /// configures rights-cleared tracks; pre-music firmware ignores it.</summary>
    public IReadOnlyList<ContentMusicItem>? Music { get; init; }

    /// <summary>Slice E — the device's parent-set bedtime-music toggle,
    /// stamped by the controller like <see cref="StoryIntroEnabled"/>.</summary>
    public bool? BedtimeMusicEnabled { get; init; }

    public static ContentManifestResponse Empty() =>
        new(Array.Empty<ContentStoryItem>());
}

/// <summary>Slice E — one downloadable bedtime-music track.</summary>
public sealed record ContentMusicItem(
    string TrackId,
    int Version,
    string Title,
    string AudioUrl,
    string Sha256,
    long SizeBytes,
    bool Enabled);

/// <summary>One downloadable story-audio item. <c>Enabled=false</c> tells
/// the device to remove its cached copy (retire) — the device may ignore
/// this in the minimal slice, but the field is on the wire from day one so
/// retirement never needs a contract change.</summary>
public sealed record ContentStoryItem(
    string StoryId,
    int Version,
    string Title,
    string AudioUrl,
    string Sha256,
    long SizeBytes,
    bool Enabled)
{
    /// <summary>B2 — optional per-story clips (intro / question / summary),
    /// each downloaded and sha-verified like the narration and sharing the
    /// story's <c>Version</c>. Null/empty for stories that ship no clips;
    /// pre-B2 firmware ignores the field entirely. Init-prop rather than a
    /// positional parameter so existing constructor call sites compile
    /// unchanged.</summary>
    public IReadOnlyList<ContentClipItem>? Clips { get; init; }
}

/// <summary>One per-story clip. <c>Kind</c> is a bounded vocabulary
/// (intro | question | summary) — validated server-side at manifest build.</summary>
public sealed record ContentClipItem(
    string Kind,
    string AudioUrl,
    string Sha256,
    long SizeBytes);

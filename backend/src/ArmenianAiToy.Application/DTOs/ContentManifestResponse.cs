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
    public static ContentManifestResponse Empty() =>
        new(Array.Empty<ContentStoryItem>());
}

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
    bool Enabled);

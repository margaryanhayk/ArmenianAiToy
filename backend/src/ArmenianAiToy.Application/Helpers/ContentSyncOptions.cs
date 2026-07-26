using Microsoft.Extensions.Configuration;

namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Config-bound description of the story-audio set the device
/// content-manifest offers, from section <c>ContentSync</c>.
/// <para>
/// Two shapes are accepted, mirroring the <c>Jwt:Keys</c> / <c>Jwt:Key</c>
/// precedent in <see cref="Auth.JwtKeys"/>:
/// </para>
/// <list type="bullet">
///   <item><description><b>Preferred</b> — <see cref="Stories"/>, an
///   ordered list. The manifest returns them in configured order.</description></item>
///   <item><description><b>Legacy</b> — the flat scalars
///   (<see cref="StoryId"/>, <see cref="Sha256"/>, …) describing ONE item.
///   Still honored so deployments and bench overlays written before
///   multi-story keep working untouched.</description></item>
/// </list>
/// <para>
/// When <see cref="Stories"/> has entries it wins and the scalars are
/// ignored. Resolution happens in <see cref="ResolveStories"/> — the one
/// place both the manifest service and the content-file endpoint read, so
/// they can never disagree about what is configured.
/// </para>
/// <para>
/// Ships <see cref="Enabled"/> = <c>false</c> → the manifest returns an
/// empty story list and the content-file endpoint 404s until an operator
/// configures real items. Per-item validity is enforced downstream by
/// <see cref="Services.ContentManifestService"/>, which drops only the
/// offending item rather than failing the whole manifest.
/// </para>
/// <para>
/// Per-device / per-tier entitlement is still a later slice; this is
/// static config for every device, on an unchanged device-facing contract.
/// </para>
/// </summary>
public sealed class ContentSyncOptions
{
    /// <summary>Master switch. When false the manifest is empty and the
    /// content-file endpoint 404s, whichever shape is configured.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Preferred multi-story shape. Empty → fall back to the
    /// legacy scalars below.</summary>
    public List<ContentSyncStoryOptions> Stories { get; set; } = new();

    /// <summary>Legacy single-item: library story id (kebab-case).</summary>
    public string StoryId { get; set; } = string.Empty;

    /// <summary>Legacy single-item: content version — bump when the audio
    /// changes so devices re-download (the SD filename embeds it).</summary>
    public int Version { get; set; } = 1;

    /// <summary>Legacy single-item: display title (informational).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Legacy single-item: URL the device downloads from.
    /// Defaults to the backend's own device-authed streamer with no query
    /// string — the shape pre-multi-story firmware already fetches, which
    /// is why the legacy path must not gain a <c>?storyId=</c>.</summary>
    public string AudioUrl { get; set; } = "/api/devices/content-file";

    /// <summary>Legacy single-item: server-side filesystem path of the MP3
    /// that <c>GET /api/devices/content-file</c> streams. Deliberately
    /// OUTSIDE wwwroot (same posture as
    /// <see cref="FirmwareUpdateOptions.ImagePath"/>). Empty → 404.</summary>
    public string AudioPath { get; set; } = string.Empty;

    /// <summary>Legacy single-item: lowercase hex SHA-256 of the MP3.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Legacy single-item: exact byte length of the MP3.</summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Binds section <c>ContentSync</c>. Both shapes are read; which one
    /// takes effect is <see cref="ResolveStories"/>'s decision.
    /// <para>
    /// Lives here as a pure config→options helper (same pattern as
    /// <see cref="RetentionPolicy.ResolveMessages"/> and
    /// <see cref="Auth.JwtKeys.ResolveOrderedKeys"/>) rather than inline in
    /// DI, so the hand-rolled array binding is reachable by tests. A silent
    /// binding bug would otherwise leave every unit test green while the
    /// device received an empty manifest.
    /// </para>
    /// </summary>
    public static ContentSyncOptions Resolve(IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var options = new ContentSyncOptions();
        var section = config.GetSection("ContentSync");

        if (bool.TryParse(section["Enabled"], out var enabled)) options.Enabled = enabled;
        options.StoryId = section["StoryId"] ?? "";
        if (int.TryParse(section["Version"], out var version)) options.Version = version;
        options.Title = section["Title"] ?? "";
        options.AudioUrl = string.IsNullOrWhiteSpace(section["AudioUrl"])
            ? options.AudioUrl : section["AudioUrl"]!;
        options.AudioPath = section["AudioPath"] ?? "";
        options.Sha256 = section["Sha256"] ?? "";
        if (long.TryParse(section["SizeBytes"], out var size)) options.SizeBytes = size;

        foreach (var child in section.GetSection("Stories").GetChildren())
        {
            var story = new ContentSyncStoryOptions
            {
                StoryId = child["StoryId"] ?? "",
                Title = child["Title"] ?? "",
                AudioUrl = child["AudioUrl"] ?? "",
                AudioPath = child["AudioPath"] ?? "",
                Sha256 = child["Sha256"] ?? "",
            };
            if (int.TryParse(child["Version"], out var storyVersion)) story.Version = storyVersion;
            if (long.TryParse(child["SizeBytes"], out var storySize)) story.SizeBytes = storySize;
            options.Stories.Add(story);
        }

        return options;
    }

    /// <summary>
    /// The configured story set, in order: <see cref="Stories"/> when it has
    /// entries, otherwise the legacy scalars projected as a one-element list.
    /// <para>
    /// The legacy item is synthesized unconditionally rather than
    /// pre-validated — an all-empty config yields one invalid item, which
    /// the manifest service then drops, so shipped defaults still produce an
    /// empty manifest. Validation lives in exactly one place.
    /// </para>
    /// </summary>
    public IReadOnlyList<ContentSyncStoryOptions> ResolveStories()
    {
        if (Stories.Count > 0)
        {
            return Stories;
        }

        return new[]
        {
            new ContentSyncStoryOptions
            {
                StoryId = StoryId,
                Version = Version,
                Title = Title,
                AudioUrl = AudioUrl,
                AudioPath = AudioPath,
                Sha256 = Sha256,
                SizeBytes = SizeBytes,
            },
        };
    }
}

/// <summary>
/// One configured story-audio item. Same fields as the legacy scalars on
/// <see cref="ContentSyncOptions"/>, so migrating a config is a move into
/// the <c>Stories</c> array with no renames.
/// </summary>
public sealed class ContentSyncStoryOptions
{
    /// <summary>Library story id (kebab-case, e.g. "anban-huri"). Doubles
    /// as the lookup key for <c>GET /api/devices/content-file?storyId=</c>,
    /// so it must be unique within the list — the manifest service keeps
    /// the first of any duplicate pair.</summary>
    public string StoryId { get; set; } = string.Empty;

    /// <summary>Content version — bump when the audio changes so devices
    /// re-download (the SD filename embeds it).</summary>
    public int Version { get; set; } = 1;

    /// <summary>Display title (informational; the device only logs it).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>URL the device downloads from. Empty → the manifest
    /// service fills in this backend's own device-authed streamer scoped to
    /// this story (<c>/api/devices/content-file?storyId=…</c>). A configured
    /// value (including an absolute URL) is passed through verbatim.</summary>
    public string AudioUrl { get; set; } = string.Empty;

    /// <summary>Server-side filesystem path of the MP3. Deliberately
    /// OUTSIDE wwwroot. Empty → the content-file endpoint 404s for this
    /// story.</summary>
    public string AudioPath { get; set; } = string.Empty;

    /// <summary>Lowercase hex SHA-256 of the MP3; the device verifies it
    /// while streaming to SD and discards the download on mismatch.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Exact byte length of the MP3.</summary>
    public long SizeBytes { get; set; }
}

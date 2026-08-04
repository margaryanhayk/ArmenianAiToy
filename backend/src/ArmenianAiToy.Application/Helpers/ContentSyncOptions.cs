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

    /// <summary>Slice E — calm Armenian music tracks for the bedtime
    /// window, synced to the toy's SD alongside stories but kept in a
    /// SEPARATE namespace (a music track must never enter the story
    /// rotation). Empty by default — the feature ships dark until the
    /// owner adds rights-cleared tracks.</summary>
    public List<ContentSyncMusicOptions> Music { get; set; } = new();

    /// <summary>Welcome-flow — device-global spoken clips (power-on
    /// greetings, the "what shall we do?" prompts, the two fallback lines).
    /// A THIRD namespace beside stories and music, for the same reason music
    /// got its own: these are not stories and must never enter the story
    /// rotation. Their ROLE is carried by the id, not by a field — the
    /// firmware pattern-matches the <c>greet-</c> prefix and looks the rest
    /// up by exact id, so adding greeting #25 is a config edit with no
    /// firmware change. Empty by default; the toy is silent at boot until
    /// the owner ships rendered clips.</summary>
    public List<ContentSyncVoiceOptions> Voice { get; set; } = new();

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

    /// <summary>
    /// Optional base directory for RELATIVE <c>AudioPath</c> values.
    ///
    /// <para>
    /// The serving endpoint requires an absolute path and 404s otherwise, so
    /// without this every deployment would have to hardcode absolute paths —
    /// impossible when the same config must work on a Windows bench
    /// (<c>C:\...</c>) and in the Linux container (<c>/app/...</c>). With a
    /// root set, a story can say <c>AudioPath: "anban-huri.mp3"</c> and stay
    /// portable.
    /// </para>
    ///
    /// <para>
    /// The fail-closed property is preserved: this only RESOLVES a path, it
    /// does not bypass anything. The endpoint still requires the resolved
    /// path to be absolute AND the file to exist, so a typo still 404s
    /// rather than serving something unintended. A relative root is itself
    /// resolved against the app's base directory, which is where content
    /// shipped inside the image lands.
    /// </para>
    /// </summary>
    public string AudioRoot { get; set; } = string.Empty;

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
    /// <summary>
    /// Applies <see cref="AudioRoot"/> to a configured <paramref name="audioPath"/>.
    /// An already-absolute path is returned untouched (existing configs are
    /// unaffected); an empty path stays empty so it keeps failing closed.
    /// </summary>
    public static string ResolveAudioPath(string audioRoot, string audioPath)
    {
        if (string.IsNullOrWhiteSpace(audioPath)) return audioPath ?? string.Empty;
        if (Path.IsPathRooted(audioPath)) return audioPath;
        if (string.IsNullOrWhiteSpace(audioRoot)) return audioPath;

        var root = Path.IsPathRooted(audioRoot)
            ? audioRoot
            : Path.Combine(AppContext.BaseDirectory, audioRoot);
        return Path.GetFullPath(Path.Combine(root, audioPath));
    }

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
        options.AudioRoot = section["AudioRoot"] ?? "";
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

            // B2 — hand-rolled clip binding, same reachable-by-tests rule as
            // the story array itself: a new field silently missing from this
            // loop would bind as default and leave every unit test green.
            foreach (var clipChild in child.GetSection("Clips").GetChildren())
            {
                var clip = new ContentSyncClipOptions
                {
                    Kind = clipChild["Kind"] ?? "",
                    AudioUrl = clipChild["AudioUrl"] ?? "",
                    AudioPath = clipChild["AudioPath"] ?? "",
                    Sha256 = clipChild["Sha256"] ?? "",
                };
                if (long.TryParse(clipChild["SizeBytes"], out var clipSize)) clip.SizeBytes = clipSize;
                story.Clips.Add(clip);
            }
            options.Stories.Add(story);
        }

        // Slice E — hand-rolled music binding, same reachable-by-tests rule.
        foreach (var child in section.GetSection("Music").GetChildren())
        {
            var track = new ContentSyncMusicOptions
            {
                TrackId = child["TrackId"] ?? "",
                Title = child["Title"] ?? "",
                AudioUrl = child["AudioUrl"] ?? "",
                AudioPath = child["AudioPath"] ?? "",
                Sha256 = child["Sha256"] ?? "",
            };
            if (int.TryParse(child["Version"], out var trackVersion)) track.Version = trackVersion;
            if (long.TryParse(child["SizeBytes"], out var trackSize)) track.SizeBytes = trackSize;
            options.Music.Add(track);
        }

        // Welcome-flow — hand-rolled voice-clip binding, same
        // reachable-by-tests rule as the story and music arrays.
        foreach (var child in section.GetSection("Voice").GetChildren())
        {
            var clip = new ContentSyncVoiceOptions
            {
                VoiceId = child["VoiceId"] ?? "",
                AudioUrl = child["AudioUrl"] ?? "",
                AudioPath = child["AudioPath"] ?? "",
                Sha256 = child["Sha256"] ?? "",
            };
            if (int.TryParse(child["Version"], out var clipVersion)) clip.Version = clipVersion;
            if (long.TryParse(child["SizeBytes"], out var clipSize)) clip.SizeBytes = clipSize;
            options.Voice.Add(clip);
        }

        return options;
    }

    /// <summary>Welcome-flow — the configured device-global voice clips with
    /// <see cref="AudioRoot"/> applied, mirroring <see cref="ResolveMusic"/>
    /// (one resolution site for the manifest service AND content-file).</summary>
    public IReadOnlyList<ContentSyncVoiceOptions> ResolveVoice()
        => Voice
            .Select(v => new ContentSyncVoiceOptions
            {
                VoiceId = v.VoiceId,
                Version = v.Version,
                AudioUrl = v.AudioUrl,
                AudioPath = ResolveAudioPath(AudioRoot, v.AudioPath),
                Sha256 = v.Sha256,
                SizeBytes = v.SizeBytes,
            })
            .ToList();

    /// <summary>Slice E — the configured music set with
    /// <see cref="AudioRoot"/> applied, mirroring <see cref="ResolveStories"/>
    /// (one resolution site for the manifest service AND content-file).</summary>
    public IReadOnlyList<ContentSyncMusicOptions> ResolveMusic()
        => Music
            .Select(m => new ContentSyncMusicOptions
            {
                TrackId = m.TrackId,
                Version = m.Version,
                Title = m.Title,
                AudioUrl = m.AudioUrl,
                AudioPath = ResolveAudioPath(AudioRoot, m.AudioPath),
                Sha256 = m.Sha256,
                SizeBytes = m.SizeBytes,
            })
            .ToList();

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
        // AudioRoot is applied HERE, in the one place both the manifest
        // service and the content-file endpoint read, so they can never
        // disagree about which file a story id points at.
        if (Stories.Count > 0)
        {
            return Stories
                .Select(s => new ContentSyncStoryOptions
                {
                    StoryId = s.StoryId,
                    Version = s.Version,
                    Title = s.Title,
                    AudioUrl = s.AudioUrl,
                    AudioPath = ResolveAudioPath(AudioRoot, s.AudioPath),
                    Sha256 = s.Sha256,
                    SizeBytes = s.SizeBytes,
                    Clips = s.Clips
                        .Select(c => new ContentSyncClipOptions
                        {
                            Kind = c.Kind,
                            AudioUrl = c.AudioUrl,
                            AudioPath = ResolveAudioPath(AudioRoot, c.AudioPath),
                            Sha256 = c.Sha256,
                            SizeBytes = c.SizeBytes,
                        })
                        .ToList(),
                })
                .ToList();
        }

        return new[]
        {
            new ContentSyncStoryOptions
            {
                StoryId = StoryId,
                Version = Version,
                Title = Title,
                AudioUrl = AudioUrl,
                AudioPath = ResolveAudioPath(AudioRoot, AudioPath),
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

    /// <summary>
    /// B2 — optional per-story CLIPS: short pre-rendered MP3s that travel
    /// with the narration (spoken intro, reflection question, after-story
    /// summary). Clips share the story's <see cref="Version"/> — bumping the
    /// story version refreshes narration AND clips (clips are tiny; a
    /// separate per-clip version is not worth the contract surface). Empty
    /// for stories that ship no clips — the toy then plays the story without
    /// intro/reflection, exactly the pre-B2 behavior.
    /// </summary>
    public List<ContentSyncClipOptions> Clips { get; set; } = new();
}

/// <summary>
/// Slice E — one configured bedtime-music track. Same field discipline as
/// <see cref="ContentSyncStoryOptions"/> (id is a lookup key + SD filename
/// component, sha/size verified on-device), but a distinct type and id
/// namespace so a track can never be mistaken for a story.
/// </summary>
public sealed class ContentSyncMusicOptions
{
    public string TrackId { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string Title { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public string AudioPath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

/// <summary>
/// Welcome-flow — one device-global spoken clip (a greeting, a menu prompt,
/// or one of the two fallback lines). Same field discipline as
/// <see cref="ContentSyncMusicOptions"/>, minus <c>Title</c>: a device-global
/// clip has no display surface anywhere, and carrying a title would cost
/// ~65 bytes per entry in three firmware-side tables for a field nothing
/// reads.
/// <para>
/// <see cref="VoiceId"/> carries the clip's ROLE. Reserved ids:
/// <c>greet-01</c>…<c>greet-NN</c> (the rotated power-on pool — the only
/// PREFIX the firmware matches), <c>ask-</c> plus the enabled-mode letters in
/// fixed order s,g,r,c (e.g. <c>ask-sgrc</c>, <c>ask-s</c>), <c>ask-any</c>
/// (generic fallback), <c>say-again</c> (one mis-hear retry line), and
/// <c>just-story</c> (the graceful default when the toy gives up asking).
/// </para>
/// </summary>
public sealed class ContentSyncVoiceOptions
{
    /// <summary>Clip id (kebab-case, same allowlist as StoryId/TrackId).
    /// Doubles as the lookup key for
    /// <c>GET /api/devices/content-file?voiceId=</c> and as the SD filename
    /// component, so it must be unique — the manifest service keeps the
    /// first of any duplicate pair.</summary>
    public string VoiceId { get; set; } = string.Empty;

    /// <summary>Content version — bump when the audio changes, or every toy
    /// keeps its cached copy (the SD filename embeds it).</summary>
    public int Version { get; set; } = 1;

    public string AudioUrl { get; set; } = string.Empty;
    public string AudioPath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

/// <summary>
/// One per-story clip. <see cref="Kind"/> is a bounded vocabulary
/// (<c>intro</c> / <c>question</c> / <c>summary</c> / <c>offer</c> / …) — the
/// manifest service drops unknown kinds so a config typo can never push an
/// arbitrary file role to the fleet.
/// </summary>
public sealed class ContentSyncClipOptions
{
    public const string KindIntro = "intro";
    public const string KindQuestion = "question";
    public const string KindSummary = "summary";

    /// <summary>Reflection-dialogue slice: questions 2 and 3 of the
    /// after-story dialogue. Bare <c>question</c> stays question index 0.</summary>
    public const string KindQuestion1 = "question1";
    public const string KindQuestion2 = "question2";

    /// <summary>Welcome-flow — the spoken offer lines, which are what let the
    /// toy name a story out loud without any runtime TTS.
    /// <c>offer</c> = «Ուզո՞ւմ ես լսել «X»-ը։» for a story the child has not
    /// heard; <c>reoffer</c> = «Մենք արդեն լսել ենք «X»-ը…» when the whole
    /// shelf has been heard.
    /// <para>
    /// Named <c>reoffer</c>, not <c>offer-again</c>, deliberately: the
    /// firmware's <c>CS_CLIP_KIND_LEN</c> is 10 and "offer-again" is 11
    /// characters, so the longer name would force a second bound bump and
    /// grow every clip slot in five 16-entry tables.
    /// </para></summary>
    public const string KindOffer = "offer";
    public const string KindReoffer = "reoffer";

    public static readonly IReadOnlyList<string> AllowedKinds =
        [KindIntro, KindQuestion, KindSummary, KindQuestion1, KindQuestion2,
         KindOffer, KindReoffer];

    /// <summary>Clip role: intro | question | summary | offer | reoffer.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>URL the device downloads from. Empty → the manifest service
    /// fills in <c>/api/devices/content-file?storyId=…&amp;clip=…</c>.</summary>
    public string AudioUrl { get; set; } = string.Empty;

    /// <summary>Server-side filesystem path of the clip MP3 (outside
    /// wwwroot; relative values resolve against <c>AudioRoot</c>).</summary>
    public string AudioPath { get; set; } = string.Empty;

    /// <summary>Lowercase hex SHA-256 of the clip MP3.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Exact byte length of the clip MP3.</summary>
    public long SizeBytes { get; set; }
}

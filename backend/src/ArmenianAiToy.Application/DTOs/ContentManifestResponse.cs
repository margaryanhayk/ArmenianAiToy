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

    /// <summary>The device's parent-set in-story-pauses flag, stamped by the
    /// controller beside <see cref="StoryIntroEnabled"/> and cached in the
    /// toy's SD index so it applies offline. Nullable for the same reason:
    /// service-built instances and older consumers are unaffected, and an
    /// absent field means the shipped default (ON).</summary>
    public bool? StoryPausesEnabled { get; init; }

    /// <summary>The device's parent-set variant-endings flag, stamped
    /// alongside <see cref="StoryPausesEnabled"/>. Absent → ON, the shipped
    /// default; a library with no alternate files behaves identically either
    /// way.</summary>
    public bool? VariantEndingsEnabled { get; init; }

    /// <summary>Slice E — bedtime music tracks (separate namespace from
    /// stories; firmware syncs them to /music). Null/empty until the owner
    /// configures rights-cleared tracks; pre-music firmware ignores it.</summary>
    public IReadOnlyList<ContentMusicItem>? Music { get; init; }

    /// <summary>Slice E — the device's parent-set bedtime-music toggle,
    /// stamped by the controller like <see cref="StoryIntroEnabled"/>.</summary>
    public bool? BedtimeMusicEnabled { get; init; }

    /// <summary>Welcome-flow — device-global spoken clips (greetings, menu
    /// prompts, fallback lines); firmware syncs them to /voice. Null until
    /// the owner configures rendered clips, so the wire stays byte-identical
    /// for deployments that have none, and pre-welcome firmware ignores it.</summary>
    public IReadOnlyList<ContentVoiceItem>? Voice { get; init; }

    /// <summary>Welcome-flow — the four parent mode switches, stamped by the
    /// controller (device flag with the default child's override applied).
    /// The toy caches them in its SD index so the "what shall we do?" prompt
    /// offers only permitted modes even offline. Nullable so service-built
    /// instances and older consumers are unaffected; the firmware treats an
    /// absent field as enabled, matching the shipped server default.
    /// <para>
    /// These do NOT enforce anything — <c>DeviceService.IsModeEnabledForRequestAsync</c>
    /// remains the gate. They exist so the toy never offers a child something
    /// it will then refuse.
    /// </para></summary>
    public bool? StoryEnabled { get; init; }
    public bool? GameEnabled { get; init; }
    public bool? RiddleEnabled { get; init; }
    public bool? CuriosityEnabled { get; init; }

    public static ContentManifestResponse Empty() =>
        new(Array.Empty<ContentStoryItem>());
}

/// <summary>Welcome-flow — one downloadable device-global spoken clip. No
/// title: the id carries the role and nothing displays these.</summary>
public sealed record ContentVoiceItem(
    string VoiceId,
    int Version,
    string AudioUrl,
    string Sha256,
    long SizeBytes,
    bool Enabled);

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

    /// <summary>Serial support — the series this story is an EPISODE of,
    /// and its 1-based position in that series. BOTH are null for an
    /// ordinary standalone story, which is every story shipped before this
    /// slice — so the wire is byte-identical for non-serial deployments and
    /// pre-serial firmware ignores the fields entirely.
    /// <para>
    /// Validated as a PAIR at manifest build: a series id that fails the id
    /// allowlist, or a missing/non-positive index, drops BOTH fields and
    /// leaves the story itself on the manifest as a standalone. A half-set
    /// pair is not something the device can order, and dropping the story
    /// over a metadata typo would take a working narration off the toy.
    /// </para></summary>
    public string? SeriesId { get; init; }

    /// <inheritdoc cref="SeriesId" />
    public int? SeriesIndex { get; init; }

    /// <summary>Variant endings — the story this item is an ALTERNATE
    /// ENDING of. Null for an ordinary story, which is every story shipped
    /// before this slice, so the wire is byte-identical for deployments that
    /// ship no variants and pre-variant firmware ignores the field.
    /// <para>
    /// An alt entry is NOT a rotation member: the device syncs it like any
    /// other file but never selects it, offers it by name, or counts it as
    /// heard. It is reachable only by asking for the base story's variant on
    /// a re-listen, with the parent's variant-endings toggle on.
    /// </para>
    /// <para>
    /// Validated at manifest build against the same id allowlist a story id
    /// must pass, plus a self-reference check. Note the failure mode is the
    /// OPPOSITE of the series pairing's: an invalid <c>altOf</c> drops the
    /// whole ITEM rather than just the field. Dropping only the field would
    /// promote a half-story — narration that starts mid-way and ends
    /// differently — into the rotation as if it were a story of its own,
    /// which is a defect a child would hear. Dropping the item costs nothing
    /// a child would otherwise get: the base story is untouched and simply
    /// keeps its only ending.
    /// </para></summary>
    public string? AltOf { get; init; }

    /// <summary>Serial support — the series' authored DISPLAY name
    /// («Ծիվիկ»), for the parent dashboard's series card. Null when the
    /// pairing is invalid OR when no name was configured; the dashboard
    /// then falls back to a generic descriptor rather than showing a slug.
    /// The device never reads this — it has no display surface.</summary>
    public string? SeriesTitle { get; init; }
}

/// <summary>One per-story clip. <c>Kind</c> is a bounded vocabulary
/// (intro | question | summary) — validated server-side at manifest build.</summary>
public sealed record ContentClipItem(
    string Kind,
    string AudioUrl,
    string Sha256,
    long SizeBytes);

namespace ArmenianAiToy.Api.Security;

/// <summary>
/// Fail-closed resolution for <c>Audio:BlobStoreRoot</c> — the durable
/// storage root for child + assistant voice recordings written by
/// <c>ArmenianAiToy.Infrastructure.Audio.LocalDiskAudioBlobStore</c>.
///
/// <para>
/// Same posture as <c>ContentSync:UploadRoot</c>
/// (<c>ArmenianAiToy.Application.Helpers.ContentItemOverlay.TryResolveRoot</c>,
/// enforced at <c>InternalController</c>'s upload endpoint): on Railway the
/// container's own filesystem is wiped on every redeploy, so an unset or
/// relative root does not mean "slightly less durable" — it means every
/// recording written since the last deploy is silently destroyed with no
/// error anywhere. This resolver turns that into a loud, immediate refusal
/// instead.
/// </para>
///
/// <para>
/// <b>Development</b> keeps today's behavior: unset resolves to the
/// historical relative default (<see cref="DevDefaultRoot"/>, next to the
/// binary) so a fresh clone just runs with no configuration.
/// </para>
///
/// <para>
/// <b>Any other environment</b> must configure an ABSOLUTE path (a mounted
/// volume, e.g. <c>/data/audio-blobs</c>). Unset OR relative there both
/// resolve to "not configured" — a relative path would resolve under the
/// app's own directory, which is exactly as ephemeral as no path at all.
/// </para>
///
/// <para>
/// Pure — no IO, no config/host access beyond the two inputs — so it is
/// unit-testable and cheap enough to call once per request (the write
/// endpoint's fail-closed gate) as well as once at startup
/// (<c>Program.cs</c>'s loud warning) and on every <c>/api/health</c> tick.
/// </para>
/// </summary>
public static class AudioBlobStoreRootResolver
{
    /// <summary>The relative default <c>LocalDiskAudioBlobStore</c> has
    /// always used — kept as a plain literal here (rather than a reference
    /// to the Infrastructure type) so this resolver stays a leaf with zero
    /// dependencies; the two are pinned to the same value by test.</summary>
    public const string DevDefaultRoot = "audio-blobs";

    /// <param name="IsConfigured">
    /// False means every endpoint that WRITES an audio blob must refuse
    /// with 503 rather than write somewhere non-durable. Reads of blobs a
    /// store already wrote are unaffected by this type.
    /// </param>
    /// <param name="Root">
    /// The effective root to hand the blob store when
    /// <see cref="IsConfigured"/> is true; <c>null</c> otherwise.
    /// </param>
    /// <param name="Reason">
    /// Human-readable explanation when NOT configured — used for the one
    /// startup log line and the 503 body. <c>null</c> when configured.
    /// </param>
    public readonly record struct Resolution(bool IsConfigured, string? Root, string? Reason);

    public static Resolution Resolve(bool isDevelopment, string? configured)
    {
        var trimmed = configured?.Trim();

        if (isDevelopment)
        {
            return new Resolution(
                IsConfigured: true,
                Root: string.IsNullOrEmpty(trimmed) ? DevDefaultRoot : trimmed,
                Reason: null);
        }

        if (string.IsNullOrEmpty(trimmed))
        {
            return new Resolution(
                IsConfigured: false,
                Root: null,
                Reason: "Audio:BlobStoreRoot is not set in a non-Development environment. " +
                        "Child and assistant voice recordings would be written inside the " +
                        "container and destroyed on the next redeploy. Set " +
                        "Audio__BlobStoreRoot to an absolute path on a durable volume " +
                        "(e.g. /data/audio-blobs).");
        }

        if (!Path.IsPathRooted(trimmed))
        {
            return new Resolution(
                IsConfigured: false,
                Root: null,
                Reason: $"Audio:BlobStoreRoot (\"{trimmed}\") is a relative path in a " +
                        "non-Development environment. A relative path resolves under the " +
                        "app's own directory, which is destroyed on every redeploy just like " +
                        "the unset case. Set Audio__BlobStoreRoot to an absolute path on a " +
                        "durable volume (e.g. /data/audio-blobs).");
        }

        return new Resolution(IsConfigured: true, Root: trimmed, Reason: null);
    }
}

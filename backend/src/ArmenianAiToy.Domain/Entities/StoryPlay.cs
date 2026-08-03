namespace ArmenianAiToy.Domain.Entities;

/// <summary>
/// One story playback that happened on a device — the durable record behind
/// the parent dashboard's "Story plays" list and per-story listen counts.
///
/// <para>
/// <b>Why this exists.</b> Stories play from the toy's SD cache (offline
/// path), so the server never sees the play as a chat request — without this
/// table the dashboard silently under-reports what the child actually heard.
/// The firmware queues play events locally (NVS) and uploads them in batches
/// via <c>POST /api/devices/story-plays</c> whenever Wi-Fi is up, deleting
/// them only after a 2xx — an at-least-once transport, which is why
/// <see cref="ClientEventKey"/> exists as the idempotency key (unique per
/// device; a re-uploaded batch inserts nothing twice).
/// </para>
/// </summary>
public class StoryPlay
{
    public Guid Id { get; set; }

    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;

    /// <summary>Curated story id (e.g. <c>anban-huri</c>). Stored verbatim as
    /// reported; it is a lookup key, never a filesystem path.</summary>
    public string StoryId { get; set; } = string.Empty;

    /// <summary>Where the audio came from on the toy: <c>sd</c> (content-sync
    /// cache), <c>pack</c> (offline SD content pack), or <c>stream</c>
    /// (Wi-Fi stream from the backend). Bounded set, validated at ingest.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>True when playback reached its natural end; false when the
    /// session ended interrupted (barge-in / pause that never resumed).</summary>
    public bool Finished { get; set; }

    /// <summary>Device-generated idempotency key (boot counter + per-boot
    /// sequence). Unique per device — duplicate uploads are safe no-ops.</summary>
    public string ClientEventKey { get; set; } = string.Empty;

    /// <summary>When the play happened, server-stamped at ingest. The toy has
    /// no wall clock: when it reports a relative age (<c>secondsAgo</c>) this
    /// is upload time minus that age; otherwise it is upload time and
    /// <see cref="TimeIsApproximate"/> is true.</summary>
    public DateTime PlayedAtUtc { get; set; }

    /// <summary>True when the toy could not report how long ago the play
    /// happened (e.g. the event survived a reboot), so
    /// <see cref="PlayedAtUtc"/> is really "when the report arrived".</summary>
    public bool TimeIsApproximate { get; set; }
}

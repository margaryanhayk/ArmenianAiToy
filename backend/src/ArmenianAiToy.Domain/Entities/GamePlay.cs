namespace ArmenianAiToy.Domain.Entities;

/// <summary>
/// One OFFLINE game session that happened on a device — the durable record
/// behind the parent dashboard's "Games played" list.
///
/// <para>
/// <b>Why this exists.</b> The three offline games (mind-reader, the
/// two-player buzzer, Button Simon) and the offline quiz run entirely on the
/// toy from SD clips and make no network call at all, so a child could play
/// all afternoon and the dashboard would show nothing. This is the same gap
/// SD story playback had, and the same fix: the firmware queues a tiny record
/// per session in NVS and uploads the queue via
/// <c>POST /api/devices/game-plays</c> whenever Wi-Fi is up, deleting events
/// only after a 2xx. At-least-once transport, which is why
/// <see cref="ClientEventKey"/> exists as the idempotency key (unique per
/// device; a re-uploaded batch inserts nothing twice).
/// </para>
///
/// <para>
/// <b>Game honesty is in the schema, not only in the prose.</b> These games
/// have no microphone — the toy can only report what the BUTTONS measured.
/// There is deliberately no free-text column, no transcript, no child
/// utterance and no "what the child said" field anywhere on this row, so a
/// future caller cannot quietly start storing one. Every field below is a
/// count, a timestamp, or a value from a closed vocabulary.
/// </para>
/// </summary>
public class GamePlay
{
    public Guid Id { get; set; }

    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;

    /// <summary>Which game, from the bounded vocabulary in
    /// <c>GamePlayReportRequest.AllowedGameKeys</c>. Same key the game's clip
    /// directory uses (<c>/games/&lt;key&gt;/</c>) and the same key
    /// <c>ContentSync:Games</c> ships, so entitlement, delivery and reporting
    /// cannot disagree about what a game is called. Validated at ingest — an
    /// unknown key is rejected, never stored.</summary>
    public string GameKey { get; set; } = string.Empty;

    /// <summary>Device-generated idempotency key (boot counter + per-boot
    /// sequence). Unique per device — duplicate uploads are safe no-ops.</summary>
    public string ClientEventKey { get; set; } = string.Empty;

    /// <summary>When the session happened, server-stamped at ingest. The toy
    /// has no wall clock: when it reports a relative age (<c>secondsAgo</c>)
    /// this is upload time minus that age; otherwise it is upload time and
    /// <see cref="TimeIsApproximate"/> is true.</summary>
    public DateTime PlayedAtUtc { get; set; }

    /// <summary>True when the toy could not report how long ago the session
    /// happened (e.g. the event survived a reboot), so
    /// <see cref="PlayedAtUtc"/> is really "when the report arrived".</summary>
    public bool TimeIsApproximate { get; set; }

    /// <summary>How many rounds the session ran, when the game counts rounds
    /// (the buzzer runs five; the mind-reader walks its tree). Null when the
    /// toy did not report it — null means "not measured", never zero.</summary>
    public int? Rounds { get; set; }

    /// <summary>How the session ended, from the bounded vocabulary in
    /// <c>GamePlayReportRequest.AllowedOutcomes</c>: <c>won</c> / <c>lost</c>
    /// / <c>stopped</c>. Null when the toy reported nothing, or reported a
    /// value outside the vocabulary. This describes the SESSION, not the
    /// child — see the dashboard's describe-never-grade rule.</summary>
    public string? Outcome { get; set; }

    /// <summary>Button Simon's longest echoed sequence length. Null for every
    /// other game, and null when the toy did not report one. It is a
    /// description of what happened, not a score to beat: nothing in this
    /// product ranks it, tracks a best, or compares it with last week.</summary>
    public int? Score { get; set; }
}

namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Body for <c>POST /api/devices/game-plays</c> — the toy's store-and-forward
/// batch upload of OFFLINE game sessions. Same transport contract as
/// <see cref="StoryPlayReportRequest"/>: at-least-once (the firmware deletes
/// queued events only after a 2xx), so every event carries a device-generated
/// idempotency <see cref="GamePlayReportEvent.Key"/> and re-uploading the same
/// batch inserts nothing twice.
/// </summary>
public sealed record GamePlayReportRequest(List<GamePlayReportEvent>? Events)
{
    /// <summary>Upper bound on events accepted per upload. Matches the
    /// story-play cap; the firmware queue is far smaller.</summary>
    public const int MaxEvents = 32;

    /// <summary>
    /// The games a toy may report. These are the SAME keys the clips ship
    /// under (<c>ContentSync:Games[].GameKey</c>) and the same names the
    /// firmware uses for the SD directory <c>/games/&lt;key&gt;/</c> — one
    /// name per game across entitlement, delivery and reporting, so a second
    /// spelling can never appear. An event naming anything else is rejected
    /// rather than stored: an unbounded key would let a device write
    /// arbitrary strings onto a parent-facing screen.
    /// <para>
    /// <c>story-pauses</c> is deliberately absent — it ships in the same clip
    /// bank but is a mid-story interlude, not a game a child chooses to play.
    /// </para>
    /// </summary>
    public static readonly string[] AllowedGameKeys =
    [
        "mind-reader",
        "who-first",
        "button-simon",
        "sound-detective",
    ];

    /// <summary>
    /// How a session ended. Bounded for the same reason metric tags are:
    /// a parent-facing label must come from a closed set, not from a string a
    /// device chose. Anything else — including a value the toy invents — is
    /// normalized to null ("the toy did not say"), which is honest, rather
    /// than kept verbatim.
    /// </summary>
    /// <summary>
    /// Bounded outcome vocabulary. Nullable on purpose, and the null case is a
    /// PRODUCT RULE rather than missing data:
    /// <list type="bullet">
    /// <item><b>who-first</b> (the two-player buzzer) MUST send null. The round
    /// is about speed, the toy addresses colours rather than names, and it never
    /// announces a loser — so there is no outcome it is allowed to report. A
    /// firmware reporter that sends "won" here would be inventing a loser.</item>
    /// <item><b>mind-reader</b> reports from AREG's side: "lost" means the toy
    /// failed to guess, which means the CHILD won. The dashboard must never
    /// render that token as the child losing.</item>
    /// <item><b>button-simon</b> / <b>sound-detective</b> report the child's own
    /// round honestly, with Score carrying the length reached.</item>
    /// </list>
    /// Every one of these is a thing the buttons actually measured — see
    /// § Game honesty in CLAUDE.md. There is deliberately no free-text column
    /// on GamePlay, so a toy with no microphone cannot claim it heard anything.
    /// </summary>
    public static readonly string[] AllowedOutcomes = ["won", "lost", "stopped"];

    /// <summary>Sanity ceiling on the two reported counts. Both are small by
    /// construction (the buzzer runs 5 rounds, Simon's ceiling is 6), so a
    /// value past this is a bug or a hostile device, not a long game. Out of
    /// range collapses to null — the session still happened, and dropping the
    /// whole row would hide it.</summary>
    public const int MaxCount = 1000;
}

/// <summary>
/// One reported session. Every field is a count, a bounded token, or a
/// timestamp — <b>there is no free-text field and must never be one</b>. The
/// offline games have no microphone, so the toy can report only what the
/// buttons measured; a transcript here would be a claim it cannot make.
/// <para>
/// <paramref name="SecondsAgo"/> is the toy's best-effort relative age of the
/// event (it has no wall clock): present when the event was produced in the
/// current boot session, absent when it survived a reboot — in that case the
/// server stamps upload time and marks the row approximate.
/// </para>
/// </summary>
public sealed record GamePlayReportEvent(
    string? Key,
    string? GameKey,
    int? Rounds = null,
    string? Outcome = null,
    int? Score = null,
    long? SecondsAgo = null);

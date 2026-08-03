namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Body for <c>POST /api/devices/story-plays</c> — the toy's store-and-forward
/// batch upload of story playback events. Transport is at-least-once (the
/// firmware deletes queued events only after a 2xx), so every event carries a
/// device-generated idempotency <see cref="StoryPlayReportEvent.Key"/>;
/// re-uploading the same batch inserts nothing twice.
/// </summary>
public sealed record StoryPlayReportRequest(List<StoryPlayReportEvent>? Events)
{
    /// <summary>Upper bound on events accepted per upload. The firmware queue
    /// is far smaller (~16); the cap just bounds request cost.</summary>
    public const int MaxEvents = 32;
}

/// <summary>
/// One reported playback. <paramref name="SecondsAgo"/> is the toy's
/// best-effort relative age of the event (it has no wall clock): present when
/// the event was produced in the current boot session, absent when it
/// survived a reboot — in that case the server stamps upload time and marks
/// the row approximate.
/// </summary>
public sealed record StoryPlayReportEvent(
    string? Key,
    string? StoryId,
    bool Finished = false,
    string? Source = null,
    long? SecondsAgo = null);

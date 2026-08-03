namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Response for <c>GET /api/parents/devices/{deviceId}/story-plays</c> — the
/// parent dashboard's "Story plays" list. <see cref="Plays"/> is the paginated
/// newest-first play history; <see cref="Totals"/> is the UNpaginated
/// per-story listen counter (drives the library view's counts), most-listened
/// first; <see cref="Total"/> is the full play-row count for pagination.
/// No child id, no audio path — same privacy discipline as the Today summary.
/// </summary>
public sealed record StoryPlaysResponse(
    List<StoryPlayDto> Plays,
    List<StoryPlayTotalDto> Totals,
    int Total);

/// <summary>One playback row. <see cref="TimeIsApproximate"/> is true when
/// the toy was offline at play time and only the upload time is known.</summary>
public sealed record StoryPlayDto(
    string StoryId,
    bool Finished,
    string Source,
    DateTime PlayedAtUtc,
    bool TimeIsApproximate);

/// <summary>Per-story listen counter across the device's whole history.</summary>
public sealed record StoryPlayTotalDto(
    string StoryId,
    int Count,
    int FinishedCount);

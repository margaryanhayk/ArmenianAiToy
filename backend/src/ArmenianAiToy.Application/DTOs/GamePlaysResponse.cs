namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Response for <c>GET /api/parents/devices/{deviceId}/game-plays</c> — the
/// parent dashboard's "Games played" list. <see cref="Plays"/> is the
/// paginated newest-first session history; <see cref="Totals"/> is the
/// UNpaginated per-game play counter, most-played first; <see cref="Total"/>
/// is the full row count for pagination. Exact mirror of
/// <see cref="StoryPlaysResponse"/>.
/// <para>
/// No child id, no free text — the wire carries only what the buttons
/// measured, same discipline as the entity.
/// </para>
/// </summary>
public sealed record GamePlaysResponse(
    List<GamePlayDto> Plays,
    List<GamePlayTotalDto> Totals,
    int Total);

/// <summary>One session row. <see cref="TimeIsApproximate"/> is true when the
/// toy was offline at play time and only the upload time is known.
/// <see cref="Rounds"/> / <see cref="Outcome"/> / <see cref="Score"/> are all
/// nullable on purpose: null means the toy did not measure it, which is a
/// different statement from zero.</summary>
public sealed record GamePlayDto(
    string GameKey,
    int? Rounds,
    string? Outcome,
    int? Score,
    DateTime PlayedAtUtc,
    bool TimeIsApproximate);

/// <summary>
/// Per-game play counter across the device's whole history.
/// <para>
/// Deliberately a plain COUNT and nothing else — no best score, no longest
/// streak, no win rate. This screen describes what a child played; it does
/// not rank it, and a "best" column is the first step towards a leaderboard
/// a four-year-old can lose at.
/// </para>
/// </summary>
public sealed record GamePlayTotalDto(
    string GameKey,
    int Count);

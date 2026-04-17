namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Per-conversation Game Mode v2 loop state. Mirrors the RiddleSessionState
/// pattern: 30-min expiry, ConcurrentDictionary keyed by conversation id.
///
/// CurrentRound is null between activities (after stop / switch). LastGameType
/// + RecentGameTypes drive variety so the model is told to AVOID the last
/// few categories on a fresh round.
/// </summary>
public record GameSessionState(
    GameRound? CurrentRound,
    IReadOnlyList<string> RecentGameTypes,
    DateTime UpdatedAt);

/// <summary>
/// One active game activity. Created when the model emits a GAME_TYPE tail
/// block; cleared when the child stops or asks for a different game.
/// TurnsCompleted bumps on each Continue turn and drives a small difficulty
/// progression (1 → 2 after 2 clean turns, 2 → 3 after 5 total).
/// </summary>
public record GameRound(
    string GameType,
    int Difficulty,
    int TurnsCompleted);

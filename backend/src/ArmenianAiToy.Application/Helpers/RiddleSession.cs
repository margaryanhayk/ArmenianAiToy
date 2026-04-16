namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Per-conversation Riddle Mode v2 loop state. Lives in-memory in
/// ChatService.RiddleSessions. Mirrors the StoryMemory pattern:
/// 30-min expiry, ConcurrentDictionary keyed by conversation id.
///
/// CurrentRound is null between riddles (after a correct answer or a
/// reveal). RecentCategories is a small ring buffer used to ask the
/// model to vary categories across turns. LastDifficulty drives a
/// gentle progression: +1 on a clean correct, -1 on reveal, hold on
/// hinted-correct.
/// </summary>
public record RiddleSessionState(
    RiddleRound? CurrentRound,
    int LastDifficulty,
    IReadOnlyList<string> RecentCategories,
    DateTime UpdatedAt);

/// <summary>
/// One active riddle. Created when the model emits a RIDDLE_ANSWER
/// tail block; cleared when the child answers correctly, gives up,
/// or burns through both hints.
/// </summary>
public record RiddleRound(
    string AnswerNormalized,
    string AnswerDisplay,
    string Category,
    int Difficulty,
    int HintsUsed);

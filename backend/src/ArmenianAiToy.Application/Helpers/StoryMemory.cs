namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Compact per-conversation story state tracked across turns.
/// Fields may be null if not yet established in the story.
/// </summary>
public record StoryMemory(
    string? Character,
    string? Place,
    string? ImportantObject,
    string? CurrentSituation,
    string? Mood,
    DateTime UpdatedAt);

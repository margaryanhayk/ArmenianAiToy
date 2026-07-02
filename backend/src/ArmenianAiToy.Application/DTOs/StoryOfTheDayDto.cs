namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// The deterministic "story of the day" for a caller's local date and
/// time-of-day bucket. The toy fetches this to learn WHICH story to play
/// today, then streams the audio via the existing story-audio path using
/// <see cref="StoryId"/>. Carries only story metadata (no content, no
/// audio bytes).
/// </summary>
public record StoryOfTheDayDto(
    string PartOfDay,
    string StoryId,
    string Title,
    int SegmentCount,
    bool BedtimeSafe,
    DateTime AsOfLocal,
    string TimeZoneId,
    bool TimeZoneResolved);

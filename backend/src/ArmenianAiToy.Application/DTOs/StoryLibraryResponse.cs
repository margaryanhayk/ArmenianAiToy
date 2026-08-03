namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Response for <c>GET /api/parents/stories</c> — the parent-facing story
/// library ("what can my toy play, and what is each story about").
/// Sourced from the shipped ContentSync set joined with the curated
/// library's metadata (author / goal / lesson from B1) and the caller's
/// own listen counts (Slice A story plays across their linked devices).
/// No child id, no segment text, no server paths on the wire.
/// </summary>
public sealed record StoryLibraryResponse(List<StoryLibraryItemDto> Stories);

public sealed record StoryLibraryItemDto(
    string StoryId,
    string Title,
    string? Author,
    string? Goal,
    string? Lesson,
    bool? BedtimeSafe,
    int? MinAge,
    int? MaxAge,
    int SegmentCount,
    List<string> ReflectionQuestions,
    int ListenCount,
    int FinishedCount,
    /// <summary>Parent-authed preview stream URL, or null when no audio is
    /// shipped for this story (metadata-only entry).</summary>
    string? PreviewUrl,
    /// <summary>Reflection-dialogue takeaway lines paired 1:1 with
    /// <c>ReflectionQuestions</c> (null for stories without authored
    /// conclusions). Drives the library card's "Discuss with your child"
    /// block — the same guide the toy speaks.</summary>
    List<string>? ReflectionConclusions = null);

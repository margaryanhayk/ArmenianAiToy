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
    List<string>? ReflectionConclusions = null,
    /// <summary>Serial support — the series this story is an EPISODE of and
    /// its 1-based position, straight off the content manifest (already
    /// validated as a pair there). Both null for a standalone story, which
    /// is what the dashboard keys off to decide whether a card belongs in a
    /// series group. Optional trailing parameters so every existing
    /// construction site compiles unchanged.
    /// <para>
    /// Null on the metadata-only fallback branch too (content sync
    /// disabled): the curated library carries no series metadata, and
    /// inventing one from the story id would be a guess.
    /// </para></summary>
    string? SeriesId = null,
    int? SeriesIndex = null,
    /// <summary>The series' authored display name, used as the series
    /// card's headline. Null when unconfigured — the dashboard then falls
    /// back to a generic descriptor, because a slug or a name guessed from
    /// the episode titles would be worse than not naming it.</summary>
    string? SeriesTitle = null,
    /// <summary>Parent-facing translations of title / goal / lesson, keyed by
    /// language code ("en", "ru"). The dashboard switches language without
    /// refetching, so every language ships on the wire and the client picks;
    /// a missing language falls back to the Armenian fields above.
    /// <para>
    /// Parent app only. Nothing in here is ever spoken or sent to the toy —
    /// the child hears Armenian.
    /// </para></summary>
    Dictionary<string, StoryTextsDto>? Translations = null);

/// <summary>One language's parent-facing text for a story. Any field may be
/// null; the dashboard falls back to the Armenian for that field alone, so a
/// half-written translation degrades one line rather than the card.</summary>
public sealed record StoryTextsDto(string? Title, string? Goal, string? Lesson);

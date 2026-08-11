namespace ArmenianAiToy.Application.Stories;

/// <summary>
/// A pre-written, human-reviewed Armenian story asset for the curated
/// story library. Every child-facing string on this record (title,
/// segments, reflection, questions) is reviewed offline — the runtime
/// serves it verbatim with no model involvement.
/// <para>
/// This slice is NOT wired into the live chat/audio flow. The library
/// and its session tracker are internal building blocks; routing,
/// gating, and MODES.md contract changes belong to later slices.
/// </para>
/// </summary>
/// <param name="Id">Stable library key (lowercase-kebab ASCII id —
/// never shown to the child).</param>
/// <param name="Title">Armenian title.</param>
/// <param name="MinAge">Inclusive lower age bound.</param>
/// <param name="MaxAge">Inclusive upper age bound.</param>
/// <param name="Tone">Authoring tone tag (e.g. "warm").</param>
/// <param name="Segments">Ordered TTS-sized beats, indexes 0..N-1.</param>
/// <param name="ReflectionText">One gentle storyteller sentence for the
/// story's end — pre-written, never a lecture.</param>
/// <param name="ReflectionQuestions">Up to two child-friendly closing
/// questions. A future serving site MUST suppress these when
/// bedtime-adjacent (Calm forbids questions).</param>
/// <param name="BedtimeSafe">True when every segment is free of
/// startles/spikes and the story may surface near the bedtime
/// window.</param>
/// <summary>One language's parent-facing text for a story. Parent app only —
/// never spoken, never rendered to audio, never sent to the toy.</summary>
public sealed record CuratedStoryTexts(string? Title, string? Goal, string? Lesson);

public sealed record CuratedStory(
    string Id,
    string Title,
    int MinAge,
    int MaxAge,
    string Tone,
    IReadOnlyList<CuratedStorySegment> Segments,
    string ReflectionText,
    IReadOnlyList<string> ReflectionQuestions,
    bool BedtimeSafe)
{
    /// <summary>Optional author attribution (Armenian). Null = unknown /
    /// unverified / in-project original — the spoken intro then omits the
    /// author line. Never guessed: a wrong attribution spoken to a child is
    /// worse than none.</summary>
    public string? Author { get; init; }

    /// <summary>Optional parent-facing purpose (Armenian, one sentence).
    /// Library card only; not spoken.</summary>
    public string? Goal { get; init; }

    /// <summary>Optional "what the story teaches" (Armenian, 1–2 warm
    /// sentences). Library card + source text for the toy's after-story
    /// summary clip. Toy-spoken content: owner listen test gates any render.</summary>
    public string? Lesson { get; init; }

    /// <summary>Optional per-question takeaway lines for the reflection
    /// dialogue, paired 1:1 with <see cref="ReflectionQuestions"/> (the
    /// parser enforces equal length). Spoken by the toy after it reacts to
    /// the child's answer to that question; also shown on the parent
    /// library card. Null = no conclusions authored (the dialogue then
    /// closes with the reaction alone).</summary>
    public IReadOnlyList<string>? ReflectionConclusions { get; init; }

    /// <summary>Parent-facing translations of title / goal / lesson, keyed by
    /// language code. Null when none are authored — the parent app then shows
    /// the Armenian. Never spoken: the child hears Armenian, always.</summary>
    public IReadOnlyDictionary<string, CuratedStoryTexts>? Translations { get; init; }
}

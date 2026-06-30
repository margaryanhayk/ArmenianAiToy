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
/// <param name="Conclusions">Optional pool of 1–4 pre-written closing
/// "meaning" lines for the story's end. When null/empty the single
/// <see cref="ReflectionText"/> is used, so old story files (and every
/// caller that does not pass this) keep their existing one-line
/// ending.</param>
public sealed record CuratedStory(
    string Id,
    string Title,
    int MinAge,
    int MaxAge,
    string Tone,
    IReadOnlyList<CuratedStorySegment> Segments,
    string ReflectionText,
    IReadOnlyList<string> ReflectionQuestions,
    bool BedtimeSafe,
    IReadOnlyList<string>? Conclusions = null)
{
    /// <summary>The conclusion pool to pick the story's ending from.
    /// Uses the authored <see cref="Conclusions"/> list when present,
    /// otherwise falls back to the single <see cref="ReflectionText"/> —
    /// so every story always has at least one ending and pre-existing
    /// story files are unaffected.</summary>
    public IReadOnlyList<string> EffectiveConclusions =>
        Conclusions is { Count: > 0 } ? Conclusions : [ReflectionText];

    /// <summary>Deterministically picks ONE conclusion from
    /// <see cref="EffectiveConclusions"/> for the given variant seed, so
    /// the ending varies across plays without randomness. Pure: the same
    /// seed always yields the same conclusion (testable). Negative seeds
    /// are handled via a safe positive modulo.</summary>
    public string SelectConclusion(int variant)
    {
        var pool = EffectiveConclusions;
        var index = ((variant % pool.Count) + pool.Count) % pool.Count;
        return pool[index];
    }
}

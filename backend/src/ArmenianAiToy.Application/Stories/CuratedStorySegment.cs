namespace ArmenianAiToy.Application.Stories;

/// <summary>
/// One TTS-sized beat of a curated story. <see cref="Text"/> is the
/// exact child-facing Armenian text — it is delivered verbatim, never
/// paraphrased or post-processed by a model. Authoring rules: 2–4
/// short Eastern Armenian sentences, Armenian punctuation only
/// («։» enders), zero Latin codepoints, zero digits, ends on a
/// complete beat.
/// </summary>
/// <param name="Index">Zero-based position within the story.</param>
/// <param name="Text">Verbatim Armenian segment text.</param>
public sealed record CuratedStorySegment(int Index, string Text);

namespace ArmenianAiToy.Application.Audio;

/// <summary>
/// Composes the text handed to TTS for the C1 voice Story path.
/// The toy has no screen, so the child must hear the two story
/// choices spoken aloud; appending a short Armenian bridge after
/// the 3–5 sentence story opening is how the choice handoff
/// surfaces on voice.
/// <para>
/// The composer is a pure function with no DI, no logger, no
/// state. It runs after <c>ChatService</c> has already stripped
/// the tail block from <c>Message.Content</c>: the persisted
/// canonical text stays story-only, and only the bytes handed to
/// <c>IAudioSynthesisService.SynthesizeArmenianAsync</c> grow the
/// spoken-choice bridge.
/// </para>
/// <para>
/// Spoken-choice bridge phrasing is
/// «Ի՞նչ անենք՝ առաջինը՝ {A}, թե՞ երկրորդը՝ {B}։» — the ordinals
/// «առաջինը» / «երկրորդը» are used instead of bare letters «ա» /
/// «բ» because TTS renders isolated single-letter tokens with
/// abrupt prosody. The trailing Armenian full stop «։» is emitted
/// exactly once even when a choice label already ends with one.
/// </para>
/// </summary>
public static class AudioStoryResponseComposer
{
    /// <summary>Armenian punctuation trimmed off each choice label
    /// before composing so we don't double up enders. Covers the
    /// Armenian full stop, the Armenian exclamation/emphasis mark,
    /// the Armenian question mark, and ASCII equivalents that
    /// sometimes slip through.</summary>
    private static readonly char[] TrailingPunctuation =
    [
        '։', // «։» Armenian full stop
        '՜', // «՜» Armenian exclamation mark
        '՞', // «՞» Armenian question mark
        '.', '!', '?',
        ' ', '\t', '\r', '\n',
    ];

    /// <summary>
    /// When <paramref name="mode"/> is Story (case-insensitive) and both
    /// choice labels are non-empty, return
    /// <c>{storyText}\n\n«Ի՞նչ անենք՝ առաջինը՝ {A}, թե՞ երկրորդը՝ {B}։»</c>.
    /// In every other case (non-Story mode, either choice null / empty,
    /// null <paramref name="mode"/>) return <paramref name="storyText"/>
    /// verbatim.
    /// </summary>
    public static string ComposeTtsText(
        string? storyText,
        string? choiceA,
        string? choiceB,
        string? mode)
    {
        var baseText = storyText ?? string.Empty;

        if (!IsStoryMode(mode)) return baseText;

        var trimmedA = TrimTrailing(choiceA);
        var trimmedB = TrimTrailing(choiceB);
        if (string.IsNullOrWhiteSpace(trimmedA) || string.IsNullOrWhiteSpace(trimmedB))
            return baseText;

        var bridge = $"Ի՞նչ անենք՝ առաջինը՝ {trimmedA}, թե՞ երկրորդը՝ {trimmedB}։";

        if (string.IsNullOrWhiteSpace(baseText)) return bridge;
        return baseText + "\n\n" + bridge;
    }

    private static bool IsStoryMode(string? mode) =>
        !string.IsNullOrWhiteSpace(mode)
        && string.Equals(mode.Trim(), "story", System.StringComparison.OrdinalIgnoreCase);

    private static string TrimTrailing(string? choice)
    {
        if (string.IsNullOrWhiteSpace(choice)) return string.Empty;
        return choice.TrimEnd(TrailingPunctuation);
    }
}

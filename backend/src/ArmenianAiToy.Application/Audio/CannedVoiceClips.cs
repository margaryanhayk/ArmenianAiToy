using System.Collections.Concurrent;

namespace ArmenianAiToy.Application.Audio;

/// <summary>
/// Tiny in-memory cache of the three server-side gated-path audio
/// clips. First access renders via <see cref="IAudioSynthesisService"/>
/// and keeps the result for the process lifetime; subsequent gated
/// hits serve bytes from memory with zero upstream cost.
/// <para>
/// Pre-rendered audio files on disk were the alternative; for C1 the
/// lazy-render approach avoids committing binary audio assets while
/// still keeping the runtime cost of a gated request tiny after the
/// first warm-up. A later phase can swap this for committed on-disk
/// clips without changing any caller.
/// </para>
/// <para>
/// Armenian copy intentionally kept short, warm, non-technical.
/// Identical child-facing strings as the shipped text path
/// (<c>ChatController.PausedResponse</c> /
/// <c>ChatController.ModeDisabledResponse</c>) for the cases where
/// those exist; the bedtime clip reuses the paused copy to mirror
/// the text path's "same canned reply, metric distinguishes which
/// gate fired" convention.
/// </para>
/// </summary>
public sealed class CannedVoiceClips
{
    public const string PausedKey = "paused";
    public const string BedtimeKey = "bedtime";
    public const string ModeDisabledKey = "mode_disabled";

    private static readonly IReadOnlyDictionary<string, string> Texts =
        new Dictionary<string, string>
        {
            // «Հիմա հանգստանում եմ։ Ծնողդ կարող է նորից միացնել։»
            [PausedKey] = "Հիմա հանգստանում եմ։ Ծնողդ կարող է նորից միացնել։",
            // Same text as paused — matches the text path's convention
            // where paused and bedtime share the canned response body
            // and only the metric tag distinguishes them.
            [BedtimeKey] = "Հիմա հանգստանում եմ։ Ծնողդ կարող է նորից միացնել։",
            // Matches ChatController.ModeDisabledResponse exactly.
            [ModeDisabledKey] = "Եկ մի ուրիշ բան փորձենք։"
        };

    private readonly IAudioSynthesisService _tts;
    private readonly ConcurrentDictionary<string, AudioSynthesisResult> _cache = new();

    public CannedVoiceClips(IAudioSynthesisService tts)
    {
        _tts = tts;
    }

    public async Task<AudioSynthesisResult> GetAsync(
        string key, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(key, out var cached))
            return cached;
        if (!Texts.TryGetValue(key, out var text))
            throw new ArgumentOutOfRangeException(nameof(key), key,
                "Unknown canned-clip key.");
        // Render outside the dictionary lock — duplicate renders on a
        // parallel first-hit race are benign (both callers get the same
        // audio bytes; only one wins the cache slot). Avoids holding a
        // lock across an async TTS call.
        var rendered = await _tts.SynthesizeArmenianAsync(text, cancellationToken);
        _cache.TryAdd(key, rendered);
        return _cache[key];
    }
}

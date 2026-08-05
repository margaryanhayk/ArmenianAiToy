namespace ArmenianAiToy.Application.Audio;

/// <summary>
/// Optional streaming capability on top of <see cref="IAudioSynthesisService"/>.
/// A provider that can begin returning audio before synthesis completes
/// implements this too; callers feature-detect with an <c>is</c> check.
/// The measured point (2026-08-05): eleven_v3's first audio byte arrives
/// in ~0.9-1.4 s while the complete file takes 3-5 s — for a toy that can
/// play-while-downloading, the perceived TTS latency is the FIRST number.
/// </summary>
public interface IStreamingAudioSynthesisService
{
    /// <summary>
    /// Begin synthesis and return the provider's live audio stream.
    /// The caller owns the stream and must dispose it; bytes are readable
    /// as they are generated.
    /// </summary>
    Task<AudioSynthesisStreamResult> SynthesizeArmenianStreamAsync(
        string text, CancellationToken cancellationToken = default);
}

/// <summary>A live synthesis stream plus its MIME type.</summary>
public sealed record AudioSynthesisStreamResult(
    Stream AudioStream,
    string MimeType) : IDisposable
{
    public void Dispose() => AudioStream.Dispose();
}

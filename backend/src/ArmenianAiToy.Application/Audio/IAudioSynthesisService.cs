namespace ArmenianAiToy.Application.Audio;

/// <summary>
/// Voice-out seam for C1. Takes Armenian assistant text and returns
/// the rendered audio + its MIME type. Keeps text canonical internally
/// — the caller hands <c>ChatService</c>'s textual response into this
/// seam verbatim.
/// <para>
/// Production implementation wraps OpenAI TTS (<c>tts-1</c>) with a
/// warm narrator-style voice. Test doubles return a static byte array
/// with a fixed MIME type so no unit test reaches the network.
/// </para>
/// </summary>
public interface IAudioSynthesisService
{
    Task<AudioSynthesisResult> SynthesizeArmenianAsync(
        string text,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Rendered audio plus its MIME type. The MIME type is what the HTTP
/// response carries back to the device and what the blob store writes
/// alongside the blob on disk.
/// </summary>
public sealed record AudioSynthesisResult(
    byte[] Content,
    string MimeType);

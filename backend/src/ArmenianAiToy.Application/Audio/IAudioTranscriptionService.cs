namespace ArmenianAiToy.Application.Audio;

/// <summary>
/// Voice-in seam for C1. Takes an audio stream (WAV / MP3 / whatever
/// the device uploads) and returns the Armenian transcript. The text
/// becomes the canonical <c>Message.Content</c> that the existing
/// <c>ChatService</c> pipeline moderates, mode-detects, and persists —
/// audio is never consumed by ChatService directly.
/// <para>
/// Production implementation wraps OpenAI Whisper with
/// <c>Language = "hy"</c>. Test doubles return canned Armenian strings
/// so no unit test reaches the network.
/// </para>
/// </summary>
public interface IAudioTranscriptionService
{
    /// <summary>
    /// Transcribe the given audio to Armenian text. Empty string return
    /// means "no usable speech recognized" — the caller treats that as
    /// a non-fatal failure (returns the warm canned clip, writes
    /// nothing to the DB). Exceptions propagate to the controller's
    /// failure path, which renders a canned clip as the 502 body.
    /// </summary>
    Task<string> TranscribeArmenianAsync(
        Stream audio,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// As <see cref="TranscribeArmenianAsync(Stream,string,CancellationToken)"/>,
    /// but biases decoding with <paramref name="prompt"/> — short Armenian
    /// context (e.g. the current story scene) that primes the model toward
    /// the proper nouns and vocabulary actually in play. This is the main
    /// lever for a child's short / single-word Armenian utterances, which
    /// Whisper otherwise mangles. A null / empty prompt behaves exactly
    /// like the 3-argument overload.
    /// </summary>
    Task<string> TranscribeArmenianAsync(
        Stream audio,
        string contentType,
        string? prompt,
        CancellationToken cancellationToken = default);
}

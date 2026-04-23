using ArmenianAiToy.Application.Audio;
using Microsoft.Extensions.Logging;
using OpenAI.Audio;

namespace ArmenianAiToy.Infrastructure.Audio;

/// <summary>
/// Production <see cref="IAudioTranscriptionService"/> backed by
/// OpenAI Whisper. Forces <c>Language = "hy"</c> so the model does not
/// auto-detect into a neighbouring language on short or noisy
/// utterances. Returns <see cref="AudioTranscriptionFormat.Text"/> —
/// the controller only needs the transcript, not timestamps.
/// <para>
/// Wraps the already-resolved <see cref="AudioClient"/> (same OpenAI
/// SDK the chat + moderation adapters already use). No new NuGet
/// dependency, no new auth config — reuses <c>OpenAI:ApiKey</c>.
/// </para>
/// </summary>
public sealed class OpenAIWhisperTranscriptionService : IAudioTranscriptionService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly AudioClient _client;
    private readonly ILogger<OpenAIWhisperTranscriptionService> _logger;

    public OpenAIWhisperTranscriptionService(
        AudioClient client, ILogger<OpenAIWhisperTranscriptionService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<string> TranscribeArmenianAsync(
        Stream audio, string contentType, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(RequestTimeout);

        var filename = ResolveFilenameHint(contentType);
        var options = new AudioTranscriptionOptions
        {
            Language = "hy",
            ResponseFormat = AudioTranscriptionFormat.Text
        };

        var result = await _client.TranscribeAudioAsync(audio, filename, options, cts.Token);
        var text = result.Value?.Text ?? string.Empty;
        _logger.LogInformation(
            "Whisper transcription completed: bytes_hint={FilenameHint} chars={TranscriptChars}",
            filename, text.Length);
        return text;
    }

    /// <summary>
    /// The SDK requires a filename hint so the multipart upload has a
    /// well-formed <c>Content-Disposition</c> extension — the actual
    /// bytes are what Whisper inspects. Map the most common inbound
    /// content types to matching file extensions; fall back to .wav
    /// (our device-side default).
    /// </summary>
    private static string ResolveFilenameHint(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return "audio.wav";
        var lower = contentType.Trim().ToLowerInvariant();
        if (lower.StartsWith("audio/wav") || lower.StartsWith("audio/x-wav"))
            return "audio.wav";
        if (lower.StartsWith("audio/mpeg") || lower.StartsWith("audio/mp3"))
            return "audio.mp3";
        if (lower.StartsWith("audio/ogg") || lower.StartsWith("audio/opus"))
            return "audio.ogg";
        if (lower.StartsWith("audio/webm"))
            return "audio.webm";
        if (lower.StartsWith("audio/m4a") || lower.StartsWith("audio/mp4"))
            return "audio.m4a";
        if (lower.StartsWith("audio/flac"))
            return "audio.flac";
        return "audio.wav";
    }
}

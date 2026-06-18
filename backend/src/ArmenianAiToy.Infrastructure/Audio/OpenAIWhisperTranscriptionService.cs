using System.Collections.Concurrent;
using ArmenianAiToy.Application.Audio;
using Microsoft.Extensions.Logging;
using OpenAI;
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

    // Optional per-call model override seam (latency). The constructor's
    // injected <see cref="AudioClient"/> is the configured DEFAULT model
    // (OpenAI:TranscriptionModel, "whisper-1") and is used whenever no
    // override is requested — so existing behavior is unchanged. When a
    // caller (the in-story Q&A path) passes a different model name, we
    // resolve a dedicated AudioClient for it lazily and cache it for the
    // process lifetime. _factory is null when this service was constructed
    // with only a baked client (e.g. tests / legacy wiring): in that case
    // any override request transparently falls back to the default client.
    private readonly OpenAIClient? _factory;
    private readonly string? _defaultModel;
    private readonly ConcurrentDictionary<string, AudioClient> _byModel = new();

    public OpenAIWhisperTranscriptionService(
        AudioClient client, ILogger<OpenAIWhisperTranscriptionService> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Wiring overload that also keeps the <see cref="OpenAIClient"/> so a
    /// per-call <c>model</c> override (the in-story Q&amp;A latency seam) can
    /// resolve a dedicated <see cref="AudioClient"/>. <paramref name="defaultModel"/>
    /// is the model baked into <paramref name="client"/>; an override equal
    /// to it (or null) reuses the default client and changes nothing.
    /// </summary>
    public OpenAIWhisperTranscriptionService(
        AudioClient client, OpenAIClient factory, string defaultModel,
        ILogger<OpenAIWhisperTranscriptionService> logger)
        : this(client, logger)
    {
        _factory = factory;
        _defaultModel = defaultModel;
    }

    public Task<string> TranscribeArmenianAsync(
        Stream audio, string contentType, CancellationToken cancellationToken = default)
        => TranscribeArmenianAsync(audio, contentType, prompt: null, cancellationToken);

    public async Task<string> TranscribeArmenianAsync(
        Stream audio, string contentType, string? prompt,
        CancellationToken cancellationToken = default, string? model = null)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(RequestTimeout);

        var client = ResolveClient(model);
        var filename = ResolveFilenameHint(contentType);
        var options = new AudioTranscriptionOptions
        {
            Language = "hy",
            ResponseFormat = AudioTranscriptionFormat.Text
        };
        // Bias decoding toward the supplied context (e.g. the current story
        // scene), which sharply improves short / single-word Armenian — the
        // model's weakest case. Whisper weights the END of the prompt most,
        // and the caller already places the most-relevant text last.
        if (!string.IsNullOrWhiteSpace(prompt))
        {
            options.Prompt = prompt;
        }

        var result = await client.TranscribeAudioAsync(audio, filename, options, cts.Token);
        var text = result.Value?.Text ?? string.Empty;
        _logger.LogInformation(
            "Whisper transcription completed: bytes_hint={FilenameHint} chars={TranscriptChars} biased={Biased} model={Model}",
            filename, text.Length, !string.IsNullOrWhiteSpace(prompt),
            string.IsNullOrWhiteSpace(model) ? _defaultModel ?? "(default)" : model);
        return text;
    }

    /// <summary>Picks the <see cref="AudioClient"/> for a transcription call.
    /// Returns the default (constructor-injected) client when no override is
    /// requested, when the override equals the default model, or when this
    /// service has no <see cref="OpenAIClient"/> factory. Otherwise resolves
    /// and caches a dedicated client for the requested model.</summary>
    private AudioClient ResolveClient(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)
            || _factory is null
            || string.Equals(model, _defaultModel, StringComparison.Ordinal))
        {
            return _client;
        }
        return _byModel.GetOrAdd(model, m => _factory.GetAudioClient(m));
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

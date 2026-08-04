using ArmenianAiToy.Application.Audio;
using Microsoft.Extensions.Logging;
using OpenAI.Audio;

namespace ArmenianAiToy.Infrastructure.Audio;

/// <summary>
/// Production <see cref="IAudioSynthesisService"/> backed by OpenAI
/// TTS (<c>tts-1</c>). Default MP3 output is what OpenAI returns
/// without an explicit <c>ResponseFormat</c>; we keep that default
/// so no extra codec handling lands on this process.
/// <para>
/// Wraps the already-resolved <see cref="AudioClient"/> (same SDK
/// the chat + moderation adapters already use). No new NuGet, no
/// new auth config — reuses <c>OpenAI:ApiKey</c>.
/// </para>
/// <para>
/// The voice is <c>OpenAI:TtsVoice</c>, defaulting to <c>nova</c> — what
/// shipped before the key existed, so an unset config is a no-op. It was
/// previously a hardcoded literal here, which meant comparing voices
/// required a code change and a redeploy; the whole point of the key is
/// that the owner's listen test is a config flip.
/// </para>
/// </summary>
public sealed class OpenAITtsSynthesisService : IAudioSynthesisService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private const string ResponseMimeType = "audio/mpeg";

    /// <summary>What shipped before <c>OpenAI:TtsVoice</c> existed.</summary>
    public const string DefaultVoiceName = "nova";

    // One retry on a transient failure. The OpenAI TTS call occasionally
    // drops mid-flight (timeout / socket abort / upstream 5xx); without a
    // retry that momentary blip surfaces to the child as a sanitized 502.
    // Unlike the chat path's OpenAIReliabilityGate (full retry + circuit
    // breaker), TTS only needs a single cheap re-attempt — this mirrors the
    // moderation adapter's minimal single-retry posture.
    private const int MaxAttempts = 2;
    private static readonly TimeSpan RetryBackoff = TimeSpan.FromMilliseconds(400);

    private readonly AudioClient _client;
    private readonly ILogger<OpenAITtsSynthesisService> _logger;
    private readonly GeneratedSpeechVoice _voice;

    public OpenAITtsSynthesisService(
        AudioClient client,
        ILogger<OpenAITtsSynthesisService> logger,
        string? voiceName = null)
    {
        _client = client;
        _logger = logger;
        _voice = ResolveVoice(voiceName, logger);
    }

    /// <summary>
    /// Maps a configured voice name onto the SDK's voice constants.
    /// <para>
    /// An unknown name falls back to <see cref="DefaultVoiceName"/> with a
    /// loud warning rather than throwing: a typo in an optional voice setting
    /// must not take the whole site down at startup — the same posture the
    /// Resend notifier config takes, and for the same reason.
    /// </para>
    /// </summary>
    internal static GeneratedSpeechVoice ResolveVoice(
        string? voiceName, ILogger? logger = null)
    {
        var name = (voiceName ?? string.Empty).Trim().ToLowerInvariant();
        if (name.Length == 0) return GeneratedSpeechVoice.Nova;

        switch (name)
        {
            case "alloy": return GeneratedSpeechVoice.Alloy;
            case "echo": return GeneratedSpeechVoice.Echo;
            case "fable": return GeneratedSpeechVoice.Fable;
            case "onyx": return GeneratedSpeechVoice.Onyx;
            case "nova": return GeneratedSpeechVoice.Nova;
            case "shimmer": return GeneratedSpeechVoice.Shimmer;
            default:
                logger?.LogWarning(
                    "Unknown OpenAI:TtsVoice '{VoiceName}' — falling back to {DefaultVoice}",
                    voiceName, DefaultVoiceName);
                return GeneratedSpeechVoice.Nova;
        }
    }

    public async Task<AudioSynthesisResult> SynthesizeArmenianAsync(
        string text, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SynthesizeOnceAsync(text, cancellationToken);
            }
            // Retry only a transient failure, and only when the CALLER did
            // not cancel — an internal timeout fires the linked token while
            // `cancellationToken` stays uncancelled, so this check cleanly
            // separates "momentary blip" (retry) from "request aborted"
            // (propagate). On the final attempt the filter is false, so the
            // exception flows out to the controller's sanitized-502 catch.
            catch (Exception ex) when (
                attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "OpenAI TTS attempt {Attempt} failed transiently; retrying once", attempt);
                await Task.Delay(RetryBackoff, cancellationToken);
            }
        }
    }

    private async Task<AudioSynthesisResult> SynthesizeOnceAsync(
        string text, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(RequestTimeout);

        var result = await _client.GenerateSpeechAsync(
            text,
            _voice,
            options: null,
            cts.Token);
        // BinaryData.ToArray copies; the result is a self-contained
        // byte[] the caller can both stream to the HTTP response AND
        // persist into the blob store without a second SDK call.
        var bytes = result.Value.ToArray();
        _logger.LogInformation(
            "OpenAI TTS rendered {AudioBytes} bytes of {MimeType} ({TextChars} chars input)",
            bytes.Length, ResponseMimeType, text.Length);
        return new AudioSynthesisResult(bytes, ResponseMimeType);
    }
}

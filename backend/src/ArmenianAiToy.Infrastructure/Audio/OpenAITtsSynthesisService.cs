using ArmenianAiToy.Application.Audio;
using Microsoft.Extensions.Logging;
using OpenAI.Audio;

namespace ArmenianAiToy.Infrastructure.Audio;

/// <summary>
/// Production <see cref="IAudioSynthesisService"/> backed by OpenAI
/// TTS (<c>tts-1</c>). Uses the <see cref="GeneratedSpeechVoice.Nova"/>
/// voice — a warm narrator shape that reads Armenian script
/// acceptably for C1. Default MP3 output is what OpenAI returns
/// without an explicit <c>ResponseFormat</c>; we keep that default
/// so no extra codec handling lands on this process.
/// <para>
/// Wraps the already-resolved <see cref="AudioClient"/> (same SDK
/// the chat + moderation adapters already use). No new NuGet, no
/// new auth config — reuses <c>OpenAI:ApiKey</c>.
/// </para>
/// </summary>
public sealed class OpenAITtsSynthesisService : IAudioSynthesisService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private const string ResponseMimeType = "audio/mpeg";

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

    public OpenAITtsSynthesisService(
        AudioClient client, ILogger<OpenAITtsSynthesisService> logger)
    {
        _client = client;
        _logger = logger;
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
            GeneratedSpeechVoice.Nova,
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

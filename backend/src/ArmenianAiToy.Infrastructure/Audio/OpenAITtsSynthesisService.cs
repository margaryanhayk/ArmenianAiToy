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

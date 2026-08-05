using ArmenianAiToy.Application.Audio;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ArmenianAiToy.Infrastructure.Audio;

/// <summary>
/// ElevenLabs TTS adapter (owner decision 2026-08-05: the eleven_v3 clone
/// won the four-voice listen test — «Eleven v3 is best» — so the LIVE
/// voice moves to the same storyteller the pre-rendered content uses,
/// one voice for the whole toy).
///
/// <para>
/// Selected via <c>AI:TtsProvider = "elevenlabs"</c> on the provider seam.
/// Raw BCL HttpClient against POST /v1/text-to-speech/{voiceId}[/stream]
/// — no new NuGet, same posture as ResendNotifier. Implements BOTH the
/// buffered contract (drop-in for today's controller; measured 3-5 s per
/// short line) and <see cref="IStreamingAudioSynthesisService"/> (first
/// byte in ~0.9-1.4 s; the play-while-downloading half lands with the
/// firmware streaming slice).
/// </para>
///
/// <para>
/// Config: <c>ElevenLabs:ApiKey</c> (falls back to the
/// <c>ELEVENLABS_API_KEY</c> env name the render tool already uses),
/// <c>ElevenLabs:VoiceId</c> (fallback <c>ELEVENLABS_VOICE_ID</c>),
/// <c>ElevenLabs:ModelId</c> (default <c>eleven_v3</c> — the only model
/// on the account that speaks Armenian; re-verify before changing).
/// Both key and voice are REQUIRED when this provider is selected —
/// resolution and the fail-fast live in DependencyInjection, so this
/// class takes plain strings.
/// </para>
///
/// <para>
/// Failure posture mirrors OpenAITtsSynthesisService's callers: throw on
/// non-success (the controller's existing catch collapses it to the
/// sanitized 502); never log the API key; log status codes only, never
/// response bodies.
/// </para>
/// </summary>
public class ElevenLabsTtsSynthesisService
    : IAudioSynthesisService, IStreamingAudioSynthesisService
{
    private const string MimeMp3 = "audio/mpeg";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _voiceId;
    private readonly string _modelId;
    private readonly ILogger<ElevenLabsTtsSynthesisService> _logger;

    public ElevenLabsTtsSynthesisService(
        HttpClient http,
        string apiKey,
        string voiceId,
        string modelId,
        ILogger<ElevenLabsTtsSynthesisService> logger)
    {
        _http = http;
        _apiKey = apiKey;
        _voiceId = voiceId;
        _modelId = string.IsNullOrWhiteSpace(modelId) ? "eleven_v3" : modelId;
        _logger = logger;
    }

    public async Task<AudioSynthesisResult> SynthesizeArmenianAsync(
        string text, CancellationToken cancellationToken = default)
    {
        using var resp = await SendAsync(text, stream: false, cancellationToken);
        var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
        return new AudioSynthesisResult(bytes, MimeMp3);
    }

    public async Task<AudioSynthesisStreamResult> SynthesizeArmenianStreamAsync(
        string text, CancellationToken cancellationToken = default)
    {
        // Caller owns the stream; the response is intentionally NOT
        // disposed here — disposing it would close the network stream.
        var resp = await SendAsync(text, stream: true, cancellationToken);
        var audio = await resp.Content.ReadAsStreamAsync(cancellationToken);
        return new AudioSynthesisStreamResult(audio, MimeMp3);
    }

    private async Task<HttpResponseMessage> SendAsync(
        string text, bool stream, CancellationToken ct)
    {
        var path = stream
            ? $"https://api.elevenlabs.io/v1/text-to-speech/{_voiceId}/stream"
            : $"https://api.elevenlabs.io/v1/text-to-speech/{_voiceId}";

        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Headers.Add("xi-api-key", _apiKey);
        // NOTE: no voice_settings on purpose — sending one replaces the
        // voice's saved settings (the eleven_v3 lesson from the story
        // narration pipeline).
        req.Content = JsonContent.Create(new { text, model_id = _modelId });

        var resp = await _http.SendAsync(
            req,
            stream ? HttpCompletionOption.ResponseHeadersRead
                   : HttpCompletionOption.ResponseContentRead,
            ct);
        if (!resp.IsSuccessStatusCode)
        {
            // Status only — the body can echo request text; the key never
            // appears anywhere.
            var status = (int)resp.StatusCode;
            resp.Dispose();
            _logger.LogWarning(
                "ElevenLabs TTS non-success: HTTP {Status} (stream={Stream})",
                status, stream);
            throw new HttpRequestException($"ElevenLabs TTS returned HTTP {status}.");
        }
        return resp;
    }
}

using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Infrastructure.Ai;
using ArmenianAiToy.Infrastructure.Audio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Pins the ElevenLabs live-TTS adapter's wire contract (fake handler, no
/// network) and the TTS-only provider seam extension.
/// </summary>
public class ElevenLabsTtsSynthesisServiceTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;
        public string? LastBody;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public byte[] ResponseBytes = Encoding.ASCII.GetBytes("MP3BYTES");

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(Status)
            {
                Content = new ByteArrayContent(ResponseBytes),
            };
        }
    }

    private static (ElevenLabsTtsSynthesisService Svc, FakeHandler Handler) Create()
    {
        var handler = new FakeHandler();
        var svc = new ElevenLabsTtsSynthesisService(
            new HttpClient(handler),
            "test-key", "voice123", "eleven_v3",
            Substitute.For<ILogger<ElevenLabsTtsSynthesisService>>());
        return (svc, handler);
    }

    [Fact]
    public async Task Buffered_PostsToVoiceUrl_WithKeyHeader_AndModel()
    {
        var (svc, h) = Create();

        var result = await svc.SynthesizeArmenianAsync("Բարև՛");

        Assert.Equal("MP3BYTES", Encoding.ASCII.GetString(result.Content));
        Assert.Equal("audio/mpeg", result.MimeType);
        Assert.Equal(
            "https://api.elevenlabs.io/v1/text-to-speech/voice123",
            h.LastRequest!.RequestUri!.ToString());
        Assert.Equal("test-key", h.LastRequest.Headers.GetValues("xi-api-key").Single());
        using var doc = JsonDocument.Parse(h.LastBody!);
        Assert.Equal("Բարև՛", doc.RootElement.GetProperty("text").GetString());
        Assert.Equal("eleven_v3", doc.RootElement.GetProperty("model_id").GetString());
        // KEYSTONE: no voice_settings — sending one replaces the voice's
        // saved settings (the story-pipeline lesson).
        Assert.False(doc.RootElement.TryGetProperty("voice_settings", out _));
    }

    [Fact]
    public async Task Streaming_UsesStreamUrl_AndReturnsLiveStream()
    {
        var (svc, h) = Create();

        using var result = await svc.SynthesizeArmenianStreamAsync("Բարև՛");

        Assert.Equal(
            "https://api.elevenlabs.io/v1/text-to-speech/voice123/stream",
            h.LastRequest!.RequestUri!.ToString());
        using var ms = new MemoryStream();
        await result.AudioStream.CopyToAsync(ms);
        Assert.Equal("MP3BYTES", Encoding.ASCII.GetString(ms.ToArray()));
        Assert.Equal("audio/mpeg", result.MimeType);
    }

    [Fact]
    public async Task NonSuccess_Throws_WithStatusOnly_NeverTheKey()
    {
        var (svc, h) = Create();
        h.Status = HttpStatusCode.Unauthorized;

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => svc.SynthesizeArmenianAsync("Բարև՛"));

        Assert.Contains("401", ex.Message);
        Assert.DoesNotContain("test-key", ex.Message);
    }

    [Fact]
    public void Adapter_ImplementsBothSynthesisContracts()
    {
        var (svc, _) = Create();
        Assert.IsAssignableFrom<IAudioSynthesisService>(svc);
        Assert.IsAssignableFrom<IStreamingAudioSynthesisService>(svc);
    }

    // ── Seam extension ──────────────────────────────────────────────

    private static IConfiguration Config(string key, string value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();

    [Fact]
    public void Seam_TtsKey_AcceptsElevenLabs()
    {
        Assert.Equal(AiProviderConfig.ElevenLabs,
            AiProviderConfig.Resolve(
                Config(AiProviderConfig.TtsKey, "ElevenLabs"),
                AiProviderConfig.TtsKey,
                AiProviderConfig.SupportedForTts));
    }

    [Fact]
    public void Seam_ChatKey_StillRejectsElevenLabs()
    {
        // KEYSTONE: elevenlabs has a TTS adapter only. Accepting it for
        // chat would boot with no IAiChatClient and die on first use.
        Assert.Throws<InvalidOperationException>(() =>
            AiProviderConfig.Resolve(
                Config(AiProviderConfig.ChatKey, "elevenlabs"),
                AiProviderConfig.ChatKey));
    }
}

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

    // ── AI logic review 2026-10-27, C8: ceiling + one retry on the buffered path ──

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public int Requests;
        public Queue<HttpStatusCode> Script = new();
        public Func<CancellationToken, Task>? OnRequest;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            if (OnRequest is not null) await OnRequest(ct);
            var status = Script.Count > 0 ? Script.Dequeue() : HttpStatusCode.OK;
            return new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(Encoding.ASCII.GetBytes("MP3BYTES")),
            };
        }
    }

    private static ElevenLabsTtsSynthesisService CreateScripted(
        ScriptedHandler handler, TimeSpan? timeout = null) =>
        new(new HttpClient(handler), "test-key", "voice123", "eleven_v3",
            Substitute.For<ILogger<ElevenLabsTtsSynthesisService>>())
        {
            RequestTimeout = timeout ?? TimeSpan.FromSeconds(30),
        };

    [Fact]
    public async Task Buffered_TransientFailureThenSuccess_RetriesOnceAndReturnsAudio()
    {
        var h = new ScriptedHandler();
        h.Script.Enqueue(HttpStatusCode.ServiceUnavailable);
        h.Script.Enqueue(HttpStatusCode.OK);
        var svc = CreateScripted(h);

        var result = await svc.SynthesizeArmenianAsync("Բարև՛");

        Assert.Equal("MP3BYTES", Encoding.ASCII.GetString(result.Content));
        Assert.Equal(2, h.Requests);
    }

    [Fact]
    public async Task Buffered_FailsTwice_ThrowsAfterExactlyTwoRequests()
    {
        var h = new ScriptedHandler();
        h.Script.Enqueue(HttpStatusCode.ServiceUnavailable);
        h.Script.Enqueue(HttpStatusCode.ServiceUnavailable);
        var svc = CreateScripted(h);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => svc.SynthesizeArmenianAsync("Բարև՛"));

        Assert.Contains("503", ex.Message);
        Assert.Equal(2, h.Requests);
    }

    [Fact]
    public async Task Buffered_CallerGoneMidRequest_IsNotRetried()
    {
        using var caller = new CancellationTokenSource();
        var h = new ScriptedHandler();
        h.Script.Enqueue(HttpStatusCode.ServiceUnavailable);
        h.OnRequest = _ => { caller.Cancel(); return Task.CompletedTask; };
        var svc = CreateScripted(h);

        await Assert.ThrowsAnyAsync<Exception>(
            () => svc.SynthesizeArmenianAsync("Բարև՛", caller.Token));

        Assert.Equal(1, h.Requests);
    }

    [Fact]
    public async Task Buffered_HungUpstream_IsCutOffByTheOwnCeiling_NotTheCallersToken()
    {
        // The handler never answers. The caller's token is a 5 s safety net
        // only: the service's OWN ceiling (100 ms here, 30 s in production)
        // must fire first, so the caller token stays un-cancelled.
        var h = new ScriptedHandler { OnRequest = ct => Task.Delay(Timeout.Infinite, ct) };
        var svc = CreateScripted(h, TimeSpan.FromMilliseconds(100));
        using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => svc.SynthesizeArmenianAsync("Բարև՛", safety.Token));

        Assert.False(safety.IsCancellationRequested);
        Assert.Equal(2, h.Requests); // timeout is transient: one retry, also cut off
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

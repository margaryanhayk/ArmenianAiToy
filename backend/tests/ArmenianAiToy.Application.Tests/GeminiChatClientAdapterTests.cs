using ArmenianAiToy.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Pins the Gemini chat adapter's wire contract (fake handler, no
/// network) and the chat-capability seam extension.
/// </summary>
public class GeminiChatClientAdapterTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;
        public string? LastBody;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string ResponseJson =
            "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Բարև՛ ձեզ\"},{\"text\":\"։\"}]}}]}";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(ResponseJson, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (GeminiChatClientAdapter Svc, FakeHandler Handler) Create()
    {
        var handler = new FakeHandler();
        var svc = new GeminiChatClientAdapter(
            new HttpClient(handler), "test-key", "gemini-3-flash-preview",
            Substitute.For<ILogger<GeminiChatClientAdapter>>());
        return (svc, handler);
    }

    [Fact]
    public async Task PostsToModelUrl_WithKeyHeader_SystemInstruction_AndThinkingOff()
    {
        var (svc, h) = Create();

        var reply = await svc.GetCompletionAsync(
            "SYSTEM RULES",
            new List<(string, string)> { ("user", "խաղանք") });

        Assert.Equal("Բարև՛ ձեզ։", reply); // multi-part join
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-3-flash-preview:generateContent",
            h.LastRequest!.RequestUri!.ToString());
        Assert.Equal("test-key", h.LastRequest.Headers.GetValues("x-goog-api-key").Single());

        using var doc = JsonDocument.Parse(h.LastBody!);
        Assert.Equal("SYSTEM RULES",
            doc.RootElement.GetProperty("system_instruction")
                .GetProperty("parts")[0].GetProperty("text").GetString());
        // KEYSTONE (2026-08-06): NO thinkingConfig by default — the
        // stable 3.6-flash line REJECTS it with HTTP 400. It is sent
        // only when Gemini:ThinkingBudget is explicitly configured
        // (needed for older preview models where omitting it means
        // ~7 s thinking-mode TTFT).
        Assert.False(doc.RootElement.TryGetProperty("generationConfig", out _));
    }

    [Fact]
    public async Task ExplicitThinkingBudget_IsSent()
    {
        var handler = new FakeHandler();
        var svc = new GeminiChatClientAdapter(
            new HttpClient(handler), "test-key", "gemini-3-flash-preview",
            Substitute.For<ILogger<GeminiChatClientAdapter>>(),
            gate: null, thinkingBudget: 0);

        await svc.GetCompletionAsync("S", new List<(string, string)> { ("user", "x") });

        using var doc = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(0,
            doc.RootElement.GetProperty("generationConfig")
                .GetProperty("thinkingConfig").GetProperty("thinkingBudget").GetInt32());
    }

    [Fact]
    public async Task History_MapsAssistantToModelRole()
    {
        var (svc, h) = Create();

        await svc.GetCompletionAsync("S", new List<(string, string)>
        {
            ("user", "Ա"), ("assistant", "Բ"), ("user", "Գ"),
        });

        using var doc = JsonDocument.Parse(h.LastBody!);
        var contents = doc.RootElement.GetProperty("contents");
        Assert.Equal(3, contents.GetArrayLength());
        Assert.Equal("user", contents[0].GetProperty("role").GetString());
        Assert.Equal("model", contents[1].GetProperty("role").GetString());
        Assert.Equal("user", contents[2].GetProperty("role").GetString());
    }

    [Fact]
    public async Task NonSuccess_Throws_StatusOnly_NeverTheKey()
    {
        var (svc, h) = Create();
        h.Status = HttpStatusCode.TooManyRequests;
        h.ResponseJson = "{\"error\":{\"message\":\"quota\"}}";

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => svc.GetCompletionAsync("S", new List<(string, string)> { ("user", "x") }));

        Assert.Contains("429", ex.Message);
        Assert.DoesNotContain("test-key", ex.Message);
    }

    // ── Seam extension ──────────────────────────────────────────────

    private static IConfiguration Config(string key, string value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();

    [Fact]
    public void Seam_ChatKey_AcceptsGemini()
    {
        Assert.Equal(AiProviderConfig.Gemini,
            AiProviderConfig.Resolve(
                Config(AiProviderConfig.ChatKey, "Gemini"),
                AiProviderConfig.ChatKey,
                AiProviderConfig.SupportedForChat));
    }

    [Fact]
    public void Seam_TtsKey_StillRejectsGemini()
    {
        // KEYSTONE: gemini has a CHAT adapter only — accepting it for TTS
        // would boot with no IAudioSynthesisService.
        Assert.Throws<InvalidOperationException>(() =>
            AiProviderConfig.Resolve(
                Config(AiProviderConfig.TtsKey, "gemini"),
                AiProviderConfig.TtsKey,
                AiProviderConfig.SupportedForTts));
    }

    // ── Gemini-side safety (owner approval 2026-08-06) ───────────────

    [Fact]
    public async Task EveryRequest_CarriesSafetySettings_AllFourCategories_StrictDefault()
    {
        var (svc, h) = Create();

        await svc.GetCompletionAsync(
            "SYSTEM", new List<(string, string)> { ("user", "պատմիր հեքիաթ") });

        using var doc = JsonDocument.Parse(h.LastBody!);
        var settings = doc.RootElement.GetProperty("safetySettings");
        var byCategory = settings.EnumerateArray().ToDictionary(
            s => s.GetProperty("category").GetString()!,
            s => s.GetProperty("threshold").GetString()!);

        // KEYSTONE: all four harm categories pinned at the strictest
        // blocking tier on the wire — Gemini's own filter is never left
        // at vendor defaults for a children's product.
        Assert.Equal(GeminiChatClientAdapter.SafetyCategories.Length, byCategory.Count);
        foreach (var category in GeminiChatClientAdapter.SafetyCategories)
        {
            Assert.Equal("BLOCK_LOW_AND_ABOVE", byCategory[category]);
        }
    }

    [Fact]
    public async Task CandidateSafetyBlock_ReturnsCalmFallback_NotAThrow()
    {
        var (svc, h) = Create();
        // Real block shape: candidate present, finishReason=SAFETY, no
        // content.parts.
        h.ResponseJson = "{\"candidates\":[{\"finishReason\":\"SAFETY\",\"safetyRatings\":[]}]}";

        var reply = await svc.GetCompletionAsync(
            "SYSTEM", new List<(string, string)> { ("user", "x") });

        // KEYSTONE: a safety block is a VALID outcome — the child hears
        // the same calm Armenian line ChatService's own safety paths
        // speak, never the sanitized 502.
        Assert.Equal(GeminiChatClientAdapter.DefaultSafetyFallbackText, reply);
    }

    [Fact]
    public async Task PromptLevelBlock_NoCandidates_ReturnsCalmFallback()
    {
        var (svc, h) = Create();
        h.ResponseJson = "{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}";

        var reply = await svc.GetCompletionAsync(
            "SYSTEM", new List<(string, string)> { ("user", "x") });

        Assert.Equal(GeminiChatClientAdapter.DefaultSafetyFallbackText, reply);
    }

    [Fact]
    public async Task ConfiguredFallbackText_IsSpokenOnBlock()
    {
        var handler = new FakeHandler
        {
            ResponseJson = "{\"candidates\":[{\"finishReason\":\"PROHIBITED_CONTENT\"}]}",
        };
        var svc = new GeminiChatClientAdapter(
            new HttpClient(handler), "test-key", "m",
            Substitute.For<ILogger<GeminiChatClientAdapter>>(),
            safetyFallbackText: "Խաղա՞նք միասին։");

        var reply = await svc.GetCompletionAsync(
            "SYSTEM", new List<(string, string)> { ("user", "x") });

        Assert.Equal("Խաղա՞նք միասին։", reply);
    }

    [Fact]
    public void ResolveSafetyThreshold_EmptyIsStrictDefault_UnknownRefusesBoot_BlockNoneRefused()
    {
        Assert.Equal("BLOCK_LOW_AND_ABOVE",
            GeminiChatClientAdapter.ResolveSafetyThreshold(null));
        Assert.Equal("BLOCK_LOW_AND_ABOVE",
            GeminiChatClientAdapter.ResolveSafetyThreshold(""));
        Assert.Equal("BLOCK_MEDIUM_AND_ABOVE",
            GeminiChatClientAdapter.ResolveSafetyThreshold("BLOCK_MEDIUM_AND_ABOVE"));

        // KEYSTONE: no config value can switch Gemini's filter off, and
        // a typo refuses boot instead of silently weakening safety.
        Assert.Throws<InvalidOperationException>(
            () => GeminiChatClientAdapter.ResolveSafetyThreshold("BLOCK_NONE"));
        Assert.Throws<InvalidOperationException>(
            () => GeminiChatClientAdapter.ResolveSafetyThreshold("block_low_and_above"));
    }

    [Fact]
    public async Task CustomThreshold_ReachesTheWire()
    {
        var handler = new FakeHandler();
        var svc = new GeminiChatClientAdapter(
            new HttpClient(handler), "test-key", "m",
            Substitute.For<ILogger<GeminiChatClientAdapter>>(),
            safetyThreshold: "BLOCK_MEDIUM_AND_ABOVE");

        await svc.GetCompletionAsync(
            "SYSTEM", new List<(string, string)> { ("user", "x") });

        using var doc = JsonDocument.Parse(handler.LastBody!);
        foreach (var s in doc.RootElement.GetProperty("safetySettings").EnumerateArray())
        {
            Assert.Equal("BLOCK_MEDIUM_AND_ABOVE", s.GetProperty("threshold").GetString());
        }
    }
}

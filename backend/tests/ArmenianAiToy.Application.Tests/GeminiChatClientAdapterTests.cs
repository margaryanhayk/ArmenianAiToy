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


    // N11: the caller's token (a toy that dropped mid-turn) reaches the
    // HTTP call and aborts it.
    private sealed class HangingHandler : HttpMessageHandler
    {
        public CancellationToken SeenToken;
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            SeenToken = ct;
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        }
    }

    [Fact]
    public async Task CallerCancellation_AbortsTheHttpCall()
    {
        var handler = new HangingHandler();
        var svc = new GeminiChatClientAdapter(
            new HttpClient(handler), "test-key", "gemini-3-flash-preview",
            Substitute.For<ILogger<GeminiChatClientAdapter>>());
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            svc.GetCompletionAsync(
                "SYSTEM", new List<(string, string)> { ("user", "խաղանք") }, cts.Token));

        Assert.True(handler.SeenToken.CanBeCanceled);
        Assert.True(handler.SeenToken.IsCancellationRequested);
    }

    // ---- AD-005 / OK-001 (live 2026-09-23): a structurally-valid 200 must
    // never throw and never hand back empty/whitespace text.

    [Theory]
    [InlineData("{\"candidates\":[{\"finishReason\":\"OTHER\"}]}")]                                     // no content
    [InlineData("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"role\":\"model\"}}]}")]   // content without parts
    [InlineData("{\"candidates\":[{\"finishReason\":\"MAX_TOKENS\",\"content\":{}}]}")]                  // MAX_TOKENS, no parts
    [InlineData("{\"candidates\":[{\"content\":{\"parts\":\"oops\"}}]}")]                                 // parts not an array
    [InlineData("{\"candidates\":[{\"content\":{\"parts\":[]}}]}")]                                         // empty parts
    [InlineData("{\"candidates\":[{\"content\":{\"parts\":[{\"inlineData\":{}}]}}]}")]                    // no text part
    [InlineData("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"\\n\"}]}}]}")] // whitespace (OK-001)
    [InlineData("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"\"},{\"text\":\"  \"}]}}]}")] // empty + blank
    [InlineData("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":42}]}}]}")]                          // non-string text
    [InlineData("{\"candidates\":[{\"finishReason\":\"LANGUAGE\"}]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"MALFORMED_FUNCTION_CALL\"}]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"SOME_FUTURE_REASON\"}]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":7}]}")]                                                  // non-string finishReason
    [InlineData("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"I think the child\",\"thought\":true}]}}]}")] // thought-only
    [InlineData("{\"candidates\":[1]}")]                                                                   // candidate not an object
    public async Task UnusableCandidate_ReturnsEmpty_ForCallerFallback_NeverThrows(string json)
    {
        // Empty, not the adapter's safety line: ChatService's empty-reply
        // guard (mode-aware, Flagged, no choices) and StoryAnswerFilter's
        // Empty rejection each apply their own fallback.
        var (svc, h) = Create();
        h.ResponseJson = json;

        var reply = await svc.GetCompletionAsync(
            "SYSTEM", new List<(string, string)> { ("user", "x") });

        Assert.Equal(string.Empty, reply);
    }

    [Theory]
    [InlineData("[1,2]")]                                                        // root not an object
    [InlineData("{\"promptFeedback\":\"oops\"}")]                                // promptFeedback not an object
    [InlineData("{\"promptFeedback\":{\"blockReason\":3}}")]                     // blockReason not a string
    [InlineData("{\"candidates\":\"oops\",\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}")]
    public async Task MalformedPromptLevelShapes_ReturnSafetyFallback_NeverThrow(string json)
    {
        var (svc, h) = Create();
        h.ResponseJson = json;

        var reply = await svc.GetCompletionAsync(
            "SYSTEM", new List<(string, string)> { ("user", "x") });

        Assert.Equal(GeminiChatClientAdapter.DefaultSafetyFallbackText, reply);
    }

    [Fact]
    public async Task Recitation_WithPartialText_ReturnsCalmFallback_NotThePartial()
    {
        var (svc, h) = Create();
        h.ResponseJson =
            "{\"candidates\":[{\"finishReason\":\"RECITATION\",\"content\":{\"parts\":[{\"text\":\"Մի ժամանակ\"}]}}]}";

        var reply = await svc.GetCompletionAsync(
            "SYSTEM", new List<(string, string)> { ("user", "x") });

        Assert.Equal(GeminiChatClientAdapter.DefaultSafetyFallbackText, reply);
    }

    [Fact]
    public async Task MaxTokens_WithPartialText_ReturnsThePartial()
    {
        // Truncated but real text still flows on (ChatService moderates and
        // quality-gates it) — only an EMPTY truncation becomes the fallback.
        var (svc, h) = Create();
        h.ResponseJson =
            "{\"candidates\":[{\"finishReason\":\"MAX_TOKENS\",\"content\":{\"parts\":[{\"text\":\"Մի ժամանակ\"}]}}]}";

        var reply = await svc.GetCompletionAsync(
            "SYSTEM", new List<(string, string)> { ("user", "x") });

        Assert.Equal("Մի ժամանակ", reply);
    }

    [Fact]
    public async Task ThoughtParts_AreSkipped_RealTextKept()
    {
        var (svc, h) = Create();
        h.ResponseJson =
            "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"reasoning\",\"thought\":true},{\"text\":\"Բարև\"}]}}]}";

        var reply = await svc.GetCompletionAsync(
            "SYSTEM", new List<(string, string)> { ("user", "x") });

        Assert.Equal("Բարև", reply);
    }

    [Fact]
    public async Task NormalReply_WhitespaceInsideRealText_IsKeptVerbatim()
    {
        var (svc, h) = Create();
        h.ResponseJson =
            "{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"Բարև\\n\"},{\"text\":\"Արեգ\"}]}}]}";

        var reply = await svc.GetCompletionAsync(
            "SYSTEM", new List<(string, string)> { ("user", "x") });

        Assert.Equal("Բարև\nԱրեգ", reply);
    }
}

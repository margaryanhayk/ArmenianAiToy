using ArmenianAiToy.Infrastructure.Ai;
using ArmenianAiToy.Infrastructure.OpenAI;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Net;
using System.Text;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Pins the Gemini adapter's reliability armor: HttpRequestException
/// classification by StatusCode, and the gated retry-once on 429/5xx.
/// </summary>
public class GeminiReliabilityTests
{
    // ── Classifier: raw-HttpClient adapters set StatusCode ──────────

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, OpenAIFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.Unauthorized, OpenAIFailureKind.AuthFailure)]
    [InlineData(HttpStatusCode.Forbidden, OpenAIFailureKind.AuthFailure)]
    [InlineData(HttpStatusCode.InternalServerError, OpenAIFailureKind.UpstreamServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable, OpenAIFailureKind.UpstreamServerError)]
    [InlineData(HttpStatusCode.BadRequest, OpenAIFailureKind.Other)]
    public void Classifier_MapsHttpRequestExceptionStatus(
        HttpStatusCode status, OpenAIFailureKind expected)
    {
        var ex = new HttpRequestException("x", null, status);
        Assert.Equal(expected, OpenAIFailureClassifier.Classify(ex));
    }

    [Fact]
    public void Classifier_NullStatusCode_IsOther_NotRetryable()
    {
        // DNS/connect refusals carry no status — deliberately Other.
        var ex = new HttpRequestException("connection refused");
        Assert.Equal(OpenAIFailureKind.Other, OpenAIFailureClassifier.Classify(ex));
    }

    // ── Gated adapter: 429 then success → one retry, right answer ───

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        public int Calls;

        public SequenceHandler(params HttpResponseMessage[] responses)
            => _responses = new Queue<HttpResponseMessage>(responses);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private static HttpResponseMessage Ok(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"" + text + "\"}]}}]}",
            Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Fail(HttpStatusCode status) => new(status)
    {
        Content = new StringContent("{\"error\":{}}", Encoding.UTF8, "application/json"),
    };

    private static GeminiChatClientAdapter Gated(SequenceHandler handler)
    {
        // Injectable no-op delay — the retry backoff must not sleep in tests.
        var gate = new OpenAIReliabilityGate(
            TimeProvider.System,
            (_, _) => Task.CompletedTask,
            Substitute.For<ILogger<OpenAIReliabilityGate>>(),
            "gemini");
        return new GeminiChatClientAdapter(
            new HttpClient(handler), "k", "gemini-3-flash-preview",
            Substitute.For<ILogger<GeminiChatClientAdapter>>(), gate);
    }

    [Fact]
    public async Task RateLimited_RetriesOnce_ThenSucceeds()
    {
        var handler = new SequenceHandler(
            Fail(HttpStatusCode.TooManyRequests), Ok("Բարև"));
        var svc = Gated(handler);

        var reply = await svc.GetCompletionAsync(
            "S", new List<(string, string)> { ("user", "hi") });

        Assert.Equal("Բարև", reply);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task ServerError_RetriesOnce_ThenGivesUp()
    {
        var handler = new SequenceHandler(
            Fail(HttpStatusCode.InternalServerError),
            Fail(HttpStatusCode.InternalServerError));
        var svc = Gated(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            svc.GetCompletionAsync("S", new List<(string, string)> { ("user", "hi") }));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task AuthFailure_NeverRetries()
    {
        // KEYSTONE: a 403 (the Gate-2 empty-key bug's face) must fail
        // fast — retrying an auth failure is pure latency.
        var handler = new SequenceHandler(Fail(HttpStatusCode.Forbidden), Ok("x"));
        var svc = Gated(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            svc.GetCompletionAsync("S", new List<(string, string)> { ("user", "hi") }));
        Assert.Equal(1, handler.Calls);
    }
}

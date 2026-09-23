using ArmenianAiToy.Infrastructure.Ai;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Pins the opt-in Vertex AI backend for the Gemini chat adapter: the
/// bounded Gemini:Backend switch, the Vertex URL shape, the service-account
/// JWT assertion (claims + a signature that verifies), token caching, and
/// that the adapter sends a Bearer token — never the API key — in Vertex
/// mode while the default AI Studio path stays exactly as before.
/// </summary>
public class GeminiVertexBackendTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly List<(HttpRequestMessage Req, string? Body)> Calls = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Respond = _ => Json(
            "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Բարև\"}]}}]}");

        public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
            => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request, request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
            return Respond(request);
        }
    }

    private static readonly RSA TestKey = RSA.Create(2048);

    private static string ServiceAccountJson(string? tokenUri = "https://oauth2.example.test/token")
    {
        var fields = new Dictionary<string, string>
        {
            ["type"] = "service_account",
            ["client_email"] = "areg-chat@test-project.iam.gserviceaccount.com",
            ["private_key"] = TestKey.ExportPkcs8PrivateKeyPem(),
        };
        if (tokenUri is not null) fields["token_uri"] = tokenUri;
        return JsonSerializer.Serialize(fields);
    }

    private static byte[] B64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    [Theory]
    [InlineData(null, "ai-studio")]
    [InlineData("", "ai-studio")]
    [InlineData("ai-studio", "ai-studio")]
    [InlineData("vertex", "vertex")]
    [InlineData(" Vertex ", "vertex")]
    public void ResolveBackend_BoundedValues(string? configured, string expected)
        => Assert.Equal(expected, GeminiChatClientAdapter.ResolveBackend(configured));

    [Theory]
    [InlineData("vertexai")]
    [InlineData("studio")]
    public void ResolveBackend_Unknown_RefusesBoot(string configured)
        => Assert.Throws<InvalidOperationException>(() => GeminiChatClientAdapter.ResolveBackend(configured));

    [Fact]
    public void VertexEndpointUrl_GlobalAndRegional()
    {
        Assert.Equal(
            "https://aiplatform.googleapis.com/v1/projects/areg-prod/locations/global/publishers/google/models/gemini-3.6-flash:generateContent",
            GeminiChatClientAdapter.VertexEndpointUrl("areg-prod", "global", "gemini-3.6-flash"));
        Assert.Equal(
            "https://europe-west4-aiplatform.googleapis.com/v1/projects/areg-prod/locations/europe-west4/publishers/google/models/gemini-3.6-flash:generateContent",
            GeminiChatClientAdapter.VertexEndpointUrl("areg-prod", "europe-west4", "gemini-3.6-flash"));
    }

    [Fact]
    public void ParseServiceAccount_DefaultsTokenUri_AndNamesMissingFields()
    {
        var (email, pem, uri) = VertexAiAccessTokenProvider.ParseServiceAccount(ServiceAccountJson(tokenUri: null));
        Assert.Equal("areg-chat@test-project.iam.gserviceaccount.com", email);
        Assert.Contains("PRIVATE KEY", pem);
        Assert.Equal("https://oauth2.googleapis.com/token", uri);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            VertexAiAccessTokenProvider.ParseServiceAccount("{\"client_email\":\"x@y\"}"));
        Assert.Contains("private_key", ex.Message);
    }

    [Fact]
    public void ParseServiceAccount_InvalidJson_NeverEchoesContents()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            VertexAiAccessTokenProvider.ParseServiceAccount("not json SECRET-MATERIAL"));
        Assert.DoesNotContain("SECRET-MATERIAL", ex.Message);
    }

    [Fact]
    public void BuildAssertion_HasGoogleClaims_AndVerifiesWithThePublicKey()
    {
        var now = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        using var provider = new VertexAiAccessTokenProvider(new HttpClient(new FakeHandler()), ServiceAccountJson());

        var parts = provider.BuildAssertion(now).Split('.');
        Assert.Equal(3, parts.Length);

        using var header = JsonDocument.Parse(B64UrlDecode(parts[0]));
        Assert.Equal("RS256", header.RootElement.GetProperty("alg").GetString());

        using var claims = JsonDocument.Parse(B64UrlDecode(parts[1]));
        var c = claims.RootElement;
        Assert.Equal("areg-chat@test-project.iam.gserviceaccount.com", c.GetProperty("iss").GetString());
        Assert.Equal(VertexAiAccessTokenProvider.Scope, c.GetProperty("scope").GetString());
        Assert.Equal("https://oauth2.example.test/token", c.GetProperty("aud").GetString());
        Assert.Equal(now.ToUnixTimeSeconds(), c.GetProperty("iat").GetInt64());
        Assert.Equal(now.AddHours(1).ToUnixTimeSeconds(), c.GetProperty("exp").GetInt64());

        Assert.True(TestKey.VerifyData(
            Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), B64UrlDecode(parts[2]),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public async Task GetAccessToken_ExchangesOnce_CachesUntilNearExpiry()
    {
        var now = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        var handler = new FakeHandler();
        var n = 0;
        handler.Respond = _ => FakeHandler.Json($"{{\"access_token\":\"tok-{++n}\",\"expires_in\":3600}}");
        using var provider = new VertexAiAccessTokenProvider(new HttpClient(handler), ServiceAccountJson(), () => now);

        Assert.Equal("tok-1", await provider.GetAccessTokenAsync(default));
        Assert.Equal("tok-1", await provider.GetAccessTokenAsync(default));
        Assert.Single(handler.Calls);

        var (req, body) = handler.Calls[0];
        Assert.Equal("https://oauth2.example.test/token", req.RequestUri!.ToString());
        Assert.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Ajwt-bearer", body);
        Assert.Contains("assertion=", body);

        now = now.AddMinutes(54); // still outside the 5-minute refresh margin
        Assert.Equal("tok-1", await provider.GetAccessTokenAsync(default));
        now = now.AddMinutes(2); // 56 min: inside the margin
        Assert.Equal("tok-2", await provider.GetAccessTokenAsync(default));
        Assert.Equal(2, handler.Calls.Count);
    }

    [Fact]
    public async Task GetAccessToken_Failure_ThrowsWithStatusOnly()
    {
        var handler = new FakeHandler
        {
            Respond = _ => FakeHandler.Json("{\"error\":\"invalid_grant areg-chat@test-project\"}", HttpStatusCode.BadRequest),
        };
        using var provider = new VertexAiAccessTokenProvider(new HttpClient(handler), ServiceAccountJson());

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetAccessTokenAsync(default));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.DoesNotContain("areg-chat", ex.Message);
    }

    [Fact]
    public async Task Adapter_VertexMode_PostsToVertexUrl_WithBearer_NoApiKey()
    {
        var handler = new FakeHandler();
        var url = GeminiChatClientAdapter.VertexEndpointUrl("areg-prod", "global", "gemini-3.6-flash");
        var svc = new GeminiChatClientAdapter(
            new HttpClient(handler), "", "gemini-3.6-flash",
            Substitute.For<ILogger<GeminiChatClientAdapter>>(),
            vertexEndpointUrl: url,
            bearerToken: _ => Task.FromResult("vertex-token"));

        var reply = await svc.GetCompletionAsync("SYSTEM", new List<(string, string)> { ("user", "խաղանք") });

        Assert.Equal("Բարև", reply);
        var (req, body) = Assert.Single(handler.Calls);
        Assert.Equal(url, req.RequestUri!.ToString());
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal("vertex-token", req.Headers.Authorization.Parameter);
        Assert.False(req.Headers.Contains("x-goog-api-key"));
        using var doc = JsonDocument.Parse(body!);
        Assert.True(doc.RootElement.TryGetProperty("safetySettings", out _)); // Gemini's own filter still pinned
    }

    [Fact]
    public void Adapter_HalfConfiguredVertex_IsAWiringError()
    {
        Assert.Throws<ArgumentException>(() => new GeminiChatClientAdapter(
            new HttpClient(new FakeHandler()), "", "m",
            Substitute.For<ILogger<GeminiChatClientAdapter>>(),
            vertexEndpointUrl: "https://aiplatform.googleapis.com/x"));
    }
}

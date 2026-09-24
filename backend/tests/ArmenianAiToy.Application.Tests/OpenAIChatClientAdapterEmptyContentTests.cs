using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using ArmenianAiToy.Infrastructure.OpenAI;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenAI;
using OpenAI.Chat;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// A completion with no content parts (refusal-only / null content) used to
/// throw ArgumentOutOfRangeException on <c>Content[0]</c> → 502 + error
/// clip. It now returns "" so ChatService's empty-reply guard applies the
/// calm, Flagged fallback. Fake transport, no network.
/// </summary>
public class OpenAIChatClientAdapterEmptyContentTests
{
    private sealed class FakeHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
    }

    private static OpenAIChatClientAdapter Create(string json)
    {
        var client = new ChatClient("gpt-4o-mini", new ApiKeyCredential("test-key"),
            new OpenAIClientOptions
            {
                Transport = new HttpClientPipelineTransport(new HttpClient(new FakeHandler(json))),
            });
        return new OpenAIChatClientAdapter(client,
            new OpenAIReliabilityGate(Substitute.For<ILogger<OpenAIReliabilityGate>>()));
    }

    private static string Completion(string messageJson) =>
        "{\"id\":\"c1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"gpt-4o-mini\"," +
        "\"choices\":[{\"index\":0,\"finish_reason\":\"stop\",\"message\":" + messageJson + "}]}";

    [Fact]
    public async Task RefusalOnly_NoContent_ReturnsEmpty_NeverThrows()
    {
        var svc = Create(Completion("{\"role\":\"assistant\",\"content\":null,\"refusal\":\"I can't help with that.\"}"));

        var reply = await svc.GetCompletionAsync("SYSTEM", new List<(string, string)> { ("user", "x") });

        Assert.Equal(string.Empty, reply);
    }

    [Fact]
    public async Task NormalContent_ReturnedVerbatim()
    {
        var svc = Create(Completion("{\"role\":\"assistant\",\"content\":\"Բարև\\nԱրեգ\"}"));

        var reply = await svc.GetCompletionAsync("SYSTEM", new List<(string, string)> { ("user", "x") });

        Assert.Equal("Բարև\nԱրեգ", reply);
    }
}

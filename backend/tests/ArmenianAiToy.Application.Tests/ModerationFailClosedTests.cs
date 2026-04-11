using ArmenianAiToy.Infrastructure.OpenAI;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenAI.Moderations;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests that the moderation adapter fails closed — when the OpenAI
/// moderation API is unreachable or throws, content is treated as unsafe.
/// Critical safety guarantee for a child-facing product.
/// </summary>
public class ModerationFailClosedTests
{
    [Fact]
    public async Task CheckContentAsync_WhenClientThrows_ReturnsUnsafe()
    {
        // ModerationClient is sealed, so we construct it with invalid credentials.
        // Any call to ClassifyTextAsync will throw (no valid API key / endpoint).
        var client = new ModerationClient(model: "text-moderation-stable", apiKey: "invalid-key");
        var logger = Substitute.For<ILogger<OpenAIModerationAdapter>>();
        var adapter = new OpenAIModerationAdapter(client, logger);

        var result = await adapter.CheckContentAsync("test content");

        Assert.False(result.IsSafe);
        Assert.Contains("moderation_unavailable", result.FlaggedCategories);
    }

    [Fact]
    public async Task CheckContentAsync_WhenClientThrows_LogsError()
    {
        var client = new ModerationClient(model: "text-moderation-stable", apiKey: "invalid-key");
        var logger = Substitute.For<ILogger<OpenAIModerationAdapter>>();
        var adapter = new OpenAIModerationAdapter(client, logger);

        await adapter.CheckContentAsync("test content");

        logger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Is<Exception>(e => e != null),
            Arg.Any<Func<object, Exception?, string>>());
    }
}

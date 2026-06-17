using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Api.Security;
using ArmenianAiToy.Application.Stories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <see cref="StoryAudioTokenController"/>
/// (<c>GET /api/chat/story-audio-token</c>) — the device-authed minting
/// endpoint for the header-less story-audio access token.
/// </summary>
public class StoryAudioTokenControllerTests
{
    private const string RealStoryId = InMemoryCuratedStoryLibrary.LittleCloudId;
    private const string SigningKey = "unit-test-signing-key";

    private static IConfiguration Config(params (string Key, string? Value)[] kv) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(kv.Select(p =>
                new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    private static StoryAudioTokenController Create(IConfiguration config) =>
        new(new InMemoryCuratedStoryLibrary(), config);

    private static T? Prop<T>(object value, string name) =>
        (T?)value.GetType().GetProperty(name)!.GetValue(value);

    [Fact]
    public void Enforced_IssuesValidTokenBoundToStory()
    {
        var controller = Create(Config(("StoryAudio:SigningKey", SigningKey)));

        var result = controller.Get(RealStoryId);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.True(Prop<bool>(ok.Value!, "enforced"));
        var token = Prop<string>(ok.Value!, "token");
        Assert.False(string.IsNullOrWhiteSpace(token));
        // The minted token validates against the same key + story.
        Assert.True(StoryAudioToken.TryValidate(
            token, RealStoryId, SigningKey, DateTimeOffset.UtcNow));
        // And is NOT accepted for a different story.
        Assert.False(StoryAudioToken.TryValidate(
            token, "hedgehog-apple", SigningKey, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void NoSigningKey_ReportsUnenforced_NullToken()
    {
        var controller = Create(Config(("StoryAudio:SigningKey", "")));

        var result = controller.Get(RealStoryId);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.False(Prop<bool>(ok.Value!, "enforced"));
        Assert.Null(Prop<string>(ok.Value!, "token"));
    }

    [Fact]
    public void UnknownStory_Returns404()
    {
        var controller = Create(Config(("StoryAudio:SigningKey", SigningKey)));

        var result = controller.Get("no-such-story");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void CustomTtl_IsHonored()
    {
        var controller = Create(Config(
            ("StoryAudio:SigningKey", SigningKey),
            ("StoryAudio:TokenTtlSeconds", "120")));

        var result = controller.Get(RealStoryId);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(120, Prop<int>(ok.Value!, "expiresInSeconds"));
    }
}

using System.Text;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Api.Security;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Stories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Access-gate tests for <see cref="StoryAudioController"/>
/// (<c>GET /api/story-audio</c>). Focus: the signed-token gate and the
/// refresh lock-down added to close the open / hammerable gap. The
/// 404-on-bad-token branches run before any TTS render (no disk IO);
/// the "accepted" branches render to a unique temp cache dir with a
/// substituted synthesizer so no network is touched.
/// </summary>
public class StoryAudioControllerSecurityTests
{
    private const string RealStoryId = InMemoryCuratedStoryLibrary.LittleCloudId;
    private const string SigningKey = "unit-test-signing-key";
    private static readonly byte[] FakeMp3 = Encoding.UTF8.GetBytes("ID3 fake story audio");

    private static IConfiguration Config(params (string Key, string? Value)[] kv) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(kv.Select(p =>
                new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    private static (StoryAudioController Controller, IAudioSynthesisService Synth, string CacheDir)
        Create(IConfiguration baseConfig)
    {
        var cacheDir = Path.Combine(
            Path.GetTempPath(), "areg-storyaudio-test-" + Guid.NewGuid().ToString("N"));
        // Layer the per-test cache root over the supplied config.
        var config = new ConfigurationBuilder()
            .AddConfiguration(baseConfig)
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("StoryAudio:CacheRoot", cacheDir)
            })
            .Build();

        var synth = Substitute.For<IAudioSynthesisService>();
        synth.SynthesizeArmenianAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AudioSynthesisResult(FakeMp3, "audio/mpeg"));
        var env = Substitute.For<IWebHostEnvironment>();
        var logger = Substitute.For<ILogger<StoryAudioController>>();
        var moderation = Substitute.For<IModerationService>();
        moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(IsSafe: true, FlaggedCategories: new List<string>()));

        var controller = new StoryAudioController(
            synth, new InMemoryCuratedStoryLibrary(), moderation, env, config, logger);
        return (controller, synth, cacheDir);
    }

    // --- Token gate (the security keystone) -------------------------

    [Fact]
    public async Task SigningKeySet_NoToken_Returns404_WithoutRendering()
    {
        var (controller, synth, _) = Create(Config(("StoryAudio:SigningKey", SigningKey)));

        var result = await controller.GetStoryAudio(RealStoryId, token: null);

        Assert.IsType<NotFoundObjectResult>(result);
        // Gate runs before any TTS render.
        await synth.DidNotReceiveWithAnyArgs()
            .SynthesizeArmenianAsync(default!, default);
    }

    [Fact]
    public async Task SigningKeySet_WrongStoryToken_Returns404()
    {
        var (controller, _, _) = Create(Config(("StoryAudio:SigningKey", SigningKey)));
        // A valid token, but bound to a DIFFERENT story.
        var token = StoryAudioToken.Issue(
            "hedgehog-apple", DateTimeOffset.UtcNow.AddHours(1), SigningKey);

        var result = await controller.GetStoryAudio(RealStoryId, token: token);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task SigningKeySet_ExpiredToken_Returns404()
    {
        var (controller, _, _) = Create(Config(("StoryAudio:SigningKey", SigningKey)));
        var token = StoryAudioToken.Issue(
            RealStoryId, DateTimeOffset.UtcNow.AddSeconds(-1), SigningKey);

        var result = await controller.GetStoryAudio(RealStoryId, token: token);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task SigningKeySet_ValidToken_PassesGate_AndServesAudio()
    {
        var (controller, _, _) = Create(Config(("StoryAudio:SigningKey", SigningKey)));
        var token = StoryAudioToken.Issue(
            RealStoryId, DateTimeOffset.UtcNow.AddHours(1), SigningKey);

        var result = await controller.GetStoryAudio(RealStoryId, token: token);

        // Past the gate → a file result (rendered to the temp cache), not 404.
        Assert.IsNotType<NotFoundObjectResult>(result);
        Assert.IsType<PhysicalFileResult>(result);
    }

    [Fact]
    public async Task NoSigningKey_OpenAccess_ServesAudioWithoutToken()
    {
        var (controller, _, _) = Create(Config(("StoryAudio:SigningKey", "")));

        var result = await controller.GetStoryAudio(RealStoryId, token: null);

        Assert.IsType<PhysicalFileResult>(result);
    }

    // --- Refresh lock-down (the cost vector) ------------------------

    [Fact]
    public async Task Refresh_WithoutConfiguredSecret_IsDenied_StillServes()
    {
        // No RefreshToken configured → refresh is silently downgraded to a
        // normal (cached) serve. The request still succeeds; it just can't
        // force a re-render.
        var (controller, _, _) = Create(Config(("StoryAudio:SigningKey", "")));

        var result = await controller.GetStoryAudio(
            RealStoryId, refresh: true, refreshToken: "anything");

        Assert.IsType<PhysicalFileResult>(result);
    }

    [Fact]
    public async Task UnknownStory_Returns404_RegardlessOfToken()
    {
        var (controller, _, _) = Create(Config(("StoryAudio:SigningKey", SigningKey)));

        var result = await controller.GetStoryAudio("no-such-story", token: null);

        Assert.IsType<NotFoundObjectResult>(result);
    }
}

using ArmenianAiToy.Api.Controllers;
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
/// #016 — output moderation on the served narration AUDIO. The story's TEXT
/// must clear the safety classifier BEFORE it is rendered + cached, FAIL-CLOSED:
/// an unsafe chunk OR a moderation outage aborts the render so unsafe narration
/// is never spoken to a child. The pre-flight runs before any audio is written
/// (no partial/unsafe cache file), and a cache hit is NOT re-moderated.
/// </summary>
public class StoryAudioModerationTests
{
    private const string StoryId = InMemoryCuratedStoryLibrary.LittleCloudId;
    private static readonly byte[] FakeMp3 = System.Text.Encoding.UTF8.GetBytes("ID3 fake mp3 bytes");

    private sealed record Harness(
        StoryAudioController Controller,
        IAudioSynthesisService Synth,
        IModerationService Moderation,
        string CacheDir,
        string CachePath);

    private static Harness Create(ModerationResult moderationResult)
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "areg-storyaudio-mod-" + Guid.NewGuid().ToString("N"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("StoryAudio:CacheRoot", cacheDir),
            new KeyValuePair<string, string?>("StoryAudio:SigningKey", "")
        }).Build();

        var synth = Substitute.For<IAudioSynthesisService>();
        synth.SynthesizeArmenianAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AudioSynthesisResult(FakeMp3, "audio/mpeg"));
        var moderation = Substitute.For<IModerationService>();
        moderation.CheckContentAsync(Arg.Any<string>()).Returns(moderationResult);

        var controller = new StoryAudioController(
            synth, new InMemoryCuratedStoryLibrary(), moderation,
            Substitute.For<IWebHostEnvironment>(), config,
            Substitute.For<ILogger<StoryAudioController>>());

        return new Harness(controller, synth, moderation, cacheDir, Path.Combine(cacheDir, StoryId + ".mp3"));
    }

    private static ModerationResult Safe() => new(IsSafe: true, FlaggedCategories: new List<string>());
    private static ModerationResult Unsafe(params string[] cats) => new(IsSafe: false, FlaggedCategories: cats.ToList());

    [Fact]
    public async Task SafeNarration_RendersAndServes_ModerationRan()
    {
        var h = Create(Safe());
        try
        {
            var result = await h.Controller.GetStoryAudio(StoryId);
            Assert.IsType<PhysicalFileResult>(result);
            Assert.True(File.Exists(h.CachePath), "a safe story should be rendered + cached");
            await h.Moderation.ReceivedWithAnyArgs().CheckContentAsync(default!);
        }
        finally { Cleanup(h); }
    }

    [Fact]
    public async Task UnsafeNarration_Blocked_NotRendered_NotServed()
    {
        var h = Create(Unsafe("violence"));
        try
        {
            var result = await h.Controller.GetStoryAudio(StoryId);
            // Concealment 404 — never a 200 / PhysicalFile of unsafe audio.
            Assert.IsType<NotFoundObjectResult>(result);
            // No cache file written: the pre-flight aborts before any audio.
            Assert.False(File.Exists(h.CachePath), "unsafe story must NOT be cached");
            // Aborted BEFORE any TTS render.
            await h.Synth.DidNotReceiveWithAnyArgs().SynthesizeArmenianAsync(default!, default);
        }
        finally { Cleanup(h); }
    }

    [Fact]
    public async Task ModerationUnavailable_FailsClosed_Blocked()
    {
        // CheckContentAsync's fail-closed contract returns IsSafe=false with the
        // moderation_unavailable sentinel; the render must treat that as unsafe.
        var h = Create(Unsafe("moderation_unavailable"));
        try
        {
            var result = await h.Controller.GetStoryAudio(StoryId);
            Assert.IsType<NotFoundObjectResult>(result);
            Assert.False(File.Exists(h.CachePath));
            await h.Synth.DidNotReceiveWithAnyArgs().SynthesizeArmenianAsync(default!, default);
        }
        finally { Cleanup(h); }
    }

    private static void Cleanup(Harness h)
    {
        try { if (Directory.Exists(h.CacheDir)) Directory.Delete(h.CacheDir, true); } catch { }
    }
}

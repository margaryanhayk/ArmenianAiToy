using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// B2 content-sync clips tests: config binding, manifest per-clip
/// fail-closed validation, default URL fill, and the content-file
/// <c>clip=</c> lookup (uniform 404, no traversal surface).
/// </summary>
public class ContentSyncClipsTests
{
    private static readonly string ValidSha = new('a', 64);

    // ---- config binding ----

    [Fact]
    public void Resolve_BindsClipsArray()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ContentSync:Enabled"] = "true",
                ["ContentSync:Stories:0:StoryId"] = "anban-huri",
                ["ContentSync:Stories:0:Version"] = "3",
                ["ContentSync:Stories:0:Sha256"] = ValidSha,
                ["ContentSync:Stories:0:SizeBytes"] = "1000",
                ["ContentSync:Stories:0:AudioPath"] = "anban-huri.mp3",
                ["ContentSync:Stories:0:Clips:0:Kind"] = "intro",
                ["ContentSync:Stories:0:Clips:0:Sha256"] = ValidSha,
                ["ContentSync:Stories:0:Clips:0:SizeBytes"] = "500",
                ["ContentSync:Stories:0:Clips:0:AudioPath"] = "anban-huri-intro.mp3",
                ["ContentSync:Stories:0:Clips:1:Kind"] = "question",
                ["ContentSync:Stories:0:Clips:1:Sha256"] = ValidSha,
                ["ContentSync:Stories:0:Clips:1:SizeBytes"] = "600",
                ["ContentSync:Stories:0:Clips:1:AudioPath"] = "anban-huri-question.mp3",
            }).Build();

        var options = ContentSyncOptions.Resolve(config);

        var story = Assert.Single(options.Stories);
        Assert.Equal(2, story.Clips.Count);
        Assert.Equal("intro", story.Clips[0].Kind);
        Assert.Equal(500, story.Clips[0].SizeBytes);
        Assert.Equal("question", story.Clips[1].Kind);
    }

    [Fact]
    public void ResolveStories_AppliesAudioRootToClipPaths()
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            AudioRoot = "story-audio",
            Stories =
            {
                new ContentSyncStoryOptions
                {
                    StoryId = "s", Sha256 = ValidSha, SizeBytes = 1,
                    AudioPath = "s.mp3",
                    Clips = { new ContentSyncClipOptions
                    {
                        Kind = "intro", Sha256 = ValidSha, SizeBytes = 1,
                        AudioPath = "s-intro.mp3",
                    } },
                },
            },
        };

        var resolved = Assert.Single(options.ResolveStories());
        Assert.True(Path.IsPathRooted(resolved.AudioPath));
        Assert.True(Path.IsPathRooted(Assert.Single(resolved.Clips).AudioPath));
    }

    // ---- manifest validation ----

    private static ContentSyncOptions ManifestOptions(params ContentSyncClipOptions[] clips)
    {
        var story = new ContentSyncStoryOptions
        {
            StoryId = "anban-huri", Version = 3, Title = "t",
            Sha256 = ValidSha, SizeBytes = 1000, AudioPath = "/abs/anban.mp3",
        };
        foreach (var c in clips) story.Clips.Add(c);
        return new ContentSyncOptions { Enabled = true, Stories = { story } };
    }

    [Fact]
    public void Manifest_ValidClips_CarriedWithDefaultUrls()
    {
        var service = new ContentManifestService(ManifestOptions(
            new ContentSyncClipOptions { Kind = "intro", Sha256 = ValidSha, SizeBytes = 10 },
            new ContentSyncClipOptions { Kind = "summary", Sha256 = ValidSha, SizeBytes = 20 }));

        var item = Assert.Single(service.Build().Stories);
        Assert.NotNull(item.Clips);
        Assert.Equal(2, item.Clips!.Count);
        Assert.Equal("/api/devices/content-file?storyId=anban-huri&clip=intro",
            item.Clips[0].AudioUrl);
        Assert.Equal("summary", item.Clips[1].Kind);
    }

    // KEYSTONE: a bad clip drops ONLY itself — never the story, never its
    // valid sibling clips. Unknown kinds are structurally unshippable.
    [Fact]
    public void Manifest_InvalidClips_DroppedWithoutTakingStory()
    {
        var service = new ContentManifestService(ManifestOptions(
            new ContentSyncClipOptions { Kind = "hack", Sha256 = ValidSha, SizeBytes = 10 },
            new ContentSyncClipOptions { Kind = "intro", Sha256 = "short", SizeBytes = 10 },
            new ContentSyncClipOptions { Kind = "question", Sha256 = ValidSha, SizeBytes = 0 },
            new ContentSyncClipOptions { Kind = "summary", Sha256 = ValidSha, SizeBytes = 10 },
            new ContentSyncClipOptions { Kind = "summary", Sha256 = ValidSha, SizeBytes = 99 }));

        var item = Assert.Single(service.Build().Stories);
        var clip = Assert.Single(item.Clips!);
        Assert.Equal("summary", clip.Kind);
        Assert.Equal(10, clip.SizeBytes); // duplicate kind kept the FIRST
    }

    [Fact]
    public void Manifest_NoClips_FieldStaysNull()
    {
        var service = new ContentManifestService(ManifestOptions());
        Assert.Null(Assert.Single(service.Build().Stories).Clips);
    }

    // ---- content-file clip lookup ----

    private static DeviceController Controller()
    {
        var controller = new DeviceController(
            Substitute.For<IDeviceService>(), Substitute.For<IConfiguration>());
        var http = new DefaultHttpContext();
        http.Items["DeviceId"] = Guid.NewGuid();
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static string TempMp3()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp3");
        File.WriteAllBytes(path, [0xFF, 0xF3, 0x01, 0x02]);
        return path;
    }

    [Fact]
    public void ContentFile_ClipKind_StreamsClipFile()
    {
        var clipFile = TempMp3();
        try
        {
            var options = ManifestOptions(new ContentSyncClipOptions
            {
                Kind = "intro", Sha256 = ValidSha, SizeBytes = 4, AudioPath = clipFile,
            });

            var result = Controller().GetContentFile(
                options, Substitute.For<ILogger<DeviceController>>(),
                storyId: "anban-huri", clip: "intro");

            var file = Assert.IsType<PhysicalFileResult>(result);
            Assert.Equal(clipFile, file.FileName);
            Assert.Equal("audio/mpeg", file.ContentType);
        }
        finally
        {
            File.Delete(clipFile);
        }
    }

    [Theory]
    [InlineData("summary")]          // configured story, unshipped kind
    [InlineData("../../etc/passwd")] // traversal-shaped — just an unknown key
    [InlineData("INTRO2")]
    public void ContentFile_UnknownClip_Uniform404(string clipParam)
    {
        var options = ManifestOptions(new ContentSyncClipOptions
        {
            Kind = "intro", Sha256 = ValidSha, SizeBytes = 4, AudioPath = "/abs/i.mp3",
        });

        var result = Controller().GetContentFile(
            options, Substitute.For<ILogger<DeviceController>>(),
            storyId: "anban-huri", clip: clipParam);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void ContentFile_NoClipParam_StillStreamsNarration()
    {
        var storyFile = TempMp3();
        try
        {
            var options = ManifestOptions(new ContentSyncClipOptions
            {
                Kind = "intro", Sha256 = ValidSha, SizeBytes = 4, AudioPath = "/abs/i.mp3",
            });
            options.Stories[0].AudioPath = storyFile;

            var result = Controller().GetContentFile(
                options, Substitute.For<ILogger<DeviceController>>(),
                storyId: "anban-huri");

            var file = Assert.IsType<PhysicalFileResult>(result);
            Assert.Equal(storyFile, file.FileName);
        }
        finally
        {
            File.Delete(storyFile);
        }
    }
}

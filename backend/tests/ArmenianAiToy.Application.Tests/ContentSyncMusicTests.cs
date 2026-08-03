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
/// Slice E — bedtime-music content sync: config binding, per-track
/// manifest validation, the content-file trackId lookup (separate
/// namespace from stories), and the music-less wire staying unchanged.
/// </summary>
public class ContentSyncMusicTests
{
    private static readonly string ValidSha = new('b', 64);

    [Fact]
    public void Resolve_BindsMusicArray_AndAppliesAudioRoot()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ContentSync:Enabled"] = "true",
                ["ContentSync:AudioRoot"] = "story-audio",
                ["ContentSync:Music:0:TrackId"] = "lullaby-one",
                ["ContentSync:Music:0:Version"] = "2",
                ["ContentSync:Music:0:Title"] = "Օրոր",
                ["ContentSync:Music:0:Sha256"] = ValidSha,
                ["ContentSync:Music:0:SizeBytes"] = "1234",
                ["ContentSync:Music:0:AudioPath"] = "lullaby-one.mp3",
            }).Build();

        var options = ContentSyncOptions.Resolve(config);

        var track = Assert.Single(options.Music);
        Assert.Equal("lullaby-one", track.TrackId);
        Assert.Equal(2, track.Version);
        var resolved = Assert.Single(options.ResolveMusic());
        Assert.True(Path.IsPathRooted(resolved.AudioPath));
    }

    [Fact]
    public void Manifest_ValidTracks_Carried_InvalidDropped()
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            Stories = { new ContentSyncStoryOptions
            {
                StoryId = "s", Sha256 = new string('a', 64), SizeBytes = 1,
            } },
            Music =
            {
                new ContentSyncMusicOptions { TrackId = "lullaby-one", Sha256 = ValidSha, SizeBytes = 10 },
                new ContentSyncMusicOptions { TrackId = "", Sha256 = ValidSha, SizeBytes = 10 },        // no id
                new ContentSyncMusicOptions { TrackId = "bad-sha", Sha256 = "xx", SizeBytes = 10 },     // bad sha
                new ContentSyncMusicOptions { TrackId = "lullaby-one", Sha256 = ValidSha, SizeBytes = 99 }, // dup
            },
        };

        var manifest = new ContentManifestService(options).Build();

        Assert.NotNull(manifest.Music);
        var track = Assert.Single(manifest.Music!);
        Assert.Equal("lullaby-one", track.TrackId);
        Assert.Equal(10, track.SizeBytes);
        Assert.Equal("/api/devices/content-file?trackId=lullaby-one", track.AudioUrl);
    }

    // KEYSTONE: a music-less deployment's wire shape stays exactly as
    // before the slice (Music null), so shipped firmware sees no change.
    [Fact]
    public void Manifest_NoMusic_FieldStaysNull()
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            Stories = { new ContentSyncStoryOptions
            {
                StoryId = "s", Sha256 = new string('a', 64), SizeBytes = 1,
            } },
        };
        Assert.Null(new ContentManifestService(options).Build().Music);
    }

    private static DeviceController Controller()
    {
        var controller = new DeviceController(
            Substitute.For<IDeviceService>(), Substitute.For<IConfiguration>());
        var http = new DefaultHttpContext();
        http.Items["DeviceId"] = Guid.NewGuid();
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    [Fact]
    public void ContentFile_TrackId_StreamsMusicFile()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp3");
        File.WriteAllBytes(file, [0xFF, 0xF3]);
        try
        {
            var options = new ContentSyncOptions
            {
                Enabled = true,
                Music = { new ContentSyncMusicOptions
                {
                    TrackId = "lullaby-one", Sha256 = ValidSha, SizeBytes = 2, AudioPath = file,
                } },
            };

            var result = Controller().GetContentFile(
                options, Substitute.For<ILogger<DeviceController>>(), trackId: "lullaby-one");

            var physical = Assert.IsType<PhysicalFileResult>(result);
            Assert.Equal(file, physical.FileName);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("unknown-track")]
    [InlineData("../../etc/passwd")]
    public void ContentFile_UnknownTrack_Uniform404(string trackId)
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            Music = { new ContentSyncMusicOptions
            {
                TrackId = "lullaby-one", Sha256 = ValidSha, SizeBytes = 2, AudioPath = "/abs/l.mp3",
            } },
        };

        var result = Controller().GetContentFile(
            options, Substitute.For<ILogger<DeviceController>>(), trackId: trackId);

        Assert.IsType<NotFoundObjectResult>(result);
    }
}

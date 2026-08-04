using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Welcome-flow content sync: the device-global VOICE clip namespace
/// (greetings, menu prompts, fallback lines), the two new per-story clip
/// kinds (offer / reoffer), and the four parent mode flags riding the
/// manifest so the toy only offers what the parent allows.
/// <para>
/// Structured as a deliberate copy of <see cref="ContentSyncMusicTests"/> —
/// voice is the third namespace built on the same contract, and the tests
/// staying recognisably parallel is what makes a divergence obvious.
/// </para>
/// </summary>
public class ContentSyncVoiceTests
{
    private static readonly string ValidSha = new('c', 64);
    private static readonly string StorySha = new('a', 64);

    private static ContentSyncOptions WithOneStory() => new()
    {
        Enabled = true,
        Stories = { new ContentSyncStoryOptions
        {
            StoryId = "anban-huri", Sha256 = StorySha, SizeBytes = 1,
        } },
    };

    // ---- config binding ----

    [Fact]
    public void Resolve_BindsVoiceArray_AndAppliesAudioRoot()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ContentSync:Enabled"] = "true",
                ["ContentSync:AudioRoot"] = "story-audio",
                ["ContentSync:Voice:0:VoiceId"] = "greet-01",
                ["ContentSync:Voice:0:Version"] = "3",
                ["ContentSync:Voice:0:Sha256"] = ValidSha,
                ["ContentSync:Voice:0:SizeBytes"] = "2048",
                ["ContentSync:Voice:0:AudioPath"] = "greet-01.mp3",
            }).Build();

        var options = ContentSyncOptions.Resolve(config);

        var clip = Assert.Single(options.Voice);
        Assert.Equal("greet-01", clip.VoiceId);
        Assert.Equal(3, clip.Version);
        Assert.Equal(2048, clip.SizeBytes);
        var resolved = Assert.Single(options.ResolveVoice());
        Assert.True(Path.IsPathRooted(resolved.AudioPath));
    }

    // ---- manifest ----

    [Fact]
    public void Manifest_ValidClipsCarried_InvalidDropped_DuplicateKeepsFirst()
    {
        var options = WithOneStory();
        options.Voice.Add(new ContentSyncVoiceOptions
        { VoiceId = "greet-01", Sha256 = ValidSha, SizeBytes = 10 });
        options.Voice.Add(new ContentSyncVoiceOptions
        { VoiceId = "", Sha256 = ValidSha, SizeBytes = 10 });            // no id
        options.Voice.Add(new ContentSyncVoiceOptions
        { VoiceId = "bad-sha", Sha256 = "xx", SizeBytes = 10 });         // bad sha
        options.Voice.Add(new ContentSyncVoiceOptions
        { VoiceId = "no-size", Sha256 = ValidSha, SizeBytes = 0 });      // no size
        options.Voice.Add(new ContentSyncVoiceOptions
        { VoiceId = "greet-01", Sha256 = ValidSha, SizeBytes = 99 });    // dup

        var manifest = new ContentManifestService(options).Build();

        Assert.NotNull(manifest.Voice);
        var clip = Assert.Single(manifest.Voice!);
        Assert.Equal("greet-01", clip.VoiceId);
        Assert.Equal(10, clip.SizeBytes);
        Assert.Equal("/api/devices/content-file?voiceId=greet-01", clip.AudioUrl);
        Assert.True(clip.Enabled);
    }

    // KEYSTONE: a deployment with no voice clips has a byte-identical wire
    // shape to before this slice (Voice null), so every shipped toy sees no
    // change until the owner actually configures rendered clips.
    [Fact]
    public void Manifest_NoVoiceClips_FieldStaysNull()
    {
        Assert.Null(new ContentManifestService(WithOneStory()).Build().Voice);
    }

    // A voice-only deployment (no stories, no music) must still produce a
    // manifest — the early "nothing configured" return has to account for
    // the third namespace or the toy would boot silent forever.
    [Fact]
    public void Manifest_VoiceOnly_IsNotTreatedAsEmpty()
    {
        var options = new ContentSyncOptions { Enabled = true };
        options.Voice.Add(new ContentSyncVoiceOptions
        { VoiceId = "greet-01", Sha256 = ValidSha, SizeBytes = 10 });

        var manifest = new ContentManifestService(options).Build();

        Assert.NotNull(manifest.Voice);
        Assert.Single(manifest.Voice!);
    }

    [Fact]
    public void Manifest_MasterSwitchOff_DropsVoiceClipsToo()
    {
        var options = new ContentSyncOptions { Enabled = false };
        options.Voice.Add(new ContentSyncVoiceOptions
        { VoiceId = "greet-01", Sha256 = ValidSha, SizeBytes = 10 });

        Assert.Null(new ContentManifestService(options).Build().Voice);
    }

    // ---- content-file addressing ----

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
    public void ContentFile_VoiceId_StreamsClipFile()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp3");
        File.WriteAllBytes(file, [0xFF, 0xF3]);
        try
        {
            var options = new ContentSyncOptions { Enabled = true };
            options.Voice.Add(new ContentSyncVoiceOptions
            {
                VoiceId = "greet-01", Sha256 = ValidSha, SizeBytes = 2, AudioPath = file,
            });

            var result = Controller().GetContentFile(
                options, Substitute.For<ILogger<DeviceController>>(), voiceId: "greet-01");

            var physical = Assert.IsType<PhysicalFileResult>(result);
            Assert.Equal(file, physical.FileName);
            Assert.Equal("audio/mpeg", physical.ContentType);
        }
        finally
        {
            File.Delete(file);
        }
    }

    // voiceId is a lookup key against configured items and never reaches the
    // filesystem — a traversal-shaped id is simply an id that matches nothing.
    [Theory]
    [InlineData("unknown-clip")]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows\\win.ini")]
    public void ContentFile_UnknownVoiceId_Uniform404(string voiceId)
    {
        var options = new ContentSyncOptions { Enabled = true };
        options.Voice.Add(new ContentSyncVoiceOptions
        {
            VoiceId = "greet-01", Sha256 = ValidSha, SizeBytes = 2, AudioPath = "/abs/g.mp3",
        });

        var result = Controller().GetContentFile(
            options, Substitute.For<ILogger<DeviceController>>(), voiceId: voiceId);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ---- Slice 3: the offer / reoffer story clip kinds ----

    [Fact]
    public void Manifest_OfferAndReofferClips_AreAllowedKinds()
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            Stories = { new ContentSyncStoryOptions
            {
                StoryId = "anban-huri", Sha256 = StorySha, SizeBytes = 1,
                Clips =
                {
                    new ContentSyncClipOptions
                    { Kind = "offer", Sha256 = ValidSha, SizeBytes = 5 },
                    new ContentSyncClipOptions
                    { Kind = "reoffer", Sha256 = ValidSha, SizeBytes = 6 },
                    new ContentSyncClipOptions
                    { Kind = "offer-again", Sha256 = ValidSha, SizeBytes = 7 }, // not a kind
                },
            } },
        };

        var story = Assert.Single(new ContentManifestService(options).Build().Stories);

        Assert.NotNull(story.Clips);
        Assert.Equal(2, story.Clips!.Count);
        Assert.Contains(story.Clips, c => c.Kind == "offer");
        Assert.Contains(story.Clips, c => c.Kind == "reoffer");
        Assert.Equal(
            "/api/devices/content-file?storyId=anban-huri&clip=offer",
            story.Clips.First(c => c.Kind == "offer").AudioUrl);
    }

    // The firmware's CS_CLIP_KIND_LEN is 10; a kind longer than that would
    // be silently truncated on the card and never resolve. Pin the bound so
    // a future kind name cannot quietly break clip resolution on-device.
    [Fact]
    public void AllowedClipKinds_AllFitTheFirmwareKindLength()
    {
        Assert.All(ContentSyncClipOptions.AllowedKinds, kind => Assert.True(
            kind.Length <= 10, $"clip kind '{kind}' exceeds CS_CLIP_KIND_LEN (10)"));
    }

    // ---- Slice 4: the four parent mode flags on the manifest ----

    private static DeviceController ControllerFor(
        IDeviceService deviceService, Guid deviceId)
    {
        var controller = new DeviceController(
            deviceService, Substitute.For<IConfiguration>());
        var http = new DefaultHttpContext();
        http.Items["DeviceId"] = deviceId;
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    [Fact]
    public async Task ContentManifest_CarriesTheFourModeFlags()
    {
        var deviceId = Guid.NewGuid();
        var deviceService = Substitute.For<IDeviceService>();
        deviceService.GetDeviceAsync(deviceId).Returns(new Device
        { Id = deviceId, MacAddress = "m", Name = "toy" });
        deviceService.IsModeEnabledForRequestAsync(
            deviceId, Arg.Any<Guid?>(), DetectedMode.Story).Returns(true);
        deviceService.IsModeEnabledForRequestAsync(
            deviceId, Arg.Any<Guid?>(), DetectedMode.Game).Returns(false);
        deviceService.IsModeEnabledForRequestAsync(
            deviceId, Arg.Any<Guid?>(), DetectedMode.Riddle).Returns(true);
        deviceService.IsModeEnabledForRequestAsync(
            deviceId, Arg.Any<Guid?>(), DetectedMode.Curiosity).Returns(false);

        var manifest = Substitute.For<IContentManifestService>();
        manifest.Build().Returns(ContentManifestResponse.Empty());

        var ok = Assert.IsType<OkObjectResult>(
            await ControllerFor(deviceService, deviceId)
                .GetContentManifest(manifest, Substitute.For<IChildService>()));
        var body = Assert.IsType<ContentManifestResponse>(ok.Value);

        Assert.True(body.StoryEnabled);
        Assert.False(body.GameEnabled);
        Assert.True(body.RiddleEnabled);
        Assert.False(body.CuriosityEnabled);
    }

    // KEYSTONE: the flags are resolved through IsModeEnabledForRequestAsync
    // with the device's DEFAULT CHILD, not from the raw Device columns — a
    // child-level override must reach the toy, or the welcome prompt would
    // offer a mode the chat gate then refuses.
    [Fact]
    public async Task ContentManifest_ModeFlags_HonorTheDefaultChildsOverride()
    {
        var deviceId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var childService = Substitute.For<IChildService>();
        childService.GetDefaultChildForDeviceAsync(deviceId).Returns(new Child
        { Id = childId, DeviceId = deviceId, Name = "Ani" });

        var deviceService = Substitute.For<IDeviceService>();
        deviceService.GetDeviceAsync(deviceId).Returns(new Device
        { Id = deviceId, MacAddress = "m", Name = "toy" });
        deviceService.IsModeEnabledForRequestAsync(
            deviceId, Arg.Any<Guid?>(), Arg.Any<DetectedMode>()).Returns(true);

        var manifest = Substitute.For<IContentManifestService>();
        manifest.Build().Returns(ContentManifestResponse.Empty());

        await ControllerFor(deviceService, deviceId)
            .GetContentManifest(manifest, childService);

        // The child's id — not null — is what reaches the resolver, which is
        // the only way its per-child override can apply.
        await deviceService.Received().IsModeEnabledForRequestAsync(
            deviceId, childId, DetectedMode.Story);
        await deviceService.Received().IsModeEnabledForRequestAsync(
            deviceId, childId, DetectedMode.Game);
    }
}

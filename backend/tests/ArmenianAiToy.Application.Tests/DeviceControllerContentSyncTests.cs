using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Api.Middleware;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Endpoint + auth-gate tests for the Cloud→SD content-sync slice:
///  - content-manifest returns the service's manifest to an authed device;
///  - content-file streams / fail-closes exactly like firmware-image;
///  - BOTH new paths are device-authed at the middleware (an unauth caller
///    401s before any controller runs; revoked devices are rejected by
///    ValidateDeviceAsync — pinned in DeviceServiceOtaTests).
/// </summary>
public class DeviceControllerContentSyncTests
{
    private static DeviceController Controller()
    {
        var controller = new DeviceController(
            Substitute.For<IDeviceService>(), Substitute.For<IConfiguration>());
        var http = new DefaultHttpContext();
        http.Items["DeviceId"] = Guid.NewGuid();
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    // ---- content-manifest ----

    [Fact]
    public async Task ContentManifest_ReturnsServiceManifest_WithIntroFlagStamped()
    {
        var manifest = Substitute.For<IContentManifestService>();
        var response = new ContentManifestResponse(new[]
        {
            new ContentStoryItem("anban-huri", 1, "t", "/api/devices/content-file",
                new string('a', 64), 123, true),
        });
        manifest.Build().Returns(response);

        var result = await Controller().GetContentManifest(manifest);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ContentManifestResponse>(ok.Value);
        // Stories pass through untouched; the B3 intro flag is stamped by the
        // controller (defaults ON when the device row can't be loaded).
        Assert.Same(response.Stories, body.Stories);
        Assert.True(body.StoryIntroEnabled);
    }

    [Fact]
    public async Task ContentManifest_EmptyWhenServiceEmpty()
    {
        var manifest = Substitute.For<IContentManifestService>();
        manifest.Build().Returns(ContentManifestResponse.Empty());

        var ok = Assert.IsType<OkObjectResult>(await Controller().GetContentManifest(manifest));
        var body = Assert.IsType<ContentManifestResponse>(ok.Value);
        Assert.Empty(body.Stories);
    }

    // ---- content-file (fail-closed, mirrors firmware-image) ----

    private static ContentSyncOptions FileOptions(bool enabled, string audioPath) => new()
    {
        Enabled = enabled,
        StoryId = "anban-huri",
        AudioPath = audioPath,
        Sha256 = new string('a', 64),
        SizeBytes = 4,
    };

    [Fact]
    public void ContentFile_Disabled_Returns404()
    {
        var result = Controller().GetContentFile(
            FileOptions(enabled: false, audioPath: Path.GetTempFileName()),
            Substitute.For<ILogger<DeviceController>>());
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void ContentFile_NoPath_Returns404()
    {
        var result = Controller().GetContentFile(
            FileOptions(enabled: true, audioPath: ""),
            Substitute.For<ILogger<DeviceController>>());
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void ContentFile_MissingFile_Returns404()
    {
        var result = Controller().GetContentFile(
            FileOptions(enabled: true,
                audioPath: Path.Combine(Path.GetTempPath(), $"no-{Guid.NewGuid():N}.mp3")),
            Substitute.For<ILogger<DeviceController>>());
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void ContentFile_RelativePath_Returns404()
    {
        var result = Controller().GetContentFile(
            FileOptions(enabled: true, audioPath: "relative/story.mp3"),
            Substitute.For<ILogger<DeviceController>>());
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void ContentFile_Present_StreamsAsMpegWithRangeSupport()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(file, new byte[] { 0xFF, 0xFB, 0x90, 0x00 }); // mp3-ish bytes
            var result = Controller().GetContentFile(
                FileOptions(enabled: true, audioPath: file),
                Substitute.For<ILogger<DeviceController>>());

            var physical = Assert.IsType<PhysicalFileResult>(result);
            Assert.Equal(file, physical.FileName);
            Assert.Equal("audio/mpeg", physical.ContentType);
            Assert.True(physical.EnableRangeProcessing);
        }
        finally
        {
            File.Delete(file);
        }
    }

    // ---- content-file: per-story addressing (multi-story) ----------

    private static ContentSyncOptions MultiFileOptions(params (string Id, string Path)[] stories) =>
        new()
        {
            Enabled = true,
            Stories = stories.Select(s => new ContentSyncStoryOptions
            {
                StoryId = s.Id,
                AudioPath = s.Path,
                Sha256 = new string('a', 64),
                SizeBytes = 4,
            }).ToList(),
        };

    private static string TempMp3()
    {
        var file = Path.GetTempFileName();
        File.WriteAllBytes(file, new byte[] { 0xFF, 0xFB, 0x90, 0x00 });
        return file;
    }

    [Fact]
    public void ContentFile_ByStoryId_ServesThatStorysFile()
    {
        var first = TempMp3();
        var second = TempMp3();
        try
        {
            var result = Controller().GetContentFile(
                MultiFileOptions(("anban-huri", first), ("little-cloud", second)),
                Substitute.For<ILogger<DeviceController>>(),
                storyId: "little-cloud");

            var physical = Assert.IsType<PhysicalFileResult>(result);
            Assert.Equal(second, physical.FileName);   // NOT the first item
            Assert.Equal("audio/mpeg", physical.ContentType);
            Assert.True(physical.EnableRangeProcessing);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact]
    public void ContentFile_UnknownStoryId_Returns404()
    {
        var file = TempMp3();
        try
        {
            var result = Controller().GetContentFile(
                MultiFileOptions(("anban-huri", file)),
                Substitute.For<ILogger<DeviceController>>(),
                storyId: "not-a-story");
            Assert.IsType<NotFoundObjectResult>(result);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>With several stories configured and no storyId supplied,
    /// guessing which one the device meant would be worse than refusing.</summary>
    [Fact]
    public void ContentFile_NoStoryId_MultipleConfigured_Returns404()
    {
        var first = TempMp3();
        var second = TempMp3();
        try
        {
            var result = Controller().GetContentFile(
                MultiFileOptions(("anban-huri", first), ("little-cloud", second)),
                Substitute.For<ILogger<DeviceController>>());
            Assert.IsType<NotFoundObjectResult>(result);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    /// <summary>BACK-COMPAT KEYSTONE: firmware flashed before multi-story
    /// requests the bare route with no storyId. A legacy single-item config
    /// must still stream, or the bench breaks.</summary>
    [Fact]
    public void ContentFile_NoStoryId_LegacySingleItem_StillStreams()
    {
        var file = TempMp3();
        try
        {
            var result = Controller().GetContentFile(
                FileOptions(enabled: true, audioPath: file),
                Substitute.For<ILogger<DeviceController>>());

            var physical = Assert.IsType<PhysicalFileResult>(result);
            Assert.Equal(file, physical.FileName);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ContentFile_StoryId_IsCaseInsensitive()
    {
        var file = TempMp3();
        try
        {
            var result = Controller().GetContentFile(
                MultiFileOptions(("anban-huri", file)),
                Substitute.For<ILogger<DeviceController>>(),
                storyId: "ANBAN-HURI");
            Assert.IsType<PhysicalFileResult>(result);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>storyId is only ever a lookup key against configured items;
    /// it must never reach the filesystem. A traversal-shaped value simply
    /// matches nothing.</summary>
    [Theory]
    [InlineData("../../../../etc/passwd")]
    [InlineData("..\\..\\windows\\system32\\config\\sam")]
    [InlineData("/etc/shadow")]
    public void ContentFile_TraversalShapedStoryId_Returns404(string storyId)
    {
        var file = TempMp3();
        try
        {
            var result = Controller().GetContentFile(
                MultiFileOptions(("anban-huri", file)),
                Substitute.For<ILogger<DeviceController>>(),
                storyId: storyId);
            Assert.IsType<NotFoundObjectResult>(result);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ContentFile_Disabled_WithStoriesConfigured_Returns404()
    {
        var file = TempMp3();
        try
        {
            var options = MultiFileOptions(("anban-huri", file));
            options.Enabled = false;

            var result = Controller().GetContentFile(
                options, Substitute.For<ILogger<DeviceController>>(), storyId: "anban-huri");
            Assert.IsType<NotFoundObjectResult>(result);
        }
        finally
        {
            File.Delete(file);
        }
    }

    // ---- middleware auth gate: the new paths REQUIRE device auth ----

    [Theory]
    [InlineData("/api/devices/content-manifest")]
    [InlineData("/api/devices/content-file")]
    public async Task Middleware_UnauthenticatedCaller_Gets401_BeforeAnyController(string path)
    {
        var reachedPipeline = false;
        var middleware = new DeviceAuthMiddleware(
            next: _ => { reachedPipeline = true; return Task.CompletedTask; },
            Substitute.For<ILogger<DeviceAuthMiddleware>>());
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path; // no X-Device-Id / X-Api-Key headers

        await middleware.InvokeAsync(ctx);

        Assert.Equal(401, ctx.Response.StatusCode);
        Assert.False(reachedPipeline); // short-circuited before controllers
    }

    [Fact]
    public async Task Middleware_UnrelatedPath_PassesThrough()
    {
        var reachedPipeline = false;
        var middleware = new DeviceAuthMiddleware(
            next: _ => { reachedPipeline = true; return Task.CompletedTask; },
            Substitute.For<ILogger<DeviceAuthMiddleware>>());
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/parents/login"; // parent-JWT surface, not device-authed

        await middleware.InvokeAsync(ctx);

        Assert.True(reachedPipeline);
        Assert.Equal(200, ctx.Response.StatusCode);
    }
}

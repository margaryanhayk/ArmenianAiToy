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
    public void ContentManifest_ReturnsServiceManifest()
    {
        var manifest = Substitute.For<IContentManifestService>();
        var response = new ContentManifestResponse(new[]
        {
            new ContentStoryItem("anban-huri", 1, "t", "/api/devices/content-file",
                new string('a', 64), 123, true),
        });
        manifest.Build().Returns(response);

        var result = Controller().GetContentManifest(manifest);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, ok.Value);
    }

    [Fact]
    public void ContentManifest_EmptyWhenServiceEmpty()
    {
        var manifest = Substitute.For<IContentManifestService>();
        manifest.Build().Returns(ContentManifestResponse.Empty());

        var ok = Assert.IsType<OkObjectResult>(Controller().GetContentManifest(manifest));
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

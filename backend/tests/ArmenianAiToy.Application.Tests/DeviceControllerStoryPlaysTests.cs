using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Api.Middleware;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Endpoint + auth-gate tests for POST /api/devices/story-plays (Slice A):
/// request-shape validation at the controller, delegation to the service with
/// the middleware-resolved device id, and the middleware 401 for
/// unauthenticated callers (revoked devices are rejected by
/// ValidateDeviceAsync — pinned in DeviceServiceOtaTests).
/// </summary>
public class DeviceControllerStoryPlaysTests
{
    private static (DeviceController Controller, IDeviceService Service, Guid DeviceId) Create()
    {
        var service = Substitute.For<IDeviceService>();
        service.HasLinkedParentAsync(Arg.Any<Guid>()).Returns(true);
        var controller = new DeviceController(service, Substitute.For<IConfiguration>());
        var http = new DefaultHttpContext();
        var deviceId = Guid.NewGuid();
        http.Items["DeviceId"] = deviceId;
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return (controller, service, deviceId);
    }

    [Fact]
    public async Task ReportStoryPlays_ValidBatch_DelegatesWithMiddlewareDeviceId()
    {
        var (controller, service, deviceId) = Create();
        service.ReportStoryPlaysAsync(deviceId, Arg.Any<StoryPlayReportRequest>(), Arg.Any<DateTime>())
            .Returns(2);
        var request = new StoryPlayReportRequest(
        [
            new StoryPlayReportEvent("b1-1", "anban-huri", true, "sd", 60),
            new StoryPlayReportEvent("b1-2", "ulik", false, "sd", 5),
        ]);

        var result = await controller.ReportStoryPlays(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        var accepted = (int)ok.Value!.GetType().GetProperty("accepted")!.GetValue(ok.Value)!;
        Assert.Equal(2, accepted);
        await service.Received(1).ReportStoryPlaysAsync(
            deviceId, request, Arg.Any<DateTime>());
    }

    [Fact]
    public async Task ReportStoryPlays_EmptyOrMissingEvents_Returns400_WithoutServiceCall()
    {
        var (controller, service, _) = Create();

        Assert.IsType<BadRequestObjectResult>(
            await controller.ReportStoryPlays(new StoryPlayReportRequest(null)));
        Assert.IsType<BadRequestObjectResult>(
            await controller.ReportStoryPlays(new StoryPlayReportRequest([])));
        Assert.IsType<BadRequestObjectResult>(
            await controller.ReportStoryPlays(null!));

        await service.DidNotReceiveWithAnyArgs()
            .ReportStoryPlaysAsync(default, default!, default);
    }

    [Fact]
    public async Task ReportStoryPlays_OversizedBatch_Returns400_WithoutServiceCall()
    {
        var (controller, service, _) = Create();
        var events = Enumerable.Range(0, StoryPlayReportRequest.MaxEvents + 1)
            .Select(i => new StoryPlayReportEvent("k" + i, "s", false, "sd", 1))
            .ToList();

        Assert.IsType<BadRequestObjectResult>(
            await controller.ReportStoryPlays(new StoryPlayReportRequest(events)));

        await service.DidNotReceiveWithAnyArgs()
            .ReportStoryPlaysAsync(default, default!, default);
    }

    // KEYSTONE: the path is in DeviceAuthMiddleware's device-auth list — an
    // unauthenticated caller 401s before any controller code runs.
    [Fact]
    public async Task Middleware_UnauthenticatedCaller_Gets401_BeforeAnyController()
    {
        var reachedPipeline = false;
        var middleware = new DeviceAuthMiddleware(
            next: _ => { reachedPipeline = true; return Task.CompletedTask; },
            Substitute.For<ILogger<DeviceAuthMiddleware>>());
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/devices/story-plays"; // no auth headers

        await middleware.InvokeAsync(ctx);

        Assert.Equal(401, ctx.Response.StatusCode);
        Assert.False(reachedPipeline);
    }
}

using System.Security.Claims;
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
/// Endpoint + auth-gate tests for the two offline-game-reporting routes:
/// <c>POST /api/devices/game-plays</c> (device-authed ingest) and
/// <c>GET /api/parents/devices/{deviceId}/game-plays</c> (parent read).
/// Mirrors <see cref="DeviceControllerStoryPlaysTests"/> and
/// <see cref="ParentControllerStoryPlaysTests"/>; the ownership and ingest
/// logic themselves are pinned in <see cref="GamePlayTests"/>.
/// </summary>
public class GamePlayControllerTests
{
    private static (DeviceController Controller, IDeviceService Service, Guid DeviceId)
        CreateDeviceController()
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

    private static (ParentController Controller, IParentService Service, Guid ParentId)
        CreateParentController()
    {
        var service = Substitute.For<IParentService>();
        var controller = new ParentController(service, new ExportCooldown());
        var parentId = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, parentId.ToString())], "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
        return (controller, service, parentId);
    }

    // ---------------------------------------------------------------
    // Device ingest
    // ---------------------------------------------------------------

    [Fact]
    public async Task ReportGamePlays_ValidBatch_DelegatesWithMiddlewareDeviceId()
    {
        var (controller, service, deviceId) = CreateDeviceController();
        service.ReportGamePlaysAsync(deviceId, Arg.Any<GamePlayReportRequest>(), Arg.Any<DateTime>())
            .Returns(2);
        var request = new GamePlayReportRequest(
        [
            new GamePlayReportEvent("b1-1", "mind-reader", 4, "won", null, 60),
            new GamePlayReportEvent("b1-2", "button-simon", 3, "lost", 4, 5),
        ]);

        var result = await controller.ReportGamePlays(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        var accepted = (int)ok.Value!.GetType().GetProperty("accepted")!.GetValue(ok.Value)!;
        Assert.Equal(2, accepted);
        await service.Received(1).ReportGamePlaysAsync(deviceId, request, Arg.Any<DateTime>());
    }

    [Fact]
    public async Task ReportGamePlays_EmptyOrMissingEvents_Returns400_WithoutServiceCall()
    {
        var (controller, service, _) = CreateDeviceController();

        Assert.IsType<BadRequestObjectResult>(
            await controller.ReportGamePlays(new GamePlayReportRequest(null)));
        Assert.IsType<BadRequestObjectResult>(
            await controller.ReportGamePlays(new GamePlayReportRequest([])));
        Assert.IsType<BadRequestObjectResult>(
            await controller.ReportGamePlays(null!));

        await service.DidNotReceiveWithAnyArgs()
            .ReportGamePlaysAsync(default, default!, default);
    }

    [Fact]
    public async Task ReportGamePlays_OversizedBatch_Returns400_WithoutServiceCall()
    {
        var (controller, service, _) = CreateDeviceController();
        var events = Enumerable.Range(0, GamePlayReportRequest.MaxEvents + 1)
            .Select(i => new GamePlayReportEvent("k" + i, "who-first"))
            .ToList();

        Assert.IsType<BadRequestObjectResult>(
            await controller.ReportGamePlays(new GamePlayReportRequest(events)));

        await service.DidNotReceiveWithAnyArgs()
            .ReportGamePlaysAsync(default, default!, default);
    }

    // KEYSTONE: the path is in DeviceAuthMiddleware's device-auth list — an
    // unauthenticated caller 401s before any controller code runs, and a
    // revoked toy is rejected by ValidateDeviceAsync on the same path.
    [Fact]
    public async Task Middleware_UnauthenticatedCaller_Gets401_BeforeAnyController()
    {
        var reachedPipeline = false;
        var middleware = new DeviceAuthMiddleware(
            next: _ => { reachedPipeline = true; return Task.CompletedTask; },
            Substitute.For<ILogger<DeviceAuthMiddleware>>());
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/devices/game-plays"; // no auth headers

        await middleware.InvokeAsync(ctx);

        Assert.Equal(401, ctx.Response.StatusCode);
        Assert.False(reachedPipeline);
    }

    // ---------------------------------------------------------------
    // Parent read
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetGamePlays_ReturnsServiceResult()
    {
        var (controller, service, parentId) = CreateParentController();
        var deviceId = Guid.NewGuid();
        var response = new GamePlaysResponse(
            [new GamePlayDto("button-simon", 4, "lost", 3, DateTime.UtcNow, false)],
            [new GamePlayTotalDto("button-simon", 1)],
            1);
        service.GetGamePlaysAsync(parentId, deviceId, 20, 0).Returns(response);

        var result = await controller.GetGamePlays(deviceId);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, ok.Value);
    }

    [Fact]
    public async Task GetGamePlays_NotLinked_Returns404()
    {
        var (controller, service, parentId) = CreateParentController();
        var deviceId = Guid.NewGuid();
        service.GetGamePlaysAsync(parentId, deviceId, 20, 0)
            .Returns((GamePlaysResponse?)null);

        Assert.IsType<NotFoundObjectResult>(await controller.GetGamePlays(deviceId));
    }

    [Theory]
    [InlineData(-1, 20)]  // offset < 0
    [InlineData(0, 0)]    // limit < 1
    [InlineData(0, -5)]   // limit < 1
    public async Task GetGamePlays_BadPagination_Returns400_WithoutServiceCall(
        int offset, int limit)
    {
        var (controller, service, _) = CreateParentController();

        var result = await controller.GetGamePlays(Guid.NewGuid(), limit, offset);

        Assert.IsType<BadRequestObjectResult>(result);
        await service.DidNotReceiveWithAnyArgs()
            .GetGamePlaysAsync(default, default, default, default);
    }

    [Fact]
    public async Task GetGamePlays_OversizedLimit_ClampedTo100()
    {
        var (controller, service, parentId) = CreateParentController();
        var deviceId = Guid.NewGuid();
        service.GetGamePlaysAsync(parentId, deviceId, 100, 0)
            .Returns(new GamePlaysResponse([], [], 0));

        var result = await controller.GetGamePlays(deviceId, limit: 500, offset: 0);

        Assert.IsType<OkObjectResult>(result);
        await service.Received(1).GetGamePlaysAsync(parentId, deviceId, 100, 0);
    }
}

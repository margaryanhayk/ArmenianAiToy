using System.Security.Claims;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Controller-shape tests for GET /api/parents/devices/{deviceId}/story-plays:
/// the standard pagination contract (offset &lt; 0 / limit &lt; 1 → 400;
/// limit clamped to 100) and the silent 404 for a device the service reports
/// as not linked. Ownership logic itself is pinned in StoryPlayTests.
/// </summary>
public class ParentControllerStoryPlaysTests
{
    private static (ParentController Controller, IParentService Service, Guid ParentId)
        CreateController()
    {
        var service = Substitute.For<IParentService>();
        var controller = new ParentController(service, new ExportCooldown());
        var parentId = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, parentId.ToString())
        }, "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
        return (controller, service, parentId);
    }

    private static StoryPlaysResponse EmptyResponse() => new([], [], 0);

    [Fact]
    public async Task GetStoryPlays_ReturnsServiceResult()
    {
        var (controller, service, parentId) = CreateController();
        var deviceId = Guid.NewGuid();
        var response = new StoryPlaysResponse(
            [new StoryPlayDto("anban-huri", true, "sd", DateTime.UtcNow, false)],
            [new StoryPlayTotalDto("anban-huri", 1, 1)],
            1);
        service.GetStoryPlaysAsync(parentId, deviceId, 20, 0).Returns(response);

        var result = await controller.GetStoryPlays(deviceId);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, ok.Value);
    }

    [Fact]
    public async Task GetStoryPlays_NotLinked_Returns404()
    {
        var (controller, service, parentId) = CreateController();
        var deviceId = Guid.NewGuid();
        service.GetStoryPlaysAsync(parentId, deviceId, 20, 0)
            .Returns((StoryPlaysResponse?)null);

        Assert.IsType<NotFoundObjectResult>(await controller.GetStoryPlays(deviceId));
    }

    [Theory]
    [InlineData(-1, 20)]  // offset < 0
    [InlineData(0, 0)]    // limit < 1
    [InlineData(0, -5)]   // limit < 1
    public async Task GetStoryPlays_BadPagination_Returns400_WithoutServiceCall(
        int offset, int limit)
    {
        var (controller, service, _) = CreateController();

        var result = await controller.GetStoryPlays(Guid.NewGuid(), limit, offset);

        Assert.IsType<BadRequestObjectResult>(result);
        await service.DidNotReceiveWithAnyArgs()
            .GetStoryPlaysAsync(default, default, default, default);
    }

    [Fact]
    public async Task GetStoryPlays_OversizedLimit_ClampedTo100()
    {
        var (controller, service, parentId) = CreateController();
        var deviceId = Guid.NewGuid();
        service.GetStoryPlaysAsync(parentId, deviceId, 100, 0).Returns(EmptyResponse());

        var result = await controller.GetStoryPlays(deviceId, limit: 500, offset: 0);

        Assert.IsType<OkObjectResult>(result);
        await service.Received(1).GetStoryPlaysAsync(parentId, deviceId, 100, 0);
    }
}

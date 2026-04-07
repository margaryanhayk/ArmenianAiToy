using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using System.Security.Claims;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Controller-level tests for the pagination guard on the parent list
/// endpoints. Validation lives as a private static helper inside
/// ConversationController; we exercise it through the public actions and
/// assert the resulting IActionResult.
/// </summary>
public class ConversationControllerPaginationTests
{
    private static (ConversationController Controller, IConversationService Convos, IParentService Parents, Guid DeviceId)
        CreateController(bool ownsDevice = true)
    {
        var convos = Substitute.For<IConversationService>();
        var parents = Substitute.For<IParentService>();

        var deviceId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        parents.GetLinkedDeviceIdsAsync(Arg.Any<Guid>())
            .Returns(ownsDevice ? new List<Guid> { deviceId } : new List<Guid>());

        var controller = new ConversationController(convos, parents);
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, parentId.ToString())
        }, "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
        return (controller, convos, parents, deviceId);
    }

    private static string? ExtractError(IActionResult result)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        // Anonymous { error = "..." } payload — read via reflection.
        var prop = bad.Value!.GetType().GetProperty("error");
        return prop?.GetValue(bad.Value) as string;
    }

    [Fact]
    public async Task GetHistory_NegativeOffset_ReturnsBadRequest()
    {
        var (controller, _, _, deviceId) = CreateController();

        var result = await controller.GetHistory(deviceId, limit: 10, offset: -1);

        var error = ExtractError(result);
        Assert.NotNull(error);
        Assert.Contains("offset", error);
    }

    [Fact]
    public async Task GetHistory_ZeroLimit_ReturnsBadRequest()
    {
        var (controller, _, _, deviceId) = CreateController();

        var result = await controller.GetHistory(deviceId, limit: 0, offset: 0);

        var error = ExtractError(result);
        Assert.NotNull(error);
        Assert.Contains("limit", error);
    }

    [Fact]
    public async Task GetSummary_NegativeLimit_ReturnsBadRequest()
    {
        var (controller, _, _, deviceId) = CreateController();

        var result = await controller.GetSummary(deviceId, limit: -5, offset: 0);

        var error = ExtractError(result);
        Assert.NotNull(error);
        Assert.Contains("limit", error);
    }

    [Fact]
    public async Task GetFlagged_LimitAboveMax_ClampsToHundredAndCallsService()
    {
        var (controller, convos, _, deviceId) = CreateController();
        convos.GetFlaggedMessagesAsync(deviceId, 100, 0)
            .Returns(new List<FlaggedMessageDto>());

        var result = await controller.GetFlagged(deviceId, limit: 500, offset: 0);

        Assert.IsType<OkObjectResult>(result);
        await convos.Received(1).GetFlaggedMessagesAsync(deviceId, 100, 0);
        // And confirm the un-clamped value was never used.
        await convos.DidNotReceive().GetFlaggedMessagesAsync(deviceId, 500, 0);
    }

    [Fact]
    public async Task GetSummary_ValidRequest_ReturnsOk()
    {
        var (controller, convos, _, deviceId) = CreateController();
        convos.GetConversationSummariesAsync(deviceId, 20, 0)
            .Returns(new List<ConversationSummaryDto>());

        var result = await controller.GetSummary(deviceId, limit: 20, offset: 0);

        Assert.IsType<OkObjectResult>(result);
        await convos.Received(1).GetConversationSummariesAsync(deviceId, 20, 0);
    }
}

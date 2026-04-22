using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using System.Security.Claims;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Controller-level tests for ParentController.UnlinkDevice. Pins two
/// invariants:
///
///  1. The parentId passed to IParentService.UnlinkDeviceAsync is the one
///     carried by the JWT claim — a controller regression that read the
///     parent id from the route/body could let one parent unlink another
///     parent's link.
///
///  2. The response shape is identical whether a link existed or not — no
///     existence leak. A caller must not be able to probe whether a given
///     (parent, device) pair is real.
/// </summary>
public class ParentControllerUnlinkDeviceTests
{
    private static (ParentController Controller, IParentService Parents, Guid ParentId) CreateController()
    {
        var parents = Substitute.For<IParentService>();
        var parentId = Guid.NewGuid();
        var controller = new ParentController(parents, new ExportCooldown());
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, parentId.ToString())
        }, "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
        return (controller, parents, parentId);
    }

    [Fact]
    public async Task UnlinkDevice_CallsServiceWithClaimParentId_ReturnsOk()
    {
        var (controller, parents, parentId) = CreateController();
        var deviceId = Guid.NewGuid();
        parents.UnlinkDeviceAsync(parentId, deviceId).Returns(true);

        var result = await controller.UnlinkDevice(deviceId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var unlinkedProp = ok.Value!.GetType().GetProperty("unlinked");
        Assert.True((bool)unlinkedProp!.GetValue(ok.Value)!);
        await parents.Received(1).UnlinkDeviceAsync(parentId, deviceId);
    }

    [Fact]
    public async Task UnlinkDevice_WhenServiceReturnsFalse_StillReturnsSameOkShape()
    {
        // No existence leak: the response must not differ between
        // "was linked" and "was never linked".
        var (controller, parents, parentId) = CreateController();
        var deviceId = Guid.NewGuid();
        parents.UnlinkDeviceAsync(parentId, deviceId).Returns(false);

        var result = await controller.UnlinkDevice(deviceId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var unlinkedProp = ok.Value!.GetType().GetProperty("unlinked");
        Assert.True((bool)unlinkedProp!.GetValue(ok.Value)!);
    }
}

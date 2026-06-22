using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using System.Security.Claims;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Controller-level ownership regression tests for ChildController.
///
/// Pins the parent-trust invariant shared by its two authenticated endpoints:
/// before creating or reading children for a device, the controller must
/// verify the caller's linked-device set includes that device and short-
/// circuit with Forbid otherwise — without ever calling IChildService.
///
/// Children's profiles (name, gender, DOB) are the most sensitive PII the
/// system holds; a regression that reversed the ownership check would
/// leak one family's child records into another parent's session.
/// </summary>
public class ChildControllerOwnershipTests
{
    private static (ChildController Controller,
                    IChildService ChildService,
                    IParentService Parents,
                    Guid OwnedDeviceId,
                    Guid UnlinkedDeviceId)
        CreateController()
    {
        var childService = Substitute.For<IChildService>();
        var parents = Substitute.For<IParentService>();

        var parentId = Guid.NewGuid();
        var ownedDeviceId = Guid.NewGuid();
        var unlinkedDeviceId = Guid.NewGuid();

        parents.GetLinkedDeviceIdsAsync(Arg.Any<Guid>())
            .Returns(new List<Guid> { ownedDeviceId });

        var controller = new ChildController(childService, parents);
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, parentId.ToString())
        }, "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
        return (controller, childService, parents, ownedDeviceId, unlinkedDeviceId);
    }

    private static Child MakeChild(Guid deviceId) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Ani",
        Gender = Gender.Girl,
        DeviceId = deviceId
    };

    // ─────────────────────────────────────────────────────────────────────
    // CreateChild — ownership short-circuit + positive sanity
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateChild_WhenDeviceNotLinked_ReturnsForbid_AndDoesNotCallService()
    {
        var (controller, childService, _, _, unlinkedDeviceId) = CreateController();
        var request = new CreateChildRequest(unlinkedDeviceId, "Ani", Gender.Girl);

        var result = await controller.CreateChild(request);

        Assert.IsType<ForbidResult>(result);
        await childService.DidNotReceiveWithAnyArgs()
            .CreateChildAsync(default, default!, default, default);
    }

    [Fact]
    public async Task CreateChild_WhenLinked_ReturnsCreated()
    {
        // Anti-tautology guard so the Forbid test above cannot degrade into
        // "always Forbid". Also proves the ownership check does not incorrectly
        // reject owned devices.
        var (controller, childService, _, ownedDeviceId, _) = CreateController();
        childService.CreateChildAsync(ownedDeviceId, "Ani", Gender.Girl, null)
            .Returns(MakeChild(ownedDeviceId));
        var request = new CreateChildRequest(ownedDeviceId, "Ani", Gender.Girl);

        var result = await controller.CreateChild(request);

        Assert.IsType<CreatedResult>(result);
    }

    // ─────────────────────────────────────────────────────────────────────
    // CreateChild — BirthYear validation (#066: year only, never exact DOB)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateChild_WhenBirthYearOutOfRange_ReturnsBadRequest_AndDoesNotCallService()
    {
        // An implausible birth year is a controlled BadRequest without reaching
        // the service layer.
        var (controller, childService, _, ownedDeviceId, _) = CreateController();
        var request = new CreateChildRequest(ownedDeviceId, "Ani", Gender.Girl, BirthYear: 1850);

        var result = await controller.CreateChild(request);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var errorProp = bad.Value!.GetType().GetProperty("error");
        Assert.Contains("BirthYear", errorProp!.GetValue(bad.Value) as string);
        await childService.DidNotReceiveWithAnyArgs()
            .CreateChildAsync(default, default!, default, default);
    }

    [Fact]
    public async Task CreateChild_WhenBirthYearValid_PassesYearToService()
    {
        // Contract guard: a plausible birth year is forwarded verbatim to
        // IChildService (only the year — never an exact date).
        var (controller, childService, _, ownedDeviceId, _) = CreateController();
        childService.CreateChildAsync(ownedDeviceId, "Ani", Gender.Girl, 2020)
            .Returns(MakeChild(ownedDeviceId));
        var request = new CreateChildRequest(ownedDeviceId, "Ani", Gender.Girl, BirthYear: 2020);

        var result = await controller.CreateChild(request);

        Assert.IsType<CreatedResult>(result);
        await childService.Received(1)
            .CreateChildAsync(ownedDeviceId, "Ani", Gender.Girl, 2020);
    }

    // ─────────────────────────────────────────────────────────────────────
    // GetChildren — ownership short-circuit + positive sanity
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetChildren_WhenDeviceNotLinked_ReturnsForbid_AndDoesNotCallService()
    {
        var (controller, childService, _, _, unlinkedDeviceId) = CreateController();

        var result = await controller.GetChildren(unlinkedDeviceId);

        Assert.IsType<ForbidResult>(result);
        await childService.DidNotReceiveWithAnyArgs().GetChildrenByDeviceAsync(default);
    }

    [Fact]
    public async Task GetChildren_WhenLinked_ReturnsOk()
    {
        var (controller, childService, _, ownedDeviceId, _) = CreateController();
        childService.GetChildrenByDeviceAsync(ownedDeviceId).Returns(new List<Child>());

        var result = await controller.GetChildren(ownedDeviceId);

        Assert.IsType<OkObjectResult>(result);
    }
}

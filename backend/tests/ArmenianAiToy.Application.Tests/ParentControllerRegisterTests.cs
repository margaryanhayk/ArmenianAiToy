using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Controller-level tests for the ParentController.Register password-policy
/// guard. The parent dashboard exposes the child's full conversation history,
/// so weak-password acceptance is a parent-trust issue: it is the user-side
/// complement of the server-side Jwt:Key hardening in commit 8d8e120.
/// </summary>
public class ParentControllerRegisterTests
{
    private static (ParentController Controller, IParentService Parents) CreateController()
    {
        var parents = Substitute.For<IParentService>();
        return (new ParentController(parents), parents);
    }

    [Fact]
    public async Task Register_WhenPasswordTooShort_ReturnsBadRequest_AndDoesNotCallService()
    {
        var (controller, parents) = CreateController();
        var request = new ParentRegisterRequest("a@b.com", "short12"); // 7 chars

        var result = await controller.Register(request);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var errorProp = bad.Value!.GetType().GetProperty("error");
        Assert.Contains("at least 8", errorProp!.GetValue(bad.Value) as string);
        await parents.DidNotReceiveWithAnyArgs().RegisterAsync(default!, default!);
    }

    [Fact]
    public async Task Register_WhenPasswordIsValid_DelegatesToService()
    {
        // Anti-tautology guard: proves the new length check does not
        // incorrectly reject passwords that meet the minimum.
        var (controller, parents) = CreateController();
        parents.RegisterAsync("a@b.com", "strongpass9").Returns(Guid.NewGuid());
        var request = new ParentRegisterRequest("a@b.com", "strongpass9"); // 11 chars

        var result = await controller.Register(request);

        Assert.IsType<CreatedResult>(result);
        await parents.Received(1).RegisterAsync("a@b.com", "strongpass9");
    }

    [Fact]
    public async Task Register_WhenPasswordMissing_ReturnsBadRequest()
    {
        // Documents that the pre-existing whitespace/missing guard still
        // fires before the length guard — a future refactor that merges
        // them into a single check must preserve both branches.
        var (controller, parents) = CreateController();
        var request = new ParentRegisterRequest("a@b.com", "   ");

        var result = await controller.Register(request);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var errorProp = bad.Value!.GetType().GetProperty("error");
        Assert.Contains("required", errorProp!.GetValue(bad.Value) as string);
        await parents.DidNotReceiveWithAnyArgs().RegisterAsync(default!, default!);
    }
}
